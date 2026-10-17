using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

public sealed class CssAnimationsTests : IDisposable
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

    private static string GetComputed(Element element, string property, string? pseudoElement = null) =>
        element.OwnerDocument!.DefaultView!.GetComputedStyle(element, pseudoElement).GetPropertyValue(property);

    [Theory]
    [InlineData("linear", "50px")]
    [InlineData("steps(2, start)", "100px")]
    [InlineData("cubic-bezier(0, 0.2333333333333333, 1, 0.2333333333333333)", "30px")]
    public void PausedAnimationWithNegativeDelay(string easing, string expected)
    {
        var document = Load($"<style>@keyframes a {{ from {{ width: 0px }} to {{ width: 100px }} }} #target {{ animation: a 100s {easing} -50s paused }}</style><div id=target></div>");
        Assert.Equal(expected, GetComputed(document.GetElementById("target")!, "width"));
    }

    [Fact]
    public void KeyframeTimingFunctionAndImplicitKeyframes()
    {
        var document = Load("<style>@keyframes a { 50% { width: 100px; animation-timing-function: steps(1) } } #target { width: 20px; animation: a 100s linear -25s paused }</style><div id=target></div>");
        Assert.Equal("60px", GetComputed(document.GetElementById("target")!, "width"));

        var document2 = Load("<style>@keyframes a { 50% { width: 100px; animation-timing-function: steps(1) } } #target { width: 20px; animation: a 100s linear -75s paused }</style><div id=target></div>");
        Assert.Equal("100px", GetComputed(document2.GetElementById("target")!, "width"));
    }

    [Fact]
    public void CascadeLayersAndLastRuleWin()
    {
        var document = Load("<style>@layer l { @keyframes a { to { width: 1000px } } } @keyframes a { to { width: 10px } } @keyframes a { to { width: 100px } } #target { width: 0px; animation: a 100s -50s paused linear }</style><div id=target></div>");
        Assert.Equal("50px", GetComputed(document.GetElementById("target")!, "width"));
    }

    [Fact]
    public void ImportantDeclarationsInKeyframesAreIgnored()
    {
        var document = Load("<style>@keyframes a { to { width: 100px !important; height: 100px } } #target { width: 0px; height: 0px; animation: a 100s -50s paused linear }</style><div id=target></div>");
        Assert.Equal("0px", GetComputed(document.GetElementById("target")!, "width"));
        Assert.Equal("50px", GetComputed(document.GetElementById("target")!, "height"));
    }

    [Fact]
    public void PathInterpolation()
    {
        var document = Load("""
            <svg><style>@keyframes a { 0% { d: path("M 0 0 L 100 100") } 100% { d: path("m 100 100 l 100 100") } } #target { animation: a 2s linear -1s paused }</style><path id=target /></svg>
            """);
        Assert.Equal("path(\"M 50 50 L 150 150\")", GetComputed(document.GetElementById("target")!, "d"));
    }

    [Fact]
    public void GetAnimations_ReturnsTheCssAnimations()
    {
        var document = Load("<style>@keyframes a { to { width: 100px } } @keyframes b { to { height: 100px } } #target { animation: a 100s, none, b 10s }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        var animations = target.GetAnimations();
        Assert.Equal(["a", "b"], animations.Cast<CssAnimation>().Select(a => a.AnimationName));
        Assert.Equal(100_000d, animations[0].Effect!.GetTiming().Duration.Value);
        Assert.Same(animations, animations); // stable list
        Assert.Same(animations[0], target.GetAnimations()[0]);
    }

    [Fact]
    public void UnknownKeyframesDoNotCreateAnimations()
    {
        var document = Load("<style>#target { animation: unknown 1s }</style><div id=target></div>");
        Assert.Empty(document.GetElementById("target")!.GetAnimations());
    }

    [Fact]
    public void Events()
    {
        var document = Load("<style>@keyframes a { to { width: 100px } } .animated { animation: a 100ms 2 }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        var events = new List<string>();
        foreach (var type in new[] { "animationstart", "animationiteration", "animationend", "animationcancel" })
        {
            target.AddEventListener(type, e => events.Add(e.Type + ":" + ((AnimationEvent)e).ElapsedTime.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        target.ClassName = "animated";
        Advance(50);
        Assert.Equal(["animationstart:0"], events);
        Advance(300);
        Assert.Equal(["animationstart:0", "animationiteration:0.1", "animationend:0.2"], events);
    }

    [Fact]
    public void RemovingTheAnimationNameCancelsTheAnimation()
    {
        var document = Load("<style>@keyframes a { to { width: 100px } } .animated { animation: a 100s }</style><div id=target class=animated></div>");
        var target = document.GetElementById("target")!;
        var events = new List<string>();
        target.AddEventListener("animationcancel", e => events.Add(e.Type));
        Advance(50);
        var animation = Assert.Single(target.GetAnimations());
        target.ClassName = "";
        Assert.Empty(target.GetAnimations());
        Assert.Equal(AnimationPlayState.Idle, animation.PlayState);
        Advance(50);
        Assert.Equal(["animationcancel"], events);
    }

    [Fact]
    public void PlayStateFollowsTheStyle()
    {
        var document = Load("<style>@keyframes a { to { width: 100px } } #target { animation: a 100s linear }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        Advance(100);
        var animation = Assert.Single(target.GetAnimations());
        Assert.Equal(AnimationPlayState.Running, animation.PlayState);
        target.SetAttribute("style", "animation-play-state: paused");
        Assert.Equal(AnimationPlayState.Paused, animation.PlayState);
        target.RemoveAttribute("style");
        Assert.Equal(AnimationPlayState.Running, animation.PlayState);

        // The play state is no longer updated by the style once it is changed through the API
        animation.Pause();
        target.SetAttribute("style", "animation-play-state: running");
        Assert.Equal(AnimationPlayState.Paused, animation.PlayState);
    }

    [Fact]
    public void DisplayNoneCancelsTheAnimations()
    {
        var document = Load("<style>@keyframes a { to { width: 100px } } #target { animation: a 100s linear }</style><div id=parent><div id=target></div></div>");
        var target = document.GetElementById("target")!;
        var animation = Assert.Single(target.GetAnimations());
        document.GetElementById("parent")!.SetAttribute("style", "display: none");
        Assert.Empty(target.GetAnimations());
        Assert.Equal(AnimationPlayState.Idle, animation.PlayState);
    }

    [Fact]
    public void RemovedElementsCancelTheirAnimations()
    {
        var document = Load("<style>@keyframes a { to { width: 100px } } #target { animation: a 100s linear }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        var animation = Assert.Single(target.GetAnimations());
        target.Remove();
        Advance(20);
        Assert.Equal(AnimationPlayState.Idle, animation.PlayState);
    }

    [Fact]
    public void PseudoElementAnimations()
    {
        var document = Load("<style>@keyframes a { from { opacity: 0 } to { opacity: 1 } } #target::before { content: ''; animation: a 100s -50s paused linear }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        Assert.Equal("0.5", GetComputed(target, "opacity", "::before"));
        var animation = Assert.Single(document.GetAnimations());
        Assert.Equal("::before", ((KeyframeEffect)animation.Effect!).PseudoElement);
    }
}
