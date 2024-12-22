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
        Assert.Equal("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a> b", result);
    }

    [Fact]
    public void SingleMatchAtEnd()
    {
        var result = Highlight("a http://test.com", new UrlLogHighlighter());
        Assert.Equal("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a>", result);
    }

    [Fact]
    public void SingleMatchAtStart()
    {
        var result = Highlight("http://test.com b", new UrlLogHighlighter());
        Assert.Equal("<a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a> b", result);
    }

    [Fact]
    public void MultipleMatches()
    {
        var result = Highlight("a http://test.com 'sample' b", new UrlLogHighlighter(), new QuoteLogHighlighter());
        Assert.Equal("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a> &#x27;<span scope class='log-message-match'>sample</span>&#x27; b", result);
    }
}
