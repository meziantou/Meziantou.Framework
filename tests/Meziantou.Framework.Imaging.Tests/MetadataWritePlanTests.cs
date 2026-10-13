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

    // Only AVIF stores a content light level, a mastering display color volume and the shape of the pixels
    private static readonly ImageFormat[] FormatsWithoutHighDynamicRangeFields = [.. Enum.GetValues<ImageFormat>().Where(format => format is not (ImageFormat.Unknown or ImageFormat.Avif))];

    [Fact]
    public void ValidIccProfilePassesValidation()
    {
        Assert.True(MetadataValidation.TryValidateIccProfile(IccTestBlobs.Create("RGB "), out var error), error);
        Assert.Equal(IccProfileColorSpace.Rgb, new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB "))).ColorSpace);
        Assert.Equal(IccProfileColorSpace.Gray, new IccProfile(new MetadataBlob(IccTestBlobs.Create("GRAY"))).ColorSpace);
        Assert.Equal(IccProfileColorSpace.Cmyk, new IccProfile(new MetadataBlob(IccTestBlobs.Create("CMYK"))).ColorSpace);
        Assert.Equal(IccProfileColorSpace.Other, new IccProfile(new MetadataBlob(IccTestBlobs.Create("Lab "))).ColorSpace);
    }

    [Fact]
    public void MalformedIccProfilesAreDetected()
    {
        Assert.False(MetadataValidation.TryValidateIccProfile(new byte[100], out _));

        var wrongSize = IccTestBlobs.Create("RGB ");
        BinaryPrimitives.WriteUInt32BigEndian(wrongSize, (uint)wrongSize.Length + 1);
        Assert.False(MetadataValidation.TryValidateIccProfile(wrongSize, out var error));
        Assert.Contains("declares", error, StringComparison.Ordinal);

        var noSignature = IccTestBlobs.Create("RGB ");
        noSignature[36] = (byte)'x';
        Assert.False(MetadataValidation.TryValidateIccProfile(noSignature, out error));
        Assert.Contains("acsp", error, StringComparison.Ordinal);

        var tagOutside = IccTestBlobs.Create("RGB ");
        BinaryPrimitives.WriteUInt32BigEndian(tagOutside.AsSpan(128 + 4 + 8), 1000); // tag size
        Assert.False(MetadataValidation.TryValidateIccProfile(tagOutside, out error));
        Assert.Contains("outside the profile", error, StringComparison.Ordinal);

        var tooManyTags = IccTestBlobs.Create("RGB ");
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
        AssertUnsupported(new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB "))) }, ImageFormat.Gif, "ICC color profile");
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
        var gray = new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("GRAY"))) };
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

    [Theory]
    [InlineData(9, 16)] // BT.2020 primaries, PQ
    [InlineData(9, 18)] // BT.2020 primaries, HLG
    [InlineData(12, 13)] // Display P3 primaries with the sRGB transfer function
    [InlineData(1, 1)] // BT.709 primaries with the BT.709 transfer function
    [InlineData(1, 8)] // BT.709 primaries, linear
    [InlineData(2, 16)] // unspecified primaries, PQ
    [InlineData(9, 2)] // BT.2020 primaries, unspecified transfer function
    [InlineData(0, 13)] // reserved primaries
    public void CicpDescriptionThatIsNotSrgbEquivalentIsUnsupportedMetadata(int colorPrimaries, int transferCharacteristics)
    {
        // No format stores the description: without it the samples would be displayed as sRGB
        var metadata = new ImageMetadata { Cicp = new CicpColorDescription((byte)colorPrimaries, (byte)transferCharacteristics, matrixCoefficients: 1, fullRange: false) };
        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(metadata, format, MetadataHandling.Strict, Canvas));
            Assert.Equal(format, exception.Format);
            Assert.StartsWith("Metadata: CICP", exception.Feature);
            Assert.Contains("DiscardUnsupported", exception.Message, StringComparison.Ordinal);

            // Dropping the description is an explicit choice
            _ = MetadataWritePlan.Create(metadata, format, MetadataHandling.DiscardUnsupported, Canvas);
            _ = MetadataWritePlan.Create(metadata, format, MetadataHandling.Strip, Canvas);
        }
    }

    [Theory]
    [InlineData(1, 13)] // BT.709 primaries, sRGB transfer function: sRGB
    [InlineData(1, 2)] // unspecified transfer function
    [InlineData(2, 13)] // unspecified primaries
    [InlineData(2, 2)] // nothing specified
    public void SrgbEquivalentCicpDescriptionNeverThrows(int colorPrimaries, int transferCharacteristics)
    {
        // The matrix coefficients and the range describe the encoded source and are already applied: they never matter
        foreach (var (matrixCoefficients, fullRange) in new[] { (0, true), (1, false), (6, false), (9, true), (2, false) })
        {
            var metadata = new ImageMetadata { Cicp = new CicpColorDescription((byte)colorPrimaries, (byte)transferCharacteristics, (byte)matrixCoefficients, fullRange) };
            foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif })
            {
                foreach (var handling in new[] { MetadataHandling.Strict, MetadataHandling.DiscardUnsupported, MetadataHandling.Strip })
                {
                    _ = MetadataWritePlan.Create(metadata, format, handling, Canvas);
                }
            }
        }
    }

    [Fact]
    public void LightLevelsMasteringDisplayAndPixelShapeAreUnsupportedMetadataWhereNoFieldStoresThem()
    {
        var lightLevel = new ImageMetadata { ContentLightLevel = new ContentLightLevel(1000, 400) };
        var display = new ImageMetadata { MasteringDisplay = new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 10_000_000, 50) };
        var wide = new ImageMetadata { PixelAspectRatio = new PixelAspectRatio(2, 1) };
        foreach (var format in FormatsWithoutHighDynamicRangeFields)
        {
            AssertUnsupported(lightLevel, format, "content light level");
            AssertUnsupported(display, format, "mastering display color volume");
            AssertUnsupported(wide, format, "pixel aspect ratio");

            var plan = MetadataWritePlan.Create(new ImageMetadata { ContentLightLevel = lightLevel.ContentLightLevel, MasteringDisplay = display.MasteringDisplay, PixelAspectRatio = wide.PixelAspectRatio }, format, MetadataHandling.DiscardUnsupported, Canvas);
            Assert.Null(plan.ContentLightLevel);
            Assert.Null(plan.MasteringDisplay);
            Assert.Null(plan.PixelAspectRatio);

            // Square pixels are what a format without a field for their shape stores: nothing is lost
            var square = MetadataWritePlan.Create(new ImageMetadata { PixelAspectRatio = new PixelAspectRatio(3, 3) }, format, MetadataHandling.Strict, Canvas);
            Assert.Null(square.PixelAspectRatio);
        }
    }

    [Fact]
    public void AvifStoresLightLevelsMasteringDisplayAndPixelShape()
    {
        var metadata = new ImageMetadata
        {
            ContentLightLevel = new ContentLightLevel(1000, 400),
            MasteringDisplay = new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 10_000_000, 50),
            PixelAspectRatio = new PixelAspectRatio(2, 1),
        };
        var plan = MetadataWritePlan.Create(metadata, ImageFormat.Avif, MetadataHandling.Strict, Canvas);
        Assert.Same(metadata.ContentLightLevel, plan.ContentLightLevel);
        Assert.Same(metadata.MasteringDisplay, plan.MasteringDisplay);
        Assert.Same(metadata.PixelAspectRatio, plan.PixelAspectRatio);

        // A square ratio is stored as it is given: the box exists for it too
        var square = new PixelAspectRatio(3, 3);
        Assert.Same(square, MetadataWritePlan.Create(new ImageMetadata { PixelAspectRatio = square }, ImageFormat.Avif, MetadataHandling.Strict, Canvas).PixelAspectRatio);

        plan = MetadataWritePlan.Create(metadata, ImageFormat.Avif, MetadataHandling.Strip, Canvas);
        Assert.Null(plan.ContentLightLevel);
        Assert.Null(plan.MasteringDisplay);
        Assert.Null(plan.PixelAspectRatio);
    }

    [Fact]
    public void JpegXlStoresTheOrientationTheLinearTransferFunctionAndEnumeratedCicpDescriptionsItself()
    {
        // The orientation is a field of the image header: no EXIF profile is created for it
        var rotated = new ImageMetadata { Orientation = ExifOrientation.LeftBottom };
        var plan = MetadataWritePlan.Create(rotated, ImageFormat.JpegXl, MetadataHandling.Strict, Canvas);
        Assert.Equal(ExifOrientation.LeftBottom, plan.Orientation);
        Assert.Null(plan.Exif);
        Assert.Equal(ExifOrientation.TopLeft, MetadataWritePlan.Create(rotated, ImageFormat.Jpeg, MetadataHandling.Strict, Canvas).Orientation);

        // An existing profile is kept, with the typed orientation
        rotated.ExifProfile = new ExifProfile(new MetadataBlob(ExifTiff.CreateMinimal(ExifOrientation.TopLeft)));
        plan = MetadataWritePlan.Create(rotated, ImageFormat.JpegXl, MetadataHandling.Strict, Canvas);
        Assert.Equal(ExifOrientation.LeftBottom, ExifTiff.ReadOrientation(plan.Exif));
        Assert.Equal(ExifOrientation.LeftBottom, plan.Orientation);

        var linear = new ImageMetadata { TransferFunction = ColorTransferFunction.Linear };
        Assert.Equal(ColorTransferFunction.Linear, MetadataWritePlan.Create(linear, ImageFormat.JpegXl, MetadataHandling.Strict, Canvas).TransferFunction);

        // A description the format enumerates is stored; the matrix and the range describe an encoded source
        var cicp = new CicpColorDescription(colorPrimaries: 9, transferCharacteristics: 16, matrixCoefficients: 9, fullRange: false);
        Assert.Same(cicp, MetadataWritePlan.Create(new ImageMetadata { Cicp = cicp }, ImageFormat.JpegXl, MetadataHandling.Strict, Canvas).Cicp);
        Assert.Null(MetadataWritePlan.Create(new ImageMetadata { Cicp = cicp }, ImageFormat.JpegXl, MetadataHandling.Strip, Canvas).Cicp);

        // A description that is displayed like sRGB is the default encoding; an ICC profile describes the colors in its place
        Assert.Null(MetadataWritePlan.Create(new ImageMetadata { Cicp = new CicpColorDescription(1, 13, 0, fullRange: true) }, ImageFormat.JpegXl, MetadataHandling.Strict, Canvas).Cicp);
        var profiled = new ImageMetadata { Cicp = cicp, IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB "))) };
        plan = MetadataWritePlan.Create(profiled, ImageFormat.JpegXl, MetadataHandling.Strict, Canvas);
        Assert.Null(plan.Cicp);
        Assert.Same(profiled.IccProfile, plan.IccProfile);

        // BT.601 primaries, and a transfer function without a JPEG XL value, cannot be enumerated
        foreach (var other in new[] { new CicpColorDescription(5, 13, 0, fullRange: true), new CicpColorDescription(9, 14, 0, fullRange: true), new CicpColorDescription(2, 16, 0, fullRange: true) })
        {
            AssertUnsupported(new ImageMetadata { Cicp = other }, ImageFormat.JpegXl, "CICP color description (the samples would be read as sRGB)");
            Assert.Null(MetadataWritePlan.Create(new ImageMetadata { Cicp = other }, ImageFormat.JpegXl, MetadataHandling.DiscardUnsupported, Canvas).Cicp);
        }

        // The format has no field for the resolution nor for text
        AssertUnsupported(new ImageMetadata { Resolution = new ImageResolution(72, 72) }, ImageFormat.JpegXl, "resolution");
        AssertUnsupported(WithText(new ImageTextEntry("Title", "t")), ImageFormat.JpegXl, "text entry 'Title'");
    }

    [Fact]
    public void Jp2StoresProfilesAndTheResolutionAndACodestreamStoresNothing()
    {
        var metadata = new ImageMetadata
        {
            ExifProfile = new ExifProfile(new MetadataBlob(ExifTiff.CreateMinimal(ExifOrientation.TopLeft))),
            XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"u8.ToArray())),
            Resolution = new ImageResolution(300, 150),
        };
        var plan = MetadataWritePlan.Create(metadata, ImageFormat.Jpeg2000, MetadataHandling.Strict, Canvas, encoder: new Jpeg2000Encoder());
        Assert.NotNull(plan.Exif);
        Assert.Same(metadata.XmpProfile, plan.XmpProfile);
        Assert.Equal(metadata.Resolution, plan.Resolution);

        // The orientation has no field of its own: it is the one of the EXIF profile, created when there is none
        var rotated = MetadataWritePlan.Create(new ImageMetadata { Orientation = ExifOrientation.RightTop }, ImageFormat.Jpeg2000, MetadataHandling.Strict, Canvas, encoder: new Jpeg2000Encoder());
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(rotated.Exif));

        // A JP2 file has no box for text, and its color specification has no CICP description
        AssertUnsupported(WithText(new ImageTextEntry("Title", "t")), ImageFormat.Jpeg2000, "text entry 'Title'");

        // A codestream has no place for any of them
        var codestream = new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream };
        foreach (var item in new[]
        {
            new ImageMetadata { ExifProfile = metadata.ExifProfile },
            new ImageMetadata { XmpProfile = metadata.XmpProfile },
            new ImageMetadata { Resolution = metadata.Resolution },
            new ImageMetadata { Orientation = ExifOrientation.RightTop },
            new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB "))) },
        })
        {
            Assert.StartsWith("Metadata: ", Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(item, ImageFormat.Jpeg2000, MetadataHandling.Strict, Canvas, encoder: codestream)).Feature, StringComparison.Ordinal);
            var discarded = MetadataWritePlan.Create(item, ImageFormat.Jpeg2000, MetadataHandling.DiscardUnsupported, Canvas, encoder: codestream);
            Assert.Null(discarded.Exif);
            Assert.Null(discarded.XmpProfile);
            Assert.Null(discarded.Resolution);
            Assert.Null(discarded.IccProfile);
        }

        _ = MetadataWritePlan.Create(new ImageMetadata(), ImageFormat.Jpeg2000, MetadataHandling.Strict, Canvas, encoder: codestream);
    }

    [Fact]
    public void SavingACicpDescriptionThatIsNotSrgbEquivalentNeedsAnExplicitSetting()
    {
        using var image = new Image<Rgb24>(2, 2);
        image.Metadata.Cicp = new CicpColorDescription(colorPrimaries: 9, transferCharacteristics: 16, matrixCoefficients: 9, fullRange: false);

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new MemoryStream(), new PngEncoder()));
        Assert.Equal(ImageFormat.Png, exception.Format);

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        using var reloaded = Image.Load(stream.ToArray());
        Assert.Null(reloaded.Metadata.Cicp);

        // An sRGB label is saved without any setting: nothing is lost
        image.Metadata.Cicp = new CicpColorDescription(colorPrimaries: 1, transferCharacteristics: 13, matrixCoefficients: 6, fullRange: true);
        image.Save(new MemoryStream(), new PngEncoder());
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
            IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB "))),
            ExifProfile = new ExifProfile(new MetadataBlob(ExifTiff.CreateMinimal(ExifOrientation.TopLeft))),
            XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8)),
        };
        metadata.TextEntries.Add(new ImageTextEntry("Title", "A title"));
        metadata.TextEntries.Add(new ImageTextEntry("Title", "Un titre", "fr", "Titre"));
        return metadata;
    }
}
