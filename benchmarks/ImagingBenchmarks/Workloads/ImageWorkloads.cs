using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// The benchmarked operations, shared by the BenchmarkDotNet classes and by the workload validation tests
/// (<c>Tests/Conformance/Benchmarks</c>), which check their outputs against independent references outside any timed
/// region so that a faster but incorrect kernel or codec mode never counts as an improvement.
/// </summary>
/// <remarks>Every operation works in memory; the file-system variants are separate workloads.</remarks>
internal static class ImageWorkloads
{
    /// <summary>Encodes an image into a reused stream and returns the encoded size.</summary>
    public static long Encode(Image image, ImageEncoder encoder, MemoryStream output)
    {
        output.SetLength(0);
        image.Save(output, encoder);
        return output.Length;
    }

    /// <summary>Service-style request: decode a JPEG, resize it to fit <paramref name="size"/> (Catmull-Rom), encode a JPEG.</summary>
    public static long JpegDecodeResizeEncode(byte[] input, Size size, MemoryStream output, ImageConfiguration? configuration = null)
    {
        using var image = Image.Load<Rgb24>(input, configuration is null ? null : new ImageDecodeOptions { Configuration = configuration });
        image.Resize(new ResizeOptions(size) { Mode = ResizeMode.Contain });
        return Encode(image, BenchmarkInputs.JpegEncoder, output);
    }

    /// <summary>
    /// Eager animation edit: load every frame, remove the second frame, move the last frame first, resize to half size
    /// (Catmull-Rom, every frame), and encode with the same format.
    /// </summary>
    public static long EditAnimation(byte[] input, ImageEncoder encoder, MemoryStream output)
    {
        using var image = Image.Load<Rgba32>(input);
        image.RemoveFrame(1);
        image.MoveFrame(image.Frames.Count - 1, 0);
        image.Resize(new ResizeOptions(image.Width / 2, image.Height / 2) { Mode = ResizeMode.Stretch });
        return Encode(image, encoder, output);
    }

    /// <summary>
    /// Sequential animation transform with bounded memory: read one frame at a time, resize it to half size, and write it to
    /// a streaming writer of the same format. Each decoded frame is disposed before the next one is read.
    /// </summary>
    /// <returns>The number of frames written.</returns>
    public static int TransformAnimationSequentially(byte[] input, ImageEncoder encoder, MemoryStream output)
    {
        output.SetLength(0);
        using var source = new MemoryStream(input, writable: false);
        using var reader = Image.OpenReader<Rgba32>(source, new ImageReaderOptions { LeaveOpen = true });
        var info = reader.Info;
        var size = new Size(info.Width / 2, info.Height / 2);
        using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
        {
            Encoder = encoder,
            Animation = info.Animation,
            ExpectedFrameCount = info.FrameCount,
            LeaveOpen = true,
        });

        var resize = new ResizeOptions(size) { Mode = ResizeMode.Stretch };
        while (reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                frame.Resize(resize);
                writer.WriteFrame(frame.Frames[0]);
            }
        }

        writer.Complete();
        return writer.FramesWritten;
    }

    /// <summary>Typed row loop with a static callback (no closure): inverts the color channels of every pixel.</summary>
    public static void InvertRows(ImageFrame<Rgba32> frame)
    {
        frame.ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (ref var pixel in pixels.GetRowSpan(y))
                {
                    pixel = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
                }
            }
        });
    }

    /// <summary>Typed row loop with explicit (reused) state and a static callback: sums the green channel.</summary>
    public static long SumGreen(ImageFrame<Rgba32> frame, SumState state)
    {
        state.Value = 0;
        frame.ProcessPixelRows(state, static (pixels, sum) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    sum.Value += pixel.G;
                }
            }
        });

        return state.Value;
    }

    /// <summary>Copies a source image and resizes the copy (the source stays unchanged so the workload can be repeated).</summary>
    public static Image ResizeCopy(Image source, ResizeOptions options)
    {
        var copy = source.Clone();
        try
        {
            copy.Resize(options);
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>Copies a source image and convolves the copy (the source stays unchanged so the workload can be repeated).</summary>
    public static Image ConvolveCopy(Image source, ConvolutionOptions options)
    {
        var copy = source.Clone();
        try
        {
            copy.Convolve(options);
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>Mutable state of <see cref="SumGreen"/>.</summary>
    internal sealed class SumState
    {
        public long Value { get; set; }
    }
}
