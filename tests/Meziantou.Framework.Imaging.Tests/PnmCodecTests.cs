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
/// parsing, truncation, limits, bounded streaming memory, and the encoder's variant selection and alpha policy.
/// </summary>
public sealed class PnmCodecTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new PnmEncoder();
        Assert.Equal(ImageFormat.Pnm, encoder.Format);
        Assert.Equal(PnmEncoding.Binary, encoder.Encoding);
        Assert.Null(encoder.BackgroundColor);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PnmEncoder { Encoding = (PnmEncoding)5 });
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
        Assert.Equal(PixelFormat.Rgba64, info.PixelFormat);
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

    [Fact]
    public void PamHeadersAreValidated()
    {
        var rgba = Bytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n", [1, 2, 3, 128, 4, 5, 6, 0]);
        Assert.Equal([new(1, 2, 3, 128), new(4, 5, 6, 0)], Decode(rgba));
        Assert.Equal(ImageColorModel.Rgba, Image.Identify(rgba).ColorModel);

        // GRAYSCALE_ALPHA has no pixel format of its own: the gray sample is replicated, which is lossless
        var graya = Bytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n", [10, 255, 20, 0]);
        var info = Image.Identify(graya);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
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
