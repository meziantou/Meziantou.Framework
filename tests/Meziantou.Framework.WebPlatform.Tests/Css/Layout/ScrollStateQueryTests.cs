using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The container scroll-state queries (https://drafts.csswg.org/css-conditional-5/#scroll-state-container), evaluated against the
/// snapshots of the scroll-state query containers taken by the rendering steps.
/// </summary>
public sealed class ScrollStateQueryTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering steps of the next frames.</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static string Get(Document document, string property, string id = "target")
        => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).GetPropertyValue(property);

    // A 100x100 scroll container (without scrollbars) whose contents are 300px high, and a target that sets --<value>: yes for
    // each value of a feature that matches
    private static string Scroller(string feature, string values, string scrollerStyle = "", string contentsStyle = "")
    {
        var rules = string.Concat(values.Split(' ').Select(value => $"@container scroll-state({feature}: {value}) {{ #target {{ --{value}: yes }} }} "));
        return $"<style>{rules}</style><div id=scroller style='container-type: scroll-state; width: 100px; height: 100px; overflow: auto; scrollbar-width: none; {scrollerStyle}'>"
            + $"<div id=target style='height: 300px; {contentsStyle}'></div></div>";
    }

    [Fact]
    public void Scrollable_InitialStateIsKnownFromTheFirstLayout()
    {
        // The first snapshot of a container is taken by the layout: the styles read by a script before the first frame use it
        var document = Parse(Scroller("scrollable", "top bottom block-end y x none"));
        Assert.Equal("yes", Get(document, "--bottom"));
        Assert.Equal("yes", Get(document, "--block-end"));
        Assert.Equal("yes", Get(document, "--y"));
        Assert.Equal("", Get(document, "--top"));
        Assert.Equal("", Get(document, "--x"));
        Assert.Equal("", Get(document, "--none"));
    }

    [Fact]
    public void Scrollable_IsUpdatedByTheRenderingSteps()
    {
        var document = Parse(Scroller("scrollable", "top bottom"));
        Assert.Equal("", Get(document, "--top"));

        // The snapshot is not updated by a forced style update: the queries see the new scroll position after the next frame
        document.GetElementById("scroller")!.ScrollTop = 200;
        Assert.Equal("", Get(document, "--top"));
        Assert.Equal("yes", Get(document, "--bottom"));

        RunFrames();
        Assert.Equal("yes", Get(document, "--top"));
        Assert.Equal("", Get(document, "--bottom"));
    }

    [Fact]
    public void Scrollable_HiddenOverflowIsNotReachableByTheUser()
    {
        var document = Parse(Scroller("scrollable", "bottom none", scrollerStyle: "overflow: hidden"));
        Assert.Equal("", Get(document, "--bottom"));
        Assert.Equal("yes", Get(document, "--none"));
    }

    [Fact]
    public void Scrollable_LogicalValuesUseTheWritingModeOfTheContainer()
    {
        var document = Parse(Scroller("scrollable", "inline-end block-end left", scrollerStyle: "writing-mode: vertical-rl", contentsStyle: "width: 300px; height: 10px"));
        Assert.Equal("yes", Get(document, "--block-end"));
        Assert.Equal("yes", Get(document, "--left"));
        Assert.Equal("", Get(document, "--inline-end"));
    }

    [Fact]
    public void Scrolled_DirectionOfTheLastRelativeScroll()
    {
        var document = Parse(Scroller("scrolled", "none top bottom y x", contentsStyle: "width: 300px"));
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal("yes", Get(document, "--none"));

        // An absolute scroll is not a relative scroll
        scroller.ScrollTop = 100;
        RunFrames();
        Assert.Equal("yes", Get(document, "--none"));

        _ = scroller.ScrollBy(0, 50);
        RunFrames();
        Assert.Equal("yes", Get(document, "--bottom"));
        Assert.Equal("yes", Get(document, "--y"));
        Assert.Equal("", Get(document, "--none"));

        // A relative scroll in the horizontal axis keeps the direction of the vertical axis
        _ = scroller.ScrollBy(50, 0);
        RunFrames();
        Assert.Equal("yes", Get(document, "--bottom"));
        Assert.Equal("yes", Get(document, "--x"));

        _ = scroller.ScrollBy(0, -10);
        RunFrames();
        Assert.Equal("yes", Get(document, "--top"));
        Assert.Equal("", Get(document, "--bottom"));

        // A relative scroll that does not move does not change the direction
        scroller.ScrollTop = 0;
        _ = scroller.ScrollBy(0, -10);
        RunFrames();
        Assert.Equal("yes", Get(document, "--top"));
    }

    [Fact]
    public void Stuck_StickyContainerShiftedByAnInset()
    {
        var document = Parse("<style>@container scroll-state(stuck: bottom) { #target { --bottom: yes } } @container scroll-state(stuck: block-end) { #target { --block-end: yes } } @container scroll-state(stuck) { #target { --stuck: yes } }</style>"
            + "<div id=scroller style='height: 300px; overflow: auto'><div id=filler style='height: 250px'></div>"
            + "<div id=sticky style='container-type: scroll-state; position: sticky; bottom: 0; height: 100px'><span id=target></span></div></div>");
        Assert.Equal("yes", Get(document, "--bottom"));
        Assert.Equal("yes", Get(document, "--block-end"));
        Assert.Equal("yes", Get(document, "--stuck"));

        document.GetElementById("filler")!.SetAttribute("style", "height: 0");
        RunFrames();
        Assert.Equal("", Get(document, "--bottom"));
        Assert.Equal("", Get(document, "--stuck"));
    }

    [Fact]
    public void Snapped_SnapTargetOfItsSnapContainer()
    {
        var items = string.Concat(Enumerable.Range(0, 5).Select(i => $"<div id=item{i} class=item><span id=target{i}></span></div>"));
        var document = Parse("<style>.item { container-type: scroll-state; height: 100px; scroll-snap-align: start }"
            + "@container scroll-state(snapped: y) { span { --y: yes } } @container scroll-state(snapped: block) { span { --block: yes } } @container scroll-state(snapped: x) { span { --x: yes } }</style>"
            + "<div id=scroller style='height: 100px; overflow: auto; scroll-snap-type: y mandatory'>" + items + "</div>");
        Assert.Equal("yes", Get(document, "--y", "target0"));
        Assert.Equal("yes", Get(document, "--block", "target0"));
        Assert.Equal("", Get(document, "--x", "target0"));
        Assert.Equal("", Get(document, "--y", "target2"));

        document.GetElementById("scroller")!.ScrollTop = 200;
        RunFrames();
        Assert.Equal("", Get(document, "--y", "target0"));
        Assert.Equal("yes", Get(document, "--y", "target2"));
    }

    [Fact]
    public void RootContainerQueriesTheViewport()
    {
        var document = Parse("<style>:root { container-type: scroll-state } @container scroll-state(scrollable: bottom) { #target { --bottom: yes } }</style><div id=target style='height: 5000px'></div>");
        Assert.Equal("yes", Get(document, "--bottom"));

        document.DocumentElement!.ScrollTop = 10000;
        RunFrames();
        Assert.Equal("", Get(document, "--bottom"));
    }

    [Fact]
    public void ElementThatIsNotAScrollStateContainerIsNotQueried()
    {
        var document = Parse(Scroller("scrollable", "bottom none", scrollerStyle: "container-type: inline-size"));
        Assert.Equal("", Get(document, "--bottom"));
        Assert.Equal("", Get(document, "--none"));
    }

    [Fact]
    public void UnchangedSnapshotsDoNotChangeTheStyles()
    {
        var document = Parse(Scroller("scrollable", "bottom"));
        Assert.Equal("yes", Get(document, "--bottom"));
        var styleEngine = document.GetStyleEngine();
        var version = styleEngine.Version;
        var layoutCount = document.GetLayoutEngine().LayoutCount;

        RunFrames(3);
        Assert.Equal(version, styleEngine.Version);
        Assert.Equal(layoutCount, document.GetLayoutEngine().LayoutCount);
    }

    [Fact]
    public void DocumentWithoutBrowsingContext()
    {
        var document = new DomParser().ParseFromString("<!DOCTYPE html>" + Scroller("scrollable", "bottom none"), DomParserSupportedType.TextHtml);
        var target = document.GetElementById("target")!;
        Assert.Equal("yes", document.GetStyleEngine().GetStyle(target).GetCustomPropertyValue("--none"));
    }
}
