using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class LegacyCollectionTests
{
    [Fact]
    public void DocumentAll()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><a name=foo></a><div name=foo></div><p id=foo></p><img name=bar>");
        var all = document.All;
        Assert.Same(all, document.All);
        Assert.Equal(7u, all.Length); // html, head, body, a, div, p, img
        Assert.Equal("HTML", all[0u]!.TagName);

        // a and p match "foo", div is not an all-named element
        var foo = all.NamedItem("foo")!.Value.HtmlCollection!;
        Assert.Equal(2u, foo.Length);
        Assert.Equal("IMG", all["bar"]!.Value.Element!.TagName);
        Assert.Equal("HTML", all.Item("0")!.Value.Element!.TagName);
        Assert.Null(all.Item());
        Assert.Null(all.Item("100"));
        Assert.Null(all.NamedItem(""));
        Assert.Equal(["foo", "bar"], all.GetSupportedPropertyNames());
    }

    [Fact]
    public void HtmlCollection_Live()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li class=a>1</li><li>2</li></ul><table><tr><td>x</td></tr></table>");
        var ul = document.QuerySelector("ul")!;
        var children = ul.Children;
        var items = document.GetElementsByTagName("li");
        var byClass = document.GetElementsByClassName("a");
        var rows = ((HtmlTableElement)document.QuerySelector("table")!).Rows;
        Assert.Equal(2u, children.Length);
        Assert.Equal(2u, items.Length);
        Assert.Equal(1u, byClass.Length);
        Assert.Equal(1u, rows.Length);

        // The changes of the attributes and of the texts only update the collections that depend on them
        children[1u]!.ClassName = "a";
        children[0u]!.TextContent = "one";
        Assert.Equal(2u, byClass.Length);
        Assert.Equal(["one", "2"], items.Select(item => item.TextContent));

        // The insertions and the removals update all of them
        ul.AppendChild(document.CreateElement("li"));
        Assert.Equal(3u, children.Length);
        Assert.Equal(3u, items.Length);
        ul.FirstElementChild!.Remove();
        Assert.Equal(["2", ""], children.Select(item => item.TextContent));
        Assert.Equal(["2", ""], items.Select(item => item.TextContent));
        Assert.Equal(1u, byClass.Length);
        document.QuerySelector("tbody")!.AppendChild(document.CreateElement("tr"));
        Assert.Equal(2u, rows.Length);
        ul.AppendChild(document.CreateTextNode("text"));
        Assert.Equal(2u, children.Length);

        // An element of another document
        var other = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        other.Body!.AppendChild(ul);
        Assert.Equal(2u, children.Length);
        Assert.Equal(0u, items.Length);
        ul.AppendChild(other.CreateElement("li"));
        Assert.Equal(3u, children.Length);
    }

    [Fact]
    public void DomStringList()
    {
        var list = new DomStringList(["a", "b"]);
        Assert.Equal(2u, list.Length);
        Assert.Equal("b", list.Item(1));
        Assert.Null(list[2u]);
        Assert.True(list.Contains("a"));
        Assert.False(list.Contains("c"));
    }

    [Fact]
    public void DomRectList()
    {
        var rect = new DomRect(1, 2, 3, 4);
        var list = new DomRectList([rect]);
        Assert.Equal(1u, list.Length);
        Assert.Same(rect, list.Item(0));
        Assert.Null(list[1u]);
    }

    [Fact]
    public void DomQuad_GetBounds()
    {
        var quad = DomQuad.FromRect(new DomRectInit { X = 10, Y = 20, Width = -5, Height = 8 });
        var bounds = quad.GetBounds();
        Assert.Equal(5, bounds.X);
        Assert.Equal(20, bounds.Y);
        Assert.Equal(5, bounds.Width);
        Assert.Equal(8, bounds.Height);

        var copy = DomQuad.FromQuad(quad.ToJson());
        Assert.Equal(quad.P3.X, copy.P3.X);
        Assert.Equal(quad.P3.Y, copy.P3.Y);
    }
}
