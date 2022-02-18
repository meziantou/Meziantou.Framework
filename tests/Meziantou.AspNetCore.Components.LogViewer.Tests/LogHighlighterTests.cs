using FluentAssertions;
using Xunit;

namespace Meziantou.AspNetCore.Components.LogViewer.Tests;

public class LogHighlighterTests
{
    private static string Highlight(string str, params ILogHighlighter[] logHighlighters)
    {
        return LogHighlighter.Highlight(str, logHighlighters, "scope").Value;
    }

    [Fact]
    public void SingleMatchInMiddle()
    {
        var result = Highlight("a http://test.com b", new UrlLogHighlighter());
        result.Should().Be("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a> b");
    }

    [Fact]
    public void SingleMatchAtEnd()
    {
        var result = Highlight("a http://test.com", new UrlLogHighlighter());
        result.Should().Be("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a>");
    }

    [Fact]
    public void SingleMatchAtStart()
    {
        var result = Highlight("http://test.com b", new UrlLogHighlighter());
        result.Should().Be("<a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a> b");
    }

    [Fact]
    public void MultipleMatches()
    {
        var result = Highlight("a http://test.com 'sample' b", new UrlLogHighlighter(), new QuoteLogHighlighter());
        result.Should().Be("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a> &#x27;<span scope class='log-message-match'>sample</span>&#x27; b");
    }
}
