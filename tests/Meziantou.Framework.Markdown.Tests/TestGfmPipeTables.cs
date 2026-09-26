// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Linq;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestGfmPipeTables
{
    private static MarkdownPipeline CreatePipeline(bool trackTrivia = false, bool inferWidths = false)
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions
        {
            UseGfmRules = true,
            RequireHeaderSeparator = false, // GFM rules take precedence over both legacy switches.
            UseHeaderForColumnCount = false,
            InferColumnWidthsFromSeparator = inferWidths
        });
        if (trackTrivia) builder.EnableTrackTrivia();
        return builder.Build();
    }

    [Theory]
    [InlineData("| --- | |")]
    [InlineData("| | --- |")]
    [InlineData("| --- ||")]
    [InlineData("| --- | : |")]
    [InlineData("| --- | :: |")]
    [InlineData("| --- | - - |")]
    [InlineData("| --- | : - |")]
    [InlineData("| --- | - : |")]
    [InlineData("| --- | x |")]
    [InlineData("| --- |")]
    [InlineData("| --- | --- | --- |")]
    [InlineData("| | |")]
    [InlineData("| --- | \u00a0--- |")]
    public void RejectInvalidDelimiterRows(string separator)
    {
        var markdown = "| a | b |\n" + separator + "\n| x | y |";
        foreach (bool trivia in new[] { false, true })
        {
            Assert.Empty(MarkdownConverter.Parse(markdown, CreatePipeline(trivia)).Descendants<Table>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiresDelimiterAndUsesHeaderWidth(bool trivia)
    {
        var pipeline = CreatePipeline(trivia);
        Assert.Empty(MarkdownConverter.Parse("a | b\nx | y", pipeline).Descendants<Table>());
        var table = (Table)MarkdownConverter.Parse("a | b\n|-|-|\nx\ny | z | ignored\n|", pipeline)[0];
        Assert.HasCount(3, table);
        Assert.All(table.Cast<TableRow>(), row => Assert.HasCount(2, row));
        Assert.DoesNotContain("ignored", MarkdownConverter.ToHtml("a | b\n|-|-|\nx\ny | z | ignored", pipeline));
    }

    [Fact]
    public void DefaultsRemainPermissive()
    {
        Assert.False(new PipeTableOptions().UseGfmRules);
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var table = (Table)MarkdownConverter.Parse("| a | b |\n| --- | |\n| x | y | z |", pipeline)[0];
        Assert.HasCount(3, (TableRow)table[0]);
        Assert.IsType<Table>(MarkdownConverter.Parse("a | b\n--- |\nx | y", pipeline)[0]);
    }

    [Theory]
    [InlineData("a\n---", "<h2>a</h2>\n")]
    [InlineData("|a|\n---", "<h2>|a|</h2>\n")]
    public void DoesNotStealSetextHeadings(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, CreatePipeline()));
    }

    [Theory]
    [InlineData("a\n:-\nx")]
    [InlineData("a\n-|\nx")]
    [InlineData("|a|\n:-:\nx")]
    public void SingleColumnTablesMayOmitOuterPipes(string markdown)
    {
        var table = (Table)MarkdownConverter.Parse(markdown, CreatePipeline())[0];
        Assert.HasCount(2, table);
        Assert.HasCount(1, table.ColumnDefinitions);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void SupportsLineEndings(string newline)
    {
        var markdown = "| a | b |\n| - | - |\n| x | y |\nz\n\nafter";
        foreach (bool trivia in new[] { false, true })
        {
            var pipeline = CreatePipeline(trivia);
            Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(markdown.Replace("\n", newline, StringComparison.Ordinal), pipeline));
        }
    }

    [Fact]
    public void BackslashRunsEscapePipesBeforeInlineParsing()
    {
        var markdown = """
            a | b
            |-|-|
            \\|x
            \\\|y
            """;
        var html = MarkdownConverter.ToHtml(markdown, CreatePipeline());
        Assert.Contains("<td>|x</td>\n<td></td>", html);
        Assert.Contains("<td>\\|y</td>\n<td></td>", html);
    }

    [Fact]
    public void ExtensionConfigurationSelectsGfmRules()
    {
        var configured = new MarkdownPipelineBuilder().Configure("gfm-pipetables").Build();
        Assert.True(configured.Extensions.Find<PipeTableExtension>()!.Options.UseGfmRules);
        var markdown = "a | b\n|-|-|\nx";
        Assert.Equal(MarkdownConverter.ToHtml(markdown, CreatePipeline()), MarkdownConverter.ToHtml(markdown, configured));
        var advanced = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseGfmRules = true }).UseAdvancedExtensions().Build();
        Assert.Equal(MarkdownConverter.ToHtml(markdown, configured), MarkdownConverter.ToHtml(markdown, advanced));
    }

    [Theory]
    [InlineData("`a|b`", "`a", "b`")]
    [InlineData("[a|b](url)", "[a", "b](url)")]
    [InlineData("**a|b**", "**a", "b**")]
    [InlineData("<i title='a|b'>", "&lt;i title='a", "b'&gt;")]
    public void PipesTakePrecedenceOverAllInlines(string body, string first, string second)
    {
        var html = MarkdownConverter.ToHtml("a | b\n|-|-|\n| " + body + " |", CreatePipeline());
        Assert.Contains("<td>" + first + "</td>\n<td>" + second + "</td>", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapesAndReferencesRemainLocalToCells(bool trivia)
    {
        var markdown = """
            | [a][ref] | b |
            | --- | --- |
            | `a\|b` | **\|** |
            | `\_` | \_ |
            | `\\` | \\ |
            | <i title="\|"> | [a\|b](url) |

            [ref]: /target

            `\|`
            """;
        var html = MarkdownConverter.ToHtml(markdown, CreatePipeline(trivia));
        Assert.Contains("<th><a href=\"/target\">a</a></th>", html);
        Assert.Contains("<code>a|b</code>", html);
        Assert.Contains("<strong>|</strong>", html);
        Assert.Contains("<code>\\_</code>", html);
        Assert.Contains("<code>\\\\</code>", html);
        Assert.Contains("<i title=\"|\">", html);
        Assert.Contains("<a href=\"url\">a|b</a>", html);
        Assert.EndsWith("<p><code>\\|</code></p>\n", html);
    }

    [Theory]
    [InlineData("# heading", "<h1>heading</h1>")]
    [InlineData("> quote", "<blockquote>")]
    [InlineData("- item", "<ul>")]
    [InlineData("1. item", "<ol>")]
    [InlineData("2. item", "<ol start=\"2\">")]
    [InlineData("---", "<hr />")]
    [InlineData("```\ncode\n```", "<pre><code>code")]
    [InlineData("<div>\nhtml\n</div>", "<div>")]
    public void OtherBlocksTerminateTables(string next, string expected)
    {
        var html = MarkdownConverter.ToHtml("a | b\n|-|-|\nx | y\n" + next, CreatePipeline());
        Assert.Contains("</table>\n" + expected, html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TablesCanInterruptParagraphsAndNestInContainers(bool trivia)
    {
        var table = "a | b\n|-|-|\nx | y";
        var pipeline = CreatePipeline(trivia);
        var html = MarkdownConverter.ToHtml(table, pipeline);
        Assert.Equal("<p>first\nsecond</p>\n" + html, MarkdownConverter.ToHtml("first\nsecond\n" + table, pipeline));
        Assert.Equal("<blockquote>\n" + html + "</blockquote>\n<p>outside</p>\n", MarkdownConverter.ToHtml("> " + table.Replace("\n", "\n> ", StringComparison.Ordinal) + "\noutside", pipeline));
        Assert.Equal("<ul>\n<li>\n" + html + "</li>\n</ul>\n", MarkdownConverter.ToHtml("- " + table.Replace("\n", "\n  ", StringComparison.Ordinal), pipeline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesSourceLocationsAndInferredWidths(bool trivia)
    {
        var markdown = "before\n\n| a | b |\n| :- | ---: |\n| `c\\|d` | e |";
        var pipeline = CreatePipeline(trivia, inferWidths: true);
        var table = (Table)MarkdownConverter.Parse(markdown, pipeline)[1];
        Assert.Equal(2, table.Line);
        Assert.Equal(markdown.IndexOf('|', StringComparison.Ordinal), table.Span.Start);
        Assert.Equal(markdown.Length - 1, table.Span.End);
        Assert.Equal(new[] { 25f, 75f }, table.ColumnDefinitions.Select(column => column.Width));
        var code = table.Descendants<CodeInline>().Single();
        Assert.Equal("`c\\|d`", markdown.Substring(code.Span.Start, code.Span.Length));
        Assert.Equal(4, code.Line);
        Assert.Equal(2, code.Column);
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
    }

    [Fact]
    public void NormalizationPreservesEscapedPipes()
    {
        var markdown = """
            a | b
            |-|-|
            `a\|b` | **\|**
            <i title="\|"> | [a\|b](url)
            """;
        var pipeline = CreatePipeline();
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
        Assert.Equal(normalized, MarkdownConverter.Normalize(normalized, pipeline: pipeline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGfmBlockBoundaries(bool trivia)
    {
        var pipeline = CreatePipeline(trivia);
        var list = "a|b\n- | -\nx|y";
        var plain = new MarkdownPipelineBuilder();
        if (trivia) plain.EnableTrackTrivia();
        Assert.Equal(MarkdownConverter.ToHtml(list, plain.Build()), MarkdownConverter.ToHtml(list, pipeline));
        var retry = "a|b|c\n--|--\na|b\n--|--\nx|y";
        Assert.Equal(MarkdownConverter.ToHtml(retry), MarkdownConverter.ToHtml(retry, pipeline));
        var header = MarkdownConverter.ToHtml("a|b\n--|--", pipeline);
        Assert.Equal(header + "<p>|\nx|y</p>\n", MarkdownConverter.ToHtml("a|b\n--|--\n|\nx|y", pipeline));
    }

    [Theory]
    [InlineData("\u00a0")]
    [InlineData("\u2003")]
    [InlineData("\v")]
    [InlineData("\f")]
    public void NativeGfmCellWhitespace(string space)
    {
        foreach (bool trivia in new[] { false, true })
        {
            var html = MarkdownConverter.ToHtml("a|b\n--|--\n|" + space + "x" + space + "|y|", CreatePipeline(trivia));
            // The scanner consumes ASCII whitespace immediately after a pipe,
            // but cell trimming only removes spaces and tabs, not Unicode spaces.
            var leading = space is "\v" or "\f" ? "" : space;
            Assert.Contains("<td>" + leading + "x" + space + "</td>", html);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGfmUnescapesReferencesAndAutolinks(bool trivia)
    {
        var markdown = "a|b\n--|--\n[a\\|b]|<https://a/\\|>\n\n[a|b]: /target";
        var html = MarkdownConverter.ToHtml(markdown, CreatePipeline(trivia));
        Assert.Contains("<a href=\"/target\">a|b</a>", html);
        Assert.Contains("<a href=\"https://a/%7C\">https://a/|</a>", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnescapingRetainsOriginalInlineLocations(bool trivia)
    {
        var markdown = "before\n\n| a | b |\n| - | - |\n| \\|x | `a\\\\|b` [x](u\\\\|v) |\n\nafter";
        var document = MarkdownConverter.Parse(markdown, CreatePipeline(trivia));
        var table = document.Descendants<Table>().Single();
        var literal = ((ParagraphBlock)((TableCell)((TableRow)table[1])[0])[0]).Inline!.FirstChild!;
        var code = table.Descendants<CodeInline>().Single();
        var link = table.Descendants<LinkInline>().Single();
        Assert.Equal("\\|x", markdown.Substring(literal.Span.Start, literal.Span.Length));
        Assert.Equal(2, literal.Column);
        Assert.Equal("`a\\\\|b`", markdown.Substring(code.Span.Start, code.Span.Length));
        Assert.Equal("[x](u\\\\|v)", markdown.Substring(link.Span.Start, link.Span.Length));
        Assert.Equal(4, code.Line);
        Assert.Equal(markdown.Split('\n')[4].IndexOf("[x]", StringComparison.Ordinal), link.Column);
        var after = document.Descendants<LiteralInline>().Last();
        Assert.Equal("after", markdown.Substring(after.Span.Start, after.Span.Length));
        Assert.Equal(6, after.Line);
    }

    [Fact]
    public void RejectsRowsBeyondNativeColumnLimit()
    {
        var pipeline = CreatePipeline();
        var header = new string('|', ushort.MaxValue + 2);
        var separator = string.Concat(Enumerable.Repeat("|-", ushort.MaxValue + 1)) + "|";
        Assert.Empty(MarkdownConverter.Parse(header + "\n" + separator, pipeline).Descendants<Table>());
        var document = MarkdownConverter.Parse("a\n|-|\n" + header, pipeline);
        Assert.HasCount(1, document.Descendants<Table>().Single());
    }
}
