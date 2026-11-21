using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    private static readonly ConvolutionEdgeMode[] ThresholdEdgeModes = [ConvolutionEdgeMode.Mirror, ConvolutionEdgeMode.Clamp, ConvolutionEdgeMode.Wrap, ConvolutionEdgeMode.Zero];

    /// <summary>
    /// <see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/> with every method: a
    /// byte of method, a byte of window size (3 to 17), a byte of edge mode, a byte of the parameter of the method (the
    /// level, the offset, <c>k</c> or <c>t</c>), a byte of pixel format (gray 8 and 16 bits, gray with alpha, RGBA), a byte of
    /// width (1 to 24) and one of height (1 to 12), then the bytes of the pixels, repeated when the input is shorter. Every
    /// pixel becomes black or white and the result is opaque. On gray pixels, every mask is the one of a brute-force
    /// evaluation of the documented formula of the method, with the same floating-point operations (the library slides
    /// its windows); RGBA pixels give the mask of their grayscale copy.
    /// </summary>
    private static void ThresholdTarget(ReadOnlySpan<byte> data)
    {
        var method = (ThresholdMethod)(FuzzInput.ReadByte(ref data) % 5);
        var windowSize = 3 + (2 * (FuzzInput.ReadByte(ref data) % 8));
        var edgeMode = FuzzInput.Pick(ThresholdEdgeModes, FuzzInput.ReadByte(ref data));
        var parameter = FuzzInput.ReadByte(ref data);
        var format = FuzzInput.ReadByte(ref data) % 4;
        var width = 1 + (FuzzInput.ReadByte(ref data) % 24);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 12);
        var options = new ThresholdOptions
        {
            Method = method,
            WindowSize = windowSize,
            EdgeMode = edgeMode,
            Level = parameter / 255.0,
            Offset = (parameter - 128) / 255.0,
            SauvolaK = parameter / 255.0,
            BradleyT = parameter / 255.0,
        };

        using var image = format switch
        {
            0 => (Image)Create<Gray8>(data, width, height),
            1 => Create<Gray16>(data, width, height),
            2 => Create<La16>(data, width, height),
            _ => Create<Rgba32>(data, width, height),
        };

        if (image is Image<Rgba32> rgba)
        {
            using var gray = rgba.CloneAs<Rgba32>();
            gray.Grayscale();
            rgba.Threshold(options);
            gray.Threshold(options);
            FuzzAssert.Equal(Snapshot(gray), Snapshot(rgba), "A color image and its grayscale copy have different masks");
            CheckBilevel(rgba);
            return;
        }

        var max = format == 1 ? 65535 : 255;
        var luma = ReadLuma(image, format);
        var expected = method switch
        {
            ThresholdMethod.Otsu => GlobalMask(luma, image.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold()),
            ThresholdMethod.Fixed => GlobalMask(luma, options.Level * max),
            _ => AdaptiveMask(luma, width, height, options, max),
        };

        image.Threshold(options);
        var actual = ReadLuma(image, format);
        for (var i = 0; i < actual.Length; i++)
        {
            FuzzAssert.True(actual[i] == 0 || actual[i] == max, $"The pixel {i} is neither black nor white: {actual[i]}");
            FuzzAssert.Equal(expected[i], actual[i] == max, $"The pixel ({i % width}, {i / width}) of luma {luma[i]} is in the wrong class ({method}, window {windowSize}, {edgeMode}, parameter {parameter})");
        }

        CheckBilevel(image);
    }

    /// <summary>The default colors are opaque black and white: the result must be opaque.</summary>
    private static void CheckBilevel(Image image)
    {
        FuzzAssert.True(image.IsOpaque(), "A binarized image is not opaque");
    }

    private static int[] ReadLuma(Image image, int format)
    {
        var frame = image.Frames[0];
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        var luma = new int[frame.Width * frame.Height];
        for (var i = 0; i < luma.Length; i++)
        {
            luma[i] = format switch
            {
                0 => bytes[i],
                1 => BitConverter.ToUInt16(bytes.AsSpan(2 * i)),
                _ => bytes[2 * i],
            };
        }

        return luma;
    }

    private static bool[] GlobalMask(int[] luma, double level)
    {
        var mask = new bool[luma.Length];
        for (var i = 0; i < luma.Length; i++)
        {
            mask[i] = luma[i] > level;
        }

        return mask;
    }

    /// <summary>
    /// The masks of the adaptive methods, from the sums of every window: <c>mean * scale - offset</c> for Mean (scale 1) and
    /// Bradley (offset 0), and <c>mean * (1 + k * (s / R - 1))</c> for Sauvola, with the variance computed exactly.
    /// </summary>
    private static bool[] AdaptiveMask(int[] luma, int width, int height, ThresholdOptions options, int max)
    {
        var radius = options.WindowSize / 2;
        var area = (long)options.WindowSize * options.WindowSize;
        var mask = new bool[luma.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                long sum = 0;
                long squares = 0;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var sy = MapWindowIndex(y + dy, height, options.EdgeMode);
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = MapWindowIndex(x + dx, width, options.EdgeMode);
                        if (sx >= 0 && sy >= 0)
                        {
                            long value = luma[(sy * width) + sx];
                            sum += value;
                            squares += value * value;
                        }
                    }
                }

                var mean = (double)sum / area;
                var threshold = options.Method switch
                {
                    ThresholdMethod.Mean => (mean * 1) - (options.Offset * max),
                    ThresholdMethod.Bradley => (mean * (1 - options.BradleyT)) - 0,
                    _ => mean * (1 + (options.SauvolaK * ((Math.Sqrt((double)(((Int128)squares * area) - ((Int128)sum * sum)) / ((double)area * area)) / (options.SauvolaR * max)) - 1))),
                };
                mask[(y * width) + x] = luma[(y * width) + x] > threshold;
            }
        }

        return mask;
    }

    /// <summary>The index a window reads outside the frame: reflected (d c b a | a b c d | d c b a), clamped, wrapped, or -1 (zero).</summary>
    private static int MapWindowIndex(int index, int length, ConvolutionEdgeMode edgeMode)
    {
        if (index >= 0 && index < length)
            return index;

        var period = 2 * length;
        var reflected = ((index % period) + period) % period;
        return edgeMode switch
        {
            ConvolutionEdgeMode.Clamp => Math.Clamp(index, 0, length - 1),
            ConvolutionEdgeMode.Wrap => ((index % length) + length) % length,
            ConvolutionEdgeMode.Mirror => reflected < length ? reflected : period - 1 - reflected,
            _ => -1,
        };
    }

    /// <summary>
    /// <see cref="ImageProcessingExtensions.IsOpaque(Image, CancellationToken)"/> and its frame overload on an image of any
    /// format of <see cref="Formats"/> (<see cref="CreateImage"/>, with frames and a poster): the result is whether every
    /// alpha read from the bytes of the frames is the largest value of the format (exactly 1 for floating-point alpha).
    /// </summary>
    private static void OpacityTarget(ReadOnlySpan<byte> data)
    {
        using var image = CreateImage(ref data, maxSide: 24);
        var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
        var all = true;
        foreach (var frame in frames)
        {
            var expected = IsOpaqueReference(frame);
            FuzzAssert.Equal(expected, frame.IsOpaque(), $"The opacity of a {frame.PixelFormat} frame of {frame.Width}x{frame.Height} pixels is wrong");
            all &= expected;
        }

        FuzzAssert.Equal(all, image.IsOpaque(), $"The opacity of a {image.PixelFormat} image of {frames.Length} frames is wrong");
    }

    private static bool IsOpaqueReference(ImageFrame frame)
    {
        var size = PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        var bytes = new byte[frame.Width * frame.Height * size];
        frame.CopyPixelBytesTo(bytes);
        for (var offset = 0; offset < bytes.Length; offset += size)
        {
            var pixel = bytes.AsSpan(offset, size);
            var opaque = frame.PixelFormat switch
            {
                PixelFormat.Rgba32 or PixelFormat.Bgra32 => pixel[3] == 255,
                PixelFormat.Rgba64 => BitConverter.ToUInt16(pixel[6..]) == ushort.MaxValue,
                PixelFormat.La16 => pixel[1] == 255,
                PixelFormat.A8 => pixel[0] == 255,
                PixelFormat.RgbaFloat => BitConverter.ToSingle(pixel[12..]) == 1f,
                PixelFormat.RgbaHalf => BitConverter.ToHalf(pixel[6..]) == Half.One,
                _ => true,
            };

            if (!opaque)
                return false;
        }

        return true;
    }

    private static IEnumerable<FuzzSeed> GetThresholdSeeds()
    {
        // The rows of the unit tests: Bradley on 100, 100, 70, 100, 100 (window 3, t = 0.15 is about 38/255), a pixel 14%
        // darker than its window with t = 0.25, Mean and Sauvola on uniform rows, and fixed levels at the edges of the range
        yield return new FuzzSeed("bradley-dip", (byte[])[4, 0, 0, 38, 0, 4, 0, 100, 100, 70, 100, 100]);
        yield return new FuzzSeed("bradley-tolerant", (byte[])[4, 0, 0, 64, 0, 2, 0, 100, 80, 100]);
        yield return new FuzzSeed("bradley-full", (byte[])[4, 0, 0, 255, 0, 2, 0, 0, 1, 255]);
        yield return new FuzzSeed("mean-uniform", (byte[])[2, 0, 0, 128, 0, 3, 0, 50, 50, 50, 50]);
        yield return new FuzzSeed("mean-offset", (byte[])[2, 0, 0, 129, 0, 3, 0, 50, 50, 50, 50]);
        yield return new FuzzSeed("sauvola-uniform", (byte[])[3, 0, 0, 51, 0, 2, 0, 100, 100, 100]);
        yield return new FuzzSeed("fixed-range", (byte[])[1, 0, 0, 128, 0, 3, 0, 0, 127, 128, 255]);
        yield return new FuzzSeed("otsu-bimodal", (byte[])[0, 0, 0, 0, 0, 13, 0, 10, 10, 10, 10, 10, 11, 11, 11, 200, 200, 200, 200, 201, 201]);

        // Textured frames in every format, edge mode and window, larger than the windows and smaller than them
        yield return new FuzzSeed("bradley-gray16-zero", (byte[])[4, 7, 3, 38, 1, 23, 11, 0x10, 0x9C, 0x40, 0x20, 0xFF, 0xFF, 0x00, 0x00, 0x34, 0x12]);
        yield return new FuzzSeed("sauvola-la16-wrap", (byte[])[3, 2, 2, 51, 2, 15, 7, 60, 255, 200, 255, 90, 128, 140, 255, 30, 0]);
        yield return new FuzzSeed("mean-rgba-clamp", (byte[])[2, 1, 1, 136, 3, 11, 5, 10, 200, 30, 255, 250, 90, 0, 128, 70, 70, 70, 255]);
        yield return new FuzzSeed("bradley-rgba-mirror", (byte[])[4, 5, 0, 38, 3, 9, 9, 120, 140, 160, 255, 40, 60, 80, 0, 230, 210, 190, 255]);
    }

    private static IEnumerable<FuzzSeed> GetOpacitySeeds()
    {
        // The cases of the unit tests: opaque and translucent 8-bit, 16-bit, gray and mask pixels, floating-point alphas of
        // 1, 0.999, 1.5 and NaN, formats without alpha, and frames with a poster whose last pixel is translucent
        yield return new FuzzSeed("rgba32-opaque", (byte[])[0, 39, 1, 0, 1, 2, 3, 255]);
        yield return new FuzzSeed("rgba32-translucent", (byte[])[0, 4, 1, 0, 1, 2, 3, 255, 1, 2, 3, 255, 1, 2, 3, 255, 1, 2, 3, 254]);
        yield return new FuzzSeed("rgba64-translucent", (byte[])[4, 2, 1, 0, 1, 0, 2, 0, 3, 0, 0xFF, 0xFF, 1, 0, 2, 0, 3, 0, 0xFE, 0xFF]);
        yield return new FuzzSeed("la16-frames-poster", (byte[])[5, 3, 2, 0x82, 10, 255, 20, 255, 30, 255, 40, 255, 50, 255, 60, 254]);
        yield return new FuzzSeed("a8-mask", (byte[])[9, 17, 1, 0, 255]);
        yield return new FuzzSeed("float-one", (byte[])[6, 5, 1, 0, 0, 0, 0x80, 0x40, 0, 0, 0x80, 0xBF, 0, 0, 0, 0x3F, 0, 0, 0x80, 0x3F]);
        yield return new FuzzSeed("float-nan", (byte[])[6, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xC0, 0x7F]);
        yield return new FuzzSeed("float-above-one", (byte[])[6, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xC0, 0x3F]);
        yield return new FuzzSeed("half-one", (byte[])[7, 9, 1, 1, 0, 0x38, 0, 0x38, 0, 0x38, 0, 0x3C]);
        yield return new FuzzSeed("rgb24-no-alpha", (byte[])[1, 3, 2, 0, 10, 20, 30]);
        yield return new FuzzSeed("bgra32-poster", (byte[])[10, 2, 2, 0x81, 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 0]);
    }
}
