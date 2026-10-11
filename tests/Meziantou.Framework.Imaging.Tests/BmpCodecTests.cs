using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Bmp;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// BMP decoding and encoding through the public APIs, on files assembled byte by byte with literal expected pixels:
/// the supported DIB headers and the rejected ones, every bit depth, row padding and order, palettes, bit fields, the
/// difference between a real alpha mask and unspecified padding, the resolution field, limits, truncation, bounded
/// streaming memory, and the encoder's layout, alpha, precision and metadata policies. The corpus conformance tests cover
/// the same codec against FFmpeg.
/// </summary>
public sealed class BmpCodecTests
{
    private const uint BiRgb = 0;
    private const uint BiRle8 = 1;
    private const uint BiRle4 = 2;
    private const uint BiBitFields = 3;
    private const uint BiJpeg = 4;
    private const uint BiPng = 5;

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new BmpEncoder();
        Assert.Equal(ImageFormat.Bmp, encoder.Format);
        Assert.Equal(BmpPixelLayout.Auto, encoder.PixelLayout);
        Assert.Null(encoder.BackgroundColor);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { PixelLayout = (BmpPixelLayout)42 });
        Assert.Throws<ArgumentException>(() => new BmpEncoder { BackgroundColor = new Rgba64(0, 0, 0, 1) });
    }

    [Fact]
    public void BottomUpAndTopDownRowsProduceTheSameImage()
    {
        // Three pixels per row: nine bytes padded to twelve, and the padding is ignored
        byte[][] rows =
        [
            [0x03, 0x02, 0x01, 0x06, 0x05, 0x04, 0x09, 0x08, 0x07, 0xAA, 0xBB, 0xCC],
            [0x13, 0x12, 0x11, 0x16, 0x15, 0x14, 0x19, 0x18, 0x17, 0xDD, 0xEE, 0xFF],
        ];

        Rgba32[] expected =
        [
            new(1, 2, 3), new(4, 5, 6), new(7, 8, 9),
            new(0x11, 0x12, 0x13), new(0x14, 0x15, 0x16), new(0x17, 0x18, 0x19),
        ];

        Assert.Equal(expected, Decode(Bmp(3, 2, 24, [rows[1], rows[0]])));
        Assert.Equal(expected, Decode(Bmp(3, 2, 24, rows, topDown: true)));
    }

    [Fact]
    public void IndexedLayoutsExpandThroughThePalette()
    {
        (byte R, byte G, byte B)[] palette = [(10, 20, 30), (200, 150, 100), (0, 0, 0), (255, 255, 255)];

        // 1-bit: the most significant bit is the left pixel
        Assert.Equal(
            [new(10, 20, 30), new(200, 150, 100), new(200, 150, 100), new(10, 20, 30), new(200, 150, 100)],
            Decode(Bmp(5, 1, 1, [[0b0110_1000, 0, 0, 0]], palette[..2])));

        // 4-bit: the high nibble is the left pixel, the last nibble of an odd width is unused
        Assert.Equal(
            [new(0, 0, 0), new(255, 255, 255), new(10, 20, 30)],
            Decode(Bmp(3, 1, 4, [[0x23, 0x0F, 0, 0]], palette)));

        // 8-bit
        Assert.Equal(
            [new(255, 255, 255), new(10, 20, 30)],
            Decode(Bmp(2, 1, 8, [[3, 0, 0, 0]], palette)));

        // An index outside the palette is malformed, never clamped
        var invalid = Assert.Throws<InvalidImageContentException>(() => Image.Load(Bmp(2, 1, 8, [[4, 0, 0, 0]], palette)));
        Assert.Equal(ImageFormat.Bmp, invalid.Format);
        Assert.Contains("palette index 4", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SixteenBitChannelsAreExpandedWithTheExactRatio()
    {
        // Implicit 5-5-5: 0 maps to 0, 31 to 255, and the values in between are round(value * 255 / 31)
        var data = Bmp(4, 1, 16, [[0x00, 0x00, 0xFF, 0x7F, 0x00, 0x7C, 0x1F, 0x00]]);
        Assert.Equal([new(0, 0, 0), new(255, 255, 255), new(255, 0, 0), new(0, 0, 255)], Decode(data));
        Assert.Equal(66, Decode(Bmp(1, 1, 16, [[0x00, 0x01, 0, 0]]))[0].G); // green 8 of 31: round(8 * 255 / 31)

        // Explicit 5-6-5 masks after a 40-byte header
        var masks = Bmp(2, 1, 16, [[0x00, 0xF8, 0xE0, 0x07]], compression: BiBitFields, masks: [0xF800, 0x07E0, 0x001F]);
        Assert.Equal([new(255, 0, 0), new(0, 255, 0)], Decode(masks));
    }

    [Fact]
    public void AlphaIsDecodedOnlyWhenAMaskDeclaresIt()
    {
        byte[][] rows = [[0x10, 0x20, 0x30, 0x40, 0x11, 0x22, 0x33, 0x00]];

        // 32-bit BI_RGB: the fourth byte is unspecified padding
        var opaque = Bmp(2, 1, 32, rows);
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(opaque).PixelFormat);
        Assert.Equal([new(0x30, 0x20, 0x10), new(0x33, 0x22, 0x11)], Decode(opaque));

        // BITMAPV4HEADER with an alpha mask: the same bytes are transparency
        var alpha = Bmp(2, 1, 32, rows, compression: BiBitFields, masks: [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000], headerLength: 108);
        var info = Image.Identify(alpha);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.True(info.MayHaveTransparency);
        Assert.Equal([new(0x30, 0x20, 0x10, 0x40), new(0x33, 0x22, 0x11, 0x00)], Decode(alpha));

        // A one-bit alpha mask expands to 0 or 255
        var oneBit = Bmp(2, 1, 16, [[0x00, 0x80, 0x00, 0x00]], compression: BiBitFields, masks: [0x7C00, 0x03E0, 0x001F, 0x8000], headerLength: 56);
        Assert.Equal([new(0, 0, 0, 255), new(0, 0, 0, 0)], Decode(oneBit));
    }

    [Theory]
    [InlineData(12, "BITMAPCOREHEADER")]
    [InlineData(64, "BITMAPCOREHEADER2")]
    [InlineData(16, "OS/2")]
    public void Os2HeadersAreRecognizedAndRejected(int headerLength, string expected)
    {
        var data = Bmp(2, 1, 24, [[1, 2, 3, 4, 5, 6]]);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), (uint)headerLength);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal(ImageFormat.Bmp, exception.Format);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BiRle8, "run-length")]
    [InlineData(BiRle4, "run-length")]
    [InlineData(BiJpeg, "JPEG")]
    [InlineData(BiPng, "PNG")]
    public void CompressedVariantsAreRecognizedAndRejected(uint compression, string expected)
    {
        var data = Bmp(2, 1, 8, [[0, 1, 0, 0]], [(0, 0, 0), (1, 1, 1)], compression: compression);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedHeadersAndMasksAreRejected()
    {
        var valid = Bmp(2, 1, 24, [[1, 2, 3, 4, 5, 6]]);

        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 18, [0, 0, 0, 0]))); // width 0
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 22, [0, 0, 0, 0]))); // height 0
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 22, [0, 0, 0, 0x80]))); // height int.MinValue
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 26, [2, 0]))); // two planes
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 28, [7, 0]))); // bit count 7
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 30, [12, 0, 0, 0]))); // undefined compression
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(valid, 10, [40, 0, 0, 0]))); // pixel data inside the header
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Edit(valid, 28, [2, 0]))); // Windows CE 2 bits per pixel

        // Masks: non-contiguous, overlapping, zero and wider than eight bits
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bmp(1, 1, 16, [[0, 0, 0, 0]], compression: BiBitFields, masks: [0xF810, 0x07E0, 0x001F])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bmp(1, 1, 16, [[0, 0, 0, 0]], compression: BiBitFields, masks: [0xF800, 0xF800, 0x001F])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bmp(1, 1, 16, [[0, 0, 0, 0]], compression: BiBitFields, masks: [0xF800, 0, 0x001F])));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Bmp(1, 1, 32, [[0, 0, 0, 0]], compression: BiBitFields, masks: [0x3FF00000, 0x000FFC00, 0x000003FF])));

        // Masks are only defined for 16 and 32 bits per pixel
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bmp(1, 1, 24, [[0, 0, 0, 0]], compression: BiBitFields, masks: [0xFF0000, 0x00FF00, 0x0000FF])));
    }

    [Fact]
    public void TheGapBeforeThePixelDataIsSkipped()
    {
        var data = Bmp(2, 1, 8, [[1, 0, 0, 0]], [(9, 9, 9), (7, 8, 9)], gap: 5);
        Assert.Equal([new(7, 8, 9), new(9, 9, 9)], Decode(data));
    }

    [Fact]
    public void TheResolutionRoundTrips()
    {
        var data = Bmp(1, 1, 24, [[1, 2, 3, 0]], pixelsPerMeter: (2835, 5670));
        var resolution = Assert.IsType<ImageResolution>(Image.Identify(data).Metadata.Resolution);
        Assert.Equal(2835 * 0.0254, resolution.HorizontalDpi);
        Assert.Equal(5670 * 0.0254, resolution.VerticalDpi);

        using var image = Image.Load(data);
        using var stream = new MemoryStream();
        image.Save(stream, new BmpEncoder());
        var written = ReferenceBmp.Parse(stream.ToArray());
        Assert.Equal((2835, 5670), written.PixelsPerMeter);

        // A zero or negative field is not a resolution
        Assert.Null(Image.Identify(Bmp(1, 1, 24, [[1, 2, 3, 0]], pixelsPerMeter: (0, 5670))).Metadata.Resolution);
    }

    [Fact]
    public void TruncationAtEveryByteIsReported()
    {
        var data = Bmp(3, 2, 24, [[1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0, 0], [9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0, 0]]);
        for (var length = 0; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();
            var exception = Record.Exception(() => Image.Load(prefix).Dispose());
            Assert.True(exception is InvalidImageContentException or UnknownImageFormatException, $"{length} bytes: {exception?.GetType().Name ?? "no exception"}");
        }
    }

    [Fact]
    public void LimitsAreCheckedFromTheHeader()
    {
        var data = Bmp(8, 4, 24, [.. Enumerable.Repeat(new byte[24], 4)]);
        var options = new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 4 } } };
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = options.Configuration })).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, options)).Kind);
    }

    [Fact]
    public async Task StreamingKeepsTwoRowsOfDecoderStateAndNeverBuffersTheFile()
    {
        using var source = CreateImage(512, 256, seed: 7);
        using var opaque = source.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        using var memory = new MemoryStream();
        opaque.Save(memory, new BmpEncoder());
        var data = memory.ToArray();
        await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1024 };
        using var image = await Image.LoadAsync<Rgb24>(stream, cancellationToken: XunitCancellationToken);
        var scope = image.Owner.Scope;
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.True(state < 64 * 1024, $"Peak decoder state and input buffer {state} bytes for a {data.Length}-byte input.");
    }

    [Theory]
    [InlineData(PixelFormat.Rgb24, 24)]
    [InlineData(PixelFormat.Gray8, 24)]
    [InlineData(PixelFormat.Rgba32, 32)]
    [InlineData(PixelFormat.Bgra32, 32)]
    public void TheLayoutFollowsThePixelFormat(PixelFormat format, int expectedBitsPerPixel)
    {
        using var source = CreateImage(5, 3, seed: (int)format);
        using var image = ConvertTo(source, format);
        var reference = ReferenceBmp.Parse(Save(image, new BmpEncoder()));
        Assert.Equal(expectedBitsPerPixel, reference.BitsPerPixel);
        Assert.Equal(expectedBitsPerPixel == 32 ? 108 : 40, reference.HeaderLength);
        Assert.Equal(expectedBitsPerPixel == 32, reference.HasAlphaMask);
        Assert.False(reference.IsTopDown);
    }

    [Fact]
    public void DiscardingAlphaNeedsABackgroundColor()
    {
        using var image = CreateImage(4, 2, seed: 3);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24 }));
        Assert.Equal("Alpha removal", exception.Feature);

        var flattened = ReferenceBmp.Parse(Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24, BackgroundColor = new Rgba64(0, 0, 0) }));
        Assert.Equal(24, flattened.BitsPerPixel);

        // An opaque image never needs one
        using var opaque = image.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        Assert.Equal(24, ReferenceBmp.Parse(Save(opaque, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24 })).BitsPerPixel);
    }

    [Fact]
    public void SixteenBitSourcesNeedExplicitBitDepthReduction()
    {
        using var source = CreateImage(4, 2, seed: 11);
        using var wide = source.CloneAs<Rgba64>();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(wide, new BmpEncoder()));
        Assert.Equal("Bit depth reduction", exception.Feature);
        Assert.Equal(32, ReferenceBmp.Parse(Save(wide, new BmpEncoder { AllowBitDepthReduction = true })).BitsPerPixel);
    }

    [Fact]
    public void AnimationsAndUnsupportedMetadataAreRejected()
    {
        using var image = CreateImage(3, 2, seed: 5);
        image.AppendFrame();
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new BmpEncoder()));

        using var still = CreateImage(3, 2, seed: 5);
        still.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "hello"));
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(still, new BmpEncoder()));
        Assert.NotEmpty(Save(still, new BmpEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
        Assert.NotEmpty(Save(still, new BmpEncoder { MetadataHandling = MetadataHandling.Strip }));
    }

    [Fact]
    public void EncodedFilesRoundTripThroughTheLibrary()
    {
        using var source = CreateImage(17, 9, seed: 2);
        foreach (var layout in new[] { BmpPixelLayout.Auto, BmpPixelLayout.Bgra32 })
        {
            using var decoded = Image.Load<Rgba32>(Save(source, new BmpEncoder { PixelLayout = layout }));
            Assert.Equal(GetPixels(source.Frames[0]), GetPixels(decoded.Frames[0]));
        }
    }

    private static Image ConvertTo(Image<Rgba32> source, PixelFormat format)
    {
        var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) };
        return format switch
        {
            PixelFormat.Rgba32 => source.Clone(),
            PixelFormat.Bgra32 => source.CloneAs<Bgra32>(),
            PixelFormat.Rgba64 => source.CloneAs<Rgba64>(),
            PixelFormat.Rgb24 => source.CloneAs<Rgb24>(flatten),
            PixelFormat.Gray8 => source.CloneAs<Gray8>(flatten),
            _ => source.CloneAs<Gray16>(flatten),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateImage(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 3) + y) % 4) switch
                {
                    0 => new Rgba32(30, 60, 90, 255),
                    1 => new Rgba32((byte)(x * 5), (byte)(y * 7), (byte)(x + y), 255),
                    2 => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                    _ => new Rgba32((byte)random.Next(256), (byte)x, (byte)y, 0),
                };
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static byte[] Save(Image image, BmpEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static Rgba32[] Decode(byte[] data)
    {
        using var image = Image.Load<Rgba32>(data);
        return GetPixels(image.Frames[0]);
    }

    private static Rgba32[] GetPixels(ImageFrame<Rgba32> frame)
    {
        var pixels = new Rgba32[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static byte[] Edit(byte[] data, int offset, byte[] replacement)
    {
        var copy = (byte[])data.Clone();
        replacement.CopyTo(copy, offset);
        return copy;
    }

    /// <summary>Assembles a BMP file; <paramref name="rows"/> are the stored rows, in storage order.</summary>
    private static byte[] Bmp(int width, int height, int bitsPerPixel, byte[][] rows, (byte R, byte G, byte B)[]? palette = null,
        uint compression = BiRgb, uint[]? masks = null, int headerLength = 40, bool topDown = false, int gap = 0,
        (int X, int Y)? pixelsPerMeter = null)
    {
        palette ??= [];
        var maskBytes = headerLength == 40 && compression == BiBitFields ? 12 : 0;
        var paletteBytes = palette.Length * 4;
        var body = rows.SelectMany(row => row).ToArray();
        var offset = 14 + headerLength + maskBytes + paletteBytes + gap;
        var data = new byte[offset + body.Length];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), (uint)offset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), (uint)headerLength);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), topDown ? -height : height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)bitsPerPixel);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(30), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(34), (uint)body.Length);
        if (pixelsPerMeter is { } resolution)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(38), resolution.X);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(42), resolution.Y);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(46), (uint)palette.Length);
        if (masks is not null)
        {
            var position = headerLength >= 52 ? 54 : 14 + headerLength;
            for (var i = 0; i < masks.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position + (i * 4)), masks[i]);
            }

            if (headerLength >= 108)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(70), 0x73524742); // LCS_sRGB
            }
        }

        for (var i = 0; i < palette.Length; i++)
        {
            var entry = 14 + headerLength + maskBytes + (i * 4);
            data[entry] = palette[i].B;
            data[entry + 1] = palette[i].G;
            data[entry + 2] = palette[i].R;
        }

        body.CopyTo(data, offset);
        return data;
    }
}
