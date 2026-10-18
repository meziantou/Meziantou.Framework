using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class CssPseudoElementTests : IDisposable
{
    private const string Style = "<style>body { margin: 0 } #target { width: 100px; padding-top: 100px } #target::after { content: ''; display: block; width: 100px; height: 50px }</style>";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html>" + Style + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void Pseudo_ReturnsTheSameObjectForTheSamePseudoElement()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;

        var before = target.Pseudo("::before")!;
        Assert.Same(before, target.Pseudo(":before"));
        Assert.Same(before, target.Pseudo("::BEFORE"));
        Assert.Equal("::before", before.Type);
        Assert.Equal("::before", before.SelectorText);
        Assert.Same(target, before.Element);
        Assert.Same(target, before.Parent.Element);
        Assert.NotSame(before, target.Pseudo("::after"));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("::first-line")]
    [InlineData("::selection")]
    [InlineData("::part(a)")]
    [InlineData("::details-content")]
    [InlineData("::scroll-button(*)")]
    [InlineData("::before::marker")]
    [InlineData("::before ")]
    public void Pseudo_ReturnsNullForTheOtherPseudoElements(string type)
    {
        var document = Load("<div id=target></div>");
        Assert.Null(document.GetElementById("target")!.Pseudo(type));
    }

    [Fact]
    public void Pseudo_SubPseudoElements()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;

        var marker = target.Pseudo("::before")!.Pseudo("::marker")!;
        Assert.Equal("::marker", marker.SelectorText);
        Assert.Same(target.Pseudo("::before"), marker.Parent.PseudoElement);
        Assert.Same(target, marker.Element);
        Assert.NotSame(target.Pseudo("::marker"), marker);
        Assert.Null(target.Pseudo("::before")!.Pseudo("::after"));

        // The view transition pseudo-elements are sub-pseudo-elements of ::view-transition
        var newImage = target.Pseudo("::view-transition-new(name)")!;
        var imagePair = target.Pseudo("::view-transition")!.Pseudo("::view-transition-group(name)")!.Pseudo("::view-transition-image-pair(name)")!;
        Assert.Same(imagePair, newImage.Parent.PseudoElement);
        Assert.Same(newImage, imagePair.Pseudo("::view-transition-new(name)"));
        Assert.Equal("::view-transition-new", newImage.Type);
        Assert.Null(imagePair.Pseudo("::view-transition-new(other)"));
        Assert.Null(target.Pseudo("::view-transition")!.Pseudo("::view-transition-image-pair(name)"));
    }

    [Fact]
    public void Pseudo_ScrollButtonOfAPhysicalDirectionIsTheButtonOfItsFlowRelativeDirection()
    {
        var document = Load("<div id=target style='writing-mode: vertical-rl'></div>");
        var target = document.GetElementById("target")!;

        Assert.Same(target.Pseudo("::scroll-button(inline-start)"), target.Pseudo("::scroll-button(up)"));
        Assert.Equal("::scroll-button(inline-start)", target.Pseudo("::scroll-button(UP)")!.SelectorText);
        Assert.Equal("::scroll-button", target.Pseudo("::scroll-button(up)")!.Type);
    }

    [Fact]
    public void GetBoxQuads_ReturnsTheQuadsOfTheBoxOfThePseudoElement()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;

        var bounds = Assert.Single(target.Pseudo("::after")!.GetBoxQuads()).GetBounds();
        Assert.Equal((0d, 100d, 100d, 50d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Empty(target.Pseudo("::before")!.GetBoxQuads());

        var point = target.Pseudo("::after")!.ConvertPointFromNode(new DomPointInit { X = 10, Y = 120, Z = 3, W = 2 }, document);
        Assert.Equal((10d, 20d, 3d, 2d), (point.X, point.Y, point.Z, point.W));
    }

    [Fact]
    public void PseudoTarget_IsThePseudoElementHitDuringTheDispatch()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;
        var after = target.Pseudo("::after");
        var events = new List<(string Type, CssPseudoElement? PseudoTarget)>();
        UIEvent? click = null;
        foreach (var type in (string[])["pointerover", "pointerdown", "mousedown", "pointerup", "mouseup", "click"])
        {
            target.AddEventListener(type, e =>
            {
                events.Add((e.Type, ((UIEvent)e).PseudoTarget));
                click = (UIEvent)e;
            });
        }

        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 120);
        mouse.Down();
        mouse.Up();

        // The boundary events have no pseudoTarget, and it is null after the dispatch
        Assert.Equal([("pointerover", null), ("pointerdown", after), ("mousedown", after), ("pointerup", after), ("mouseup", after), ("click", after)], events);
        Assert.Null(click!.PseudoTarget);
    }

    [Fact]
    public void PseudoTarget_IsNullWhenThePseudoElementIsRemovedBeforeTheClick()
    {
        var document = Load("<style>.hide::after { content: none !important }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        CssPseudoElement? clickPseudoTarget = null;
        var clicked = false;
        target.AddEventListener("mousedown", _ => target.ClassName = "hide");
        document.AddEventListener("click", e => (clicked, clickPseudoTarget) = (true, ((UIEvent)e).PseudoTarget));

        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 120);
        mouse.Down();
        mouse.Up();

        Assert.True(clicked);
        Assert.Null(clickPseudoTarget);
    }

    [Fact]
    public void PseudoTarget_FromTheInitializationDictionary()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;
        var after = target.Pseudo("::after");
        CssPseudoElement? pseudoTarget = null;
        target.AddEventListener("test", e => pseudoTarget = ((UIEvent)e).PseudoTarget);

        target.DispatchEvent(new UIEvent("test", new UIEventInit { PseudoTarget = after }));

        Assert.Same(after, pseudoTarget);
    }
}
