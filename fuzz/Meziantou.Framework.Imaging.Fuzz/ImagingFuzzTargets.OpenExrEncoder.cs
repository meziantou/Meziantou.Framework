using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    // The pixel formats an OpenEXR encoder receives: floating-point pixels, and integer ones written as their fractions
    private static readonly PixelFormat[] OpenExrFormats = [PixelFormat.RgbaFloat, PixelFormat.RgbaHalf, PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray16, PixelFormat.Rgba64];

    /// <summary>
    /// <see cref="OpenExrEncoder"/> with settings from the fuzzer: a byte of compression, a byte of tiling (none, or a level
    /// mode and a rounding), a byte of tile width and one of tile height (1 to 32), a byte of line order, a byte of sample type,
    /// a byte of DWA level, a byte of pixel format, a byte of width and one of height (1 to 24), then the bytes of the pixels,
    /// repeated when the input is shorter. An encoding fails only for the samples the documentation lists (beyond the range of
    /// the sample type), and the same image encodes to the same bytes again. The file is identified and loaded at the size of the
    /// image; a lossless compression (PXR24 of half samples and B44 of float samples included) decodes to the pixels of the
    /// uncompressed scan-line file, whatever the tiles, the levels and the line order, and DWA keeps the alpha of that file.
    /// </summary>
    private static void OpenExrEncoderTarget(ReadOnlySpan<byte> data)
    {
        var compression = (OpenExrCompression)(FuzzInput.ReadByte(ref data) % 10);
        var tilingByte = FuzzInput.ReadByte(ref data);
        var tileWidth = 1 + (FuzzInput.ReadByte(ref data) % 32);
        var tileHeight = 1 + (FuzzInput.ReadByte(ref data) % 32);
        var lineOrder = (OpenExrLineOrder)(FuzzInput.ReadByte(ref data) % 3);
        var pixelType = (FuzzInput.ReadByte(ref data) % 3) switch
        {
            0 => (OpenExrPixelType?)null,
            1 => OpenExrPixelType.Half,
            _ => OpenExrPixelType.Float,
        };
        var level = (float)FuzzInput.ReadByte(ref data);
        var format = OpenExrFormats[FuzzInput.ReadByte(ref data) % OpenExrFormats.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % 24);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 24);
        var tiling = (tilingByte & 3) == 0 ? null : new OpenExrTiling
        {
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            LevelMode = (OpenExrLevelMode)((tilingByte & 3) - 1),
            LevelRounding = (OpenExrLevelRounding)((tilingByte >> 2) & 1),
        };

        using Image image = format switch
        {
            PixelFormat.RgbaFloat => Create<RgbaFloat>(data, width, height),
            PixelFormat.RgbaHalf => Create<RgbaHalf>(data, width, height),
            PixelFormat.Rgba32 => Create<Rgba32>(data, width, height),
            PixelFormat.Rgb24 => Create<Rgb24>(data, width, height),
            PixelFormat.Gray16 => Create<Gray16>(data, width, height),
            PixelFormat.Rgba64 => Create<Rgba64>(data, width, height),
            _ => throw new InvalidOperationException("The format is not listed."),
        };
        image.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;

        var encoder = new OpenExrEncoder { Compression = compression, Tiling = tiling, LineOrder = lineOrder, PixelType = pixelType, AllowBitDepthReduction = true, DwaCompressionLevel = level, MetadataHandling = MetadataHandling.Strip };
        if (tiling is null && lineOrder == OpenExrLineOrder.RandomY)
        {
            FuzzAssert.True(Rejects<ArgumentException>(image, encoder), "A random order of scan lines is accepted");
            return;
        }

        var reference = new OpenExrEncoder { Compression = OpenExrCompression.None, PixelType = pixelType, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
        byte[] file;
        byte[] uncompressed;
        try
        {
            uncompressed = Save(image, reference);
        }
        catch (UnsupportedImageFeatureException)
        {
            // Samples beyond the range of the sample type: every compression and tiling rejects them
            FuzzAssert.True(Rejects<UnsupportedImageFeatureException>(image, encoder), "The settings accept samples that the uncompressed file rejects");
            return;
        }

        file = Save(image, encoder);
        FuzzAssert.True(file.AsSpan().SequenceEqual(Save(image, encoder)), "The same image encodes to other bytes");
        var info = Image.Identify(file, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        FuzzAssert.Equal(ImageFormat.OpenExr, info.Format, "The identified format");
        FuzzAssert.Equal(image.Size, info.Size, "The identified size");

        using var decoded = Image.Load<RgbaFloat>(file);
        using var expected = Image.Load<RgbaFloat>(uncompressed);
        FuzzAssert.Equal(image.Size, decoded.Size, "The decoded size");
        var storesHalf = (pixelType ?? (format is PixelFormat.RgbaHalf or PixelFormat.Rgba32 or PixelFormat.Rgb24 ? OpenExrPixelType.Half : OpenExrPixelType.Float)) == OpenExrPixelType.Half;
        var lossless = compression <= OpenExrCompression.Piz
            || (compression == OpenExrCompression.Pxr24 && storesHalf)
            || (compression is OpenExrCompression.B44 or OpenExrCompression.B44A && !storesHalf);
        var actualBytes = GetBytes(decoded);
        var expectedBytes = GetBytes(expected);
        if (lossless)
        {
            FuzzAssert.True(actualBytes.AsSpan().SequenceEqual(expectedBytes), $"The {compression} file decodes to other pixels than the uncompressed file");
        }
        else if (compression is OpenExrCompression.Dwaa or OpenExrCompression.Dwab)
        {
            for (var i = 12; i < actualBytes.Length; i += 16)
            {
                FuzzAssert.True(actualBytes.AsSpan(i, 4).SequenceEqual(expectedBytes.AsSpan(i, 4)), $"The alpha of the pixel {i / 16} of the DWA file differs");
            }
        }
    }

    private static bool Rejects<TException>(Image image, ImageEncoder encoder)
        where TException : Exception
    {
        try
        {
            Save(image, encoder);
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static IEnumerable<FuzzSeed> GetOpenExrEncoderSeeds()
    {
        // The settings of the unit tests: every compression, scan lines and tiles, the three level modes and line orders
        byte[] pixels = [0, 60, 120, 255, 7, 200, 13, 128, 64, 1, 90, 33, 250, 17, 180, 99];
        for (var compression = 0; compression < 10; compression++)
        {
            yield return new FuzzSeed($"lines-{compression}", (byte[])[(byte)compression, 0, 0, 0, 1, 1, 45, 1, 9, 6, .. pixels]);
            yield return new FuzzSeed($"tiles-{compression}", (byte[])[(byte)compression, 1, 4, 3, 0, 0, 45, 0, 13, 7, .. pixels]);
        }

        yield return new FuzzSeed("mipmap-round-down", (byte[])[3, 2, 7, 5, 1, 2, 0, 0, 19, 11, .. pixels]);
        yield return new FuzzSeed("ripmap-round-up", (byte[])[4, 7, 2, 3, 2, 1, 0, 1, 10, 23, .. pixels]);
        yield return new FuzzSeed("dwab-gray-ripmap", (byte[])[9, 3, 8, 8, 0, 2, 200, 4, 17, 17, .. pixels]);
        yield return new FuzzSeed("b44a-mipmap-half", (byte[])[7, 6, 4, 4, 1, 1, 0, 1, 8, 8, .. pixels]);
        yield return new FuzzSeed("random-lines", (byte[])[3, 0, 0, 0, 2, 0, 0, 0, 2, 2, .. pixels]);
    }
}
