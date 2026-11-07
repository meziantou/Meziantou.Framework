using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// The conversion of the light of PQ and HLG samples to a standard dynamic range in <see cref="decimal"/> arithmetic,
/// written from the published definitions and not from the library: the display light of Rec. ITU-R BT.2100 (PQ: a
/// fraction of 10,000 cd/m2; HLG: the opto-optical transfer function), divided by the luminance that becomes white, then
/// a curve applied to the largest component of each color.
/// </summary>
/// <param name="Operator">
/// The curve: 0 Report ITU-R BT.2390 section 5.4.1; 1 clip; 2 Reinhard et al. 2002, equation 4, with the peak as white
/// point; 3 the filmic curve of John Hable over its value at the peak; 4 the identity up to 0.3, then the Möbius
/// function with the same value and slope there that reaches 1 at the peak.
/// </param>
/// <param name="PeakLuminance">The peak of the source, in candelas per square meter.</param>
/// <param name="WhiteLuminance">The luminance that becomes white, in candelas per square meter.</param>
public sealed record ReferenceHdrToneMap(int Operator, decimal PeakLuminance, decimal WhiteLuminance)
{
    /// <summary>Converts the decoded light of one color.</summary>
    /// <param name="primaries">The color primaries code point, whose luminance weights the HLG function uses; not used for a gray sample.</param>
    /// <param name="transfer">The transfer characteristics code point: 16 (PQ) or 18 (HLG).</param>
    /// <param name="linear">What the transfer function decodes to: one gray value, or red, green and blue.</param>
    /// <returns>The light of the standard dynamic range color, in [0, 1], with as many components.</returns>
    public decimal[] Apply(int primaries, int transfer, ReadOnlySpan<decimal> linear)
    {
        var display = new decimal[linear.Length];
        if (transfer == 18)
        {
            // Rec. ITU-R BT.2100, table 5: FD = alpha YS^(gamma - 1) E, with alpha = LW for a black of no light and
            // gamma = 1.2 + 0.42 log10(LW / 1000)
            decimal luminance;
            if (linear.Length == 1 || primaries == 10)
            {
                luminance = linear.Length == 1 ? linear[0] : linear[1];
            }
            else
            {
                var matrix = ReferenceCicpColorSpace.GetRgbToXyz(primaries);
                luminance = (matrix[3] * linear[0]) + (matrix[4] * linear[1]) + (matrix[5] * linear[2]);
            }

            var gamma = 1.2m + (0.42m * DecimalMath.Ln(PeakLuminance / 1000) / DecimalMath.Ln(10));
            var gain = luminance > 0 ? PeakLuminance * DecimalMath.Pow(luminance, gamma - 1) : 0;
            for (var i = 0; i < linear.Length; i++)
            {
                display[i] = gain * linear[i] / WhiteLuminance;
            }
        }
        else
        {
            for (var i = 0; i < linear.Length; i++)
            {
                display[i] = linear[i] * 10000 / WhiteLuminance;
            }
        }

        var x = display.Max();
        if (x <= 0)
            return new decimal[linear.Length];

        var y = Math.Min(Curve(x), 1m);
        for (var i = 0; i < display.Length; i++)
        {
            display[i] = display[i] * y / x;
        }

        return display;
    }

    /// <summary>Evaluates the curve for a light of <paramref name="x"/> whites.</summary>
    /// <param name="x">The light, in units of the white.</param>
    /// <returns>The light of the standard dynamic range color; at most 1 once it is clipped by the caller.</returns>
    public decimal Curve(decimal x)
    {
        // A source that is not brighter than the white is not compressed; light above the peak is the peak
        var p = Math.Max(PeakLuminance / WhiteLuminance, 1m);
        x = Math.Min(x, p);
        switch (Operator)
        {
            case 1:
                return x;
            case 2:
                return x * (1 + (x / (p * p))) / (1 + x);
            case 3:
                return Hable(x) / Hable(p);
            case 4:
            {
                // The Möbius function (alpha x + beta) / (x + delta) with f(j) = j, f'(j) = 1 and f(p) = 1: the three
                // conditions give delta = (p + j^2 - 2 j p) / (p - 1), alpha = 2 j + delta and beta = -j^2
                const decimal J = 0.3m;
                if (x <= J || p <= 1)
                    return x;

                var delta = (p + (J * J) - (2 * J * p)) / (p - 1);
                return ((((2 * J) + delta) * x) - (J * J)) / (x + delta);
            }

            default:
            {
                // Report ITU-R BT.2390, section 5.4.1, with a source and a target whose black is no light
                var sourcePeak = ReferenceCicpTransfer.Encode(16, p * WhiteLuminance / 10000);
                var maxLum = ReferenceCicpTransfer.Encode(16, WhiteLuminance / 10000) / sourcePeak;
                if (maxLum >= 1)
                    return x;

                var knee = Math.Max((1.5m * maxLum) - 0.5m, 0m);
                var e1 = ReferenceCicpTransfer.Encode(16, x * WhiteLuminance / 10000) / sourcePeak;
                var e2 = e1;
                if (e1 > knee)
                {
                    var t = (e1 - knee) / (1 - knee);
                    e2 = (((2 * t * t * t) - (3 * t * t) + 1) * knee) + (((t * t * t) - (2 * t * t) + t) * (1 - knee)) + (((-2 * t * t * t) + (3 * t * t)) * maxLum);
                }

                return ReferenceCicpTransfer.Decode(16, e2 * sourcePeak) * 10000 / WhiteLuminance;
            }
        }
    }

    private static decimal Hable(decimal x)
    {
        const decimal A = 0.15m;
        const decimal B = 0.50m;
        const decimal C = 0.10m;
        const decimal D = 0.20m;
        const decimal E = 0.02m;
        const decimal F = 0.30m;
        return (((x * ((A * x) + (C * B))) + (D * E)) / ((x * ((A * x) + B)) + (D * F))) - (E / F);
    }
}
