using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Save-time metadata policy and validation of caller-supplied payloads.</summary>
public sealed class MetadataWritePlanTests
{
    private static readonly Size Canvas = new(4, 3);

    [Fact]
    public void ValidIccProfilePassesValidation()
    {
        Assert.True(MetadataValidation.TryValidateIccProfile(CreateIccProfile("RGB "), out var error), error);
        Assert.Equal(IccProfileColorSpace.Rgb, new IccProfile(new MetadataBlob(CreateIccProfile("RGB "))).ColorSpace);
        Assert.Equal(IccProfileColorSpace.Gray, new IccProfile(new MetadataBlob(CreateIccProfile("GRAY"))).ColorSpace);
        Assert.Equal(IccProfileColorSpace.Cmyk, new IccProfile(new MetadataBlob(CreateIccProfile("CMYK"))).ColorSpace);
        Assert.Equal(IccProfileColorSpace.Other, new IccProfile(new MetadataBlob(CreateIccProfile("Lab "))).ColorSpace);
    }

    [Fact]
    public void MalformedIccProfilesAreDetected()
    {
        Assert.False(MetadataValidation.TryValidateIccProfile(new byte[100], out _));

        var wrongSize = CreateIccProfile("RGB ");
        BinaryPrimitives.WriteUInt32BigEndian(wrongSize, (uint)wrongSize.Length + 1);
        Assert.False(MetadataValidation.TryValidateIccProfile(wrongSize, out var error));
        Assert.Contains("declares", error, StringComparison.Ordinal);

        var noSignature = CreateIccProfile("RGB ");
        noSignature[36] = (byte)'x';
        Assert.False(MetadataValidation.TryValidateIccProfile(noSignature, out error));
        Assert.Contains("acsp", error, StringComparison.Ordinal);

        var tagOutside = CreateIccProfile("RGB ");
        BinaryPrimitives.WriteUInt32BigEndian(tagOutside.AsSpan(128 + 4 + 8), 1000); // tag size
        Assert.False(MetadataValidation.TryValidateIccProfile(tagOutside, out error));
        Assert.Contains("outside the profile", error, StringComparison.Ordinal);

        var tooManyTags = CreateIccProfile("RGB ");
        BinaryPrimitives.WriteUInt32BigEndian(tooManyTags.AsSpan(128), 100);
        Assert.False(MetadataValidation.TryValidateIccProfile(tooManyTags, out _));
    }

    [Fact]
    public void XmpPacketsMustBeUtf8()
    {
        Assert.True(MetadataValidation.TryValidateXmpPacket("<x:xmpmeta/>"u8, out _));
        Assert.False(MetadataValidation.TryValidateXmpPacket([], out _));
        Assert.False(MetadataValidation.TryValidateXmpPacket([0x3C, 0xFF, 0xFE], out _));
    }

    [Fact]
    public void NoMetadataOrStripWritesNothing()
    {
        Assert.Null(MetadataWritePlan.Create(null, ImageFormat.Png, MetadataHandling.Strict, Canvas).Exif);

        // Strip writes no optional metadata and therefore does not even validate it
        var metadata = CreateFullMetadata();
        metadata.IccProfile = new IccProfile(new MetadataBlob([1, 2, 3]));
        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Gif, ImageFormat.Jpeg })
        {
            var plan = MetadataWritePlan.Create(metadata, format, MetadataHandling.Strip, Canvas);
            Assert.Null(plan.IccProfile);
            Assert.Null(plan.Exif);
            Assert.Null(plan.XmpProfile);
            Assert.Null(plan.Resolution);
            Assert.Empty(plan.TextEntries);
        }
    }

    [Fact]
    public void PngStoresEverySupportedItem()
    {
        var metadata = CreateFullMetadata();
        var plan = MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strict, Canvas);
        Assert.Same(metadata.IccProfile, plan.IccProfile);
        Assert.Same(metadata.XmpProfile, plan.XmpProfile);
        Assert.Same(metadata.Resolution, plan.Resolution);
        Assert.Equal(metadata.TextEntries, plan.TextEntries);
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(plan.Exif)); // typed orientation is authoritative
    }

    [Fact]
    public void OrientationIsSynthesizedOnlyWhenNeeded()
    {
        var plain = new ImageMetadata();
        Assert.Null(MetadataWritePlan.Create(plain, ImageFormat.Png, MetadataHandling.Strict, Canvas).Exif);
        Assert.Null(MetadataWritePlan.Create(plain, ImageFormat.Jpeg, MetadataHandling.Strict, Canvas).Exif);

        var rotated = new ImageMetadata { Orientation = ExifOrientation.LeftBottom };
        Assert.Equal(ExifTiff.CreateMinimal(ExifOrientation.LeftBottom), MetadataWritePlan.Create(rotated, ImageFormat.Jpeg, MetadataHandling.Strict, Canvas).Exif);

        // An existing TopLeft profile is rewritten even if its stored tag says otherwise
        var stale = new ImageMetadata { ExifProfile = new ExifProfile(new MetadataBlob(ExifTiff.CreateMinimal(ExifOrientation.RightTop))) };
        Assert.Equal(ExifOrientation.TopLeft, ExifTiff.ReadOrientation(MetadataWritePlan.Create(stale, ImageFormat.Png, MetadataHandling.Strict, Canvas).Exif));
    }

    [Fact]
    public void GifRejectsUnsupportedMetadataInStrictModeAndDropsItOtherwise()
    {
        AssertUnsupported(new ImageMetadata { Orientation = ExifOrientation.BottomRight }, ImageFormat.Gif, "EXIF orientation other than TopLeft");
        AssertUnsupported(new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(CreateIccProfile("RGB "))) }, ImageFormat.Gif, "ICC color profile");
        AssertUnsupported(new ImageMetadata { ExifProfile = new ExifProfile(new MetadataBlob(ExifTiff.CreateMinimal(ExifOrientation.TopLeft))) }, ImageFormat.Gif, "EXIF profile");
        AssertUnsupported(new ImageMetadata { XmpProfile = new XmpProfile(new MetadataBlob("<x/>"u8)) }, ImageFormat.Gif, "XMP packet");
        AssertUnsupported(new ImageMetadata { Resolution = new ImageResolution(72, 72) }, ImageFormat.Gif, "resolution");
        AssertUnsupported(WithText(new ImageTextEntry("Title", "t")), ImageFormat.Gif, "text entry 'Title'");
        AssertUnsupported(WithText(new ImageTextEntry(ImageTextEntry.CommentKeyword, "c", "en")), ImageFormat.Jpeg, "text entry 'Comment'");
        AssertUnsupported(WithText(new ImageTextEntry(ImageTextEntry.CommentKeyword, "c", translatedKeyword: "Kommentar")), ImageFormat.Gif, "text entry 'Comment'");

        // GIF comments are read back as Latin-1: other characters cannot round-trip; any length is fine (sub-blocks)
        AssertUnsupported(WithText(new ImageTextEntry(ImageTextEntry.CommentKeyword, "snow \u2603")), ImageFormat.Gif, "text entry 'Comment'");
        Assert.Single(MetadataWritePlan.Create(WithText(new ImageTextEntry(ImageTextEntry.CommentKeyword, new string('\u00FF', 70_000))), ImageFormat.Gif, MetadataHandling.Strict, Canvas).TextEntries);

        var comment = new ImageTextEntry(ImageTextEntry.CommentKeyword, "kept");
        var metadata = CreateFullMetadata();
        metadata.TextEntries.Add(comment);
        var gif = MetadataWritePlan.Create(metadata, ImageFormat.Gif, MetadataHandling.DiscardUnsupported, Canvas);
        Assert.Equal([comment], gif.TextEntries);
        Assert.Null(gif.Exif);
        Assert.Null(gif.IccProfile);
    }

    [Theory]
    [InlineData("Title", true)]
    [InlineData("Creation Time", true)]
    [InlineData("Copyright ©", true)] // Latin-1
    [InlineData(" Title", false)]
    [InlineData("Title ", false)]
    [InlineData("Two  spaces", false)]
    [InlineData("Tab\tKeyword", false)]
    [InlineData("Ābc", false)] // outside Latin-1
    [InlineData(" nbsp", false)] // non-breaking space is not allowed
    public void PngKeywordsFollowTheSpecification(string keyword, bool supported)
    {
        var metadata = WithText(new ImageTextEntry(keyword, "value"));
        if (supported)
        {
            Assert.Single(MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strict, Canvas).TextEntries);
        }
        else
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strict, Canvas));
            Assert.Empty(MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.DiscardUnsupported, Canvas).TextEntries);
        }
    }

    [Fact]
    public void PngKeywordLengthIsLimited()
    {
        Assert.True(MetadataWritePlan.FormatCapabilities.IsValidPngKeyword(new string('k', 79)));
        Assert.False(MetadataWritePlan.FormatCapabilities.IsValidPngKeyword(new string('k', 80)));
    }

    [Fact]
    public void PngTextEntriesMustBeEncodable()
    {
        Assert.True(MetadataWritePlan.FormatCapabilities.IsValidPngTextEntry(new ImageTextEntry("Title", "中文 😀", "zh-Hans-CN", "标题")));
        Assert.False(MetadataWritePlan.FormatCapabilities.IsValidPngTextEntry(new ImageTextEntry("XML:com.adobe.xmp", "<x/>")));
        Assert.False(MetadataWritePlan.FormatCapabilities.IsValidPngTextEntry(new ImageTextEntry("Title", "a\0b")));
        Assert.False(MetadataWritePlan.FormatCapabilities.IsValidPngTextEntry(new ImageTextEntry("Title", "\udc00")));
        Assert.False(MetadataWritePlan.FormatCapabilities.IsValidPngTextEntry(new ImageTextEntry("Title", "x", "en US")));
        Assert.False(MetadataWritePlan.FormatCapabilities.IsValidPngTextEntry(new ImageTextEntry("Title", "x", "en", "\ud800")));
    }

    [Fact]
    public void IccProfilesMustMatchTheWrittenPixelFormat()
    {
        var gray = new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(CreateIccProfile("GRAY"))) };
        Assert.NotNull(MetadataWritePlan.Create(gray, ImageFormat.Png, MetadataHandling.Strict, Canvas).IccProfile); // pixel format unknown
        Assert.NotNull(MetadataWritePlan.Create(gray, ImageFormat.Png, MetadataHandling.Strict, Canvas, PixelFormat.Gray16).IccProfile);
        Assert.Null(MetadataWritePlan.Create(gray, ImageFormat.Jpeg, MetadataHandling.DiscardUnsupported, Canvas, PixelFormat.Rgb24).IccProfile);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(gray, ImageFormat.Png, MetadataHandling.Strict, Canvas, PixelFormat.Rgba32));
        Assert.Equal("Metadata: ICC color profile declaring the Gray color space for Rgba32 pixels", exception.Feature);
    }

    [Fact]
    public void JpegSegmentSizeLimitsAreEnforced()
    {
        var xmp = new XmpProfile(new MetadataBlob(Encoding.UTF8.GetBytes(new string('x', MetadataWritePlan.MaxJpegStandardXmpBytes + 1))));
        AssertUnsupported(new ImageMetadata { XmpProfile = xmp }, ImageFormat.Jpeg, "XMP packet larger than one JPEG APP1 segment (extended XMP)");
        Assert.Same(xmp, MetadataWritePlan.Create(new ImageMetadata { XmpProfile = xmp }, ImageFormat.Png, MetadataHandling.Strict, Canvas).XmpProfile);

        var fits = new XmpProfile(new MetadataBlob(Encoding.UTF8.GetBytes(new string('x', MetadataWritePlan.MaxJpegStandardXmpBytes))));
        Assert.Same(fits, MetadataWritePlan.Create(new ImageMetadata { XmpProfile = fits }, ImageFormat.Jpeg, MetadataHandling.Strict, Canvas).XmpProfile);
    }

    [Fact]
    public void MalformedPayloadsThrowWhateverThePolicyExceptStrip()
    {
        foreach (var handling in new[] { MetadataHandling.Strict, MetadataHandling.DiscardUnsupported })
        {
            var icc = new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(new byte[200])) };
            Assert.Equal(ImageFormat.Png, Assert.Throws<InvalidImageContentException>(() => MetadataWritePlan.Create(icc, ImageFormat.Png, handling, Canvas)).Format);

            var exif = new ImageMetadata { ExifProfile = new ExifProfile(new MetadataBlob([.. "Exif\0\0"u8, .. ExifTiff.CreateMinimal(ExifOrientation.TopLeft)])) };
            Assert.Equal(ImageFormat.Jpeg, Assert.Throws<InvalidImageContentException>(() => MetadataWritePlan.Create(exif, ImageFormat.Jpeg, handling, Canvas)).Format);

            var xmp = new ImageMetadata { XmpProfile = new XmpProfile(new MetadataBlob([0xFF])) };
            Assert.Throws<InvalidImageContentException>(() => MetadataWritePlan.Create(xmp, ImageFormat.Png, handling, Canvas));
        }

        // A format that cannot store the payload never validates it: discarding wins
        var unsupported = new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(new byte[200])) };
        Assert.Null(MetadataWritePlan.Create(unsupported, ImageFormat.Gif, MetadataHandling.DiscardUnsupported, Canvas).IccProfile);
    }

    [Fact]
    public void ExifDimensionsAreReconciledWithTheCanvas()
    {
        var exif = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(0x0112, 1)).ExifIfd(TiffBuilder.Short(0xA002, 640), TiffBuilder.Short(0xA003, 480)).Build();
        var metadata = new ImageMetadata { ExifProfile = new ExifProfile(new MetadataBlob(exif)), Orientation = ExifOrientation.BottomLeft };
        var plan = MetadataWritePlan.Create(metadata, ImageFormat.Jpeg, MetadataHandling.Strict, Canvas);
        Assert.Equal(((uint)Canvas.Width, (uint)Canvas.Height), ExifTiff.ReadPixelDimensions(plan.Exif));
        Assert.Equal(ExifOrientation.BottomLeft, ExifTiff.ReadOrientation(plan.Exif));
        Assert.Equal((640u, 480u), ExifTiff.ReadPixelDimensions(metadata.ExifProfile.Data.Span)); // the source profile is unchanged
    }

    [Fact]
    public void UnrepresentableResolutionFollowsThePolicy()
    {
        var metadata = new ImageMetadata { Resolution = new ImageResolution(1e15, 72) };
        AssertUnsupported(metadata, ImageFormat.Png, "resolution (outside the range of the format's integer density fields)");
        AssertUnsupported(metadata, ImageFormat.Jpeg, "resolution (outside the range of the format's integer density fields)");
        Assert.Null(MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.DiscardUnsupported, Canvas).Resolution);
    }

    [Fact]
    public void SourceFormatNeverSelectsTheOutput()
    {
        var metadata = new ImageMetadata { SourceFormat = ImageFormat.Gif, Resolution = new ImageResolution(72, 72) };
        Assert.Same(metadata.Resolution, MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strict, Canvas).Resolution);
        metadata.SourceFormat = ImageFormat.Png;
        Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(metadata, ImageFormat.Gif, MetadataHandling.Strict, Canvas));
    }

    private static void AssertUnsupported(ImageMetadata metadata, ImageFormat format, string feature)
    {
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(metadata, format, MetadataHandling.Strict, Canvas));
        Assert.Equal(format, exception.Format);
        Assert.Equal("Metadata: " + feature, exception.Feature);
        Assert.Contains("DiscardUnsupported", exception.Message, StringComparison.Ordinal);
        _ = MetadataWritePlan.Create(metadata, format, MetadataHandling.DiscardUnsupported, Canvas); // never throws for unsupported items
    }

    private static ImageMetadata WithText(ImageTextEntry entry)
    {
        var metadata = new ImageMetadata();
        metadata.TextEntries.Add(entry);
        return metadata;
    }

    private static ImageMetadata CreateFullMetadata()
    {
        var metadata = new ImageMetadata
        {
            Orientation = ExifOrientation.RightTop,
            Resolution = new ImageResolution(300, 300),
            IccProfile = new IccProfile(new MetadataBlob(CreateIccProfile("RGB "))),
            ExifProfile = new ExifProfile(new MetadataBlob(ExifTiff.CreateMinimal(ExifOrientation.TopLeft))),
            XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8)),
        };
        metadata.TextEntries.Add(new ImageTextEntry("Title", "A title"));
        metadata.TextEntries.Add(new ImageTextEntry("Title", "Un titre", "fr", "Titre"));
        return metadata;
    }

    internal static byte[] CreateIccProfile(string colorSpace)
    {
        // Header + one 'desc'-like tag of 12 bytes
        var data = new byte[128 + 4 + 12 + 12];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
        "XYZ "u8.CopyTo(data.AsSpan(20));
        "acsp"u8.CopyTo(data.AsSpan(36));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(128), 1);
        "desc"u8.CopyTo(data.AsSpan(132));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(136), 144);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(140), 12);
        return data;
    }
}
