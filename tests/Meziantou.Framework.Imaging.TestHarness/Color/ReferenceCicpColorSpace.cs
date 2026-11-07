namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// The color space of a CICP color description (Rec. ITU-T H.273) in <see cref="decimal"/> arithmetic: the chromaticities
/// of the color primaries (table 2), transcribed here, and the conversion of normalized samples to CIEXYZ of the ICC
/// profile connection space: the function of the transfer characteristics (<see cref="ReferenceCicpTransfer"/>), the
/// matrix of the primaries for a white of luminance 1, and the linear Bradford adaptation of that white to D50
/// (ICC.1:2022, annex E).
/// </summary>
public static class ReferenceCicpColorSpace
{
    // ICC.1:2022, annex E.3: the cone response matrix of the linear Bradford transform
    private static readonly decimal[] Bradford =
    [
        0.8951m, 0.2664m, -0.1614m,
        -0.7502m, 1.7135m, 0.0367m,
        0.0389m, -0.0685m, 1.0296m,
    ];

    /// <summary>Gets the chromaticities of color primaries: the x and y of red, green, blue and of the white point.</summary>
    /// <param name="primaries">The color primaries code point.</param>
    /// <returns>Eight values, or <see langword="null"/> for a code point without chromaticities.</returns>
    public static decimal[]? GetChromaticities(int primaries) => primaries switch
    {
        1 => [0.640m, 0.330m, 0.300m, 0.600m, 0.150m, 0.060m, 0.3127m, 0.3290m],
        4 => [0.67m, 0.33m, 0.21m, 0.71m, 0.14m, 0.08m, 0.310m, 0.316m],
        5 => [0.64m, 0.33m, 0.29m, 0.60m, 0.15m, 0.06m, 0.3127m, 0.3290m],
        6 or 7 => [0.630m, 0.340m, 0.310m, 0.595m, 0.155m, 0.070m, 0.3127m, 0.3290m],
        8 => [0.681m, 0.319m, 0.243m, 0.692m, 0.145m, 0.049m, 0.310m, 0.316m],
        9 => [0.708m, 0.292m, 0.170m, 0.797m, 0.131m, 0.046m, 0.3127m, 0.3290m],
        10 => [1m, 0m, 0m, 1m, 0m, 0m, 1m / 3, 1m / 3],
        11 => [0.680m, 0.320m, 0.265m, 0.690m, 0.150m, 0.060m, 0.314m, 0.351m],
        12 => [0.680m, 0.320m, 0.265m, 0.690m, 0.150m, 0.060m, 0.3127m, 0.3290m],
        22 => [0.630m, 0.340m, 0.295m, 0.605m, 0.155m, 0.077m, 0.3127m, 0.3290m],
        _ => null,
    };

    /// <summary>Computes the matrix from linear red, green and blue to CIEXYZ relative to the white of the primaries, whose luminance is 1.</summary>
    /// <param name="primaries">The color primaries code point.</param>
    /// <returns>Nine values, row by row.</returns>
    /// <exception cref="NotSupportedException">The code point has no chromaticities.</exception>
    public static decimal[] GetRgbToXyz(int primaries)
    {
        var c = GetChromaticities(primaries) ?? throw new NotSupportedException($"The color primaries {primaries.ToString(CultureInfo.InvariantCulture)} have no chromaticities.");
        if (primaries == 10)
        {
            // The primaries are X, Y and Z
            return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        }

        // The columns are the (x, y, z) of the primaries, each scaled so that equal samples give the white
        decimal[] white = [c[6] / c[7], 1m, (1 - c[6] - c[7]) / c[7]];
        decimal[] columns =
        [
            c[0], c[2], c[4],
            c[1], c[3], c[5],
            1 - c[0] - c[1], 1 - c[2] - c[3], 1 - c[4] - c[5],
        ];
        var scales = ReferenceIccMath.Multiply(ReferenceIccMath.Invert(columns), white);
        var rgbToXyz = new decimal[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                rgbToXyz[(3 * row) + column] = columns[(3 * row) + column] * scales[column];
            }
        }

        return rgbToXyz;
    }

    /// <summary>Computes the matrix from linear red, green and blue to CIEXYZ adapted to D50.</summary>
    /// <param name="primaries">The color primaries code point.</param>
    /// <returns>Nine values, row by row.</returns>
    /// <exception cref="NotSupportedException">The code point has no chromaticities.</exception>
    public static decimal[] GetToConnectionSpace(int primaries)
    {
        var rgbToXyz = GetRgbToXyz(primaries);
        var c = GetChromaticities(primaries)!;
        decimal[] white = [c[6] / c[7], 1m, (1 - c[6] - c[7]) / c[7]];

        // Linear Bradford: the cone responses are scaled by the ratio of the responses of the two whites
        var sourceCone = ReferenceIccMath.Multiply(Bradford, white);
        var destinationCone = ReferenceIccMath.Multiply(Bradford, ReferenceIccMath.D50);
        var scaled = new decimal[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                scaled[(3 * row) + column] = Bradford[(3 * row) + column] * destinationCone[row] / sourceCone[row];
            }
        }

        return MultiplyMatrices(MultiplyMatrices(ReferenceIccMath.Invert(Bradford), scaled), rgbToXyz);
    }

    /// <summary>Converts normalized samples to CIEXYZ of the profile connection space.</summary>
    /// <param name="primaries">The color primaries code point; not used for a gray sample.</param>
    /// <param name="transfer">The transfer characteristics code point.</param>
    /// <param name="samples">One gray sample, or the red, green and blue samples.</param>
    /// <param name="toneMap">The conversion of the light of PQ and HLG samples to a standard dynamic range, or <see langword="null"/>.</param>
    /// <returns>The X, Y and Z of the color.</returns>
    public static decimal[] ToConnectionSpace(int primaries, int transfer, ReadOnlySpan<decimal> samples, ReferenceHdrToneMap? toneMap = null)
    {
        var linear = new decimal[samples.Length];
        for (var i = 0; i < linear.Length; i++)
        {
            linear[i] = ReferenceCicpTransfer.Decode(transfer, samples[i]);
        }

        if (toneMap is not null)
            linear = toneMap.Apply(primaries, transfer, linear);

        // A gray sample is a fraction of the white, which is the white of the connection space once adapted
        return linear.Length == 1
            ? [linear[0] * ReferenceIccMath.D50[0], linear[0] * ReferenceIccMath.D50[1], linear[0] * ReferenceIccMath.D50[2]]
            : ReferenceIccMath.Multiply(GetToConnectionSpace(primaries), linear);
    }

    private static decimal[] MultiplyMatrices(decimal[] left, decimal[] right)
    {
        var result = new decimal[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                result[(3 * row) + column] = (left[3 * row] * right[column]) + (left[(3 * row) + 1] * right[3 + column]) + (left[(3 * row) + 2] * right[6 + column]);
            }
        }

        return result;
    }
}
