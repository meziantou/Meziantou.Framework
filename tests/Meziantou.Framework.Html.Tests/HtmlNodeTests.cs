using System.Diagnostics;

namespace Meziantou.Framework.Html.Tests;

public class HtmlNodeTests
{
    [Fact]
    public void HtmlNode_InnerText()
    {
        var doc = new HtmlDocument();
        doc.AppendChild(doc.CreateText("abc"));
        Assert.Equal("abc", doc.InnerText);
    }

    [Fact]
    public void HtmlNode_InnerText_CombineValues()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("abc<p>def</p>");
        Assert.Equal("abcdef", doc.InnerText);
    }

    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedAfterADescendantChanges()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p><span>a</span></p></div>");
        Assert.Equal("<div><p><span>a</span></p></div>", doc.InnerHtml);
        Assert.Equal("a", doc.InnerText);

        doc.SelectSingleNode("//span")!.InnerText = "b";

        Assert.Equal("<div><p><span>b</span></p></div>", doc.InnerHtml);
        Assert.Equal("b", doc.InnerText);
    }

    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedAfterADescendantIsRemoved()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p>a</p><p>b</p></div>");
        Assert.Equal("<div><p>a</p><p>b</p></div>", doc.InnerHtml);

        Assert.True(doc.SelectSingleNode("//p")!.Remove());

        Assert.Equal("<div><p>b</p></div>", doc.InnerHtml);
    }

    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedAfterAnAttributeChanges()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p class='a'>x</p></div>");
        Assert.Equal("<div><p class='a'>x</p></div>", doc.InnerHtml);

        doc.SelectSingleNode("//p")!.Attributes[0].Value = "b";

        Assert.Equal("<div><p class='b'>x</p></div>", doc.InnerHtml);
    }

    // A node that has been removed no longer shares the version its descendants report their changes against,
    // so it must not keep serving what it computed before they changed.
    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedOnADetachedSubtree()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p><span>a</span></p></div>");

        var p = doc.SelectSingleNode("//p")!;
        Assert.True(p.Remove());
        Assert.Equal("<p><span>a</span></p>", p.OuterHtml);

        p.SelectSingleNode("span")!.InnerText = "b";

        Assert.Equal("<p><span>b</span></p>", p.OuterHtml);
    }

    // Every change used to clear the cached serializations of each ancestor of the changed node. Parsing adds
    // one node per level of nesting, so loading a deeply nested document was quadratic in its depth: this input
    // took about 11 seconds. The budget is far below that and far above the tenth of a second it now needs.
    [Fact]
    public void HtmlNode_LoadHtml_DeeplyNestedDocumentIsNotQuadratic()
    {
        const int Depth = 20_000;
        var html = string.Concat(Enumerable.Repeat("<div>", Depth)) + "x" + string.Concat(Enumerable.Repeat("</div>", Depth));

        var stopwatch = Stopwatch.StartNew();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        stopwatch.Stop();

        Assert.Equal(html, doc.InnerHtml);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Loading {Depth} nested elements took {stopwatch.Elapsed}");
    }

    [Fact]
    public void HtmlNode_ParentElement_Null()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<p>def</p>");
        var node = doc.SelectSingleNode("/p");
        Assert.NotNull(node);
        Assert.Null(node!.ParentElement);
    }

    [Fact]
    public void HtmlNode_ParentElement()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<p>def</p>");
        var node = doc.SelectSingleNode("/p/node()");
        Assert.NotNull(node);
        Assert.Equal("p", node!.ParentElement!.Name);
    }
}
