using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>JPEG marker identification on synthetic inputs: frame headers, unsupported modes, segment validation, scans and metadata.</summary>
public sealed class JpegIdentifyTests
{
    private static readonly ImageIdentifyOptions FullScan = new() { Mode = ImageIdentifyMode.FullScan };

    [Theory]
    [InlineData((byte)0xC0, 3, PixelFormat.Rgb24, ImageColorModel.YCbCr)]
    [InlineData((byte)0xC1, 3, PixelFormat.Rgb24, ImageColorModel.YCbCr)]
    [InlineData((byte)0xC2, 3, PixelFormat.Rgb24, ImageColorModel.YCbCr)]
    [InlineData((byte)0xC0, 1, PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData((byte)0xC2, 1, PixelFormat.Gray8, ImageColorModel.Grayscale)]
    public void SupportedFramesAreIdentifiedInBothModes(byte marker, int components, PixelFormat pixelFormat, ImageColorModel colorModel)
    {
        var data = SyntheticImages.Jpeg(marker, components, width: 17, height: 9);
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var info = Image.Identify(data, options);
            Assert.Equal(ImageFormat.Jpeg, info.Format);
            Assert.Equal(new Size(17, 9), info.Size);
            Assert.Equal(pixelFormat, info.PixelFormat);
            Assert.Equal(colorModel, info.ColorModel);
            Assert.Equal(8, info.BitsPerComponent);
            Assert.Equal(1, info.FrameCount);
            Assert.False(info.IsAnimated);
            Assert.False(info.HasPosterFrame);
            Assert.False(info.MayHaveTransparency);
            Assert.Null(info.Animation);
            Assert.Equal(new ImageResolution(72, 72), info.Metadata.Resolution);
        }
    }

    [Theory]
    [InlineData((byte)0xC3, 3, 8, "JPEG lossless")]
    [InlineData((byte)0xC5, 3, 8, "JPEG hierarchical")]
    [InlineData((byte)0xC7, 3, 8, "JPEG hierarchical")]
    [InlineData((byte)0xC9, 3, 8, "JPEG arithmetic coding")]
    [InlineData((byte)0xCA, 1, 8, "JPEG arithmetic coding")]
    [InlineData((byte)0xCF, 3, 8, "JPEG arithmetic coding")]
    [InlineData((byte)0xC0, 3, 12, "JPEG 12-bit precision")]
    [InlineData((byte)0xC2, 1, 16, "JPEG 16-bit precision")]
    [InlineData((byte)0xC0, 4, 8, "JPEG CMYK/YCCK")]
    [InlineData((byte)0xC0, 2, 8, "JPEG 2 components")]
    [InlineData((byte)0xF7, 3, 8, "JPEG-LS")]
    public void RecognizedUnsupportedModesAreRejectedExplicitly(byte marker, int components, int precision, string feature)
    {
        var data = SyntheticImages.Jpeg(marker, components, precision);
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data, options));
            Assert.Equal(ImageFormat.Jpeg, exception.Format);
            Assert.Equal(feature, exception.Feature);
        }

        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
    }

    [Fact]
    public void DnlHeightsAndArithmeticConditioningAreUnsupported()
    {
        var dnl = SyntheticImages.Jpeg(0xC0, 3, height: 0);
        Assert.Equal("JPEG DNL height", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(dnl)).Feature);

        var dac = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xCC, [0, 0x11]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray();
        Assert.Equal("JPEG arithmetic coding", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(dac)).Feature);
    }

    public static TheoryData<string, byte[]> InvalidHeaders => new()
    {
        { "scan before frame", new SyntheticImages.JpegBuilder().StartOfImage().Scan(1).EntropyData([0]).EndOfImage().ToArray() },
        { "two frames", new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 1).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray() },
        { "zero sampling factor", new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 1, sampling: 0x01).Scan(1).EndOfImage().ToArray() },
        { "sampling factor 5", new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 1, sampling: 0x51).Scan(1).EndOfImage().ToArray() },
        { "zero width", new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 0, 8, 1).Scan(1).EndOfImage().ToArray() },
        { "frame length", new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xC0, [8, 0, 8, 0, 8, 2, 1, 0x11, 0]).Scan(1).EndOfImage().ToArray() },
        { "segment length", new SyntheticImages.JpegBuilder().StartOfImage().Raw([0xFF, 0xE0, 0x00, 0x01]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray() },
        { "reserved marker", new SyntheticImages.JpegBuilder().StartOfImage().Segment(0x02, [0]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray() },
        { "restart marker outside a scan", new SyntheticImages.JpegBuilder().StartOfImage().Raw([0xFF, 0xD3]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray() },
        { "data instead of a marker", new SyntheticImages.JpegBuilder().StartOfImage().Jfif().Raw([0x12, 0x34]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray() },
        { "no scan", new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 1).EndOfImage().ToArray() },
        { "DRI length", new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xDD, [0, 1, 2]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray() },
    };

    [Theory]
    [MemberData(nameof(InvalidHeaders))]
    public void MalformedMarkersAreInvalidInBothModes(string name, byte[] data)
    {
        Assert.NotNull(name);
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, options));
            Assert.Equal(ImageFormat.Jpeg, exception.Format);
        }
    }

    [Fact]
    public void ScanHeadersAreValidatedByAFullScan()
    {
        var unknownComponent = new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 1).Scan(1, firstComponent: 9).EndOfImage().ToArray();
        var duplicateComponent = new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 3).Segment(0xDA, [2, 1, 0, 1, 0, 0, 63, 0]).EndOfImage().ToArray();
        var badLength = new SyntheticImages.JpegBuilder().StartOfImage().Frame(0xC0, 8, 8, 8, 1).Segment(0xDA, [1, 1, 0, 0, 63]).EndOfImage().ToArray();
        foreach (var data in new[] { unknownComponent, duplicateComponent, badLength })
        {
            // The header walk stops before the first scan header
            Assert.Equal(8, Image.Identify(data).Width);
            Assert.Equal(ImageFormat.Jpeg, Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, FullScan)).Format);
        }
    }

    [Fact]
    public void SamplingFactorsMustDivideTheLargestFactors()
    {
        // Y 3x1 with chroma 2x1: fractional upsampling ratios are a recognized unsupported mode (also for a header identification)
        var fractional = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xC0, [8, 0, 8, 0, 8, 3, 1, 0x31, 0, 2, 0x21, 0, 3, 0x21, 0]).Scan(3).EndOfImage().ToArray();
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            Assert.Equal("JPEG non-integral sampling ratios", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(fractional, options)).Feature);
        }

        // Integral ratios of any size are identified (Y 4x1 with chroma 2x1 and 1x1)
        var integral = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xC0, [8, 0, 8, 0, 8, 3, 1, 0x41, 0, 2, 0x21, 0, 3, 0x11, 0]).Scan(3).EndOfImage().ToArray();
        Assert.Equal(8, Image.Identify(integral, FullScan).Width);
    }

    [Fact]
    public void InterleavedScansHaveAtMostTenBlocksPerMcu()
    {
        // Y 4x2 (8 blocks) + Cb 2x1 (2) + Cr 1x1 (1) = 11 blocks: a structure defect found at the scan header (after the header walk)
        var data = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xC0, [8, 0, 8, 0, 8, 3, 1, 0x42, 0, 2, 0x21, 0, 3, 0x11, 0]).Scan(3).EndOfImage().ToArray();
        Assert.Equal(8, Image.Identify(data).Width);
        Assert.Contains("10 blocks", Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, FullScan)).Message, StringComparison.Ordinal);

        // The same components in separate scans are valid
        var separate = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xC0, [8, 0, 8, 0, 8, 3, 1, 0x42, 0, 2, 0x21, 0, 3, 0x11, 0]).Scan(1).Scan(1, firstComponent: 2).Scan(1, firstComponent: 3).EndOfImage().ToArray();
        Assert.Equal(8, Image.Identify(separate, FullScan).Width);
    }

    [Fact]
    public void EntropyCodedSegmentsAreDelimitedWithoutDecoding()
    {
        // Stuffed bytes, restart markers and fill bytes are part of the scan; progressive files have several scans
        var data = new SyntheticImages.JpegBuilder()
            .StartOfImage()
            .Frame(0xC2, 8, 8, 8, 3)
            .Scan(3, end: 0).EntropyData([0xFF, 0x00, 0xFF, 0xD7, 0x01, 0xFF, 0xFF])
            .HuffmanTable()
            .Scan(1, start: 1, end: 63).EntropyData([0x01, 0x02])
            .Segment(0xFE, "after the scans"u8)
            .EndOfImage()
            .Raw("trailing bytes are not examined"u8)
            .ToArray();

        var full = Image.Identify(data, FullScan);
        Assert.Equal("after the scans", Assert.Single(full.Metadata.TextEntries).Value);
        Assert.Empty(Image.Identify(data).Metadata.TextEntries);

        var truncated = data[..^33];
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(truncated, FullScan));
        Assert.Equal(8, Image.Identify(truncated).Width);
    }

    [Fact]
    public void ColorModelFollowsAdobeAndComponentIdentifiers()
    {
        var adobeRgb = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 0]).Frame(0xC0, 8, 8, 8, 3).Scan(3).EndOfImage().ToArray();
        Assert.Equal(ImageColorModel.Rgb, Image.Identify(adobeRgb).ColorModel);
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(adobeRgb).PixelFormat);

        var adobeYcc = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 1]).Frame(0xC0, 8, 8, 8, 3).Scan(3).EndOfImage().ToArray();
        Assert.Equal(ImageColorModel.YCbCr, Image.Identify(adobeYcc).ColorModel);

        // Component identifiers 'R', 'G', 'B' without JFIF
        var rgbIds = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xC0, [8, 0, 8, 0, 8, 3, (byte)'R', 0x11, 0, (byte)'G', 0x11, 0, (byte)'B', 0x11, 0]).Segment(0xDA, [1, (byte)'R', 0, 0, 63, 0]).EndOfImage().ToArray();
        Assert.Equal(ImageColorModel.Rgb, Image.Identify(rgbIds).ColorModel);
    }

    [Fact]
    public void MetadataSegmentsAreDecoded()
    {
        var exif = ExifWithOrientation(6);
        var profile = TestRawImage.CreateIccHeader("RGB "u8);
        var data = new SyntheticImages.JpegBuilder()
            .StartOfImage()
            .Segment(0xE1, [.. "Exif\0\0"u8, .. exif])
            .Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta/>"u8])
            .Segment(0xE2, [.. "ICC_PROFILE\0"u8, 2, 2, .. profile.AsSpan(100)])
            .Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile.AsSpan(0, 100)])
            .Segment(0xE0, [.. "JFIF\0"u8, 1, 2, 2, 0, 118, 0, 59, 0, 0])
            .Segment(0xFE, [0x63, 0x61, 0x66, 0xE9])
            .Frame(0xC0, 8, 8, 8, 3)
            .Scan(3)
            .EndOfImage()
            .ToArray();

        var metadata = Image.Identify(data).Metadata;
        Assert.Equal(ImageFormat.Jpeg, metadata.SourceFormat);
        Assert.Equal(ExifOrientation.RightTop, metadata.Orientation);
        Assert.Equal(exif, metadata.ExifProfile?.Data.ToArray());
        Assert.Equal("<x:xmpmeta/>"u8.ToArray(), metadata.XmpProfile?.Data.ToArray());
        Assert.Equal(profile, metadata.IccProfile?.Data.ToArray());
        Assert.Equal(new ImageResolution(118 * 2.54, 59 * 2.54), metadata.Resolution);
        Assert.Equal("café", Assert.Single(metadata.TextEntries).Value);
    }

    [Fact]
    public void InconsistentIccChunksAreNotAdopted()
    {
        var profile = TestRawImage.CreateIccHeader("RGB "u8);
        var duplicate = new SyntheticImages.JpegBuilder().StartOfImage()
            .Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile.AsSpan(0, 100)])
            .Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile.AsSpan(0, 100)])
            .Frame(0xC0, 8, 8, 8, 3).Scan(3).EndOfImage().ToArray();
        Assert.Null(Image.Identify(duplicate).Metadata.IccProfile);

        var missing = new SyntheticImages.JpegBuilder().StartOfImage()
            .Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 3, .. profile])
            .Frame(0xC0, 8, 8, 8, 3).Scan(3).EndOfImage().ToArray();
        Assert.Null(Image.Identify(missing).Metadata.IccProfile);

        // A grayscale profile cannot label color samples
        var gray = TestRawImage.CreateIccHeader("GRAY"u8);
        var mismatched = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. gray]).Frame(0xC0, 8, 8, 8, 3).Scan(3).EndOfImage().ToArray();
        Assert.Null(Image.Identify(mismatched).Metadata.IccProfile);
        var grayImage = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. gray]).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray();
        Assert.Equal(IccProfileColorSpace.Gray, Image.Identify(grayImage).Metadata.IccProfile?.ColorSpace);
    }

    [Fact]
    public void MetadataSegmentsAreBoundedByTheMetadataLimit()
    {
        var comment = new byte[60_000];
        var data = new SyntheticImages.JpegBuilder().StartOfImage().Segment(0xFE, comment).Segment(0xFE, comment).Frame(0xC0, 8, 8, 8, 1).Scan(1).EndOfImage().ToArray();
        Assert.HasCount(2, Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 120_000 } } }).Metadata.TextEntries);
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 119_999 } } }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
    }

    private static byte[] ExifWithOrientation(ushort orientation)
    {
        // Little-endian TIFF header, IFD0 with one SHORT entry (orientation), no next IFD
        var data = new byte[26];
        "II"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), orientation);
        return data;
    }
}
