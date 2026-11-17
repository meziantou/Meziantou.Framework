using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The layers of JPEG XL images (<see cref="JpegXlLayeredImage"/>) and the animations written as the regions that change
/// (<see cref="JpegXlEncoder.OptimizeFrames"/>):
/// <list type="bullet">
/// <item><description><c>imaging-jxl-layers</c>: layers of any size, position, blend mode and name, written with any setting, are read back with their geometry, their names and, for lossless output, their samples; the image they make has the size of the canvas.</description></item>
/// <item><description><c>imaging-jxl-layers-load</c>: the layers of any input are read or rejected with the documented exceptions; layers that are read are written again losslessly and read back the same (a document round trip).</description></item>
/// <item><description><c>imaging-jxl-optimized-animation</c>: an animation written as the regions that change decodes to its frames exactly.</description></item>
/// </list>
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The bytes of the settings and of the layers, and the tokens of the codestream and of its frame headers. A property: All,
    // in another file of the class, reads it, and the initializers of the files of a partial class run in no defined order
    private static string[] JpegXlLayerTokens =>
    [
        .. Tokens, "ÿ\u000A", "\0\0\0\u000CJXL \r\n\u0087\n", "ftypjxl ", "jxlp", "jxll", "jxlc", "\u0080", "ÿ", "þ", "\u0081",
    ];

    // Small images: a file that declares a large one fails at the limits instead of allocating
    private static readonly ImageConfiguration JpegXlLayerConfiguration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 20, MaxTotalPixels = 1 << 22, MaxMetadataBytes = 1 << 20 },
    };

    private static readonly PixelFormat[] JpegXlLayerFormats = [PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16, PixelFormat.Rgba64, PixelFormat.Bgra32];

    /// <summary>
    /// Reads the settings (a byte of pixel format, a byte of effort with the progressive, lossy and quality bits, the width
    /// and the height of the canvas, a layer count), then for each layer its width, height, signed offsets, blend mode and
    /// name length, then the bytes of the pixels; writes the layers, reads them back and checks them.
    /// </summary>
    private static void JpegXlLayersTarget(ReadOnlySpan<byte> data)
    {
        var format = JpegXlLayerFormats[FuzzInput.ReadByte(ref data) % JpegXlLayerFormats.Length];
        var settings = FuzzInput.ReadByte(ref data);
        var lossy = (settings & 0x20) != 0;
        var encoder = new JpegXlEncoder
        {
            Effort = (settings & 0x0F) % 10,
            Progressive = (settings & 0x10) != 0,
            Compression = lossy ? JpegXlCompression.Lossy : JpegXlCompression.Lossless,
            Quality = (settings & 0x40) != 0 ? 95 : 40,
        };
        var width = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var count = 1 + (FuzzInput.ReadByte(ref data) % 3);
        using var document = new JpegXlLayeredImage(width, height, JpegXlLayerConfiguration);
        var layers = new (int Width, int Height, Point Offset, JpegXlBlendMode Mode, string? Name)[count];
        for (var i = 0; i < count; i++)
        {
            var layerWidth = 1 + (FuzzInput.ReadByte(ref data) % 48);
            var layerHeight = 1 + (FuzzInput.ReadByte(ref data) % 48);
            var offset = new Point((sbyte)FuzzInput.ReadByte(ref data) % 50, (sbyte)FuzzInput.ReadByte(ref data) % 50);
            var mode = (JpegXlBlendMode)(FuzzInput.ReadByte(ref data) % 5);
            var nameLength = FuzzInput.ReadByte(ref data) % 4;
            layers[i] = (layerWidth, layerHeight, offset, mode, nameLength == 0 ? null : new string((char)('a' + i), nameLength));
        }

        foreach (var layer in layers)
        {
            using var image = CreateLayerImage(format, layer.Width, layer.Height, data);
            document.AddLayer(image, layer.Offset, layer.Mode, layer.Name);
            data = data.Length > 1 ? data[1..] : data;
        }

        using var stream = new MemoryStream();
        document.Save(stream, encoder);
        var encoded = stream.ToArray();
        using var decoded = JpegXlLayeredImage.Load(encoded, JpegXlLayerConfiguration);
        FuzzAssert.Equal(document.Size, decoded.Size, "The size of the canvas");
        FuzzAssert.Equal(count, decoded.Layers.Count, "The number of layers");
        for (var i = 0; i < count; i++)
        {
            var (expected, actual) = (document.Layers[i], decoded.Layers[i]);
            FuzzAssert.Equal((expected.Offset, expected.BlendMode, expected.Name, expected.Image.Size), (actual.Offset, actual.BlendMode, actual.Name, actual.Image.Size), $"The description of layer {i}");
            if (!lossy)
            {
                FuzzAssert.True(GetRgba64Bytes(expected.Image).AsSpan().SequenceEqual(GetRgba64Bytes(actual.Image)), $"The samples of the lossless layer {i}");
            }
        }

        using var composite = Image.Load(encoded, new ImageDecodeOptions { Configuration = JpegXlLayerConfiguration });
        FuzzAssert.Equal((document.Size, 1), (composite.Size, composite.Frames.Count), "The image the layers make");
    }

    /// <summary>Reads the layers of any input; the layers that are read are written again losslessly and read back the same.</summary>
    private static void JpegXlLayersLoadTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        JpegXlLayeredImage? document = null;
        try
        {
            try
            {
                document = JpegXlLayeredImage.Load(input, JpegXlLayerConfiguration);
            }
            catch (Exception exception) when (IsDocumented(exception))
            {
                return;
            }

            FuzzAssert.True(document.Layers.Count > 0, "A layered image has a layer");
            if (document.Layers.Any(layer => PixelFormats.IsFloatingPoint(layer.Image.PixelFormat)))
                return;

            using var stream = new MemoryStream();
            document.Save(stream, new JpegXlEncoder { Effort = input.Length % 4, MetadataHandling = MetadataHandling.Strip });
            using var decoded = JpegXlLayeredImage.Load(stream.ToArray(), JpegXlLayerConfiguration);
            FuzzAssert.Equal((document.Size, document.Layers.Count), (decoded.Size, decoded.Layers.Count), "The canvas and the layers written again");
            for (var i = 0; i < document.Layers.Count; i++)
            {
                var (expected, actual) = (document.Layers[i], decoded.Layers[i]);
                FuzzAssert.Equal((expected.Offset, expected.BlendMode, expected.Name, expected.Image.Size), (actual.Offset, actual.BlendMode, actual.Name, actual.Image.Size), $"The description of layer {i} written again");
                FuzzAssert.True(GetRgba64Bytes(expected.Image).AsSpan().SequenceEqual(GetRgba64Bytes(actual.Image)), $"The samples of layer {i} written again");
            }
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>Writes the frames of an image (<see cref="CreateImage"/>, integer pixel formats) as the regions that change, and decodes them exactly.</summary>
    private static void JpegXlOptimizedAnimationTarget(ReadOnlySpan<byte> data)
    {
        var effort = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 24);
        if (PixelFormats.IsFloatingPoint(image.PixelFormat) || image.PixelFormat == PixelFormat.Bgr565)
            return;

        image.RemovePosterFrame();
        foreach (var frame in image.Frames)
        {
            frame.Metadata.Duration = new FrameDuration(1, 10);
        }

        using var stream = new MemoryStream();
        image.Save(stream, new JpegXlEncoder { OptimizeFrames = true, Effort = effort % 10, Progressive = (effort & 0x10) != 0 });
        using var decoded = Image.Load(stream.ToArray());
        FuzzAssert.Equal((image.Size, image.Frames.Count), (decoded.Size, decoded.Frames.Count), "The size and the frames of the animation");
        using var expected = image.CloneAs<Rgba64>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        using var actual = decoded.CloneAs<Rgba64>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        for (var i = 0; i < image.Frames.Count; i++)
        {
            FuzzAssert.True(GetFrameBytes(expected, i).AsSpan().SequenceEqual(GetFrameBytes(actual, i)), $"The samples of frame {i}");
        }
    }

    private static Image CreateLayerImage(PixelFormat format, int width, int height, ReadOnlySpan<byte> pixels) => format switch
    {
        PixelFormat.Rgba32 => Create<Rgba32>(pixels, width, height),
        PixelFormat.Rgb24 => Create<Rgb24>(pixels, width, height),
        PixelFormat.Gray8 => Create<Gray8>(pixels, width, height),
        PixelFormat.Gray16 => Create<Gray16>(pixels, width, height),
        PixelFormat.Rgba64 => Create<Rgba64>(pixels, width, height),
        PixelFormat.Bgra32 => Create<Bgra32>(pixels, width, height),
        _ => throw new InvalidOperationException("The format is not listed."),
    };

    /// <summary>Gets the samples of an image as 16-bit RGBA, for a comparison: a gray profile is dropped from the copy.</summary>
    private static byte[] GetRgba64Bytes(Image image)
    {
        using var converted = image.CloneAs<Rgba64>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        return GetFrameBytes(converted, 0);
    }

    private static byte[] GetFrameBytes(Image image, int frame)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[frame].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetJpegXlLayersSeeds()
    {
        // The cases of the unit tests: layers at negative offsets and beyond the canvas with every mode and names, progressive
        // and lossy layers, gray and 16-bit layers
        yield return new FuzzSeed("every-mode", (byte[])[0, 0x05, 30, 20, 4, 30, 20, 0, 0, 0, 3, 40, 30, 0xF6, 0xFB, 2, 1, 29, 27, 6, 2, 1, 0, 7, 3, 29, 19, 4, 2, 10, 200, 30, 0, 255, 128]);
        yield return new FuzzSeed("progressive", (byte[])[1, 0x10, 14, 9, 2, 14, 9, 0, 0, 0, 0, 30, 2, 0xB0, 3, 2, 3, 1, 2, 3, 4, 5, 6, 7]);
        yield return new FuzzSeed("lossy", (byte[])[0, 0x65, 12, 8, 2, 12, 8, 0, 0, 0, 2, 9, 7, 0xF9, 5, 2, 1, 90, 180, 45, 255, 0, 17]);
        yield return new FuzzSeed("gray16-add", (byte[])[3, 0x03, 4, 4, 2, 4, 4, 0, 0, 0, 0, 2, 2, 1, 1, 1, 0, 100, 0, 200, 3]);
        yield return new FuzzSeed("bgra-multiply", (byte[])[5, 0x07, 6, 6, 2, 6, 6, 0, 0, 0, 0, 3, 3, 2, 2, 4, 1, 10, 20, 30, 255, 128, 64]);
    }

    private static IEnumerable<FuzzSeed> GetJpegXlLayersLoadSeeds()
    {
        // The layered files of the cases of the unit tests, then the JPEG XL files of the golden corpus
        foreach (var seed in GetJpegXlLayersSeeds())
        {
            byte[] file;
            try
            {
                file = EncodeLayersSeed(seed.Data.Span);
            }
            catch (Exception exception) when (IsDocumented(exception))
            {
                continue;
            }

            yield return new FuzzSeed("layers-" + seed.Name, file);
        }

        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/jxl/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name["Seeds/jxl/".Length..], memory.ToArray());
        }
    }

    /// <summary>Writes the layers that an input of <see cref="JpegXlLayersTarget"/> describes, without checking them.</summary>
    private static byte[] EncodeLayersSeed(ReadOnlySpan<byte> data)
    {
        var format = JpegXlLayerFormats[FuzzInput.ReadByte(ref data) % JpegXlLayerFormats.Length];
        var settings = FuzzInput.ReadByte(ref data);
        var width = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var count = 1 + (FuzzInput.ReadByte(ref data) % 3);
        using var document = new JpegXlLayeredImage(width, height);
        var layers = new (int Width, int Height, Point Offset, JpegXlBlendMode Mode)[count];
        for (var i = 0; i < count; i++)
        {
            layers[i] = (1 + (FuzzInput.ReadByte(ref data) % 48), 1 + (FuzzInput.ReadByte(ref data) % 48), new Point((sbyte)FuzzInput.ReadByte(ref data) % 50, (sbyte)FuzzInput.ReadByte(ref data) % 50), (JpegXlBlendMode)(FuzzInput.ReadByte(ref data) % 5));
            _ = FuzzInput.ReadByte(ref data);
        }

        foreach (var layer in layers)
        {
            using var image = CreateLayerImage(format, layer.Width, layer.Height, data);
            document.AddLayer(image, layer.Offset, layer.Mode, "layer");
        }

        using var stream = new MemoryStream();
        document.Save(stream, new JpegXlEncoder { Compression = (settings & 0x20) != 0 ? JpegXlCompression.Lossy : JpegXlCompression.Lossless, Progressive = (settings & 0x10) != 0 });
        return stream.ToArray();
    }

    private static IEnumerable<FuzzSeed> GetJpegXlOptimizedAnimationSeeds()
    {
        // The cases of the unit tests: animations of three frames that differ, in the pixel formats the encoder stores and in
        // the ones it widens, progressive or not
        yield return new FuzzSeed("rgba", (byte[])[5, 0, 20, 16, 2, 10, 20, 30, 255, 40, 50, 60, 0]);
        yield return new FuzzSeed("rgb-progressive", (byte[])[0x13, 1, 12, 9, 2, 1, 2, 3, 200, 100, 50]);
        yield return new FuzzSeed("gray16", (byte[])[7, 3, 23, 5, 2, 0, 1, 255, 255, 128, 0]);
        yield return new FuzzSeed("la16", (byte[])[2, 5, 6, 6, 2, 10, 255, 20, 0]);
        yield return new FuzzSeed("rgba64-single-frame", (byte[])[9, 4, 3, 3, 0, 1, 2, 3, 4, 5, 6, 7, 8]);
    }
}
