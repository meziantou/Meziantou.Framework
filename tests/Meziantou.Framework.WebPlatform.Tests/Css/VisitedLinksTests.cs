using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>
/// The visited links (https://drafts.csswg.org/selectors-4/#link): the history of the browsing contexts
/// (<see cref="BrowsingContextOptions.VisitedLinks"/>), the painting of the colors of the <c>:visited</c> rules, and what the
/// scripts observe (every link is unvisited).
/// </summary>
public sealed class VisitedLinksTests : IDisposable
{
    private const string PageUrl = "https://www.example.com/dir/page.html";
    private const string Styles = "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } a { text-decoration: none } :link { color: rgb(0, 0, 255) } :visited { color: rgb(255, 0, 0) }</style>";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private static DisplayColor Rgb(byte r, byte g, byte b) => DisplayColor.FromRgb(r, g, b);

    private Document Load(string html, VisitedLinkStore? store, string url = PageUrl, BrowsingContext? context = null)
    {
        context ??= _contexts.Create(new BrowsingContextOptions { VisitedLinks = store });
        var document = context.Navigate(url, _ => Styles + html);
        context.EventLoop.RunUntilIdle();
        return document;
    }

    private static DisplayColor[] GetGlyphColors(Document document)
        => [.. document.GetDisplayList()!.Items.ToArray().OfType<DrawGlyphsItem>().Select(item => item.Paint.Color)];

    private static DrawRectItem[] GetRects(Document document, string id)
        => [.. document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Where(item => (item.Source as Element)?.GetAttributeValue("id") == id)];

    private static string GetComputed(Document document, string selector, string property)
        => document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!).GetPropertyValue(property);

    [Fact]
    public void WithoutStore_NoLinkIsVisited()
    {
        var document = Load("<a href=''>X</a>", store: null);
        Assert.Equal([Rgb(0, 0, 255)], GetGlyphColors(document));
    }

    [Fact]
    public void LinkToTheDocument_IsPaintedAsVisited()
    {
        var document = Load("<a href=''>X</a><a href='other.html'>X</a><a href='#'>X</a>", new InMemoryVisitedLinkStore());
        Assert.Equal([Rgb(255, 0, 0), Rgb(0, 0, 255), Rgb(0, 0, 255)], GetGlyphColors(document));
    }

    [Fact]
    public void Scripts_ObserveUnvisitedLinks()
    {
        var document = Load("<div id=parent><a id=link href=''>X</a><span id=next>X</span></div>", new InMemoryVisitedLinkStore());
        var link = document.GetElementById("link")!;
        Assert.Equal("rgb(0, 0, 255)", GetComputed(document, "#link", "color"));
        Assert.True(link.Matches(":link"));
        Assert.False(link.Matches(":visited"));
        Assert.True(link.Matches(":any-link"));
        Assert.Null(document.QuerySelector(":visited"));
        Assert.Same(link, document.QuerySelector(":link"));
        Assert.True(document.GetElementById("parent")!.Matches(":has(:link)"));
        Assert.False(document.GetElementById("parent")!.Matches(":has(:visited)"));
    }

    [Fact]
    public void OnlyTheColorsOfTheVisitedRulesApply()
    {
        var document = Load("<style>a:visited { font-size: 40px; display: block; margin-left: 50px; background-color: rgb(0, 255, 0); --c: rgb(1, 2, 3); outline: 3px solid rgb(0, 255, 0) } a { background-color: rgb(255, 255, 0); outline: 1px solid black }</style><a id=link href=''>X</a>", new InMemoryVisitedLinkStore());
        Assert.Equal("10px", GetComputed(document, "#link", "font-size"));
        Assert.Equal("inline", GetComputed(document, "#link", "display"));
        Assert.Equal("rgb(255, 255, 0)", GetComputed(document, "#link", "background-color"));
        Assert.Equal("1px", GetComputed(document, "#link", "outline-width"));
        Assert.Equal("", GetComputed(document, "#link", "--c"));

        // The background is painted with the visited color, where the unvisited style paints it
        var rects = GetRects(document, "link");
        Assert.Contains(rects, rect => rect.Paint.Color == Rgb(0, 255, 0) && rect.Rect.X == 0 && rect.Rect.Width == 10);
        Assert.DoesNotContain(rects, rect => rect.Paint.Color == Rgb(255, 255, 0));
    }

    [Fact]
    public void VisitedColor_HasTheAlphaOfTheUnvisitedColor()
    {
        var document = Load("<style>a:link { color: rgba(0, 0, 255, 0.5); background-color: transparent } a:visited { color: rgb(255, 0, 0); background-color: rgb(0, 255, 0) }</style><a id=link href=''>X</a>", new InMemoryVisitedLinkStore());
        Assert.Equal([new DisplayColor(1, 0, 0, 0.5f)], GetGlyphColors(document));

        // (the transparent background of the unvisited style is not painted)
        Assert.Empty(GetRects(document, "link"));
    }

    [Fact]
    public void Descendants_InheritTheVisitedColors()
    {
        var document = Load("<style>a:visited span { background-color: rgb(0, 255, 0) } span { background-color: rgb(255, 255, 0) } div { background-color: currentcolor; width: 10px; height: 10px }</style><a href=''><span id=inner>X</span><div id=block></div></a>", new InMemoryVisitedLinkStore());
        Assert.Equal([Rgb(255, 0, 0)], GetGlyphColors(document));
        Assert.Equal(Rgb(0, 255, 0), Assert.Single(GetRects(document, "inner")).Paint.Color);
        Assert.Equal(Rgb(255, 0, 0), Assert.Single(GetRects(document, "block")).Paint.Color);
        Assert.Equal("rgb(0, 0, 255)", GetComputed(document, "#block", "background-color"));
    }

    [Fact]
    public void Siblings_AreNotStyledByTheVisitedLinks()
    {
        var document = Load("<style>:visited + span, div:has(:visited) > span { color: rgb(0, 255, 0) } span { color: rgb(1, 2, 3) }</style><div><a href=''>X</a><span>X</span></div>", new InMemoryVisitedLinkStore());
        Assert.Equal([Rgb(255, 0, 0), Rgb(1, 2, 3)], GetGlyphColors(document));
    }

    [Fact]
    public void LinkBecomesVisited_WhenTheHistoryChanges()
    {
        var store = new InMemoryVisitedLinkStore();
        var document = Load("<a href='other.html'>X</a>", store);
        Assert.Equal([Rgb(0, 0, 255)], GetGlyphColors(document));

        store.Add(new VisitedLink("https://www.example.com/dir/other.html", "https://www.example.com", "https://example.com"));
        Assert.Equal([Rgb(255, 0, 0)], GetGlyphColors(document));

        store.Clear();
        Assert.Equal([Rgb(0, 0, 255)], GetGlyphColors(document));
    }

    [Fact]
    public void Navigations_AreRecorded()
    {
        var store = new InMemoryVisitedLinkStore();
        var context = _contexts.Create(new BrowsingContextOptions { VisitedLinks = store });
        Load("", store, context: context);
        Assert.True(store.IsVisited(new VisitedLink(PageUrl, "https://www.example.com", "https://example.com")));

        // A page of the same origin sees the visit; the pages of the other origins do not
        var document = Load("<a href='/dir/page.html'>X</a>", store, "https://www.example.com/second.html", context);
        Assert.Equal([Rgb(255, 0, 0)], GetGlyphColors(document));
        document = Load("<a href='" + PageUrl + "'>X</a>", store, "https://other.example.com/", context);
        Assert.Equal([Rgb(0, 0, 255)], GetGlyphColors(document));
        document = Load("<a href='" + PageUrl + "'>X</a>", store, "https://www.example.org/", context);
        Assert.Equal([Rgb(0, 0, 255)], GetGlyphColors(document));
    }

    [Fact]
    public void UnpartitionedStore_LinkIsVisitedInEveryDocument()
    {
        var store = new InMemoryVisitedLinkStore(partitioned: false);
        var context = _contexts.Create(new BrowsingContextOptions { VisitedLinks = store });
        Load("", store, context: context);
        var document = Load("<a href='" + PageUrl + "'>X</a>", store, "https://www.example.org/", context);
        Assert.Equal([Rgb(255, 0, 0)], GetGlyphColors(document));
    }

    [Fact]
    public void HostStore_ReceivesThePartitionOfTheLink()
    {
        var store = new RecordingStore();
        var document = Load("<a href='other.html#fragment'>X</a>", store);
        Assert.Contains(new VisitedLink(PageUrl, "https://www.example.com", "https://example.com"), store.Added);
        Assert.Equal([Rgb(255, 0, 0)], GetGlyphColors(document));
        Assert.Contains(new VisitedLink("https://www.example.com/dir/other.html#fragment", "https://www.example.com", "https://example.com"), store.Queried);
    }

    [Fact]
    public void BodyVlinkAttribute_SetsTheColorOfTheVisitedLinks()
    {
        var context = _contexts.Create(new BrowsingContextOptions { VisitedLinks = new InMemoryVisitedLinkStore() });
        var document = context.Navigate(PageUrl, _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style><body link='#00ff00' vlink='#ffff00'><a href=''>X</a><a href='other.html'>X</a>");
        context.EventLoop.RunUntilIdle();
        Assert.Equal([Rgb(255, 255, 0), Rgb(0, 255, 0)], GetGlyphColors(document));
    }

    private sealed class RecordingStore : VisitedLinkStore
    {
        public List<VisitedLink> Added { get; } = [];

        public List<VisitedLink> Queried { get; } = [];

        public override long Version => 0;

        public override bool IsVisited(VisitedLink link)
        {
            Queried.Add(link);
            return true;
        }

        public override void Add(VisitedLink link) => Added.Add(link);
    }
}
