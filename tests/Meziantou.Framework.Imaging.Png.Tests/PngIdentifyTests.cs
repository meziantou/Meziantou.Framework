using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>PNG/APNG structure identification on synthetic inputs: modes, validation, error categories, metadata and limits.</summary>
public sealed class PngIdentifyTests
{
    private static readonly ImageIdentifyOptions FullScan = new() { Mode = ImageIdentifyMode.FullScan };

    [Theory]
    [InlineData((byte)0, (byte)1, PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData((byte)0, (byte)16, PixelFormat.Gray16, ImageColorModel.Grayscale)]
    [InlineData((byte)2, (byte)8, PixelFormat.Rgb24, ImageColorModel.Rgb)]
    [InlineData((byte)2, (byte)16, PixelFormat.Rgba64, ImageColorModel.Rgb)]
    [InlineData((byte)4, (byte)8, PixelFormat.Rgba32, ImageColorModel.GrayscaleAlpha)]
    [InlineData((byte)6, (byte)16, PixelFormat.Rgba64, ImageColorModel.Rgba)]
    public void StaticImagesAreIdentifiedInBothModes(byte colorType, byte bitDepth, PixelFormat pixelFormat, ImageColorModel colorModel)
    {
        var data = SyntheticImages.Png(3, 2, colorType, bitDepth);
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var info = Image.Identify(data, options);
            Assert.Equal(ImageFormat.Png, info.Format);
            Assert.Equal(new Size(3, 2), info.Size);
            Assert.Equal(pixelFormat, info.PixelFormat);
            Assert.Equal(colorModel, info.ColorModel);
            Assert.Equal(bitDepth, info.BitsPerComponent);
            Assert.Equal(1, info.FrameCount);
            Assert.False(info.IsAnimated);
            Assert.False(info.HasPosterFrame);
            Assert.Equal(colorType is 4 or 6, info.MayHaveTransparency);
            Assert.Null(info.Animation);
            Assert.Equal(options.Mode, info.IdentifyMode);
        }
    }

    [Fact]
    public void HeaderModeExaminesOnlyTheBytesBeforeTheImageData()
    {
        // Signature (8) + IHDR (25): the walk peeks the 8-byte IDAT chunk header and stops there
        var data = SyntheticImages.Png(4, 4);
        var info = Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxEncodedBytes = 41 }, ImageIdentifyMode.Header));
        Assert.Equal(4, info.Width);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxEncodedBytes = 40 }, ImageIdentifyMode.Header))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxEncodedBytes = 41 }, ImageIdentifyMode.FullScan))).Kind);

        // A truncated or corrupt payload is not examined by a header identification
        var truncated = data[..45];
        Assert.Equal(4, Image.Identify(truncated).Width);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(truncated, FullScan));
    }

    [Fact]
    public void FullScanAllocatesNoPixels()
    {
        // A 10000x10000 RGBA canvas would need 400 MB of pixels; a full scan only walks the chunks
        var data = BuildLargeApng();
        _ = Image.Identify(data, FullScan);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var info = Image.Identify(data, FullScan);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3, info.FrameCount);
        Assert.True(allocated < 256 * 1024, string.Create(CultureInfo.InvariantCulture, $"A full scan allocated {allocated} bytes."));

        static byte[] BuildLargeApng()
        {
            var compressed = SyntheticImages.Zlib(new byte[1024]);
            return new SyntheticImages.PngBuilder()
                .Header(10_000, 10_000)
                .AnimationControl(3, 0)
                .FrameControl(10_000, 10_000)
                .Chunk("IDAT", compressed)
                .FrameControl(5_000, 5_000)
                .Chunk("fdAT", [0, 0, 0, 2, .. compressed])
                .FrameControl(10_000, 10_000, sequence: 3)
                .Chunk("fdAT", [0, 0, 0, 4, .. compressed])
                .End()
                .ToArray();
        }
    }

    [Theory]
    [InlineData((byte)2, (byte)4)]
    [InlineData((byte)3, (byte)16)]
    [InlineData((byte)7, (byte)8)]
    [InlineData((byte)0, (byte)3)]
    public void IllegalColorTypeAndBitDepthCombinationsAreInvalid(byte colorType, byte bitDepth)
    {
        var data = new SyntheticImages.PngBuilder().Header(2, 2, bitDepth, colorType).ImageData(2, 2).End().ToArray();
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Equal(ImageFormat.Png, exception.Format);
    }

    public static TheoryData<string, byte[]> InvalidStructures => new()
    {
        { "first chunk not IHDR", new SyntheticImages.PngBuilder().Text("Title", "x").Header(2, 2).ImageData(2, 2).End().ToArray() },
        { "zero width", new SyntheticImages.PngBuilder().Header(0, 2).ImageData(2, 2).End().ToArray() },
        { "IHDR length", new SyntheticImages.PngBuilder().Chunk("IHDR", new byte[12]).ImageData(2, 2).End().ToArray() },
        { "corrupt IHDR CRC", new SyntheticImages.PngBuilder().Chunk("IHDR", Ihdr(2, 2, 8, 6), corruptCrc: true).ImageData(2, 2).End().ToArray() },
        { "corrupt ancillary CRC", new SyntheticImages.PngBuilder().Header(2, 2).Chunk("tEXt", "Title\0x"u8, corruptCrc: true).ImageData(2, 2).End().ToArray() },
        { "non-letter chunk type", new SyntheticImages.PngBuilder().Header(2, 2).Chunk("te1t", []).ImageData(2, 2).End().ToArray() },
        { "palette image without PLTE", new SyntheticImages.PngBuilder().Header(2, 2, 8, 3).ImageData(2, 2, 3).End().ToArray() },
        { "PLTE in a grayscale image", new SyntheticImages.PngBuilder().Header(2, 2, 8, 0).Chunk("PLTE", new byte[6]).ImageData(2, 2, 0).End().ToArray() },
        { "PLTE larger than the bit depth allows", new SyntheticImages.PngBuilder().Header(2, 2, 1, 3).Chunk("PLTE", new byte[9]).ImageData(2, 2, 3, 1).End().ToArray() },
        { "tRNS before PLTE", new SyntheticImages.PngBuilder().Header(2, 2, 8, 3).Chunk("tRNS", [0]).Chunk("PLTE", new byte[6]).ImageData(2, 2, 3).End().ToArray() },
        { "tRNS length", new SyntheticImages.PngBuilder().Header(2, 2, 8, 2).Chunk("tRNS", [0, 0]).ImageData(2, 2, 2).End().ToArray() },
        { "no IDAT", new SyntheticImages.PngBuilder().Header(2, 2).End().ToArray() },
    };

    [Theory]
    [MemberData(nameof(InvalidStructures))]
    public void MalformedStructuresAreInvalidInBothModes(string name, byte[] data)
    {
        Assert.NotNull(name);
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, options));
            Assert.Equal(ImageFormat.Png, exception.Format);
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
    }

    public static TheoryData<string, byte[]> InvalidAfterImageData => new()
    {
        { "IEND with data", new SyntheticImages.PngBuilder().Header(2, 2).ImageData(2, 2).Chunk("IEND", [0]).ToArray() },
        { "non-consecutive IDAT", new SyntheticImages.PngBuilder().Header(2, 2).ImageData(2, 2).Text("a", "b").ImageData(2, 2).End().ToArray() },
        { "missing IEND", new SyntheticImages.PngBuilder().Header(2, 2).ImageData(2, 2).ToArray() },
        { "truncated IDAT", new SyntheticImages.PngBuilder().Header(2, 2).ImageData(2, 2).ToArray()[..^3] },
        { "corrupt IDAT CRC", new SyntheticImages.PngBuilder().Header(2, 2).Chunk("IDAT", SyntheticImages.Zlib(new byte[18]), corruptCrc: true).End().ToArray() },
        { "APNG sequence", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(2, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2, sequence: 5).FrameData(2, 2).End().ToArray() },
        { "APNG frame outside the canvas", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(2, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2, x: 1).FrameData(2, 2).End().ToArray() },
        { "APNG dispose op", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(2, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2, dispose: 3).FrameData(2, 2).End().ToArray() },
        { "APNG fewer frames than declared", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(3, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2).FrameData(2, 2).End().ToArray() },
        { "APNG more frames than declared", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(1, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2).FrameData(2, 2).End().ToArray() },
        { "APNG frame without data", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(3, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2).FrameControl(2, 2).FrameData(2, 2).End().ToArray() },
        { "APNG fdAT without fcTL", new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(1, 0).ImageData(2, 2).FrameData(2, 2).End().ToArray() },
    };

    [Theory]
    [MemberData(nameof(InvalidAfterImageData))]
    public void MalformedDataAfterTheHeaderIsFoundByAFullScan(string name, byte[] data)
    {
        Assert.NotNull(name);
        _ = Image.Identify(data);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, FullScan));
        Assert.Equal(ImageFormat.Png, exception.Format);
    }

    [Fact]
    public void UnknownCriticalChunksAreUnsupportedAndAncillaryChunksAreSkipped()
    {
        var critical = new SyntheticImages.PngBuilder().Header(2, 2).Chunk("XyZw", [1, 2, 3]).ImageData(2, 2).End().ToArray();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(critical));
        Assert.Equal(ImageFormat.Png, exception.Format);
        Assert.Equal("PNG critical chunk XyZw", exception.Feature);

        var ancillary = new SyntheticImages.PngBuilder().Header(2, 2).Chunk("xyZw", new byte[100_000]).ImageData(2, 2).Chunk("abCd", [1]).End().ToArray();
        Assert.Equal(1, Image.Identify(ancillary, FullScan).FrameCount);
    }

    [Fact]
    public void AnimationControlAfterTheImageDataIsIgnored()
    {
        var data = new SyntheticImages.PngBuilder().Header(2, 2).ImageData(2, 2).AnimationControl(2, 0).FrameControl(2, 2).FrameData(2, 2).End().ToArray();
        var info = Image.Identify(data, FullScan);
        Assert.False(info.IsAnimated);
        Assert.Equal(1, info.FrameCount);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
    }

    [Fact]
    public void ApngHeaderDeclaresFramesPlaysAndPoster()
    {
        var poster = new SyntheticImages.PngBuilder().Header(4, 4, 8, 2).AnimationControl(2, 3).ImageData(4, 4, 2).FrameControl(2, 2, x: 1, y: 1).FrameData(2, 2).FrameControl(4, 4).FrameData(4, 4).End().ToArray();
        var header = Image.Identify(poster);
        Assert.True(header.IsAnimated);
        Assert.True(header.HasPosterFrame);
        Assert.Equal(2, header.FrameCount);
        Assert.Equal(3, header.Animation?.TotalPlays);
        Assert.Equal(PixelFormat.Rgba32, header.PixelFormat);
        Assert.Null(header.MayHaveTransparency);

        // The first displayed frame does not cover the canvas: transparent black shows through
        var full = Image.Identify(poster, FullScan);
        Assert.Equal(2, full.FrameCount);
        Assert.True(full.MayHaveTransparency);

        var opaque = new SyntheticImages.PngBuilder().Header(4, 4, 16, 2).AnimationControl(2, 0).FrameControl(4, 4).ImageData(4, 4, 2, 16).FrameControl(2, 2, dispose: 1).FrameData(2, 2).End().ToArray();
        var opaqueInfo = Image.Identify(opaque, FullScan);
        Assert.Equal(PixelFormat.Rgba64, opaqueInfo.PixelFormat);
        Assert.Null(opaqueInfo.Animation!.TotalPlays);
        Assert.False(opaqueInfo.MayHaveTransparency, "Disposing the last frame never shows the background.");

        var disposed = new SyntheticImages.PngBuilder().Header(4, 4, 8, 2).AnimationControl(2, 0).FrameControl(4, 4, dispose: 1).ImageData(4, 4, 2).FrameControl(2, 2).FrameData(2, 2).End().ToArray();
        Assert.True(Image.Identify(disposed, FullScan).MayHaveTransparency);
    }

    [Fact]
    public void DefaultImageFrameControlMustCoverTheCanvas()
    {
        var data = new SyntheticImages.PngBuilder().Header(4, 4).AnimationControl(1, 0).FrameControl(2, 2).ImageData(4, 4).End().ToArray();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        var zeroFrames = new SyntheticImages.PngBuilder().Header(4, 4).AnimationControl(0, 0).ImageData(4, 4).End().ToArray();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(zeroFrames));
    }

    [Fact]
    public void FullScanChargesEveryFrameIncludingThePoster()
    {
        var data = new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(2, 0).ImageData(2, 2).FrameControl(2, 2).FrameData(2, 2).FrameControl(2, 2).FrameData(2, 2).End().ToArray();
        Assert.Equal(2, Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxFrames = 3 }, ImageIdentifyMode.FullScan)).FrameCount);
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxFrames = 2 }, ImageIdentifyMode.FullScan)));
        Assert.Equal(ImageResourceLimitKind.Frames, exception.Kind);
        Assert.Equal(3, exception.Requested);

        // A header identification traverses no frame
        Assert.Equal(2, Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxFrames = 1 }, ImageIdentifyMode.Header)).FrameCount);
    }

    [Fact]
    public void CanvasLimitsApplyToIdentification()
    {
        var data = SyntheticImages.Png(40, 30);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxHeight = 29 }, ImageIdentifyMode.Header))).Kind);
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxFramePixels = 1199 }, ImageIdentifyMode.Header))).Kind);
        Assert.Equal(40, Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxFramePixels = 1200, MaxWidth = 40, MaxHeight = 30 }, ImageIdentifyMode.Header)).Width);
    }

    [Fact]
    public void TextChunksAreDecodedInFileOrderAndBounded()
    {
        var value = new string('a', 1000);
        var data = new SyntheticImages.PngBuilder()
            .Header(2, 2)
            .Text("Title", "Before é")
            .CompressedText("Comment", Encoding.Latin1.GetBytes(value))
            .Chunk("iTXt", [.. "Author"u8, 0, 0, 0, .. "fr"u8, 0, .. "Auteur"u8, 0, .. "Jérôme"u8.ToArray()])
            .Chunk("iTXt", [.. "XML:com.adobe.xmp"u8, 0, 1, 0, 0, 0, .. SyntheticImages.Zlib("<x:xmpmeta/>"u8)])
            .ImageData(2, 2)
            .Text("After", "image data")
            .End()
            .ToArray();

        var header = Image.Identify(data).Metadata;
        Assert.Equal(["Title", "Comment", "Author"], header.TextEntries.Select(entry => entry.Keyword));
        Assert.Equal("Before é", header.TextEntries[0].Value);
        Assert.Equal(value, header.TextEntries[1].Value);
        Assert.Equal(new ImageTextEntry("Author", "Jérôme", "fr", "Auteur"), header.TextEntries[2]);
        Assert.Equal("<x:xmpmeta/>"u8.ToArray(), header.XmpProfile?.Data.ToArray());

        var full = Image.Identify(data, FullScan).Metadata;
        Assert.Equal(["Title", "Comment", "Author", "After"], full.TextEntries.Select(entry => entry.Keyword));
    }

    [Fact]
    public void DecompressedMetadataIsChargedBeforeItIsRetained()
    {
        // zTXt: 7-byte keyword + 100,000 decompressed bytes from a few hundred compressed bytes
        var data = new SyntheticImages.PngBuilder().Header(2, 2).CompressedText("Comment", new byte[100_000]).ImageData(2, 2).End().ToArray();
        Assert.Single(Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxMetadataBytes = 100_007 }, ImageIdentifyMode.Header)).Metadata.TextEntries);
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, CreateOptions(new ImageResourceLimits { MaxMetadataBytes = 100_006 }, ImageIdentifyMode.Header)));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);

        var bomb = new SyntheticImages.PngBuilder().Header(2, 2).CompressedText("Comment", new byte[10_000_000]).ImageData(2, 2).End().ToArray();
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(bomb, CreateOptions(new ImageResourceLimits { MaxMetadataBytes = 65_536 }, ImageIdentifyMode.Header))).Kind);
    }

    [Fact]
    public void InvalidOrMismatchedPayloadsAreNotAdopted()
    {
        var grayProfile = TestRawImage.CreateIccHeader("GRAY"u8);
        var data = new SyntheticImages.PngBuilder()
            .Header(2, 2, 8, 2)
            .Chunk("iCCP", [.. "gray"u8, 0, 0, .. SyntheticImages.Zlib(grayProfile)]) // valid profile, wrong color space for RGB samples
            .Chunk("eXIf", [1, 2, 3, 4])
            .Chunk("zTXt", [.. "Comment"u8, 0, 0, 1, 2, 3, 4, 5, 6])
            .Chunk("tEXt", [0, .. "no keyword"u8])
            .ImageData(2, 2, 2)
            .End()
            .ToArray();

        var metadata = Image.Identify(data, FullScan).Metadata;
        Assert.Null(metadata.IccProfile);
        Assert.Null(metadata.ExifProfile);
        Assert.Empty(metadata.TextEntries);

        var gray = new SyntheticImages.PngBuilder().Header(2, 2, 8, 0).Chunk("iCCP", [.. "gray"u8, 0, 0, .. SyntheticImages.Zlib(grayProfile)]).ImageData(2, 2, 0).End().ToArray();
        Assert.Equal(IccProfileColorSpace.Gray, Image.Identify(gray).Metadata.IccProfile?.ColorSpace);
    }

    [Fact]
    public void TruncatedInputsAreInvalidAtEveryLength()
    {
        var data = SyntheticImages.Apng(3, 3, frames: 2);
        for (var length = 8; length < data.Length; length++)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data.AsSpan(0, length), FullScan));
            Assert.Equal(ImageFormat.Png, exception.Format);
        }

        for (var length = 0; length < 8; length++)
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.Identify(data.AsSpan(0, length)));
        }
    }

    private static ImageIdentifyOptions CreateOptions(ImageResourceLimits limits, ImageIdentifyMode mode) => new() { Mode = mode, Configuration = new ImageConfiguration { Limits = limits } };

    private static byte[] Ihdr(int width, int height, byte bitDepth, byte colorType)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(data, width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), height);
        data[8] = bitDepth;
        data[9] = colorType;
        return data;
    }
}
