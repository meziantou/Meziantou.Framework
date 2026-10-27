using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

/// <summary>The overlay property (https://drafts.csswg.org/css-position-4/#overlay): its user agent rules and its transitions, which keep an element rendered in the top layer.</summary>
public sealed class OverlayTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private void Advance(double milliseconds)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            var step = Math.Min(remaining, 1000d / 60);
            _clock.Advance(TimeSpan.FromMilliseconds(step));
            _contexts.EventLoop.RunUntilIdle();
            remaining -= step;
        }
    }

    private static string GetComputed(Element element, string property) =>
        element.OwnerDocument!.DefaultView!.GetComputedStyle(element).GetPropertyValue(property);

    private static bool IsRenderedInTopLayer(Element element)
    {
        var tree = BoxTree.Build(element.OwnerDocument!);
        return tree.GetPrincipalBox(element) is { IsInTopLayer: true };
    }

    [Fact]
    public void UserAgentRulesSetOverlay()
    {
        // https://drafts.csswg.org/css-position-4/#overlay: "* { overlay: none !important; }", and overlay: auto !important for
        // the elements in the top layer; the animations do not override them
        var document = Load("<style>@keyframes a { from, to { overlay: auto } } #a { overlay: auto; animation: a 10s }</style><div id=a></div><div id=p popover style='overlay: none !important'></div>");
        var element = document.GetElementById("a")!;
        var popover = (HtmlElement)document.GetElementById("p")!;
        Assert.Equal("none", GetComputed(element, "overlay"));
        Assert.Equal("none", GetComputed(popover, "overlay"));
        popover.ShowPopover();
        Assert.Equal("auto", GetComputed(popover, "overlay"));
        Assert.True(IsRenderedInTopLayer(popover));
        popover.HidePopover();
        Assert.Equal("none", GetComputed(popover, "overlay"));
    }

    [Fact]
    public void TransitionToNoneKeepsTheElementRenderedInTheTopLayer()
    {
        var document = Load("<style>#p { display: block; transition: overlay 1s step-end allow-discrete }</style><div id=p popover></div>");
        var popover = (HtmlElement)document.GetElementById("p")!;

        // (the styles computed once the document is parsed are the before-change styles: the overlay transitions to auto first)
        popover.ShowPopover();
        Advance(1100);
        Assert.Equal("auto", GetComputed(popover, "overlay"));
        popover.HidePopover();

        // The popover is no longer in the top layer for the algorithms (:popover-open), but it is rendered in it while its
        // overlay transitions to none, then removed from the top layer by a rendering update
        Assert.False(popover.Matches(":popover-open"));
        Assert.Equal("auto", GetComputed(popover, "overlay"));
        Assert.True(IsRenderedInTopLayer(popover));
        Advance(1100);
        Assert.Equal("none", GetComputed(popover, "overlay"));
        Assert.False(IsRenderedInTopLayer(popover));
        Assert.DoesNotContain(popover, document.TopLayerElementsForRendering ?? []);
    }

    [Fact]
    public void TransitionToAutoDelaysTheRenderingInTheTopLayer()
    {
        var document = Load("<style>#p { display: block; transition: overlay 0.1s step-end; transition-behavior: allow-discrete }</style><div id=p popover></div>");
        var popover = (HtmlElement)document.GetElementById("p")!;
        Assert.Equal("none", GetComputed(popover, "overlay"));
        popover.ShowPopover();
        Assert.Equal("none", GetComputed(popover, "overlay"));
        Assert.False(IsRenderedInTopLayer(popover));
        Advance(200);
        Assert.Equal("auto", GetComputed(popover, "overlay"));
        Assert.True(IsRenderedInTopLayer(popover));
    }

    [Fact]
    public void ChangesWhileTransitioningToNoneBuildTheBoxTreeAgain()
    {
        // The popover pending removal is rendered in the top layer while its overlay transitions: the boxes of a change inside
        // it are not patched at their place in the tree
        var document = Load("<style>#p { display: block; transition: overlay 1s step-end allow-discrete }</style><div id=p popover><span id=s>a</span></div>");
        var popover = (HtmlElement)document.GetElementById("p")!;
        var engine = document.GetLayoutEngine();
        popover.ShowPopover();
        Advance(1100);
        engine.EnsureLayout();
        popover.HidePopover();
        engine.EnsureLayout();
        Advance(100);
        document.GetElementById("s")!.TextContent = "bc";
        engine.EnsureLayout();
        Assert.True(engine.BoxTree!.GetPrincipalBox(popover) is { IsInTopLayer: true });
        Assert.Null(BoxTreeBuilder.DescribeDifference(engine.BoxTree!, BoxTree.Build(document)));
    }
}
