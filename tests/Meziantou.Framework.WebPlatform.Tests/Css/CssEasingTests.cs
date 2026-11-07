using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class CssEasingTests
{
    [Theory]
    [InlineData("linear", 0.3, 0.3)]
    [InlineData("linear", -2, -2)]
    [InlineData("ease", 0, 0)]
    [InlineData("ease", 1, 1)]
    [InlineData("ease", 0.5, 0.8024033877399112)]
    [InlineData("ease-in", 0.5, 0.3153568026389259)]
    [InlineData("ease-out", 0.5, 0.6846431973610741)]
    [InlineData("ease-in-out", 0.5, 0.5)]
    [InlineData("ease", -0.5, -0.2)] // tangent through P0 and P1
    [InlineData("ease", 1.5, 1)] // tangent through P2 and P3 (horizontal)
    [InlineData("cubic-bezier(0, 0.2333333333333333, 1, 0.2333333333333333)", 0.5, 0.3)] // the easing of interpolation-testcommon.js
    [InlineData("cubic-bezier(0, 1.5, 1, 1.5)", 0.5, 1.25)]
    [InlineData("cubic-bezier(0, 0, 1, 1)", -1, -1)]
    [InlineData("cubic-bezier(0, 0, 1, 1)", 2, 2)]
    [InlineData("cubic-bezier(calc(0.5 * 0.5), 0.1, 0.25, 1)", 0.5, 0.8024033877399112)]
    public void Evaluate_CubicBezier(string easing, double input, double expected)
    {
        var function = CssEasing.Parse(easing);
        Assert.NotNull(function);
        Assert.Equal(expected, function.Evaluate(input), 1e-6);
    }

    [Theory]
    [InlineData("steps(4)", 0.3, false, 0.25)]
    [InlineData("steps(4, end)", 1, false, 1)]
    [InlineData("steps(4, jump-start)", 0.3, false, 0.5)]
    [InlineData("steps(4, jump-none)", 0.3, false, 1d / 3)]
    [InlineData("steps(4, jump-both)", 0.3, false, 0.4)]
    [InlineData("steps(1, start)", 0, false, 1)]
    [InlineData("steps(1, start)", 0, true, 0)]
    [InlineData("step-start", 0.5, false, 1)]
    [InlineData("step-end", 0.5, false, 0)]
    [InlineData("step-end", 1, false, 1)]
    [InlineData("steps(2, start)", -0.1, false, 0)]
    [InlineData("steps(2, start)", -0.5, true, -0.5)]
    [InlineData("steps(2, end)", 1.5, false, 1.5)]
    [InlineData("steps(2, jump-none)", 1, false, 1)]
    [InlineData("steps(calc(2 * 2))", 0.3, false, 0.25)]
    public void Evaluate_Steps(string easing, double input, bool beforeFlag, double expected)
    {
        var function = CssEasing.Parse(easing);
        Assert.NotNull(function);
        Assert.Equal(expected, function.Evaluate(input, beforeFlag), 1e-9);
    }

    [Theory]
    [InlineData("linear(0, 0.25, 1)", 0.25, 0.125)]
    [InlineData("linear(0, 0.25, 1)", 0.75, 0.625)]
    [InlineData("linear(0, 0.25, 1)", -0.5, -0.25)]
    [InlineData("linear(0, 0.25, 1)", 1.5, 1.75)]
    [InlineData("linear(0 0%, 0.5 50% 70%, 1 100%)", 0.6, 0.5)]
    [InlineData("linear(0, 1 50%, 0 50%, 1)", 0.5, 0)]
    [InlineData("linear(0, 1 50%, 0 50%, 1)", 0.75, 0.5)]
    public void Evaluate_Linear(string easing, double input, double expected)
    {
        var function = CssEasing.Parse(easing);
        Assert.NotNull(function);
        Assert.Equal(expected, function.Evaluate(input), 1e-9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("ease, linear")]
    [InlineData("cubic-bezier(2, 0, 1, 1)")]
    [InlineData("cubic-bezier(0, 0, 1)")]
    [InlineData("steps(0)")]
    [InlineData("steps(1, jump-none)")]
    [InlineData("steps(1.5)")]
    [InlineData("steps(2, middle)")]
    [InlineData("linear(0)")]
    [InlineData("linear(0 10px, 1)")]
    public void Parse_Invalid(string easing) => Assert.Null(CssEasing.Parse(easing));

    [Fact]
    public void Parse_Linear_IsKeyword() => Assert.True(CssEasing.Parse("linear")!.IsLinear);
}
