namespace Meziantou.Framework.Markdown.Tests;

public class TestCodeInline
{
    [Fact]
    public void UnpairedCodeInlineWithTrailingChars()
    {
        TestParser.TestSpec("*`\n\f", "<p>*`</p>");
    }

    [Theory]
    [InlineData("\\``a`", "<p>`<code>a</code></p>")]
    [InlineData("\\```a``", "<p>`<code>a</code></p>")]
    [InlineData("`a`\\``b`", "<p><code>a</code>`<code>b</code></p>")]
    [InlineData("\\\\\\``a`", "<p>\\`<code>a</code></p>")]
    [InlineData("\\\\``a``", "<p>\\<code>a</code></p>")]
    [InlineData("\\\\\\\\``a`", "<p>\\\\``a`</p>")]
    [InlineData("\\``a``", "<p>``a``</p>")]
    public void BacktickAfterAnEscapedBacktickCanOpenACodeSpan(string markdown, string expected)
    {
        TestParser.TestSpec(markdown, expected);
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Theory(DisableParallelization = true)]
    [InlineData("\\``a ", "<p>``a ``a ")]
    [InlineData("\\``a\n", "<p>``a\n``a\n")]
    public void UnclosedCodeSpansAreParsedInLinearTime(string item, string expectedStart)
    {
        // Each opening backtick string without a closing one used to scan the rest of the paragraph again
        var markdown = string.Concat(Enumerable.Repeat(item, 100_000));

        var stopwatch = ThreadCpuStopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown);
        stopwatch.Stop();

        Assert.StartsWith(expectedStart, html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("`\r`", "<p><code> </code></p>\n")]
    [InlineData("` a\rb `", "<p><code>a b</code></p>\n")]
    public void CarriageReturnIsASpace(string markdown, string expectedHtml)
    {
        // A carriage return line ending was removed instead of being converted to a space
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(markdown));
    }
}
