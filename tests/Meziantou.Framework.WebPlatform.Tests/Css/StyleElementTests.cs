using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class StyleElementTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var document = _contexts.Create().Navigate(_ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static (HtmlStyleElement Style, Func<int> Calls) CreateStyle(Document document, string css = "div { color: red }")
    {
        var style = (HtmlStyleElement)document.CreateElement("style");
        style.TextContent = css;
        var calls = 0;
        style.AddEventListener("load", _ => calls++);
        return (style, () => calls);
    }

    [Fact]
    public void LoadEvent_IsFiredWithoutAccessingTheSheet()
    {
        var document = Parse("");
        var (style, calls) = CreateStyle(document);
        document.Head!.AppendChild(style);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, calls());

        // Computing the style sheet does not fire another event
        Assert.Equal(1u, style.Sheet!.CssRules.Length);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, calls());
    }

    [Fact]
    public void LoadEvent_IsFiredForEachUpdateOfTheStyleBlock()
    {
        var document = Parse("");
        var (style, calls) = CreateStyle(document);
        document.Head!.AppendChild(style);
        style.AppendChild(document.CreateTextNode("p { color: blue }"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, calls());

        style.TextContent = "p { color: green }";
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(2, calls());

        _ = style.Sheet;
        style.TextContent = "p { color: red }";
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(3, calls());
    }

    [Fact]
    public void LoadEvent_IsNotFiredWithoutStyleSheet()
    {
        var document = Parse("");
        var (removed, removedCalls) = CreateStyle(document);
        document.Head!.AppendChild(removed);
        removed.Remove();

        var (other, otherCalls) = CreateStyle(document);
        other.SetAttribute("type", "text/plain");
        document.Head!.AppendChild(other);

        var (detached, detachedCalls) = CreateStyle(document);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(0, removedCalls());
        Assert.Equal(0, otherCalls());
        Assert.Equal(0, detachedCalls());
        Assert.Null(other.Sheet);
        Assert.Null(detached.Sheet);
    }

    [Fact]
    public void LoadEvent_ParsedStyleElements()
    {
        var calls = 0;
        var document = _contexts.Create().Navigate(_ => "<style>div { color: red }</style><style type=text/plain></style>");
        foreach (var style in document.QuerySelectorAll("style"))
        {
            style.AddEventListener("load", _ => calls++);
        }

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void LoadEvent_WithoutBrowsingContext_IsNotFired()
    {
        // A document parsed without a browsing context has no event loop: the load event is never fired
        var document = HtmlDocumentParser.ParseDocument("");
        var (style, calls) = CreateStyle(document);
        document.Head!.AppendChild(style);
        Assert.NotNull(style.Sheet);
        Assert.Equal(0, calls());
    }
}
