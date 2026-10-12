namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The transfer characteristics of Rec. ITU-T H.273 (ISO/IEC 23091-2), table 3, on normalized values: the function of
/// each code point, which gives an encoded value <c>V</c> for a linear value <c>L</c> (<see cref="Encode"/>), and its
/// inverse (<see cref="Decode"/>).
/// </summary>
/// <remarks>
/// <para>
/// A linear value of 1 is the nominal peak of the code point: reference white for the standard dynamic range functions,
/// 10,000 cd/m2 for PQ (16, SMPTE ST 2084 and Rec. ITU-R BT.2100 table 4), the peak of the scene for HLG (18,
/// Rec. ITU-R BT.2100 table 5, the OETF without the OOTF of a display) and 52.37 cd/m2 for SMPTE ST 428-1 (17), whose
/// encoded value 1 therefore decodes to more than its reference white of 48 cd/m2.
/// </para>
/// <para>
/// The functions are the ones the table gives, without any display adjustment (no BT.1886 gamma, no system gamma).
/// The code points that share the function of Rec. ITU-R BT.709 on [0, 1] (1, 6, 11, 12, 14 and 15) share its
/// implementation, with the constants H.273 gives (the values that make the two segments meet). The logarithmic
/// functions (9 and 10) encode every value below their threshold as 0: decoding 0 gives 0, the generalized inverse.
/// The sRGB function (13) is <see cref="SrgbTransfer"/>, so that results do not depend on how sRGB is declared. PQ
/// encodes no light as <c>c1^m2</c> (about 7.3e-7), which is less than half a 16-bit step, and decodes it to 0.
/// </para>
/// <para>
/// Results depend on <see cref="Math.Pow(double, double)"/>, <see cref="Math.Log(double)"/> and
/// <see cref="Math.Exp(double)"/>, which may differ by one unit in the last place between platforms: this can only
/// change a rounded output sample whose exact value is within about 1e-9 of a rounding boundary. Decoding of stored
/// samples goes through tables built once per process and per function (256 entries for 8-bit samples, 65,536 for
/// 16-bit samples; static, shared, never charged to an image scope).
/// </para>
/// </remarks>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "Shared color infrastructure; consumed by the color conversions and the codecs and covered by unit tests.")]
internal static class CicpTransfer
{
    /// <summary>The linear transfer characteristics.</summary>
    public const byte Linear = 8;

    /// <summary>The sRGB transfer characteristics (IEC 61966-2-1).</summary>
    public const byte Srgb = 13;

    /// <summary>The perceptual quantizer of SMPTE ST 2084.</summary>
    public const byte Pq = 16;

    /// <summary>The hybrid log-gamma function of ARIB STD-B67.</summary>
    public const byte Hlg = 18;

    // H.273 table 3: the alpha and beta of the functions of Rec. ITU-R BT.709, at the precision that makes the two
    // segments of the function meet
    private const double Bt709Alpha = 1.09929682680944;
    private const double Bt709Beta = 0.018053968510807;

    // SMPTE 240M
    private const double Smpte240Alpha = 1.1115;
    private const double Smpte240Beta = 0.0228;

    // SMPTE ST 2084: m1 = 2610 / 16384, m2 = 2523 / 4096 * 128, c1 = 3424 / 4096, c2 = 2413 / 4096 * 32,
    // c3 = 2392 / 4096 * 32
    private const double PqM1 = 2610.0 / 16384;
    private const double PqM2 = 2523.0 / 4096 * 128;
    private const double PqC1 = 3424.0 / 4096;
    private const double PqC2 = 2413.0 / 4096 * 32;
    private const double PqC3 = 2392.0 / 4096 * 32;

    // Rec. ITU-R BT.2100 table 5: a = 0.17883277, b = 1 - 4a, c = 0.5 - a ln(4a)
    private const double HlgA = 0.17883277;
    private const double HlgB = 1 - (4 * HlgA);
    private static readonly double HlgC = 0.5 - (HlgA * Math.Log(4 * HlgA));

    // The code points from 0 to 18, by the function they share
    private const int CurveCount = 19;
    private static readonly double[]?[] Decode8Tables = new double[]?[CurveCount];
    private static readonly double[]?[] Decode16Tables = new double[]?[CurveCount];

    /// <summary>Returns whether the code point is a function of the table: 1 and 4 to 18. The unspecified code point (2) and the reserved ones are not.</summary>
    public static bool IsSupported(byte code) => code is 1 or (>= 4 and <= 18);

    /// <summary>Returns whether the code point is a high dynamic range function: PQ (16) or HLG (18).</summary>
    public static bool IsHdr(byte code) => code is Pq or Hlg;

    /// <summary>Converts an encoded value to a linear value.</summary>
    /// <param name="code">A supported code point (<see cref="IsSupported"/>).</param>
    /// <param name="value">The encoded value; it is clipped to [0, 1] and a value that is not a number is 0.</param>
    /// <returns>The linear value, in [0, 1] (at most 52.37 / 48 for the code point 17).</returns>
    public static double Decode(byte code, double value)
    {
        var v = !(value > 0) ? 0 : value > 1 ? 1 : value;
        switch (GetCurve(code))
        {
            case 1:
                return v < 4.5 * Bt709Beta ? v / 4.5 : Math.Pow((v + (Bt709Alpha - 1)) / Bt709Alpha, 1 / 0.45);

            case 4:
                return Math.Pow(v, 2.2);

            case 5:
                return Math.Pow(v, 2.8);

            case 7:
                return v < 4 * Smpte240Beta ? v / 4 : Math.Pow((v + (Smpte240Alpha - 1)) / Smpte240Alpha, 1 / 0.45);

            case Linear:
                return v;

            case 9:
                return v is 0 ? 0 : Math.Pow(10, (v - 1) * 2);

            case 10:
                return v is 0 ? 0 : Math.Pow(10, (v - 1) * 2.5);

            case Srgb:
                return SrgbTransfer.Decode(v);

            case Pq:
            {
                var p = Math.Pow(v, 1 / PqM2);
                return Math.Pow(Math.Max(p - PqC1, 0) / (PqC2 - (PqC3 * p)), 1 / PqM1);
            }

            case 17:
                return Math.Pow(v, 2.6) * (52.37 / 48);

            case Hlg:
                return v <= 0.5 ? v * v / 3 : (Math.Exp((v - HlgC) / HlgA) + HlgB) / 12;

            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, "The transfer characteristics are not supported.");
        }
    }

    /// <summary>Converts a linear value to an encoded value.</summary>
    /// <param name="code">A supported code point (<see cref="IsSupported"/>).</param>
    /// <param name="value">The linear value; a negative value and a value that is not a number are 0.</param>
    /// <returns>The encoded value, clipped to [0, 1].</returns>
    public static double Encode(byte code, double value)
    {
        var l = !(value > 0) ? 0 : value;
        double v;
        switch (GetCurve(code))
        {
            case 1:
                v = l < Bt709Beta ? 4.5 * l : (Bt709Alpha * Math.Pow(l, 0.45)) - (Bt709Alpha - 1);
                break;

            case 4:
                v = Math.Pow(l, 1 / 2.2);
                break;

            case 5:
                v = Math.Pow(l, 1 / 2.8);
                break;

            case 7:
                v = l < Smpte240Beta ? 4 * l : (Smpte240Alpha * Math.Pow(l, 0.45)) - (Smpte240Alpha - 1);
                break;

            case Linear:
                v = l;
                break;

            case 9:
                v = l < 0.01 ? 0 : 1 + (Math.Log10(l) / 2);
                break;

            case 10:
                v = l < Math.Sqrt(10) / 1000 ? 0 : 1 + (Math.Log10(l) / 2.5);
                break;

            case Srgb:
                v = SrgbTransfer.Encode(l);
                break;

            case Pq:
            {
                var y = Math.Pow(l, PqM1);
                v = Math.Pow((PqC1 + (PqC2 * y)) / (1 + (PqC3 * y)), PqM2);
                break;
            }

            case 17:
                v = Math.Pow(48 * l / 52.37, 1 / 2.6);
                break;

            case Hlg:
                v = l <= 1.0 / 12 ? Math.Sqrt(3 * l) : (HlgA * Math.Log((12 * l) - HlgB)) + HlgC;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, "The transfer characteristics are not supported.");
        }

        return v > 1 ? 1 : v;
    }

    /// <summary>Gets the linear value of every 8-bit sample: <c>Decode(code, v / 255)</c>.</summary>
    /// <param name="code">A supported code point (<see cref="IsSupported"/>).</param>
    public static ReadOnlySpan<double> GetDecode8(byte code)
    {
        var curve = GetCurve(code);
        if (curve is Srgb)
            return SrgbTransfer.Decode8;

        if (!IsSupported(code))
            throw new ArgumentOutOfRangeException(nameof(code), code, "The transfer characteristics are not supported.");

        return Decode8Tables[curve] ??= CreateTable(code, byte.MaxValue);
    }

    /// <summary>Gets the linear value of every 16-bit sample: <c>Decode(code, v / 65535)</c>.</summary>
    /// <param name="code">A supported code point (<see cref="IsSupported"/>).</param>
    public static ReadOnlySpan<double> GetDecode16(byte code)
    {
        var curve = GetCurve(code);
        if (curve is Srgb)
            return SrgbTransfer.Decode16;

        if (!IsSupported(code))
            throw new ArgumentOutOfRangeException(nameof(code), code, "The transfer characteristics are not supported.");

        return Decode16Tables[curve] ??= CreateTable(code, ushort.MaxValue);
    }

    // The functions of Rec. ITU-R BT.709, BT.601, IEC 61966-2-4, BT.1361 and BT.2020 are one function on [0, 1]
    private static int GetCurve(byte code) => code is 6 or 11 or 12 or 14 or 15 ? 1 : code;

    private static double[] CreateTable(byte code, int max)
    {
        var table = new double[max + 1];
        for (var i = 0; i <= max; i++)
        {
            table[i] = Decode(code, (double)i / max);
        }

        return table;
    }
}
