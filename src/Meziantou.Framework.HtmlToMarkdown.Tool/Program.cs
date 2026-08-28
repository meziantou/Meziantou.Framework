using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using AngleSharp.Html.Parser;

[assembly: InternalsVisibleTo("Meziantou.Framework.HtmlToMarkdown.Tool.Tests")]

namespace Meziantou.Framework.HtmlToMarkdownTool;

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
        var defaultOptions = new HtmlToMarkdownOptions();

        var inputOption = new Option<string?>("--input")
        {
            Description = "Path to the HTML file to convert. If omitted, reads from stdin",
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to the Markdown file to write. If omitted, writes to stdout",
        };
        var emphasisMarkerOption = new Option<EmphasisMarker>("--emphasis-marker")
        {
            Description = "Marker used for emphasis",
            DefaultValueFactory = _ => defaultOptions.EmphasisMarker,
        };
        var headingStyleOption = new Option<HeadingStyle>("--heading-style")
        {
            Description = "Style used for headings",
            DefaultValueFactory = _ => defaultOptions.HeadingStyle,
        };
        var codeBlockStyleOption = new Option<CodeBlockStyle>("--code-block-style")
        {
            Description = "Style used for code blocks",
            DefaultValueFactory = _ => defaultOptions.CodeBlockStyle,
        };
        var codeBlockFenceCharacterOption = new Option<char>("--code-block-fence-character")
        {
            Description = "Fence character used for fenced code blocks",
            DefaultValueFactory = _ => defaultOptions.CodeBlockFenceCharacter,
            CustomParser = ParseCharacter,
        };
        var unorderedListMarkerOption = new Option<char>("--unordered-list-marker")
        {
            Description = "Marker used for unordered lists",
            DefaultValueFactory = _ => defaultOptions.UnorderedListMarker,
            CustomParser = ParseCharacter,
        };
        var thematicBreakOption = new Option<string>("--thematic-break")
        {
            Description = "Text used for horizontal rules",
            DefaultValueFactory = _ => defaultOptions.ThematicBreak,
        };
        var lineBreakStyleOption = new Option<LineBreakStyle>("--line-break-style")
        {
            Description = "Style used for line breaks",
            DefaultValueFactory = _ => defaultOptions.LineBreakStyle,
        };
        var simplePunctuationOption = new Option<bool>("--simple-punctuation")
        {
            Description = "Convert smart punctuation characters to simple ASCII punctuation",
            DefaultValueFactory = _ => defaultOptions.UseSimplePunctuation,
        };
        var emojiShortcodeModeOption = new Option<EmojiShortcodeMode>("--emoji-shortcode-mode")
        {
            Description = "Mode used to convert emoji to shortcodes",
            DefaultValueFactory = _ => defaultOptions.EmojiShortcodeMode,
        };
        var unknownElementHandlingOption = new Option<UnknownElementHandling>("--unknown-element-handling")
        {
            Description = "Handling of unknown HTML elements",
            DefaultValueFactory = _ => defaultOptions.UnknownElementHandling,
        };

        var rootCommand = new RootCommand("Convert HTML to Markdown using Meziantou.Framework.HtmlToMarkdown");
        rootCommand.Options.Add(inputOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(emphasisMarkerOption);
        rootCommand.Options.Add(headingStyleOption);
        rootCommand.Options.Add(codeBlockStyleOption);
        rootCommand.Options.Add(codeBlockFenceCharacterOption);
        rootCommand.Options.Add(unorderedListMarkerOption);
        rootCommand.Options.Add(thematicBreakOption);
        rootCommand.Options.Add(lineBreakStyleOption);
        rootCommand.Options.Add(simplePunctuationOption);
        rootCommand.Options.Add(emojiShortcodeModeOption);
        rootCommand.Options.Add(unknownElementHandlingOption);
        rootCommand.SetAction((parseResult, cancellationToken) =>
        {
            var options = new HtmlToMarkdownOptions
            {
                EmphasisMarker = parseResult.GetValue(emphasisMarkerOption),
                HeadingStyle = parseResult.GetValue(headingStyleOption),
                CodeBlockStyle = parseResult.GetValue(codeBlockStyleOption),
                CodeBlockFenceCharacter = parseResult.GetValue(codeBlockFenceCharacterOption),
                UnorderedListMarker = parseResult.GetValue(unorderedListMarkerOption),
                ThematicBreak = parseResult.GetRequiredValue(thematicBreakOption),
                LineBreakStyle = parseResult.GetValue(lineBreakStyleOption),
                UseSimplePunctuation = parseResult.GetValue(simplePunctuationOption),
                EmojiShortcodeMode = parseResult.GetValue(emojiShortcodeModeOption),
                UnknownElementHandling = parseResult.GetValue(unknownElementHandlingOption),
            };

            return ConvertAsync(
                parseResult.GetValue(inputOption),
                parseResult.GetValue(outputOption),
                options,
                input,
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                cancellationToken);
        });

        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static async Task<int> ConvertAsync(
        string? inputFile,
        string? outputFile,
        HtmlToMarkdownOptions options,
        TextReader? input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string html;
        if (string.IsNullOrWhiteSpace(inputFile))
        {
            html = await (input ?? Console.In).ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var inputPath = FullPath.FromPath(inputFile);
            if (!File.Exists(inputPath))
            {
                await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The input file '{inputPath}' does not exist").AsMemory(), cancellationToken);
                return 1;
            }

            html = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        }

        var markdown = HtmlToMarkdown.Convert(GetBodyContent(html), options);
        if (markdown.Length > 0)
        {
            markdown += "\n";
        }

        if (string.IsNullOrWhiteSpace(outputFile))
        {
            await output.WriteAsync(markdown.AsMemory(), cancellationToken);
            return 0;
        }

        var outputPath = FullPath.FromPath(outputFile);
        outputPath.CreateParentDirectory();
        await File.WriteAllTextAsync(outputPath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    // The converter handles HTML fragments, so extract the body content to ignore the head of complete documents
    private static string GetBodyContent(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        return document.Body?.InnerHtml ?? html;
    }

    private static char ParseCharacter(ArgumentResult result)
    {
        if (result.Tokens.Count is not 1 || result.Tokens[0].Value.Length is not 1)
        {
            result.AddError("The value must be a single character");
            return default;
        }

        return result.Tokens[0].Value[0];
    }
}
