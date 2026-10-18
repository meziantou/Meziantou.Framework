using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The size container queries and the container-relative lengths evaluated with the sizes of the layout (https://drafts.csswg.org/css-conditional-5/#container-queries).</summary>
public sealed class ContainerQueryLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string Get(Document document, string id, string property, string? pseudoElement = null)
        => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!, pseudoElement).GetPropertyValue(property);

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private const string Float = "<div style='float: left; width: 100px; height: 10px'></div>";

    [Theory]
    // The width of a container next to a float is only known from the layout
    [InlineData(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target></div></div><style>@container (width = 700px) { #target { color: green } }</style>", "color", "rgb(0, 128, 0)")]
    [InlineData(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target style='width: 10cqw'></div></div>", "width", "70px")]
    [InlineData(Float + "<div id=c style='container-type: inline-size; display: flow-root'><div id=target style='width: 10cqi'></div></div>", "width", "70px")]

    // The scrollbars and the scrollbar gutters are not part of the queried content box
    [InlineData("<div id=c style='container-type: inline-size; width: 300px; height: 100px; overflow-y: scroll'><div id=target style='width: 100cqw'></div></div>", "width", "285px")]
    [InlineData("<div id=c style='container-type: inline-size; width: 300px; scrollbar-gutter: stable; overflow: auto'><div id=target style='width: 100cqw'></div></div>", "width", "285px")]

    // A shrink-to-fit container with inline-size containment is sized as if it was empty (contain-intrinsic-size)
    [InlineData("<div id=c style='container-type: inline-size; float: left; contain-intrinsic-width: 120px'><div id=target style='width: 50cqw'></div></div>", "width", "60px")]

    // The block size of a size container with an automatic block size is its contain-intrinsic-size, clamped by min-height
    [InlineData("<div id=c style='container-type: size; min-height: 50px'><div id=target style='height: 100cqh'></div></div>", "height", "50px")]
    [InlineData("<div id=c style='container-type: size; contain-intrinsic-height: 30px; padding: 5px'><div id=target style='height: 100cqh'></div></div>", "height", "30px")]

    // Nested containers: the inner container is laid out after the styles of its contents are computed with the outer size
    [InlineData(Float + "<div id=a style='container-type: inline-size; overflow: hidden'><div id=b style='container-type: inline-size; width: 50cqw'><div id=target style='width: 50cqw'></div></div></div>", "width", "175px")]
    [InlineData(Float + "<div id=a style='container-type: inline-size; overflow: hidden'><div id=b style='container-type: inline-size; width: 50%'><div id=target></div></div></div><style>@container (width = 350px) { #target { color: green } }</style>", "color", "rgb(0, 128, 0)")]

    // The pseudo-elements of the container query it
    [InlineData(Float + "<div id=target style='container-type: inline-size; overflow: hidden'></div><style>@container (width = 700px) { #target::before { content: 'x'; color: green } }</style>", "color", "rgb(0, 128, 0)", "::before")]

    // Zoom: the size of the container is in the coordinates of its computed values
    [InlineData(Float + "<div id=c style='container-type: inline-size; overflow: hidden; zoom: 2'><div id=target></div></div><style>@container (width = 350px) { #target { color: green } }</style>", "color", "rgb(0, 128, 0)")]

    // Vertical writing mode: the inline size is the height
    [InlineData("<div id=c style='container-type: inline-size; writing-mode: vertical-rl; height: 200px'><div id=target style='height: 50cqi'></div></div>", "height", "100px")]
    public void QueriesUseTheSizeOfTheLayout(string html, string property, string expected, string? pseudoElement = null)
    {
        var document = Parse(html);
        Assert.Equal(expected, Get(document, "target", property, pseudoElement));
    }

    [Fact]
    public void BoxesOfTheContentsAreBuiltAgain()
    {
        // The display of the descendants depends on the size of the container: their boxes are built with their new styles
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=hidden style='height: 20px'></div><span id=inline>a</span></div>"
            + "<style>#inline { display: block; height: 30px } @container (width > 600px) { #hidden { display: none } #inline { display: inline } }</style>");
        var engine = document.GetLayoutEngine();
        Assert.Empty(engine.GetFragments(document.GetElementById("hidden")!));
        Assert.Equal(new PhysicalRect(100, 0, 10, 10), GetRect(document, "inline"));
        Assert.Equal(new PhysicalRect(100, 0, 700, 10), GetRect(document, "c"));
        Assert.Equal(1, engine.LayoutCount);
    }

    [Fact]
    public void ContainerUnitsAreUsedByTheLayout()
    {
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target style='width: 50cqw; height: 10cqw'></div></div>");
        Assert.Equal(new PhysicalRect(100, 0, 350, 70), GetRect(document, "target"));
        Assert.Equal(new PhysicalRect(100, 0, 700, 70), GetRect(document, "c"));
    }

    [Fact]
    public void ContainerSizeChange_DescendantsStyledAgain()
    {
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target></div></div><style>#target { color: red } @container (width < 600px) { #target { color: green } }</style>");
        Assert.Equal("rgb(255, 0, 0)", Get(document, "target", "color"));

        // The float is wider: the container is narrower (the styles do not change)
        ((HtmlElement)document.Body!.FirstElementChild!).Style.SetProperty("width", "300px");
        Assert.Equal("rgb(0, 128, 0)", Get(document, "target", "color"));
        Assert.Equal(new PhysicalRect(300, 0, 500, 0), GetRect(document, "c"));
    }

    [Fact]
    public void Relayout_SameSize_StylesKept()
    {
        // A layout without a change of the size of the container does not compute the styles of its descendants again
        var document = Parse("<div id=c style='container-type: inline-size; width: 300px; overflow-y: scroll'><div id=target></div></div><style>@container (width = 285px) { #target { color: green } }</style>");
        var target = document.GetElementById("target")!;
        Assert.Equal("rgb(0, 128, 0)", Get(document, "target", "color"));
        var style = document.GetStyleEngine().GetStyle(target);
        var engine = document.GetLayoutEngine();
        var version = document.GetStyleEngine().Version;
        var count = engine.LayoutCount;
        engine.SetNeedsLayout();
        engine.EnsureLayout();
        Assert.Equal(version, document.GetStyleEngine().Version);
        Assert.Equal(count + 1, engine.LayoutCount);
        Assert.Same(style, document.GetStyleEngine().GetStyle(target));
    }

    [Fact]
    public void MatchContainer_UsesTheSizeOfTheLayout()
    {
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target></div></div>");
        var target = document.GetElementById("target")!;
        Assert.True(target.MatchContainer("(width = 700px)").Matches);
        Assert.False(target.MatchContainer("(width = 800px)").Matches);
    }

    [Fact]
    public void ContainerQueryListChangeEvent_AfterTheLayoutChanged()
    {
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target></div></div>");
        var target = document.GetElementById("target")!;
        var list = target.MatchContainer("(width < 600px)");
        var changes = 0;
        list.AddEventListener("change", _ => changes++);
        Assert.False(list.Matches);

        ((HtmlElement)document.Body!.FirstElementChild!).Style.SetProperty("width", "300px");
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1000d / 60));
            _contexts.EventLoop.RunUntilIdle();
        }

        Assert.Equal(1, changes);
        Assert.True(list.Matches);
    }

    [Fact]
    public void WithoutBrowsingContext_SizesKnownWithoutLayout()
    {
        // A document without browsing context is not laid out: the sizes known without layout are used
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=c style='container-type: inline-size; width: 400px'><div id=target></div></div><style>@container (width = 400px) { #target { color: green } }</style>");
        var style = document.GetStyleEngine().GetStyle(document.GetElementById("target")!);
        Assert.Equal("rgb(0, 128, 0)", style.GetComputedValue("color"));
        Assert.Null(document.LayoutEngineOrNull);
    }

    [Fact]
    public void Transition_StartsFromTheStyleBeforeTheChange()
    {
        // The descendants are styled with the size of the previous layout, then with the new size: the transition starts from
        // the style before the change (the first style of the generation is not a before-change style)
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target></div></div>"
            + "<style>#target { width: 100px; transition: width 1s linear } @container (width < 600px) { #target { width: 300px } }</style>");
        Assert.Equal("100px", Get(document, "target", "width"));

        ((HtmlElement)document.Body!.FirstElementChild!).Style.SetProperty("width", "300px");
        Assert.Equal("100px", Get(document, "target", "width"));
        Assert.Single(document.GetElementById("target")!.GetAnimations());
    }

    [Fact]
    public void Transition_KeepsRunningWhenTheContainerIsLaidOutAgain()
    {
        // The container next to the float is laid out in the whole width to measure it, then beside the float: the style of the
        // descendants computed with the first size does not cancel the transition started with the second one
        var document = ParseTransitionNextToFloat();
        ((HtmlElement)document.Body!.FirstElementChild!).Style.SetProperty("width", "300px");
        Assert.Equal("100px", Get(document, "target", "width"));

        RunFrame(TimeSpan.FromMilliseconds(1000d / 60)); // The transition starts
        RunFrame(TimeSpan.FromMilliseconds(500));
        Assert.Equal("200px", Get(document, "target", "width"));
        Assert.Single(document.GetElementById("target")!.GetAnimations());
        Assert.Single(document.Animations.GetElementAnimations(document.GetElementById("target")!)!.Get(pseudoElement: null)!);
    }

    [Fact]
    public void Transition_KeepsRunningWhenTheLayoutIsComputedAgain()
    {
        // The layout is computed again before the next frame (e.g. a change of the process-wide version of the styles by
        // another document computes the styles and the layout again)
        var document = ParseTransitionNextToFloat();
        ((HtmlElement)document.Body!.FirstElementChild!).Style.SetProperty("width", "300px");
        Assert.Equal("100px", Get(document, "target", "width"));

        document.GetLayoutEngine().SetNeedsLayout();
        Assert.Equal("100px", Get(document, "target", "width"));
        Assert.Single(document.GetElementById("target")!.GetAnimations());
    }

    [Fact]
    public void Transition_CanceledByTheStyleOfTheNewSize()
    {
        var document = ParseTransitionNextToFloat();
        var target = document.GetElementById("target")!;
        var canceled = 0;
        target.AddEventListener("transitioncancel", _ => canceled++);
        var floatElement = (HtmlElement)document.Body!.FirstElementChild!;
        floatElement.Style.SetProperty("width", "300px");
        Assert.Equal("100px", Get(document, "target", "width"));
        RunFrame(TimeSpan.FromMilliseconds(1000d / 60));

        // The container is wide again: the transition to the narrow style is canceled (its current value is the new value)
        floatElement.Style.SetProperty("width", "100px");
        Assert.Equal("100px", Get(document, "target", "width"));
        Assert.Empty(target.GetAnimations());
        RunFrame(TimeSpan.FromMilliseconds(1000d / 60));
        Assert.Equal(1, canceled);
    }

    private Document ParseTransitionNextToFloat()
    {
        var document = Parse(Float + "<div id=c style='container-type: inline-size; overflow: hidden'><div id=target></div></div>"
            + "<style>#target { width: 100px; transition: width 1s linear } @container (width < 600px) { #target { width: 300px } }</style>");
        Assert.Equal("100px", Get(document, "target", "width"));
        return document;
    }

    private void RunFrame(TimeSpan duration)
    {
        _clock.Advance(duration);
        _contexts.EventLoop.RunUntilIdle();
    }

    [Fact]
    public void TopLayerDescendants_StyledAgain()
    {
        // The modal dialogs are in the top layer (children of the viewport): their boxes are built again with their new styles
        var document = Parse("<style>dialog { color: red } #container { width: 100px } #container, #outer { container-type: inline-size } @container (min-width: 200px) { #outer { width: 400px; color: lime } } @container (min-width: 400px) { #inner { color: green } }</style>"
            + "<div id=container><dialog id=outer><dialog id=inner></dialog></dialog></div>");
        var container = (HtmlElement)document.GetElementById("container")!;
        _ = container.OffsetTop;
        ((HtmlDialogElement)document.GetElementById("outer")!).ShowModal();
        ((HtmlDialogElement)document.GetElementById("inner")!).ShowModal();
        Assert.Equal("rgb(255, 0, 0)", Get(document, "inner", "color"));

        container.Style.SetProperty("width", "200px");
        Assert.Equal("rgb(0, 255, 0)", Get(document, "outer", "color"));
        Assert.Equal("rgb(0, 128, 0)", Get(document, "inner", "color"));
        Assert.Equal(400, GetRect(document, "outer").Width - (2 * 13)); // 1em padding (10px) and 3px borders on each side
        Assert.NotEmpty(document.GetLayoutEngine().GetFragments(document.GetElementById("inner")!));
    }
}
