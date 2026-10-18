using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

public sealed class CssTransitionsTests : IDisposable
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

    [Fact]
    public void InterpolationTestCommonPattern()
    {
        // css/support/interpolation-testcommon.js: the start value is flushed, then the transition starts with a negative delay
        var document = Load("<div id=target style='width: 0px'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        Assert.Equal("0px", GetComputed(target, "width"));
        target.Style.TransitionDuration = "100s";
        target.Style.TransitionDelay = "-50s";
        target.Style.TransitionTimingFunction = "linear";
        target.Style.TransitionProperty = "width";
        target.Style.SetProperty("width", "100px");
        Assert.Equal("50px", GetComputed(target, "width"));
        var transition = Assert.IsType<CssTransition>(Assert.Single(target.GetAnimations()));
        Assert.Equal("width", transition.TransitionProperty);
    }

    [Fact]
    public void TransitionAll()
    {
        var document = Load("<div id=target style='color: black; opacity: 1'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        GetComputed(target, "color");
        target.Style.SetProperty("transition", "all 100s linear -50s");
        target.Style.SetProperty("color", "rgb(0, 0, 200)");
        target.Style.SetProperty("opacity", "0");
        Assert.Equal("rgb(0, 0, 100)", GetComputed(target, "color"));
        Assert.Equal("0.5", GetComputed(target, "opacity"));
    }

    [Fact]
    public void DiscreteValues_NeedAllowDiscrete()
    {
        var document = Load("<div id=target style='display: block; transition: all 100s linear -50s'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        GetComputed(target, "display");
        target.Style.SetProperty("display", "flex");
        Assert.Equal("flex", GetComputed(target, "display"));
        Assert.Empty(target.GetAnimations());

        target.Style.SetProperty("transition", "all 100s linear -60s allow-discrete");
        GetComputed(target, "display");
        target.Style.SetProperty("display", "grid");
        Assert.Equal("grid", GetComputed(target, "display"));
        Assert.Single(target.GetAnimations());
    }

    [Fact]
    public void NoTransitionWithoutBeforeChangeStyle()
    {
        var document = Load("<div id=target style='transition: width 1s'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        target.Style.SetProperty("width", "100px");
        Assert.Equal("100px", GetComputed(target, "width"));
        Assert.Empty(target.GetAnimations());
    }

    [Fact]
    public void Events()
    {
        var document = Load("<div id=target style='width: 0px; transition: width 100ms linear'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        var events = new List<string>();
        foreach (var type in new[] { "transitionrun", "transitionstart", "transitionend", "transitioncancel" })
        {
            target.AddEventListener(type, e => events.Add(e.Type + ":" + ((TransitionEvent)e).PropertyName));
        }

        GetComputed(target, "width");
        target.Style.SetProperty("width", "100px");
        Advance(50);
        Assert.Equal(["transitionrun:width", "transitionstart:width"], events);
        Advance(200);
        Assert.Equal(["transitionrun:width", "transitionstart:width", "transitionend:width"], events);
        Assert.Equal("100px", GetComputed(target, "width"));
        Assert.Empty(target.GetAnimations());
    }

    [Fact]
    public void ChangingTheEndValueReversesTheTransition()
    {
        var document = Load("<div id=target style='width: 0px; transition: width 100s linear'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        GetComputed(target, "width");
        target.Style.SetProperty("width", "100px");
        GetComputed(target, "width");
        Advance(25_000);
        Assert.Equal(25d, double.Parse(GetComputed(target, "width").TrimEnd('p', 'x'), System.Globalization.CultureInfo.InvariantCulture), 1);

        // Back to the start value: the reversed transition is shortened
        target.Style.SetProperty("width", "0px");
        var transition = Assert.IsType<CssTransition>(Assert.Single(target.GetAnimations()));
        Assert.True(transition.Effect!.GetTiming().Duration.Value is double duration && duration < 30_000, "The reversed transition is shortened");
    }

    [Fact]
    public void ImportantDeclarationsDoNotWinOverTransitions()
    {
        var document = Load("<div id=target style='width: 0px !important; transition: width 100s linear -50s'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        GetComputed(target, "width");
        target.Style.SetProperty("width", "100px", "important");
        Assert.Equal("50px", GetComputed(target, "width"));
    }

    [Fact]
    public void RemovingTheTransitionPropertyCancelsTheTransition()
    {
        var document = Load("<div id=target style='width: 0px; transition: width 100s linear'></div>");
        var target = (HtmlElement)document.GetElementById("target")!;
        GetComputed(target, "width");
        target.Style.SetProperty("width", "100px");
        var transition = Assert.Single(target.GetAnimations());
        target.Style.SetProperty("transition", "none");
        Assert.Empty(target.GetAnimations());
        Assert.Equal(AnimationPlayState.Idle, transition.PlayState);
        Assert.Equal("100px", GetComputed(target, "width"));
    }
}
