// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.
using System.Text;
using System.Text.RegularExpressions;

using Meziantou.Framework.Markdown.Extensions.Abbreviations;
using Meziantou.Framework.Markdown.Extensions.Alerts;
using Meziantou.Framework.Markdown.Extensions.AutoLinks;
using Meziantou.Framework.Markdown.Extensions.CustomContainers;
using Meziantou.Framework.Markdown.Extensions.DefinitionLists;
using Meziantou.Framework.Markdown.Extensions.Emoji;
using Meziantou.Framework.Markdown.Extensions.Figures;
using Meziantou.Framework.Markdown.Extensions.Footers;
using Meziantou.Framework.Markdown.Extensions.JiraLinks;
using Meziantou.Framework.Markdown.Extensions.SmartyPants;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestParser
{
    [Fact]
    public void EnsureSpecsAreUpToDate()
    {
        var specsFilePaths = Directory.GetDirectories(TestsDirectory)
            .Where(dir => dir.EndsWith("Specs", StringComparison.Ordinal))
            .SelectMany(dir => Directory.GetFiles(dir)
                .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Where(file => !file.Contains("readme", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var specsMarkdown = new string[specsFilePaths.Length];
        var specsSyntaxTrees = new MarkdownDocument[specsFilePaths.Length];

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        for (int i = 0; i < specsFilePaths.Length; i++)
        {
            string markdown = specsMarkdown[i] = File.ReadAllText(specsFilePaths[i]);
            specsSyntaxTrees[i] = MarkdownConverter.Parse(markdown, pipeline);
        }

        foreach (var specFilePath in specsFilePaths)
        {
            string testFilePath = Path.ChangeExtension(specFilePath, ".generated.cs");

            Assert.True(File.Exists(testFilePath), message: "A new specification file has been added. Add the spec to the list in tools/Meziantou.Framework.Markdown.Specs.Generator and regenerate the tests.");

            // Git does not preserve file timestamps, so they cannot be compared on a fresh CI checkout
            if (IsContinuousIntegration)
                continue;

            DateTime specTime = File.GetLastWriteTimeUtc(specFilePath);
            DateTime testTime = File.GetLastWriteTimeUtc(testFilePath);

            // If file creation times aren't preserved by git, add some leeway
            // If specs have come from git, assume that they were regenerated since CI would fail otherwise
            testTime = testTime.AddMinutes(3);

            // This might not catch a changed spec every time, but should at least sometimes. Otherwise CI will catch it

            // This could also trigger, if a user has modified the spec file but reverted the change - can't think of a good workaround
            Assert.True(
                specTime < testTime,
                message: $"{Path.GetFileName(specFilePath)} has been modified. Run tools/Meziantou.Framework.Markdown.Specs.Generator to regenerate the tests. " +
                "If you have modified a specification file, but reverted all changes, ignore this error or revert the 'changed' timestamp metadata on the file.");
        }

        TestDescendantsOrder.TestSchemas(specsSyntaxTrees);
    }

    [Fact]
    public void ParseEmptyDocumentWithTrackTriviaEnabled()
    {
        var document = MarkdownConverter.Parse("", trackTrivia: true);
        using var sw = new StringWriter();
        new RoundtripRenderer(sw).Render(document);
        Assert.Equal("", sw.ToString());
    }

    internal static void TestSpec(string inputText, string expectedOutputText, string? extensions = null, bool plainText = false, string? context = null)
    {
        context ??= string.Empty;
        if (!string.IsNullOrEmpty(context))
        {
            context += "\n";
        }
        foreach (var pipeline in GetPipeline(extensions))
        {
            TestSpec(inputText, expectedOutputText, pipeline.Value, plainText, context: context + $"Pipeline configured with extensions: {pipeline.Key}");
        }

        // The chain of open containers and the openers bottoms are only used for long paragraphs, which the examples are not
        foreach (var pipeline in GetPipeline(extensions))
        {
            TestSpec(inputText, expectedOutputText, UseMinimumThresholds(pipeline.Value), plainText, context: context + $"Pipeline configured with extensions: {pipeline.Key}, with the minimum thresholds");
        }

        // With trivia, every example must be written back exactly as it was parsed
        foreach (var builder in GetPipelineBuilders(extensions))
        {
            if (builder.Value.Extensions.Any(extension => ExtensionsWithoutRoundtrip.Contains(extension.GetType())))
            {
                continue;
            }

            TestRoundtrip.RoundTrip(inputText, builder.Value, context: context + $"Roundtrip with extensions: {builder.Key}");
        }
    }

    // The extensions that do not write their syntax back yet, so the examples that use them are not round-tripped
    private static readonly HashSet<Type> ExtensionsWithoutRoundtrip =
    [
        typeof(AbbreviationExtension),
        typeof(AlertExtension),
        typeof(AutoLinkExtension),
        typeof(CustomContainerExtension),
        typeof(DefinitionListExtension),
        typeof(EmojiExtension),
        typeof(FigureExtension),
        typeof(FooterExtension),
        typeof(GridTableExtension),
        typeof(PipeTableExtension),
        typeof(SmartyPantsExtension),
    ];

    /// <summary>
    /// Makes the inline processor track the chain of open containers, and the emphasis parser track the openers bottoms, from
    /// the first container and delimiter, instead of only for long paragraphs.
    /// </summary>
    internal static MarkdownPipeline UseMinimumThresholds(MarkdownPipeline pipeline)
    {
        pipeline.OpenContainersTrackingThreshold = 1;
        foreach (var parser in pipeline.InlineParsers.OfType<EmphasisInlineParser>())
        {
            parser.OpenersBottomThreshold = 0;
        }

        return pipeline;
    }

    public static TheoryData<string, string, string> PaddedInlineExamples()
    {
        var data = new TheoryData<string, string, string>();
        string? section = null;
        var lines = File.ReadAllLines(Path.Combine(TestsDirectory, "Specs", "CommonMark.md"));
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                section = lines[i][3..];
            }
            else if (lines[i] == "```````````````````````````````` example")
            {
                var markdown = new StringBuilder();
                while (lines[++i] != ".")
                {
                    markdown.Append(lines[i]).Append('\n');
                }

                var html = new StringBuilder();
                while (lines[++i] != "````````````````````````````````")
                {
                    html.Append(lines[i]).Append('\n');
                }

                if (section is "Emphasis and strong emphasis" or "Links" or "Images")
                {
                    AddPaddedExample(data, markdown.ToString().Replace('→', '\t'), html.ToString().Replace('→', '\t'));
                }
            }
        }

        return data;

        // The unmatched delimiters stay literal text before the example as long as the example cannot close them, starts the
        // paragraph and is a single paragraph without link reference definitions
        static void AddPaddedExample(TheoryData<string, string, string> data, string markdown, string html)
        {
            var delimiter = !markdown.Contains('_', StringComparison.Ordinal) ? "_" : !markdown.Contains('*', StringComparison.Ordinal) ? "*" : null;
            if (delimiter is null || char.IsWhiteSpace(markdown[0]) || markdown.Contains("]:", StringComparison.Ordinal) ||
                !html.StartsWith("<p>", StringComparison.Ordinal) || !html.EndsWith("</p>\n", StringComparison.Ordinal) || html.IndexOf("<p>", 1, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            // 40 delimiters make the emphasis parser track the openers bottoms, and 300 make the inline processor track the chain
            foreach (var count in new[] { 40, 300 })
            {
                var padding = string.Concat(Enumerable.Repeat(delimiter + "a ", count));
                data.Add(padding + markdown, "<p>" + padding + html[3..], $"{count} '{delimiter}'");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PaddedInlineExamples))]
    public void InlineExamplesAreUnchangedAfterUnmatchedDelimiters(string markdown, string expected, string padding)
    {
        TestSpec(markdown, expected, new MarkdownPipelineBuilder().Build(), context: "Padded with " + padding);
    }

    internal static void TestSpec(string inputText, string expectedOutputText, MarkdownPipeline pipeline, bool plainText = false, string? context = null)
    {
        // Uncomment this line to get more debug information for process inlines.
        //pipeline.DebugLog = Console.Out;
        var result = plainText ? MarkdownConverter.ToPlainText(inputText, pipeline) : MarkdownConverter.ToHtml(inputText, pipeline);

        result = Compact(result);
        expectedOutputText = Compact(expectedOutputText);

        PrintAssertExpected(inputText, result, expectedOutputText, context);
    }

    internal static void PrintAssertExpected(string source, string result, string expected, string? context = null)
    {
        if (expected != result)
        {
            // xunit does not capture the console output, so the details are also reported in the assertion message
            var message = new StringBuilder();
            if (context is not null)
            {
                message.Append(context).Append('\n');
            }
            message.Append("```````````````````Source\n");
            message.Append(DisplaySpaceAndTabs(source)).Append('\n');
            message.Append("```````````````````Result\n");
            message.Append(DisplaySpaceAndTabs(result)).Append('\n');
            message.Append("```````````````````Expected\n");
            message.Append(DisplaySpaceAndTabs(expected)).Append('\n');
            message.Append("```````````````````\n");
            Console.WriteLine(message);
            TextAssert.AreEqual(expected, result, message.ToString());
        }
    }

    public static IEnumerable<KeyValuePair<string, MarkdownPipeline>> GetPipeline(string? extensionsGroupText)
    {
        foreach (var builder in GetPipelineBuilders(extensionsGroupText))
        {
            yield return new KeyValuePair<string, MarkdownPipeline>(builder.Key, builder.Value.Build());
        }
    }

    private static IEnumerable<KeyValuePair<string, MarkdownPipelineBuilder>> GetPipelineBuilders(string? extensionsGroupText)
    {
        // For the standard case, we make sure that both the CommmonMark core and Extra/Advanced are CommonMark compliant!
        if (string.IsNullOrEmpty(extensionsGroupText))
        {
            yield return new KeyValuePair<string, MarkdownPipelineBuilder>("default", new MarkdownPipelineBuilder());

            yield return new KeyValuePair<string, MarkdownPipelineBuilder>("advanced", new MarkdownPipelineBuilder()  // Use similar to advanced extension without auto-identifier
             .UseAbbreviations()
            //.UseAutoIdentifiers()
            .UseCitations()
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseFigures()
            .UseFooters()
            .UseFootnotes()
            .UseGridTables()
            .UseMathematics()
            .UseMediaLinks()
            .UsePipeTables()
            .UseListExtras()
            .UseGenericAttributes());

            yield break;
        }

        var extensionGroups = extensionsGroupText.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var extensionsText in extensionGroups)
        {
            var builder = new MarkdownPipelineBuilder();
            builder.DebugLog = Console.Out;
            if (extensionsText == "jiralinks")
            {
                builder.UseJiraLinks(new JiraLinkOptions("http://your.company.abc"));
            }
            else
            {
                builder = builder.Configure(extensionsText);
            }
            yield return new KeyValuePair<string, MarkdownPipelineBuilder>(extensionsText, builder);
        }
    }

    public static string DisplaySpaceAndTabs(string text)
    {
        // Output special characters to check correctly the results
        return text.Replace('\t', '→').Replace(' ', '·');
    }

    private static string Compact(string html)
    {
        // Normalize the output to make it compatible with CommonMark specs
        html = html.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        html = Regex.Replace(html, @"\s+</li>", "</li>", RegexOptions.None, Regex.InfiniteMatchTimeout);
        html = Regex.Replace(html, @"<li>\s+", "<li>", RegexOptions.None, Regex.InfiniteMatchTimeout);
        return html;
    }

    public static readonly bool IsContinuousIntegration = Environment.GetEnvironmentVariable("CI") is not null;

    // CallerFilePath cannot be used: CI builds map the source paths (/_/)
    public static readonly string TestsDirectory = (FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot() / "tests" / "Meziantou.Framework.Markdown.Tests").Value;
}