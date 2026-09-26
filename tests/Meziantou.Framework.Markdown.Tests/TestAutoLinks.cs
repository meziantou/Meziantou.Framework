using Meziantou.Framework.Markdown.Extensions.AutoLinks;

namespace Meziantou.Framework.Markdown.Tests;

public class TestAutoLinks
{
    [Theory]
    [InlineData("https://localhost", "<p><a href=\"https://localhost\">https://localhost</a></p>")]
    [InlineData("http://localhost", "<p><a href=\"http://localhost\">http://localhost</a></p>")]
    [InlineData("https://l", "<p><a href=\"https://l\">https://l</a></p>")]
    [InlineData("www.l", "<p><a href=\"http://www.l\">www.l</a></p>")]
    [InlineData("https://localhost:5000", "<p><a href=\"https://localhost:5000\">https://localhost:5000</a></p>")]
    [InlineData("www.l:5000", "<p><a href=\"http://www.l:5000\">www.l:5000</a></p>")]
    public void TestLinksWithAllowDomainWithoutPeriod(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoLinks(new AutoLinkOptions { AllowDomainWithoutPeriod = true })
            .Build();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        AssertEqualIgnoringWhiteSpace(expected, html);
    }

    // https://github.com/xoofx/markdig/issues/668
    // A heading's implicit reference must not resolve inside another still-open
    // link bracket, which would break the outer link.
    [Fact]
    public void TestAutoIdentifierHeadingLinkDoesNotHijackNestedLinkLabel()
    {
        var markdown = "# Testing Markdown\n\n" +
                        "### Header\n\n" +
                        "[Testing [Header]](https://www.bing.com)\n\n" +
                        "[Testing [test]](https://www.google.com)\n";
        var expected = "<h1 id=\"testing-markdown\">Testing Markdown</h1>\n" +
                        "<h3 id=\"header\">Header</h3>\n" +
                        "<p><a href=\"https://www.bing.com\">Testing [Header]</a></p>\n" +
                        "<p><a href=\"https://www.google.com\">Testing [test]</a></p>";

        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseAutoIdentifiers()
            .Build();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        AssertEqualIgnoringWhiteSpace(expected, html);
    }

    [Theory]
    [InlineData("[Header]", "<a href=\"#header\">Header</a>")]
    [InlineData("[Header][]", "<a href=\"#header\">Header</a>")]
    [InlineData("[label][Header]", "<a href=\"#header\">label</a>")]
    [InlineData("[Testing [Header]](/target)", "<a href=\"/target\">Testing [Header]</a>")]
    [InlineData("[Testing [Header][]](/target)", "<a href=\"/target\">Testing [Header][]</a>")]
    [InlineData("[Testing [label][Header]](/target)", "<a href=\"/target\">Testing [label][Header]</a>")]
    [InlineData("![Testing [Header]](/image.png)", "<img src=\"/image.png\" alt=\"Testing [Header]\" />")]
    public void TestAutoIdentifierReferenceResolution(string markdown, string expected)
    {
        foreach (var trackTrivia in new[] { false, true })
        {
            var builder = new MarkdownPipelineBuilder().UseAutoLinks().UseAutoIdentifiers();
            if (trackTrivia)
            {
                builder.EnableTrackTrivia();
            }

            var html = MarkdownConverter.ToHtml("# Header\n\n" + markdown, builder.Build());

            AssertEqualIgnoringWhiteSpace("<h1 id=\"header\">Header</h1>\n<p>" + expected + "</p>", html, $"TrackTrivia: {trackTrivia}");
        }
    }

    [Fact]
    public void TestExplicitReferenceStillResolvesInsideOpenLink()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().UseAutoIdentifiers().Build();
        var html = MarkdownConverter.ToHtml("[Header]: /explicit\n\n# Header\n\n[Testing [Header]](/target)", pipeline);

        AssertEqualIgnoringWhiteSpace("<h1 id=\"header\">Header</h1>\n<p>[Testing <a href=\"/explicit\">Header</a>](/target)</p>", html);
    }

    [Theory]
    [InlineData("[<a href='x'> b ] www.a.com", "[<a href='x'> b ] www.a.com")]
    [InlineData("[<a href='x'> b </a> ] www.a.com", "[<a href='x'> b </a> ] <a href=\"http://www.a.com\">www.a.com</a>")]
    [InlineData("[b <a href='x'>] www.a.com", "[b <a href='x'>] www.a.com")]
    [InlineData("[<a href='x'>[c] www.a.com", "[<a href='x'>[c] www.a.com")]
    public void AutoLinkIsNotCreatedAfterAPendingAnchorMovedByABracket(string markdown, string expected)
    {
        // Many unresolved emphasis delimiters make the inline processor track what the autolink parser looks for
        var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
        foreach (var prefix in new[] { "", string.Concat(Enumerable.Repeat("*x ", 300)) })
        {
            var html = MarkdownConverter.ToHtml(prefix + markdown, pipeline);

            Assert.Equal("<p>" + prefix + expected + "</p>\n", html);
        }
    }

    private static void AssertEqualIgnoringWhiteSpace(string expected, string actual, string? message = null)
    {
        Assert.Equal(RemoveWhiteSpace(expected), RemoveWhiteSpace(actual), message: message);

        static string RemoveWhiteSpace(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    }
}
