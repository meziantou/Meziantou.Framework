using System.CommandLine;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Meziantou.Framework.Templating.Tool.Tests")]

namespace Meziantou.Framework.Templating.Tool;

internal static partial class Program
{
    private const string DefaultStartCodeBlockDelimiter = "<%";
    private const string DefaultEndCodeBlockDelimiter = "%>";
    private const string T4StartCodeBlockDelimiter = "<#";
    private const string T4EndCodeBlockDelimiter = "#>";
    private const string LineEndingLf = "LF";
    private const string LineEndingCrlf = "CRLF";
    private const string LineEndingCr = "CR";

    public static Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        var inputOption = new Option<string[]>("--input")
        {
            Description = "Path to an input template file, or a glob pattern (*, ?, **) that matches template files. Can be repeated",
            Required = true,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to the output file of a single template. If omitted, writes next to the template when its name is known, or to stdout",
            Required = false,
        };
        var outputDirectoryOption = new Option<string?>("--output-directory")
        {
            Description = "Directory of the generated files. A template matched by a glob pattern keeps its path relative to the start of the pattern",
            Required = false,
        };
        var outputExtensionOption = new Option<string?>("--output-extension")
        {
            Description = "Extension of the generated file of a template that has no output directive",
            Required = false,
        };
        var rootDirectoryOption = new Option<string?>("--root-directory")
        {
            Description = "Directory the input paths and glob patterns are relative to. Defaults to the current directory",
            Required = false,
        };
        var argOption = new Option<string[]>("--arg")
        {
            Description = "Argument passed to the templates, as name=value. The value is a string. Can be repeated",
            Required = false,
            Arity = ArgumentArity.ZeroOrMore,
        };
        var argumentsFileOption = new Option<string?>("--arguments-file")
        {
            Description = "Path to a JSON file whose root object holds the arguments passed to the templates",
            Required = false,
        };
        var startCodeBlockDelimiterOption = new Option<string?>("--start-code-block-delimiter")
        {
            Description = "Delimiter that marks the start of a code block",
            Required = false,
        };
        var endCodeBlockDelimiterOption = new Option<string?>("--end-code-block-delimiter")
        {
            Description = "Delimiter that marks the end of a code block",
            Required = false,
        };
        var outputEncodingOption = new Option<string?>("--output-encoding")
        {
            Description = "Output encoding for generated files. Example: utf8, utf8-bom, unicode, utf-16, utf-32",
            Required = false,
        };
        var lineEndingOption = new Option<string?>("--line-ending")
        {
            Description = "Line ending to use in output. Values: LF, CRLF, CR",
            Required = false,
        };

        var rootCommand = new RootCommand("Render template files using Meziantou.Framework.Templating");
        rootCommand.Options.Add(inputOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(outputDirectoryOption);
        rootCommand.Options.Add(outputExtensionOption);
        rootCommand.Options.Add(rootDirectoryOption);
        rootCommand.Options.Add(argOption);
        rootCommand.Options.Add(argumentsFileOption);
        rootCommand.Options.Add(startCodeBlockDelimiterOption);
        rootCommand.Options.Add(endCodeBlockDelimiterOption);
        rootCommand.Options.Add(outputEncodingOption);
        rootCommand.Options.Add(lineEndingOption);
        rootCommand.SetAction((parseResult, cancellationToken) =>
        {
            var options = new RenderOptions
            {
                Inputs = parseResult.GetRequiredValue(inputOption),
                OutputFile = parseResult.GetValue(outputOption),
                OutputDirectory = parseResult.GetValue(outputDirectoryOption),
                OutputExtension = parseResult.GetValue(outputExtensionOption),
                RootDirectory = parseResult.GetValue(rootDirectoryOption),
                Arguments = parseResult.GetValue(argOption) ?? [],
                ArgumentsFile = parseResult.GetValue(argumentsFileOption),
                StartCodeBlockDelimiter = parseResult.GetValue(startCodeBlockDelimiterOption),
                EndCodeBlockDelimiter = parseResult.GetValue(endCodeBlockDelimiterOption),
                OutputEncoding = parseResult.GetValue(outputEncodingOption),
                LineEnding = parseResult.GetValue(lineEndingOption),
            };

            return RenderTemplatesAsync(options, parseResult.InvocationConfiguration.Output, parseResult.InvocationConfiguration.Error, cancellationToken);
        });

        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static async Task<int> RenderTemplatesAsync(RenderOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var settings = ResolveSettings(options, errors);
        var arguments = errors.Count is 0 ? await ReadArgumentsAsync(options, errors, cancellationToken).ConfigureAwait(false) : null;
        var inputs = errors.Count is 0 ? ResolveInputFiles(options, errors) : null;
        if (errors.Count > 0 || settings is null || arguments is null || inputs is null)
            return await WriteErrorsAsync(error, errors, cancellationToken).ConfigureAwait(false);

        if (inputs.IsMultiple && settings.OutputFile is not null)
        {
            errors.Add("--output names the file of a single template. Use --output-directory to render several templates");
            return await WriteErrorsAsync(error, errors, cancellationToken).ConfigureAwait(false);
        }

        // Load every template and name its output before rendering any of them, so that an invalid
        // input does not leave a partial set of generated files.
        var templates = new List<LoadedTemplate>(inputs.Files.Count);
        try
        {
            var outputPaths = new Dictionary<FullPath, FullPath>();
            foreach (var input in inputs.Files)
            {
                var loadedTemplate = await LoadTemplateAsync(input, settings, arguments, inputs.IsMultiple, errors, cancellationToken).ConfigureAwait(false);
                if (loadedTemplate is null)
                    continue;

                templates.Add(loadedTemplate);
                if (loadedTemplate.OutputPath is { } outputPath)
                {
                    if (outputPaths.TryGetValue(outputPath, out var otherInputPath))
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"The templates '{otherInputPath}' and '{input.Path}' both generate '{outputPath}'"));
                    }
                    else
                    {
                        outputPaths.Add(outputPath, input.Path);
                    }
                }
            }

            if (errors.Count > 0)
                return await WriteErrorsAsync(error, errors, cancellationToken).ConfigureAwait(false);

            var results = new string?[templates.Count];
            var renderErrors = new string?[templates.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, templates.Count), cancellationToken, async (index, itemCancellationToken) =>
            {
                var template = templates[index];
                if (!TryRunTemplate(template, arguments, out var result, out var errorMessage))
                {
                    renderErrors[index] = errorMessage;
                    return;
                }

                if (settings.LineEnding is not null)
                {
                    result = result.ReplaceLineEndings(settings.LineEnding);
                }

                if (template.OutputPath is { } outputPath)
                {
                    outputPath.CreateParentDirectory();
                    await File.WriteAllTextAsync(outputPath, result, settings.OutputEncoding, itemCancellationToken).ConfigureAwait(false);
                }
                else
                {
                    results[index] = result;
                }
            }).ConfigureAwait(false);

            // Only a single template can be written to stdout
            foreach (var result in results)
            {
                if (result is not null)
                {
                    await output.WriteAsync(result.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            errors.AddRange(renderErrors.OfType<string>());
            return await WriteErrorsAsync(error, errors, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var template in templates)
            {
                template.Template.Dispose();
            }
        }
    }

    private static async Task<int> WriteErrorsAsync(TextWriter error, List<string> errors, CancellationToken cancellationToken)
    {
        foreach (var message in errors)
        {
            await error.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        return errors.Count is 0 ? 0 : 1;
    }

    private static RenderSettings? ResolveSettings(RenderOptions options, List<string> errors)
    {
        if (options.StartCodeBlockDelimiter is { Length: 0 })
        {
            errors.Add("The start code block delimiter cannot be empty");
        }

        if (options.EndCodeBlockDelimiter is { Length: 0 })
        {
            errors.Add("The end code block delimiter cannot be empty");
        }

        if (!TryResolveLineEnding(options.LineEnding, out var lineEnding))
        {
            errors.Add("Invalid line ending. Allowed values are LF, CRLF, CR");
        }

        if (!TryResolveOutputEncoding(options.OutputEncoding, out var outputEncoding))
        {
            errors.Add("Invalid output encoding. Use a valid encoding name, for example utf8, utf8-bom, unicode, utf-16, utf-32");
        }

        string? outputExtension = null;
        if (options.OutputExtension is not null)
        {
            outputExtension = NormalizeExtension(options.OutputExtension);
            if (outputExtension is null)
            {
                errors.Add("The output extension cannot be empty");
            }
        }

        var outputFile = string.IsNullOrWhiteSpace(options.OutputFile) ? (FullPath?)null : FullPath.FromPath(options.OutputFile);
        var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory) ? (FullPath?)null : FullPath.FromPath(options.OutputDirectory);
        if (outputFile is not null && outputDirectory is not null)
        {
            errors.Add("--output and --output-directory cannot be used together");
        }

        if (errors.Count > 0)
            return null;

        return new RenderSettings
        {
            StartCodeBlockDelimiter = options.StartCodeBlockDelimiter,
            EndCodeBlockDelimiter = options.EndCodeBlockDelimiter,
            LineEnding = lineEnding,
            OutputEncoding = outputEncoding,
            OutputExtension = outputExtension,
            OutputFile = outputFile,
            OutputDirectory = outputDirectory,
        };
    }

    private static async Task<LoadedTemplate?> LoadTemplateAsync(InputFile input, RenderSettings settings, IReadOnlyList<TemplateArgumentValue> arguments, bool isMultiple, List<string> errors, CancellationToken cancellationToken)
    {
        var templateContent = await File.ReadAllTextAsync(input.Path, cancellationToken).ConfigureAwait(false);
        if (!TryResolveCodeBlockDelimiters(templateContent, settings.StartCodeBlockDelimiter, settings.EndCodeBlockDelimiter, out var startCodeBlockDelimiter, out var endCodeBlockDelimiter))
        {
            errors.Add("The start and end code block delimiters must be specified together, unless the one you specify belongs to a well-known pair (<% %> or <# #>)");
            return null;
        }

        Template? template = new Template
        {
            StartCodeBlockDelimiter = startCodeBlockDelimiter,
            EndCodeBlockDelimiter = endCodeBlockDelimiter,
            SourceFileName = input.Path,
        };

        try
        {
            foreach (var argument in arguments)
            {
                if (string.Equals(argument.Name, template.OutputParameterName, StringComparison.Ordinal))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"The argument name '{argument.Name}' is reserved"));
                    return null;
                }

                template.Arguments.Add(new TemplateArgument(argument.Name, argument.Type));
            }

            if (arguments.Any(argument => argument.Type == typeof(JsonElement)))
            {
                template.AssemblyReferences.Add(typeof(JsonElement));
            }

            template.Load(templateContent);
            if (!TryResolveOutputPath(input, template, settings, isMultiple, out var outputPath, out var errorMessage))
            {
                errors.Add(errorMessage);
                return null;
            }

            var loadedTemplate = new LoadedTemplate(input.Path, template, outputPath);
            template = null;
            return loadedTemplate;
        }
        finally
        {
            template?.Dispose();
        }
    }

    private static bool TryRunTemplate(LoadedTemplate template, IReadOnlyList<TemplateArgumentValue> arguments, [NotNullWhen(true)] out string? result, [NotNullWhen(false)] out string? errorMessage)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            values[argument.Name] = argument.Value;
        }

        try
        {
            result = template.Template.Run((IReadOnlyDictionary<string, object?>)values);
            errorMessage = null;
            return true;
        }
        catch (TemplateException ex)
        {
            result = null;
            errorMessage = ex.Message;
            return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // The template threw while running. Report it against the template file instead of
            // letting a reflection stack trace escape as if the tool itself had crashed.
            result = null;
            errorMessage = FormatTemplateRuntimeError(ex.InnerException, template.InputPath);
            return false;
        }
    }

    /// <summary>
    /// Formats an exception thrown by the template itself as a diagnostic pointing at the template
    /// file, using the location the <c>#line</c> directives map the failing frame back to.
    /// </summary>
    private static string FormatTemplateRuntimeError(Exception exception, FullPath inputPath)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: true).GetFrames())
        {
            var fileName = frame.GetFileName();
            if (string.IsNullOrEmpty(fileName) || FullPath.FromPath(fileName) != inputPath)
                continue;

            var line = frame.GetFileLineNumber();
            if (line <= 0)
                break;

            var column = frame.GetFileColumnNumber();
            return column > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{inputPath}({line},{column}): error: {exception.Message}")
                : string.Create(CultureInfo.InvariantCulture, $"{inputPath}({line}): error: {exception.Message}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{inputPath}: error: {exception.Message}");
    }

    private static bool TryResolveLineEnding(string? value, out string? lineEnding)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            lineEnding = null;
            return true;
        }

        lineEnding = value.Trim().ToUpperInvariant() switch
        {
            LineEndingLf => "\n",
            LineEndingCrlf => "\r\n",
            LineEndingCr => "\r",
            _ => null,
        };

        return lineEnding is not null;
    }

    private static bool TryResolveOutputEncoding(string? value, out Encoding encoding)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return true;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "utf8", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return true;
        }

        if (string.Equals(normalized, "utf8-bom", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "utf-8-bom", StringComparison.OrdinalIgnoreCase))
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            return true;
        }

        try
        {
            encoding = Encoding.GetEncoding(normalized);
            return true;
        }
        catch (ArgumentException)
        {
            encoding = null!;
            return false;
        }
    }

    /// <summary>
    /// Names the generated file of a template: <c>--output</c>, or the name of the template with the extension of its
    /// output directive (or <c>--output-extension</c>), in <c>--output-directory</c> or next to the template.
    /// <see langword="null"/> means stdout, which only a single template can use.
    /// </summary>
    private static bool TryResolveOutputPath(InputFile input, Template template, RenderSettings settings, bool isMultiple, out FullPath? outputPath, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;
        if (settings.OutputFile is { } outputFile)
        {
            outputPath = outputFile;
            return true;
        }

        var extension = GetDirectiveOutputExtension(template) ?? settings.OutputExtension;
        if (extension is null)
        {
            outputPath = null;
            if (!isMultiple && settings.OutputDirectory is null)
                return true;

            errorMessage = string.Create(CultureInfo.InvariantCulture, $"{input.Path}: error: The template has no output directive. Add one, or use --output-extension to name its output file");
            return false;
        }

        var path = settings.OutputDirectory is { } outputDirectory
            ? outputDirectory / input.RelativePath
            : input.Path;
        outputPath = path.WithExtension(extension);
        if (outputPath == input.Path)
        {
            errorMessage = string.Create(CultureInfo.InvariantCulture, $"{input.Path}: error: The output file would overwrite the template");
            return false;
        }

        return true;
    }

    private static bool TryResolveCodeBlockDelimiters(
        string templateContent,
        string? startCodeBlockDelimiter,
        string? endCodeBlockDelimiter,
        [NotNullWhen(true)] out string? resolvedStartCodeBlockDelimiter,
        [NotNullWhen(true)] out string? resolvedEndCodeBlockDelimiter)
    {
        if (startCodeBlockDelimiter is null && endCodeBlockDelimiter is null)
        {
            (resolvedStartCodeBlockDelimiter, resolvedEndCodeBlockDelimiter) = ContainsT4CodeBlock(templateContent)
                ? (T4StartCodeBlockDelimiter, T4EndCodeBlockDelimiter)
                : (DefaultStartCodeBlockDelimiter, DefaultEndCodeBlockDelimiter);
            return true;
        }

        resolvedStartCodeBlockDelimiter = startCodeBlockDelimiter ?? GetPairedStartCodeBlockDelimiter(endCodeBlockDelimiter);
        resolvedEndCodeBlockDelimiter = endCodeBlockDelimiter ?? GetPairedEndCodeBlockDelimiter(startCodeBlockDelimiter);
        return resolvedStartCodeBlockDelimiter is not null && resolvedEndCodeBlockDelimiter is not null;
    }

    /// <summary>
    /// Determines whether the template uses T4 delimiters: it must contain at least one complete
    /// <c>&lt;# … #&gt;</c> block and no <c>&lt;%</c> at all, so that a stray <c>&lt;#</c> in the
    /// template text cannot silently switch the delimiters.
    /// </summary>
    private static bool ContainsT4CodeBlock(string templateContent)
    {
        if (templateContent.Contains(DefaultStartCodeBlockDelimiter, StringComparison.Ordinal))
            return false;

        var startIndex = templateContent.IndexOf(T4StartCodeBlockDelimiter, StringComparison.Ordinal);
        if (startIndex < 0)
            return false;

        return templateContent.IndexOf(T4EndCodeBlockDelimiter, startIndex + T4StartCodeBlockDelimiter.Length, StringComparison.Ordinal) >= 0;
    }

    private static string? GetPairedStartCodeBlockDelimiter(string? endCodeBlockDelimiter)
    {
        return endCodeBlockDelimiter switch
        {
            T4EndCodeBlockDelimiter => T4StartCodeBlockDelimiter,
            DefaultEndCodeBlockDelimiter => DefaultStartCodeBlockDelimiter,
            _ => null,
        };
    }

    private static string? GetPairedEndCodeBlockDelimiter(string? startCodeBlockDelimiter)
    {
        return startCodeBlockDelimiter switch
        {
            T4StartCodeBlockDelimiter => T4EndCodeBlockDelimiter,
            DefaultStartCodeBlockDelimiter => DefaultEndCodeBlockDelimiter,
            _ => null,
        };
    }

    private static string? GetDirectiveOutputExtension(Template template)
    {
        foreach (var directive in template.Blocks.OfType<DirectiveBlock>())
        {
            if (string.Equals(directive.Name, "outputextension", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeExtension(directive.Value);
            }

            if (string.Equals(directive.Name, "output", StringComparison.OrdinalIgnoreCase))
            {
                var extension = TryGetOutputExtensionFromOutputDirective(directive.Value);
                if (extension is not null)
                {
                    return extension;
                }
            }
        }

        return null;
    }

    private static string? TryGetOutputExtensionFromOutputDirective(string value)
    {
        var searchStartIndex = 0;
        while (searchStartIndex < value.Length)
        {
            var extensionIndex = value.IndexOf("extension", searchStartIndex, StringComparison.OrdinalIgnoreCase);
            if (extensionIndex < 0)
            {
                return null;
            }

            if (extensionIndex > 0 && !char.IsWhiteSpace(value[extensionIndex - 1]))
            {
                searchStartIndex = extensionIndex + 1;
                continue;
            }

            var valueStartIndex = extensionIndex + "extension".Length;
            while (valueStartIndex < value.Length && char.IsWhiteSpace(value[valueStartIndex]))
            {
                valueStartIndex++;
            }

            if (valueStartIndex >= value.Length || value[valueStartIndex] != '=')
            {
                searchStartIndex = extensionIndex + 1;
                continue;
            }

            valueStartIndex++;
            while (valueStartIndex < value.Length && char.IsWhiteSpace(value[valueStartIndex]))
            {
                valueStartIndex++;
            }

            if (valueStartIndex >= value.Length)
            {
                return null;
            }

            var quoteChar = value[valueStartIndex];
            if (quoteChar is '"' or '\'')
            {
                valueStartIndex++;
                var quotedValueLength = value[valueStartIndex..].AsSpan().IndexOf(quoteChar);
                if (quotedValueLength < 0)
                {
                    return null;
                }

                var valueEndIndex = valueStartIndex + quotedValueLength;
                return NormalizeExtension(value[valueStartIndex..valueEndIndex]);
            }

            var valueLength = value[valueStartIndex..].AsSpan().IndexOfAny([' ', '\t', '\r', '\n']);
            return valueLength < 0
                ? NormalizeExtension(value[valueStartIndex..])
                : NormalizeExtension(value.Substring(valueStartIndex, valueLength));
        }

        return null;
    }

    private static string? NormalizeExtension(string value)
    {
        var extension = value.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        if (!extension.StartsWith('.', StringComparison.Ordinal))
        {
            extension = "." + extension;
        }

        return extension;
    }
}
