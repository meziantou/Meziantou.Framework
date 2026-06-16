using System.CommandLine;
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
        var inputOption = new Option<string>("--input")
        {
            Description = "Path to the input template file",
            Required = true,
        };
        var outputOption = new Option<string>("--output")
        {
            Description = "Path to the output file. If omitted, writes to stdout",
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

        var rootCommand = new RootCommand("Render a template file using Meziantou.Framework.Templating");
        rootCommand.Options.Add(inputOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(startCodeBlockDelimiterOption);
        rootCommand.Options.Add(endCodeBlockDelimiterOption);
        rootCommand.Options.Add(outputEncodingOption);
        rootCommand.Options.Add(lineEndingOption);
        rootCommand.SetAction((parseResult, cancellationToken) =>
        {
            return RenderTemplateAsync(
                parseResult.GetRequiredValue(inputOption),
                parseResult.GetValue(outputOption),
                parseResult.GetValue(startCodeBlockDelimiterOption),
                parseResult.GetValue(endCodeBlockDelimiterOption),
                parseResult.GetValue(outputEncodingOption),
                parseResult.GetValue(lineEndingOption),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                cancellationToken);
        });

        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static async Task<int> RenderTemplateAsync(
        string inputFile,
        string? outputFile,
        string? startCodeBlockDelimiter,
        string? endCodeBlockDelimiter,
        string? outputEncoding,
        string? lineEnding,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var inputPath = FullPath.FromPath(inputFile);
        if (!File.Exists(inputPath))
        {
            await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The input template file '{inputPath}' does not exist").AsMemory(), cancellationToken);
            return 1;
        }

        var templateContent = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var (resolvedStartCodeBlockDelimiter, resolvedEndCodeBlockDelimiter) = ResolveCodeBlockDelimiters(templateContent, startCodeBlockDelimiter, endCodeBlockDelimiter);
        if (string.IsNullOrEmpty(resolvedStartCodeBlockDelimiter))
        {
            await error.WriteLineAsync("The start code block delimiter cannot be empty".AsMemory(), cancellationToken);
            return 1;
        }

        if (string.IsNullOrEmpty(resolvedEndCodeBlockDelimiter))
        {
            await error.WriteLineAsync("The end code block delimiter cannot be empty".AsMemory(), cancellationToken);
            return 1;
        }

        if (!TryResolveLineEnding(lineEnding, out var resolvedLineEnding))
        {
            await error.WriteLineAsync("Invalid line ending. Allowed values are LF, CRLF, CR".AsMemory(), cancellationToken);
            return 1;
        }

        if (!TryResolveOutputEncoding(outputEncoding, out var resolvedOutputEncoding))
        {
            await error.WriteLineAsync("Invalid output encoding. Use a valid encoding name, for example utf8, utf8-bom, unicode, utf-16, utf-32".AsMemory(), cancellationToken);
            return 1;
        }

        var template = new Template
        {
            StartCodeBlockDelimiter = resolvedStartCodeBlockDelimiter,
            EndCodeBlockDelimiter = resolvedEndCodeBlockDelimiter,
            SourceFileName = inputPath,
        };
        template.Load(templateContent);
        var resolvedOutputPath = ResolveOutputPath(outputFile, inputPath, template);

        string result;
        try
        {
            result = template.Run();
        }
        catch (TemplateException ex)
        {
            await error.WriteLineAsync(ex.Message.AsMemory(), cancellationToken);
            return 1;
        }

        if (resolvedLineEnding is not null)
        {
            result = result.ReplaceLineEndings(resolvedLineEnding);
        }

        if (resolvedOutputPath is null)
        {
            await output.WriteAsync(result.AsMemory(), cancellationToken);
            return 0;
        }

        var outputPath = resolvedOutputPath.Value;
        outputPath.CreateParentDirectory();
        await File.WriteAllTextAsync(outputPath, result, resolvedOutputEncoding, cancellationToken).ConfigureAwait(false);
        return 0;
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

    private static FullPath? ResolveOutputPath(string? outputFile, FullPath inputPath, Template template)
    {
        if (!string.IsNullOrWhiteSpace(outputFile))
        {
            return FullPath.FromPath(outputFile);
        }

        var directiveOutputExtension = GetDirectiveOutputExtension(template);
        if (directiveOutputExtension is null)
        {
            return null;
        }

        return inputPath.WithExtension(directiveOutputExtension);
    }

    private static (string StartCodeBlockDelimiter, string EndCodeBlockDelimiter) ResolveCodeBlockDelimiters(string templateContent, string? startCodeBlockDelimiter, string? endCodeBlockDelimiter)
    {
        if (startCodeBlockDelimiter is null && endCodeBlockDelimiter is null)
        {
            if (templateContent.Contains(T4StartCodeBlockDelimiter, StringComparison.Ordinal))
            {
                return (T4StartCodeBlockDelimiter, T4EndCodeBlockDelimiter);
            }

            return (DefaultStartCodeBlockDelimiter, DefaultEndCodeBlockDelimiter);
        }

        return (
            startCodeBlockDelimiter ?? GetDefaultStartCodeBlockDelimiter(endCodeBlockDelimiter),
            endCodeBlockDelimiter ?? GetDefaultEndCodeBlockDelimiter(startCodeBlockDelimiter));
    }

    private static string GetDefaultStartCodeBlockDelimiter(string? endCodeBlockDelimiter)
    {
        return endCodeBlockDelimiter switch
        {
            T4EndCodeBlockDelimiter => T4StartCodeBlockDelimiter,
            DefaultEndCodeBlockDelimiter => DefaultStartCodeBlockDelimiter,
            _ => DefaultStartCodeBlockDelimiter,
        };
    }

    private static string GetDefaultEndCodeBlockDelimiter(string? startCodeBlockDelimiter)
    {
        return startCodeBlockDelimiter switch
        {
            T4StartCodeBlockDelimiter => T4EndCodeBlockDelimiter,
            DefaultStartCodeBlockDelimiter => DefaultEndCodeBlockDelimiter,
            _ => DefaultEndCodeBlockDelimiter,
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
