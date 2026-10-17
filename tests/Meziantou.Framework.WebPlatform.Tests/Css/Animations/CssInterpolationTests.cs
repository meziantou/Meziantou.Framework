using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

public sealed class CssInterpolationTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Interpolates the computed values of <paramref name="from"/> and <paramref name="to"/> on an element, and returns the
    /// computed value of the result and the computed value of <paramref name="expected"/> (as in interpolation-testcommon.js).
    /// </summary>
    private (string Actual, string Expected) Interpolate(string property, string from, string to, double progress, string expected, string? parentStyle = null, CssCombineOperation? operation = null)
    {
        var document = _contexts.Create(new BrowsingContextOptions()).Navigate(_ => $"<div style='{parentStyle}'><div id=target></div><div id=from></div><div id=to></div><div id=expected></div></div>");
        var engine = document.GetStyleEngine();
        string Computed(string id, string value)
        {
            var element = document.GetElementById(id)!;
            ((HtmlElement)element).Style.SetProperty(property, value);
            return document.DefaultView!.GetComputedStyle(element).GetPropertyValue(property);
        }

        // The computed values (the custom properties have no resolved value)
        var fromValue = Computed("from", from);
        var toValue = Computed("to", to);
        if (!property.StartsWith("--", StringComparison.Ordinal))
        {
            fromValue = engine.GetStyle(document.GetElementById("from")!).GetComputedValue(property);
            toValue = engine.GetStyle(document.GetElementById("to")!).GetComputedValue(property);
        }
        var context = new CssInterpolationContext(engine.GetStyle(document.GetElementById("target")!));
        var result = operation switch
        {
            CssCombineOperation.Add => CssInterpolation.Add(context, property, fromValue, toValue),
            CssCombineOperation.Accumulate => CssInterpolation.Accumulate(context, property, fromValue, toValue),
            _ => CssInterpolation.Prepare(context, property, fromValue, toValue).Interpolate(progress),
        };

        return (Computed("target", result), Computed("expected", expected));
    }

    private void AssertInterpolation(string property, string from, string to, double progress, string expected, string? parentStyle = null)
    {
        var (actual, expectedValue) = Interpolate(property, from, to, progress, expected, parentStyle);
        Assert.Equal(expectedValue, actual);
    }

    [Theory]
    [InlineData("width", "0px", "100px", 0.3, "30px")]
    [InlineData("width", "0px", "100px", -0.5, "0px")] // clamped to the range of the property
    [InlineData("width", "10px", "20px", 1.5, "25px")]
    [InlineData("margin-left", "0px", "100px", -0.5, "-50px")]
    [InlineData("margin-left", "1em", "20px", 0.5, "18px")]
    [InlineData("left", "0%", "100px", 0.25, "calc(0% + 25px)")]
    [InlineData("left", "0%", "100px", 1, "calc(0% + 100px)")]
    [InlineData("left", "0%", "100px", 0, "0%")]
    [InlineData("left", "calc(50% - 10px)", "30px", 0.5, "calc(25% + 10px)")]
    [InlineData("opacity", "0", "1", 1.5, "1")]
    [InlineData("opacity", "0.2", "0.8", 0.5, "0.5")]
    [InlineData("z-index", "0", "5", 0.5, "3")] // rounded towards positive infinity
    [InlineData("z-index", "0", "5", 0.3, "2")]
    [InlineData("z-index", "-2", "2", 0.375, "0")]
    [InlineData("font-weight", "100", "900", 0.5, "500")]
    [InlineData("font-size", "10px", "20px", 0.5, "15px")]
    [InlineData("rotate", "10deg", "20deg", 0.5, "15deg")]
    [InlineData("flex-grow", "1", "2", -2, "0")]
    [InlineData("border-top-left-radius", "10px 20px", "30px 40px", 0.5, "20px 30px")]
    public void Interpolate_Numeric(string property, string from, string to, double progress, string expected) => AssertInterpolation(property, from, to, progress, expected);

    [Theory]
    [InlineData("0px", "10px -20px", 0.125, "1.25px -2.5px", null)] // a single value is both insets
    [InlineData("10px", "30px", 0.5, "20px", null)]
    [InlineData("auto", "auto", 0.5, "auto", null)]
    [InlineData("auto", "10px -20px", 0.5, "5.5px -9.5px", "font-size: 10px")] // auto is 1px: half the thickness of 1px, 1px at least
    [InlineData("auto", "10px", 0.5, "6px", "font-size: 40px")] // half the thickness of 4px
    [InlineData("10%", "auto", 0.5, "calc(5% + 1px)", "font-size: 40px")]
    public void Interpolate_TextDecorationInset(string from, string to, double progress, string expected, string? parentStyle)
        => AssertInterpolation("text-decoration-inset", from, to, progress, expected, parentStyle);

    [Theory]
    [InlineData("clip-path", "inset(10% 20% 30% 40%)", "inset(50% 20% 30% 20%)", 0.5, "inset(30% 20% 30% 30%)")] // the omitted offsets are restored
    [InlineData("clip-path", "inset(10px)", "inset(30px round 20px)", 0.5, "inset(20px round 10px)")]
    [InlineData("clip-path", "xywh(40% 10% 40% 60%)", "inset(50% 20% 30% 20%)", 0, "inset(10% 20% 30% 40%)")]
    [InlineData("shape-outside", "polygon(0px 0px, 100px 0px, 100px 100px)", "polygon(round 20px, 0px 0px, 100px 0px, 100px 100px)", 0.5, "polygon(round 10px, 0px 0px, 100px 0px, 100px 100px)")]
    [InlineData("shape-outside", "path('M 50 60 H 70') padding-box", "path('M 10 140 H 270') padding-box", 0.5, "path(\"M 30 100 H 170\") padding-box")]
    [InlineData("clip-path", "circle(10px at 20px 20px)", "circle(30px at 40px 40px)", 0.5, "circle(20px at 30px 30px)")]
    [InlineData("clip-path", "circle(farthest-side at 20px 20px)", "circle(farthest-side at 40px 40px)", 0.25, "circle(farthest-side at 20px 20px)")] // discrete: a keyword radius
    public void Interpolate_BasicShapes(string property, string from, string to, double progress, string expected) => AssertInterpolation(property, from, to, progress, expected);

    [Theory]
    [InlineData("color", "black", "orange", 0.3, "rgb(77, 50, 0)")]
    [InlineData("color", "rgba(0, 0, 0, 0)", "rgb(0, 0, 255)", 0.5, "rgba(0, 0, 255, 0.5)")]
    [InlineData("background-color", "transparent", "green", 0.5, "rgba(0, 128, 0, 0.5)")]
    [InlineData("background-color", "currentcolor", "rgba(0, 0, 255, 0.4)", 0.5, "rgba(0, 91, 255, 0.7)")]
    [InlineData("color", "oklab(0 0 0)", "oklab(1 0 0)", 1.5, "oklab(1 0 0)")]
    [InlineData("color", "black", "oklab(1 0 0)", 0.5, "oklab(0.5 0 0)")]
    public void Interpolate_Color(string property, string from, string to, double progress, string expected)
        => AssertInterpolation(property, from, to, progress, expected, parentStyle: "color: rgb(0, 128, 255)");

    [Theory]
    [InlineData("matrix(1, 0, 0, 1, 0, 0) rotate(0deg)", "matrix(2, 0, 0, 2, 0, 0) rotate(360deg)", "matrix(1.5, 0, 0, 1.5, 0, 0) rotate(180deg)")] // a pair of matrices does not collapse the functions after it
    [InlineData("skewX(0deg) rotate(0deg)", "skewY(0deg) rotate(360deg)", "matrix(1, 0, 0, 1, 0, 0)")] // no common primitive: interpolated as matrices
    [InlineData("skewX(0deg) rotate(0deg)", "skewX(60deg) rotate(360deg)", "skewX(30deg) rotate(180deg)")]
    [InlineData("skewX(20deg)", "none", "skewX(10deg)")]
    [InlineData("perspective(0px) translateZ(0.5px)", "perspective(3px) translateZ(0.5px)", "perspective(1.5px) translateZ(0.5px)")] // clamped to 1px
    public void Interpolate_TransformLists(string from, string to, string expected) => AssertInterpolation("transform", from, to, 0.5, expected);

    [Theory]
    [InlineData("background-position-x", "0px, 10px", "20px", 0.5, "10px, 15px")]
    [InlineData("background-position-x", "0px, 10px", "20px, 30px, 40px", 0.5, "10px, 20px, 20px, 15px, 15px, 25px")]
    public void Interpolate_RepeatableList(string property, string from, string to, double progress, string expected) => AssertInterpolation(property, from, to, progress, expected);

    [Theory]
    [InlineData("visibility", "visible", "hidden", 0.5, "visible")]
    [InlineData("visibility", "visible", "hidden", 1, "hidden")]
    [InlineData("visibility", "hidden", "visible", -0.1, "hidden")]
    [InlineData("visibility", "hidden", "collapse", 0.4, "hidden")]
    [InlineData("visibility", "hidden", "collapse", 0.6, "collapse")]
    [InlineData("display", "none", "block", 0.1, "block")]
    [InlineData("display", "block", "none", 0.9, "block")]
    [InlineData("display", "block", "none", 1, "none")]
    [InlineData("display", "block", "flex", 0.4, "block")]
    [InlineData("display", "block", "flex", 0.6, "flex")]
    [InlineData("line-height", "normal", "2", 0.4, "normal")]
    [InlineData("line-height", "normal", "2", 0.6, "2")]
    [InlineData("box-sizing", "border-box", "content-box", 0.6, "content-box")]
    [InlineData("--foo", "a", "b", 0.4, "a")]
    [InlineData("--foo", "1px", "2px", 0.6, "2px")]
    public void Interpolate_Discrete(string property, string from, string to, double progress, string expected) => AssertInterpolation(property, from, to, progress, expected);

    [Fact]
    public void Interpolate_Display_IsDiscreteForTransitions()
    {
        var pair = CssInterpolation.Prepare(CssInterpolationContext.Empty, "display", "none", "block");
        Assert.True(pair.IsDiscrete);
        Assert.Equal("block", pair.Interpolate(0.1));
        Assert.False(CssInterpolation.Prepare(CssInterpolationContext.Empty, "width", "1px", "2px").IsDiscrete);
        Assert.True(CssInterpolation.Prepare(CssInterpolationContext.Empty, "width", "1px", "auto").IsDiscrete);
    }

    [Theory]
    [InlineData("width", "10px", "20px", "30px")]
    [InlineData("left", "10%", "20px", "calc(10% + 20px)")]
    [InlineData("color", "rgb(50, 50, 50)", "rgb(10, 10, 10)", "rgb(60, 60, 60)")]
    [InlineData("color", "rgb(250, 50, 50)", "rgb(10, 10, 10)", "rgb(255, 60, 60)")]
    [InlineData("box-sizing", "border-box", "content-box", "content-box")]
    [InlineData("background-position-x", "10px, 20px", "5px", "15px, 25px")]
    public void Add(string property, string underlying, string value, string expected)
    {
        var (actual, expectedValue) = Interpolate(property, underlying, value, 0, expected, operation: CssCombineOperation.Add);
        Assert.Equal(expectedValue, actual);
    }

    [Fact]
    public void AccumulateIterations()
    {
        Assert.Equal("35px", CssInterpolation.AccumulateIterations(CssInterpolationContext.Empty, "width", "10px", 3, "5px"));
        Assert.Equal("5px", CssInterpolation.AccumulateIterations(CssInterpolationContext.Empty, "width", "10px", 0, "5px"));
        Assert.Equal("b", CssInterpolation.AccumulateIterations(CssInterpolationContext.Empty, "box-sizing", "a", 3, "b"));
    }

    [Fact]
    public void AnimatableLonghands()
    {
        Assert.Contains("width", CssInterpolation.AnimatableLonghands);
        Assert.Contains("box-sizing", CssInterpolation.AnimatableLonghands);
        Assert.DoesNotContain("animation-name", CssInterpolation.AnimatableLonghands);
        Assert.DoesNotContain("margin", CssInterpolation.AnimatableLonghands);
    }
}
