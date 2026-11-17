using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    // Samples that are exactly 1 (opaque alphas), out of the range of colors, subnormal, or not numbers
    private static readonly float[] SpecialSamples = [1f, 1f, 1f, 1f, 0f, -0f, 0.5f, 2f, -1f, 1e-40f, 3e38f, float.NaN, MathF.BitIncrement(1f), MathF.BitDecrement(1f)];

    /// <summary>
    /// <see cref="ImageProcessingExtensions.Resize(Image, ResizeOptions, CancellationToken)"/> of <see cref="RgbaFloat"/>
    /// images, whose kernels have vector loops of several widths (the scan for opaque pixels, the loads and the stores):
    /// a byte of filter (all the alphas are set to 1 when its high bit is set), a byte of source width and one of height (1 to 40), a byte of output width and one of height (1
    /// to 40), then one byte per sample (a special value below 14, a fraction otherwise), repeated when the input is
    /// shorter. The colors filter independently and the same way, so exchanging the red and the blue of the source
    /// exchanges them in the result, bit for bit; an image whose alphas are all 1 keeps them.
    /// </summary>
    private static void ResizeFloatTarget(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        var filter = ResizeFilters[(mode & 0x7F) % ResizeFilters.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var outputWidth = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var outputHeight = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var samples = new float[width * height * 4];
        for (var i = 0; i < samples.Length && !data.IsEmpty; i++)
        {
            var b = data[i % data.Length];
            samples[i] = b < SpecialSamples.Length ? SpecialSamples[b] : (b - 14) / 241f;
        }

        if (mode >= 0x80)
        {
            // Opaque images take the kernels of 3 colors per pixel
            for (var i = 3; i < samples.Length; i += 4)
            {
                samples[i] = 1f;
            }
        }

        var swapped = (float[])samples.Clone();
        var isOpaque = true;
        for (var i = 0; i < samples.Length; i += 4)
        {
            (swapped[i], swapped[i + 2]) = (samples[i + 2], samples[i]);
            isOpaque &= BitConverter.SingleToUInt32Bits(samples[i + 3]) == BitConverter.SingleToUInt32Bits(1f);
        }

        var options = new ResizeOptions(outputWidth, outputHeight) { Mode = ResizeMode.Stretch, Filter = filter };
        var result = Resize(samples);
        var resultSwapped = Resize(swapped);
        for (var i = 0; i < result.Length; i += 4)
        {
            FuzzAssert.Equal(BitConverter.SingleToUInt32Bits(result[i]), BitConverter.SingleToUInt32Bits(resultSwapped[i + 2]), $"Red of output pixel {i / 4} differs from the blue of the swapped resize");
            FuzzAssert.Equal(BitConverter.SingleToUInt32Bits(result[i + 1]), BitConverter.SingleToUInt32Bits(resultSwapped[i + 1]), $"Green of output pixel {i / 4} differs in the swapped resize");
            FuzzAssert.Equal(BitConverter.SingleToUInt32Bits(result[i + 2]), BitConverter.SingleToUInt32Bits(resultSwapped[i]), $"Blue of output pixel {i / 4} differs from the red of the swapped resize");
            FuzzAssert.Equal(BitConverter.SingleToUInt32Bits(result[i + 3]), BitConverter.SingleToUInt32Bits(resultSwapped[i + 3]), $"Alpha of output pixel {i / 4} differs in the swapped resize");
            if (isOpaque)
            {
                FuzzAssert.Equal(1f, result[i + 3], $"The alpha of output pixel {i / 4} is not 1");
            }
        }

        float[] Resize(float[] source)
        {
            var sourceBytes = new byte[source.Length * sizeof(float)];
            Buffer.BlockCopy(source, 0, sourceBytes, 0, sourceBytes.Length);
            using var image = Image.ImportPixelBytes<RgbaFloat>(sourceBytes, width, height);
            image.Resize(options);
            var outputBytes = new byte[outputWidth * outputHeight * 4 * sizeof(float)];
            image.Frames[0].CopyPixelBytesTo(outputBytes);
            var output = new float[outputWidth * outputHeight * 4];
            Buffer.BlockCopy(outputBytes, 0, output, 0, outputBytes.Length);
            return output;
        }
    }

    private static IEnumerable<FuzzSeed> GetResizeFloatSeeds()
    {
        // Opaque images, images with alphas of every kind, special values, and the sizes of the unit tests
        yield return new FuzzSeed("opaque-bicubic-2x", (byte[])[0x81, 15, 9, 7, 4, 120, 200, 40, 0, 90, 30, 250, 1, 60, 60, 60, 2]);
        yield return new FuzzSeed("opaque-upscale", (byte[])[0x80, 3, 2, 37, 20, 100, 30, 200, 3]);
        yield return new FuzzSeed("one-translucent-pixel", (byte[])[2, 21, 5, 6, 3, 17, 40, 63, 0, 80, 120, 160, 128]);
        yield return new FuzzSeed("special-values", (byte[])[4, 9, 9, 4, 4, 11, 10, 9, 0, 8, 7, 6, 12, 5, 4, 13, 1]);
        yield return new FuzzSeed("single-pixel", (byte[])[3, 0, 0, 0, 0, 200, 100, 50, 0]);
    }
}
