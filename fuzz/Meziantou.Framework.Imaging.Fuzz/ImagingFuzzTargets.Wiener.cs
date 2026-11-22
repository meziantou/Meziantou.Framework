using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="ImageProcessingExtensions.WienerFilter(Image, int, double?, ConvolutionEdgeMode, CancellationToken)"/>
    /// with options from the input: a byte of options (the edge mode in bits 0 and 1, an estimated noise in bit 2, the first
    /// frame only in bit 3), a byte of radius (0 to 6) and a byte of noise variance (<c>(n / 255)²</c>), then an image
    /// (<see cref="CreateImage"/>). The filter keeps the size and the frames, gives the same result twice, and leaves the
    /// other frames when it filters one. On the samples of an image without alpha, every result lies between the smallest
    /// and the largest sample its windows read (0 included with <see cref="ConvolutionEdgeMode.Zero"/>), within a unit of
    /// rounding, and a noise variance of 0 changes nothing.
    /// </summary>
    private static void WienerFilterTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var edgeMode = (ConvolutionEdgeMode)(flags & 3);
        var estimated = (flags & 4) != 0;
        var firstFrameOnly = (flags & 8) != 0;
        var radius = FuzzInput.ReadByte(ref data) % 7;
        var noiseByte = FuzzInput.ReadByte(ref data);
        double? noise = estimated ? null : noiseByte / 255d * (noiseByte / 255d);
        using var image = CreateImage(ref data, maxSide: 12);
        using var original = image.Clone();
        using var again = image.Clone();
        Filter(image);
        Filter(again);

        FuzzAssert.Equal(Snapshot(again), Snapshot(image), "The Wiener filter is not deterministic");
        FuzzAssert.Equal(original.Size, image.Size, "The Wiener filter changed the size");
        FuzzAssert.Equal(original.Frames.Count, image.Frames.Count, "The Wiener filter changed the frames");
        FuzzAssert.Equal(original.PosterFrame is null, image.PosterFrame is null, "The Wiener filter changed the poster");
        var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
        var originalFrames = original.PosterFrame is null ? [.. original.Frames] : (ImageFrame[])[.. original.Frames, original.PosterFrame];
        var length = image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat);
        var (sampleBytes, channels) = image.PixelFormat switch
        {
            PixelFormat.Gray8 => (1, 1),
            PixelFormat.Rgb24 => (1, 3),
            PixelFormat.Gray16 => (2, 1),
            PixelFormat.Rgb48 => (2, 3),
            _ => (0, 0),
        };

        for (var f = 0; f < frames.Length; f++)
        {
            var expected = new byte[length];
            var actual = new byte[length];
            originalFrames[f].CopyPixelBytesTo(expected);
            frames[f].CopyPixelBytesTo(actual);
            if ((firstFrameOnly && f > 0) || radius == 0 || (noise == 0 && sampleBytes != 0))
            {
                FuzzAssert.True(expected.AsSpan().SequenceEqual(actual), $"The Wiener filter changed frame {f}, which it should keep");
                continue;
            }

            if (sampleBytes == 0)
                continue;

            // The bounds of every channel: the samples of the frame, and 0 when the windows read beyond the edges as zeros
            var low = new int[channels];
            var high = new int[channels];
            for (var c = 0; c < channels; c++)
            {
                low[c] = edgeMode == ConvolutionEdgeMode.Zero ? 0 : int.MaxValue;
                high[c] = edgeMode == ConvolutionEdgeMode.Zero ? 0 : int.MinValue;
            }

            for (var i = 0; i < length; i += sampleBytes)
            {
                var c = i / sampleBytes % channels;
                var sample = sampleBytes == 1 ? expected[i] : BitConverter.ToUInt16(expected, i);
                low[c] = Math.Min(low[c], sample);
                high[c] = Math.Max(high[c], sample);
            }

            for (var i = 0; i < length; i += sampleBytes)
            {
                var c = i / sampleBytes % channels;
                var sample = sampleBytes == 1 ? actual[i] : BitConverter.ToUInt16(actual, i);
                FuzzAssert.True(sample >= low[c] - 1 && sample <= high[c] + 1, $"Sample {i / sampleBytes} of frame {f} is {sample}, outside [{low[c]}, {high[c]}]");
            }
        }

        void Filter(Image target)
        {
            if (firstFrameOnly)
            {
                target.Frames[0].WienerFilter(radius, noise, edgeMode);
            }
            else
            {
                target.WienerFilter(radius, noise, edgeMode);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetWienerFilterSeeds()
    {
        // The cases of the unit tests: halves with a little noise and a given variance, an estimated noise with each edge
        // mode, no noise, translucent pixels, a sample that is not a number, and a radius of 0
        yield return new FuzzSeed("halves-given", (byte[])[0, 2, 3, 2, 31, 15, 0, 40, 42, 44, 46, 48, 200, 202, 204, 206, 208]);
        yield return new FuzzSeed("estimated-mirror-rgb48", (byte[])[5, 2, 0, 11, 9, 7, 0x81, 0x10, 0x27, 0x50, 0xC3, 0xFF, 0xFF, 0, 0, 0x34, 0x12]);
        yield return new FuzzSeed("estimated-zero-gray16", (byte[])[7, 1, 0, 3, 7, 5, 1, 0, 0x80, 0xFF, 0xFF, 0x12, 0x34, 0, 0]);
        yield return new FuzzSeed("no-noise-rgb24", (byte[])[2, 3, 0, 1, 6, 6, 2, 1, 2, 3, 250, 128, 7]);
        yield return new FuzzSeed("translucent-frame", (byte[])[12, 1, 40, 0, 5, 5, 2, 255, 0, 0, 128, 0, 255, 0, 0]);
        yield return new FuzzSeed("float-nan", (byte[])[4, 1, 0, 6, 3, 3, 0, 0, 0, 0xC0, 0x7F, 0, 0, 0x80, 0x3F]);
        yield return new FuzzSeed("radius-zero", (byte[])[0, 0, 100, 2, 4, 4, 0, 1, 2, 3, 4]);
    }

    /// <summary>
    /// <see cref="ImageProcessingExtensions.WienerDeconvolve(Image, WienerDeconvolutionOptions, CancellationToken)"/> with a
    /// kernel and a noise-to-signal ratio from the input. The input is a byte of options (the edge mode in bits 0 and 1, a
    /// round trip in bit 2, the first frame only in bit 3), a byte of kernel width and one of height (1 to 5), a byte of
    /// noise-to-signal ratio (10^-n, n from 0 to 12), the weights of the kernel, then an image
    /// (<see cref="CreateImage"/>). The deconvolution keeps the size and the frames of the image, gives the same result
    /// twice, and leaves the other frames when it deconvolves one. In a round trip, the weights are positive and the
    /// middle one exceeds the others by at least 1/4: a convolution with wrapped edges then a deconvolution with wrapped
    /// edges gives the samples of an image without alpha back within 3 units.
    /// </summary>
    private static void WienerDeconvolutionTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var edgeMode = (ConvolutionEdgeMode)(flags & 3);
        var roundTrip = (flags & 4) != 0;
        var firstFrameOnly = (flags & 8) != 0;
        var kernelWidth = 1 + (FuzzInput.ReadByte(ref data) % 5);
        var kernelHeight = 1 + (FuzzInput.ReadByte(ref data) % 5);
        var noise = Math.Pow(10, -(FuzzInput.ReadByte(ref data) % 13));
        var weights = new double[kernelWidth * kernelHeight];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = FuzzInput.ReadByte(ref data);
        }

        using var image = CreateImage(ref data, maxSide: 12);
        if (roundTrip)
        {
            CheckWienerRoundTrip(image, kernelWidth, kernelHeight, weights);
            return;
        }

        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = ((sbyte)weights[i]) / 32d;
        }

        var options = new WienerDeconvolutionOptions(new ConvolutionKernel(kernelWidth, kernelHeight, weights), Math.Max(noise, WienerDeconvolutionOptions.MinNoiseToSignalRatio)) { EdgeMode = edgeMode };
        using var original = image.Clone();
        using var again = image.Clone();
        Deconvolve(image, options, firstFrameOnly);
        Deconvolve(again, options, firstFrameOnly);

        FuzzAssert.Equal(Snapshot(again), Snapshot(image), "The deconvolution is not deterministic");
        FuzzAssert.Equal(original.Size, image.Size, "The deconvolution changed the size");
        FuzzAssert.Equal(original.Frames.Count, image.Frames.Count, "The deconvolution changed the frames");
        FuzzAssert.Equal(original.PosterFrame is null, image.PosterFrame is null, "The deconvolution changed the poster");
        if (firstFrameOnly)
        {
            var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
            var originalFrames = original.PosterFrame is null ? [.. original.Frames] : (ImageFrame[])[.. original.Frames, original.PosterFrame];
            var length = image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat);
            for (var i = 1; i < frames.Length; i++)
            {
                var expected = new byte[length];
                var actual = new byte[length];
                originalFrames[i].CopyPixelBytesTo(expected);
                frames[i].CopyPixelBytesTo(actual);
                FuzzAssert.True(expected.AsSpan().SequenceEqual(actual), $"The deconvolution of the first frame changed frame {i}");
            }
        }
    }

    private static void Deconvolve(Image image, WienerDeconvolutionOptions options, bool firstFrameOnly)
    {
        if (firstFrameOnly)
        {
            image.Frames[0].WienerDeconvolve(options);
        }
        else
        {
            image.WienerDeconvolve(options);
        }
    }

    /// <summary>Convolves with wrapped edges, deconvolves with wrapped edges, and compares the samples with the original.</summary>
    private static void CheckWienerRoundTrip(Image image, int kernelWidth, int kernelHeight, double[] bytes)
    {
        var middle = ((kernelHeight / 2) * kernelWidth) + (kernelWidth / 2);
        var weights = new double[bytes.Length];
        var others = 0d;
        for (var i = 0; i < weights.Length; i++)
        {
            if (i != middle)
            {
                weights[i] = bytes[i] / 255 * 0.375 / Math.Max(1, weights.Length - 1);
                others += weights[i];
            }
        }

        weights[middle] = 1 - others;
        var kernel = new ConvolutionKernel(kernelWidth, kernelHeight, weights);
        using var original = image.Clone();
        image.Convolve(new ConvolutionOptions(kernel) { EdgeMode = ConvolutionEdgeMode.Wrap });
        image.WienerDeconvolve(new WienerDeconvolutionOptions(kernel, WienerDeconvolutionOptions.MinNoiseToSignalRatio) { EdgeMode = ConvolutionEdgeMode.Wrap });
        var (sampleBytes, compare) = image.PixelFormat switch
        {
            PixelFormat.Gray8 or PixelFormat.Rgb24 => (1, true),
            PixelFormat.Gray16 or PixelFormat.Rgb48 => (2, true),
            _ => (0, false),
        };

        if (!compare)
            return;

        var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
        var originalFrames = original.PosterFrame is null ? [.. original.Frames] : (ImageFrame[])[.. original.Frames, original.PosterFrame];
        for (var f = 0; f < frames.Length; f++)
        {
            var length = image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat);
            var expected = new byte[length];
            var actual = new byte[length];
            originalFrames[f].CopyPixelBytesTo(expected);
            frames[f].CopyPixelBytesTo(actual);
            for (var i = 0; i < length; i += sampleBytes)
            {
                var expectedSample = sampleBytes == 1 ? expected[i] : BitConverter.ToUInt16(expected, i);
                var actualSample = sampleBytes == 1 ? actual[i] : BitConverter.ToUInt16(actual, i);
                FuzzAssert.True(Math.Abs(expectedSample - actualSample) <= 3, $"Sample {i / sampleBytes} of frame {f} is {actualSample} after the round trip, instead of {expectedSample}");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetWienerDeconvolutionSeeds()
    {
        // The cases of the unit tests: a horizontal motion blur, a wrapped round trip of a cross kernel over every frame of
        // a gray image and of 16-bit colors, a frame of an animation, and an even kernel over translucent pixels
        yield return new FuzzSeed("motion-clamp", (byte[])[0, 4, 0, 4, 32, 32, 32, 32, 32, 2, 11, 7, 1, 50, 200, 50, 200]);
        yield return new FuzzSeed("round-trip-gray", (byte[])[6, 2, 2, 12, 10, 200, 30, 90, 0, 120, 40, 250, 5, 2, 7, 5, 0x81, 0, 255, 17, 200, 3, 99, 180, 44, 250, 1, 128, 77]);
        yield return new FuzzSeed("round-trip-rgb48", (byte[])[6, 2, 2, 12, 200, 30, 70, 64, 0, 90, 5, 1, 16, 11, 4, 3, 0, 0x10, 0x27, 0x50, 0xC3, 0xFF, 0xFF, 0, 0, 0x34, 0x12, 0x99, 0x88]);
        yield return new FuzzSeed("frame-mirror", (byte[])[9, 0, 4, 2, 8, 8, 64, 8, 8, 2, 5, 5, 2, 100, 100, 200, 100]);
        yield return new FuzzSeed("even-zero-rgba", (byte[])[3, 1, 1, 6, 32, 16, 16, 8, 0, 6, 4, 0, 255, 0, 0, 128, 0, 255, 0, 64]);
        yield return new FuzzSeed("float-nan", (byte[])[1, 1, 1, 1, 32, 16, 16, 8, 6, 2, 2, 0, 0, 0, 0xC0, 0x7F, 0, 0, 0x80, 0x3F]);
    }
}
