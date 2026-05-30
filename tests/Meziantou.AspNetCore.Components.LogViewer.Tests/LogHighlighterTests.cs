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

    [Fact]
    public void MatchLocalhostUrl()
    {
        var result = Highlight("a http://localhost:5000/path b", new UrlLogHighlighter());
        Assert.Equal("a <a scope class='log-message-match-link' target='_blank' href='http://localhost:5000/path'>http://localhost:5000/path</a> b", result);
    }

    [Fact]
    public void MatchUrlWithTrailingPunctuation()
    {
        var result = Highlight("a http://test.com, b", new UrlLogHighlighter());
        Assert.Equal("a <a scope class='log-message-match-link' target='_blank' href='http://test.com'>http://test.com</a>, b", result);
    }

    [Fact]
    public void ParseAnsiColor()
    {
        var result = Highlight($"a \u001b[31mred\u001b[0m b");
        Assert.Equal("a <span scope class='log-ansi' style='color: rgb(205, 49, 49);'>red</span> b", result);
    }

    [Fact]
    public void ParseAnsiStylesWithPartialReset()
    {
        var result = Highlight($"\u001b[1;4mA\u001b[24mB\u001b[0m");
        Assert.Equal("<span scope class='log-ansi log-ansi-bold log-ansi-underline'>A</span><span scope class='log-ansi log-ansi-bold'>B</span>", result);
    }

    [Fact]
    public void ParseAnsiTrueColor()
    {
        var result = Highlight($"\u001b[38;2;100;200;50mX\u001b[0m");
        Assert.Equal("<span scope class='log-ansi' style='color: rgb(100, 200, 50);'>X</span>", result);
    }

    [Fact]
    public void IgnoreInvalidAnsiColorValues()
    {
        var result = Highlight($"\u001b[38;2;300;1;1mX");
        Assert.Equal("X", result);
    }

    [Fact]
    public void MatchUrlAcrossAnsiColorBoundary()
    {
        var result = Highlight($"a http://te\u001b[31mst.com/path\u001b[0m b", new UrlLogHighlighter());
        Assert.Equal("a <a scope class='log-message-match-link' target='_blank' href='http://test.com/path'>http://te<span scope class='log-ansi' style='color: rgb(205, 49, 49);'>st.com/path</span></a> b", result);
    }

    [Fact]
    public void LongInvalidUrlDoesNotTimeout()
    {
        var input = "http://test.com:" + new string('9', 200_000);
        var highlighter = new UrlLogHighlighter();
        var count = 0;

        var exception = Record.Exception(() =>
        {
            foreach (var _ in highlighter.Process(input))
            {
                count++;
            }
        });

        Assert.Null(exception);
        Assert.Equal(0, count);
    }
}
