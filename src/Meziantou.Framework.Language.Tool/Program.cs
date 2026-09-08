using System.Buffers;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meziantou.Framework.Language.Json;
using Meziantou.Framework.Language.Regex;
using Meziantou.Framework.Language.Shell;
using Meziantou.Framework.Language.Xml;

[assembly: InternalsVisibleTo("Meziantou.Framework.Language.Tool.Tests")]

namespace Meziantou.Framework.LanguageTool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        return MainImpl(args, configure, input: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure, TextReader? input)
    {
        var rootCommand = new RootCommand("Inspect JSON, XML, regular expression, and shell documents");
        AddSyntaxCommand(rootCommand, input);

        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static void AddSyntaxCommand(RootCommand rootCommand, TextReader? input)
    {
        var inputOption = new Option<string?>("--input")
        {
            Description = "Path to the file to parse. If omitted, reads from stdin",
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to the JSON file to write. If omitted, writes to stdout",
        };
        var languageOption = new Option<SyntaxLanguage?>("--language")
        {
            Description = $"Language to parse the input as. If omitted, it is detected from the extension of --input, which is why it is required when reading from stdin. One of: {SyntaxLanguage.SupportedValues}",
            CustomParser = ParseLanguage,
        };
        var noTokensOption = new Option<bool>("--no-tokens")
        {
            Description = "Omit the tokens of each node, and the trivia they carry",
        };
        var noTriviaOption = new Option<bool>("--no-trivia")
        {
            Description = "Omit the leading and trailing trivia of each token",
        };
        var noTextOption = new Option<bool>("--no-text")
        {
            Description = "Omit the source text of each node, token, and trivia",
        };
        var noSpansOption = new Option<bool>("--no-spans")
        {
            Description = "Omit the source spans of each node, token, and trivia",
        };

        var syntaxCommand = new Command("syntax")
        {
            Description = "Dump the syntax tree of a JSON, XML, regular expression, or shell document as JSON",
        };
        syntaxCommand.Options.Add(inputOption);
        syntaxCommand.Options.Add(outputOption);
        syntaxCommand.Options.Add(languageOption);
        syntaxCommand.Options.Add(noTokensOption);
        syntaxCommand.Options.Add(noTriviaOption);
        syntaxCommand.Options.Add(noTextOption);
        syntaxCommand.Options.Add(noSpansOption);
        syntaxCommand.SetAction((parseResult, cancellationToken) =>
        {
            var noTokens = parseResult.GetValue(noTokensOption);
            var options = new DumpOptions
            {
                IncludeTokens = !noTokens,
                IncludeTrivia = !noTokens && !parseResult.GetValue(noTriviaOption),
                IncludeText = !parseResult.GetValue(noTextOption),
                IncludeSpans = !parseResult.GetValue(noSpansOption),
            };

            return DumpAsync(
                parseResult.GetValue(inputOption),
                parseResult.GetValue(outputOption),
                parseResult.GetValue(languageOption),
                options,
                input,
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                cancellationToken);
        });

        rootCommand.Subcommands.Add(syntaxCommand);
    }

    private static async Task<int> DumpAsync(
        string? inputFile,
        string? outputFile,
        SyntaxLanguage? language,
        DumpOptions options,
        TextReader? input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string text;
        if (string.IsNullOrWhiteSpace(inputFile))
        {
            if (language is null)
            {
                await error.WriteLineAsync("--language is required when reading from the standard input".AsMemory(), cancellationToken);
                return 1;
            }

            text = await (input ?? Console.In).ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var inputPath = FullPath.FromPath(inputFile);
            if (!File.Exists(inputPath))
            {
                await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The input file '{inputPath}' does not exist").AsMemory(), cancellationToken);
                return 1;
            }

            if (language is null && !SyntaxLanguage.TryDetectFromExtension(inputPath, out language))
            {
                await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Cannot detect the language of '{inputPath}' from its extension. Set --language to one of: {SyntaxLanguage.SupportedValues}").AsMemory(), cancellationToken);
                return 1;
            }

            text = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(outputFile))
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, SyntaxTreeJsonWriter.CreateWriterOptions()))
            {
                WriteTree(writer, text, language, options);
            }

            await output.WriteLineAsync(Encoding.UTF8.GetString(buffer.WrittenSpan).AsMemory(), cancellationToken);
            return 0;
        }

        var outputPath = FullPath.FromPath(outputFile);
        outputPath.CreateParentDirectory();
        var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new Utf8JsonWriter(stream, SyntaxTreeJsonWriter.CreateWriterOptions());
            await using (writer.ConfigureAwait(false))
            {
                WriteTree(writer, text, language, options);
            }

            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private static void WriteTree(Utf8JsonWriter writer, string text, SyntaxLanguage language, DumpOptions options)
    {
        switch (language.Family)
        {
            case SyntaxLanguageFamily.Json:
                JsonSyntaxTreeWriter.Write(writer, JsonSyntaxTree.ParseText(text), options);
                break;

            case SyntaxLanguageFamily.Xml:
                XmlSyntaxTreeWriter.Write(writer, XmlSyntaxTree.ParseText(text), options);
                break;

            case SyntaxLanguageFamily.Regex:
                RegexSyntaxTreeWriter.Write(writer, RegexSyntaxTree.ParseText(text, language.RegexDialect!), options);
                break;

            case SyntaxLanguageFamily.Shell:
                ShellSyntaxTreeWriter.Write(writer, ShellSyntaxTree.ParseText(text, language.ShellDialect!), options);
                break;

            default:
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Unsupported language family '{language.Family}'"));
        }
    }

    private static SyntaxLanguage? ParseLanguage(ArgumentResult result)
    {
        if (result.Tokens.Count is not 1 || !SyntaxLanguage.TryParse(result.Tokens[0].Value, out var language))
        {
            result.AddError(string.Create(CultureInfo.InvariantCulture, $"The language must be one of: {SyntaxLanguage.SupportedValues}"));
            return null;
        }

        return language;
    }
}
