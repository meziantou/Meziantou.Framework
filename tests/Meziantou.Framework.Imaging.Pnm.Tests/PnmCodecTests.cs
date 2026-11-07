using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Pnm;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Netpbm decoding and encoding through the public APIs, on files assembled byte by byte with literal expected
/// samples: the plain and binary forms of PBM, PGM and PPM, PAM headers and tuple types, comments and the single
/// white-space byte that ends a header, <c>MAXVAL</c> normalization, 16-bit precision, concatenated images, bounded header
/// parsing, truncation, limits, bounded streaming memory, and the encoder's variant selection, alpha policy and portable
/// bitmap output.
/// </summary>
public sealed class PnmCodecTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new PnmEncoder();
        Assert.Equal(ImageFormat.Pnm, encoder.Format);
        Assert.Equal(PnmEncoding.Binary, encoder.Encoding);
        Assert.Equal(PnmPixelLayout.Auto, encoder.PixelLayout);
        Assert.Null(encoder.BackgroundColor);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PnmEncoder { Encoding = (PnmEncoding)5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PnmEncoder { PixelLayout = (PnmPixelLayout)5 });
        Assert.Throws<ArgumentException>(() => new PnmEncoder { BackgroundColor = new Rgba64(0, 0, 0, 7) });
    }

    [Fact]
    public void ASetPortableBitmapBitIsBlack()
    {
        // Binary PBM: the row is padded to a whole byte and the padding bits are ignored
        var binary = Bytes("P4\n# a comment\n5 2\n", [0b0110_1111, 0b1001_0000]);
        Rgba32[] expected =
        [
            new(255, 255, 255), new(0, 0, 0), new(0, 0, 0), new(255, 255, 255), new(0, 0, 0),
            new(0, 0, 0), new(255, 255, 255), new(255, 255, 255), new(0, 0, 0), new(255, 255, 255),
        ];

        Assert.Equal(expected, Decode(binary));
        Assert.Equal(1, Image.Identify(binary).BitsPerComponent);

        // The plain form has the same pixels: every non-blank character is one sample
        Assert.Equal(expected, Decode(Bytes("P1\n5 2\n01101\n#comment\n1 0 0 1 0\n", [])));
    }

    [Fact]
    public void MaxValueNormalizationIsTheExactRatio()
    {
        // MAXVAL 15: round(value * 255 / 15)
        var data = Bytes("P2\n4 1\n15\n0 7 8 15\n", []);
        Assert.Equal([new(0, 0, 0), new(119, 119, 119), new(136, 136, 136), new(255, 255, 255)], Decode(data));
        Assert.Equal(4, Image.Identify(data).BitsPerComponent);

        // Above 255 the samples are 16-bit: round(value * 65535 / 1000)
        var wide = Bytes("P3\n2 1\n1000\n0 500 1000 1000 0 250\n", []);
        var info = Image.Identify(wide);
        Assert.Equal(PixelFormat.Rgb48, info.PixelFormat);
        Assert.Equal(16, info.BitsPerComponent);
        using var image = Image.Load<Rgba64>(wide);
        var pixels = new Rgba64[2];
        image.Frames[0].CopyPixelDataTo(pixels);
        Assert.Equal(new Rgba64(0, 32768, 65535, 65535), pixels[0]);
        Assert.Equal(new Rgba64(65535, 0, 16384, 65535), pixels[1]);

        // A sample above MAXVAL is malformed, never clamped
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P2\n2 1\n15\n3 16\n", [])));
    }

    [Fact]
    public void SixteenBitBinarySamplesAreBigEndianAndLossless()
    {
        var data = Bytes("P5\n2 1\n65535\n", [0x12, 0x34, 0xFF, 0xFE]);
        Assert.Equal(PixelFormat.Gray16, Image.Identify(data).PixelFormat);
        using var image = Image.Load<Gray16>(data);
        var pixels = new Gray16[2];
        image.Frames[0].CopyPixelDataTo(pixels);
        Assert.Equal(0x1234, pixels[0].Value);
        Assert.Equal(0xFFFE, pixels[1].Value);
    }

    [Fact]
    public void ExactlyOneWhiteSpaceByteEndsABinaryHeader()
    {
        // The byte after MAXVAL ends the header; the next byte is the first sample, even when it is white space
        Assert.Equal([new(32, 32, 32), new(1, 1, 1)], Decode(Bytes("P5\n2 1\n255\n", [32, 1])));

        // A comment may not interrupt a token: the single separator is what tells the header from the raster
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n2 1\n255#comment\n", [32, 1])));
    }

    public static TheoryData<InputVariant> ReaderVariants => [.. InputVariants.ReaderVariants];

    /// <summary>
    /// Headers whose tokens are ended by a comment, with the pixels Netpbm 11.10.5, ImageMagick 7.1.2 and FFmpeg 6.1.2 decode:
    /// the line feed or carriage return that ends the comment delimits the token (the raster starts after it).
    /// </summary>
    private static (byte[] Data, int Width, Rgba32[] Pixels)[] HeaderTokensEndedByComments()
    {
        byte[] gray = [10, 20, 30, 40, 50, 60, 70, 80];
        var grayPixels = gray.Select(value => new Rgba32(value, value, value)).ToArray();
        var bk = new Rgba32(0, 0, 0);
        var wh = new Rgba32(255, 255, 255);
        return
        [
            (Bytes("P5\n4#c\n2\n255\n", gray), 4, grayPixels),
            (Bytes("P5\n4 2#c\n255\n", gray), 4, grayPixels),
            (Bytes("P5\n4 2\n255#c\n", gray), 4, grayPixels),
            (Bytes("P5\n4 2\n255#c\r", gray), 4, grayPixels),
            (Bytes("P5\n4 2\n255#c\r\n", gray), 4, [new(10, 10, 10), .. grayPixels[..^1]]), // the line feed is the first sample
            (Bytes("P2\n4#c\n2\n255\n10 20 30 40\n50 60 70 80\n", []), 4, grayPixels),
            (Bytes("P6\n2#c\n1\n255#x\n", [1, 2, 3, 4, 5, 6]), 2, [new(1, 2, 3), new(4, 5, 6)]),
            (Bytes("P4\n8#c\n2\n", [0xA5, 0x5A]), 8, [bk, wh, bk, wh, wh, bk, wh, bk, wh, bk, wh, bk, bk, wh, bk, wh]),

            // Not the digits 1 and 6 of one width: 1x6, whose raster starts with the bytes "1\n"
            (Bytes("P4\n1#c\n6 1\n", [0x80, 0x00, 0x80, 0x00]), 1, [wh, wh, bk, wh, bk, wh]),
        ];
    }

    [Fact]
    public void LenientDecodingEndsAHeaderTokenAtAComment()
    {
        var lenient = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
        foreach (var (data, width, pixels) in HeaderTokensEndedByComments())
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
            Assert.Equal("A Netpbm comment interrupts a header token; a token must be followed by white space.", exception.Message);

            var size = new Size(width, pixels.Length / width);
            Assert.Equal(size, Image.Identify(data, new ImageIdentifyOptions { Configuration = lenient }).Size);
            Assert.Equal(1, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = lenient }).FrameCount);
            using var image = Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = lenient });
            Assert.Equal(size, image.Size);
            Assert.Equal(pixels, GetPixels(image.Frames[0]));
        }

        // A comment that splits the digits of MAXVAL ends it too: MAXVAL 25, then the raster starts with "5", above it
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n4 2\n25#c\n5\n", new byte[8]), new ImageDecodeOptions { Configuration = lenient }));
    }

    [Theory]
    [MemberData(nameof(ReaderVariants))]
    public async Task LenientReadersEndAHeaderTokenAtAComment(InputVariant variant)
    {
        var lenient = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
        foreach (var (data, width, pixels) in HeaderTokensEndedByComments())
        {
            using (var image = await InputVariants.LoadAsync<Rgba32>(variant, data, ImageFormat.Pnm, new ImageDecodeOptions { Configuration = lenient }, XunitCancellationToken))
            {
                Assert.Equal(pixels, GetPixels(image.Frames[0]));
            }

            await InputVariants.ReadAsync<Rgba32, int>(variant, data, ImageFormat.Pnm, new ImageReaderOptions { Configuration = lenient }, async (reader, asynchronous) =>
            {
                Assert.Equal(width, reader.Info.Size.Width);
                using var frame = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
                Assert.NotNull(frame);
                Assert.Equal(pixels, GetPixels(frame.Frames[0]));
                Assert.Null(asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame());
                return 0;
            }, XunitCancellationToken);
        }
    }

    [Fact]
    public void LenientDecodingKeepsTheHeaderLimits()
    {
        var data = Bytes("P5\n4#c\n2\n255\n", [10, 20, 30, 40, 50, 60, 70, 80]);
        using (Image.Load(data, Options(new ImageResourceLimits { MaxWidth = 4 })))
        {
        }

        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxWidth = 3 }))).Kind);

        // The comment counts in the bounded header length as any other comment
        var comment = new string('c', 70_000);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes($"P5\n1#{comment}\n1\n255\n", [7]), Options(new ImageResourceLimits())));
        Assert.Contains("header is longer", exception.Message, StringComparison.Ordinal);

        static ImageDecodeOptions Options(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient, Limits = limits } };
    }

    [Fact]
    public void PamHeadersAreValidated()
    {
        var rgba = Bytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n", [1, 2, 3, 128, 4, 5, 6, 0]);
        Assert.Equal([new(1, 2, 3, 128), new(4, 5, 6, 0)], Decode(rgba));
        Assert.Equal(ImageColorModel.Rgba, Image.Identify(rgba).ColorModel);

        // GRAYSCALE_ALPHA loads as gray with alpha; as RGBA, the gray sample is replicated, which is lossless
        var graya = Bytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n", [10, 255, 20, 0]);
        var info = Image.Identify(graya);
        Assert.Equal(PixelFormat.La16, info.PixelFormat);
        Assert.Equal(ImageColorModel.GrayscaleAlpha, info.ColorModel);
        Assert.Equal([new(10, 10, 10, 255), new(20, 20, 20, 0)], Decode(graya));

        // A PAM BLACKANDWHITE sample is an intensity: 0 is black and 1 is white (the opposite of a PBM bit)
        Assert.Equal([new(0, 0, 0), new(255, 255, 255)], Decode(Bytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 1\nTUPLTYPE BLACKANDWHITE\nENDHDR\n", [0, 1])));

        // Without TUPLTYPE the depth selects the tuple
        Assert.Equal([new(1, 2, 3)], Decode(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nENDHDR\n", [1, 2, 3])));

        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n", [1, 2, 3])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P7\nWIDTH 1\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nENDHDR\n", [1, 2, 3])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nGAMMA 2\nENDHDR\n", [1, 2, 3])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\n", [1, 2, 3])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 1\nMAXVAL 255\nTUPLTYPE BLACKANDWHITE\nENDHDR\n", [1])));

        var unsupported = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nTUPLTYPE CMYK\nENDHDR\n", [1, 2, 3, 4])));
        Assert.Equal("PAM tuple type", unsupported.Feature);
    }

    [Fact]
    public void EveryPamTupleIsDecoded()
    {
        // BLACKANDWHITE_ALPHA: an intensity and an alpha sample, both 0 or 1
        Assert.Equal([new(0, 0, 0, 255), new(255, 255, 255, 0)], Decode(Bytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 1\nTUPLTYPE BLACKANDWHITE_ALPHA\nENDHDR\n", [0, 1, 1, 0])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nTUPLTYPE BLACKANDWHITE_ALPHA\nENDHDR\n", [0, 1])));

        // Without TUPLTYPE, DEPTH 2 is gray and alpha and DEPTH 4 is RGB and alpha; there is no standard tuple of 5 samples
        Assert.Equal([new(10, 10, 10, 200)], Decode(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nENDHDR\n", [10, 200])));
        Assert.Equal([new(1, 2, 3, 4)], Decode(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nENDHDR\n", [1, 2, 3, 4])));
        var unsupported = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 5\nMAXVAL 255\nENDHDR\n", [1, 2, 3, 4, 5])));
        Assert.Equal("PAM tuple type", unsupported.Feature);

        // Comments, blank lines, indentation and CRLF line ends between the header lines
        Assert.Equal([new(1, 2, 3)], Decode(Bytes("P7\r\n# comment\r\n\r\n  WIDTH 1\r\nHEIGHT\t1\r\nDEPTH 3\r\nMAXVAL 255\r\nENDHDR\r\n", [1, 2, 3])));
    }

    [Theory]
    [InlineData("TUPLTYPE RGB\nTUPLTYPE RGB\n", "TUPLTYPE more than once")]
    [InlineData("MAXVAL\n", "has no value")]
    [InlineData("MAXVAL 0x10\n", "not a decimal number")]
    [InlineData("MAXVAL 2147483648\n", "larger than 2147483647")]
    [InlineData("", "before ENDHDR")]
    public void MalformedPamHeaderLinesAreRejected(string lines, string expectedMessage)
    {
        var data = Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\n" + lines + "ENDHDR\n", [1, 2, 3]);
        Assert.Contains(expectedMessage, Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APamHeaderLineIsBounded()
    {
        var data = Bytes($"P7\n#{new string('c', 70_000)}\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nENDHDR\n", [1, 2, 3]);
        Assert.Contains("PAM header line is longer", Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SamplesBelowTheMaximumOfTheirSizeAreScaled()
    {
        // 8-bit RGB and gray-and-alpha samples with MAXVAL 100: round(value * 255 / 100)
        Assert.Equal([new(0, 128, 255)], Decode(Bytes("P6\n1 1\n100\n", [0, 50, 100])));
        Assert.Equal([new(128, 128, 128, 255)], Decode(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 2\nMAXVAL 100\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n", [50, 100])));

        // A binary sample above MAXVAL is malformed, never clamped
        Assert.Contains("greater than MAXVAL (100)", Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n2 1\n100\n", [100, 101]))).Message, StringComparison.Ordinal);

        // 16-bit samples: round(value * 65535 / MAXVAL), from a table up to 4096 and computed above
        Assert.Equal([new Rgba64(32768, 32768, 32768, 65535)], Decode16(Bytes("P5\n1 1\n1000\n", [0x01, 0xF4])));
        Assert.Equal([new Rgba64(32768, 32768, 32768, 65535), new Rgba64(65535, 65535, 65535, 65535)], Decode16(Bytes("P5\n2 1\n40000\n", [0x4E, 0x20, 0x9C, 0x40])));
        Assert.Equal([new Rgba64(32768, 32768, 32768, 65535)], Decode16(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 2\nMAXVAL 1000\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n", [0x01, 0xF4, 0x03, 0xE8])));
        Assert.Equal([new Rgba64(0, 32768, 65535, 16384)], Decode16(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 4\nMAXVAL 1000\nTUPLTYPE RGB_ALPHA\nENDHDR\n", [0, 0, 0x01, 0xF4, 0x03, 0xE8, 0x00, 0xFA])));

        // Gray and alpha at MAXVAL 65535 are copied
        Assert.Equal([new Rgba64(0x1234, 0x1234, 0x1234, 0xABCD)], Decode16(Bytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 2\nMAXVAL 65535\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n", [0x12, 0x34, 0xAB, 0xCD])));
    }

    [Fact]
    public void PlainRastersAcceptOnlyDigitsWhiteSpaceAndComments()
    {
        // A comment may end a sample
        Assert.Equal([new(3, 3, 3), new(255, 255, 255)], Decode(Bytes("P2\n2 1\n255\n3# comment\n255\n", [])));
        Assert.Contains("byte 0x78", Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P2\n2 1\n255\n3 x\n", []))).Message, StringComparison.Ordinal);
        Assert.Contains("larger than 65535", Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P2\n1 1\n65535\n65536\n", []))).Message, StringComparison.Ordinal);
        Assert.Contains("larger than 2147483647", Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n99999999999 1\n255\n", [1]))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IllegalHeaderFieldsAreRejected()
    {
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n0 1\n255\n", [1])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n1 0\n255\n", [1])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n1 1\n0\n", [1])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n1 1\n65536\n", [1, 2])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P5\n1 x\n255\n", [1])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Bytes("P1\n2 1\n0 2\n", [])));
    }

    [Fact]
    public void TheHeaderIsBounded()
    {
        // Adversarial comments cannot make the header walk unbounded
        var comment = new string('c', 70_000);
        var data = Bytes($"P5\n#{comment}\n1 1\n255\n", [7]);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains("header is longer", exception.Message, StringComparison.Ordinal);

        // A shorter comment is accepted
        Assert.Equal([new(7, 7, 7)], Decode(Bytes($"P5\n#{new string('c', 1000)}\n1 1\n255\n", [7])));
    }

    [Fact]
    public void OnlyTheFirstImageOfAConcatenationIsRead()
    {
        // Netpbm files may hold several images; this version reads the first one and never reports an animation
        var first = Bytes("P5\n2 1\n255\n", [10, 20]);
        var second = Bytes("P5\n2 1\n255\n", [30, 40]);
        var data = (byte[])[.. first, .. second];
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.Equal([new(10, 10, 10), new(20, 20, 20)], Decode(data));
    }

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task APlainRasterEndsAtTheEndOfTheInputWhateverTheInput(InputVariant variant)
    {
        // The last sample of a plain raster is ended by one white-space byte or by the end of the input: a stream only
        // reports its end once it is read past, which must not cost more encoded bytes than the file holds
        string[] files =
        [
            "P1\n2 1\n10", "P1\n2 1\n1 0", "P1\n2 1\n1 0\n",
            "P2\n2 1\n255\n7 200", "P2\n2 1\n255\n7 200 ",
            "P3\n1 1\n255\n1 2 3", "P3\n1 1\n255\n1 2 3\n",
            "P2\n3 1\n65535\n0 1000 65535",
        ];
        foreach (var file in files)
        {
            await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, Bytes(file, []), ImageFormat.Pnm, XunitCancellationToken);
        }

        Assert.Equal([new(255, 255, 255), new(0, 0, 0)], Decode(Bytes("P1\n2 1\n01", [])));
        Assert.Equal([new(7, 7, 7), new(200, 200, 200)], Decode(Bytes("P2\n2 1\n255\n7 200", [])));
    }

    [Fact]
    public void TruncationAtEveryByteIsReported()
    {
        var data = Bytes("P6\n3 2\n255\n", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]);
        for (var length = 0; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();
            var exception = Record.Exception(() => Image.Load(prefix).Dispose());
            Assert.True(exception is InvalidImageContentException or UnknownImageFormatException, $"{length} bytes: {exception?.GetType().Name ?? "no exception"}");
        }
    }

    [Fact]
    public void LimitsAreCheckedOnceTheHeaderIsComplete()
    {
        var data = Bytes("P6\n8 4\n255\n", new byte[8 * 4 * 3]);
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 4 } };
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration })).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = configuration })).Kind);
    }

    [Fact]
    public async Task StreamingKeepsTwoRowsOfDecoderStateAndNeverBuffersTheFile()
    {
        using var source = CreateImage(512, 256, seed: 9);
        using var opaque = source.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        var data = Save(opaque, new PnmEncoder());
        await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1024 };
        using var image = await Image.LoadAsync<Rgb24>(stream, cancellationToken: XunitCancellationToken);
        var scope = image.Owner.Scope;
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.True(state < 64 * 1024, $"Peak decoder state and input buffer {state} bytes for a {data.Length}-byte input.");
    }

    [Theory]
    [InlineData(PixelFormat.Gray8, 5, 255)]
    [InlineData(PixelFormat.Gray16, 5, 65535)]
    [InlineData(PixelFormat.Rgb24, 6, 255)]
    [InlineData(PixelFormat.Rgba32, 7, 255)]
    [InlineData(PixelFormat.Bgra32, 7, 255)]
    [InlineData(PixelFormat.Rgba64, 7, 65535)]
    public void TheVariantFollowsThePixelFormat(PixelFormat format, int expectedMagic, int expectedMaxValue)
    {
        using var source = CreateImage(5, 3, seed: (int)format);
        using var image = ConvertTo(source, format);
        var reference = ReferencePnm.Parse(Save(image, new PnmEncoder()));
        Assert.Equal(expectedMagic, reference.Magic);
        Assert.Equal(expectedMaxValue, reference.MaxValue);
        Assert.Equal(0, reference.TrailingBytes);
    }

    [Fact]
    public void PlainOutputHasNoAlphaAndWrapsItsLines()
    {
        using var source = CreateImage(40, 3, seed: 6);
        Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => Save(source, new PnmEncoder { Encoding = PnmEncoding.Plain })).Feature);

        var data = Save(source, new PnmEncoder { Encoding = PnmEncoding.Plain, BackgroundColor = new Rgba64(0, 0, 0) });
        var text = Encoding.ASCII.GetString(data);
        Assert.StartsWith("P3\n40 3\n255\n", text, StringComparison.Ordinal);
        var longest = text.Split('\n').Max(line => line.Length);
        Assert.True(longest <= 70, $"The longest plain raster line is {longest} characters.");

        // The plain and binary forms of an opaque image hold the same samples
        using var opaque = source.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        using var plain = Image.Load<Rgb24>(Save(opaque, new PnmEncoder { Encoding = PnmEncoding.Plain }));
        using var binary = Image.Load<Rgb24>(Save(opaque, new PnmEncoder()));
        Assert.Equal(GetPixels(plain.Frames[0]), GetPixels(binary.Frames[0]));
    }

    [Theory]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgba64)]
    public void APortableBitmapIsWrittenOnRequestFromEveryPixelFormat(PixelFormat format)
    {
        using var source = CreateBlackAndWhiteImage(11, "01101001101" + "11001101000" + "00010111011");
        using var image = ConvertTo(source, format);

        // One bit per pixel, most significant bit first, a set bit is black, and the padding bits of a row are clear
        Assert.Equal(Bytes("P4\n11 3\n", [0x69, 0xA0, 0xCD, 0x00, 0x17, 0x60]), Save(image, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap }));
        Assert.Equal(Bytes("P1\n11 3\n01101001101\n11001101000\n00010111011\n", []), Save(image, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, Encoding = PnmEncoding.Plain }));

        // Black and white pixels alone never select it: the default layout still follows the pixel format
        Assert.NotEqual(4, ReferencePnm.Parse(Save(image, new PnmEncoder())).Magic);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(70)]
    [InlineData(71)]
    [InlineData(140)]
    [InlineData(141)]
    public void BitmapRowsEndOnAByteAndPlainBitmapLinesWithinSeventyCharacters(int width)
    {
        using var image = Image.ImportPixelData<Gray8>(new Gray8[width * 2], width, 2);

        // Every row starts on a new byte: the last byte of a row keeps its unused low bits clear
        var row = Enumerable.Repeat((byte)0xFF, (width + 7) / 8).ToArray();
        row[^1] = (byte)(0xFF << ((8 - (width % 8)) % 8));
        Assert.Equal(Bytes(string.Create(CultureInfo.InvariantCulture, $"P4\n{width} 2\n"), [.. row, .. row]), Save(image, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap }));

        // Every row starts on a new line too, and a row that fills its last line exactly is not followed by an empty one
        var lines = string.Concat(Enumerable.Range(0, width).Chunk(70).Select(line => new string('1', line.Length) + "\n"));
        var plain = Encoding.ASCII.GetString(Save(image, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, Encoding = PnmEncoding.Plain }));
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"P1\n{width} 2\n{lines}{lines}"), plain);
    }

    [Fact]
    public void APortableBitmapRejectsEveryPixelThatIsNotBlackOrWhite()
    {
        // The pixels are compared at their own precision, never as a luma: Rec. 709 rounds (0, 0, 6) to 0 and
        // (255, 255, 249) to 255, and both are colors
        Image[] images =
        [
            Image.ImportPixelData<Rgb24>([new(0, 0, 0), new(0, 0, 6)], 2, 1),
            Image.ImportPixelData<Rgb24>([new(255, 255, 255), new(255, 255, 249)], 2, 1),
            Image.ImportPixelData<Rgb24>([new(255, 0, 0)], 1, 1),
            Image.ImportPixelData<Gray8>([new(255), new(1)], 2, 1),
            Image.ImportPixelData<Gray8>([new(128)], 1, 1),
            Image.ImportPixelData<Gray8>([new(0), new(254)], 2, 1),
            Image.ImportPixelData<Gray16>([new(0), new(1)], 2, 1),
            Image.ImportPixelData<Gray16>([new(65535), new(65534)], 2, 1),
            Image.ImportPixelData<Rgba32>([new(255, 0, 0)], 1, 1),
            Image.ImportPixelData<Bgra32>([new(0, 0, 255)], 1, 1),
            Image.ImportPixelData<Rgba64>([new(65535, 65535, 65534)], 1, 1),
            Image.ImportPixelData<Rgba64>([new(0, 255, 0)], 1, 1),
        ];

        foreach (var image in images)
        {
            using (image)
            {
                foreach (var encoding in Enum.GetValues<PnmEncoding>())
                {
                    using var stream = new MemoryStream();
                    var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, Encoding = encoding }));
                    Assert.Equal("Bilevel reduction", exception.Feature);
                    Assert.Equal(ImageFormat.Pnm, exception.Format);
                    Assert.Equal(0, stream.Length);
                }
            }
        }
    }

    [Fact]
    public void APortableBitmapHasNoAlphaAndChecksTheCompositedPixels()
    {
        var white = new Rgba64(65535, 65535, 65535);
        var black = new Rgba64(0, 0, 0);
        using var transparent = Image.ImportPixelData<Rgba32>([new(0, 0, 0), new(255, 255, 255, 0)], 2, 1);
        Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => Save(transparent, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap })).Feature);
        Assert.Equal(Bytes("P4\n2 1\n", [0b1000_0000]), Save(transparent, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, BackgroundColor = white }));
        Assert.Equal(Bytes("P4\n2 1\n", [0b1100_0000]), Save(transparent, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, BackgroundColor = black }));

        // A nearly opaque white pixel is white over white, and a gray level over black
        using var nearlyOpaque = Image.ImportPixelData<Rgba32>([new(255, 255, 255, 254)], 1, 1);
        Assert.Equal(Bytes("P1\n1 1\n0\n", []), Save(nearlyOpaque, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, Encoding = PnmEncoding.Plain, BackgroundColor = white }));
        Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => Save(nearlyOpaque, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, BackgroundColor = black })).Feature);

        using var nearlyOpaqueWide = Image.ImportPixelData<Rgba64>([new(65535, 65535, 65535, 65534), new(0, 0, 0, 0)], 2, 1);
        Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => Save(nearlyOpaqueWide, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap })).Feature);
        Assert.Equal(Bytes("P4\n2 1\n", [0b0000_0000]), Save(nearlyOpaqueWide, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, BackgroundColor = white }));
        Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => Save(nearlyOpaqueWide, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, BackgroundColor = black })).Feature);

        // The background itself may be any color as long as nothing is composited over it
        using var opaque = Image.ImportPixelData<Rgba64>([new(65535, 65535, 65535), new(0, 0, 0)], 2, 1);
        Assert.Equal(Bytes("P4\n2 1\n", [0b0100_0000]), Save(opaque, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, BackgroundColor = new Rgba64(65535, 0, 0) }));
    }

    [Fact]
    public void ARejectedBitmapFrameWritesNothingAndLeavesTheWriterUsable()
    {
        using var gray = Image.ImportPixelData<Gray8>([new(0), new(128)], 2, 1);
        using var bitmap = Image.ImportPixelData<Gray8>([new(0), new(255)], 2, 1);
        using var stream = new MemoryStream();
        using (var writer = Image.CreateWriter<Gray8>(stream, new ImageWriterOptions(2, 1) { Encoder = new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap }, LeaveOpen = true }))
        {
            Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(gray.Frames[0])).Feature);
            Assert.Equal(0, stream.Length);
            writer.WriteFrame(bitmap.Frames[0]);
            writer.Complete();
        }

        Assert.Equal(Bytes("P4\n2 1\n", [0b1000_0000]), stream.ToArray());
    }

    [Fact]
    public async Task OnlyThePbmExtensionSelectsThePortableBitmap()
    {
        using var bitmap = Image.ImportPixelData<Gray8>([new(0), new(255)], 2, 1);
        using var gray = Image.ImportPixelData<Gray8>([new(0), new(128)], 2, 1);
        var directory = TestFiles.CreateDirectory();
        try
        {
            var path = directory / "image.PBM";
            bitmap.Save(path);
            Assert.Equal(Bytes("P4\n2 1\n", [0b1000_0000]), await File.ReadAllBytesAsync(path, XunitCancellationToken));

            // A rejected image leaves the existing file as it was, and nothing else behind
            Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => gray.Save(path)).Feature);
            Assert.Equal(Bytes("P4\n2 1\n", [0b1000_0000]), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal([path], Directory.GetFiles(directory));

            // An explicit encoder never looks at the extension, and the other Netpbm extensions keep the default layout
            await gray.SaveAsync(path, new PnmEncoder(), XunitCancellationToken);
            Assert.Equal(Bytes("P5\n2 1\n255\n", [0, 128]), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            await bitmap.SaveAsync(directory / "image.pgm", cancellationToken: XunitCancellationToken);
            Assert.Equal(Bytes("P5\n2 1\n255\n", [0, 255]), await File.ReadAllBytesAsync(directory / "image.pgm", XunitCancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnimationsAndMetadataAreRejected()
    {
        using var image = CreateImage(3, 2, seed: 5);
        image.AppendFrame();
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new PnmEncoder()));

        using var still = CreateImage(3, 2, seed: 5);
        still.Metadata.Resolution = new ImageResolution(72, 72);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(still, new PnmEncoder()));
        Assert.NotEmpty(Save(still, new PnmEncoder { MetadataHandling = MetadataHandling.Strip }));
    }

    [Fact]
    public void EncodedFilesRoundTripThroughTheLibrary()
    {
        using var source = CreateImage(17, 9, seed: 2);
        using var decoded = Image.Load<Rgba32>(Save(source, new PnmEncoder()));
        Assert.Equal(GetPixels(source.Frames[0]), GetPixels(decoded.Frames[0]));

        using var wide = source.CloneAs<Rgba64>();
        using var decodedWide = Image.Load<Rgba64>(Save(wide, new PnmEncoder()));
        var expected = new Rgba64[wide.Width * wide.Height];
        wide.Frames[0].CopyPixelDataTo(expected);
        var actual = new Rgba64[wide.Width * wide.Height];
        decodedWide.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(expected, actual);
    }

    private static byte[] Bytes(string header, byte[] raster) => [.. Encoding.ASCII.GetBytes(header), .. raster];

    private static byte[] Save(Image image, PnmEncoder encoder)
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

    private static Rgba64[] Decode16(byte[] data)
    {
        using var image = Image.Load<Rgba64>(data);
        return GetPixels(image.Frames[0]);
    }

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
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

    /// <summary>Creates an opaque image from the digits of a plain portable bitmap: 1 is black and 0 is white.</summary>
    private static Image<Rgba32> CreateBlackAndWhiteImage(int width, string bits)
    {
        var pixels = bits.Select(bit => bit == '1' ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255)).ToArray();
        return Image.ImportPixelData<Rgba32>(pixels, width, pixels.Length / width);
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
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
}
