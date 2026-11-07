using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class CssCalcTests
{
    // em = 16px, rem = 10px, vw = 1% of 1000px; other relative units are unknown
    private static readonly Func<double, string, (double Value, string Unit)?> Resolve = static (value, unit) => unit switch
    {
        "em" => (value * 16, "px"),
        "rem" => (value * 10, "px"),
        "vw" => (value * 10, "px"),
        _ => null,
    };

    private static readonly CssCalcContext NoPercentage = new() { ResolveDimension = Resolve };
    private static readonly CssCalcContext LengthPercentage = new() { ResolveDimension = Resolve, PercentageUnit = "px" };
    private static readonly CssCalcContext AnglePercentage = new() { PercentageUnit = "deg" };
    private static readonly CssCalcContext ResolvedPercentage = new() { ResolveDimension = Resolve, PercentageUnit = "px", PercentageBasis = (200, "px") };
    private static readonly CssCalcContext NumberPercentage = new() { PercentageUnit = "" };

    private static CssCalcValue? Evaluate(string input, CssCalcContext context)
    {
        foreach (var value in CssParser.ParseListOfComponentValues(input))
        {
            if (value is CssFunction function)
                return CssCalc.Evaluate(function, context);
        }

        throw new ArgumentException("No function in " + input, nameof(input));
    }

    private static string? Serialize(string input, CssCalcContext context) => Evaluate(input, context)?.Serialize();

    [Theory]
    [InlineData("calc(1 + 2)", "3")]
    [InlineData("calc(1 + 2 * 3)", "7")]
    [InlineData("calc((1 + 2) * 3)", "9")]
    [InlineData("calc(10 - 4 - 3)", "3")]
    [InlineData("calc(12 / 3 / 2)", "2")]
    [InlineData("calc(2*3)", "6")]
    [InlineData("calc(6/4)", "1.5")]
    [InlineData("calc( 1  +  2 )", "3")]
    [InlineData("calc(1 + -2)", "-1")]
    [InlineData("calc(((((1)))))", "1")]
    [InlineData("calc(1 / 3)", "0.333333")]
    [InlineData("calc(1px + 2px)", "3px")]
    [InlineData("calc(2px * 3)", "6px")]
    [InlineData("calc(3 * 2px)", "6px")]
    [InlineData("calc(10px / 4)", "2.5px")]
    [InlineData("calc(1px - 3px)", "-2px")]
    [InlineData("calc(calc(1px + 1px) * 2)", "4px")]
    [InlineData("CALC(1PX + 1Px)", "2px")]
    [InlineData("-webkit-calc(1px + 1px)", "2px")]
    [InlineData("calc(1px * (2 + (3 - 1) * 2))", "6px")]
    [InlineData("calc(10px / 2px)", "5")]
    [InlineData("calc(10px / 2px * 1deg)", "5deg")]
    [InlineData("calc(1px * 1px / 1px)", "1px")]
    [InlineData("calc(0.1 + 0.2)", "0.3")]
    public void Arithmetic(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, NoPercentage));
    }

    [Theory]
    [InlineData("calc(1in)", "96px")]
    [InlineData("calc(2.54cm)", "96px")]
    [InlineData("calc(25.4mm)", "96px")]
    [InlineData("calc(4q)", "3.779528px")]
    [InlineData("calc(72pt)", "96px")]
    [InlineData("calc(1pc)", "16px")]
    [InlineData("calc(1in + 4px)", "100px")]
    [InlineData("calc(0.5turn)", "180deg")]
    [InlineData("calc(100grad)", "90deg")]
    [InlineData("calc(1rad)", "57.29578deg")]
    [InlineData("calc(1turn - 90deg)", "270deg")]
    [InlineData("calc(500ms)", "0.5s")]
    [InlineData("calc(1s + 500ms)", "1.5s")]
    [InlineData("calc(1khz + 1hz)", "1001hz")]
    [InlineData("calc(96dpi)", "1dppx")]
    [InlineData("calc(2x)", "2dppx")]
    [InlineData("calc(1fr * 2)", "2fr")]
    [InlineData("calc(2em + 10px)", "42px")]
    [InlineData("calc(1rem + 1em)", "26px")]
    [InlineData("calc(50vw)", "500px")]
    [InlineData("calc(2EM)", "32px")]
    public void Units(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, NoPercentage));
    }

    [Theory]
    [InlineData("calc(1em)")]
    [InlineData("calc(1cqw)")] // Known unit, not resolved by the callback
    [InlineData("calc(1foo)")] // Unknown unit
    [InlineData("calc(1px + 1s)")]
    [InlineData("calc(1px + 1)")]
    [InlineData("calc(1deg + 1px)")]
    [InlineData("calc(50% + 10px)")] // Percentages are their own type
    public void RelativeUnitsWithoutResolution_AndTypeMismatch_AreInvalid(string input)
    {
        Assert.Null(Evaluate(input, CssCalcContext.Default));
    }

    [Theory]
    [InlineData("calc(1cqw)")]
    [InlineData("calc(1foo + 1px)")]
    public void UnresolvedUnits_AreInvalid(string input)
    {
        Assert.Null(Evaluate(input, NoPercentage));
    }

    [Theory]
    [InlineData("calc(100% - 40px)", "calc(100% - 40px)")]
    [InlineData("calc(100% - 1em)", "calc(100% - 16px)")]
    [InlineData("calc(50% + 50%)", "100%")]
    [InlineData("calc(50% * 2)", "100%")]
    [InlineData("calc(50%)", "50%")]
    [InlineData("calc(10px + 50%)", "calc(50% + 10px)")]
    [InlineData("calc(1px - 50%)", "calc(-50% + 1px)")]
    [InlineData("calc(-50% + 1px)", "calc(-50% + 1px)")]
    [InlineData("calc(10% + 1em + 20% - 6px)", "calc(30% + 10px)")]
    [InlineData("calc(100% + 0px)", "calc(100% + 0px)")]
    [InlineData("calc(2 * (100% - 10px))", "calc(200% - 20px)")]
    [InlineData("calc((100% - 10px) / 2)", "calc(50% - 5px)")]
    [InlineData("calc(1px - (100% - 2px))", "calc(-100% + 3px)")]
    [InlineData("calc(50% * 1px / 1px)", "50%")]
    [InlineData("calc((100% - 10px) / 1px * 1px)", "calc(1px * (100% - 10px) / 1px)")]
    [InlineData("calc(50% / 1% * 1px)", "50px")]
    [InlineData("calc(100% - 1in)", "calc(100% - 96px)")]
    public void Percentages_Unresolved(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, LengthPercentage));
    }

    [Fact]
    public void Percentages_Unresolved_Properties()
    {
        var value = Evaluate("calc(100% - 1em)", LengthPercentage)!;
        Assert.False(value.IsNumeric);
        Assert.False(value.IsResolved);
        Assert.Equal("px", value.Unit);
        Assert.Equal("px", value.TypeUnit);

        value = Evaluate("calc(50% + 50%)", LengthPercentage)!;
        Assert.True(value.IsNumeric);
        Assert.False(value.IsResolved);
        Assert.Equal(100, value.Value);
        Assert.Equal("%", value.Unit);
        Assert.Equal("px", value.TypeUnit);

        value = Evaluate("calc(2em + 10px)", LengthPercentage)!;
        Assert.True(value.IsNumeric);
        Assert.True(value.IsResolved);
        Assert.Equal(42, value.Value);
        Assert.Equal("px", value.Unit);
    }

    [Theory]
    [InlineData("calc(50% + 50%)", "100%")]
    [InlineData("calc(50% * 2)", "100%")]
    [InlineData("calc(50% / 2)", "25%")]
    [InlineData("min(10%, 20%)", "10%")]
    [InlineData("calc(50% / 1%)", "50")]
    public void Percentages_OwnType(string input, string expected)
    {
        var value = Evaluate(input, NoPercentage);
        Assert.NotNull(value);
        Assert.Equal(expected, value.Serialize());
        Assert.True(value.IsResolved);
    }

    [Theory]
    [InlineData("calc(50% + 10px)")]
    [InlineData("calc(50% * 1px)")]
    [InlineData("calc(1px / 1%)")]
    public void Percentages_OwnType_Invalid(string input)
    {
        Assert.Null(Evaluate(input, NoPercentage));
    }

    [Theory]
    [InlineData("calc(100% - 40px)", "160px")]
    [InlineData("calc(50%)", "100px")]
    [InlineData("calc(100% - 1em)", "184px")]
    [InlineData("min(10%, 50px)", "20px")]
    [InlineData("calc(50% * 2)", "200px")]
    public void Percentages_Resolved(string input, string expected)
    {
        var value = Evaluate(input, ResolvedPercentage);
        Assert.NotNull(value);
        Assert.Equal(expected, value.Serialize());
        Assert.True(value.IsResolved);
    }

    [Fact]
    public void Percentages_ResolvedToNumber()
    {
        var context = new CssCalcContext { PercentageUnit = "", PercentageBasis = (2, "") };
        Assert.Equal("1.5", Serialize("calc(50% + 0.5)", context));
    }

    [Theory]
    [InlineData("calc(50% + 0.5)", "calc(0.5 + 50%)")]
    [InlineData("calc(50% * 2)", "100%")]
    public void Percentages_Number(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, NumberPercentage));
    }

    [Theory]
    [InlineData("calc(50% + 10deg)", "calc(50% + 10deg)")]
    [InlineData("calc(50% + 0.5turn)", "calc(50% + 180deg)")]
    public void Percentages_Angle(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, AnglePercentage));
    }

    [Theory]
    [InlineData("min(1px, 2px)", "1px")]
    [InlineData("max(1px, 2px, 3px)", "3px")]
    [InlineData("min(1em, 20px)", "16px")]
    [InlineData("max(1in, 1em)", "96px")]
    [InlineData("calc(min(1px, 2px) + max(3px, 4px))", "5px")]
    [InlineData("min(10%, 20px)", "min(10%, 20px)")]
    [InlineData("max(10%, 1em)", "max(10%, 16px)")]
    [InlineData("min(10%, 20px, 1em, 5%)", "min(10%, 16px, 5%)")]
    [InlineData("min(10%, calc(20% + 1px))", "min(10%, 20% + 1px)")]
    [InlineData("calc(min(10%, 20px) + 5px)", "calc(5px + min(10%, 20px))")]
    [InlineData("calc(2 * min(10%, 20px))", "calc(2 * min(10%, 20px))")]
    [InlineData("clamp(10px, 5px, 20px)", "10px")]
    [InlineData("clamp(10px, 15px, 20px)", "15px")]
    [InlineData("clamp(10px, 25px, 20px)", "20px")]
    [InlineData("clamp(30px, 25px, 20px)", "30px")] // MIN wins
    [InlineData("clamp(none, 25px, 20px)", "20px")]
    [InlineData("clamp(30px, 25px, none)", "30px")]
    [InlineData("clamp(none, 25px, none)", "25px")]
    [InlineData("clamp(10px, 50%, 1em)", "clamp(10px, 50%, 16px)")]
    [InlineData("clamp(none, 50%, 1em)", "min(50%, 16px)")]
    [InlineData("MIN(1PX, 2PX)", "1px")]
    public void MinMaxClamp(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, LengthPercentage));
    }

    [Theory]
    [InlineData("min(1px, 1s)")]
    [InlineData("min(1px, 1)")]
    [InlineData("min()")]
    [InlineData("min(1px,)")]
    [InlineData("min(,1px)")]
    [InlineData("clamp(1px, 2px)")]
    [InlineData("clamp(1px, 2px, 3px, 4px)")]
    [InlineData("clamp(1px, none, 3px)")]
    [InlineData("max(1px 2px)")]
    public void MinMaxClamp_Invalid(string input)
    {
        Assert.Null(Evaluate(input, LengthPercentage));
    }

    [Theory]
    [InlineData("round(2.5)", "3")]
    [InlineData("round(-2.5)", "-2")]
    [InlineData("round(1.4)", "1")]
    [InlineData("round(23px, 10px)", "20px")]
    [InlineData("round(25px, 10px)", "30px")]
    [InlineData("round(nearest, 25px, 10px)", "30px")]
    [InlineData("round(up, 21px, 10px)", "30px")]
    [InlineData("round(down, 29px, 10px)", "20px")]
    [InlineData("round(to-zero, 29px, 10px)", "20px")]
    [InlineData("round(to-zero, -29px, 10px)", "-20px")]
    [InlineData("round(up, -29px, 10px)", "-20px")]
    [InlineData("round(down, -21px, 10px)", "-30px")]
    [InlineData("round(23px, -10px)", "20px")]
    [InlineData("round(UP, 1.1)", "2")]
    [InlineData("round(pi, 1)", "3")]
    [InlineData("round(1em, 5px)", "15px")]
    [InlineData("round(5px, 0px)", "calc(NaN * 1px)")]
    [InlineData("round(infinity * 1px, 5px)", "calc(infinity * 1px)")]
    [InlineData("round(5px, infinity * 1px)", "0px")]
    [InlineData("round(up, 5px, infinity * 1px)", "calc(infinity * 1px)")]
    [InlineData("round(up, 10%, 1px)", "round(up, 10%, 1px)")]
    [InlineData("round(10%, 1px)", "round(10%, 1px)")]
    [InlineData("mod(18px, 5px)", "3px")]
    [InlineData("mod(-18px, 5px)", "2px")]
    [InlineData("mod(18px, -5px)", "-2px")]
    [InlineData("rem(18px, 5px)", "3px")]
    [InlineData("rem(-18px, 5px)", "-3px")]
    [InlineData("rem(18px, -5px)", "3px")]
    [InlineData("mod(10, 3)", "1")]
    [InlineData("mod(140deg, 90deg)", "50deg")]
    [InlineData("mod(1px, 0px)", "calc(NaN * 1px)")]
    [InlineData("mod(infinity * 1px, 1px)", "calc(NaN * 1px)")]
    [InlineData("rem(5px, infinity * 1px)", "5px")]
    [InlineData("mod(-5px, infinity * 1px)", "calc(NaN * 1px)")]
    [InlineData("mod(10%, 3px)", "mod(10%, 3px)")]
    public void RoundModRem(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, LengthPercentage));
    }

    [Theory]
    [InlineData("round(1px)")] // B can be omitted only for numbers
    [InlineData("round(foo, 1px, 1px)")]
    [InlineData("round(up)")]
    [InlineData("round(1px, 1s)")]
    [InlineData("round(up, 1, 2, 3)")]
    [InlineData("mod(1px)")]
    [InlineData("mod(1px, 1)")]
    [InlineData("rem(1px, 2px, 3px)")]
    public void RoundModRem_Invalid(string input)
    {
        Assert.Null(Evaluate(input, LengthPercentage));
    }

    [Theory]
    [InlineData("sin(90deg)", "1")]
    [InlineData("sin(30deg)", "0.5")]
    [InlineData("cos(0)", "1")]
    [InlineData("cos(180deg)", "-1")]
    [InlineData("cos(0.5turn)", "-1")]
    [InlineData("tan(45deg)", "1")]
    [InlineData("tan(90deg)", "calc(infinity)")]
    [InlineData("tan(-90deg)", "calc(-infinity)")]
    [InlineData("sin(pi / 2)", "1")]
    [InlineData("calc(sin(30deg) * 10px)", "5px")]
    [InlineData("asin(1)", "90deg")]
    [InlineData("acos(0)", "90deg")]
    [InlineData("acos(-1)", "180deg")]
    [InlineData("atan(1)", "45deg")]
    [InlineData("atan(infinity)", "90deg")]
    [InlineData("atan2(1, 1)", "45deg")]
    [InlineData("atan2(1px, -1px)", "135deg")]
    [InlineData("atan2(0, -1)", "180deg")]
    [InlineData("asin(2)", "calc(NaN * 1deg)")]
    [InlineData("calc(1turn - asin(1))", "270deg")]
    public void Trigonometric(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, NoPercentage));
    }

    [Theory]
    [InlineData("sin(1px)")]
    [InlineData("asin(1deg)")]
    [InlineData("atan2(1px, 1deg)")]
    [InlineData("sin(1, 2)")]
    public void Trigonometric_Invalid(string input)
    {
        Assert.Null(Evaluate(input, NoPercentage));
    }

    [Fact]
    public void Trigonometric_UnresolvedPercentage()
    {
        Assert.Equal("sin(50%)", Serialize("sin(50%)", AnglePercentage));
        Assert.Equal("", Evaluate("sin(50%)", AnglePercentage)!.TypeUnit);
    }

    [Theory]
    [InlineData("pow(2, 10)", "1024")]
    [InlineData("pow(4, 0.5)", "2")]
    [InlineData("calc(pow(2, 3) * 1px)", "8px")]
    [InlineData("sqrt(16)", "4")]
    [InlineData("sqrt(-1)", "calc(NaN)")]
    [InlineData("hypot(3, 4)", "5")]
    [InlineData("hypot(3px, 4px)", "5px")]
    [InlineData("hypot(-5px)", "5px")]
    [InlineData("hypot(1em, 12px)", "20px")]
    [InlineData("log(e)", "1")]
    [InlineData("log(8, 2)", "3")]
    [InlineData("log(0)", "calc(-infinity)")]
    [InlineData("exp(0)", "1")]
    [InlineData("exp(1)", "2.718282")]
    [InlineData("abs(-5px)", "5px")]
    [InlineData("abs(-1em)", "16px")]
    [InlineData("sign(-5px)", "-1")]
    [InlineData("sign(5deg)", "1")]
    [InlineData("sign(0px)", "0")]
    [InlineData("calc(sign(-3px) * 10px)", "-10px")]
    public void ExponentialAndSign(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, NoPercentage));
    }

    [Theory]
    [InlineData("pow(2px, 2)")]
    [InlineData("sqrt(4px)")]
    [InlineData("hypot(1px, 1s)")]
    [InlineData("log(1px)")]
    [InlineData("exp(1deg)")]
    [InlineData("pow(2)")]
    [InlineData("abs(1px, 2px)")]
    public void ExponentialAndSign_Invalid(string input)
    {
        Assert.Null(Evaluate(input, NoPercentage));
    }

    [Theory]
    [InlineData("abs(-10%)", "abs(-10%)")]
    [InlineData("sign(10%)", "sign(10%)")]
    [InlineData("hypot(10%, 1px)", "hypot(10%, 1px)")]
    public void ExponentialAndSign_UnresolvedPercentage(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, LengthPercentage));
    }

    [Theory]
    [InlineData("calc(pi)", "3.141593")]
    [InlineData("calc(e)", "2.718282")]
    [InlineData("calc(PI * 2)", "6.283185")]
    [InlineData("calc(infinity)", "calc(infinity)")]
    [InlineData("calc(-infinity)", "calc(-infinity)")]
    [InlineData("calc(InFiNiTy)", "calc(infinity)")]
    [InlineData("calc(NaN)", "calc(NaN)")]
    [InlineData("calc(nan)", "calc(NaN)")]
    [InlineData("calc(infinity * 1px)", "calc(infinity * 1px)")]
    [InlineData("calc(-infinity * 1px)", "calc(-infinity * 1px)")]
    [InlineData("calc(NaN * 1px)", "calc(NaN * 1px)")]
    [InlineData("calc(1px / 0)", "calc(infinity * 1px)")]
    [InlineData("calc(-1px / 0)", "calc(-infinity * 1px)")]
    [InlineData("calc(0px / 0)", "calc(NaN * 1px)")]
    [InlineData("calc(infinity * 1px - infinity * 1px)", "calc(NaN * 1px)")]
    [InlineData("calc(1 / infinity)", "0")]
    [InlineData("calc(1px / 0px)", "calc(infinity)")]
    public void Constants(string input, string expected)
    {
        Assert.Equal(expected, Serialize(input, NoPercentage));
    }

    [Fact]
    public void Constants_Unresolved()
    {
        Assert.Equal("calc(100% + (infinity * 1px))", Serialize("calc(100% + infinity * 1px)", LengthPercentage));
    }

    [Theory]
    [InlineData("calc()")]
    [InlineData("calc( )")]
    [InlineData("calc(1px+2px)")]
    [InlineData("calc(1px +2px)")]
    [InlineData("calc(1px -2px)")]
    [InlineData("calc(1px- 2px)")]
    [InlineData("calc(1px +(2px))")]
    [InlineData("calc((1px)+ 2px)")]
    [InlineData("calc(1px + )")]
    [InlineData("calc(+ 1px)")]
    [InlineData("calc(1px *)")]
    [InlineData("calc(* 2)")]
    [InlineData("calc(1px 2px)")]
    [InlineData("calc(1px, 2px)")]
    [InlineData("calc(1px * 2px)")]
    [InlineData("calc(1 / 1px)")]
    [InlineData("calc(1px / 1px * 1px * 1px)")]
    [InlineData("calc(foo)")]
    [InlineData("calc(-pi)")]
    [InlineData("calc(\"1px\")")]
    [InlineData("calc([1px])")]
    [InlineData("calc(var(--x))")]
    [InlineData("calc(attr(x) + 1px)")]
    [InlineData("calc(1px + 1s)")]
    [InlineData("calc(1px / 1s)")]
    [InlineData("foo(1px)")]
    public void Invalid(string input)
    {
        Assert.Null(Evaluate(input, NoPercentage));
    }

    [Fact]
    public void NestedDepthLimit()
    {
        var input = string.Concat(Enumerable.Repeat("calc(", 500)) + "1px" + new string(')', 500);
        Assert.Null(Evaluate(input, NoPercentage));

        input = string.Concat(Enumerable.Repeat("calc(", 20)) + "1px" + new string(')', 20);
        Assert.Equal("1px", Serialize(input, NoPercentage));
    }

    [Theory]
    [InlineData("calc(1px + 1px)", "px")]
    [InlineData("calc(1deg)", "deg")]
    [InlineData("calc(1)", "")]
    [InlineData("calc(10px / 2px)", "")]
    [InlineData("calc(50%)", "%")]
    [InlineData("calc(1s)", "s")]
    public void TypeUnit(string input, string expected)
    {
        Assert.Equal(expected, Evaluate(input, NoPercentage)!.TypeUnit);
    }

    [Theory]
    [InlineData("calc", true)]
    [InlineData("CALC", true)]
    [InlineData("-webkit-calc", true)]
    [InlineData("min", true)]
    [InlineData("clamp", true)]
    [InlineData("atan2", true)]
    [InlineData("hypot", true)]
    [InlineData("sign", true)]
    [InlineData("var", false)]
    [InlineData("rgb", false)]
    [InlineData("calc-size", true)]
    [InlineData("random", true)]
    [InlineData("progress", true)]
    public void IsMathFunction(string name, bool expected)
    {
        Assert.Equal(expected, CssCalc.IsMathFunction(name));
    }

    [Theory]
    [InlineData("calc(-5px)", 0, double.MaxValue, "0px")]
    [InlineData("calc(5px)", 0, double.MaxValue, "5px")]
    [InlineData("calc(NaN * 1px)", 0, double.MaxValue, "0px")]
    [InlineData("calc(infinity * 1px)", 0, 1000, "1000px")]
    [InlineData("calc(-infinity * 1px)", 0, 1000, "0px")]
    [InlineData("calc(100% - 20px)", 0, double.MaxValue, "calc(100% - 20px)")]
    public void Clamp(string input, double min, double max, string expected)
    {
        Assert.Equal(expected, Evaluate(input, LengthPercentage)!.Clamp(min, max).Serialize());
    }
}
