using System.Text;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Tone curves (gamma, levels, curves through points, one-dimensional tables) and <c>.cube</c> color lookup tables. The
/// expectations are hand-computed from the definitions, or come from independent implementations, run in a
/// <c>python:3.13-slim</c> container (numpy 2.5.4, scipy 1.18.1, Debian's ImageMagick 7.1.1-43 Q16 and FFmpeg 7.1.5):
/// SciPy's <c>PchipInterpolator</c> for the curves through points, ImageMagick's <c>-gamma 2.2</c> and
/// <c>-level 10%,90%,1.6</c> on 16-bit gray samples (<see cref="ImageMagickGray16"/>), and FFmpeg's <c>lut3d</c> and
/// <c>lut1d</c> filters on <c>rgb48le</c> frames of <see cref="CreateLutInput"/> (<see cref="TestGrade"/>). FFmpeg's
/// <c>lut3d</c> scales by <c>1 / (DOMAIN_MAX - DOMAIN_MIN)</c> but ignores the offset of <c>DOMAIN_MIN</c>, so it only
/// checks domains that start at 0; the other domains are checked by hand.
/// </summary>
public sealed class ToneAdjustmentTests
{
    // The 256 samples of ImageMagickGray16, big-endian, after "-level 10%,90%,1.6" and "-gamma 2.2"
    private const string ImageMagickLevels16 = "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000aee0ccd1294176d18a61cd71dec21b3253d262c29782c9f2d753073313f341a36de379c3a463cdd3d90401240c0433045924637488b492c4b714dab4e465074529a532f554a55dc57ed59f65a845c855e7f5f0860fa6182636d655265d667b4698d6a0e6be06c606e2c6ff57071723473f3746d7627769f78547a057a7b7c287dd17e457fea805d81fe839b840c85a6873d87ac894089ae8b3d8ccb8d378ec18f2c90b3923792a1942395a1960a978697ee99679ade9b449cb89e2b9e909fffa064a1d1a33ca39fa508a66fa6d1a836a897a9faab5aabbbad19ae76aed6b030b08fb1e8b33fb39db4f2b646b6a3b7f5b851b9a2baf0bb4cbc99bde4be3fbf89bfe3c12bc272c2cbc410c469c5adc6efc747c888c9c7ca1fcb5dcbb4ccf1ce2cce82cfbcd0f5d14bd282d2d8d40ed543d597d6cbd7fed852d983d9d7db07dc36dc89ddb7dee4df36e062e0b4e1dfe308e35ae482e5aae5fbe721e772e897e9bcea0ceb2fec52eca1edc3ee12ef32f052f0a1f1bff20ef32bf448f496f5b2f6cdf71af835f882f99bfab3fb00fc17fd2efd7bfe90fedcfff1ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string ImageMagickGamma16 = "000017db20b022a728e62e2a2f7d33fa352139193cc33db84113443c4512480548ce4b954e3e4ef4517c53ec549456e9578a59c85bf65c8c5ea660b26140633b63c565b16793681569ea6bb66c326df26e6b702071ce724273e6758375f3778877f579827b087b727cf27d5a7ed2804580aa8217838083e1854485a48701885a88b78a0b8b5b8bb68d018d5b8ea28fe4903c917b92b6930c9444949995cc96fd9750987d99a799f89b1f9b6f9c939db49e039f21a03ca089a1a2a1eea304a418a463a573a682a6cca7d8a821a92aaa32aa7aab7fabc6acc9adcaae10af0fb00cb051b14cb190b289b380b3c4b4b9b5adb5efb6e1b723b812b901b942ba2ebb19bb5abc43bc83bd6abe51be8fbf74c058c096c177c1b5c295c374c3b1c48fc56bc5a7c682c6bec797c870c8abc982ca58ca92cb67cba1cc74cd46cd80ce51ce8acf5ad028d061d12ed1fbd233d2fed336d400d4c9d500d5c8d690d6c6d78cd7c3d888d94cd982da45db07db3cdbfedc33dcf3ddb3dde7dea6df64df98e054e088e144e1ffe232e2ede3a6e3d9e492e4c4e57ce633e665e71ce7d1e803e8b7e8e9e99dea50ea81eb34eb64ec16ecc7ecf7eda8ee57ee87ef36ef66f014f0c1f0f1f19ef24af279f324f353f3fef4a8f4d7f580f629f657f700f72ef7d5f87cf8aaf950f9f6fa23fac9faf6fb9afc3efc6bfd0ffdb1fddefe80feadff4efff0ffff";

    // SciPy's PchipInterpolator through the points of the curves below, at every 8-bit level, rounded to the nearest level
    private const string ScipyCurveS8 = "000000010101020202020303040404050506060707080809090a0a0b0c0c0d0d0e0f0f10111112131414151616171819191a1b1c1d1d1e1f2021212223242526262728292a2b2c2d2e2f30323334353638393a3c3d3e404142444547484a4b4d4e505153545658595b5c5e5f6163646667696b6c6e6f7172747677797a7c7d7f8082838587888a8c8d8f91939596989a9c9ea0a2a4a6a7a9abadafb1b3b5b7b9babcbec0c2c4c5c7c9cacccecfd1d2d4d5d7d8d9dbdcdddedfe0e1e2e3e4e5e5e6e7e7e8e9e9eaeaebececededeeefeff0f0f1f1f2f2f3f3f4f4f5f5f6f6f7f7f8f8f9f9f9fafafafbfbfbfcfcfcfdfdfdfdfefefefefefeffffffffffffffff";
    private const string ScipyCurveWave8 = "3333333333333333333333333333333333333333333333333333363d43494f555b60656b7074797e82878b8f93969a9da1a4a7aaacafb2b4b6b8bbbcbec0c1c3c4c5c7c8c8c9cacbcbcbcccccccccbcac9c7c5c2bfbcb8b4b0aca8a39e99948f8a85807c77726e6965615d5a575452504e4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4e4e4e4e4e4e4e4f4f4f4f4f5050505051515152525253535454545555565657575859595a5b5b5c5d5d5e5f60616162636465666768696a6b6c6e6f70717374757778797b7c7e8081838486888a8b8d8f91939597999b9d9fa1a4a6a8abadafb2b4b7b9bcbfc1c4c7cacdd0d3d6d9dcdfe2e6";

    // FFmpeg 7.1.5 lut3d / lut1d on the rgb48le frame of CreateLutInput (base64)
    private const string FfmpegTetrahedral = "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";
    private const string FfmpegTrilinear = "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";
    private const string FfmpegDomainTetrahedral = "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";
    private const string FfmpegDomainTrilinear = "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";
    private const string FfmpegLut1D = "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";

    // A nonlinear grade of 5 x 5 x 5 points: 0.5 + 0.45 sin(3r + g - b), (rg + b^2) / 2, 0.9 sqrt(b) (1 - 0.3 r)
    private const string TestGrade = """
        TITLE "Test grade"
        # a nonlinear grade
        LUT_3D_SIZE 5
        0.500000 0.000000 0.000000
        0.806737 0.000000 0.000000
        0.948873 0.000000 0.000000
        0.850133 0.000000 0.000000
        0.563504 0.000000 0.000000
        0.611332 0.000000 0.000000
        0.878662 0.031250 0.000000
        0.942794 0.062500 0.000000
        0.769312 0.093750 0.000000
        0.451312 0.125000 0.000000
        0.715741 0.000000 0.000000
        0.927043 0.062500 0.000000
        0.909184 0.125000 0.000000
        0.671747 0.187500 0.000000
        0.342148 0.250000 0.000000
        0.806737 0.000000 0.000000
        0.948873 0.093750 0.000000
        0.850133 0.187500 0.000000
        0.563504 0.281250 0.000000
        0.242797 0.375000 0.000000
        0.878662 0.000000 0.000000
        0.942794 0.125000 0.000000
        0.769312 0.250000 0.000000
        0.451312 0.375000 0.000000
        0.159439 0.500000 0.000000
        0.388668 0.031250 0.450000
        0.715741 0.031250 0.416250
        0.927043 0.031250 0.382500
        0.909184 0.031250 0.348750
        0.671747 0.031250 0.315000
        0.500000 0.031250 0.450000
        0.806737 0.062500 0.416250
        0.948873 0.093750 0.382500
        0.850133 0.125000 0.348750
        0.563504 0.156250 0.315000
        0.611332 0.031250 0.450000
        0.878662 0.093750 0.416250
        0.942794 0.156250 0.382500
        0.769312 0.218750 0.348750
        0.451312 0.281250 0.315000
        0.715741 0.031250 0.450000
        0.927043 0.125000 0.416250
        0.909184 0.218750 0.382500
        0.671747 0.312500 0.348750
        0.342148 0.406250 0.315000
        0.806737 0.031250 0.450000
        0.948873 0.156250 0.416250
        0.850133 0.281250 0.382500
        0.563504 0.406250 0.348750
        0.242797 0.531250 0.315000
        0.284259 0.125000 0.636396
        0.611332 0.125000 0.588666
        0.878662 0.125000 0.540937
        0.942794 0.125000 0.493207
        0.769312 0.125000 0.445477
        0.388668 0.125000 0.636396
        0.715741 0.156250 0.588666
        0.927043 0.187500 0.540937
        0.909184 0.218750 0.493207
        0.671747 0.250000 0.445477
        0.500000 0.125000 0.636396
        0.806737 0.187500 0.588666
        0.948873 0.250000 0.540937
        0.850133 0.312500 0.493207
        0.563504 0.375000 0.445477
        0.611332 0.125000 0.636396
        0.878662 0.218750 0.588666
        0.942794 0.312500 0.540937
        0.769312 0.406250 0.493207
        0.451312 0.500000 0.445477
        0.715741 0.125000 0.636396
        0.927043 0.250000 0.588666
        0.909184 0.375000 0.540937
        0.671747 0.500000 0.493207
        0.342148 0.625000 0.445477
        0.193263 0.281250 0.779423
        0.500000 0.281250 0.720966
        0.806737 0.281250 0.662509
        0.948873 0.281250 0.604053
        0.850133 0.281250 0.545596
        0.284259 0.281250 0.779423
        0.611332 0.312500 0.720966
        0.878662 0.343750 0.662509
        0.942794 0.375000 0.604053
        0.769312 0.406250 0.545596
        0.388668 0.281250 0.779423
        0.715741 0.343750 0.720966
        0.927043 0.406250 0.662509
        0.909184 0.468750 0.604053
        0.671747 0.531250 0.545596
        0.500000 0.281250 0.779423
        0.806737 0.375000 0.720966
        0.948873 0.468750 0.662509
        0.850133 0.562500 0.604053
        0.563504 0.656250 0.545596
        0.611332 0.281250 0.779423
        0.878662 0.406250 0.720966
        0.942794 0.531250 0.662509
        0.769312 0.656250 0.604053
        0.451312 0.781250 0.545596
        0.121338 0.500000 0.900000
        0.388668 0.500000 0.832500
        0.715741 0.500000 0.765000
        0.927043 0.500000 0.697500
        0.909184 0.500000 0.630000
        0.193263 0.500000 0.900000
        0.500000 0.531250 0.832500
        0.806737 0.562500 0.765000
        0.948873 0.593750 0.697500
        0.850133 0.625000 0.630000
        0.284259 0.500000 0.900000
        0.611332 0.562500 0.832500
        0.878662 0.625000 0.765000
        0.942794 0.687500 0.697500
        0.769312 0.750000 0.630000
        0.388668 0.500000 0.900000
        0.715741 0.593750 0.832500
        0.927043 0.687500 0.765000
        0.909184 0.781250 0.697500
        0.671747 0.875000 0.630000
        0.500000 0.500000 0.900000
        0.806737 0.625000 0.832500
        0.948873 0.750000 0.765000
        0.850133 0.875000 0.697500
        0.563504 1.000000 0.630000
        """;

    private static readonly PointD[] SCurve = [new(0, 0), new(0.25, 0.15), new(0.5, 0.5), new(0.75, 0.9), new(1, 1)];
    private static readonly PointD[] WaveCurve = [new(0.1, 0.2), new(0.3, 0.8), new(0.45, 0.3), new(1, 0.9)];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -----------------------------------------------------------------------------------------------------------------
    // Tone curves
    // -----------------------------------------------------------------------------------------------------------------

    #region ToneCurve_Levels
    [Fact]
    public void ToneCurve_Levels()
    {
        using var image = new Image<Rgb24>(2, 1);
        image.Frames[0][0, 0] = new Rgb24(64, 128, 192);
        image.Frames[0][1, 0] = new Rgb24(10, 240, 255);

        // Clip the darkest and the brightest 10% of the levels and brighten the midtones, then add contrast with an
        // S curve through control points
        var levels = ToneCurve.CreateLevels(inputBlack: 0.1, inputWhite: 0.9, gamma: 1.2);
        var contrast = ToneCurve.CreateFromPoints(new PointD(0, 0), new PointD(0.25, 0.2), new PointD(0.75, 0.8), new PointD(1, 1));
        image.ApplyToneCurve(levels.Then(contrast));

        Assert.Equal(new Rgb24(51, 149, 225), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(0, 255, 255), image.Frames[0][1, 0]);
    }
    #endregion

    [Fact]
    public void Gamma_And_Levels_MatchImageMagick()
    {
        using var levels = CreateGray16(ImageMagickGray16());
        levels.ApplyToneCurve(ToneCurve.CreateLevels(0.1, 0.9, 1.6), Ct);
        Assert.Equal(ImageMagickLevels16, ReadGray16Hex(levels));

        using var gamma = CreateGray16(ImageMagickGray16());
        gamma.ApplyToneCurve(ToneCurve.CreateGamma(2.2), Ct);
        Assert.Equal(ImageMagickGamma16, ReadGray16Hex(gamma));
    }

    [Fact]
    public void Gamma_Brightens_And_KeepsTheSign()
    {
        var curve = ToneCurve.CreateGamma(2);
        Assert.Equal(0.5, curve.Evaluate(0.25));
        Assert.Equal(-0.5, curve.Evaluate(-0.25));
        Assert.Equal(2, curve.Evaluate(4));
        Assert.Equal(0, curve.Evaluate(0));
        Assert.Equal(1, curve.Evaluate(1));
        Assert.True(double.IsNaN(curve.Evaluate(double.NaN)));
        Assert.Same(ToneCurve.Identity, ToneCurve.CreateGamma(1));
    }

    [Fact]
    public void Levels_Clip_AndMapTheOutputRange()
    {
        var curve = ToneCurve.CreateLevels(0.2, 0.6, gamma: 1, outputBlack: 0.1, outputWhite: 0.9);
        Assert.Equal(0.1, curve.Evaluate(-3));
        Assert.Equal(0.1, curve.Evaluate(0.2));
        Assert.Equal(0.5, curve.Evaluate(0.4), 1e-15);
        Assert.Equal(0.9, curve.Evaluate(0.6));
        Assert.Equal(0.9, curve.Evaluate(7));

        // An output black above the output white inverts the values
        var inverse = ToneCurve.CreateLevels(0, 1, outputBlack: 1, outputWhite: 0);
        Assert.Equal(0.75, inverse.Evaluate(0.25));
    }

    [Fact]
    public void Curve_MatchesScipyPchip()
    {
        // PchipInterpolator(x, y)(v), printed with repr
        var s = ToneCurve.CreateFromPoints(SCurve);
        AssertClose(0.01528, s.Evaluate(0.05));
        AssertClose(0.20133333333333328, s.Evaluate(0.3));
        AssertClose(0.23965973333333335, s.Evaluate(0.33));
        AssertClose(0.6791999999999999, s.Evaluate(0.6));
        AssertClose(0.91265152, s.Evaluate(0.77));
        AssertClose(0.9997785599999999, s.Evaluate(0.99));

        // A curve that changes direction: flat before its first point and after its last one
        var wave = ToneCurve.CreateFromPoints(WaveCurve);
        AssertClose(0.6654761904761907, wave.Evaluate(0.2));
        AssertClose(0.748, wave.Evaluate(0.33));
        AssertClose(0.4296296296296295, wave.Evaluate(0.4));
        AssertClose(0.31217129977460556, wave.Evaluate(0.6));
        AssertClose(0.6286250939143501, wave.Evaluate(0.9));
        Assert.Equal(0.2, wave.Evaluate(0.05));
        Assert.Equal(0.9, wave.Evaluate(1.5));
        Assert.Equal(0.8, wave.Evaluate(0.3));

        // Two points give a straight line
        AssertClose(0.4, ToneCurve.CreateFromPoints(new PointD(0, 0.2), new PointD(1, 1)).Evaluate(0.25));

        using var gray = CreateGray8Gradient();
        gray.ApplyToneCurve(s, Ct);
        Assert.Equal(ScipyCurveS8, ReadGray8Hex(gray));

        using var waved = CreateGray8Gradient();
        waved.ApplyToneCurve(wave, Ct);
        Assert.Equal(ScipyCurveWave8, ReadGray8Hex(waved));

        static void AssertClose(double expected, double actual) => Assert.Equal(expected, actual, 1e-14);
    }

    [Fact]
    public void Table_InterpolatesLinearly()
    {
        var table = ToneCurve.CreateFromTable(0, 0.5, 0.6, 1);
        Assert.Equal(0.25, table.Evaluate(1.0 / 6), 1e-15);
        Assert.Equal(0.55, table.Evaluate(0.5), 1e-15);
        Assert.Equal(0, table.Evaluate(-1));
        Assert.Equal(1, table.Evaluate(2));

        // A table of 256 values reads one value per 8-bit level
        var values = new double[256];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (255 - i) / 255.0;
        }

        using var gray = CreateGray8Gradient();
        gray.ApplyToneCurve(ToneCurve.CreateFromTable(values), Ct);
        for (var x = 0; x < 256; x++)
        {
            Assert.Equal(255 - x, gray.Frames[0][x, 0].Value);
        }
    }

    [Fact]
    public void ToneCurves_MapEachChannel_AndKeepAlpha()
    {
        var red = ToneCurve.CreateGamma(2);
        var blue = ToneCurve.CreateFromTable(1, 0);
        using var image = new Image<Rgba32>(1, 1);
        image.Frames[0][0, 0] = new Rgba32(100, 100, 100, 7);
        image.ApplyToneCurves(red, ToneCurve.Identity, blue, Ct);
        Assert.Equal(new Rgba32(160, 100, 155, 7), image.Frames[0][0, 0]);

        // A gray pixel stores the Rec. 709 luma of its mapped color: round(255 sqrt(v / 255)), v, 255 - v
        byte[] levels = [0, 50, 100, 200, 255];
        byte[] expected = [18, 75, 117, 195, 237];
        using var gray = new Image<Gray8>(levels.Length, 1);
        using var la = new Image<La16>(levels.Length, 1);
        for (var x = 0; x < levels.Length; x++)
        {
            gray.Frames[0][x, 0] = new Gray8(levels[x]);
            la.Frames[0][x, 0] = new La16(levels[x], 200);
        }

        gray.ApplyToneCurves(red, ToneCurve.Identity, blue, Ct);
        la.ApplyToneCurves(red, ToneCurve.Identity, blue, Ct);
        for (var x = 0; x < levels.Length; x++)
        {
            Assert.Equal(expected[x], gray.Frames[0][x, 0].Value);
            Assert.Equal(new La16(expected[x], 200), la.Frames[0][x, 0]);
        }
    }

    [Fact]
    public void ToneCurve_PackedFormats_AreTheResultOfTheirWorkingFormat()
    {
        var curve = ToneCurve.CreateFromPoints(SCurve);
        using var source = CreateSource(alpha: true);
        using var opaque = CreateSource(alpha: false);
        AssertPacked<Bgr565, Rgb24>(opaque);
        AssertPacked<Rgba1010102, Rgba64>(source);
        AssertPacked<Argb32, Rgba32>(source);

        // An alpha mask has no color
        using var mask = source.CloneAs<A8>();
        using var before = mask.Clone();
        mask.ApplyToneCurve(curve, Ct);
        TestImages.AssertSamePixels(before, mask);

        void AssertPacked<TPacked, TWorking>(Image original)
            where TPacked : unmanaged
            where TWorking : unmanaged
        {
            using var packed = original.CloneAs<TPacked>();
            using var working = packed.CloneAs<TWorking>();
            packed.ApplyToneCurve(curve, Ct);
            working.ApplyToneCurve(curve, Ct);
            using var expected = working.CloneAs<TPacked>();
            TestImages.AssertSamePixels(expected, packed);
        }

        static Image<Rgba64> CreateSource(bool alpha)
        {
            var image = new Image<Rgba64>(64, 4);
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    image.Frames[0][x, y] = new Rgba64((ushort)(x * 1040), (ushort)(65535 - (x * 977)), (ushort)(y * 21845), alpha ? (ushort)(x * 1000) : ushort.MaxValue);
                }
            }

            return image;
        }
    }

    [Fact]
    public void ToneCurve_FloatingPointSamples_AreNotClamped()
    {
        using var image = new Image<RgbaFloat>(2, 1);
        image.Frames[0][0, 0] = new RgbaFloat(4, -0.25f, float.NaN, 0.5f);
        image.Frames[0][1, 0] = new RgbaFloat(0.25f, 1, 0, 2);
        image.ApplyToneCurve(ToneCurve.CreateGamma(2), Ct);
        Assert.Equal(new RgbaFloat(2, -0.5f, float.NaN, 0.5f), image.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(0.5f, 1, 0, 2), image.Frames[0][1, 0]);

        // The levels clip
        image.ApplyToneCurve(ToneCurve.CreateLevels(0, 1), Ct);
        Assert.Equal(new RgbaFloat(1, 0, float.NaN, 0.5f), image.Frames[0][0, 0]);

        using var half = new Image<RgbaHalf>(1, 1);
        half.Frames[0][0, 0] = new RgbaHalf((Half)4, (Half)0.25, (Half)(-1), (Half)1);
        half.ApplyToneCurve(ToneCurve.CreateGamma(2), Ct);
        Assert.Equal(new RgbaHalf((Half)2, (Half)0.5, (Half)(-1), (Half)1), half.Frames[0][0, 0]);
    }

    [Fact]
    public void ToneCurve_OneFrame_LeavesTheOthers()
    {
        using var image = new Image<Gray8>(1, 1, new Gray8(64));
        image.AppendFrame()[0, 0] = new Gray8(64);
        image.Frames[1].ApplyToneCurve(ToneCurve.CreateGamma(2), Ct);
        Assert.Equal(64, image.Frames[0][0, 0].Value);
        Assert.Equal(128, image.Frames[1][0, 0].Value);

        image.ApplyLut(CubeLut.Parse(TestGrade), cancellationToken: Ct);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void ToneCurve_Cancellation_ChangesNothing()
    {
        using var image = new Image<Gray8>(4, 4, new Gray8(64));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.ApplyToneCurve(ToneCurve.CreateGamma(2), source.Token));
        Assert.Throws<OperationCanceledException>(() => image.ApplyLut(CubeLut.Parse(TestGrade), cancellationToken: source.Token));
        Assert.Equal(64, image.Frames[0][3, 3].Value);
    }

    [Fact]
    public void ToneCurve_InvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("gamma", () => ToneCurve.CreateGamma(0));
        Assert.Throws<ArgumentOutOfRangeException>("gamma", () => ToneCurve.CreateGamma(double.PositiveInfinity));
        Assert.Throws<ArgumentException>("inputWhite", () => ToneCurve.CreateLevels(0.5, 0.5));
        Assert.Throws<ArgumentException>("outputBlack", () => ToneCurve.CreateLevels(0, 1, 1, double.NaN));
        Assert.Throws<ArgumentException>("points", () => ToneCurve.CreateFromPoints(new PointD(0, 0)));
        Assert.Throws<ArgumentException>("points", () => ToneCurve.CreateFromPoints(new PointD(0.5, 0), new PointD(0.5, 1)));
        Assert.Throws<ArgumentException>("points", () => ToneCurve.CreateFromPoints(new PointD(0, 0), new PointD(1, double.NaN)));
        Assert.Throws<ArgumentException>("values", () => ToneCurve.CreateFromTable(1));
        Assert.Throws<ArgumentException>("values", () => ToneCurve.CreateFromTable(0, double.PositiveInfinity));

        using var image = new Image<Rgb24>(1, 1);
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).ApplyToneCurve(ToneCurve.Identity));
        Assert.Throws<ArgumentNullException>("curve", () => image.ApplyToneCurve(null!));
        Assert.Throws<ArgumentNullException>("green", () => image.ApplyToneCurves(ToneCurve.Identity, null!, ToneCurve.Identity));
        Assert.Throws<ArgumentNullException>("lut", () => image.ApplyLut(null!));
        Assert.Throws<ArgumentOutOfRangeException>("interpolation", () => image.ApplyLut(CubeLut.Parse(TestGrade), (LutInterpolation)7));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cube LUTs
    // -----------------------------------------------------------------------------------------------------------------

    #region CubeLut_Apply
    [Fact]
    public void CubeLut_Apply()
    {
        // A three-dimensional table of 2 x 2 x 2 points that inverts the colors, as a .cube file holds it
        var lut = CubeLut.Parse("""
            TITLE "Negative"
            LUT_3D_SIZE 2
            1 1 1
            0 1 1
            1 0 1
            0 0 1
            1 1 0
            0 1 0
            1 0 0
            0 0 0
            """);

        using var image = new Image<Rgba32>(1, 1, new Rgba32(255, 64, 0, 128));
        image.ApplyLut(lut);

        Assert.Equal("Negative", lut.Title);
        Assert.Equal(new Rgba32(0, 191, 255, 128), image.Frames[0][0, 0]);
    }
    #endregion

    [Theory]
    [InlineData(LutInterpolation.Tetrahedral, false)]
    [InlineData(LutInterpolation.Trilinear, false)]
    [InlineData(LutInterpolation.Tetrahedral, true)]
    [InlineData(LutInterpolation.Trilinear, true)]
    public void CubeLut_MatchesFfmpeg(LutInterpolation interpolation, bool domain)
    {
        var text = domain ? TestGrade.Replace("LUT_3D_SIZE 5", "LUT_3D_SIZE 5\nDOMAIN_MIN 0 0 0\nDOMAIN_MAX 2 1.5 1.25", StringComparison.Ordinal) : TestGrade;
        var lut = CubeLut.Parse(text);
        Assert.Equal("Test grade", lut.Title);
        Assert.Equal(5, lut.Size);
        Assert.True(lut.IsThreeDimensional);

        using var image = CreateLutInput();
        image.ApplyLut(lut, interpolation, Ct);
        var expected = (interpolation, domain) switch
        {
            (LutInterpolation.Tetrahedral, false) => FfmpegTetrahedral,
            (LutInterpolation.Trilinear, false) => FfmpegTrilinear,
            (LutInterpolation.Tetrahedral, true) => FfmpegDomainTetrahedral,
            _ => FfmpegDomainTrilinear,
        };

        AssertRgb48Close(Convert.FromBase64String(expected), image, FfmpegTolerance);
    }

    [Fact]
    public void CubeLut_OneDimensional_MatchesFfmpeg()
    {
        var lut = CubeLut.Parse("""
            LUT_1D_SIZE 4
            0 0.1 0
            0.5 0.2 0.3
            0.6 0.7 0.9
            1 1 0.95
            """);
        Assert.False(lut.IsThreeDimensional);
        Assert.Null(lut.Title);

        using var image = CreateLutInput();
        image.ApplyLut(lut, cancellationToken: Ct);
        AssertRgb48Close(Convert.FromBase64String(FfmpegLut1D), image, FfmpegTolerance);
    }

    [Theory]
    [InlineData(LutInterpolation.Tetrahedral)]
    [InlineData(LutInterpolation.Trilinear)]
    public void CubeLut_IdentityLattice_KeepsEveryColor(LutInterpolation interpolation)
    {
        // Both interpolations reproduce a linear function exactly
        var lut = CubeLut.Parse(CreateIdentityCube(9, domain: null));
        using var image = CreateLutInput();
        using var expected = image.Clone();
        image.ApplyLut(lut, interpolation, Ct);
        TestImages.AssertSamePixels(expected, image);
    }

    [Fact]
    public void CubeLut_Domain_IsClampedThenScaled()
    {
        // An identity lattice over [0.2, 0.6]: a value v becomes clamp((v - 0.2) / 0.4, 0, 1)
        var lut = CubeLut.Parse(CreateIdentityCube(2, domain: (0.2, 0.6)));
        using var image = new Image<Rgb48>(1, 1, new Rgb48(6554, 26214, 52428));
        image.ApplyLut(lut, cancellationToken: Ct);
        Assert.Equal(new Rgb48(0, 32768, 65535), image.Frames[0][0, 0]);

        using var floats = new Image<RgbaFloat>(1, 1, new RgbaFloat(-1, 0.3f, 5, 0.25f));
        floats.ApplyLut(lut, cancellationToken: Ct);
        var pixel = floats.Frames[0][0, 0];
        Assert.Equal(0, pixel.R);
        Assert.Equal(0.25, pixel.G, 1e-6);
        Assert.Equal(1, pixel.B);
        Assert.Equal(0.25f, pixel.A);

        // A one-dimensional table of the resolve input range keyword
        var range = CubeLut.Parse("LUT_1D_INPUT_RANGE 0.2 0.6\nLUT_1D_SIZE 2\n0 0 0\n1 1 1\n");
        using var image1D = new Image<Rgb48>(1, 1, new Rgb48(6554, 26214, 52428));
        image1D.ApplyLut(range, cancellationToken: Ct);
        Assert.Equal(new Rgb48(0, 32768, 65535), image1D.Frames[0][0, 0]);
    }

    [Fact]
    public void CubeLut_FloatingPointOutput_IsNotClamped()
    {
        var lut = CubeLut.Parse("LUT_3D_SIZE 2\n" + string.Join('\n', Enumerable.Repeat("2 -0.5 0.25", 8)));
        using var image = new Image<RgbaFloat>(2, 1, new RgbaFloat(0.5f, 0.5f, 0.5f, 1));
        image.Frames[0][1, 0] = new RgbaFloat(0.5f, float.NaN, 0.5f, 1);
        image.ApplyLut(lut, cancellationToken: Ct);
        Assert.Equal(new RgbaFloat(2, -0.5f, 0.25f, 1), image.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(float.NaN, float.NaN, float.NaN, 1), image.Frames[0][1, 0]);

        using var bytes = new Image<Rgb24>(1, 1);
        bytes.ApplyLut(lut, cancellationToken: Ct);
        Assert.Equal(new Rgb24(255, 0, 64), bytes.Frames[0][0, 0]);
    }

    [Fact]
    public void CubeLut_GrayFormats_StoreTheLumaOfTheResult()
    {
        var lut = CubeLut.Parse(TestGrade);
        using var gray = CreateGray8Gradient();
        using var color = gray.CloneAs<Rgb24>();
        gray.ApplyLut(lut, cancellationToken: Ct);
        color.ApplyLut(lut, cancellationToken: Ct);
        for (var x = 0; x < 256; x++)
        {
            var c = color.Frames[0][x, 0];
            Assert.Equal((byte)(((2126 * c.R) + (7152 * c.G) + (722 * c.B) + 5000) / 10000), gray.Frames[0][x, 0].Value);
        }
    }

    [Fact]
    public void CubeLut_Load()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(TestGrade)).ToArray();
        using var stream = new MemoryStream(bytes);
        var lut = CubeLut.Load(stream);
        Assert.Equal("Test grade", lut.Title);
        Assert.True(stream.CanRead);

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cube");
        File.WriteAllText(path, TestGrade);
        try
        {
            Assert.Equal(5, CubeLut.Load(path).Size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0 0 0\n", 1)]
    [InlineData("LUT_3D_SIZE 2\nLUT_1D_SIZE 2\n", 2)]
    [InlineData("LUT_3D_SIZE 1\n", 1)]
    [InlineData("LUT_3D_SIZE 257\n", 1)]
    [InlineData("LUT_1D_SIZE 65537\n", 1)]
    [InlineData("LUT_1D_SIZE 2\n0 0 0\n", 2)]
    [InlineData("LUT_1D_SIZE 2\n0 0 0\n1 1 1\n1 1 1\n", 4)]
    [InlineData("LUT_1D_SIZE 2\n0 0\n1 1 1\n", 2)]
    [InlineData("LUT_1D_SIZE 2\n0 0 0 0\n1 1 1\n", 2)]
    [InlineData("LUT_1D_SIZE 2\n0 0 x\n1 1 1\n", 2)]
    [InlineData("LUT_1D_SIZE 2\n0 0 1e39\n1 1 1\n", 2)]
    [InlineData("LUT_1D_SIZE 2\n0 0 0\nTITLE \"late\"\n", 3)]
    [InlineData("LUT_1D_SIZE 2\nLUT_SIZE 2\n", 2)]
    [InlineData("TITLE no quotes\n", 1)]
    [InlineData("TITLE \"a\"\nTITLE \"b\"\n", 2)]
    [InlineData("DOMAIN_MIN 0 0 1\nLUT_1D_SIZE 2\n0 0 0\n1 1 1\n", 3)]
    [InlineData("DOMAIN_MAX 1 1\n", 1)]
    [InlineData("", 0)]
    public void CubeLut_InvalidFiles(string text, int line)
    {
        var exception = Assert.Throws<FormatException>(() => CubeLut.Parse(text));
        Assert.StartsWith($"Invalid .cube file, line {line}:", exception.Message);
    }

    [Fact]
    public void CubeLut_CommentsAndBlankLines_AreIgnored()
    {
        var lut = CubeLut.Parse("# header\n\n  LUT_1D_SIZE\t2  \r\n# data\n 0 0 0 \n\n+1.0E0 1 .5\n");
        Assert.Equal(2, lut.Size);
        using var image = new Image<Rgb24>(1, 1, new Rgb24(255, 255, 255));
        image.ApplyLut(lut, cancellationToken: Ct);
        Assert.Equal(new Rgb24(255, 255, 128), image.Frames[0][0, 0]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------------------------

    // FFmpeg interpolates in single precision: one 16-bit level apart at most
    private const int FfmpegTolerance = 1;

    private static ushort[] ImageMagickGray16()
    {
        var values = new ushort[256];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (ushort)Math.Min(65535, (i * 257) + (i * 97 % 257));
        }

        return values;
    }

    private static Image<Gray16> CreateGray16(ushort[] values)
    {
        var image = new Image<Gray16>(values.Length, 1);
        for (var x = 0; x < values.Length; x++)
        {
            image.Frames[0][x, 0] = new Gray16(values[x]);
        }

        return image;
    }

    private static string ReadGray16Hex(Image<Gray16> image)
    {
        var builder = new StringBuilder();
        for (var x = 0; x < image.Width; x++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{image.Frames[0][x, 0].Value:x4}");
        }

        return builder.ToString();
    }

    private static Image<Gray8> CreateGray8Gradient()
    {
        var image = new Image<Gray8>(256, 1);
        for (var x = 0; x < 256; x++)
        {
            image.Frames[0][x, 0] = new Gray8((byte)x);
        }

        return image;
    }

    private static string ReadGray8Hex(Image<Gray8> image)
    {
        var bytes = new byte[image.Width];
        for (var x = 0; x < bytes.Length; x++)
        {
            bytes[x] = image.Frames[0][x, 0].Value;
        }

        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>16 x 16 colors spread over the cube: the frame given to FFmpeg.</summary>
    private static Image<Rgb48> CreateLutInput()
    {
        var image = new Image<Rgb48>(16, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                image.Frames[0][x, y] = new Rgb48((ushort)(((x * 4369) + (y * 97)) % 65536), (ushort)(((y * 4369) + (x * 211)) % 65536), (ushort)((((x + y) * 2179) + (x * y * 13)) % 65536));
            }
        }

        return image;
    }

    private static void AssertRgb48Close(byte[] expected, Image<Rgb48> actual, int tolerance)
    {
        var max = 0;
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var offset = ((y * actual.Width) + x) * 6;
                var pixel = actual.Frames[0][x, y];
                max = Math.Max(max, Math.Abs(BitConverter.ToUInt16(expected, offset) - pixel.R));
                max = Math.Max(max, Math.Abs(BitConverter.ToUInt16(expected, offset + 2) - pixel.G));
                max = Math.Max(max, Math.Abs(BitConverter.ToUInt16(expected, offset + 4) - pixel.B));
            }
        }

        Assert.True(max <= tolerance, $"The largest difference is {max}.");
    }

    private static string CreateIdentityCube(int size, (double Min, double Max)? domain)
    {
        var builder = new StringBuilder();
        if (domain is { } d)
        {
            builder.Append(CultureInfo.InvariantCulture, $"DOMAIN_MIN {d.Min} {d.Min} {d.Min}\nDOMAIN_MAX {d.Max} {d.Max} {d.Max}\n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"LUT_3D_SIZE {size}\n");
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"{r / (double)(size - 1):R} {g / (double)(size - 1):R} {b / (double)(size - 1):R}\n");
                }
            }
        }

        return builder.ToString();
    }
}
