using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Bmp;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// BMP decoding and encoding through the public APIs, on files assembled byte by byte with literal expected pixels:
/// the Windows and OS/2 DIB headers and the rejected ones, every bit depth, row padding and order, palettes, bit fields,
/// run-length streams and the pixels they skip, the difference between a real alpha mask and unspecified padding, the
/// resolution field, limits, truncation, bounded streaming memory, and the encoder's layout, alpha, precision and metadata
/// policies. The corpus conformance tests cover
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
    private const uint BcaHuffman1D = 3;
    private const uint BcaRle24 = 4;

    // The first entry is not black: the pixels a run-length stream skips are palette index 0
    private static readonly (byte R, byte G, byte B)[] Palette4 = [(10, 20, 30), (200, 150, 100), (0, 0, 0), (255, 255, 255)];
    private static readonly Rgba32 Color0 = new(10, 20, 30);
    private static readonly Rgba32 Color1 = new(200, 150, 100);
    private static readonly Rgba32 Color2 = new(0, 0, 0);
    private static readonly Rgba32 Color3 = new(255, 255, 255);

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new BmpEncoder();
        Assert.Equal(ImageFormat.Bmp, encoder.Format);
        Assert.Equal(BmpPixelLayout.Auto, encoder.PixelLayout);
        Assert.Null(encoder.BackgroundColor);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.Equal(BmpCompression.None, encoder.Compression);
        Assert.Equal(BmpRowOrder.BottomUp, encoder.RowOrder);
        Assert.False(encoder.AllowColorReduction);
        Assert.Equal(256, encoder.Quantization.MaxColors);
        Assert.Equal(DitheringMode.None, encoder.Quantization.Dithering);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { PixelLayout = (BmpPixelLayout)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { Compression = (BmpCompression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { RowOrder = (BmpRowOrder)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { Quantization = new QuantizeOptions { Dithering = (DitheringMode)99 } });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { Quantization = new QuantizeOptions { MaxColors = 1 } });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { Quantization = new QuantizeOptions { MaxColors = 257 } });
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

    [Fact]
    public void Os2CoreHeadersAreDecoded()
    {
        // BITMAPCOREHEADER: 16-bit dimensions, BGR rows, always bottom-up, no resolution
        var data = CoreBmp(2, 2, 24, [[1, 2, 3, 4, 5, 6, 0, 0], [7, 8, 9, 10, 11, 12, 0, 0]]);
        Assert.Equal([new(9, 8, 7), new(12, 11, 10), new(3, 2, 1), new(6, 5, 4)], Decode(data));
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Null(info.Metadata.Resolution);

        // The palette entries are three bytes each (RGBTRIPLE), not four
        Assert.Equal([Color1, Color0, Color1], Decode(CoreBmp(3, 1, 1, [[0b1010_0000, 0, 0, 0]], Palette4[..2])));

        // The header has no color count: a palette shorter than the bit count allows ends where the pixel data starts
        var shortPalette = CoreBmp(3, 1, 8, [[2, 1, 0, 0]], Palette4[..3], gap: 2);
        Assert.Equal([Color2, Color1, Color0], Decode(shortPalette));
        var invalid = Assert.Throws<InvalidImageContentException>(() => Image.Load(CoreBmp(3, 1, 8, [[3, 1, 0, 0]], Palette4[..3])));
        Assert.Equal(ImageFormat.Bmp, invalid.Format);
        Assert.Contains("palette index 3", invalid.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(data, 18, [0, 0]))); // width 0
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(data, 20, [0, 0]))); // height 0
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(data, 22, [2, 0]))); // two planes
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(data, 24, [16, 0]))); // 16 bits per pixel
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(shortPalette, 10, [28, 0, 0, 0]))); // no room for one palette entry
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(24, false)]
    [InlineData(28, false)]
    [InlineData(32, true)]
    [InlineData(36, true)]
    [InlineData(48, true)]
    [InlineData(64, true)]
    public void Os2V2HeadersOfEveryLengthAreDecoded(int headerLength, bool storesResolution)
    {
        // BITMAPCOREHEADER2 may stop after any field that follows the bit count: the fields it leaves out are zero
        var data = Os2Bmp(headerLength, 2, 1, 24, [1, 2, 3, 4, 5, 6, 0, 0], pixelsPerMeter: (2835, 5670));
        Assert.Equal([new(3, 2, 1), new(6, 5, 4)], Decode(data));
        var resolution = Image.Identify(data).Metadata.Resolution;
        Assert.Equal(storesResolution, resolution is not null);
        if (resolution is not null)
        {
            Assert.Equal(2835 * 0.0254, resolution.HorizontalDpi);
            Assert.Equal(5670 * 0.0254, resolution.VerticalDpi);
        }
    }

    [Fact]
    public void Os2V2FieldsAreHonoredAndValidated()
    {
        byte[] body = [1, 2, 3, 4, 5, 6, 0, 0];

        // The resolution is only in pixels per meter for the metric unit
        Assert.Null(Image.Identify(Os2Bmp(64, 2, 1, 24, body, pixelsPerMeter: (2835, 5670), units: 1)).Metadata.Resolution);

        // A partial palette through cclrUsed, and a full one of four-byte entries when the header stops before it
        Assert.Equal([Color1, Color0], Decode(Os2Bmp(64, 2, 1, 8, [1, 0, 0, 0], Palette4[..2])));
        var palette16 = Enumerable.Range(0, 16).Select(i => ((byte)i, (byte)(i * 2), (byte)(i * 3))).ToArray();
        Assert.Equal([new(1, 2, 3), new(15, 30, 45)], Decode(Os2Bmp(16, 2, 1, 4, [0x1F, 0, 0, 0], palette16)));

        // Run-length encoding uses the same values as a Windows header
        Assert.Equal([Color1, Color1], Decode(Os2Bmp(64, 2, 1, 8, [2, 1, 0, 1], Palette4[..2], compression: BiRle8)));

        // The values 3 and 4 are Huffman 1D and RLE24 here, not BI_BITFIELDS and BI_JPEG
        var unsupported = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Os2Bmp(64, 2, 1, 1, [0, 0, 0, 0], Palette4[..2], compression: BcaHuffman1D)));
        Assert.Equal(ImageFormat.Bmp, unsupported.Format);
        Assert.Equal("Compression: Huffman 1D", unsupported.Feature);
        Assert.Equal("Header: OS/2 recording", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Os2Bmp(64, 2, 1, 24, body, recording: 1))).Feature);
        Assert.Equal("Header: OS/2 color encoding", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Os2Bmp(64, 2, 1, 24, body, colorEncoding: uint.MaxValue))).Feature);

        Assert.Throws<InvalidImageContentException>(() => Image.Load(Os2Bmp(64, 2, 1, 24, body, compression: 5))); // undefined compression
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Os2Bmp(64, 2, 1, 16, body))); // 16 bits per pixel
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Os2Bmp(64, 2, 1, 8, [0, 1], Palette4[..2], compression: BcaRle24))); // RLE24 with 8 bits
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Edit(Os2Bmp(64, 2, 1, 24, body), 22, [0xFF, 0xFF, 0xFF, 0xFF]))); // the height is unsigned
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(65)]
    [InlineData(107)]
    [InlineData(125)]
    public void UndefinedHeaderLengthsAreRejected(int headerLength)
    {
        var data = Bmp(2, 1, 24, [[1, 2, 3, 4, 5, 6, 0, 0]]);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), (uint)headerLength);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Equal(ImageFormat.Bmp, exception.Format);
        Assert.Contains("header size", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BiJpeg, "JPEG")]
    [InlineData(BiPng, "PNG")]
    public void EmbeddedCodecsAreRecognizedAndRejected(uint compression, string expected)
    {
        var data = Bmp(2, 1, 8, [[0, 1, 0, 0]], [(0, 0, 0), (1, 1, 1)], compression: compression);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal("Compression: embedded codec", exception.Feature);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunLength8CommandsAreDecoded()
    {
        // Two runs and an end of line; an absolute run of three indexes with its padding byte and a run of one; the end
        byte[] stream =
        [
            2, 1, 2, 2, 0, 0,
            0, 3, 3, 2, 1, 0, 1, 3, 0, 0,
            0, 1,
        ];
        var data = Bmp(4, 2, 8, [stream], Palette4, compression: BiRle8);
        Assert.Equal([Color3, Color2, Color1, Color3, Color1, Color1, Color2, Color2], Decode(data));

        // An indexed image like any other: opaque, with the precision of its indexes
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
        Assert.Equal(8, info.BitsPerComponent);
        Assert.False(info.MayHaveTransparency);

        // The end of line of the last row is optional, and the bytes after the end of bitmap are not read
        byte[] withoutLastEndOfLine = [4, 1, 0, 0, 4, 2, 0, 1];
        Rgba32[] expected = [Color2, Color2, Color2, Color2, Color1, Color1, Color1, Color1];
        Assert.Equal(expected, Decode(Bmp(4, 2, 8, [withoutLastEndOfLine], Palette4, compression: BiRle8)));
        Assert.Equal(expected, Decode(Bmp(4, 2, 8, [[.. withoutLastEndOfLine, 9, 9, 9]], Palette4, compression: BiRle8)));

        // A delta of (0, 0) moves nowhere
        Assert.Equal(expected, Decode(Bmp(4, 2, 8, [[0, 2, 0, 0, .. withoutLastEndOfLine]], Palette4, compression: BiRle8)));
    }

    [Fact]
    public void RunLength4CommandsAreDecoded()
    {
        // A run alternates the two indexes of its byte, high-order first; an absolute run packs two indexes per byte
        byte[] stream =
        [
            5, 0x12, 0, 0,
            0, 3, 0x32, 0x10, 2, 0x30, 0, 0,
            0, 1,
        ];
        var data = Bmp(5, 2, 4, [stream], Palette4, compression: BiRle4);
        Assert.Equal([Color3, Color2, Color1, Color3, Color0, Color1, Color2, Color1, Color2, Color1], Decode(data));
        Assert.Equal(4, Image.Identify(data).BitsPerComponent);

        // An absolute run of five indexes that starts on an odd column: three bytes (the last half is unused) and a padding byte
        byte[] oddColumn = [1, 0x30, 0, 5, 0x01, 0x23, 0x10, 0, 0, 0, 0, 1];
        Assert.Equal([Color3, Color0, Color1, Color2, Color3, Color1], Decode(Bmp(6, 1, 4, [oddColumn], Palette4, compression: BiRle4)));
    }

    [Fact]
    public void RunLength24CommandsAreDecoded()
    {
        // Only an OS/2 2.x header defines it: runs of one BGR triplet, absolute runs padded to a 16-bit boundary
        byte[] stream =
        [
            2, 3, 2, 1, 0, 0,
            0, 3, 6, 5, 4, 9, 8, 7, 12, 11, 10, 0,
            0, 1,
        ];
        var data = Os2Bmp(64, 3, 2, 24, stream, compression: BcaRle24);

        // There is no palette: the pixel the first row skips is black
        Assert.Equal([new(4, 5, 6), new(7, 8, 9), new(10, 11, 12), new(1, 2, 3), new(1, 2, 3), new(0, 0, 0)], Decode(data));
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
    }

    [Fact]
    public void SkippedRunLengthPixelsAreTheFirstPaletteEntry()
    {
        // An early end of line, a delta to the right, a delta that skips a whole row, and an early end of bitmap
        byte[] stream =
        [
            1, 1, 0, 0,
            0, 2, 2, 0, 2, 3, 0, 0,
            0, 2, 1, 1, 1, 1,
            0, 1,
        ];
        var data = Bmp(4, 4, 8, [stream], Palette4, compression: BiRle8);
        Assert.Equal(
            [
                Color0, Color1, Color0, Color0,
                Color0, Color0, Color0, Color0,
                Color0, Color0, Color3, Color3,
                Color1, Color0, Color0, Color0,
            ],
            Decode(data));

        // Skipped pixels are a color, not transparency
        Assert.False(Image.Identify(data).MayHaveTransparency);
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(data).PixelFormat);

        // The same with 4-bit indexes: a delta of three pixels, then the second row is never started
        Assert.Equal(
            [Color0, Color0, Color0, Color0, Color0, Color0, Color0, Color0, Color1, Color2],
            Decode(Bmp(5, 2, 4, [[0, 2, 3, 0, 2, 0x12, 0, 1]], Palette4, compression: BiRle4)));

        // A stream that only holds the end of bitmap skips every pixel of every row
        Assert.All(Decode(Bmp(3, 4096, 8, [[0, 1]], Palette4, compression: BiRle8)), pixel => Assert.Equal(Color0, pixel));
    }

    [Fact]
    public void MalformedRunLengthStreamsAreRejected()
    {
        static byte[] Rle8(params byte[] stream) => Bmp(4, 2, 8, [stream], Palette4, compression: BiRle8);

        // Nothing is clamped: a run never leaves its row and a delta never leaves the canvas
        AssertInvalid(Rle8(5, 1, 0, 1), "run of 5 pixels");
        AssertInvalid(Rle8(2, 1, 0, 3, 1, 1, 1, 0, 0, 1), "absolute run of 3 pixels");
        AssertInvalid(Rle8(0, 2, 5, 0, 0, 1), "delta");
        AssertInvalid(Rle8(0, 2, 0, 2, 0, 1), "delta");

        // After the last row, only the end of bitmap is expected
        AssertInvalid(Rle8(0, 0, 0, 0, 1, 1, 0, 1), "after the last");
        AssertInvalid(Rle8(0, 0, 0, 0, 0, 0, 0, 1), "after the last");
        AssertInvalid(Rle8(0, 0, 0, 0, 0, 2, 0, 0, 0, 1), "after the last");
        AssertInvalid(Rle8(0, 0, 0, 0, 0, 3, 1, 1, 1, 0, 0, 1), "after the last");

        // The end of bitmap is required, even when every row is complete
        AssertInvalid(Rle8(4, 1, 0, 0, 4, 1, 0, 0), "truncated");
        AssertInvalid(Rle8(4, 1, 0, 0, 0, 3, 1, 1), "truncated");

        // Palette indexes are checked in runs and in absolute runs
        AssertInvalid(Rle8(4, 4, 0, 1), "palette index 4");
        AssertInvalid(Rle8(0, 3, 1, 9, 1, 0, 0, 1), "palette index 9");

        // The compression must agree with the bit count, and a run-length bitmap is always bottom-up
        AssertInvalid(Bmp(4, 2, 4, [[0, 1]], Palette4, compression: BiRle8), "run-length");
        AssertInvalid(Bmp(4, 2, 8, [[0, 1]], Palette4, compression: BiRle4), "run-length");
        AssertInvalid(Bmp(4, 2, 8, [[0, 1]], Palette4, compression: BiRle8, topDown: true), "bottom-up");

        // A run that never ends its row: every escape consumes input, so the stream ends as truncated data
        var deltas = new byte[64 * 1024];
        for (var i = 1; i < deltas.Length; i += 4)
        {
            deltas[i] = 2;
        }

        AssertInvalid(Rle8(deltas), "truncated");

        static void AssertInvalid(byte[] data, string expected)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
            Assert.Equal(ImageFormat.Bmp, exception.Format);
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RunLengthStructureIsCheckedByAFullScanAndPaletteIndexesByDecoding()
    {
        var fullScan = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };

        // A header identification stops before the stream; a full scan walks its commands
        var runPastRowEnd = Bmp(4, 2, 8, [[5, 1, 0, 1]], Palette4, compression: BiRle8);
        Assert.Equal(new Size(4, 2), Image.Identify(runPastRowEnd).Size);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(runPastRowEnd, fullScan));

        // A full scan does not expand pixels, so it accepts an index only decoding can reject
        var indexOutOfRange = Bmp(4, 2, 8, [[4, 4, 0, 1]], Palette4, compression: BiRle8);
        Assert.Equal(new Size(4, 2), Image.Identify(indexOutOfRange, fullScan).Size);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(indexOutOfRange));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task RunLengthStreamsDecodeTheSameWhateverTheReadSize(int maxBytesPerRead)
    {
        foreach (var data in RunLengthSamples())
        {
            var expected = Decode(data);
            await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = maxBytesPerRead };
            using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
            Assert.Equal(expected, GetPixels(image.Frames[0]));
        }
    }

    [Fact]
    public void RunLengthTruncationAtEveryByteIsReported()
    {
        foreach (var data in RunLengthSamples())
        {
            Image.Load(data).Dispose();
            for (var length = 0; length < data.Length; length++)
            {
                var prefix = data.AsSpan(0, length).ToArray();
                var exception = Record.Exception(() => Image.Load(prefix).Dispose());
                Assert.True(exception is InvalidImageContentException or UnknownImageFormatException, $"{length} of {data.Length} bytes: {exception?.GetType().Name ?? "no exception"}");
            }
        }
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
    public void EveryLayoutWithoutAlphaNeedsABackgroundColor()
    {
        using var image = CreateImage(4, 2, seed: 3);
        foreach (var layout in new[] { BmpPixelLayout.Indexed1, BmpPixelLayout.Indexed4, BmpPixelLayout.Indexed8, BmpPixelLayout.Bgr565, BmpPixelLayout.Bgr555 })
        {
            var encoder = new BmpEncoder { PixelLayout = layout, AllowBitDepthReduction = true, AllowColorReduction = true };
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
            Assert.Equal("Alpha removal", exception.Feature);
            Assert.Equal(0, stream.Length);

            var flattened = ReferenceBmp.Parse(Save(image, new BmpEncoder { PixelLayout = layout, AllowBitDepthReduction = true, AllowColorReduction = true, BackgroundColor = new Rgba64(0, 0, 0) }));
            Assert.False(flattened.HasAlphaMask);
        }
    }

    [Fact]
    public void ContradictorySettingsAreRejectedBeforeAnythingIsWritten()
    {
        using var image = Image.ImportPixelData<Rgb24>([new(1, 2, 3), new(4, 5, 6)], 2, 1);

        // BI_RLE8 and BI_RLE4 only exist for 8-bit and 4-bit indexes, and a run-length bitmap is always bottom-up
        foreach (var encoder in new[]
        {
            new BmpEncoder { Compression = BmpCompression.RunLength },
            new BmpEncoder { Compression = BmpCompression.RunLength, PixelLayout = BmpPixelLayout.Bgr24 },
            new BmpEncoder { Compression = BmpCompression.RunLength, PixelLayout = BmpPixelLayout.Bgra32 },
            new BmpEncoder { Compression = BmpCompression.RunLength, PixelLayout = BmpPixelLayout.Indexed1 },
            new BmpEncoder { Compression = BmpCompression.RunLength, PixelLayout = BmpPixelLayout.Bgr565, AllowBitDepthReduction = true },
            new BmpEncoder { Compression = BmpCompression.RunLength, PixelLayout = BmpPixelLayout.Indexed8, RowOrder = BmpRowOrder.TopDown },
        })
        {
            using var stream = new MemoryStream();
            Assert.Throws<ArgumentException>(() => image.Save(stream, encoder));
            Assert.Equal(0, stream.Length);
        }
    }

    [Fact]
    public void SixteenBitLayoutsReduceEachChannelToTheNearestLevel()
    {
        // (255, 128, 0): 31 and 0 on five bits, and green is round(128 * 63 / 255) = 32 on six bits or round(128 * 31 / 255) = 16 on five
        using var image = Image.ImportPixelData<Rgb24>([new(255, 128, 0), new(0, 0, 255)], 2, 1);
        foreach (var layout in new[] { BmpPixelLayout.Bgr565, BmpPixelLayout.Bgr555 })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new BmpEncoder { PixelLayout = layout }));
            Assert.Equal("Bit depth reduction", exception.Feature);
        }

        // 5-6-5: BI_BITFIELDS in a 40-byte header, followed by the three masks; rows are padded to four bytes
        var data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr565, AllowBitDepthReduction = true });
        Assert.HasCount(66 + 4, data);
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2)));
        Assert.Equal(66u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10)));
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(14)));
        Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28)));
        Assert.Equal(BiBitFields, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30)));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(34)));
        Assert.Equal(new byte[] { 0x00, 0xF8, 0, 0, 0xE0, 0x07, 0, 0, 0x1F, 0, 0, 0 }, data[54..66]);
        Assert.Equal(new byte[] { 0x00, 0xFC, 0x1F, 0x00 }, data[66..]);
        Assert.Equal([new(255, 130, 0), new(0, 0, 255)], Decode(data));

        // 5-5-5: the implicit masks of 16-bit BI_RGB, nothing between the header and the pixels
        data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr555, AllowBitDepthReduction = true });
        Assert.HasCount(54 + 4, data);
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10)));
        Assert.Equal(BiRgb, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30)));
        Assert.Equal(new byte[] { 0x00, 0x7E, 0x1F, 0x00 }, data[54..]);
        Assert.Equal([new(255, 132, 0), new(0, 0, 255)], Decode(data));
    }

    [Fact]
    public void IndexedLayoutsStoreTheColorsExactlyWhenTheyFit()
    {
        Rgb24 red = new(255, 0, 0), green = new(0, 255, 0), blue = new(0, 0, 255), white = new(255, 255, 255);
        using var image = Image.ImportPixelData<Rgb24>([red, green, red, blue, blue, white], 3, 2);

        // The palette only holds the colors of the image, sorted by red, green then blue; rows are bottom-up
        var data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8 });
        Assert.HasCount(54 + 16 + 8, data);
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2)));
        Assert.Equal(70u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10)));
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28)));
        Assert.Equal(BiRgb, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(34)));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(46)));
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 255, 255, 255, 0 }, data[54..70]);
        Assert.Equal(new byte[] { 0, 0, 3, 0, 2, 1, 2, 0 }, data[70..]);

        // Four bits: two indexes per byte, the left pixel in the high-order half
        data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4 });
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28)));
        Assert.Equal(new byte[] { 0x00, 0x30, 0, 0, 0x21, 0x20, 0, 0 }, data[70..]);

        // The same pixels come back, whatever the layout and the row order
        Rgba32[] expected = [new(255, 0, 0), new(0, 255, 0), new(255, 0, 0), new(0, 0, 255), new(0, 0, 255), new(255, 255, 255)];
        foreach (var layout in new[] { BmpPixelLayout.Indexed4, BmpPixelLayout.Indexed8 })
        {
            foreach (var rowOrder in new[] { BmpRowOrder.BottomUp, BmpRowOrder.TopDown })
            {
                data = Save(image, new BmpEncoder { PixelLayout = layout, RowOrder = rowOrder });
                var reference = ReferenceBmp.Parse(data);
                Assert.Equal(rowOrder == BmpRowOrder.TopDown, reference.IsTopDown);
                Assert.Equal(4, reference.PaletteEntries);
                Assert.Equal(expected, Decode(data));
            }
        }

        // One bit: the most significant bit is the left pixel
        using var twoColors = Image.ImportPixelData<Rgb24>([white, blue, blue, white, white, white, blue, white, white, blue], 5, 2);
        data = Save(twoColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1 });
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(46)));
        Assert.Equal(new byte[] { 0b1011_0000, 0, 0, 0, 0b1001_1000, 0, 0, 0 }, data[62..]);

        // Gray pixels always fit an 8-bit palette
        using var gray = Image.ImportPixelData<Gray8>([.. Enumerable.Range(0, 256).Select(value => new Gray8((byte)value))], 16, 16);
        var grayReference = ReferenceBmp.Parse(Save(gray, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8 }));
        Assert.Equal(256, grayReference.PaletteEntries);
        Assert.Equal(grayReference.Rgba.ToArray(), [.. Enumerable.Range(0, 256).SelectMany(value => new[] { (byte)value, (byte)value, (byte)value, byte.MaxValue })]);
    }

    [Fact]
    public void ReducingColorsNeedsAnExplicitSetting()
    {
        Rgb24[] pixels = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(250, 5, 5), new(5, 250, 5), new(5, 5, 250)];
        using var image = Image.ImportPixelData<Rgb24>(pixels, 3, 2);

        // Six colors do not fit two entries: the image is rejected, and nothing is written
        foreach (var encoder in new[]
        {
            new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1 },
            new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, Quantization = new QuantizeOptions { MaxColors = 5 } },
            new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, Quantization = new QuantizeOptions { MaxColors = 3, Dithering = DitheringMode.FloydSteinberg } },
        })
        {
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
            Assert.Equal(ImageFormat.Bmp, exception.Format);
            Assert.Equal("Color reduction", exception.Feature);
            Assert.Equal(0, stream.Length);
        }

        // MaxColors only limits the palette: six colors fit six entries exactly
        var exact = ReferenceBmp.Parse(Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, Quantization = new QuantizeOptions { MaxColors = 6 } }));
        Assert.Equal(6, exact.PaletteEntries);

        // Allowed: three entries, each the mean of the two colors it replaces, and every pixel maps to its nearest entry
        var data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, AllowColorReduction = true, Quantization = new QuantizeOptions { MaxColors = 3 } });
        var reduced = ReferenceBmp.Parse(data);
        Assert.Equal(3, reduced.PaletteEntries);
        Rgba32 blue = new(3, 3, 253), green = new(3, 253, 3), red = new(253, 3, 3);
        Assert.Equal([red, green, blue, red, green, blue], Decode(data));

        // The capacity of the layout applies whatever MaxColors says
        Assert.Equal(2, ReferenceBmp.Parse(Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1, AllowColorReduction = true })).PaletteEntries);
    }

    [Fact]
    public void AFixedPaletteIsUsedWhenTheColorsAreReducedOrFit()
    {
        Rgb24[] pixels = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(250, 5, 5), new(5, 250, 5), new(5, 5, 250)];
        using var image = Image.ImportPixelData<Rgb24>(pixels, 3, 2);
        var palette = new ImagePalette([new Rgba32(0, 0, 255), new Rgba32(0, 255, 0), new Rgba32(255, 0, 0), new Rgba32(9, 9, 9)]);

        // Colors that are not in the palette need AllowColorReduction
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, Quantization = new QuantizeOptions { Palette = palette } }));
        Assert.Equal("Color reduction", exception.Feature);

        var data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, AllowColorReduction = true, Quantization = new QuantizeOptions { Palette = palette } });
        Assert.Equal(4, ReferenceBmp.Parse(data).PaletteEntries);
        Rgba32 red = new(255, 0, 0), green = new(0, 255, 0), blue = new(0, 0, 255);
        Assert.Equal([red, green, blue, red, green, blue], Decode(data));

        // An image whose colors are in the palette is stored exactly
        using var fits = Image.ImportPixelData<Rgb24>([new(255, 0, 0), new(9, 9, 9)], 2, 1);
        Assert.Equal([red, new Rgba32(9, 9, 9)], Decode(Save(fits, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, Quantization = new QuantizeOptions { Palette = palette } })));

        // The palette must fit the layout, and its entries that are not opaque need a background
        Assert.Throws<ArgumentException>(() => Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1, AllowColorReduction = true, Quantization = new QuantizeOptions { Palette = palette } }));
        var translucent = new ImagePalette([new Rgba32(255, 255, 255, 128), new Rgba32(0, 0, 0)]);
        Assert.Throws<ArgumentException>(() => Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1, AllowColorReduction = true, Quantization = new QuantizeOptions { Palette = translucent } }));
        var flattened = ReferenceBmp.Parse(Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1, AllowColorReduction = true, BackgroundColor = new Rgba64(0, 0, 0), Quantization = new QuantizeOptions { Palette = translucent } }));
        Assert.Equal([0, 0, 0, 128, 128, 128], flattened.Palette.ToArray());
    }

    [Fact]
    public void ObsoletePropertiesSetTheQuantization()
    {
#pragma warning disable CS0618 // The obsolete properties forward to Quantization
        var encoder = new BmpEncoder { MaxColors = 16, Dithering = BmpDithering.FloydSteinberg };
        Assert.Equal(16, encoder.MaxColors);
        Assert.Equal(BmpDithering.FloydSteinberg, encoder.Dithering);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { Dithering = (BmpDithering)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BmpEncoder { MaxColors = 257 });
#pragma warning restore CS0618
        Assert.Equal(16, encoder.Quantization.MaxColors);
        Assert.Equal(DitheringMode.FloydSteinberg, encoder.Quantization.Dithering);
    }

    [Fact]
    public void RunLengthStreamsAreWrittenRowByRow()
    {
        // Four colors whose palette order is their order here
        Rgb24 a = new(0, 0, 1), b = new(0, 0, 2), c = new(0, 0, 3), d = new(0, 0, 4);
        using var image = Image.ImportPixelData<Rgb24>([a, b, a, b, a, b, a, b, a, a, a, a, b, c, d, d], 8, 2);

        // BI_RLE8, bottom row first: a run of four, then four literal pixels (two equal pixels are not worth a run); the
        // top row has no run at all. Each row ends with an end of line, and the stream with the end of bitmap
        var data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, Compression = BmpCompression.RunLength });
        byte[] expected8 = [4, 0, 0, 4, 1, 2, 3, 3, 0, 0, 0, 8, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 1];
        Assert.Equal(expected8, data[70..]);
        Assert.Equal(BiRle8, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30)));
        Assert.Equal((uint)expected8.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(34)));
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22)));

        // BI_RLE4: a run alternates two indexes, so the top row is one run of eight; literal indexes are packed two per byte
        data = Save(image, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, Compression = BmpCompression.RunLength });
        byte[] expected4 = [4, 0x00, 0, 4, 0x12, 0x33, 0, 0, 8, 0x01, 0, 0, 0, 1];
        Assert.Equal(expected4, data[70..]);
        Assert.Equal(BiRle4, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30)));
        Assert.Equal((uint)expected4.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(34)));
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void RunLengthRowsDecodeToTheirIndexesAndNeverExceedTwoBytesPerPixel(bool fourBit)
    {
        var colors = fourBit ? 16 : 256;
        var palette = Enumerable.Range(0, colors).Select(i => ((byte)i, (byte)(255 - i), (byte)(i * 7))).ToArray();
        var random = new Random(fourBit ? 4 : 8);
        var rows = new List<byte[]>();
        foreach (var width in new[] { 1, 2, 3, 4, 5, 254, 255, 256, 257, 258, 511, 600 })
        {
            rows.Add([.. Enumerable.Repeat((byte)7, width)]); // one run, longer than a count can store
            rows.Add([.. Enumerable.Range(0, width).Select(x => (byte)(x % 2))]); // two alternating indexes
            rows.Add([.. Enumerable.Range(0, width).Select(x => (byte)(x % colors))]); // no repetition
            rows.Add([.. Enumerable.Range(0, width).Select(x => (byte)(x / 3 % 5))]); // runs of exactly three
            rows.Add([.. Enumerable.Range(0, width).Select(x => (byte)(x % 7 < 2 ? 1 : x % colors))]); // runs of two between literals
            for (var i = 0; i < 4; i++)
            {
                rows.Add([.. Enumerable.Range(0, width).Select(_ => (byte)random.Next(3))]);
                rows.Add([.. Enumerable.Range(0, width).Select(_ => (byte)random.Next(colors))]);
            }
        }

        foreach (var row in rows)
        {
            var maximum = (int)BmpRunLengthEncoder.GetMaxEncodedRowLength(row.Length);
            var encoded = new byte[maximum + BmpRunLengthEncoder.EndOfBitmapLength];
            var length = BmpRunLengthEncoder.EncodeRow(row, fourBit, encoded);
            Assert.InRange(length, 4, maximum);
            Assert.Equal(new byte[] { 0, 0 }, encoded[(length - 2)..length]);
            BmpRunLengthEncoder.WriteEndOfBitmap(encoded.AsSpan(length));

            // The independent reader of the harness decodes the stream; every pixel must be written (no reliance on index 0)
            var file = Bmp(row.Length, 1, fourBit ? 4 : 8, [encoded[..(length + 2)]], palette, compression: fourBit ? BiRle4 : BiRle8);
            var reference = ReferenceBmp.Parse(file);
            Assert.Equal(length + 2, reference.PixelDataLength);
            var decoded = reference.Rgba.Span;
            for (var x = 0; x < row.Length; x++)
            {
                Assert.True(decoded[x * 4] == palette[row[x]].Item1 && decoded[(x * 4) + 1] == palette[row[x]].Item2, $"width {row.Length}, column {x}");
            }

            // The absolute runs hold at least three pixels, and no delta is ever written
            for (var position = 0; position < length - 2;)
            {
                if (encoded[position] != 0)
                {
                    position += 2;
                    continue;
                }

                int count = encoded[position + 1];
                Assert.InRange(count, 3, 255);
                var bytes = fourBit ? (count + 1) / 2 : count;
                position += 2 + bytes + (bytes & 1);
            }
        }
    }

    [Fact]
    public void CompressionRowOrderAndOutputKindDoNotChangeThePixels()
    {
        using var source = CreateImage(37, 19, seed: 12);
        using var image = source.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        foreach (var layout in new[] { BmpPixelLayout.Indexed4, BmpPixelLayout.Indexed8 })
        {
            foreach (var dithering in new[] { DitheringMode.None, DitheringMode.FloydSteinberg })
            {
                var settings = new BmpEncoder { PixelLayout = layout, AllowColorReduction = true, Quantization = new QuantizeOptions { Dithering = dithering } };
                var bottomUp = Save(image, settings);
                var topDown = Save(image, new BmpEncoder { PixelLayout = layout, AllowColorReduction = true, RowOrder = BmpRowOrder.TopDown, Quantization = new QuantizeOptions { Dithering = dithering } });
                var runLength = Save(image, new BmpEncoder { PixelLayout = layout, AllowColorReduction = true, Compression = BmpCompression.RunLength, Quantization = new QuantizeOptions { Dithering = dithering } });

                // Dithering runs from the top row down whatever the order of the rows in the file
                var expected = Decode(bottomUp);
                Assert.Equal(expected, Decode(topDown));
                Assert.Equal(expected, Decode(runLength));
                Assert.Equal(ReferenceBmp.Parse(bottomUp).Rgba.ToArray(), ReferenceBmp.Parse(runLength).Rgba.ToArray());
                Assert.True(ReferenceBmp.Parse(runLength).IsRunLength);

                // The declared sizes are the bytes written, on a non-seekable output too
                using var stream = new TestOutputStream { Seekable = false };
                image.Save(stream, new BmpEncoder { PixelLayout = layout, AllowColorReduction = true, Compression = BmpCompression.RunLength, Quantization = new QuantizeOptions { Dithering = dithering } });
                var written = stream.ToArray();
                Assert.Equal(runLength, written);
                Assert.Equal((uint)written.Length, BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(2)));
                Assert.Equal((uint)written.Length - BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(10)), BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(34)));

                // Saving twice gives the same bytes
                Assert.Equal(bottomUp, Save(image, settings));
            }

            // Dithering changes the pixels of a reduced palette, and nothing when the colors fit
            Assert.NotEqual(
                Decode(Save(image, new BmpEncoder { PixelLayout = layout, AllowColorReduction = true })),
                Decode(Save(image, new BmpEncoder { PixelLayout = layout, AllowColorReduction = true, Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } })));
        }

        using var fewColors = Image.ImportPixelData<Rgb24>([new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(1, 2, 3)], 2, 2);
        Assert.Equal(
            Save(fewColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8 }),
            Save(fewColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } }));
    }

    [Fact]
    public void TopDownRowsAreWrittenWithANegativeHeight()
    {
        using var image = Image.ImportPixelData<Rgb24>([new(1, 2, 3), new(4, 5, 6)], 1, 2);
        var data = Save(image, new BmpEncoder { RowOrder = BmpRowOrder.TopDown });
        Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22)));
        Assert.Equal(new byte[] { 3, 2, 1, 0, 6, 5, 4, 0 }, data[54..]);
        Assert.Equal([new(1, 2, 3), new(4, 5, 6)], Decode(data));

        // The default order stores the last row first
        Assert.Equal(new byte[] { 6, 5, 4, 0, 3, 2, 1, 0 }, Save(image, new BmpEncoder())[54..]);
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
            foreach (var rowOrder in new[] { BmpRowOrder.BottomUp, BmpRowOrder.TopDown })
            {
                using var decoded = Image.Load<Rgba32>(Save(source, new BmpEncoder { PixelLayout = layout, RowOrder = rowOrder }));
                Assert.Equal(GetPixels(source.Frames[0]), GetPixels(decoded.Frames[0]));
            }
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

    /// <summary>Run-length files that use every command, each ending exactly at its end-of-bitmap marker.</summary>
    private static byte[][] RunLengthSamples() =>
    [
        // A run and an absolute run; a delta to the right; a delta to the next row but one; an early end of bitmap
        Bmp(5, 4, 8, [[2, 1, 0, 3, 3, 2, 1, 0, 0, 0, 0, 2, 2, 0, 2, 3, 0, 0, 0, 2, 1, 1, 0, 3, 1, 2, 3, 0, 0, 1]], Palette4, compression: BiRle8),

        // An alternating run; a delta, an absolute run of five indexes on an odd column with its padding byte; a delta up
        Bmp(7, 3, 4, [[5, 0x12, 0, 0, 0, 2, 1, 0, 0, 5, 0x01, 0x23, 0x10, 0, 0, 2, 0, 1, 1, 0x30, 0, 1]], Palette4, compression: BiRle4),

        // A run; an absolute run of three triplets with its padding byte; a delta up and a last run
        Os2Bmp(64, 4, 3, 24, [2, 3, 2, 1, 0, 0, 0, 3, 6, 5, 4, 9, 8, 7, 12, 11, 10, 0, 0, 2, 0, 1, 1, 30, 20, 10, 0, 1], compression: BcaRle24),
    ];

    /// <summary>Assembles an OS/2 1.x file: a <c>BITMAPCOREHEADER</c> and three-byte palette entries; <paramref name="rows"/> are in storage order.</summary>
    private static byte[] CoreBmp(int width, int height, int bitsPerPixel, byte[][] rows, (byte R, byte G, byte B)[]? palette = null, int gap = 0)
    {
        palette ??= [];
        var body = rows.SelectMany(row => row).ToArray();
        var offset = 14 + 12 + (palette.Length * 3) + gap;
        var data = new byte[offset + body.Length];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), (uint)offset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), (ushort)bitsPerPixel);
        for (var i = 0; i < palette.Length; i++)
        {
            var entry = 26 + (i * 3);
            data[entry] = palette[i].B;
            data[entry + 1] = palette[i].G;
            data[entry + 2] = palette[i].R;
        }

        body.CopyTo(data, offset);
        return data;
    }

    /// <summary>
    /// Assembles an OS/2 2.x file: a <c>BITMAPCOREHEADER2</c> cut to <paramref name="headerLength"/> bytes (16 to 64) and
    /// four-byte palette entries; <paramref name="body"/> is the pixel data as stored.
    /// </summary>
    private static byte[] Os2Bmp(int headerLength, int width, int height, int bitsPerPixel, byte[] body, (byte R, byte G, byte B)[]? palette = null,
        uint compression = BiRgb, (int X, int Y)? pixelsPerMeter = null, ushort units = 0, ushort recording = 0, uint colorEncoding = 0)
    {
        palette ??= [];
        var header = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)headerLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)bitsPerPixel);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), pixelsPerMeter?.X ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), pixelsPerMeter?.Y ?? 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(32), (uint)palette.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), units);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(44), recording);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(56), colorEncoding);

        var offset = 14 + headerLength + (palette.Length * 4);
        var data = new byte[offset + body.Length];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), (uint)offset);
        header.AsSpan(0, headerLength).CopyTo(data.AsSpan(14));
        for (var i = 0; i < palette.Length; i++)
        {
            var entry = 14 + headerLength + (i * 4);
            data[entry] = palette[i].B;
            data[entry + 1] = palette[i].G;
            data[entry + 2] = palette[i].R;
        }

        body.CopyTo(data, offset);
        return data;
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
