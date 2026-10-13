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
/// </remarks>
internal static class Jpeg2000QuantizationDesign
{
    /// <summary>The largest number of decomposition levels the encoder uses.</summary>
    public const int MaxLevels = 5;

    private const int ImpulseLength = 64;

    // The norm of the waveform of a low-pass coefficient and of a high-pass coefficient of each decomposition level, in one dimension
    private static readonly double[] LowNorms = MeasureNorms(isHigh: false);
    private static readonly double[] HighNorms = MeasureNorms(isHigh: true);

    /// <summary>Gets the base step of a quality, in levels of a sample of <paramref name="precision"/> bits.</summary>
    /// <param name="quality">The quality, 1 to 100.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static double GetBaseStep(int quality, int precision)
    {
        // The scale of the JPEG encoder of the library: 100 at the quality 50, 1 at the quality 100
        var scale = Math.Max(1, quality < 50 ? 5000 / quality : 200 - (2 * quality));
        return Math.ScaleB(scale / 16.0, precision - 8);
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

    private static double[] MeasureNorms(bool isHigh)
    {
        var norms = new double[MaxLevels + 1];
        norms[0] = 1;
        var signal = new float[ImpulseLength << MaxLevels];
        var scratch = new float[ImpulseLength << MaxLevels];
        for (var level = 1; level <= MaxLevels; level++)
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

                Jpeg2000Dwt.LiftIrreversible(low, parity: 0);
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

        return norms;
    }
}
