namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// The reconstruction error of a lossy decoding against its source: PSNR over every sample, the largest
/// absolute sample error, the mean absolute error, and the per-channel mean signed error (a systematic offset, for example
/// a range or channel-order defect, shows up there even when the PSNR looks acceptable).
/// </summary>
/// <param name="Psnr">The peak signal-to-noise ratio in dB (<see cref="double.PositiveInfinity"/> for identical buffers).</param>
/// <param name="MaxError">The largest absolute sample difference.</param>
/// <param name="MeanAbsoluteError">The mean absolute sample difference.</param>
/// <param name="MeanSignedErrors">The mean of <c>actual - expected</c> per channel.</param>
public sealed record ReconstructionError(double Psnr, int MaxError, double MeanAbsoluteError, IReadOnlyList<double> MeanSignedErrors)
{
    /// <summary>Gets the largest absolute per-channel mean signed error.</summary>
    public double MaxChannelBias => MeanSignedErrors.Max(Math.Abs);

    /// <summary>Measures the error of 8-bit buffers of the same size and layout.</summary>
    /// <param name="expected">The source.</param>
    /// <param name="actual">The decoded pixels.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentException">The buffers differ in size or layout, or are not 8-bit.</exception>
    public static ReconstructionError Measure(RawPixelBuffer expected, RawPixelBuffer actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Width != actual.Width || expected.Height != actual.Height || expected.Layout != actual.Layout || expected.Layout.BytesPerSample != 1)
            throw new ArgumentException($"Cannot compare {actual.Width}x{actual.Height} {actual.Layout} with {expected.Width}x{expected.Height} {expected.Layout} (8-bit layouts of the same size are required).", nameof(actual));

        var channels = expected.Layout.ChannelCount;
        var source = expected.Span;
        var decoded = actual.Span;
        long squared = 0;
        long absolute = 0;
        var max = 0;
        var signed = new long[channels];
        for (var i = 0; i < source.Length; i++)
        {
            var difference = decoded[i] - source[i];
            squared += difference * difference;
            absolute += Math.Abs(difference);
            max = Math.Max(max, Math.Abs(difference));
            signed[i % channels] += difference;
        }

        var samples = (double)source.Length;
        var psnr = squared == 0 ? double.PositiveInfinity : 10 * Math.Log10(255d * 255d / (squared / samples));
        return new ReconstructionError(psnr, max, absolute / samples, [.. signed.Select(value => value / (samples / channels))]);
    }

    /// <summary>Describes the error.</summary>
    /// <returns>The description.</returns>
    public string Describe() => string.Create(CultureInfo.InvariantCulture, $"PSNR {Psnr:F2} dB, max {MaxError}, mean {MeanAbsoluteError:F3}, bias [{string.Join(", ", MeanSignedErrors.Select(value => value.ToString("F3", CultureInfo.InvariantCulture)))}]");
}
