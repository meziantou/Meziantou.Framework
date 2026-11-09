namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding parameters of a JPEG-LS scan (ITU-T T.87 C.2.4.1.1 and A.2.1): the largest sample value, the thresholds of
/// the gradient quantization, the reset threshold of the contexts, and the values the near-lossless error bound derives
/// from them.
/// </summary>
internal sealed class JpegLsParameters
{
    private JpegLsParameters(int maxValue, int near, int t1, int t2, int t3, int reset)
    {
        MaxValue = maxValue;
        Near = near;
        T1 = t1;
        T2 = t2;
        T3 = t3;
        Reset = reset;
        Delta = (2 * near) + 1;
        Range = near == 0 ? maxValue + 1 : ((maxValue + (2 * near)) / Delta) + 1;
        var quantizedBits = 1;
        while (1 << quantizedBits < Range)
        {
            quantizedBits++;
        }

        var bits = 1;
        while (1 << bits < maxValue + 1)
        {
            bits++;
        }

        bits = Math.Max(2, bits);
        QuantizedBits = quantizedBits;
        Limit = 2 * (bits + Math.Max(8, bits));
    }

    /// <summary>Gets MAXVAL, the largest sample value.</summary>
    public int MaxValue { get; }

    /// <summary>Gets NEAR, the largest difference between a sample and its reconstruction (0: lossless).</summary>
    public int Near { get; }

    public int T1 { get; }

    public int T2 { get; }

    public int T3 { get; }

    /// <summary>Gets RESET, the number of occurrences of a context after which its statistics are halved.</summary>
    public int Reset { get; }

    /// <summary>Gets <c>2 * NEAR + 1</c>, the quantization step of the prediction errors.</summary>
    public int Delta { get; }

    /// <summary>Gets RANGE, the number of values of a quantized prediction error.</summary>
    public int Range { get; }

    /// <summary>Gets qbpp, the number of bits of a quantized prediction error.</summary>
    public int QuantizedBits { get; }

    /// <summary>Gets LIMIT, the largest length of the code of a prediction error.</summary>
    public int Limit { get; }

    /// <summary>
    /// Gets the parameters of a scan: the preset values of an LSE segment (zero for a default) completed by the defaults of
    /// T.87 C.2.4.1.1, and validated.
    /// </summary>
    /// <param name="precision">The precision of the frame, 2 to 16 bits.</param>
    /// <param name="near">The NEAR parameter of the scan.</param>
    /// <param name="preset">The values of the last LSE segment of type 1 (MAXVAL, T1, T2, T3, RESET), or zeros.</param>
    /// <exception cref="InvalidImageContentException">A value is out of its range.</exception>
    public static JpegLsParameters Create(int precision, int near, (int MaxValue, int T1, int T2, int T3, int Reset) preset)
    {
        var maxValue = preset.MaxValue == 0 ? (1 << precision) - 1 : preset.MaxValue;
        if (maxValue >= 1 << precision)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG-LS MAXVAL {maxValue} does not fit in the {precision}-bit samples of the frame."));

        if (near > Math.Min(255, maxValue / 2))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG-LS NEAR parameter {near} is greater than min(255, MAXVAL / 2) = {Math.Min(255, maxValue / 2)}."));

        int t1, t2, t3;
        if (maxValue >= 128)
        {
            var factor = (Math.Min(maxValue, 4095) + 128) >> 8;
            t1 = preset.T1 != 0 ? preset.T1 : Clamp((factor * (3 - 2)) + 2 + (3 * near), near + 1, maxValue);
            t2 = preset.T2 != 0 ? preset.T2 : Clamp((factor * (7 - 3)) + 3 + (5 * near), t1, maxValue);
            t3 = preset.T3 != 0 ? preset.T3 : Clamp((factor * (21 - 4)) + 4 + (7 * near), t2, maxValue);
        }
        else
        {
            var factor = 256 / (maxValue + 1);
            t1 = preset.T1 != 0 ? preset.T1 : Clamp(Math.Max(2, (3 / factor) + (3 * near)), near + 1, maxValue);
            t2 = preset.T2 != 0 ? preset.T2 : Clamp(Math.Max(3, (7 / factor) + (5 * near)), t1, maxValue);
            t3 = preset.T3 != 0 ? preset.T3 : Clamp(Math.Max(4, (21 / factor) + (7 * near)), t2, maxValue);
        }

        // T.87 C.2.4.1.1: NEAR + 1 <= T1 <= T2 <= T3 <= MAXVAL, and 3 <= RESET <= max(255, MAXVAL)
        if (t1 < near + 1 || t1 > maxValue || t2 < t1 || t2 > maxValue || t3 < t2 || t3 > maxValue)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG-LS thresholds T1 = {t1}, T2 = {t2}, T3 = {t3} are not ordered between NEAR + 1 = {near + 1} and MAXVAL = {maxValue}."));

        var reset = preset.Reset == 0 ? 64 : preset.Reset;
        if (reset < 3 || reset > Math.Max(255, maxValue))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG-LS RESET value {reset} is not between 3 and max(255, MAXVAL)."));

        return new JpegLsParameters(maxValue, near, t1, t2, t3, reset);
    }

    /// <summary>The CLAMP function of T.87 C.2.4.1.1.1: <paramref name="minimum"/> when the value is out of range.</summary>
    private static int Clamp(int value, int minimum, int maxValue) => value > maxValue || value < minimum ? minimum : value;

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Jpeg);
}
