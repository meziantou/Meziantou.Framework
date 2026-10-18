using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The shadow trees: the slots (https://dom.spec.whatwg.org/#shadow-tree-slots) and the declarative shadow roots.</summary>
public sealed class ShadowDomTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static HtmlSlotElement AttachManualSlot(Element host)
    {
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, SlotAssignment = SlotAssignmentMode.Manual });
        return (HtmlSlotElement)shadowRoot.AppendChild(host.NodeDocument.CreateElement("slot"));
    }

    [Fact]
    public void SlotAssign_SignalsASlotChangeToThePreviousSlotOfAnotherTree()
    {
        var document = Load("<div id=host1><div id=node></div></div><div id=host2></div>");
        var node = document.GetElementById("node")!;
        var slot1 = AttachManualSlot(document.GetElementById("host1")!);
        var slot2 = AttachManualSlot(document.GetElementById("host2")!);
        var events = new List<string>();
        slot1.AddEventListener("slotchange", _ => events.Add("slot1"));
        slot2.AddEventListener("slotchange", _ => events.Add("slot2"));

        slot1.Assign(node);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["slot1"], events);
        Assert.Same(slot1, node.AssignedSlot);

        // The node is not a child of the host of the other slot: it is not assigned to it, and the assigned nodes of the
        // other slot do not change
        events.Clear();
        slot2.Assign(node);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["slot1"], events);
        Assert.Empty(slot1.AssignedNodes());
        Assert.Empty(slot2.AssignedNodes());
        Assert.Null(node.AssignedSlot);
    }

    [Fact]
    public void SlotAssign_DisconnectedSlotTakesTheNodeOfAConnectedSlot()
    {
        var document = Load("<div id=host><div id=node></div></div>");
        var node = document.GetElementById("node")!;
        var slot = AttachManualSlot(document.GetElementById("host")!);
        slot.Assign(node);
        _contexts.EventLoop.RunUntilIdle();

        var count = 0;
        slot.AddEventListener("slotchange", _ => count++);
        ((HtmlSlotElement)document.CreateElement("slot")).Assign(node);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, count);
        Assert.Empty(slot.AssignedNodes());
        Assert.Null(node.AssignedSlot);
    }

    [Fact]
    public void SetHtmlUnsafe_DeclarativeShadowRootIsNotAttachedToTheContextElement()
    {
        var document = Load("<div id=container></div>");
        var container = document.GetElementById("container")!;

        // The template is a child of the element that precedes it in the markup
        container.SetHtmlUnsafe("<div id=host><template shadowrootmode=open><slot></slot></template>light</div>");
        var host = container.FirstElementChild!;
        Assert.NotNull(host.ShadowRoot);
        Assert.Null(host.QuerySelector("template"));

        // The template is a child of the context element (a void element has no children): the template is kept
        container.SetHtmlUnsafe("<br><template shadowrootmode=open><slot></slot></template>");
        Assert.Null(container.ShadowRoot);
        Assert.Equal("open", container.QuerySelector("template")!.GetAttribute("shadowrootmode"));
    }
}
