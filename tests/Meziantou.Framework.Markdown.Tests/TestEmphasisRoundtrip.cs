// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestEmphasisRoundtrip
{
    private static string RoundTrip(string markdown, MarkdownPipeline pipeline)
    {
        MarkdownDocument document = MarkdownConverter.Parse(markdown, pipeline);
        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);
        return writer.ToString();
    }

    // Leftover delimiter characters below the minimum count used to stay nested
    // inside the emphasis they closed, moving the following text to the end of
    // the block. https://github.com/xoofx/markdig/issues/743
    [Fact]
    public void GridTableSeparatorRoundtripsWithTrackTrivia()
    {
        string markdown =
            "+---------------+---------------+--------------------+\n" +
            "| Fruit         | Price         | Advantages         |\n" +
            "+===============+===============+====================+\n" +
            "| Bananas       | first line    | first line         |\n" +
            "|               | next line     | next line          |\n" +
            "+---------------+---------------+--------------------+\n";

        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UseListExtras()
            .EnableTrackTrivia()
            .Build();

        Assert.Equal(markdown, RoundTrip(markdown, pipeline));
    }

    // Same defect, minimal: leftovers (15 = 7 * 2 + 1) below the "==" minimum.
    [Fact]
    public void UnbalancedMarkedRunsRoundtrip()
    {
        string markdown = "+===============+===============+\n";

        var pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .EnableTrackTrivia()
            .Build();

        Assert.Equal(markdown, RoundTrip(markdown, pipeline));
    }

    [Theory]
    [InlineData("===a===== tail\n", "<p>=<mark>a===</mark> tail</p>")]
    [InlineData("===a==== tail\n", "<p>=<mark>a==</mark> tail</p>")]
    [InlineData("=====a======= tail\n", "<p>=<mark><mark>a===</mark></mark> tail</p>")]
    [InlineData("+++a+++++ tail\n", "<p>+<ins>a+++</ins> tail</p>")]
    [InlineData("*===a===== tail*\n", "<p><em>=<mark>a===</mark> tail</em></p>")]
    [InlineData("===a===== tail ==next==\n", "<p>=<mark>a===</mark> tail <mark>next</mark></p>")]
    public void UnmatchedCloserPreservesTrailingContent(string markdown, string expectedHtml)
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().EnableTrackTrivia().Build();

        Assert.Equal(markdown, RoundTrip(markdown, pipeline));
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(markdown, pipeline).Trim());
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UseEmphasisExtras().Build()).Trim());
    }

    // The fix must not prevent balanced "marked" emphasis from being detected.
    [Fact]
    public void BalancedMarkedEmphasisStillParses()
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().Build();
        Assert.Equal("<p><mark>bold</mark></p>", MarkdownConverter.ToHtml("==bold==", pipeline).Trim());
    }
}
