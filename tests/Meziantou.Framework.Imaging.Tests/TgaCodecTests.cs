using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.TestHarness.Tga;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// TGA decoding and encoding through the public APIs, on files assembled byte by byte with literal expected pixels:
/// the supported image types and depths, both origin bits, BGR ordering, the descriptor alpha bits, color maps with a
/// first-entry offset, run-length packets across scan lines and at the end of the image, the TGA 2.0 trailer (footer,
/// extension area and its attributes type, developer area), truncation, limits, bounded streaming memory, and the
/// encoder's compression, precision and metadata policies.
/// </summary>
public sealed class TgaCodecTests
{
    private static readonly byte[] FooterSignature = "TRUEVISION-XFILE.\0"u8.ToArray();

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new TgaEncoder();
        Assert.Equal(ImageFormat.Tga, encoder.Format);
        Assert.Equal(TgaCompression.None, encoder.Compression);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TgaEncoder { Compression = (TgaCompression)9 });
    }

    [Fact]
    public void BothOriginBitsAreResolved()
    {
        // Two rows of two pixels, stored bottom-up and left to right
        byte[] pixels = [3, 2, 1, 6, 5, 4, 0x13, 0x12, 0x11, 0x16, 0x15, 0x14];
        Rgba32[] expected = [new(0x11, 0x12, 0x13), new(0x14, 0x15, 0x16), new(1, 2, 3), new(4, 5, 6)];
        Assert.Equal(expected, Decode(Tga(2, 2, 24, 2, pixels)));

        // Top to bottom (descriptor bit 5): the stored rows are reversed
        byte[] topDown = [0x13, 0x12, 0x11, 0x16, 0x15, 0x14, 3, 2, 1, 6, 5, 4];
        Assert.Equal(expected, Decode(Tga(2, 2, 24, 2, topDown, descriptor: 0x20)));

        // Right to left (descriptor bit 4): every stored row is reversed
        byte[] rightToLeft = [6, 5, 4, 3, 2, 1, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11];
        Assert.Equal(expected, Decode(Tga(2, 2, 24, 2, rightToLeft, descriptor: 0x10)));
    }

    [Fact]
    public void AlphaComesFromTheImageDescriptor()
    {
        byte[] pixels = [0x30, 0x20, 0x10, 0x40, 0x33, 0x22, 0x11, 0x00];

        var opaque = Tga(2, 1, 32, 2, pixels);
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(opaque).PixelFormat);
        Assert.Equal([new(0x10, 0x20, 0x30), new(0x11, 0x22, 0x33)], Decode(opaque));

        var transparent = Tga(2, 1, 32, 2, pixels, descriptor: 0x08);
        var info = Image.Identify(transparent);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.True(info.MayHaveTransparency);
        Assert.Equal([new(0x10, 0x20, 0x30, 0x40), new(0x11, 0x22, 0x33, 0x00)], Decode(transparent));

        // An alpha-bit count the samples cannot hold is not a TGA at all (the header is the only signature)
        Assert.Throws<UnknownImageFormatException>(() => Image.Load(Tga(2, 1, 24, 2, [1, 2, 3, 4, 5, 6], descriptor: 0x08)));
    }

    [Fact]
    public void SixteenBitSamplesExpandWithTheExactRatioAndTheAlphaBit()
    {
        // A1 R5 G5 B5, little-endian
        var data = Tga(3, 1, 16, 2, [0x00, 0x80, 0xFF, 0xFF, 0x00, 0x7C], descriptor: 0x01);
        Assert.Equal([new(0, 0, 0, 255), new(255, 255, 255, 255), new(255, 0, 0, 0)], Decode(data)); // the last pixel has the alpha bit clear

        // Without a declared alpha bit the top bit is undefined and the image is opaque
        var opaque = Tga(2, 1, 15, 2, [0x00, 0x80, 0x1F, 0x00]);
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(opaque).PixelFormat);
        Assert.Equal([new(0, 0, 0), new(0, 0, 255)], Decode(opaque));
    }

    [Fact]
    public void GrayscaleImagesDecodeToGray8()
    {
        var data = Tga(3, 1, 8, 3, [10, 128, 255]);
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Gray8, info.PixelFormat);
        Assert.Equal(ImageColorModel.Grayscale, info.ColorModel);
        Assert.Equal([new(10, 10, 10), new(128, 128, 128), new(255, 255, 255)], Decode(data));
    }

    [Fact]
    public void ColorMapsHonorTheFirstEntryOffset()
    {
        // The stored map holds the entries for the indexes 2, 3 and 4
        byte[] map = [30, 20, 10, 60, 50, 40, 90, 80, 70];
        var data = Tga(3, 1, 8, 1, [2, 4, 3], colorMap: (2, 3, 24), colorMapData: map);
        Assert.Equal(ImageColorModel.Indexed, Image.Identify(data).ColorModel);
        Assert.Equal([new(10, 20, 30), new(70, 80, 90), new(40, 50, 60)], Decode(data));

        // An index below the first stored entry is malformed
        var invalid = Assert.Throws<InvalidImageContentException>(() => Image.Load(Tga(1, 1, 8, 1, [1], colorMap: (2, 3, 24), colorMapData: map)));
        Assert.Contains("color-map index 1", invalid.Message, StringComparison.Ordinal);

        // 32-bit map entries carry alpha when the descriptor declares it
        byte[] rgbaMap = [30, 20, 10, 128, 60, 50, 40, 0];
        var alpha = Tga(2, 1, 8, 1, [0, 1], colorMap: (0, 2, 32), colorMapData: rgbaMap, descriptor: 0x08);
        Assert.Equal([new(10, 20, 30, 128), new(40, 50, 60, 0)], Decode(alpha));
    }

    [Fact]
    public void RunLengthPacketsMayCrossScanLines()
    {
        // A run of five pixels over a 2x3 image, then a raw packet of one
        byte[] packets = [0x84, 1, 2, 3, 0x00, 9, 8, 7];
        Assert.Equal(
            [new(3, 2, 1), new(3, 2, 1), new(3, 2, 1), new(3, 2, 1), new(3, 2, 1), new(7, 8, 9)],
            Decode(Tga(2, 3, 24, 10, packets, descriptor: 0x20)));

        // A packet that runs past the last pixel is malformed
        var invalid = Assert.Throws<InvalidImageContentException>(() => Image.Load(Tga(2, 2, 24, 10, [0x85, 1, 2, 3], descriptor: 0x20)));
        Assert.Contains("extends past the last pixel", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedDepthsAreRecognizedAndRejected()
    {
        var grayscale = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Tga(1, 1, 16, 3, [0, 0])));
        Assert.Equal("Grayscale: 16-bit samples", grayscale.Feature);

        var indexes = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Tga(1, 1, 16, 1, [0, 0], colorMap: (0, 2, 24), colorMapData: [0, 0, 0, 1, 1, 1])));
        Assert.Equal("Color map: 16-bit indexes", indexes.Feature);
    }

    [Fact]
    public void TheIdentificationFieldIsSkipped()
    {
        var data = Tga(2, 1, 24, 2, [3, 2, 1, 6, 5, 4], identification: "hello"u8.ToArray());
        Assert.Equal([new(1, 2, 3), new(4, 5, 6)], Decode(data));
    }

    [Fact]
    public void TheTrailerIsReadAndTheImageDescriptorStaysAuthoritative()
    {
        var opaque = Tga(2, 1, 24, 2, [3, 2, 1, 6, 5, 4], identification: "hello"u8.ToArray());
        Rgba32[] opaquePixels = [new(1, 2, 3), new(4, 5, 6)];
        var alpha = Tga(2, 1, 32, 2, [0x30, 0x20, 0x10, 0x40, 0x33, 0x22, 0x11, 0x00], descriptor: 0x08);
        Rgba32[] alphaPixels = [new(0x10, 0x20, 0x30, 0x40), new(0x11, 0x22, 0x33, 0x00)];

        // A footer without an extension area, and a developer area nothing but the footer points at
        Assert.Equal(opaquePixels, Decode([.. opaque, .. Trailer(opaque.Length)]));
        Assert.Equal(alphaPixels, Decode([.. alpha, .. Trailer(alpha.Length, developerAreaLength: 40)]));

        // The attributes type says whether the alpha data is meaningful (0: none, 1 and 2: undefined, 3: useful); the
        // representation was fixed by the image descriptor before the first pixel, so no value changes the pixels, and
        // the reserved and unassigned values do not either
        foreach (var attributesType in new[] { 0, 1, 2, 3, 5, 127, 128, 255 })
        {
            Assert.Equal(alphaPixels, Decode([.. alpha, .. Trailer(alpha.Length, attributesType)]));
            Assert.Equal(opaquePixels, Decode([.. opaque, .. Trailer(opaque.Length, attributesType)]));
        }

        // The extension area is where the footer says, not where the image data ends
        Assert.Equal(alphaPixels, Decode([.. alpha, .. Trailer(alpha.Length, attributesType: 3, developerAreaLength: 40)]));
        Assert.Equal(alphaPixels, Decode([.. alpha, .. Trailer(alpha.Length, attributesType: 3, developerAreaLength: 40_000)]));

        // A future, longer extension area keeps the attributes type at the same offset
        Assert.Equal(alphaPixels, Decode([.. alpha, .. Trailer(alpha.Length, attributesType: 3, extensionSize: 600)]));

        // Without the signature in the last 18 bytes there is no footer: a TGA 1.0 file, whose trailing bytes mean nothing
        var notAFooter = Trailer(alpha.Length, attributesType: 4);
        notAFooter[^1] = (byte)'!';
        Assert.Equal(alphaPixels, Decode([.. alpha, .. notAFooter]));
        Assert.Equal(alphaPixels, Decode([.. alpha, .. Trailer(alpha.Length, attributesType: 4), 0]));
        for (var length = 1; length < 26; length++)
        {
            Assert.Equal(alphaPixels, Decode([.. alpha, .. FooterSignature.AsSpan(0, Math.Min(length, FooterSignature.Length)), .. new byte[Math.Max(0, length - FooterSignature.Length)]]));
        }
    }

    [Fact]
    public void PremultipliedAlphaIsRejectedOnceTheTrailerIsRead()
    {
        // 32-bit pixels, a 16-bit pixel with its alpha bit, and a color map with 32-bit entries
        byte[][] withAlpha =
        [
            Tga(2, 1, 32, 2, [0x30, 0x20, 0x10, 0x40, 0x33, 0x22, 0x11, 0x00], descriptor: 0x08),
            Tga(1, 1, 16, 2, [0xFF, 0xFF], descriptor: 0x01),
            Tga(1, 1, 8, 1, [0], descriptor: 0x08, colorMap: (0, 1, 32), colorMapData: [1, 2, 3, 4]),
        ];
        foreach (var image in withAlpha)
        {
            byte[] data = [.. image, .. Trailer(image.Length, attributesType: 4, developerAreaLength: 7)];
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
            Assert.Equal(ImageFormat.Tga, exception.Format);
            Assert.Equal("Attributes type: premultiplied alpha", exception.Feature);
            Assert.Equal(exception.Feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan })).Feature);

            // The header alone cannot tell: the trailer is the last thing in the file
            Assert.True(Image.Identify(data).MayHaveTransparency);

            // A reader reports the header, then fails instead of returning the frame
            using var stream = new MemoryStream(data);
            using var reader = Image.OpenReader<Rgba32>(stream);
            Assert.Equal(ImageFormat.Tga, reader.Info.Format);
            Assert.Equal(exception.Feature, Assert.Throws<UnsupportedImageFeatureException>(() => reader.ReadFrame()?.Dispose()).Feature);
        }

        // Without an alpha channel nothing is premultiplied: the fourth byte of a 32-bit pixel is not decoded at all
        var opaque = Tga(2, 1, 24, 2, [3, 2, 1, 6, 5, 4]);
        Assert.Equal([new(1, 2, 3), new(4, 5, 6)], Decode([.. opaque, .. Trailer(opaque.Length, attributesType: 4)]));
        var undeclared = Tga(2, 1, 32, 2, [0x30, 0x20, 0x10, 0x40, 0x33, 0x22, 0x11, 0x00]);
        Assert.Equal([new(0x10, 0x20, 0x30), new(0x11, 0x22, 0x33)], Decode([.. undeclared, .. Trailer(undeclared.Length, attributesType: 4)]));
    }

    [Fact]
    public void AFooterThatMislocatesTheExtensionAreaIsMalformed()
    {
        var image = Tga(2, 1, 32, 2, [0x30, 0x20, 0x10, 0x40, 0x33, 0x22, 0x11, 0x00], descriptor: 0x08);
        var end = image.Length;
        var footer = end + 495;

        // Inside the header or the image data, overlapping the footer, at the footer, and past the end of the file
        foreach (var offset in new long[] { 1, 18, end - 1, end + 1, footer, footer + 26, uint.MaxValue })
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load([.. image, .. Trailer(end, attributesType: 3, extensionOffset: offset)]));
            Assert.Equal(ImageFormat.Tga, exception.Format);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify([.. image, .. Trailer(end, attributesType: 3, extensionOffset: offset)], new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        }

        // An extension area too short to hold an attributes type
        foreach (var size in new[] { 0, 494 })
        {
            Assert.Throws<InvalidImageContentException>(() => Image.Load([.. image, .. Trailer(end, attributesType: 3, extensionSize: size)]));
        }

        // A malformed trailer is reported before the attributes type it does not locate
        Assert.Throws<InvalidImageContentException>(() => Image.Load([.. image, .. Trailer(end, attributesType: 4, extensionSize: 494)]));
    }

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task TheTrailerIsReadTheSameWayFromEveryInput(InputVariant variant)
    {
        // The footer is found from the end of the input, which a stream only reports once it is read past: that must cost
        // neither more encoded bytes than the file holds nor a different failure than from a span
        var image = Tga(2, 1, 32, 10, [0x81, 0x30, 0x20, 0x10, 0x40], descriptor: 0x08);
        var end = image.Length;
        byte[][] files =
        [
            image,
            [.. image, 1, 2, 3],
            [.. image, .. Trailer(end)],
            [.. image, .. Trailer(end, attributesType: 3)],
            [.. image, .. Trailer(end, attributesType: 3, developerAreaLength: 40_000)],
            [.. image, .. Trailer(end, attributesType: 4)],
            [.. image, .. Trailer(end, attributesType: 3, extensionOffset: 1)],
        ];
        foreach (var file in files)
        {
            await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, file, ImageFormat.Tga, XunitCancellationToken);

            // A limit before the end of the image data, at it, and inside the trailer
            foreach (var limit in new[] { end - 1, end, end + 1, end + ((file.Length - end) / 2) })
            {
                var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = limit } };
                var expected = await InputVariants.DescribeOutcomeAsync(InputVariant.Span, file, ImageFormat.Tga, configuration, XunitCancellationToken);
                Assert.Equal(expected, await InputVariants.DescribeOutcomeAsync(variant, file, ImageFormat.Tga, configuration, XunitCancellationToken));
                if (limit < file.Length)
                {
                    Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"load: ImageResourceLimitException(EncodedBytes, limit {limit}, requested {limit + 1})"), expected);
                }
            }
        }
    }

    [Fact]
    public async Task TheTrailerIsTheOnlyPartOfTheFileThatIsBuffered()
    {
        using var source = CreateImage(512, 256, seed: 4);
        var image = Save(source, new TgaEncoder { Compression = TgaCompression.RunLength });
        var end = image.Length - 26;
        byte[] data = [.. image.AsSpan(0, end), .. Trailer(end, attributesType: 3, developerAreaLength: 100_000)];
        await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1024 };
        using var loaded = await Image.LoadAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
        Assert.Equal(data.Length, stream.BytesRead);
        var scope = loaded.Owner.Scope;
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.True(state >= 100_000 + 495 + 26, $"Peak decoder state and input buffer {state} bytes for a {data.Length - end}-byte trailer.");
        Assert.True(state < 3 * (data.Length - end), $"Peak decoder state and input buffer {state} bytes for a {data.Length - end}-byte trailer of a {data.Length}-byte input.");
    }

    [Fact]
    public void TruncationBeforeTheEndOfTheImageDataIsReported()
    {
        var data = Tga(3, 2, 24, 10, [0x82, 1, 2, 3, 0x02, 4, 5, 6, 7, 8, 9, 10, 11, 12], descriptor: 0x20);
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
        var data = Tga(8, 4, 24, 2, new byte[8 * 4 * 3]);
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 4 } };
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration })).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = configuration })).Kind);
    }

    [Fact]
    public async Task StreamingKeepsTwoRowsOfDecoderStateAndNeverBuffersTheFile()
    {
        using var source = CreateImage(512, 256, seed: 4);
        var data = Save(source, new TgaEncoder { Compression = TgaCompression.RunLength });
        await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1024 };
        using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
        var scope = image.Owner.Scope;
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.True(state < 64 * 1024, $"Peak decoder state and input buffer {state} bytes for a {data.Length}-byte input.");
    }

    [Theory]
    [InlineData(PixelFormat.Rgb24, TgaCompression.None, 2, 24)]
    [InlineData(PixelFormat.Rgb24, TgaCompression.RunLength, 10, 24)]
    [InlineData(PixelFormat.Rgba32, TgaCompression.None, 2, 32)]
    [InlineData(PixelFormat.Bgra32, TgaCompression.RunLength, 10, 32)]
    [InlineData(PixelFormat.Gray8, TgaCompression.None, 3, 8)]
    [InlineData(PixelFormat.Gray8, TgaCompression.RunLength, 11, 8)]
    public void TheImageTypeAndDepthFollowThePixelFormat(PixelFormat format, TgaCompression compression, int expectedType, int expectedDepth)
    {
        using var source = CreateImage(9, 5, seed: (int)format);
        using var image = ConvertTo(source, format);
        var reference = ReferenceTga.Parse(Save(image, new TgaEncoder { Compression = compression }));
        Assert.Equal(expectedType, reference.ImageType);
        Assert.Equal(expectedDepth, reference.PixelDepth);
        Assert.Equal(expectedDepth == 32 ? 8 : 0, reference.AlphaBits);
        Assert.Equal(0, reference.Descriptor & 0x30); // the origin is the bottom left
        Assert.Equal(26, reference.TrailingBytes); // the TGA 2.0 footer only
    }

    [Fact]
    public void RunLengthOutputIsLosslessAndSmallerOnFlatImages()
    {
        var pixels = new Rgba32[64 * 8];
        Array.Fill(pixels, new Rgba32(7, 8, 9, 255));
        using var image = Image.ImportPixelData<Rgba32>(pixels, 64, 8);
        var raw = Save(image, new TgaEncoder());
        var packed = Save(image, new TgaEncoder { Compression = TgaCompression.RunLength });
        int packedLength = packed.Length, rawLength = raw.Length;
        Assert.True(packedLength < rawLength, $"{packedLength} bytes is not smaller than {rawLength}.");
        using var decoded = Image.Load<Rgba32>(packed);
        Assert.Equal(pixels, GetPixels(decoded.Frames[0]));
    }

    [Fact]
    public void SixteenBitSourcesNeedExplicitBitDepthReduction()
    {
        using var source = CreateImage(4, 2, seed: 12);
        using var wide = source.CloneAs<Rgba64>();
        Assert.Equal("Bit depth reduction", Assert.Throws<UnsupportedImageFeatureException>(() => Save(wide, new TgaEncoder())).Feature);
        Assert.Equal(32, ReferenceTga.Parse(Save(wide, new TgaEncoder { AllowBitDepthReduction = true })).PixelDepth);
    }

    [Fact]
    public void AnimationsAndMetadataAreRejected()
    {
        using var image = CreateImage(3, 2, seed: 5);
        image.AppendFrame();
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new TgaEncoder()));

        using var still = CreateImage(3, 2, seed: 5);
        still.Metadata.Resolution = new ImageResolution(72, 72);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(still, new TgaEncoder()));
        Assert.NotEmpty(Save(still, new TgaEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
    }

    [Fact]
    public void EncodedFilesRoundTripThroughTheLibrary()
    {
        using var source = CreateImage(17, 9, seed: 2);
        foreach (var compression in new[] { TgaCompression.None, TgaCompression.RunLength })
        {
            using var decoded = Image.Load<Rgba32>(Save(source, new TgaEncoder { Compression = compression }));
            Assert.Equal(GetPixels(source.Frames[0]), GetPixels(decoded.Frames[0]));
        }
    }

    private static byte[] Save(Image image, TgaEncoder encoder)
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

    /// <summary>
    /// Assembles the bytes that follow the image data of a TGA 2.0 file: an optional developer area (a directory without
    /// tags and filler), an optional 495-byte extension area, and the 26-byte footer that locates them.
    /// </summary>
    /// <param name="imageDataEnd">The offset of the first byte after the image data.</param>
    /// <param name="attributesType">The attributes type of the extension area, or <see langword="null"/> for no extension area.</param>
    /// <param name="developerAreaLength">The length of the developer area, stored before the extension area.</param>
    /// <param name="extensionSize">The size the extension area declares.</param>
    /// <param name="extensionOffset">The extension offset of the footer, or <see langword="null"/> for the actual one.</param>
    private static byte[] Trailer(int imageDataEnd, int? attributesType = null, int developerAreaLength = 0, int extensionSize = 495, long? extensionOffset = null)
    {
        var developerArea = new byte[developerAreaLength];
        developerArea.AsSpan(Math.Min(2, developerAreaLength)).Fill(0xD0);
        var extensionArea = new byte[attributesType is null ? 0 : 495];
        if (attributesType is { } type)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(extensionArea, (ushort)extensionSize);
            extensionArea[494] = (byte)type;
        }

        var footer = new byte[26];
        BinaryPrimitives.WriteUInt32LittleEndian(footer, (uint)(extensionOffset ?? (attributesType is null ? 0 : imageDataEnd + developerAreaLength)));
        BinaryPrimitives.WriteUInt32LittleEndian(footer.AsSpan(4), developerAreaLength == 0 ? 0u : (uint)imageDataEnd);
        FooterSignature.CopyTo(footer, 8);
        return [.. developerArea, .. extensionArea, .. footer];
    }

    /// <summary>Assembles a TGA file; <paramref name="body"/> is the stored pixel data or packet stream.</summary>
    private static byte[] Tga(int width, int height, int pixelDepth, int imageType, byte[] body, byte descriptor = 0,
        (int First, int Length, int EntryBits)? colorMap = null, byte[]? colorMapData = null, byte[]? identification = null)
    {
        identification ??= [];
        colorMapData ??= [];
        var header = new byte[18];
        header[0] = (byte)identification.Length;
        header[1] = colorMap is null ? (byte)0 : (byte)1;
        header[2] = (byte)imageType;
        if (colorMap is { } map)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(3), (ushort)map.First);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)map.Length);
            header[7] = (byte)map.EntryBits;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = (byte)pixelDepth;
        header[17] = descriptor;
        return [.. header, .. identification, .. colorMapData, .. body];
    }
}
