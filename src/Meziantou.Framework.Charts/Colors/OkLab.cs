namespace Meziantou.Framework.Charts;

/// <summary>Conversions between sRGB and OKLab (Björn Ottosson, 2020), for perceptually even color interpolation.</summary>
internal static class OkLab
{
    public static (double L, double A, double B) FromColor(ChartColor color)
    {
        var r = ToLinear(color.R / 255.0);
        var g = ToLinear(color.G / 255.0);
        var b = ToLinear(color.B / 255.0);

        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));

        return (
            (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s));
    }

    public static ChartColor ToColor(double lightness, double a, double b, byte alpha)
    {
        var l = lightness + (0.3963377774 * a) + (0.2158037573 * b);
        var m = lightness - (0.1055613458 * a) - (0.0638541728 * b);
        var s = lightness - (0.0894841775 * a) - (1.2914855480 * b);
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        var red = (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s);
        var green = (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s);
        var blue = (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s);
        return new ChartColor(ToByte(FromLinear(red)), ToByte(FromLinear(green)), ToByte(FromLinear(blue)), alpha);
    }

    public static ChartColor Interpolate(ChartColor start, ChartColor end, double t)
    {
        var (l0, a0, b0) = FromColor(start);
        var (l1, a1, b1) = FromColor(end);
        var alpha = (byte)Math.Round(start.A + ((end.A - start.A) * t), MidpointRounding.AwayFromZero);
        return ToColor(l0 + ((l1 - l0) * t), a0 + ((a1 - a0) * t), b0 + ((b1 - b0) * t), alpha);
    }

    private static double ToLinear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double FromLinear(double value) => value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
