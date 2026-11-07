using System.Text.Json;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The conversions and the color differences of the color value types (<see cref="CieXyz"/>, <see cref="CieLab"/>,
/// <see cref="Oklab"/>, ...) against values computed outside the library by two independent references (the scripts of
/// <c>ColorValues/</c>): the sample code of CSS Color Module Level 4, run by Node, and colour-science, a Python
/// implementation of the CIE equations. The CIEDE2000 difference is also checked against the test data of Sharma, Wu and
/// Dalal (2005).
/// </summary>
/// <remarks>
/// The two references agree to 1e-13 wherever they define the same conversion, except Oklab: colour-science uses the
/// ten-digit matrices Björn Ottosson first published, CSS Color 4 the matrices it recomputed for its D65 white, which the
/// library uses. They differ by up to 1.3e-4.
/// </remarks>
public sealed class ColorValueReferenceTests
{
    // The acceptance criterion is 1e-6; the library follows the reference code to its last bits but for the functions
    // of the platform
    private const double Tolerance = 1e-9;

    // colour-science computes Oklab with the original matrices of Ottosson (see the remarks)
    private const double OttossonOklabTolerance = 2e-4;

    private static readonly Lazy<JsonDocument> CssData = new(() => Load("css-color-4.json"));
    private static readonly Lazy<JsonDocument> ColourData = new(() => Load("colour-science.json"));

    [Fact]
    public void Srgb8ToEveryTypeIsThatOfCssColor4()
    {
        foreach (var entry in CssData.Value.RootElement.GetProperty("srgb8").EnumerateArray())
        {
            var rgb = Ints(entry, "rgb");
            var color8 = new Rgba32((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
            AssertRgbConversions(color8, color8, entry, rgb);
        }
    }

    [Fact]
    public void Srgb16ToEveryTypeIsThatOfCssColor4()
    {
        foreach (var entry in CssData.Value.RootElement.GetProperty("srgb16").EnumerateArray())
        {
            var rgb = Ints(entry, "rgb");
            AssertRgbConversions(new Rgba64((ushort)rgb[0], (ushort)rgb[1], (ushort)rgb[2], ushort.MaxValue), color8Value: null, entry, rgb);
        }
    }

    [Fact]
    public void SrgbToEveryTypeIsThatOfColourScience()
    {
        foreach (var set in new[] { "srgb8", "srgb16" })
        {
            foreach (var entry in ColourData.Value.RootElement.GetProperty(set).EnumerateArray())
            {
                var rgb = Ints(entry, "rgb");
                var is8Bit = set == "srgb8";
                var color8 = is8Bit ? new Rgba32((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]) : default;
                var color = is8Bit ? color8.ToRgba64() : new Rgba64((ushort)rgb[0], (ushort)rgb[1], (ushort)rgb[2], ushort.MaxValue);
                var context = string.Join(", ", rgb);
                var xyz = is8Bit ? CieXyz.FromRgb(color8) : CieXyz.FromRgb(color);
                AssertComponents(Doubles(entry, "xyzD50"), [xyz.X, xyz.Y, xyz.Z], Tolerance, context);

                var lab = CieLab.FromXyz(xyz);
                AssertComponents(Doubles(entry, "lab"), [lab.L, lab.A, lab.B], Tolerance, context);
                var lch = CieLch.FromLab(lab);
                AssertPolar(Doubles(entry, "lch"), lch.L, lch.C, lch.H, Tolerance, powerlessChroma: 0, context);
                var luv = CieLuv.FromXyz(xyz);
                AssertComponents(Doubles(entry, "luv"), [luv.L, luv.U, luv.V], Tolerance, context);

                var oklab = is8Bit ? Oklab.FromRgb(color8) : Oklab.FromRgb(color);
                AssertComponents(Doubles(entry, "oklab"), [oklab.L, oklab.A, oklab.B], OttossonOklabTolerance, context);

                var hsv = Hsv.FromRgb(color);
                AssertPolar(Rotate(Doubles(entry, "hsv")), hsv.V, hsv.S, hsv.H, Tolerance, powerlessChroma: 0, context);
                var hsl = Hsl.FromRgb(color);
                AssertPolar(Rotate(Doubles(entry, "hsl")), hsl.L, hsl.S, hsl.H, Tolerance, powerlessChroma: 0, context);
                var cmyk = Cmyk.FromRgb(color);
                AssertComponents(Doubles(entry, "cmyk"), [cmyk.C, cmyk.M, cmyk.Y, cmyk.K], Tolerance, context);
            }
        }

        // colour-science gives the hue first: the assertion takes it last
        static double[] Rotate(double[] values) => [values[2], values[1], values[0]];
    }

    [Fact]
    public void LabToXyzAndSrgbIsThatOfCssColor4()
    {
        foreach (var entry in CssData.Value.RootElement.GetProperty("lab").EnumerateArray())
        {
            var values = Doubles(entry, "lab");
            var lab = new CieLab(values[0], values[1], values[2]);
            var context = string.Join(", ", values);
            var xyz = lab.ToXyz();
            AssertComponents(Doubles(entry, "xyzD50"), [xyz.X, xyz.Y, xyz.Z], Tolerance, context);
            var (r, g, b) = lab.ToRgb(RgbColorSpace.Srgb);
            AssertComponents(Doubles(entry, "srgb"), [r, g, b], Tolerance, context);

            // The round trip
            var back = CieLab.FromXyz(xyz);
            AssertComponents(values, [back.L, back.A, back.B], Tolerance, context);
        }

        foreach (var entry in CssData.Value.RootElement.GetProperty("lch").EnumerateArray())
        {
            var values = Doubles(entry, "lch");
            var lab = new CieLch(values[0], values[1], values[2]).ToLab();
            AssertComponents(Doubles(entry, "lab"), [lab.L, lab.A, lab.B], Tolerance, string.Join(", ", values));
        }
    }

    [Fact]
    public void OklabToXyzAndSrgbIsThatOfCssColor4()
    {
        foreach (var entry in CssData.Value.RootElement.GetProperty("oklab").EnumerateArray())
        {
            var values = Doubles(entry, "oklab");
            var oklab = new Oklab(values[0], values[1], values[2]);
            var context = string.Join(", ", values);

            // The library gives CIEXYZ relative to D50: brought back to D65 with the matrix of CSS
            var xyz = oklab.ToXyz().Adapt(CieXyz.D50, CieXyz.D65);
            AssertComponents(Doubles(entry, "xyzD65"), [xyz.X, xyz.Y, xyz.Z], Tolerance, context);
            var (r, g, b) = oklab.ToRgb(RgbColorSpace.Srgb);
            AssertComponents(Doubles(entry, "srgb"), [r, g, b], Tolerance, context);

            var back = Oklab.FromXyz(oklab.ToXyz());
            AssertComponents(values, [back.L, back.A, back.B], Tolerance, context);
        }

        foreach (var entry in CssData.Value.RootElement.GetProperty("oklch").EnumerateArray())
        {
            var values = Doubles(entry, "oklch");
            var oklab = new Oklch(values[0], values[1], values[2]).ToOklab();
            AssertComponents(Doubles(entry, "oklab"), [oklab.L, oklab.A, oklab.B], Tolerance, string.Join(", ", values));
        }
    }

    [Fact]
    public void DeviceModelsToSrgbAreThoseOfTheReferences()
    {
        foreach (var entry in CssData.Value.RootElement.GetProperty("hsl").EnumerateArray())
        {
            var values = Doubles(entry, "hsl");
            var (r, g, b) = new Hsl(values[0], values[1], values[2]).ToRgb();
            AssertComponents(Doubles(entry, "srgb"), [r, g, b], Tolerance, string.Join(", ", values));
        }

        foreach (var entry in CssData.Value.RootElement.GetProperty("hwb").EnumerateArray())
        {
            var values = Doubles(entry, "hwb");
            var (r, g, b) = new Hwb(values[0], values[1], values[2]).ToRgb();
            AssertComponents(Doubles(entry, "srgb"), [r, g, b], Tolerance, string.Join(", ", values));
        }

        foreach (var entry in CssData.Value.RootElement.GetProperty("cmyk").EnumerateArray())
        {
            var values = Doubles(entry, "cmyk");
            var (r, g, b) = new Cmyk(values[0], values[1], values[2], values[3]).ToRgb();
            AssertComponents(Doubles(entry, "srgb"), [r, g, b], Tolerance, string.Join(", ", values));
        }

        foreach (var entry in ColourData.Value.RootElement.GetProperty("hsv").EnumerateArray())
        {
            var values = Doubles(entry, "hsv");
            var (r, g, b) = new Hsv(values[0], values[1], values[2]).ToRgb();
            AssertComponents(Doubles(entry, "srgb"), [r, g, b], Tolerance, string.Join(", ", values));
        }
    }

    [Fact]
    public void LuvToXyzIsThatOfColourScience()
    {
        foreach (var entry in ColourData.Value.RootElement.GetProperty("luv").EnumerateArray())
        {
            var values = Doubles(entry, "luv");
            var xyz = new CieLuv(values[0], values[1], values[2]).ToXyz();
            AssertComponents(Doubles(entry, "xyzD50"), [xyz.X, xyz.Y, xyz.Z], Tolerance, string.Join(", ", values));
        }
    }

    [Fact]
    public void DisplayP3OfACicpDescriptionIsThatOfTheReferences()
    {
        // Display P3: the primaries of SMPTE EG 432-1 (12) with the sRGB transfer function (13). The library adapts the
        // white of the primaries to D50 and CSS works in D65: the result is brought back to D65 to compare
        var space = RgbColorSpace.FromCicp(new CicpColorDescription(12, 13, 0, fullRange: true));
        foreach (var data in new[] { CssData.Value, ColourData.Value })
        {
            foreach (var entry in data.RootElement.GetProperty("displayP3").EnumerateArray())
            {
                var rgb = Ints(entry, "rgb");
                var xyz = CieXyz.FromRgb(new Rgba32((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]), space).Adapt(CieXyz.D50, CieXyz.D65);
                AssertComponents(Doubles(entry, "xyzD65"), [xyz.X, xyz.Y, xyz.Z], Tolerance, string.Join(", ", rgb));
            }
        }
    }

    [Fact]
    public void DisplayP3OfAnIccProfileIsThatOfTheReferencesWithinTheRoundingOfTheProfile()
    {
        // The colorants and the curve of an ICC profile are s15Fixed16 numbers: about 1.5e-5 each
        var space = RgbColorSpace.FromIccProfile(IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve()));
        foreach (var entry in CssData.Value.RootElement.GetProperty("displayP3").EnumerateArray())
        {
            var rgb = Ints(entry, "rgb");
            var xyz = CieXyz.FromRgb(new Rgba32((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]), space).Adapt(CieXyz.D50, CieXyz.D65);
            AssertComponents(Doubles(entry, "xyzD65"), [xyz.X, xyz.Y, xyz.Z], 1e-4, string.Join(", ", rgb));
        }
    }

    [Fact]
    public void ColorDifferencesAreThoseOfTheReferences()
    {
        foreach (var entry in CssData.Value.RootElement.GetProperty("deltaE").EnumerateArray())
        {
            var lab1 = Lab(entry, "lab1");
            var lab2 = Lab(entry, "lab2");
            Assert.Equal(entry.GetProperty("deltaE2000").GetDouble(), ColorDifference.DeltaE2000(lab1, lab2), Tolerance);
            var ok1 = Doubles(entry, "oklab1");
            var ok2 = Doubles(entry, "oklab2");
            Assert.Equal(entry.GetProperty("deltaEOK").GetDouble(), ColorDifference.DeltaEOK(new Oklab(ok1[0], ok1[1], ok1[2]), new Oklab(ok2[0], ok2[1], ok2[2])), Tolerance);
        }

        foreach (var entry in ColourData.Value.RootElement.GetProperty("deltaE").EnumerateArray())
        {
            var lab1 = Lab(entry, "lab1");
            var lab2 = Lab(entry, "lab2");
            Assert.Equal(entry.GetProperty("deltaE76").GetDouble(), ColorDifference.DeltaE76(lab1, lab2), Tolerance);
            Assert.Equal(entry.GetProperty("deltaE94").GetDouble(), ColorDifference.DeltaE94(lab1, lab2), Tolerance);
            Assert.Equal(entry.GetProperty("deltaE94Textiles").GetDouble(), ColorDifference.DeltaE94(lab1, lab2, Cie94Application.Textiles), Tolerance);
            Assert.Equal(entry.GetProperty("deltaE2000").GetDouble(), ColorDifference.DeltaE2000(lab1, lab2), Tolerance);
        }

        static CieLab Lab(JsonElement entry, string name)
        {
            var values = Doubles(entry, name);
            return new CieLab(values[0], values[1], values[2]);
        }
    }

    [Fact]
    public void Ciede2000PassesTheTestDataOfSharma()
    {
        // The 34 pairs of Sharma, Wu and Dalal (2005), table 1, as published with the paper
        // (https://hajim.rochester.edu/ece/sites/gsharma/ciede2000/dataNprograms/ciede2000testdata.txt):
        // L1 a1 b1 L2 a2 b2 and the difference to four decimals.
        // The pairs exercise the hue wrap-around, the mean hue of opposite hues and the neutral colors: the difference
        // is checked both ways, since the formula is symmetric
        using var reader = new StreamReader(OpenResource("ciede2000-sharma.txt"));
        var count = 0;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = line.Split('\t', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            var lab1 = new CieLab(values[0], values[1], values[2]);
            var lab2 = new CieLab(values[3], values[4], values[5]);
            Assert.Equal(values[6], Math.Round(ColorDifference.DeltaE2000(lab1, lab2), 4, MidpointRounding.AwayFromZero), 0.0);
            Assert.Equal(values[6], Math.Round(ColorDifference.DeltaE2000(lab2, lab1), 4, MidpointRounding.AwayFromZero), 0.0);
            count++;
        }

        Assert.Equal(34, count);
    }

    // The 8-bit conversions read v / 255, the 16-bit ones v / 65535: an 8-bit color is converted by its own overloads
    private static void AssertRgbConversions(Rgba64 color, Rgba32? color8Value, JsonElement entry, int[] rgb)
    {
        var context = string.Join(", ", rgb);
        var is8Bit = color8Value.HasValue;
        var color8 = color8Value.GetValueOrDefault();

        var xyz = is8Bit ? CieXyz.FromRgb(color8) : CieXyz.FromRgb(color);
        AssertComponents(Doubles(entry, "xyzD50"), [xyz.X, xyz.Y, xyz.Z], Tolerance, context);

        var lab = is8Bit ? CieLab.FromRgb(color8) : CieLab.FromRgb(color);
        AssertComponents(Doubles(entry, "lab"), [lab.L, lab.A, lab.B], Tolerance, context);
        var lch = is8Bit ? CieLch.FromRgb(color8) : CieLch.FromRgb(color);
        AssertPolar(Doubles(entry, "lch"), lch.L, lch.C, lch.H, Tolerance, powerlessChroma: 0.0015, context);

        var oklab = is8Bit ? Oklab.FromRgb(color8) : Oklab.FromRgb(color);
        AssertComponents(Doubles(entry, "oklab"), [oklab.L, oklab.A, oklab.B], Tolerance, context);
        var oklch = is8Bit ? Oklch.FromRgb(color8) : Oklch.FromRgb(color);
        AssertPolar(Doubles(entry, "oklch"), oklch.L, oklch.C, oklch.H, Tolerance, powerlessChroma: 0.000004, context);

        // CSS gives the hue first and makes the hue of a gray missing
        var hsl = is8Bit ? Hsl.FromRgb(color8) : Hsl.FromRgb(color);
        var expectedHsl = Doubles(entry, "hsl");
        AssertPolar([expectedHsl[2], expectedHsl[1], expectedHsl[0]], hsl.L, hsl.S, hsl.H, Tolerance, powerlessChroma: 0.00001, context);
        var hwb = is8Bit ? Hwb.FromRgb(color8) : Hwb.FromRgb(color);
        var expectedHwb = Doubles(entry, "hwb");
        AssertComponents([expectedHwb[1], expectedHwb[2]], [hwb.W, hwb.B], Tolerance, context);
        if (!double.IsNaN(expectedHwb[0]))
        {
            AssertHue(expectedHwb[0], hwb.H, context);
        }

        var cmyk = is8Bit ? Cmyk.FromRgb(color8) : Cmyk.FromRgb(color);
        AssertComponents(Doubles(entry, "cmyk"), [cmyk.C, cmyk.M, cmyk.Y, cmyk.K], Tolerance, context);

        // Back to RGB: the color itself
        Assert.Equal(color, lab.ToRgba64());
        Assert.Equal(color, oklch.ToRgba64());
        Assert.Equal(color, hsl.ToRgba64());
        Assert.Equal(color, hwb.ToRgba64());
        if (is8Bit)
        {
            Assert.Equal(color8, lab.ToRgba32());
            Assert.Equal(color8, oklab.ToRgba32());
            Assert.Equal(color8, hsl.ToRgba32());
            Assert.Equal(color8, Hsv.FromRgb(color8).ToRgba32());
            Assert.Equal(color8, cmyk.ToRgba32());
        }
    }

    // Lightness, chroma (or saturation) and hue; a reference hue that is not a number is a powerless one: the chroma is then tiny
    private static void AssertPolar(double[] expected, double lightness, double chroma, double hue, double tolerance, double powerlessChroma, string context)
    {
        Assert.Equal(expected[0], lightness, tolerance, $"{context}: lightness");
        Assert.Equal(expected[1], chroma, tolerance, $"{context}: chroma");
        if (double.IsNaN(expected[2]))
        {
            Assert.True(chroma <= powerlessChroma, $"{context}: the hue is powerless, but the chroma is {chroma}");
            return;
        }

        // A hue whose chroma is tiny is numerical noise in both
        if (expected[1] > 1e-6)
        {
            AssertHue(expected[2], hue, context);
        }
    }

    private static void AssertHue(double expected, double actual, string context)
    {
        Assert.True(actual >= 0 && actual < 360, $"{context}: the hue {actual} is not in [0, 360)");
        var difference = Math.Abs(expected - actual);
        Assert.True(Math.Min(difference, 360 - difference) <= 1e-7, $"{context}: the hue is {actual}, expected {expected}");
    }

    private static void AssertComponents(double[] expected, double[] actual, double tolerance, string context)
    {
        Assert.HasCount(expected.Length, actual);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], tolerance, $"{context}: component {i}");
        }
    }

    private static double[] Doubles(JsonElement entry, string name)
        => entry.GetProperty(name).EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Null ? double.NaN : value.GetDouble()).ToArray();

    private static int[] Ints(JsonElement entry, string name) => entry.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();

    private static JsonDocument Load(string name)
    {
        using var stream = OpenResource(name);
        return JsonDocument.Parse(stream);
    }

    private static Stream OpenResource(string name)
        => typeof(ColorValueReferenceTests).Assembly.GetManifestResourceStream("ColorValues/" + name) ?? throw new InvalidOperationException($"The resource {name} is missing.");
}
