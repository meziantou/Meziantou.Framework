namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// How the lossy encoder chooses the quantization step size of each sub-band (T.800, Annex E) from the quality setting.
/// </summary>
/// <remarks>
/// <para>
/// The error that the quantization of a coefficient leaves in the image is the error of the coefficient multiplied by
/// the norm of the synthesis waveform of its sub-band. The step size of a sub-band is therefore a base step, which only
/// depends on the quality, divided by that norm: every sub-band then contributes the same error.
/// </para>
/// <para>
/// The norms are not transcribed from a table: they are measured once on the inverse transformation of the decoder
/// (<see cref="Jpeg2000Dwt.LiftIrreversible"/>), by rebuilding the signal of a single coefficient.
/// </para>
/// <para>
/// The base step is calibrated on the JPEG encoder of the library, whose quality scales its quantization tables by
/// <c>s</c> (100 at the quality 50, 1 at the quality 100). The base step of the luminance that gives the structural
/// similarity (SSIM) of the JPEG file of the same quality was measured on photographs, for the qualities 20 to 95: it
/// follows <c>0.47 * s^(3/4)</c> within 6 %. The error of a quantizer with a dead zone grows more slowly than its step,
/// more so for the tables of JPEG than for the sub-bands: hence a power below one. The chrominances of a JPEG file are
/// sub-sampled and have coarser tables: the same similarity is reached with a step 2 to 2.5 times the one of the
/// luminance, and the factor 2 keeps them at least as good at every quality.
/// </para>
/// </remarks>
internal static class Jpeg2000QuantizationDesign
{
    /// <summary>The largest number of decomposition levels of a codestream (T.800, Table A.15).</summary>
    public const int MaxLevels = 32;

    // The levels whose norms are measured; the norms of the levels above grow by the ratio of the last two measured ones,
    // which no longer changes
    private const int MeasuredLevels = 10;

    /// <summary>The base step of the luminance, in levels of an 8-bit sample, when the scale of the quality is 1 (the quality 100).</summary>
    public const double LuminanceFactor = 0.47;

    /// <summary>How many times coarser than the luminance the two chrominances are quantized.</summary>
    public const double ChrominanceRatio = 2;

    private const int ImpulseLength = 64;

    // The norm of the waveform of a low-pass coefficient and of a high-pass coefficient of each decomposition level, in one dimension
    private static readonly double[] LowNorms = MeasureNorms(isHigh: false, isReversible: false);
    private static readonly double[] HighNorms = MeasureNorms(isHigh: true, isReversible: false);
    private static readonly double[] ReversibleLowNorms = MeasureNorms(isHigh: false, isReversible: true);
    private static readonly double[] ReversibleHighNorms = MeasureNorms(isHigh: true, isReversible: true);

    /// <summary>Gets the base step of a quality, in levels of a sample of <paramref name="precision"/> bits.</summary>
    /// <param name="quality">The quality, 1 to 100.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    /// <param name="isChrominance">Whether the step is the one of the two chrominances of a color image.</param>
    public static double GetBaseStep(int quality, int precision, bool isChrominance)
    {
        // The scale of the JPEG encoder of the library: 100 at the quality 50, 1 at the quality 100
        var scale = Math.Max(1, quality < 50 ? 5000 / quality : 200 - (2 * quality));
        var step = LuminanceFactor * GetPower(scale);
        if (isChrominance)
        {
            step *= ChrominanceRatio;
        }

        return Math.ScaleB(step, precision - 8);
    }

    /// <summary>Gets the scale of a quality raised to the power 3/4, from square roots only: the result is the same on every platform.</summary>
    private static double GetPower(int scale)
    {
        var root = Math.Sqrt(scale);
        return root * Math.Sqrt(root);
    }

    /// <summary>Gets the norm of the synthesis waveform of a sub-band.</summary>
    /// <param name="level">The decomposition level of the sub-band, from 1; 0 for the samples themselves.</param>
    /// <param name="isHighHorizontally">Whether the sub-band is high-pass horizontally.</param>
    /// <param name="isHighVertically">Whether the sub-band is high-pass vertically.</param>
    public static double GetNorm(int level, bool isHighHorizontally, bool isHighVertically)
    {
        if (level == 0)
            return 1;

        return (isHighHorizontally ? HighNorms[level] : LowNorms[level]) * (isHighVertically ? HighNorms[level] : LowNorms[level]);
    }

    /// <summary>Gets the norm of the synthesis waveform of a sub-band of the reversible transformation, without its roundings.</summary>
    /// <param name="level">The decomposition level of the sub-band, from 1; 0 for the samples themselves.</param>
    /// <param name="isHighHorizontally">Whether the sub-band is high-pass horizontally.</param>
    /// <param name="isHighVertically">Whether the sub-band is high-pass vertically.</param>
    public static double GetReversibleNorm(int level, bool isHighHorizontally, bool isHighVertically)
    {
        if (level == 0)
            return 1;

        return (isHighHorizontally ? ReversibleHighNorms[level] : ReversibleLowNorms[level]) * (isHighVertically ? ReversibleHighNorms[level] : ReversibleLowNorms[level]);
    }

    /// <summary>Gets the exponent and the mantissa that describe the step size nearest below a wanted one (equation E-3).</summary>
    /// <param name="step">The wanted step size, in levels of a sample.</param>
    /// <param name="range">The nominal dynamic range of the sub-band (Rb): the depth of the samples plus the gain of the sub-band.</param>
    /// <param name="exponent">Receives the exponent, 0 to 31.</param>
    /// <param name="mantissa">Receives the mantissa, 0 to 2047.</param>
    public static void GetStepParameters(double step, int range, out int exponent, out int mantissa)
    {
        // The step size is 2^(range - exponent) * (1 + mantissa / 2^11): the exponent bounds what can be described
        var power = Math.Clamp(Math.ILogB(step), range - 31, range);
        exponent = range - power;
        var fraction = Math.ScaleB(step, -power) - 1;
        mantissa = Math.Clamp((int)(fraction * 2048), 0, 2047);
    }

    /// <summary>Gets the step size an exponent and a mantissa describe (equation E-3).</summary>
    public static float GetStep(int exponent, int mantissa, int range) => (float)Math.ScaleB(1 + (mantissa / 2048.0), range - exponent);

    private static double[] MeasureNorms(bool isHigh, bool isReversible)
    {
        var norms = new double[MaxLevels + 1];
        norms[0] = 1;
        var signal = new float[ImpulseLength << MeasuredLevels];
        var scratch = new float[ImpulseLength << MeasuredLevels];
        for (var level = 1; level <= MeasuredLevels; level++)
        {
            // One coefficient of the sub-band, far from the ends of the signal, and nothing else
            var length = ImpulseLength;
            Array.Clear(signal);
            signal[length / 2] = 1;
            for (var step = level; step >= 1; step--)
            {
                // Only the first step rebuilds from the high-pass coefficients: the others see the result as low-pass coefficients
                var low = scratch.AsSpan(0, 2 * length);
                low.Clear();
                var parity = isHigh && step == level ? 1 : 0;
                for (var i = 0; i < length; i++)
                {
                    low[(2 * i) + parity] = signal[i];
                }

                if (isReversible)
                {
                    LiftReversibleWithoutRounding(low);
                }
                else
                {
                    Jpeg2000Dwt.LiftIrreversible(low, parity: 0);
                }

                length *= 2;
                low.CopyTo(signal);
            }

            var sum = 0.0;
            for (var i = 0; i < length; i++)
            {
                sum += (double)signal[i] * signal[i];
            }

            norms[level] = Math.Sqrt(sum);
        }

        for (var level = MeasuredLevels + 1; level <= MaxLevels; level++)
        {
            norms[level] = norms[level - 1] * norms[MeasuredLevels] / norms[MeasuredLevels - 1];
        }

        return norms;
    }

    /// <summary>The synthesis of the reversible filter (F.3.8.1) on an interleaved signal that starts with a low-pass sample, without its roundings.</summary>
    private static void LiftReversibleWithoutRounding(Span<float> signal)
    {
        var length = signal.Length;
        for (var i = 0; i < length; i += 2)
        {
            signal[i] -= (Mirror(signal, i - 1) + Mirror(signal, i + 1)) / 4;
        }

        for (var i = 1; i < length; i += 2)
        {
            signal[i] += (Mirror(signal, i - 1) + Mirror(signal, i + 1)) / 2;
        }

        static float Mirror(Span<float> signal, int index)
        {
            if (index < 0)
                return signal[-index];

            return index >= signal.Length ? signal[(2 * (signal.Length - 1)) - index] : signal[index];
        }
    }
}
