using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The positional and streaming HTML insertion methods (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#html-insertion-methods, https://github.com/whatwg/html/pull/12758).</summary>
public sealed class HtmlInsertionTests
{
    private static Element CreateContainer(string html = "<span>a</span>")
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div>" + html + "</div>");
        return document.QuerySelector("div")!;
    }

    [Fact]
    public void PositionalMethods_InsertAtTheirPosition()
    {
        var container = CreateContainer();
        var span = container.FirstElementChild!;
        container.AppendHtml("<b>append</b>");
        container.PrependHtml("<i>prepend</i>");
        span.BeforeHtmlUnsafe("<u>before</u>");
        span.AfterHtmlUnsafe("<s>after</s>");
        Assert.Equal("<i>prepend</i><u>before</u><span>a</span><s>after</s><b>append</b>", container.InnerHtml);

        span.ReplaceWithHtml("<em>replaced</em><script>x</script>");
        Assert.Equal("<i>prepend</i><u>before</u><em>replaced</em><s>after</s><b>append</b>", container.InnerHtml);
    }

    [Fact]
    public void PositionalMethods_OfATextNode()
    {
        var container = CreateContainer("text");
        ((Text)container.FirstChild!).AfterHtml("<b>b</b>");
        Assert.Equal("text<b>b</b>", container.InnerHtml);
    }

    [Fact]
    public void ChildNodeMethods_WithoutParent_DoNothing_AndThrowForATemplateChild()
    {
        var container = CreateContainer();
        var detached = container.OwnerDocument!.CreateElement("p");
        detached.BeforeHtml("<b>b</b>");
        Assert.Null(detached.ParentNode);

        var template = (HtmlTemplateElement)container.OwnerDocument.CreateElement("template");
        var child = template.AppendChild(container.OwnerDocument.CreateElement("p"));
        var exception = Assert.Throws<DomException>(() => ((Element)child).AfterHtml("<b>b</b>"));
        Assert.Equal("HierarchyRequestError", exception.Name);

        var root = container.OwnerDocument.DocumentElement!;
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => root.BeforeHtmlUnsafe("x")).Name);
    }

    [Fact]
    public void InsertionMethods_IntoOrNextToAChildOfAnHtmlElement_ThrowHierarchyRequestError()
    {
        var document = CreateContainer().OwnerDocument!;
        var html = document.DocumentElement!;
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => html.AppendHtml("<p>a</p>")).Name);
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => html.PrependHtmlUnsafe("<p>a</p>")).Name);
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => html.StreamAppendHtml()).Name);
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => document.Body!.AfterHtml("<p>a</p>")).Name);
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => document.Head!.ReplaceWithHtmlUnsafe("<p>a</p>")).Name);
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => document.Body!.StreamBeforeHtml()).Name);

        var detached = document.CreateElement("html");
        var comment = detached.AppendChild(document.CreateComment("c"));
        Assert.Equal("HierarchyRequestError", Assert.Throws<DomException>(() => ((Comment)comment).BeforeHtml("<p>a</p>")).Name);
        Assert.Equal(2, html.ChildNodes.Count);
        Assert.Single(detached.ChildNodes);

        html.SetHtmlUnsafe("<head></head><body><p>new</p></body>");
        Assert.Equal("<p>new</p>", document.Body!.InnerHtml);
    }

    [Fact]
    public void AppendHtml_OfATemplate_InsertsIntoItsContents()
    {
        var container = CreateContainer("<template><p>a</p></template>");
        var template = (HtmlTemplateElement)container.FirstElementChild!;
        template.AppendHtml("<p>b</p>");
        Assert.Equal("<p>a</p><p>b</p>", template.InnerHtml);
    }

    [Fact]
    public void TemplateFor_PatchesTheMarkersOfTheFragmentOnly()
    {
        var container = CreateContainer("<p><?start name=\"x\">old<?end></p>");
        container.AppendHtmlUnsafe("<div><?marker name=\"x\"></div><template for=\"x\">new</template>");
        Assert.Equal("<p><?start name=\"x\"?>old<?end ?></p><div>new</div>", container.InnerHtml);
    }

    [Fact]
    public void StreamAppendHtml_ParsesTheChunksAsTheyAreWritten()
    {
        var container = CreateContainer();
        var writer = container.StreamAppendHtmlUnsafe();
        writer.Write("<ul><li>first");
        Assert.Equal("<span>a</span><ul><li>first</li></ul>", container.InnerHtml);

        writer.Write("</li><li>second</li>");
        writer.Close();
        Assert.Equal("<span>a</span><ul><li>first</li><li>second</li></ul>", container.InnerHtml);
        Assert.True(writer.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => writer.Write("x"));
    }

    [Fact]
    public void StreamHtml_ReplacesTheChildren_AndSanitizes()
    {
        var container = CreateContainer();
        var writer = container.StreamHtml();
        Assert.Equal("", container.InnerHtml);

        writer.Write("<p onclick=x>a<scr");
        writer.Write("ipt>alert(1)</script>b</p>");
        writer.Close();
        Assert.Equal("<p>ab</p>", container.InnerHtml);
    }

    [Fact]
    public void StreamBeforeHtml_InsertsBeforeTheNode_AndRejectsAMovedReferenceChild()
    {
        var container = CreateContainer("<span>a</span><span>b</span>");
        var second = container.LastElementChild!;
        var writer = second.StreamBeforeHtml();
        writer.Write("<i>x</i>");
        Assert.Equal("<span>a</span><i>x</i><span>b</span>", container.InnerHtml);

        container.OwnerDocument!.Body!.AppendChild(second);
        var exception = Assert.Throws<DomException>(() => writer.Write("<i>y</i>"));
        Assert.Equal("HierarchyRequestError", exception.Name);
        Assert.True(writer.IsCompleted);
    }

    [Fact]
    public void StreamReplaceWithHtml_RemovesTheNode()
    {
        var container = CreateContainer("<span>a</span><span>b</span>");
        var writer = container.FirstElementChild!.StreamReplaceWithHtml();
        Assert.Equal("<span>b</span>", container.InnerHtml);

        writer.Write("<b>new</b>");
        writer.Close();
        Assert.Equal("<b>new</b><span>b</span>", container.InnerHtml);
    }

    [Fact]
    public void StreamAppendHtml_PatchesTheTemplateForMarkersOfTheTarget()
    {
        var container = CreateContainer("<p><?start name=\"x\">placeholder<?end></p>");
        var writer = container.StreamAppendHtmlUnsafe();
        writer.Write("<template for=\"x\">new ");
        writer.Write("content</template>");
        writer.Close();
        Assert.Equal("<p>new content</p>", container.InnerHtml);
    }

    [Fact]
    public void StreamHtml_IntoAScriptElement_Throws()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><script></script>");
        var script = document.QuerySelector("script")!;
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => script.StreamAppendHtmlUnsafe()).Name);
    }

    [Fact]
    public void StreamAppendHtml_Abort_StopsParsing()
    {
        var container = CreateContainer("");
        var writer = container.StreamAppendHtmlUnsafe();
        writer.Write("<p>a");
        writer.Abort();
        Assert.True(writer.IsCompleted);
        Assert.Equal("<p>a</p>", container.InnerHtml);
    }

    [Fact]
    public void Documentation_ParseFragment()
    {
        #region WebPlatform_ParseFragment
        var document = HtmlDocumentParser.ParseDocument("<table><tr id=row></tr></table>");
        var row = document.GetElementById("row")!;

        // The markup is parsed as the content of the context element: here, cells of a table row
        var fragment = new HtmlDocumentParser().ParseFragment("<td>a<td>b", row);
        row.Append(fragment);
        Assert.Equal("<td>a</td><td>b</td>", row.InnerHtml);

        // The same, in one step
        row.InnerHtml = "<td>c<td>d";
        row.InsertAdjacentHtml("beforeend", "<td>e");
        Assert.Equal("<td>c</td><td>d</td><td>e</td>", row.InnerHtml);
        #endregion
    }
}
