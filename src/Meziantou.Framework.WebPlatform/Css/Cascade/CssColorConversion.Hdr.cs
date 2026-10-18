using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The HDR color spaces (https://drafts.csswg.org/css-color-hdr-1/): rec2100-pq, rec2100-hlg, rec2100-linear, ictcp, jzazbz
// and jzczhz, with the sample code of https://drafts.csswg.org/css-color-hdr-1/#color-conversion-code
internal static partial class CssColorConversion
{
    /// <summary>The luminance of HDR Reference White, in cd/m² (https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function).</summary>
    public const double ReferenceWhiteLuminance = 203;

    // The Perceptual Quantizer constants (SMPTE ST 2084)
    private const double PqN = 2610.0 / 16384;
    private const double PqM = 2523.0 / 32;
    private const double PqC1 = 3424.0 / 4096;
    private const double PqC2 = 2413.0 / 128;
    private const double PqC3 = 2392.0 / 128;

    // The Hybrid Log-Gamma constants (ITU-R BT.2390); the scale places the 18% grey at 0.38, so HDR Reference White is 0.75
    private const double HlgA = 0.17883277;
    private const double HlgB = 0.28466892;
    private const double HlgC = 0.55991073;
    private const double HlgScale = 3.7743;

    // https://drafts.csswg.org/css-color-hdr-1/#Jzazbz
    private const double JzazbzB = 1.15;
    private const double JzazbzG = 0.66;
    private const double JzazbzP = 1.7 * 2523 / 32;
    private const double JzazbzD = -0.56;
    private const double JzazbzD0 = 1.6295499532821566e-11;

    /// <summary>The epsilon of the powerless hue of jzczhz (https://drafts.csswg.org/css-color-hdr-1/#JzCzhz).</summary>
    private const double JzczhzEpsilon = 0.0000026;

    private static readonly double[][] AbsoluteXyzToJzazbzLms =
    [
        [0.41478972, 0.579999, 0.0146480],
        [-0.2015100, 1.120649, 0.0531008],
        [-0.0166008, 0.264800, 0.6684799],
    ];

    private static readonly double[][] JzazbzLmsToAbsoluteXyz = Invert(AbsoluteXyzToJzazbzLms);

    private static readonly double[][] JzazbzLmsToIab =
    [
        [0.5, 0.5, 0],
        [3.524000, -4.066708, 0.542708],
        [0.199076, 1.096799, -1.295875],
    ];

    private static readonly double[][] IabToJzazbzLms = Invert(JzazbzLmsToIab);

    // The XYZ to LMS matrix of ICtCp includes the 4% crosstalk (https://drafts.csswg.org/css-color-hdr-1/#ictcp_code)
    private static readonly double[][] AbsoluteXyzToIctcpLms =
    [
        [0.3592832590121217, 0.6976051147779502, -0.0358915932320290],
        [-0.1920808463704993, 1.1004767970374321, 0.0753748658519118],
        [0.0070797844607479, 0.0748396662186362, 0.8433265453898765],
    ];

    private static readonly double[][] IctcpLmsToAbsoluteXyz = Invert(AbsoluteXyzToIctcpLms);

    private static readonly double[][] IctcpLmsToIctcp =
    [
        [2048.0 / 4096, 2048.0 / 4096, 0],
        [6610.0 / 4096, -13613.0 / 4096, 7003.0 / 4096],
        [17933.0 / 4096, -17390.0 / 4096, -543.0 / 4096],
    ];

    private static readonly double[][] IctcpToIctcpLms = Invert(IctcpLmsToIctcp);

    /// <summary>Converts an HDR color to CIE XYZ (D65, relative to HDR Reference White), or returns null when the color space is not an HDR color space.</summary>
    private static double[]? HdrToXyz(string space, double[] c) => space switch
    {
        "rec2100-linear" => Multiply(LinearRec2020ToXyz, c),
        "rec2100-pq" => Multiply(LinearRec2020ToXyz, Map(c, PqToLinear)),
        "rec2100-hlg" => Multiply(LinearRec2020ToXyz, Map(c, HlgToLinear)),
        "jzazbz" => JzazbzToXyz(c),
        "jzczhz" => JzazbzToXyz(LchToLab(c)),
        "ictcp" => IctcpToXyz(c),
        _ => null,
    };

    /// <summary>Converts a color from CIE XYZ (D65, relative to HDR Reference White) to an HDR color space, or returns null when the color space is not an HDR color space; a powerless hue is NaN.</summary>
    private static double[]? XyzToHdr(string space, double[] xyz) => space switch
    {
        "rec2100-linear" => Multiply(XyzToLinearRec2020, xyz),
        "rec2100-pq" => Map(Multiply(XyzToLinearRec2020, xyz), LinearToPq),
        "rec2100-hlg" => Map(Multiply(XyzToLinearRec2020, xyz), LinearToHlg),
        "jzazbz" => XyzToJzazbz(xyz),
        "jzczhz" => LabToLch(XyzToJzazbz(xyz), JzczhzEpsilon),
        "ictcp" => XyzToIctcp(xyz),
        _ => null,
    };

    /// <summary>
    /// https://drafts.csswg.org/css-color-hdr-1/#headroom-interpolation: the color of color-hdr() at a headroom (in stops). The
    /// colors are interpolated in absolute CIE XYZ (D65), as a weighted geometric mean; the alpha is interpolated linearly.
    /// When the two headrooms are the same (they can only be math functions), the first color is used. Returns null when a
    /// color space is not supported (e.g. a custom color space).
    /// </summary>
    public static CssColor? InterpolateByHeadroom(CssColor first, double firstHeadroom, CssColor second, double secondHeadroom, double headroom)
    {
        if (FromColor(first) is not { } c1 || FromColor(second) is not { } c2)
            return null;

        if (firstHeadroom == secondHeadroom)
            return first;

        const double Epsilon = 0.001;
        var w1 = Math.Clamp((headroom - secondHeadroom) / (firstHeadroom - secondHeadroom), 0, 1);
        var w2 = Math.Clamp((headroom - firstHeadroom) / (secondHeadroom - firstHeadroom), 0, 1);
        var xyz1 = Convert(c1, "xyz-d65").Channels;
        var xyz2 = Convert(c2, "xyz-d65").Channels;
        var channels = new double?[3];
        for (var i = 0; i < 3; i++)
        {
            var a = ((xyz1[i] ?? 0) * ReferenceWhiteLuminance) + Epsilon;
            var b = ((xyz2[i] ?? 0) * ReferenceWhiteLuminance) + Epsilon;
            channels[i] = ((Math.Pow(a, w1) * Math.Pow(b, w2)) - Epsilon) / ReferenceWhiteLuminance;
        }

        return ToColor(new CssColorComponents("xyz-d65", channels, ((c1.Alpha ?? 0) * w1) + ((c2.Alpha ?? 0) * w2)));
    }

    /// <summary>The PQ inverse EOTF of a linear-light component relative to HDR Reference White (10,000 cd/m² is 1).</summary>
    private static double LinearToPq(double value) => PqEncode(Math.Max(value * ReferenceWhiteLuminance / 10000, 0), PqM);

    /// <summary>The PQ EOTF: a linear-light component relative to HDR Reference White.</summary>
    private static double PqToLinear(double value) => PqDecode(value, PqM) * 10000 / ReferenceWhiteLuminance;

    private static double PqEncode(double value, double m)
    {
        var power = SignedPow(value, PqN);
        return SignedPow((PqC1 + (PqC2 * power)) / (1 + (PqC3 * power)), m);
    }

    /// <param name="clampBlack">Whether the values below black are clamped to black (Jzazbz keeps them, so its conversions round-trip).</param>
    private static double PqDecode(double value, double m, bool clampBlack = true)
    {
        var power = SignedPow(value, 1 / m);
        var numerator = power - PqC1;
        return SignedPow((clampBlack ? Math.Max(numerator, 0) : numerator) / (PqC2 - (PqC3 * power)), 1 / PqN);
    }

    /// <summary>The HLG OETF of a linear-light component relative to HDR Reference White.</summary>
    private static double LinearToHlg(double value)
    {
        value /= HlgScale;
        return value <= 1.0 / 12 ? SignedPow(3 * value, 0.5) : (HlgA * Math.Log((12 * value) - HlgB)) + HlgC;
    }

    /// <summary>The HLG inverse OETF: a linear-light component relative to HDR Reference White.</summary>
    private static double HlgToLinear(double value) => value <= 0.5 ? value * value / 3 * HlgScale : (Math.Exp((value - HlgC) / HlgA) + HlgB) / 12 * HlgScale;

    private static double[] XyzToJzazbz(double[] xyz)
    {
        // The absolute XYZ, with X and Y modified to minimize the blue curvature
        var (x, y, z) = (xyz[0] * ReferenceWhiteLuminance, xyz[1] * ReferenceWhiteLuminance, xyz[2] * ReferenceWhiteLuminance);
        var lms = Multiply(AbsoluteXyzToJzazbzLms, [(JzazbzB * x) - ((JzazbzB - 1) * z), (JzazbzG * y) - ((JzazbzG - 1) * x), z]);
        var iab = Multiply(JzazbzLmsToIab, Map(lms, v => PqEncode(v / 10000, JzazbzP)));
        var iz = iab[0];
        return [((1 + JzazbzD) * iz / (1 + (JzazbzD * iz))) - JzazbzD0, iab[1], iab[2]];
    }

    private static double[] JzazbzToXyz(double[] jzazbz)
    {
        var jz = jzazbz[0] + JzazbzD0;
        var iz = jz / (1 + JzazbzD - (JzazbzD * jz));
        var lms = Map(Multiply(IabToJzazbzLms, [iz, jzazbz[1], jzazbz[2]]), v => PqDecode(v, JzazbzP, clampBlack: false) * 10000);
        var modified = Multiply(JzazbzLmsToAbsoluteXyz, lms);
        var z = modified[2];
        var x = (modified[0] + ((JzazbzB - 1) * z)) / JzazbzB;
        var y = (modified[1] + ((JzazbzG - 1) * x)) / JzazbzG;
        return [x / ReferenceWhiteLuminance, y / ReferenceWhiteLuminance, z / ReferenceWhiteLuminance];
    }

    private static double[] XyzToIctcp(double[] xyz)
    {
        var lms = Multiply(AbsoluteXyzToIctcpLms, [xyz[0] * ReferenceWhiteLuminance, xyz[1] * ReferenceWhiteLuminance, xyz[2] * ReferenceWhiteLuminance]);
        return Multiply(IctcpLmsToIctcp, Map(lms, v => PqEncode(v / 10000, PqM)));
    }

    private static double[] IctcpToXyz(double[] ictcp)
    {
        var lms = Map(Multiply(IctcpToIctcpLms, ictcp), v => PqDecode(v, PqM) * 10000);
        var xyz = Multiply(IctcpLmsToAbsoluteXyz, lms);
        return [xyz[0] / ReferenceWhiteLuminance, xyz[1] / ReferenceWhiteLuminance, xyz[2] / ReferenceWhiteLuminance];
    }

    /// <summary>The power of the absolute value with the sign of the value (spow of the sample code).</summary>
    private static double SignedPow(double value, double exponent) => Math.CopySign(Math.Pow(Math.Abs(value), exponent), value);

    private static double[][] Invert(double[][] m)
    {
        var determinant = (m[0][0] * ((m[1][1] * m[2][2]) - (m[1][2] * m[2][1])))
            - (m[0][1] * ((m[1][0] * m[2][2]) - (m[1][2] * m[2][0])))
            + (m[0][2] * ((m[1][0] * m[2][1]) - (m[1][1] * m[2][0])));

        return
        [
            [((m[1][1] * m[2][2]) - (m[1][2] * m[2][1])) / determinant, ((m[0][2] * m[2][1]) - (m[0][1] * m[2][2])) / determinant, ((m[0][1] * m[1][2]) - (m[0][2] * m[1][1])) / determinant],
            [((m[1][2] * m[2][0]) - (m[1][0] * m[2][2])) / determinant, ((m[0][0] * m[2][2]) - (m[0][2] * m[2][0])) / determinant, ((m[0][2] * m[1][0]) - (m[0][0] * m[1][2])) / determinant],
            [((m[1][0] * m[2][1]) - (m[1][1] * m[2][0])) / determinant, ((m[0][1] * m[2][0]) - (m[0][0] * m[2][1])) / determinant, ((m[0][0] * m[1][1]) - (m[0][1] * m[1][0])) / determinant],
        ];
    }
}
