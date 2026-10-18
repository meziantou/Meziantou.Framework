using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

public sealed class WebAnimationsTests : IDisposable
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

    /// <summary>Advances the clock and runs the tasks and the frames that are due.</summary>
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

    private static Keyframe[] OpacityKeyframes() =>
    [
        new Keyframe { Properties = { ["opacity"] = "0" } },
        new Keyframe { Properties = { ["opacity"] = "1" } },
    ];

    [Fact]
    public void Animate_IsPendingUntilTheNextFrame()
    {
        var document = Load("<div id=target></div>");
        var animation = document.GetElementById("target")!.Animate(OpacityKeyframes(), 1000);
        Assert.True(animation.Pending);
        Assert.Equal(AnimationPlayState.Running, animation.PlayState);
        Assert.Null(animation.StartTime);
        Assert.Equal(0d, animation.CurrentTime!.Value.Value);
        Assert.False(animation.Ready.IsCompleted);

        Advance(20);
        Assert.False(animation.Pending);
        Assert.True(animation.Ready.IsCompletedSuccessfully);
        Assert.NotNull(animation.StartTime);
        Assert.Same(document.Timeline, animation.Timeline);
    }

    [Fact]
    public void Animate_FinishesAndFiresTheFinishEvent()
    {
        var document = Load("<div id=target></div>");
        var animation = document.GetElementById("target")!.Animate(OpacityKeyframes(), 100);
        var events = new List<string>();
        animation.OnFinish = e => events.Add(e.Type);
        Advance(200);
        Assert.Equal(AnimationPlayState.Finished, animation.PlayState);
        Assert.Equal(100d, animation.CurrentTime!.Value.Value);
        Assert.True(animation.Finished.IsCompletedSuccessfully);
        Assert.Equal(["finish"], events);
    }

    [Fact]
    public void Pause_HoldsTheCurrentTime()
    {
        var document = Load("<div id=target></div>");
        var animation = document.GetElementById("target")!.Animate(OpacityKeyframes(), 1000);
        animation.Pause();
        animation.CurrentTime = 500;
        Assert.Equal(AnimationPlayState.Paused, animation.PlayState);
        Assert.False(animation.Pending);
        Advance(100);
        Assert.Equal(500d, animation.CurrentTime!.Value.Value);

        var timing = animation.Effect!.GetComputedTiming();
        Assert.Equal(0.5, timing.Progress);
        Assert.Equal(0d, timing.CurrentIteration);
    }

    [Fact]
    public void Cancel_RejectsTheFinishedPromise()
    {
        var document = Load("<div id=target></div>");
        var animation = document.GetElementById("target")!.Animate(OpacityKeyframes(), 1000);
        var finished = animation.Finished;
        var events = new List<string>();
        animation.OnCancel = e => events.Add(e.Type);
        animation.Cancel();
        Assert.Equal(AnimationPlayState.Idle, animation.PlayState);
        Assert.True(finished.IsFaulted);
        Assert.NotSame(finished, animation.Finished);
        Advance(20);
        Assert.Equal(["cancel"], events);
    }

    [Fact]
    public void GetComputedTiming_PhasesAndFill()
    {
        var document = Load("<div id=target></div>");
        var effect = new KeyframeEffect(document.GetElementById("target"), OpacityKeyframes(), new KeyframeEffectOptions { Delay = 100, Duration = 1000, Iterations = 2, Direction = PlaybackDirection.Alternate, Fill = FillMode.Both });
        var animation = new Animation(effect, document.Timeline);
        animation.CurrentTime = 0;
        Assert.Equal(0d, effect.GetComputedTiming().Progress); // before phase, filling backwards
        animation.CurrentTime = 1350;
        var timing = effect.GetComputedTiming();
        Assert.Equal(1d, timing.CurrentIteration);
        Assert.Equal(0.75, timing.Progress!.Value, 1e-9); // alternate: the second iteration plays backwards
        Assert.Equal(2100d, timing.EndTime.Value);
        Assert.Equal(2000d, timing.ActiveDuration.Value);
        animation.CurrentTime = 5000;
        Assert.Equal(0d, effect.GetComputedTiming().Progress); // after phase, filling forwards
    }

    [Fact]
    public void UpdateTiming_InvalidValues_Throw()
    {
        var document = Load("<div id=target></div>");
        var effect = new KeyframeEffect(document.GetElementById("target"), OpacityKeyframes());
        Assert.Throws<TypeErrorException>(() => effect.UpdateTiming(new OptionalEffectTiming { Duration = -1 }));
        Assert.Throws<TypeErrorException>(() => effect.UpdateTiming(new OptionalEffectTiming { Duration = "foo" }));
        Assert.Throws<TypeErrorException>(() => effect.UpdateTiming(new OptionalEffectTiming { Easing = "bounce" }));
        Assert.Throws<TypeErrorException>(() => effect.UpdateTiming(new OptionalEffectTiming { Iterations = -1 }));
        effect.UpdateTiming(new OptionalEffectTiming { Easing = "steps(2, end)", Duration = 10 });
        Assert.Equal("steps(2)", effect.GetTiming().Easing);
        Assert.Equal(10d, effect.GetTiming().Duration.Value);
    }

    [Fact]
    public void Keyframes_ComputedOffsetsAndInvalidValues()
    {
        var document = Load("<div id=target></div>");
        var effect = new KeyframeEffect(document.GetElementById("target"),
        [
            new Keyframe { Properties = { ["left"] = "10px", ["foo"] = "1" } },
            new Keyframe { Properties = { ["left"] = "invalid" } },
            new Keyframe { Offset = 0.8, Properties = { ["margin"] = "10px" } },
            new Keyframe { Properties = { ["left"] = "20px" } },
        ]);

        var keyframes = effect.GetKeyframes();
        Assert.Equal([0d, 0.4, 0.8, 1d], keyframes.Select(k => k.ComputedOffset));
        Assert.Equal("10px", keyframes[0].Properties["left"]);
        Assert.Empty(keyframes[1].Properties);
        Assert.Equal("10px", keyframes[2].Properties["margin"]);
        Assert.Throws<TypeErrorException>(() => effect.SetKeyframes([new Keyframe { Offset = 0.5 }, new Keyframe { Offset = 0.2 }]));
        Assert.Throws<TypeErrorException>(() => effect.SetKeyframes([new Keyframe { Offset = 1.5 }]));
    }

    [Fact]
    public void GetAnimations_ReturnsTheRelevantAnimations()
    {
        var document = Load("<div id=parent><div id=target></div></div>");
        var target = document.GetElementById("target")!;
        var animation1 = target.Animate(OpacityKeyframes(), 1000);
        var animation2 = target.Animate(OpacityKeyframes(), new KeyframeAnimationOptions { Duration = 1000, PseudoElement = "::before" });
        Assert.Equal([animation1], target.GetAnimations());
        Assert.Equal([animation2], target.GetAnimations(new GetAnimationsOptions { PseudoElement = "::before" }));
        Assert.Equal([animation1, animation2], document.GetElementById("parent")!.GetAnimations(new GetAnimationsOptions { Subtree = true }));
        Assert.Equal([animation1, animation2], document.GetAnimations());

        animation1.Finish();
        Assert.Equal([animation2], document.GetAnimations());
    }

    [Fact]
    public void Timeline_IsTheTimeOfTheFrames()
    {
        var document = Load("<div></div>");
        var time = document.Timeline.CurrentTime!.Value.Value;
        Assert.Equal(time, document.Timeline.CurrentTime!.Value.Value);
        double? frameTime = null;
        document.DefaultView!.RequestAnimationFrame(t => frameTime = t);
        Advance(20);
        Assert.NotNull(frameTime);
        Assert.Equal(frameTime, (double)document.Timeline.CurrentTime!.Value.Value);
    }

    private static string GetComputed(Element element, string property, string? pseudoElement = null) =>
        element.OwnerDocument!.DefaultView!.GetComputedStyle(element, pseudoElement).GetPropertyValue(property);

    private static Animation AnimateAt(Element element, Keyframe[] keyframes, double currentTime, KeyframeAnimationOptions? options = null)
    {
        var animation = element.Animate(keyframes, options ?? new KeyframeAnimationOptions { Duration = 1000, Fill = FillMode.Forwards });
        animation.Pause();
        animation.CurrentTime = currentTime;
        return animation;
    }

    [Fact]
    public void ComputedStyle_ReflectsTheAnimation()
    {
        var document = Load("<div id=target style='width: 50px'></div>");
        var target = document.GetElementById("target")!;
        var animation = AnimateAt(target, [new Keyframe { Properties = { ["width"] = "0px" } }, new Keyframe { Properties = { ["width"] = "100px" } }], 250);
        Assert.Equal("25px", GetComputed(target, "width"));

        animation.CurrentTime = 750;
        Assert.Equal("75px", GetComputed(target, "width"));

        animation.Cancel();
        Assert.Equal("50px", GetComputed(target, "width"));
    }

    [Fact]
    public void ComputedStyle_NeutralKeyframesUseTheUnderlyingValue()
    {
        var document = Load("<div id=target style='margin-left: 100px'></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["margin-left"] = "200px" } }], 500);
        Assert.Equal("150px", GetComputed(target, "margin-left"));
    }

    [Fact]
    public void ComputedStyle_ImportantDeclarationsWinOverAnimations()
    {
        var document = Load("<div id=target style='width: 50px !important; height: 10px'></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["width"] = "0px", ["height"] = "0px" } }, new Keyframe { Properties = { ["width"] = "100px", ["height"] = "100px" } }], 500);
        Assert.Equal("50px", GetComputed(target, "width"));
        Assert.Equal("50px", GetComputed(target, "height"));
    }

    [Fact]
    public void ComputedStyle_InheritsTheAnimatedValues()
    {
        var document = Load("<div id=parent><span id=child style='margin-left: 1em'></span></div>");
        var parent = document.GetElementById("parent")!;
        AnimateAt(parent, [new Keyframe { Properties = { ["font-size"] = "10px", ["color"] = "black" } }, new Keyframe { Properties = { ["font-size"] = "20px", ["color"] = "rgb(0, 0, 200)" } }], 500);
        var child = document.GetElementById("child")!;
        Assert.Equal("15px", GetComputed(child, "font-size"));
        Assert.Equal("15px", GetComputed(child, "margin-left"));
        Assert.Equal("rgb(0, 0, 100)", GetComputed(child, "color"));
    }

    [Fact]
    public void ComputedStyle_KeyframesWithVariablesAndRelativeUnits()
    {
        var document = Load("<div id=target style='--to: 40px; font-size: 10px'></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["width"] = "2em" } }, new Keyframe { Properties = { ["width"] = "var(--to)" } }], 500);
        Assert.Equal("30px", GetComputed(target, "width"));
    }

    [Fact]
    public void ComputedStyle_CompositeAddAndOrder()
    {
        var document = Load("<div id=target style='margin-left: 10px'></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["margin-left"] = "100px" } }, new Keyframe { Properties = { ["margin-left"] = "100px" } }], 500);
        AnimateAt(target, [new Keyframe { Properties = { ["margin-left"] = "5px" } }, new Keyframe { Properties = { ["margin-left"] = "5px" } }], 500, new KeyframeAnimationOptions { Duration = 1000, Composite = CompositeOperation.Add });
        Assert.Equal("105px", GetComputed(target, "margin-left"));
    }

    [Fact]
    public void ComputedStyle_LogicalProperties()
    {
        var document = Load("<div id=target style='direction: rtl'></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["margin-inline-start"] = "0px" } }, new Keyframe { Properties = { ["margin-inline-start"] = "100px" } }], 500);
        Assert.Equal("50px", GetComputed(target, "margin-right"));
        Assert.Equal("0px", GetComputed(target, "margin-left"));
    }

    [Fact]
    public void ComputedStyle_PseudoElement()
    {
        var document = Load("<style>#target::before { content: 'a' }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["opacity"] = "0" } }, new Keyframe { Properties = { ["opacity"] = "1" } }], 500, new KeyframeAnimationOptions { Duration = 1000, PseudoElement = "::before" });
        Assert.Equal("0.5", GetComputed(target, "opacity", "::before"));
        Assert.Equal("1", GetComputed(target, "opacity"));
    }

    [Fact]
    public void ComputedStyle_CustomProperties()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;
        AnimateAt(target, [new Keyframe { Properties = { ["--x"] = "a" } }, new Keyframe { Properties = { ["--x"] = "b" } }], 600);
        Assert.Equal("b", GetComputed(target, "--x"));
    }

    [Fact]
    public void ComputedStyle_RunningAnimationAdvancesWithTheFrames()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;
        target.Animate([new Keyframe { Properties = { ["opacity"] = "0" } }, new Keyframe { Properties = { ["opacity"] = "1" } }], 1000);
        Assert.Equal("0", GetComputed(target, "opacity"));
        Advance(20);
        var first = double.Parse(GetComputed(target, "opacity"), System.Globalization.CultureInfo.InvariantCulture);
        Advance(500);
        var second = double.Parse(GetComputed(target, "opacity"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(second > first, $"{second} > {first}");
        Advance(1000);
        Assert.Equal("1", GetComputed(target, "opacity"));
    }

    [Fact]
    public void CommitStyles_WritesTheAnimatedValuesToTheStyleAttribute()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;
        var animation = AnimateAt(target, [new Keyframe { Properties = { ["width"] = "0px" } }, new Keyframe { Properties = { ["width"] = "100px" } }], 300);
        animation.CommitStyles();
        animation.Cancel();
        Assert.Equal("width: 30px;", target.GetAttribute("style"));
        Assert.Equal("30px", GetComputed(target, "width"));
    }

    [Theory]
    [InlineData("scaleX(1)", "scaleX(2)", "scaleX(1.5)")]
    [InlineData("scaleX(2)", "scaleY(2)", "scale(1.5)")]
    [InlineData("translate(50px, 0px)", "translate(100px, 0px)", "translate(75px)")]
    [InlineData("translateY(0%)", "translateX(50%)", "translate(25%)")]
    [InlineData("translate3d(0, 0, -50px)", "translateZ(50px)", "translate3d(0px, 0px, 0px)")]
    [InlineData("skewX(0deg)", "skewX(60deg)", "skewX(30deg)")]
    public void CommitStyles_Transform_WritesTheShortestFormOfTheFunctions(string from, string to, string expected)
    {
        var document = Load("<div id=target></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        var animation = AnimateAt(target, [new Keyframe { Properties = { ["transform"] = from } }, new Keyframe { Properties = { ["transform"] = to } }], 500);
        animation.CommitStyles();
        animation.Cancel();
        Assert.Equal(expected, target.Style.Transform);
    }

    [Fact]
    public void CommitStyles_PseudoElement_Throws()
    {
        var document = Load("<div id=target></div>");
        var target = document.GetElementById("target")!;
        var animation = AnimateAt(target, OpacityKeyframes(), 300, new KeyframeAnimationOptions { Duration = 1000, PseudoElement = "::before" });
        Assert.Equal("NoModificationAllowedError", Assert.Throws<DomException>(animation.CommitStyles).Name);
    }
}
