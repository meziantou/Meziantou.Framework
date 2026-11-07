using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using Meziantou.Framework.AtlassianDataFormat;

[assembly: InternalsVisibleTo("Meziantou.Framework.AtlassianDataFormat.Tool.Tests")]

namespace Meziantou.Framework.AtlassianDataFormatTool;

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
        var defaultOptions = new AdfToMarkdownOptions();

        var inputOption = new Option<string?>("--input")
        {
            Description = "Path to the ADF JSON file to convert. If omitted, reads from stdin",
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to the Markdown file to write. If omitted, writes to stdout",
        };
        var headingStyleOption = new Option<AdfHeadingStyle>("--heading-style")
        {
            Description = "Style used for headings",
            DefaultValueFactory = _ => defaultOptions.HeadingStyle,
        };
        var emphasisMarkerOption = new Option<AdfEmphasisMarker>("--emphasis-marker")
        {
            Description = "Marker used for emphasis",
            DefaultValueFactory = _ => defaultOptions.EmphasisMarker,
        };
        var codeBlockStyleOption = new Option<AdfCodeBlockStyle>("--code-block-style")
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
        var lineBreakStyleOption = new Option<AdfLineBreakStyle>("--line-break-style")
        {
            Description = "Style used for line breaks",
            DefaultValueFactory = _ => defaultOptions.LineBreakStyle,
        };
        var panelStyleOption = new Option<AdfPanelStyle>("--panel-style")
        {
            Description = "Style used for panels",
            DefaultValueFactory = _ => defaultOptions.PanelStyle,
        };
        var expandStyleOption = new Option<AdfExpandStyle>("--expand-style")
        {
            Description = "Style used for collapsible sections",
            DefaultValueFactory = _ => defaultOptions.ExpandStyle,
        };
        var tableStyleOption = new Option<AdfTableStyle>("--table-style")
        {
            Description = "Style used for tables",
            DefaultValueFactory = _ => defaultOptions.TableStyle,
        };
        var mediaRenderingOption = new Option<AdfMediaRendering>("--media-rendering")
        {
            Description = "Rendering used for media items",
            DefaultValueFactory = _ => defaultOptions.MediaRendering,
        };
        var emojiRenderingOption = new Option<AdfEmojiRendering>("--emoji-rendering")
        {
            Description = "Rendering used for emoji",
            DefaultValueFactory = _ => defaultOptions.EmojiRendering,
        };
        var taskListStyleOption = new Option<AdfTaskListStyle>("--task-list-style")
        {
            Description = "Style used for task lists",
            DefaultValueFactory = _ => defaultOptions.TaskListStyle,
        };
        var decisionListStyleOption = new Option<AdfDecisionListStyle>("--decision-list-style")
        {
            Description = "Style used for decision lists",
            DefaultValueFactory = _ => defaultOptions.DecisionListStyle,
        };
        var unknownNodeHandlingOption = new Option<AdfUnknownNodeHandling>("--unknown-node-handling")
        {
            Description = "Handling of nodes whose type is not part of the supported schema",
            DefaultValueFactory = _ => defaultOptions.UnknownNodeHandling,
        };
        var mentionFormatOption = new Option<string>("--mention-format")
        {
            Description = "Format used for mentions, where {text} is the display name and {id} the account identifier",
            DefaultValueFactory = _ => defaultOptions.MentionFormat,
        };
        var statusFormatOption = new Option<string>("--status-format")
        {
            Description = "Format used for status lozenges, where {text} is the text and {color} the color",
            DefaultValueFactory = _ => defaultOptions.StatusFormat,
        };
        var dateFormatOption = new Option<string>("--date-format")
        {
            Description = "Format used for dates, applied with the invariant culture",
            DefaultValueFactory = _ => defaultOptions.DateFormat,
        };

        var rootCommand = new RootCommand("Convert Atlassian Document Format (ADF) documents to Markdown using Meziantou.Framework.AtlassianDataFormat");
        rootCommand.Options.Add(inputOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(headingStyleOption);
        rootCommand.Options.Add(emphasisMarkerOption);
        rootCommand.Options.Add(codeBlockStyleOption);
        rootCommand.Options.Add(codeBlockFenceCharacterOption);
        rootCommand.Options.Add(unorderedListMarkerOption);
        rootCommand.Options.Add(thematicBreakOption);
        rootCommand.Options.Add(lineBreakStyleOption);
        rootCommand.Options.Add(panelStyleOption);
        rootCommand.Options.Add(expandStyleOption);
        rootCommand.Options.Add(tableStyleOption);
        rootCommand.Options.Add(mediaRenderingOption);
        rootCommand.Options.Add(emojiRenderingOption);
        rootCommand.Options.Add(taskListStyleOption);
        rootCommand.Options.Add(decisionListStyleOption);
        rootCommand.Options.Add(unknownNodeHandlingOption);
        rootCommand.Options.Add(mentionFormatOption);
        rootCommand.Options.Add(statusFormatOption);
        rootCommand.Options.Add(dateFormatOption);
        rootCommand.SetAction((parseResult, cancellationToken) =>
        {
            var options = new AdfToMarkdownOptions
            {
                HeadingStyle = parseResult.GetValue(headingStyleOption),
                EmphasisMarker = parseResult.GetValue(emphasisMarkerOption),
                CodeBlockStyle = parseResult.GetValue(codeBlockStyleOption),
                CodeBlockFenceCharacter = parseResult.GetValue(codeBlockFenceCharacterOption),
                UnorderedListMarker = parseResult.GetValue(unorderedListMarkerOption),
                ThematicBreak = parseResult.GetRequiredValue(thematicBreakOption),
                LineBreakStyle = parseResult.GetValue(lineBreakStyleOption),
                PanelStyle = parseResult.GetValue(panelStyleOption),
                ExpandStyle = parseResult.GetValue(expandStyleOption),
                TableStyle = parseResult.GetValue(tableStyleOption),
                MediaRendering = parseResult.GetValue(mediaRenderingOption),
                EmojiRendering = parseResult.GetValue(emojiRenderingOption),
                TaskListStyle = parseResult.GetValue(taskListStyleOption),
                DecisionListStyle = parseResult.GetValue(decisionListStyleOption),
                UnknownNodeHandling = parseResult.GetValue(unknownNodeHandlingOption),
                MentionFormat = parseResult.GetRequiredValue(mentionFormatOption),
                StatusFormat = parseResult.GetRequiredValue(statusFormatOption),
                DateFormat = parseResult.GetRequiredValue(dateFormatOption),
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
        AdfToMarkdownOptions options,
        TextReader? input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string json;
        if (string.IsNullOrWhiteSpace(inputFile))
        {
            json = await (input ?? Console.In).ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var inputPath = FullPath.FromPath(inputFile);
            if (!File.Exists(inputPath))
            {
                await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The input file '{inputPath}' does not exist").AsMemory(), cancellationToken);
                return 1;
            }

            json = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        }

        string markdown;
        if (string.IsNullOrWhiteSpace(json))
        {
            markdown = "";
        }
        else
        {
            try
            {
                markdown = AdfToMarkdown.Convert(json, options);
            }
            catch (AdfException ex)
            {
                await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The input is not a valid ADF document: {ex.Message}").AsMemory(), cancellationToken);
                return 1;
            }
        }

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
