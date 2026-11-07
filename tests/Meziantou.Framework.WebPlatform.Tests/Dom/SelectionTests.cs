using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class SelectionTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static Text GetText(Document document, string id) => (Text)document.GetElementById(id)!.FirstChild!;

    private static List<string> RecordSelectionChangeEvents(EventTarget target)
    {
        var events = new List<string>();
        target.AddEventListener("selectionchange", e => events.Add($"{e.Type} bubbles={e.Bubbles}"));
        return events;
    }

    [Fact]
    public void RenderedBoundaryPoints_AcrossShadowTrees()
    {
        // The anchor and the focus of setBaseAndExtent() in different trees are kept for the rendering, in shadow-including
        // tree order; the live range of the selection is collapsed at the focus
        var document = Load("<p id=a>outer</p><div id=host1></div><div id=host2></div>");
        Node outer = GetText(document, "a");
        var root1 = document.GetElementById("host1")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var root2 = document.GetElementById("host2")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        Node inner1 = root1.AppendChild(document.CreateTextNode("inner1"));
        Node inner2 = root2.AppendChild(document.CreateTextNode("inner2"));
        var selection = document.GetSelection()!;

        selection.SetBaseAndExtent(inner2, 3, inner1, 2);
        Assert.Equal((inner1, 2u, inner2, 3u), selection.GetRenderedBoundaryPoints());

        selection.SetBaseAndExtent(outer, 1, root2, 1);
        Assert.Equal((outer, 1u, (Node)root2, 1u), selection.GetRenderedBoundaryPoints());

        // The points are compared in shadow-including tree order: a shadow tree is before the children of its host
        Assert.Equal(AbstractRange.BoundaryPointPosition.Before, AbstractRange.CompareComposedPoints(outer, 5, inner1, 0));
        Assert.Equal(AbstractRange.BoundaryPointPosition.After, AbstractRange.CompareComposedPoints(inner2, 0, inner1, 6));
        Assert.Equal(AbstractRange.BoundaryPointPosition.Before, AbstractRange.CompareComposedPoints(inner1, 0, document.GetElementById("host1")!, 0));
        Assert.Equal(AbstractRange.BoundaryPointPosition.After, AbstractRange.CompareComposedPoints(document.Body!, 3, inner2, 0));

        // The points are forgotten when the selection changes
        selection.Collapse(outer, 2);
        Assert.Equal((outer, 2u, outer, 2u), selection.GetRenderedBoundaryPoints());
        selection.RemoveAllRanges();
        Assert.Null(selection.GetRenderedBoundaryPoints());
    }

    [Fact]
    public void GetSelection_RequiresBrowsingContext()
    {
        var document = Load("<p>text</p>");
        var selection = document.GetSelection();
        Assert.NotNull(selection);
        Assert.Same(selection, document.GetSelection());
        Assert.Same(selection, document.DefaultView!.GetSelection());
        Assert.Equal(0u, selection.RangeCount);
        Assert.Equal("None", selection.Type);
        Assert.Equal("none", selection.Direction);
        Assert.True(selection.IsCollapsed);

        Assert.Null(HtmlDocumentParser.ParseDocument("<p>text</p>").GetSelection());
    }

    [Fact]
    public void AddRange_KeepsReference()
    {
        var document = Load("<p id=p>hello world</p>");
        var selection = document.GetSelection()!;
        var text = GetText(document, "p");
        var range = document.CreateRange();
        range.SetStart(text, 0);
        range.SetEnd(text, 5);

        selection.AddRange(range);
        Assert.Same(range, selection.GetRangeAt(0));
        Assert.Equal("hello", selection.ToString());
        Assert.Equal("Range", selection.Type);
        Assert.Equal("forward", selection.Direction);
        Assert.Same(text, selection.AnchorNode);
        Assert.Equal(0u, selection.AnchorOffset);
        Assert.Equal(5u, selection.FocusOffset);

        // The range is not replaced: modifying it modifies the selection
        range.SetStart(text, 6);
        range.SetEnd(text, 11);
        Assert.Equal("world", selection.ToString());

        // A second range is ignored
        selection.AddRange(document.CreateRange());
        Assert.Same(range, selection.GetRangeAt(0));
        Assert.Throws<DomException>(() => selection.GetRangeAt(1));

        Assert.Throws<DomException>(() => selection.RemoveRange(document.CreateRange()));
        selection.RemoveRange(range);
        Assert.Equal(0u, selection.RangeCount);
    }

    [Fact]
    public void AddRange_IgnoresRangesOutsideTheDocument()
    {
        var document = Load("<p>text</p>");
        var selection = document.GetSelection()!;
        var range = document.CreateRange();
        range.SelectNodeContents(document.CreateElement("div"));

        selection.AddRange(range);
        Assert.Equal(0u, selection.RangeCount);
    }

    [Fact]
    public void SetBaseAndExtent_Backwards()
    {
        var document = Load("<p id=p>hello world</p>");
        var selection = document.GetSelection()!;
        var text = GetText(document, "p");

        selection.SetBaseAndExtent(text, 5, text, 0);
        Assert.Equal("backward", selection.Direction);
        Assert.Equal(5u, selection.AnchorOffset);
        Assert.Equal(0u, selection.FocusOffset);
        Assert.Equal(0u, selection.GetRangeAt(0).StartOffset);
        Assert.Equal(5u, selection.GetRangeAt(0).EndOffset);
        Assert.Equal("hello", selection.ToString());

        Assert.Throws<DomException>(() => selection.SetBaseAndExtent(text, 100, text, 0));
    }

    [Fact]
    public void CollapseAndExtend()
    {
        var document = Load("<p id=p>hello world</p>");
        var selection = document.GetSelection()!;
        var text = GetText(document, "p");

        Assert.Throws<DomException>(() => selection.Extend(text, 1));

        selection.Collapse(text, 8);
        Assert.Equal("Caret", selection.Type);
        Assert.True(selection.IsCollapsed);
        var collapsedRange = selection.GetRangeAt(0);

        selection.Extend(text, 2);
        Assert.NotSame(collapsedRange, selection.GetRangeAt(0));
        Assert.Equal("backward", selection.Direction);
        Assert.Equal(8u, selection.AnchorOffset);
        Assert.Equal(2u, selection.FocusOffset);
        Assert.Equal("llo wo", selection.ToString());

        selection.CollapseToStart();
        Assert.Equal(2u, selection.AnchorOffset);
        Assert.True(selection.IsCollapsed);

        selection.Collapse(null);
        Assert.Equal(0u, selection.RangeCount);
        Assert.Throws<DomException>(selection.CollapseToEnd);
    }

    [Fact]
    public void SelectAllChildren_ContainsNodeAndDeleteFromDocument()
    {
        var document = Load("<div id=d><b>a</b><i>b</i></div><p id=outside>c</p>");
        var selection = document.GetSelection()!;
        var div = document.GetElementById("d")!;

        selection.SelectAllChildren(div);
        Assert.Equal(0u, selection.GetRangeAt(0).StartOffset);
        Assert.Equal(2u, selection.GetRangeAt(0).EndOffset);
        Assert.True(selection.ContainsNode(div.FirstChild!));
        Assert.False(selection.ContainsNode(document.GetElementById("outside")!, allowPartialContainment: true));

        selection.SetBaseAndExtent(div.LastChild!.FirstChild!, 0, div.LastChild!.FirstChild!, 1);
        Assert.True(selection.ContainsNode(div.LastChild!.FirstChild!));
        Assert.False(selection.ContainsNode(div.LastChild!));
        Assert.True(selection.ContainsNode(div.LastChild!, allowPartialContainment: true));
        Assert.False(selection.ContainsNode(div.FirstChild!, allowPartialContainment: true));

        selection.SelectAllChildren(div);

        selection.DeleteFromDocument();
        Assert.Equal(0u, div.ChildNodes.Length);
        Assert.True(selection.IsCollapsed);
    }

    [Fact]
    public void SelectionRange_IsLive()
    {
        var document = Load("<p id=p><b>a</b><i>b</i><u>c</u></p>");
        var selection = document.GetSelection()!;
        var p = document.GetElementById("p")!;

        selection.SetBaseAndExtent(p, 1, p, 3);
        ((Element)p.FirstChild!).Remove();
        Assert.Equal(0u, selection.AnchorOffset);
        Assert.Equal(2u, selection.FocusOffset);

        // The selection is not in the document tree anymore
        selection.GetRangeAt(0).SelectNodeContents(document.CreateElement("div"));
        Assert.Equal(0u, selection.RangeCount);
        Assert.Null(selection.AnchorNode);
        Assert.Equal("None", selection.Type);
    }

    [Fact]
    public void GetComposedRanges_RescopesShadowTrees()
    {
        var document = Load("<div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.AppendChild(document.CreateTextNode("hello"));
        var selection = document.GetSelection()!;

        selection.SetBaseAndExtent(shadowRoot.FirstChild!, 1, shadowRoot.FirstChild!, 3);

        // The anchor and the focus are not in the document tree
        Assert.Equal(0u, selection.RangeCount);
        Assert.Null(selection.AnchorNode);
        Assert.Same(selection, shadowRoot.GetSelection());

        var range = Assert.Single(selection.GetComposedRanges());
        Assert.Same(document.Body, range.StartContainer);
        Assert.Equal(0u, range.StartOffset);
        Assert.Same(document.Body, range.EndContainer);
        Assert.Equal(1u, range.EndOffset);

        range = Assert.Single(selection.GetComposedRanges(new GetComposedRangesOptions { ShadowRoots = [shadowRoot] }));
        Assert.Same(shadowRoot.FirstChild, range.StartContainer);
        Assert.Equal(1u, range.StartOffset);
        Assert.Equal(3u, range.EndOffset);
    }

    [Fact]
    public void SelectionChange_IsCoalescedAndOnlyFiredOnChange()
    {
        var document = Load("<p id=p>hello</p>");
        var selection = document.GetSelection()!;
        var text = GetText(document, "p");
        var events = RecordSelectionChangeEvents(document);

        selection.SetBaseAndExtent(text, 0, text, 1);
        selection.SetBaseAndExtent(text, 0, text, 2);
        Assert.Empty(events);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["selectionchange bubbles=False"], events);

        // Same selection
        selection.SetBaseAndExtent(text, 0, text, 2);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(events);

        // Mutation of the range by a Range method or by the DOM
        selection.GetRangeAt(0).SetEnd(text, 3);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(2, events.Count);

        text.Data = "";
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(3, events.Count);

        selection.RemoveAllRanges();
        selection.RemoveAllRanges();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void SelectionChange_FiredOnTextControls()
    {
        var document = Load("<input id=i value=hello><textarea id=t>hello</textarea>");
        var input = (HtmlInputElement)document.GetElementById("i")!;
        var textArea = (HtmlTextAreaElement)document.GetElementById("t")!;
        var events = RecordSelectionChangeEvents(document.Body!);

        input.SetSelectionRange(1, 2);
        input.SetSelectionRange(1, 3);
        textArea.Select();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["selectionchange bubbles=True", "selectionchange bubbles=True"], events);

        input.SetSelectionRange(1, 3);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(2, events.Count);
    }
}
