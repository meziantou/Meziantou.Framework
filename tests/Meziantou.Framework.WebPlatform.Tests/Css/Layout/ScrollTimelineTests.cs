using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// Scroll-driven animations (https://drafts.csswg.org/scroll-animations-1/): the scroll progress and view progress timelines,
/// sampled by the rendering steps, the animation attachment ranges and the progress-based timing of their animations.
/// </summary>
public sealed class ScrollTimelineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            Features = features,
        });
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

    private static double? Percent(CssNumberish? value) => value?.Value is CssUnitValue { Unit: "percent" } unit ? unit.Value : null;

    private static double? DurationPercent(ComputedEffectTiming timing) => timing.Duration.Value is CssUnitValue { Unit: "percent" } unit ? unit.Value : null;

    // A 100x100 scroller (no scrollbars) whose contents are 500px high: the scroll range is 400px
    private const string Scroller = "<div id=scroller style='width: 100px; height: 100px; overflow: hidden'><div style='height: 500px'></div></div>";

    [Fact]
    public void ScrollTimeline_CurrentTimeIsTheScrollProgressOfTheLastRenderingUpdate()
    {
        var document = Parse(Scroller);
        var scroller = document.GetElementById("scroller")!;
        var timeline = new ScrollTimeline(new ScrollTimelineOptions { Source = scroller });

        // Sampled when created, with the current layout
        Assert.Equal(0, Percent(timeline.CurrentTime));
        Assert.Equal(100, Percent(timeline.Duration));
        Assert.Same(scroller, timeline.Source);
        Assert.Equal(ScrollAxis.Block, timeline.Axis);

        // The current time stays the same until the next rendering update
        scroller.ScrollTop = 100;
        Assert.Equal(0, Percent(timeline.CurrentTime));
        RunFrames();
        Assert.Equal(25, Percent(timeline.CurrentTime));
    }

    [Fact]
    public void ScrollTimeline_IsInactiveWhenTheAxisDoesNotScroll()
    {
        var document = Parse(Scroller);
        var scroller = document.GetElementById("scroller")!;
        var timeline = new ScrollTimeline(new ScrollTimelineOptions { Source = scroller, Axis = ScrollAxis.Inline });
        Assert.Null(timeline.CurrentTime);

        var noSource = new ScrollTimeline(new ScrollTimelineOptions { Source = null });
        Assert.Null(noSource.Source);
        Assert.Null(noSource.CurrentTime);
    }

    [Fact]
    public void Animation_StartTimeIsAlignedWithTheAttachmentRange()
    {
        var document = Parse(Scroller + "<div id=target></div>");
        var scroller = document.GetElementById("scroller")!;
        var timeline = new ScrollTimeline(new ScrollTimelineOptions { Source = scroller });
        var animation = document.GetElementById("target")!.Animate(
            [new Keyframe { Properties = { ["opacity"] = "0" } }, new Keyframe { Properties = { ["opacity"] = "1" } }],
            new KeyframeAnimationOptions { Timeline = timeline, RangeStart = "20%", RangeEnd = "80%", Fill = FillMode.Both });

        Assert.True(animation.Pending);
        Assert.Null(animation.StartTime);

        // The intrinsic iteration duration fills the attachment range
        Assert.Equal(60, DurationPercent(animation.Effect!.GetComputedTiming()));

        RunFrames();
        Assert.False(animation.Pending);
        Assert.Equal(20, Percent(animation.StartTime));
        Assert.Equal(-20, Percent(animation.CurrentTime));

        // 50% of the timeline is the middle of the attachment range
        scroller.ScrollTop = 200;
        RunFrames();
        Assert.Equal(30, Percent(animation.CurrentTime));
        Assert.Equal(0.5, animation.Effect!.GetComputedTiming().Progress);
        Assert.Equal("0.5", document.DefaultView!.GetComputedStyle(document.GetElementById("target")!).GetPropertyValue("opacity"));

        // The time values of the animation of a progress-based timeline are percentages
        Assert.Throws<TypeErrorException>(() => animation.CurrentTime = 10);
        animation.CurrentTime = new CssUnitValue(10, "percent");
        Assert.Equal(10, Percent(animation.CurrentTime));
    }

    [Fact]
    public void ViewTimeline_ProgressIsTheVisibilityOfTheSubjectInTheScrollport()
    {
        // The subject is at 300px in a 100px high scrollport: the cover range is [200px, 400px]
        var document = Parse("""
            <div id=scroller style='width: 100px; height: 100px; overflow: hidden'>
              <div style='height: 300px'></div>
              <div id=subject style='height: 100px'></div>
              <div style='height: 300px'></div>
            </div>
            """);
        var scroller = document.GetElementById("scroller")!;
        var subject = document.GetElementById("subject")!;
        var timeline = new ViewTimeline(new ViewTimelineOptions { Subject = subject });
        Assert.Same(scroller, timeline.Source);
        Assert.Equal(200d, ((CssUnitValue)timeline.StartOffset).Value);
        Assert.Equal(400d, ((CssUnitValue)timeline.EndOffset).Value);
        Assert.Equal(-100, Percent(timeline.CurrentTime));

        scroller.ScrollTop = 300;
        RunFrames();
        Assert.Equal(50, Percent(timeline.CurrentTime));

        // The named ranges: contain is [300px, 300px] (the subject is as high as the scrollport)
        var animation = subject.Animate(
            [new Keyframe { Properties = { ["opacity"] = "0" } }, new Keyframe { Properties = { ["opacity"] = "1" } }],
            new KeyframeAnimationOptions { Timeline = timeline, RangeStart = "entry 0%", RangeEnd = "exit 100%" });
        RunFrames();
        Assert.Equal(0, Percent(animation.StartTime));
        Assert.Equal(100, DurationPercent(animation.Effect!.GetComputedTiming()));
        var range = Assert.IsType<TimelineRangeOffset>(animation.RangeStart.Value);
        Assert.Equal("entry", range.RangeName);
    }

    [Fact]
    public void ViewTimeline_SvgGraphicsElementSubjectUsesItsTransformedBoundingBox()
    {
        // The rect is at 300px (y=100 user units, scaled by the viewBox 2x, translated by 100px) and 100px high: the cover
        // range is [200px, 400px]
        var document = Parse("""
            <div id=scroller style='width: 100px; height: 100px; overflow: hidden'>
              <svg width=100 height=800 viewBox='0 0 50 400' style='display: block'>
                <rect id=subject x=0 y=100 width=10 height=50 transform='translate(0, 50)' />
              </svg>
            </div>
            """);
        var scroller = document.GetElementById("scroller")!;
        var subject = document.GetElementById("subject")!;
        var timeline = new ViewTimeline(new ViewTimelineOptions { Subject = subject });
        Assert.Same(scroller, timeline.Source);
        Assert.Equal(200d, ((CssUnitValue)timeline.StartOffset).Value);
        Assert.Equal(400d, ((CssUnitValue)timeline.EndOffset).Value);
    }

    [Fact]
    public void CssAnimation_NamedScrollTimeline()
    {
        var document = Parse("""
            <style>
              @keyframes grow { from { width: 0px } to { width: 100px } }
              #scroller { scroll-timeline: --t; width: 100px; height: 100px; overflow: hidden }
              #target { animation: grow linear; animation-timeline: --t }
            </style>
            <div id=scroller><div style='height: 500px'></div><div id=target></div></div>
            """);
        var scroller = document.GetElementById("scroller")!;
        var target = document.GetElementById("target")!;
        var animation = Assert.Single(target.GetAnimations());
        var timeline = Assert.IsType<ScrollTimeline>(animation.Timeline);
        Assert.Same(scroller, timeline.Source);

        RunFrames(2);
        scroller.ScrollTop = 100;
        RunFrames();
        Assert.Equal("25px", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("width"));
    }

    [Fact]
    public void CssAnimation_UnresolvedTimelineNameHasNoEffect()
    {
        var document = Parse("""
            <style>
              @keyframes grow { from { width: 50px } to { width: 100px } }
              #target { width: 10px; animation: grow 1s linear; animation-timeline: --unknown }
            </style>
            <div id=target></div>
            """);
        var target = document.GetElementById("target")!;
        RunFrames(2);
        var animation = Assert.Single(target.GetAnimations());
        Assert.Null(animation.Timeline);
        Assert.Equal("10px", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("width"));
    }

    [Fact]
    public void Feature_Disabled_TheScrollTimelinesOfTheStylesAreNoTimeline()
    {
        var document = Parse("""
            <style>
              @keyframes grow { from { width: 50px } to { width: 100px } }
              #target { animation: grow 1s linear; animation-timeline: scroll() }
            </style>
            <div id=target></div>
            """, new WebPlatformFeatures { ScrollDrivenAnimationsEnabled = false });
        var target = document.GetElementById("target")!;
        var animation = Assert.Single(target.GetAnimations());
        Assert.Null(animation.Timeline);
        Assert.Null(document.GetLayoutEngine().BoxTree);
    }
}
