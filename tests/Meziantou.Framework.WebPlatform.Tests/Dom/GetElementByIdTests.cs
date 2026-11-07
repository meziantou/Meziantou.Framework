using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class GetElementByIdTests
{
    [Fact]
    public void Duplicates_ReturnsFirstInTreeOrder()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=x class=a></div><p><span id=x class=b></span></p>");
        Assert.Equal("a", document.GetElementById("x")!.ClassName);

        // Inserting an element with the same ID before the first one
        var first = document.CreateElement("em");
        first.Id = "x";
        document.Body!.Prepend(first);
        Assert.Same(first, document.GetElementById("x"));

        first.Remove();
        Assert.Equal("a", document.GetElementById("x")!.ClassName);
    }

    [Fact]
    public void IdChanges()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=x></div>");
        var div = document.GetElementById("x")!;
        div.Id = "y";
        Assert.Null(document.GetElementById("x"));
        Assert.Same(div, document.GetElementById("y"));
        div.RemoveAttribute("id");
        Assert.Null(document.GetElementById("y"));
        div.SetAttribute("id", "z");
        Assert.Same(div, document.GetElementById("z"));
    }

    [Fact]
    public void Subtrees()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=container></div>");
        var detached = document.CreateElement("div");
        detached.InnerHtml = "<p><span id=inner></span></p>";
        Assert.Null(document.GetElementById("inner"));

        document.GetElementById("container")!.AppendChild(detached);
        Assert.NotNull(document.GetElementById("inner"));

        // Changing the ID of an element in the document after insertion
        document.GetElementById("inner")!.Id = "renamed";
        Assert.NotNull(document.GetElementById("renamed"));

        detached.Remove();
        Assert.Null(document.GetElementById("renamed"));

        // moveBefore() keeps the element in the map
        document.Body!.Append(detached);
        var target = document.CreateElement("section");
        document.Body.Append(target);
        target.MoveBefore(detached, null);
        Assert.Same(detached.FirstElementChild!.FirstElementChild, document.GetElementById("renamed"));
    }

    [Fact]
    public void ShadowTreesAndTemplates()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=host></div><template><span id=t></span></template>");
        var host = document.GetElementById("host")!;
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<b id=s></b>";
        Assert.Null(document.GetElementById("s"));
        Assert.NotNull(shadowRoot.GetElementById("s"));
        Assert.Null(document.GetElementById("t"));

        // The shadow tree keeps its IDs when the host is disconnected
        host.Remove();
        Assert.NotNull(shadowRoot.GetElementById("s"));

        // ... and when the host is adopted by a document that never used getElementById()
        var otherDocument = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        otherDocument.Body!.AppendChild(host);
        shadowRoot.GetElementById("s")!.Id = "s2";
        ((Element)shadowRoot.AppendChild(otherDocument.CreateElement("i"))).Id = "s3";
        Assert.NotNull(shadowRoot.GetElementById("s2"));
        Assert.NotNull(shadowRoot.GetElementById("s3"));
    }
}
