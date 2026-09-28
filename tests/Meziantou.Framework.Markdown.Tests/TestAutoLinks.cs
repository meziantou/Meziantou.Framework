using System.Diagnostics.CodeAnalysis;

using Meziantou.Framework.Markdown.Extensions.AutoLinks;
using Meziantou.Framework.Markdown.Helpers;

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

    [Theory]
    [InlineData("(www.a(www.a(www.a", "<p>(www.a(www.a(www.a</p>\n")]
    [InlineData("(www.a.b)(www.c.d)x", "<p>(<a href=\"http://www.a.b\">www.a.b</a>)(<a href=\"http://www.c.d\">www.c.d</a>)x</p>\n")]
    [InlineData("*http://a*http://a*http://a", "<p><em>http://a</em>http://a*http://a</p>\n")]
    [InlineData("~ftp://a~ftp://a.b", "<p>~ftp://a~<a href=\"ftp://a.b\">ftp://a.b</a></p>\n")]
    [InlineData("_www.a_www.a_www.a.b", "<p>_<a href=\"http://www.a_www.a_www.a.b\">www.a_www.a_www.a.b</a></p>\n")]
    [InlineData("*mailto:a*mailto:u@a.b", "<p>*<a href=\"mailto:a*mailto:u@a.b\">a*mailto:u@a.b</a></p>\n")]
    [InlineData("_mailto:a@b_mailto:u@a.b", "<p>_mailto:a@b_<a href=\"mailto:u@a.b\">u@a.b</a></p>\n")]
    [InlineData("(http://a.b(c)d)(www.e.f)", "<p>(http://a.b(c)d)(<a href=\"http://www.e.f\">www.e.f</a>)</p>\n")]
    [InlineData("(www.a.b_(www.c_d.e.f", "<p>(www.a.b_(<a href=\"http://www.c_d.e.f\">www.c_d.e.f</a></p>\n")]
    public void UrlCandidatesInTheSameRunOfText(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Equal(expected, html);
    }

    [Fact]
    public void PendingEmphasisIsRemovedFromTheEndOfEachUrlCandidate()
    {
        // Both candidates end before the entity, but only the second one has '~' as pending emphasis
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var html = MarkdownConverter.ToHtml("*www.a*~www.a.b*~&amp;", pipeline);

        Assert.Equal("<p><em>www.a</em><sub><a href=\"http://www.a.b\">www.a.b</a>*</sub>&amp;</p>\n", html);
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void ScanCacheGivesTheSameAnswersAsIndependentScans()
    {
        // Candidates after the first one of a run are answered from tables built for the run; a new cache answers the first
        // candidate of its run directly
        string[] atoms = ["www.", "http://", "mailto:", "a", "b", "0", "_", "_", ".", ".", "..", "(", ")", "/", "?", "#", ":", "@", "@", "&amp;", "&x", ";", ",", "*", "*", "~", "-", "!", "\"", "é"];
        var random = new Random(42);
        for (var n = 0; n < 3000; n++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(1, 30)).Select(_ => atoms[random.Next(atoms.Length)]));
            var cache = new AutoLinkScanCache();
            for (var start = 0; start < text.Length; start++)
            {
                var slice = new StringSlice(text, start, text.Length - 1);
                var end = cache.ScanUrl(slice);
                var independent = new AutoLinkScanCache();
                Assert.Equal(independent.ScanUrl(slice), end, message: $"ScanUrl {text} {start}");
                if (end < 0)
                {
                    continue;
                }

                var atIndex = text.AsSpan(start, end - start).IndexOf('@');
                Assert.Equal(atIndex, cache.IndexOfAt(start, end), message: $"IndexOfAt {text} {start}");
                for (var linkEnd = start; linkEnd <= end; linkEnd++)
                {
                    for (var domainStart = start; domainStart <= linkEnd; domainStart++)
                    {
                        foreach (var allowDomainWithoutPeriod in new[] { false, true })
                        {
                            Assert.Equal(
                                independent.IsValidDomain(start, domainStart, linkEnd, allowDomainWithoutPeriod),
                                cache.IsValidDomain(start, domainStart, linkEnd, allowDomainWithoutPeriod),
                                message: $"IsValidDomain {text} {start} {domainStart} {linkEnd} {allowDomainWithoutPeriod}");
                        }
                    }
                }
            }

            // The pending emphasis characters are the same for the candidates of a run, which often end at the same position
            foreach (var characters in new[] { "*", "*_~" })
            {
                cache = new AutoLinkScanCache();
                for (var start = 0; start < text.Length; start++)
                {
                    var end = cache.ScanUrl(new StringSlice(text, start, text.Length - 1));
                    if (end >= 0)
                    {
                        var trimStart = end;
                        while (trimStart > start && characters.Contains(text[trimStart - 1], StringComparison.Ordinal))
                        {
                            trimStart--;
                        }

                        Assert.Equal(trimStart, cache.GetTrailingCharactersStart(start, end, characters), message: $"Trailing {text} {start} {characters}");
                    }
                }
            }
        }
    }

    [Theory(DisableParallelization = true)]
    [InlineData("(www.a", "autolinks", true)]
    [InlineData("*http://a", "autolinks", false)]
    [InlineData("~ftp://a", "autolinks", true)]
    [InlineData("_www.a", "autolinks", true)]
    [InlineData("*mailto:a", "autolinks", false)]
    [InlineData("_mailto:a@b", "autolinks", true)]
    [InlineData("(www.a", "advanced", false)]
    [InlineData("*http://a", "advanced", false)]
    [InlineData("~ftp://a", "advanced", false)]
    [InlineData("_www.a", "advanced", false)]
    public void ManyUrlCandidatesWithoutWhitespaceAreParsedInLinearTime(string item, string extensions, bool isRenderedAsText)
    {
        // Each candidate used to scan and copy the rest of the text without whitespace, and to check the domain in all of it
        var markdown = string.Concat(Enumerable.Repeat(item, 160_000 / item.Length));
        var pipeline = new MarkdownPipelineBuilder().Configure(extensions).Build();

        var stopwatch = ThreadCpuStopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        if (isRenderedAsText)
        {
            Assert.Equal("<p>" + markdown + "</p>\n", html);
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    private static void AssertEqualIgnoringWhiteSpace(string expected, string actual, string? message = null)
    {
        Assert.Equal(RemoveWhiteSpace(expected), RemoveWhiteSpace(actual), message: message);

        static string RemoveWhiteSpace(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    }
}
