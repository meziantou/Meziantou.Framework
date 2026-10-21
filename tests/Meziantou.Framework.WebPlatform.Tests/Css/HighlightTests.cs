using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>
/// The CSS Custom Highlight API (https://drafts.csswg.org/css-highlight-api-1/) and the highlight cascade of the highlight
/// pseudo-elements (https://drafts.csswg.org/css-pseudo-4/#highlight-cascade).
/// </summary>
public sealed class HighlightTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html) => _contexts.Create(new BrowsingContextOptions()).Navigate(_ => html);

    private static string Get(Document document, string selector, string pseudoElement, string property) =>
        document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!, pseudoElement).GetPropertyValue(property);

    // ----------------------------------------------------------------------------------------------------------------
    // Highlight and HighlightRegistry
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Highlight_IsASetOfRanges()
    {
        var document = Parse("<p>text</p>");
        var range1 = document.CreateRange();
        var range2 = new StaticRange(new StaticRangeInit { StartContainer = document.Body!, StartOffset = 0, EndContainer = document.Body!, EndOffset = 0 });
        var highlight = new Highlight(range1, range1);
        Assert.Single(highlight);
        Assert.Equal(0, highlight.Priority);
        Assert.Equal(HighlightType.Highlight, highlight.Type);

        // As for a JavaScript Set, an enumeration sees the ranges added and deleted while it is in progress
        using var enumerator = highlight.GetEnumerator();
        highlight.Add(range2);
        highlight.Delete(range1);
        Assert.True(enumerator.MoveNext());
        Assert.Same(range2, enumerator.Current);
        Assert.False(enumerator.MoveNext());

        highlight.Clear();
        Assert.Empty(highlight);
        Assert.False(highlight.Has(range2));
    }

    [Fact]
    public void HighlightRegistry_IsAMapOfHighlights()
    {
        var document = Parse("");
        var registry = document.Highlights;
        var a = new Highlight();
        var b = new Highlight();
        var c = new Highlight();
        Assert.Same(registry, registry.Set("a", a).Set("b", b));

        // Replacing the highlight of a name keeps its position
        registry.Set("a", c);
        Assert.Equal(["a", "b"], registry.Select(entry => entry.Key));
        Assert.Same(c, registry.Get("a"));
        Assert.Null(registry.Get("c"));
        Assert.True(registry.Delete("a"));
        Assert.False(registry.Delete("a"));
        Assert.False(registry.Has("a"));
        Assert.Single(registry);
    }

    [Fact]
    public void CssHighlights_IsTheRegistryOfTheDocumentOfTheWindow()
    {
        using var engine = TestScriptEngines.CreateDisposable();
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = engine });
        var document = context.Navigate("https://example.com/", _ => """
            <p id=p>text</p>
            <script>
              const range = new Range();
              range.selectNodeContents(p);
              const highlight = new Highlight(range);
              highlight.priority = 2;
              highlight.type = 'unknown';
              CSS.highlights.set('a', highlight).set('b', new Highlight());
              const keys = [...CSS.highlights.keys()].join();
              let throws = false;
              try { CSS.highlights.set('c', {}); } catch (e) { throws = e instanceof TypeError; }
              document.title = [keys, CSS.highlights.size, CSS.highlights.get('a') === highlight, highlight.has(range), highlight.type, throws].join(' ');
            </script>
            """);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("a,b 2 true true highlight true", document.Title);
        Assert.Equal(2, document.Highlights.Get("a")!.Priority);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Highlight cascade
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void HighlightPseudoElements_InheritFromTheHighlightPseudoElementOfTheParent()
    {
        var document = Parse("""
            <style>
              #parent::selection { background-color: green; color: lime }
              #parent::highlight(foo) { text-decoration-line: underline }
            </style>
            <div id=parent><span id=child></span></div>
            """);

        // All the applicable properties inherit, even the properties that are not inherited (background-color)
        Assert.Equal("rgb(0, 128, 0)", Get(document, "#child", "::selection", "background-color"));
        Assert.Equal("rgb(0, 255, 0)", Get(document, "#child", "::selection", "color"));
        Assert.Equal("underline", Get(document, "#child", "::highlight(foo)", "text-decoration-line"));
        Assert.Equal("none", Get(document, "#child", "::highlight(bar)", "text-decoration-line"));
    }

    [Fact]
    public void HighlightPseudoElements_UseTheFontOfTheOriginatingElement()
    {
        var document = Parse("""
            <style>
              div { font-size: 10px }
              ::selection { font-size: 40px; text-shadow: green 1em 0; --x: 1 }
              span { --x: 2 }
            </style>
            <div><span id=target></span></div>
            """);

        // The declarations of the properties that do not apply are ignored: the values are the ones of the originating element
        Assert.Equal("10px", Get(document, "#target", "::selection", "font-size"));
        Assert.Equal("rgb(0, 128, 0) 10px 0px 0px", Get(document, "#target", "::selection", "text-shadow"));
        Assert.Equal("1", Get(document, "#target", "::selection", "--x"));
    }

    [Fact]
    public void HighlightPseudoElements_CurrentColorIsTheColorOfTheOriginatingElement()
    {
        var document = Parse("""
            <style>
              #parent { color: blue }
              #parent::selection { background-color: currentcolor }
              #child { color: lime }
            </style>
            <div id=parent><span id=child></span></div>
            """);

        // The inherited color of the root is currentcolor, resolved with the color of each originating element
        Assert.Equal("rgb(0, 0, 255)", Get(document, "#parent", "::selection", "color"));
        Assert.Equal("rgb(0, 255, 0)", Get(document, "#child", "::selection", "color"));
        Assert.Equal("rgb(0, 255, 0)", Get(document, "#child", "::selection", "background-color"));
    }

    [Fact]
    public void SearchText_CurrentNeverMatches()
    {
        var document = Parse("""
            <style>
              #target::search-text:not(:current) { background-color: green }
              #target::search-text:current { background-color: red }
            </style>
            <div id=target></div>
            """);

        // There is no active search result
        Assert.Equal("rgb(0, 128, 0)", Get(document, "#target", "::search-text", "background-color"));
    }

    [Theory]
    [InlineData("::highlight(foo)", "rgb(0, 128, 0)", "rgb(255, 255, 0)")]
    [InlineData("::highlight(bar)", "rgb(255, 0, 0)", "rgb(255, 255, 0)")]
    [InlineData("::highlight(\\*)", "rgb(0, 0, 255)", "rgb(255, 255, 0)")]
    public void UniversalHighlight_AppliesToEveryCustomHighlight(string pseudoElement, string color, string backgroundColor)
    {
        // ::highlight(*) has a zero specificity: the rules of the named highlights win
        var document = Parse("""
            <style>
              #target::highlight(foo) { color: green }
              #target::highlight(\*) { color: blue }
              #target::highlight(*) { color: red; background-color: yellow }
            </style>
            <div id=target></div>
            """);

        Assert.Equal(color, Get(document, "#target", pseudoElement, "color"));
        Assert.Equal(backgroundColor, Get(document, "#target", pseudoElement, "background-color"));
        Assert.Equal(0u, document.DefaultView!.GetComputedStyle(document.QuerySelector("#target")!, "::highlight(*)").Length);
    }

    [Fact]
    public void HighlightsFromPoint_ReturnsTheHighlightsOfTheRangesAtThePoint()
    {
        var document = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } })
            .Navigate(_ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/10px Ahem }</style><span id=s>0123456789</span>");
        var text = document.GetElementById("s")!.FirstChild!;
        var range1 = document.CreateRange();
        range1.SetStart(text, 2);
        range1.SetEnd(text, 10);
        var range2 = document.CreateRange();
        range2.SetStart(text, 5);
        range2.SetEnd(text, 10);
        var highlight1 = new Highlight(range1);
        var highlight2 = new Highlight(range2);
        document.Highlights.Set("h1", highlight1).Set("h2", highlight2);

        Assert.Empty(document.Highlights.HighlightsFromPoint(15, 5));
        var result = Assert.Single(document.Highlights.HighlightsFromPoint(35, 5));
        Assert.Same(highlight1, result.Highlight);
        Assert.Equal([range1], result.Ranges);

        // The highlight registered last first, unless the priorities differ
        Assert.Equal([highlight2, highlight1], document.Highlights.HighlightsFromPoint(75, 5).Select(r => r.Highlight));
        highlight1.Priority = 1;
        Assert.Equal([highlight1, highlight2], document.Highlights.HighlightsFromPoint(75, 5).Select(r => r.Highlight));

        // Outside of the text and of the viewport
        Assert.Empty(document.Highlights.HighlightsFromPoint(105, 5));
        Assert.Empty(document.Highlights.HighlightsFromPoint(-1, 5));
        Assert.Empty(document.Highlights.HighlightsFromPoint(75, 601));
    }
}
