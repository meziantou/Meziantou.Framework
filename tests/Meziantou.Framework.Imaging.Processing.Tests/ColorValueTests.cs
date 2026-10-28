using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The color value types (<see cref="CieXyz"/>, <see cref="CieLab"/>, <see cref="Oklab"/>, <see cref="Hsl"/>, ...):
/// their CSS notations, white points, color spaces and interpolation. The sRGB colors expected from CSS notations were
/// computed with the sample code of CSS Color 4 (see <c>ColorValues/generate-css-color-4.mjs</c>); the conversions
/// themselves are checked against the references by <see cref="ColorValueReferenceTests"/>.
/// </summary>
public sealed class ColorValueTests
{
    [Fact]
    public void TheWhitePointsAreThoseOfCssColor4()
    {
        // The four-figure chromaticities of CIE: D50 (0.3457, 0.3585) and D65 (0.3127, 0.3290), with Y = 1
        Assert.Equal(new CieXyz(0.3457 / 0.3585, 1, 0.2958 / 0.3585), CieXyz.D50);
        Assert.Equal(new CieXyz(0.3127 / 0.3290, 1, 0.3583 / 0.3290), CieXyz.D65);
        Assert.Equal(0.9642956764295677, CieXyz.D50.X, 1e-15);
        Assert.Equal(0.9504559270516716, CieXyz.D65.X, 1e-15);

        // White is the white of each space
        var white = new Rgba32(255, 255, 255);
        AssertXyz(CieXyz.D50, CieXyz.FromRgb(white), 1e-15);
        AssertLab(new CieLab(100, 0, 0), CieLab.FromRgb(white), 1e-12);
        var luv = CieLuv.FromRgb(white);
        Assert.Equal(100, luv.L, 1e-12);
        Assert.Equal(0, luv.U, 1e-12);
        Assert.Equal(0, luv.V, 1e-12);
        var oklab = Oklab.FromRgb(white);
        Assert.Equal(1, oklab.L, 1e-12);
        Assert.Equal(0, oklab.A, 1e-12);
        Assert.Equal(0, oklab.B, 1e-12);

        // The Bradford adaptation maps one white to the other and back
        AssertXyz(CieXyz.D50, CieXyz.D65.Adapt(CieXyz.D65, CieXyz.D50), 1e-15);
        AssertXyz(CieXyz.D65, CieXyz.D65.Adapt(CieXyz.D65, CieXyz.D50).Adapt(CieXyz.D50, CieXyz.D65), 1e-15);
        var red = CieXyz.FromRgb(new Rgba32(255, 0, 0));
        AssertXyz(red, red.Adapt(CieXyz.D50, CieXyz.D50), 1e-15);

        // CIELAB and CIELUV relative to another white
        AssertLab(new CieLab(100, 0, 0), CieLab.FromXyz(CieXyz.D65, CieXyz.D65), 1e-12);
        AssertXyz(CieXyz.D65, new CieLab(100, 0, 0).ToXyz(CieXyz.D65), 1e-15);
        AssertXyz(CieXyz.D65, new CieLuv(100, 0, 0).ToXyz(CieXyz.D65), 1e-15);
        Assert.Equal(new CieLuv(100, 0, 0), CieLuv.FromXyz(CieXyz.D65, CieXyz.D65));
    }

    [Fact]
    public void BlackIsZeroInEverySpace()
    {
        var black = new Rgba32(0, 0, 0);
        Assert.Equal(default, CieXyz.FromRgb(black));
        Assert.Equal(default, CieLab.FromRgb(black));
        Assert.Equal(default, CieLch.FromRgb(black));
        Assert.Equal(default, CieLuv.FromRgb(black));
        Assert.Equal(default, Oklab.FromRgb(black));
        Assert.Equal(default, Oklch.FromRgb(black));
        Assert.Equal(new Hsl(0, 0, 0), Hsl.FromRgb(black));
        Assert.Equal(new Hsv(0, 0, 0), Hsv.FromRgb(black));
        Assert.Equal(new Hwb(0, 0, 1), Hwb.FromRgb(black));
        Assert.Equal(new Cmyk(0, 0, 0, 1), Cmyk.FromRgb(black));
        Assert.Equal(new CieXyz(0, 0, 0), new CieLuv(0, 12, 34).ToXyz());
    }

    [Fact]
    public void TheDeviceModelsDescribeTheRgbValues()
    {
        Assert.Equal(new Hsl(120, 1, 0.5), Hsl.FromRgb(new Rgba64(0, 65535, 0, 0)));
        Assert.Equal(new Rgba32(0, 128, 0), new Hsl(120, 1, 0.25).ToRgba32());
        Assert.Equal(new Rgba32(102, 153, 204), new Hsl(210, 0.5, 0.6).ToRgba32());
        Assert.Equal(new Rgba32(51, 179, 51), new Hwb(120, 0.2, 0.3).ToRgba32());
        Assert.Equal(new Rgba32(255, 0, 255), new Hsv(300, 1, 1).ToRgba32());

        // Hues outside [0, 360) are angles: one turn more or less is the same color
        Assert.Equal(new Hsl(210, 0.5, 0.6).ToRgba64(), new Hsl(-150, 0.5, 0.6).ToRgba64());
        Assert.Equal(new Hsl(210, 0.5, 0.6).ToRgba64(), new Hsl(930, 0.5, 0.6).ToRgba64());
        Assert.Equal(new Hsv(30, 0.5, 0.6).ToRgba64(), new Hsv(-330, 0.5, 0.6).ToRgba64());

        // Whiteness and blackness that add up to more than 1 give the gray of their ratio; alpha is not a color coordinate
        Assert.Equal(new Rgba32(85, 85, 85), new Hwb(45, 0.5, 1).ToRgba32());
        Assert.Equal(new Hsl(0, 1, 0.5), Hsl.FromRgb(new Rgba32(255, 0, 0, 7)));

        // The naive CMYK of CSS: black is what the inks share
        Assert.Equal(new Cmyk(0, 1, 1, 0), Cmyk.FromRgb(new Rgba32(255, 0, 0)));
        Assert.Equal(new Rgba32(128, 128, 128), new Cmyk(0, 0, 0, 0.5).ToRgba32());
        Assert.Equal(new Rgba32(0, 0, 0), new Cmyk(1, 1, 1, 1).ToRgba32());
    }

    [Fact]
    public void ColorsOutsideTheGamutAreClipped()
    {
        // lab(50% 125 -125) is far outside sRGB: (0.8396, -0.3789, 1.3443) by the sample code of CSS Color 4. Each
        // component is clipped after the transfer function
        var (r, g, b) = new CieLab(50, 125, -125).ToRgb(RgbColorSpace.Srgb);
        Assert.Equal(0.8395727383790209, r, 1e-12);
        Assert.Equal(-0.37885175289468964, g, 1e-12);
        Assert.Equal(1.344307035664144, b, 1e-12);
        Assert.Equal(new Rgba32(214, 0, 255), new CieLab(50, 125, -125).ToRgba32());
        Assert.Equal(new Rgba64(55021, 0, 65535, 65535), new CieLab(50, 125, -125).ToRgba64());
        Assert.Equal(new Rgba32(0, 0, 0), new CieLab(double.NaN, 0, 0).ToRgba32());
    }

    [Theory]
    [InlineData("lab(52.2345 40.1645 59.9971)", 52.2345, 40.1645, 59.9971, 1)]
    [InlineData("lab(52.2345% 40.1645 59.9971)", 52.2345, 40.1645, 59.9971, 1)]
    [InlineData("LAB( 50%   50% -50% / 0.25 )", 50, 62.5, -62.5, 0.25)]
    [InlineData("lab(none 1e1 +2.5E-1 / 50%)", 0, 10, 0.25, 0.5)]
    [InlineData("lab(120 0 0 / 2)", 100, 0, 0, 1)]
    [InlineData("lab(-5 0 0 / -1)", 0, 0, 0, 0)]
    [InlineData("  lab(.5 -.5 0/none)\t", 0.5, -0.5, 0, 0)]
    public void ParsesLab(string text, double l, double a, double b, double alpha)
    {
        var lab = CieLab.Parse(text, out var parsedAlpha);
        Assert.Equal(new CieLab(l, a, b), lab);
        Assert.Equal(alpha, parsedAlpha);
        Assert.True(CieLab.TryParse(text, out var other, out _));
        Assert.Equal(lab, other);
    }

    [Theory]
    [InlineData("lch(52.2345 72.2 56.2)", 52.2345, 72.2, 56.2, 1)]
    [InlineData("lch(50% 100% 0.25turn)", 50, 150, 90, 1)]
    [InlineData("lch(50 -10 -90deg)", 50, 0, 270, 1)]
    [InlineData("lch(50 10 200grad / 0.5)", 50, 10, 180, 0.5)]
    [InlineData("lch(50 10 400)", 50, 10, 40, 1)]
    [InlineData("lch(50 10 none)", 50, 10, 0, 1)]
    public void ParsesLch(string text, double l, double c, double h, double alpha)
    {
        var lch = CieLch.Parse(text, out var parsedAlpha);
        Assert.Equal(l, lch.L);
        Assert.Equal(c, lch.C);
        Assert.Equal(h, lch.H, 1e-12);
        Assert.Equal(alpha, parsedAlpha);
    }

    [Theory]
    [InlineData("oklab(0.62796 0.22486 0.12585)", 0.62796, 0.22486, 0.12585)]
    [InlineData("oklab(62.796% 50% -25%)", 0.62796, 0.2, -0.1)]
    [InlineData("oklab(1.5 0 0)", 1, 0, 0)]
    public void ParsesOklab(string text, double l, double a, double b)
    {
        var oklab = Oklab.Parse(text, out var alpha);
        Assert.Equal(l, oklab.L, 1e-15);
        Assert.Equal(a, oklab.A, 1e-15);
        Assert.Equal(b, oklab.B, 1e-15);
        Assert.Equal(1, alpha);
    }

    [Theory]
    [InlineData("oklch(0.62796 0.25768 29.23)", 0.62796, 0.25768, 29.23)]
    [InlineData("oklch(70% 25% 3.14159265358979rad)", 0.7, 0.1, 180)]
    [InlineData("oklch(0.5 -0.1 -10)", 0.5, 0, 350)]
    public void ParsesOklch(string text, double l, double c, double h)
    {
        var oklch = Oklch.Parse(text, out _);
        Assert.Equal(l, oklch.L, 1e-15);
        Assert.Equal(c, oklch.C, 1e-15);
        Assert.Equal(h, oklch.H, 1e-9);
    }

    [Theory]
    [InlineData("hsl(120 100% 25%)", 120, 1, 0.25, 1)]
    [InlineData("hsl(120 100 25)", 120, 1, 0.25, 1)]
    [InlineData("hsl(120deg, 100%, 25%)", 120, 1, 0.25, 1)]
    [InlineData("hsla(120, 100%, 25%, 0.5)", 120, 1, 0.25, 0.5)]
    [InlineData("hsl(120, 100%, 25%, 50%)", 120, 1, 0.25, 0.5)]
    [InlineData("hsl(-120 -10% 25% / 1)", 240, 0, 0.25, 1)]
    [InlineData("hsl(none none none)", 0, 0, 0, 1)]
    public void ParsesHsl(string text, double h, double s, double l, double alpha)
    {
        Assert.Equal(new Hsl(h, s, l), Hsl.Parse(text, out var parsedAlpha));
        Assert.Equal(alpha, parsedAlpha);
    }

    [Theory]
    [InlineData("hwb(120 20% 30%)", 120, 0.2, 0.3)]
    [InlineData("hwb(120 20 30 / 0.1)", 120, 0.2, 0.3)]
    [InlineData("hwb(1turn 120% 0%)", 0, 1.2, 0)]
    public void ParsesHwb(string text, double h, double w, double b)
    {
        var hwb = Hwb.Parse(text, out _);
        Assert.Equal(h, hwb.H);
        Assert.Equal(w, hwb.W, 1e-15);
        Assert.Equal(b, hwb.B, 1e-15);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lab")]
    [InlineData("lab()")]
    [InlineData("lab(50 0)")]
    [InlineData("lab(50 0 0 0)")]
    [InlineData("lab(50 0 0 /)")]
    [InlineData("lab(50 0 0 / 1 / 1)")]
    [InlineData("lab(50, 0, 0)")]
    [InlineData("lab (50 0 0)")]
    [InlineData("lab(50 0 0")]
    [InlineData("lab(50 0 0deg)")]
    [InlineData("lab(50 0 calc(1))")]
    [InlineData("lab(50 0 1.)")]
    [InlineData("lab(50 0 1e)")]
    [InlineData("lab(50 0 1e999)")]
    [InlineData("lab(50 0 nan)")]
    [InlineData("lab(50 0 0)x")]
    [InlineData("lch(50 10 10%)")]
    [InlineData("lch(50 10 10px)")]
    public void RejectsInvalidModernSyntax(string text)
    {
        Assert.False(CieLab.TryParse(text, out var result, out var alpha));
        Assert.Equal(default, result);
        Assert.Equal(0, alpha);
        Assert.Throws<FormatException>(() => CieLab.Parse(text, out _));
        Assert.False(CieLch.TryParse(text, out _, out _));
        Assert.False(Oklch.TryParse(text, out _, out _));
        Assert.False(Rgba32.TryParseCss(text, out _));
    }

    [Fact]
    public void EachTypeReadsItsOwnFunction()
    {
        Assert.False(CieLab.TryParse("oklab(0.5 0 0)", out _, out _));
        Assert.False(Oklab.TryParse("lab(50 0 0)", out _, out _));
        Assert.False(CieLch.TryParse("oklch(0.5 0 0)", out _, out _));
        Assert.False(Oklch.TryParse("lch(50 0 0)", out _, out _));
        Assert.False(Hsl.TryParse("hwb(0 0% 0%)", out _, out _));
        Assert.False(Hwb.TryParse("hsl(0 0% 0%)", out _, out _));
        Assert.True(Hsl.TryParse("HSLA(0 0% 0%)", out _, out _));
    }

    [Theory]
    [InlineData("hsl(120, 100, 25)")]
    [InlineData("hsl(120, 100%, 25)")]
    [InlineData("hsl(120 100%, 25%)")]
    [InlineData("hsl(120, 100%, 25% / 0.5)")]
    [InlineData("hsl(none, 100%, 25%)")]
    [InlineData("hsl(120, 100%, 25%, none)")]
    [InlineData("hsl(120, 100%, 25%,)")]
    [InlineData("hwb(120, 20%, 30%)")]
    [InlineData("rgb(120, 20%, 30%)")]
    public void RejectsInvalidLegacySyntax(string text)
    {
        Assert.False(Hsl.TryParse(text, out _, out _));
        Assert.False(Hwb.TryParse(text, out _, out _));
        Assert.False(Rgba32.TryParseCss(text, out _));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void FormatsCssNotations()
    {
        Assert.Equal("lab(52.2345 40.1645 59.9971)", new CieLab(52.2345, 40.1645, 59.9971).ToCssString());
        Assert.Equal("lab(0 -1.5 0 / 0.25)", new CieLab(-0.0, -1.5, 0).ToCssString(0.25));
        Assert.Equal("lch(52.2345 72.2 56.2)", new CieLch(52.2345, 72.2, 56.2).ToCssString());
        Assert.Equal("oklab(0.62796 0.22486 0.12585)", new Oklab(0.62796, 0.22486, 0.12585).ToCssString());
        Assert.Equal("oklch(0.62796 0.25768 29.23 / 0)", new Oklch(0.62796, 0.25768, 29.23).ToCssString(0));
        Assert.Equal("hsl(120 100% 25%)", new Hsl(120, 1, 0.25).ToCssString());
        Assert.Equal("hwb(120 20% 30% / 0.5)", new Hwb(120, 0.2, 0.3).ToCssString(0.5));

        Assert.Throws<ArgumentOutOfRangeException>("alpha", () => new CieLab(50, 0, 0).ToCssString(1.5));
        Assert.Throws<ArgumentOutOfRangeException>("alpha", () => new CieLab(50, 0, 0).ToCssString(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CieLab(double.PositiveInfinity, 0, 0).ToCssString());

        // The text reads back the same values, exactly for the spaces whose components are written as they are
        var random = new Random(3638);
        for (var i = 0; i < 1000; i++)
        {
            // A lightness of 100 or 1 may come out one bit larger, which CSS clamps: the colors are darker than white
            var lab = CieLab.FromRgb(new Rgba32((byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255)));
            Assert.Equal(lab, CieLab.Parse(lab.ToCssString(), out _));
            var lch = lab.ToLch();
            Assert.Equal(lch, CieLch.Parse(lch.ToCssString(), out _));
            var oklab = Oklab.FromXyz(lab.ToXyz());
            Assert.Equal(oklab, Oklab.Parse(oklab.ToCssString(0.5), out var alpha));
            Assert.Equal(0.5, alpha);
            var oklch = oklab.ToOklch();
            Assert.Equal(oklch, Oklch.Parse(oklch.ToCssString(), out _));
            var hsl = Hsl.FromRgb(lab.ToRgba32());
            var parsed = Hsl.Parse(hsl.ToCssString(), out _);
            Assert.Equal(hsl.H, parsed.H);
            Assert.Equal(hsl.S, parsed.S, 1e-15);
            Assert.Equal(hsl.L, parsed.L, 1e-15);
        }
    }

    [Theory]
    [InlineData("#C80", 204, 136, 0, 255)]
    [InlineData("#11223344", 17, 34, 51, 68)]
    [InlineData("rgb(255 128 0)", 255, 128, 0, 255)]
    [InlineData("rgb(100% 50% 0% / 50%)", 255, 128, 0, 128)]
    [InlineData("rgb(127.5 300 -5)", 128, 255, 0, 255)]
    [InlineData("rgba(255, 128, 0, 0.5)", 255, 128, 0, 128)]
    [InlineData("rgb(100%, 50%, 0%)", 255, 128, 0, 255)]
    [InlineData("rgb(none 128 0)", 0, 128, 0, 255)]
    [InlineData("hsl(120 100% 25%)", 0, 128, 0, 255)]
    [InlineData("hsla(210, 50%, 60%, 0.2)", 102, 153, 204, 51)]
    [InlineData("hwb(120 20% 30%)", 51, 179, 51, 255)]
    [InlineData("lab(29.2345% 39.3825 20.0664)", 125, 35, 41, 255)]
    [InlineData("lab(52.2345 40.1645 59.9971)", 198, 93, 6, 255)]
    [InlineData("lab(50% 50% -50%)", 176, 67, 228, 255)]
    [InlineData("lch(52.2345 72.2 56.2)", 198, 93, 6, 255)]
    [InlineData("oklab(0.62796 0.22486 0.12585)", 255, 0, 0, 255)]
    [InlineData("oklch(0.62796 0.25768 29.23)", 255, 0, 0, 255)]
    [InlineData("oklch(70% 0.1 200 / 0)", 64, 177, 183, 0)]
    public void ParsesCssColorsAsSrgb(string text, byte r, byte g, byte b, byte a)
    {
        Assert.Equal(new Rgba32(r, g, b, a), Rgba32.ParseCss(text));
    }

    [Theory]
    [InlineData("C80")]
    [InlineData("red")]
    [InlineData("color(srgb 1 0 0)")]
    [InlineData("rgb(255, 50%, 0)")]
    [InlineData("rgb(255 128)")]
    public void RejectsUnsupportedCssColors(string text)
    {
        Assert.False(Rgba32.TryParseCss(text, out var result));
        Assert.Equal(default, result);
        Assert.Throws<FormatException>(() => Rgba32.ParseCss(text));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ColorSpacesComeFromCicpDescriptionsAndIccProfiles()
    {
        // The sRGB descriptions are sRGB itself, unspecified code points included
        Assert.Same(RgbColorSpace.Srgb, RgbColorSpace.FromCicp(new CicpColorDescription(1, 13, 0, fullRange: true)));
        Assert.Same(RgbColorSpace.Srgb, RgbColorSpace.FromCicp(new CicpColorDescription(2, 2, 2, fullRange: false)));
        Assert.Equal("sRGB", RgbColorSpace.Srgb.ToString());
        Assert.True(RgbColorSpace.Srgb.CanConvertFromXyz);

        // Display P3 makes the red of sRGB less saturated, and converts it back
        var displayP3 = RgbColorSpace.FromCicp(new CicpColorDescription(12, 13, 0, fullRange: true));
        Assert.Equal("CICP 12/13", displayP3.Description);
        var red = CieLab.FromRgb(new Rgba32(255, 0, 0));
        Assert.Equal(new Rgba32(234, 51, 35), red.ToRgba32(displayP3));
        Assert.Equal(new Rgba32(255, 0, 0), CieLab.FromRgb(new Rgba32(234, 51, 35), displayP3).ToRgba32());
        var p3Red = new Rgba64(65535, 0, 0, 65535);
        Assert.Equal(p3Red, Oklch.FromRgb(p3Red, displayP3).ToRgba64(displayP3));
        Assert.Equal(p3Red, CieXyz.FromRgb(p3Red, displayP3).ToRgba64(displayP3));
        Assert.Equal(p3Red, CieLuv.FromRgb(p3Red, displayP3).ToRgba64(displayP3));

        // White is white in every space
        AssertXyz(CieXyz.D50, CieXyz.FromRgb(new Rgba32(255, 255, 255), RgbColorSpace.FromCicp(new CicpColorDescription(9, 1, 0, fullRange: true))), 1e-12);

        Assert.Throws<ArgumentNullException>(() => RgbColorSpace.FromCicp(null!));
        Assert.Throws<UnsupportedImageFeatureException>(() => RgbColorSpace.FromCicp(new CicpColorDescription(9, 16, 9, fullRange: true)));
        Assert.Throws<UnsupportedImageFeatureException>(() => RgbColorSpace.FromCicp(new CicpColorDescription(9, 18, 9, fullRange: true)));
        Assert.Throws<UnsupportedImageFeatureException>(() => RgbColorSpace.FromCicp(new CicpColorDescription(3, 13, 0, fullRange: true)));
        Assert.Throws<UnsupportedImageFeatureException>(() => RgbColorSpace.FromCicp(new CicpColorDescription(1, 3, 0, fullRange: true)));

        // The built-in sRGB profile is sRGB within the rounding of its s15Fixed16 numbers
        var profile = RgbColorSpace.FromIccProfile(IccProfile.Srgb);
        Assert.True(profile.CanConvertFromXyz);
        AssertXyz(CieXyz.D50, CieXyz.FromRgb(new Rgba32(255, 255, 255), profile), 1e-5);
        var random = new Random(3638);
        for (var i = 0; i < 200; i++)
        {
            var color = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            AssertXyz(CieXyz.FromRgb(color), CieXyz.FromRgb(color, profile), 1e-4);
            Assert.Equal(color, CieLab.FromRgb(color, profile).ToRgba32(profile));
            Assert.Equal(color, Oklab.FromRgb(color, profile).ToRgba32());
        }

        // A profile of lookup tables, with a CIELAB connection space
        var lut = RgbColorSpace.FromIccProfile(IccTestProfiles.RgbLabLut8());
        var gray = CieLab.FromRgb(new Rgba32(128, 128, 128), lut);
        Assert.True(gray.L is > 0 and < 100);

        Assert.Throws<ArgumentNullException>(() => RgbColorSpace.FromIccProfile(null!));
        Assert.Throws<UnsupportedImageFeatureException>(() => RgbColorSpace.FromIccProfile(IccProfile.SrgbGray));
        Assert.Throws<UnsupportedImageFeatureException>(() => RgbColorSpace.FromIccProfile(IccProfile.Cgats001CompatibleCmyk));
    }

    [Fact]
    public void OklabMixesComponentByComponent()
    {
        var from = new Oklab(0.2, 0.1, -0.1);
        var to = new Oklab(0.8, -0.1, 0.3);
        Assert.Equal(from, Oklab.Lerp(from, to, 0));
        var end = Oklab.Lerp(from, to, 1);
        Assert.Equal(to.L, end.L, 1e-15);
        Assert.Equal(to.A, end.A, 1e-15);
        Assert.Equal(to.B, end.B, 1e-15);
        var middle = Oklab.Lerp(from, to, 0.5);
        Assert.Equal(0.5, middle.L, 1e-15);
        Assert.Equal(0, middle.A, 1e-15);
        Assert.Equal(0.1, middle.B, 1e-15);
    }

    [Theory]
    [InlineData(HueInterpolation.Shorter, 10, 350, 0)]
    [InlineData(HueInterpolation.Shorter, 350, 10, 0)]
    [InlineData(HueInterpolation.Shorter, 30, 90, 60)]
    [InlineData(HueInterpolation.Longer, 30, 90, 240)]
    [InlineData(HueInterpolation.Longer, 10, 350, 180)]
    [InlineData(HueInterpolation.Longer, 90, 90, 270)]
    [InlineData(HueInterpolation.Increasing, 350, 10, 0)]
    [InlineData(HueInterpolation.Increasing, 90, 30, 240)]
    [InlineData(HueInterpolation.Decreasing, 10, 350, 0)]
    [InlineData(HueInterpolation.Decreasing, 30, 90, 240)]
    [InlineData(HueInterpolation.Decreasing, 390, -270, 240)]
    public void OklchMixesTheHueAlongTheArcOfTheMethod(HueInterpolation method, double fromHue, double toHue, double expected)
    {
        var middle = Oklch.Lerp(new Oklch(0.2, 0.1, fromHue), new Oklch(0.6, 0.3, toHue), 0.5, method);
        Assert.Equal(0.4, middle.L, 1e-15);
        Assert.Equal(0.2, middle.C, 1e-15);
        Assert.Equal(expected, middle.H, 1e-12);
    }

    [Fact]
    public void OklchTakesTheHueOfTheColorWhenTheOtherIsGray()
    {
        var gray = new Oklch(0.5, 0, 0);
        var blue = new Oklch(0.5, 0.2, 264);
        Assert.Equal(264, Oklch.Lerp(gray, blue, 0.25).H);
        Assert.Equal(264, Oklch.Lerp(blue, gray, 0.25).H);
        Assert.Equal(264, Oklch.Lerp(new Oklch(0.5, 0.000004, 30), blue, 0.5).H);
        Assert.Equal(0, Oklch.Lerp(gray, gray, 0.5).H);
        Assert.Equal(264, Oklch.Lerp(new Oklch(0.5, 0.1, double.NaN), blue, 0.5).H);
        Assert.Throws<ArgumentOutOfRangeException>("hueInterpolation", () => Oklch.Lerp(gray, blue, 0.5, (HueInterpolation)4));
    }

    [Fact]
    public void ColorDifferencesHaveTheirProperties()
    {
        var reference = new CieLab(50, 2.6772, -79.7751);
        var sample = new CieLab(50, 0, -82.7485);
        Assert.Equal(0, ColorDifference.DeltaE76(reference, reference));
        Assert.Equal(0, ColorDifference.DeltaE94(reference, reference));
        Assert.Equal(0, ColorDifference.DeltaE2000(reference, reference));
        Assert.Equal(0, ColorDifference.DeltaEOK(new Oklab(0.5, 0.1, 0.1), new Oklab(0.5, 0.1, 0.1)));
        Assert.Equal(5, ColorDifference.DeltaE76(new CieLab(50, 0, 0), new CieLab(50, 3, 4)));
        Assert.Equal(0.5, ColorDifference.DeltaEOK(new Oklab(0.5, 0, 0), new Oklab(0.5, 0.3, 0.4)), 1e-15);

        // CIE 1994 weights the differences by the chroma of the reference: not symmetric
        Assert.NotEqual(ColorDifference.DeltaE94(reference, sample), ColorDifference.DeltaE94(sample, reference));
        Assert.Equal(ColorDifference.DeltaE2000(reference, sample), ColorDifference.DeltaE2000(sample, reference), 1e-12);

        // The parametric factors divide their difference: a lightness difference alone is halved by kL = 2
        var lighter = new CieLab(60, 0, 0);
        var darker = new CieLab(40, 0, 0);
        Assert.Equal(ColorDifference.DeltaE2000(lighter, darker) / 2, ColorDifference.DeltaE2000(lighter, darker, lightnessWeight: 2), 1e-12);
        Assert.Equal(ColorDifference.DeltaE94(lighter, darker) / 2, ColorDifference.DeltaE94(lighter, darker, Cie94Application.Textiles), 1e-12);
        Assert.Throws<ArgumentOutOfRangeException>("application", () => ColorDifference.DeltaE94(lighter, darker, (Cie94Application)2));
    }

    [Fact]
    public void ValuesHaveValueEquality()
    {
        Assert.Equal(new CieLab(1, 2, 3), new CieLab(1, 2, 3));
        Assert.True(new Oklch(1, 2, 3) == new Oklch(1, 2, 3));
        Assert.True(new Hsv(1, 2, 3) != new Hsv(1, 2, 4));
        Assert.Equal(new Cmyk(0.1, 0.2, 0.3, 0.4).GetHashCode(), new Cmyk(0.1, 0.2, 0.3, 0.4).GetHashCode());
        Assert.Equal(new CieXyz(0, 0, 0), new CieXyz(-0.0, 0, 0));
        Assert.Equal(new CieXyz(0, 0, 0).GetHashCode(), new CieXyz(-0.0, 0, 0).GetHashCode());
        Assert.False(new Hwb(1, 2, 3).Equals((object)new Hsl(1, 2, 3)));
        Assert.Equal("CieLab(50, -1.5, 2.25)", new CieLab(50, -1.5, 2.25).ToString());
        Assert.Equal("Cmyk(0.1, 0.2, 0.3, 0.4)", new Cmyk(0.1, 0.2, 0.3, 0.4).ToString());
        Assert.Equal("CieXyz(1, 2, 3)", new CieXyz(1, 2, 3).ToString());
    }

    private static void AssertXyz(CieXyz expected, CieXyz actual, double tolerance)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    private static void AssertLab(CieLab expected, CieLab actual, double tolerance)
    {
        Assert.Equal(expected.L, actual.L, tolerance);
        Assert.Equal(expected.A, actual.A, tolerance);
        Assert.Equal(expected.B, actual.B, tolerance);
    }
}
