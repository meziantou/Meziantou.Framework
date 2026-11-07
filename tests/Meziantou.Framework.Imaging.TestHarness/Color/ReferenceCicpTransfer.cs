using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// The transfer characteristics of Rec. ITU-T H.273 (table 3) in <see cref="decimal"/> arithmetic: the function that the
/// table gives for each code point, from a linear value to an encoded value, and its inverse. The powers, the logarithms
/// and the exponentials are the series of <see cref="DecimalMath"/>, with about 27 significant digits: the reference of
/// the conversions that go through a transfer function, independent of the floating-point functions of the platform.
/// </summary>
/// <remarks>
/// A linear value of 1 is the nominal peak of the code point: 10,000 cd/m2 for PQ (16, SMPTE ST 2084), the peak of the
/// scene for HLG (18, the OETF of Rec. ITU-R BT.2100 without the OOTF of a display). The logarithmic functions (9, 10)
/// encode every value below their threshold as 0, which decodes to 0. Values are clipped to [0, 1] on input of
/// <see cref="Decode"/> and on output of <see cref="Encode"/>, as the library documents.
/// </remarks>
public static class ReferenceCicpTransfer
{
    // H.273 table 3: the constants of the functions of Rec. ITU-R BT.709, at the precision that makes the two segments meet
    private const decimal Bt709Alpha = 1.09929682680944m;
    private const decimal Bt709Beta = 0.018053968510807m;

    // SMPTE ST 2084
    private const decimal PqM1 = 2610m / 16384;
    private const decimal PqM2 = 2523m / 4096 * 128;
    private const decimal PqC1 = 3424m / 4096;
    private const decimal PqC2 = 2413m / 4096 * 32;
    private const decimal PqC3 = 2392m / 4096 * 32;

    // Rec. ITU-R BT.2100, table 5: a, b = 1 - 4a and c = 0.5 - a ln(4a)
    private const decimal HlgA = 0.17883277m;
    private const decimal HlgB = 1 - (4 * HlgA);
    private static readonly decimal HlgC = 0.5m - (HlgA * DecimalMath.Ln(4 * HlgA));

    /// <summary>Returns whether a code point is a function of the table: 1 and 4 to 18.</summary>
    /// <param name="code">The transfer characteristics code point.</param>
    /// <returns><see langword="false"/> for the unspecified code point (2) and the reserved ones.</returns>
    public static bool IsSupported(int code) => code is 1 or (>= 4 and <= 18);

    /// <summary>Converts a linear value to an encoded value.</summary>
    /// <param name="code">A supported code point.</param>
    /// <param name="linear">The linear value; a negative value is 0.</param>
    /// <returns>The encoded value, at most 1.</returns>
    /// <exception cref="NotSupportedException">The code point has no function.</exception>
    public static decimal Encode(int code, decimal linear)
    {
        // (a zero of System.Decimal may be negative, which is not a base of a power)
        var l = linear > 0 ? linear : 0m;
        var encoded = code switch
        {
            1 or 6 or 11 or 12 or 14 or 15 => l < Bt709Beta ? 4.5m * l : (Bt709Alpha * DecimalMath.Pow(l, 0.45m)) - (Bt709Alpha - 1),
            4 => DecimalMath.Pow(l, 1 / 2.2m),
            5 => DecimalMath.Pow(l, 1 / 2.8m),
            7 => l < 0.0228m ? 4 * l : (1.1115m * DecimalMath.Pow(l, 0.45m)) - 0.1115m,
            8 => l,
            9 => l < 0.01m ? 0 : 1 + (DecimalMath.Ln(l) / DecimalMath.Ln(10) / 2),
            10 => l * l * 1_000_000 < 10 ? 0 : 1 + (DecimalMath.Ln(l) / DecimalMath.Ln(10) / 2.5m),
            13 => l <= 0.0031308m ? 12.92m * l : (1.055m * DecimalMath.Pow(l, 1 / 2.4m)) - 0.055m,
            16 => EncodePq(l),
            17 => DecimalMath.Pow(48 * l / 52.37m, 1 / 2.6m),
            18 => l <= 1m / 12 ? DecimalMath.Pow(3 * l, 0.5m) : (HlgA * DecimalMath.Ln((12 * l) - HlgB)) + HlgC,
            _ => throw new NotSupportedException($"The transfer characteristics {code.ToString(CultureInfo.InvariantCulture)} have no function."),
        };

        return Math.Min(encoded, 1m);
    }

    /// <summary>Converts an encoded value to a linear value.</summary>
    /// <param name="code">A supported code point.</param>
    /// <param name="encoded">The encoded value, which is clipped to [0, 1].</param>
    /// <returns>The linear value.</returns>
    /// <exception cref="NotSupportedException">The code point has no function.</exception>
    public static decimal Decode(int code, decimal encoded)
    {
        var v = encoded > 0 ? Math.Min(encoded, 1m) : 0m;
        return code switch
        {
            1 or 6 or 11 or 12 or 14 or 15 => v < 4.5m * Bt709Beta ? v / 4.5m : DecimalMath.Pow((v + (Bt709Alpha - 1)) / Bt709Alpha, 1 / 0.45m),
            4 => DecimalMath.Pow(v, 2.2m),
            5 => DecimalMath.Pow(v, 2.8m),
            7 => v < 4 * 0.0228m ? v / 4 : DecimalMath.Pow((v + 0.1115m) / 1.1115m, 1 / 0.45m),
            8 => v,
            9 => v == 0 ? 0 : DecimalMath.Exp((v - 1) * 2 * DecimalMath.Ln(10)),
            10 => v == 0 ? 0 : DecimalMath.Exp((v - 1) * 2.5m * DecimalMath.Ln(10)),
            13 => v <= 0.04045m ? v / 12.92m : DecimalMath.Pow((v + 0.055m) / 1.055m, 2.4m),
            16 => DecodePq(v),
            17 => DecimalMath.Pow(v, 2.6m) * 52.37m / 48,
            18 => v <= 0.5m ? v * v / 3 : (DecimalMath.Exp((v - HlgC) / HlgA) + HlgB) / 12,
            _ => throw new NotSupportedException($"The transfer characteristics {code.ToString(CultureInfo.InvariantCulture)} have no function."),
        };
    }

    private static decimal EncodePq(decimal linear)
    {
        var y = DecimalMath.Pow(linear, PqM1);
        return DecimalMath.Pow((PqC1 + (PqC2 * y)) / (1 + (PqC3 * y)), PqM2);
    }

    private static decimal DecodePq(decimal encoded)
    {
        var p = DecimalMath.Pow(encoded, 1 / PqM2);
        return DecimalMath.Pow(p > PqC1 ? (p - PqC1) / (PqC2 - (PqC3 * p)) : 0m, 1 / PqM1);
    }
}
