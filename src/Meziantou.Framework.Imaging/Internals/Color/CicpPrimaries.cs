namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color primaries of Rec. ITU-T H.273 (ISO/IEC 23091-2), table 2: the chromaticities of the red, green and blue
/// primaries and of the white point of each code point.
/// </summary>
/// <remarks>
/// The table gives decimal numbers: they are kept as integers over <see cref="GetDenominator"/>, so that what is derived
/// from them (the luma coefficients of the chromaticity-derived matrix coefficients, for example) can be computed
/// exactly. The unspecified code point (2) and the reserved ones have no chromaticities: the caller decides what an
/// undescribed image is.
/// </remarks>
internal static class CicpPrimaries
{
    /// <summary>The primaries of Rec. ITU-R BT.709 and of sRGB.</summary>
    public const byte Bt709 = 1;

    /// <summary>The primaries of Rec. ITU-R BT.2020 and BT.2100.</summary>
    public const byte Bt2020 = 9;

    // The x and y of red, green and blue, then of the white point, over 100,000
    private static ReadOnlySpan<int> Bt709Values => [64000, 33000, 30000, 60000, 15000, 6000, 31270, 32900];
    private static ReadOnlySpan<int> Bt470MValues => [67000, 33000, 21000, 71000, 14000, 8000, 31000, 31600];
    private static ReadOnlySpan<int> Bt470BgValues => [64000, 33000, 29000, 60000, 15000, 6000, 31270, 32900];
    private static ReadOnlySpan<int> Bt601Values => [63000, 34000, 31000, 59500, 15500, 7000, 31270, 32900];
    private static ReadOnlySpan<int> FilmValues => [68100, 31900, 24300, 69200, 14500, 4900, 31000, 31600];
    private static ReadOnlySpan<int> Bt2020Values => [70800, 29200, 17000, 79700, 13100, 4600, 31270, 32900];
    private static ReadOnlySpan<int> DciP3Values => [68000, 32000, 26500, 69000, 15000, 6000, 31400, 35100];
    private static ReadOnlySpan<int> DisplayP3Values => [68000, 32000, 26500, 69000, 15000, 6000, 31270, 32900];
    private static ReadOnlySpan<int> Ebu3213Values => [63000, 34000, 29500, 60500, 15500, 7700, 31270, 32900];

    // SMPTE ST 428-1: the primaries are the X, Y and Z of CIE 1931, and the white point is the equal-energy point
    // (1/3, 1/3), over 3
    private static ReadOnlySpan<int> XyzValues => [3, 0, 0, 3, 0, 0, 1, 1];

    /// <summary>Returns whether the code point has chromaticities: 1, 4 to 12 and 22.</summary>
    public static bool IsSupported(byte code) => code is 1 or (>= 4 and <= 12) or 22;

    /// <summary>Gets the denominator of the chromaticities of a code point.</summary>
    public static int GetDenominator(byte code) => code is 10 ? 3 : 100_000;

    /// <summary>Gets the chromaticities of a code point as integers over <see cref="GetDenominator"/>.</summary>
    /// <param name="code">The color primaries code point.</param>
    /// <param name="chromaticities">Receives the x and y of red, green and blue, then of the white point (eight values).</param>
    /// <returns><see langword="false"/> when the code point has no chromaticities.</returns>
    public static bool TryGetChromaticities(byte code, Span<int> chromaticities)
    {
        ReadOnlySpan<int> values = code switch
        {
            Bt709 => Bt709Values,
            4 => Bt470MValues,
            5 => Bt470BgValues,
            6 or 7 => Bt601Values,
            8 => FilmValues,
            Bt2020 => Bt2020Values,
            10 => XyzValues,
            11 => DciP3Values,
            12 => DisplayP3Values,
            22 => Ebu3213Values,
            _ => default,
        };

        if (values.IsEmpty)
            return false;

        values.CopyTo(chromaticities);
        return true;
    }

    /// <summary>Gets the chromaticities of a code point.</summary>
    /// <param name="code">The color primaries code point.</param>
    /// <param name="primaries">Receives the x and y of red, green and blue (six values).</param>
    /// <param name="whiteX">Receives the x of the white point.</param>
    /// <param name="whiteY">Receives the y of the white point.</param>
    /// <returns><see langword="false"/> when the code point has no chromaticities.</returns>
    public static bool TryGet(byte code, Span<double> primaries, out double whiteX, out double whiteY)
    {
        Span<int> values = stackalloc int[8];
        if (!TryGetChromaticities(code, values))
        {
            whiteX = 0;
            whiteY = 0;
            return false;
        }

        double denominator = GetDenominator(code);
        for (var i = 0; i < 6; i++)
        {
            primaries[i] = values[i] / denominator;
        }

        whiteX = values[6] / denominator;
        whiteY = values[7] / denominator;
        return true;
    }

    /// <summary>
    /// Gets the contribution of each primary to the luminance: the Y row of the matrix from linear samples to CIE XYZ,
    /// whose three values sum to 1.
    /// </summary>
    /// <param name="code">The color primaries code point.</param>
    /// <param name="coefficients">Receives the coefficients of red, green and blue.</param>
    /// <returns><see langword="false"/> when the code point has no chromaticities.</returns>
    public static bool TryGetLuminanceCoefficients(byte code, Span<double> coefficients)
    {
        Span<double> primaries = stackalloc double[6];
        Span<double> matrix = stackalloc double[9];
        if (!TryGet(code, primaries, out var whiteX, out var whiteY) || !ChromaticityMath.TryGetRgbToXyz(primaries, whiteX, whiteY, matrix))
            return false;

        matrix.Slice(3, 3).CopyTo(coefficients);
        return true;
    }
}
