using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

/// <summary>The conversions of the CIE-based color spaces (ISO 32000-2, 8.6.5) to sRGB: XYZ adapted from the white point of the color space to D65 (Bradford), then the sRGB primaries and transfer function.</summary>
internal static class CieConversion
{
    public static RgbColor FromXyz(double x, double y, double z, double[] whitePoint)
    {
        // Bradford chromatic adaptation from the white point of the color space to D65
        var (xs, ys, zs) = Cone(whitePoint[0], whitePoint[1], whitePoint[2]);
        var (xd, yd, zd) = Cone(0.95047, 1, 1.08883);
        var (l, m, s) = Cone(x, y, z);
        l *= xd / Safe(xs);
        m *= yd / Safe(ys);
        s *= zd / Safe(zs);
        var ax = (0.9869929 * l) - (0.1470543 * m) + (0.1599627 * s);
        var ay = (0.4323053 * l) + (0.5183603 * m) + (0.0492912 * s);
        var az = (-0.0085287 * l) + (0.0400428 * m) + (0.9684867 * s);

        var r = (3.2404542 * ax) - (1.5371385 * ay) - (0.4985314 * az);
        var g = (-0.9692660 * ax) + (1.8760108 * ay) + (0.0415560 * az);
        var b = (0.0556434 * ax) - (0.2040259 * ay) + (1.0572252 * az);
        return new RgbColor(Encode(r), Encode(g), Encode(b));
    }

    public static double[] ReadWhitePoint(PdfDictionary? dictionary) => RenderFunction.ReadNumbers(dictionary?.Get<PdfArray>(PdfName.Create("WhitePoint"))) is { Length: 3 } point && point[1] > 0 ? point : [0.9505, 1, 1.089];

    private static (double L, double M, double S) Cone(double x, double y, double z) => ((0.8951 * x) + (0.2664 * y) - (0.1614 * z), (-0.7502 * x) + (1.7135 * y) + (0.0367 * z), (0.0389 * x) - (0.0685 * y) + (1.0296 * z));

    private static double Safe(double value) => Math.Abs(value) < 1e-9 ? 1e-9 : value;

    /// <summary>The sRGB transfer function.</summary>
    private static double Encode(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= 0.0031308 ? 12.92 * value : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
    }
}
