using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The CSS gamut mapping of the painted colors (https://drafts.csswg.org/css-color-4/#css-gamut-mapping) and the rec2020 transfer function.</summary>
public sealed class GamutMappingTests
{
    private static (int R, int G, int B) Paint(string color, RenderingColorSpace gamut = RenderingColorSpace.Srgb)
    {
        var parsed = CssValueComputer.ParseCssColorValue(color);
        Assert.NotNull(parsed);
        var display = DisplayColor.FromCss(parsed, gamut);
        Assert.NotNull(display);
        return (ToByte(display.Value.R), ToByte(display.Value.G), ToByte(display.Value.B));

        static int ToByte(float value) => (int)Math.Round(value * 255, MidpointRounding.AwayFromZero);
    }

    private static void AssertColor((int R, int G, int B) expected, (int R, int G, int B) actual)
    {
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
    }

    // The expected values are the ones of the Ray Trace algorithm of color.js (css/css-color/canvas-srgb-gamut-mapping.html)
    [Theory]
    [InlineData("oklch(90% 0.35 0)", 255, 207, 220)]
    [InlineData("oklch(80% 0.35 30)", 255, 161, 145)]
    [InlineData("oklch(90% 0.3 240)", 190, 228, 255)]
    [InlineData("oklch(40% 0.35 210)", 0, 81, 92)]
    [InlineData("oklch(60% 0.35 60)", 188, 104, 0)]
    public void OutOfGamutColorsAreGamutMapped(string color, int r, int g, int b) => AssertColor((r, g, b), Paint(color));

    [Theory]
    [InlineData("lch(100% 110 60)", 255, 255, 255)]
    [InlineData("oklch(100% 0.3 60)", 255, 255, 255)]
    [InlineData("oklch(99.9999% 0.2 45)", 255, 255, 255)]
    [InlineData("lch(0% 110 60)", 0, 0, 0)]
    [InlineData("oklch(0% 0.3 60)", 0, 0, 0)]
    public void LightnessLimitsAreWhiteAndBlack(string color, int r, int g, int b) => Assert.Equal((r, g, b), Paint(color));

    [Theory]
    [InlineData("rgb(200 100 50)", 200, 100, 50)]
    [InlineData("color(srgb 0.2 0.4 0.6)", 51, 102, 153)]
    [InlineData("oklch(60% 0.1 120)", 121, 137, 64)]
    public void ColorsInsideOfTheGamutAreNotChanged(string color, int r, int g, int b) => AssertColor((r, g, b), Paint(color));

    [Fact]
    public void LegacySrgbColorsAreClamped() => Assert.Equal((0, 255, 0), Paint("hsl(120, 200%, 50%)"));

    [Fact]
    public void DisplayP3ColorsAreMappedToTheGamutOfTheSurface()
    {
        // Outside of sRGB: mapped at constant OkLCh lightness on an sRGB surface, unchanged on a Display P3 surface (as extended sRGB)
        var srgb = Paint("color(display-p3 1 0 0)");
        Assert.Equal(255, srgb.R);
        Assert.InRange(srgb.G, 40, 60);

        var parsed = CssValueComputer.ParseCssColorValue("color(display-p3 1 0 0)")!;
        var p3 = DisplayColor.FromCss(parsed, RenderingColorSpace.DisplayP3)!.Value;
        Assert.True(p3.R > 1);
        Assert.True(p3.G < 0);
    }

    [Fact]
    public void Rec2020UsesThePiecewiseTransferFunction() => AssertColor((0, 128, 0), Paint("color(rec2020 0.235202 0.431704 0.085432)"));
}
