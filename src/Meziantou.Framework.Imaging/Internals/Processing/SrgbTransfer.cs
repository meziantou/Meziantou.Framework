namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sRGB transfer function of IEC 61966-2-1 (also used by sGray), on normalized values in [0, 1]:
/// decoding <c>L = V / 12.92</c> for <c>V &lt;= 0.04045</c>, else <c>((V + 0.055) / 1.055)^2.4</c>; encoding
/// <c>V = 12.92 L</c> for <c>L &lt;= 0.0031308</c>, else <c>1.055 L^(1 / 2.4) - 0.055</c>.
/// </summary>
/// <remarks>
/// Decoding of stored samples goes through tables built once per process from <see cref="Decode(double)"/> (256 entries
/// for 8-bit samples, 65,536 for 16-bit samples; static, shared, never charged to an image scope). Results depend on
/// <see cref="Math.Pow(double, double)"/>, which may differ by one unit in the last place between platforms; this can only
/// change a rounded output sample whose exact value is within about 1e-12 of a rounding boundary.
/// </remarks>
internal static class SrgbTransfer
{
    private static double[]? s_decode8;
    private static double[]? s_decode16;

    /// <summary>Converts an encoded value to linear light.</summary>
    public static double Decode(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary>Converts linear light to an encoded value.</summary>
    public static double Encode(double value) => value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;

    /// <summary>Gets the linear value of every 8-bit sample: <c>Decode(v / 255)</c>.</summary>
    public static ReadOnlySpan<double> Decode8 => s_decode8 ??= CreateTable(byte.MaxValue);

    /// <summary>Gets the linear value of every 16-bit sample: <c>Decode(v / 65535)</c>.</summary>
    public static ReadOnlySpan<double> Decode16 => s_decode16 ??= CreateTable(ushort.MaxValue);

    private static double[] CreateTable(int max)
    {
        var table = new double[max + 1];
        for (var i = 0; i <= max; i++)
        {
            table[i] = Decode((double)i / max);
        }

        return table;
    }
}
