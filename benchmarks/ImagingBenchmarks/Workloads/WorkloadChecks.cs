namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// Cheap correctness gates run by the benchmarks before any timed region (global setup). They compare decoded results with
/// the independent synthetic sources of <see cref="BenchmarkPixels"/>, so that a benchmark run of a broken build fails instead
/// of reporting a faster number. The thorough checks against independent reference decoders, the reference resampler and
/// the golden corpus are the workload validation tests (<c>Tests/Conformance/Benchmarks</c>).
/// </summary>
internal static class WorkloadChecks
{
    /// <summary>Requires a lossless encoding to decode to exactly the source pixels.</summary>
    public static void RequireLossless<TPixel>(byte[] encoded, TPixel[] source)
        where TPixel : unmanaged, IEquatable<TPixel>
    {
        using var image = Image.Load<TPixel>(encoded);
        var pixels = new TPixel[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        if (!pixels.AsSpan().SequenceEqual(source))
            throw new InvalidOperationException($"The decoded {typeof(TPixel).Name} pixels differ from the source.");
    }

    /// <summary>Requires a lossy RGB encoding to decode close to the source pixels (peak signal-to-noise ratio).</summary>
    public static double RequirePsnr(byte[] encoded, Rgb24[] source, double minimum)
    {
        using var image = Image.Load<Rgb24>(encoded);
        var pixels = new Rgb24[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        if (pixels.Length != source.Length)
            throw new InvalidOperationException("The decoded size differs from the source.");

        double squares = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            squares += Square(pixels[i].R - source[i].R) + Square(pixels[i].G - source[i].G) + Square(pixels[i].B - source[i].B);
        }

        var psnr = 10 * Math.Log10(255.0 * 255.0 / (squares / (pixels.Length * 3.0)));
        if (psnr < minimum)
            throw new InvalidOperationException($"The decoded pixels are too far from the source: PSNR {psnr:0.00} dB < {minimum} dB.");

        return psnr;

        static double Square(int value) => value * (double)value;
    }

    /// <summary>Requires an animation of the expected structure.</summary>
    public static void RequireAnimation(byte[] encoded, int width, int height, int frames, FrameDuration duration)
    {
        using var image = Image.Load<Rgba32>(encoded);
        if (image.Width != width || image.Height != height || image.Frames.Count != frames)
            throw new InvalidOperationException($"Unexpected animation: {image.Width}x{image.Height}, {image.Frames.Count} frames.");

        foreach (var frame in image.Frames)
        {
            if (frame.Metadata.Duration != duration)
                throw new InvalidOperationException($"Unexpected frame duration {frame.Metadata.Duration}.");
        }
    }
}
