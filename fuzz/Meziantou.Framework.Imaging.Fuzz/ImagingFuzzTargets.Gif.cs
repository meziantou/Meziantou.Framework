using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The lossy compression of the GIF encoder (<see cref="GifEncoder.LossyTolerance"/>). The input is two bytes of tolerance, a
/// byte of options (interlacing, frame regions, dithering, number of colors), then an image. The lossless and the lossy
/// encodings succeed or fail together, and give the same displayed frames, with the same transparent pixels, and each opaque
/// pixel of the lossy output at most the tolerance farther (squared RGB distance) from its source color than in the
/// lossless output.
/// </summary>
public static partial class ImagingFuzzTargets
{
    private static readonly DitheringMode[] GifDitheringModes = [DitheringMode.None, DitheringMode.FloydSteinberg, DitheringMode.Atkinson, DitheringMode.Bayer4x4];
    private static readonly int[] GifColorCounts = [256, 16, 4, 2];

    private static void GifLossyTarget(ReadOnlySpan<byte> data)
    {
        var tolerance = Math.Min(GifEncoder.MaxLossyTolerance, 1 + ((FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8)) * 3));
        var options = FuzzInput.ReadByte(ref data);
        var quantization = new QuantizeOptions { Dithering = GifDitheringModes[(options >> 2) & 3], MaxColors = GifColorCounts[(options >> 4) & 3] };
        var lossless = new GifEncoder { Interlaced = (options & 1) != 0, OptimizeFrames = (options & 2) != 0, Quantization = quantization, MetadataHandling = MetadataHandling.Strip };
        var lossy = new GifEncoder { Interlaced = lossless.Interlaced, OptimizeFrames = lossless.OptimizeFrames, Quantization = quantization, MetadataHandling = MetadataHandling.Strip, LossyTolerance = tolerance };

        using var image = CreateImage(ref data, maxSide: 24);
        byte[]? losslessGif = null;
        byte[]? lossyGif = null;
        var losslessResult = Catch(() => losslessGif = Save(image, lossless));
        var lossyResult = Catch(() => lossyGif = Save(image, lossy));
        FuzzAssert.Equal(losslessResult.Exception?.GetType(), lossyResult.Exception?.GetType(), "The lossless and the lossy encodings fail differently");
        if (losslessGif is null || lossyGif is null)
            return;

        FuzzAssert.True(Save(image, lossy).AsSpan().SequenceEqual(lossyGif), "The lossy encoding is not deterministic");
        using var expected = Image.Load<Rgba32>(losslessGif);
        using var actual = Image.Load<Rgba32>(lossyGif);
        FuzzAssert.Equal(expected.Frames.Count, actual.Frames.Count, "The number of frames");
        using var source = image.CloneAs<Rgba32>();
        for (var i = 0; i < expected.Frames.Count; i++)
        {
            var sourceBytes = GetRgbaBytes(source.Frames[i]);
            var expectedBytes = GetRgbaBytes(expected.Frames[i]);
            var actualBytes = GetRgbaBytes(actual.Frames[i]);
            for (var p = 0; p < sourceBytes.Length; p += 4)
            {
                FuzzAssert.Equal(expectedBytes[p + 3], actualBytes[p + 3], "The alpha of a pixel");
                if (expectedBytes[p + 3] == 0)
                    continue;

                var added = GetSquaredDistance(sourceBytes, actualBytes, p) - GetSquaredDistance(sourceBytes, expectedBytes, p);
                FuzzAssert.True(added <= tolerance, $"A pixel of frame {i} is {added} farther from its color, more than the tolerance {tolerance}");
            }
        }
    }

    private static byte[] GetRgbaBytes(ImageFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * 4];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static int GetSquaredDistance(byte[] a, byte[] b, int offset)
    {
        var total = 0;
        for (var c = 0; c < 3; c++)
        {
            var difference = a[offset + c] - b[offset + c];
            total += difference * difference;
        }

        return total;
    }

    private static IEnumerable<FuzzSeed> GetGifLossySeeds()
    {
        // The cases of the unit tests: a photo-like gradient (no dithering, interlaced Floyd–Steinberg, ordered dithering),
        // an image with transparent pixels, animated frames with regions, few colors, and the largest tolerance
        yield return new FuzzSeed("gradient", (byte[])[10, 0, 0x00, 1, 23, 15, 0, 10, 40, 90, 20, 60, 130, 30, 80, 170, 45, 100, 210, 60, 120, 250]);
        yield return new FuzzSeed("interlaced-floyd-steinberg", (byte[])[100, 0, 0x05, 1, 20, 20, 0, 255, 0, 0, 200, 50, 0, 150, 100, 0, 100, 150, 0, 50, 200]);
        yield return new FuzzSeed("bayer", (byte[])[50, 0, 0x0C, 0, 18, 12, 0, 1, 2, 3, 90, 80, 70, 200, 10, 30, 7, 9, 250, 128, 64, 32]);
        yield return new FuzzSeed("transparent", (byte[])[30, 0, 0x00, 0, 16, 16, 0, 10, 20, 30, 255, 40, 50, 60, 0, 70, 80, 90, 255, 100, 110, 120, 10]);
        yield return new FuzzSeed("animation-regions", (byte[])[60, 0, 0x02, 0, 12, 10, 2, 5, 15, 25, 255, 35, 45, 55, 255, 65, 75, 85, 0]);
        yield return new FuzzSeed("four-colors", (byte[])[200, 0, 0x20, 1, 16, 16, 0, 0, 64, 128, 192, 255, 32, 96, 160, 224]);
        yield return new FuzzSeed("largest-tolerance", (byte[])[0xFF, 0xFF, 0x00, 1, 23, 23, 0, 13, 57, 101, 145, 189, 233, 21, 65, 109, 153, 197, 241]);
    }
}
