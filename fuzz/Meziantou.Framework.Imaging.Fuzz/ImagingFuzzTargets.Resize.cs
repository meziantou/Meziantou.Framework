using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    private static readonly ResamplingFilter[] ResizeFilters =
    [
        ResamplingFilter.Bilinear, ResamplingFilter.Bicubic, ResamplingFilter.Lanczos3, ResamplingFilter.Box, ResamplingFilter.Hermite,
    ];

    /// <summary>
    /// <see cref="ImageProcessingExtensions.Resize(Image, ResizeOptions, CancellationToken)"/> of opaque 8-bit images, whose
    /// kernels (gray, 3-sample and 4-sample pixels, each with vector loops of several widths and their tails) must agree: a
    /// byte of filter, a byte of source width and one of height (1 to 96), a byte of output width and one of height (1 to
    /// 96), then the samples of three gray planes, repeated when the input is shorter. Each plane is resized as a
    /// <see cref="Gray8"/> image and as one channel of an <see cref="Rgb24"/> and an opaque <see cref="Rgba32"/> image: the
    /// channels filter independently, so every sample must match its gray resize, and the alpha must stay opaque.
    /// </summary>
    private static void ResizeTarget(ReadOnlySpan<byte> data)
    {
        var filter = ResizeFilters[FuzzInput.ReadByte(ref data) % ResizeFilters.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % 96);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 96);
        var outputWidth = 1 + (FuzzInput.ReadByte(ref data) % 96);
        var outputHeight = 1 + (FuzzInput.ReadByte(ref data) % 96);
        var count = width * height;
        var planes = new byte[3][];
        for (var c = 0; c < 3; c++)
        {
            planes[c] = new byte[count];
            for (var i = 0; i < count && !data.IsEmpty; i++)
            {
                planes[c][i] = data[((c * count) + i) % data.Length];
            }
        }

        var rgb = new byte[count * 3];
        var rgba = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                rgb[(i * 3) + c] = planes[c][i];
                rgba[(i * 4) + c] = planes[c][i];
            }

            rgba[(i * 4) + 3] = byte.MaxValue;
        }

        var options = new ResizeOptions(outputWidth, outputHeight) { Mode = ResizeMode.Stretch, Filter = filter };
        var outputCount = outputWidth * outputHeight;
        var resizedRgb = Resize<Rgb24>(rgb, width, height, options, outputCount * 3);
        var resizedRgba = Resize<Rgba32>(rgba, width, height, options, outputCount * 4);
        for (var c = 0; c < 3; c++)
        {
            var gray = Resize<Gray8>(planes[c], width, height, options, outputCount);
            for (var i = 0; i < outputCount; i++)
            {
                FuzzAssert.Equal(gray[i], resizedRgb[(i * 3) + c], $"Rgb24 channel {c} of output pixel {i} differs from its gray resize");
                FuzzAssert.Equal(gray[i], resizedRgba[(i * 4) + c], $"Rgba32 channel {c} of output pixel {i} differs from its gray resize");
            }
        }

        for (var i = 0; i < outputCount; i++)
        {
            FuzzAssert.Equal(byte.MaxValue, resizedRgba[(i * 4) + 3], $"The alpha of output pixel {i} is not opaque");
        }

        static byte[] Resize<TPixel>(byte[] pixels, int width, int height, ResizeOptions options, int outputLength)
            where TPixel : unmanaged
        {
            using var image = Image.ImportPixelBytes<TPixel>(pixels, width, height);
            image.Resize(options);
            FuzzAssert.Equal(options.Size.Width, image.Width, "Output width");
            FuzzAssert.Equal(options.Size.Height, image.Height, "Output height");
            var result = new byte[outputLength];
            image.Frames[0].CopyPixelBytesTo(result);
            return result;
        }
    }

    private static IEnumerable<FuzzSeed> GetResizeSeeds()
    {
        // The sizes of the unit tests of the kernels: a 2x downscale (8 bicubic taps), strong downscales (long windows: 8-, 4-
        // and 2-tap blocks), upscales, single pixels, and widths around the 32-, 16- and 8-sample loops of the vertical pass
        yield return new FuzzSeed("bicubic-2x", (byte[])[1, 63, 31, 31, 15, 0, 255, 17, 128, 64, 200, 3, 99]);
        yield return new FuzzSeed("lanczos-strong-downscale", (byte[])[2, 95, 40, 2, 3, 255, 0, 255, 0, 30, 220]);
        yield return new FuzzSeed("bilinear-upscale", (byte[])[0, 4, 3, 95, 70, 10, 250, 40, 90]);
        yield return new FuzzSeed("single-pixel", (byte[])[1, 0, 0, 0, 0, 77]);
        yield return new FuzzSeed("box-37-to-11", (byte[])[3, 36, 36, 10, 10, 1, 2, 3, 4, 5, 6, 7, 8, 9, 250, 251, 252]);
        yield return new FuzzSeed("hermite-wide", (byte[])[4, 95, 1, 32, 1, 0, 255]);
        yield return new FuzzSeed("bicubic-1000-to-9-like", (byte[])[1, 95, 5, 8, 4, 255, 255, 0, 0, 128]);
    }
}
