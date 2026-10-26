namespace Meziantou.Framework.WebPlatform.WebAudio.Decoding;

/// <summary>
/// Converts the sample rate of a channel with a windowed-sinc band-limited interpolation: 16 zero crossings on each side, a
/// Blackman window, and a cutoff below the Nyquist frequency of the lower rate (so that downsampling does not alias).
/// </summary>
internal static class Resampler
{
    private const int ZeroCrossings = 16;
    private const int TableResolution = 512;

    // The windowed sinc from 0 to ZeroCrossings, by steps of 1 / TableResolution (with one more point for the interpolation)
    private static readonly float[] Kernel = CreateKernel();

    private static float[] CreateKernel()
    {
        var kernel = new float[(ZeroCrossings * TableResolution) + 2];
        for (var i = 0; i < kernel.Length; i++)
        {
            var x = i / (double)TableResolution;
            if (x >= ZeroCrossings)
                continue;

            var sinc = x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
            var w = x / ZeroCrossings; // From 0 (center) to 1 (edge)
            var window = 0.42 + (0.5 * Math.Cos(Math.PI * w)) + (0.08 * Math.Cos(2 * Math.PI * w));
            kernel[i] = (float)(sinc * window);
        }

        return kernel;
    }

    /// <summary>Resamples <paramref name="input"/> from <paramref name="sourceRate"/> to <paramref name="targetRate"/> into <paramref name="length"/> frames.</summary>
    public static float[] Resample(float[] input, double sourceRate, double targetRate, int length)
    {
        var output = new float[length];
        var step = sourceRate / targetRate; // The input frames per output frame

        // The cutoff relative to the Nyquist frequency of the input (lower when downsampling), with a margin for the transition band
        var cutoff = Math.Min(1.0, targetRate / sourceRate) * 0.95;
        var radius = ZeroCrossings / cutoff; // In input frames
        for (var n = 0; n < length; n++)
        {
            var center = n * step;
            var first = Math.Max(0, (int)Math.Ceiling(center - radius));
            var last = Math.Min(input.Length - 1, (int)Math.Floor(center + radius));
            double sum = 0;
            for (var k = first; k <= last; k++)
            {
                var position = Math.Abs(center - k) * cutoff * TableResolution;
                var index = (int)position;
                if (index >= Kernel.Length - 1)
                    continue;

                var fraction = position - index;
                var weight = Kernel[index] + ((Kernel[index + 1] - Kernel[index]) * fraction);
                sum += input[k] * weight;
            }

            output[n] = (float)(sum * cutoff);
        }

        return output;
    }
}
