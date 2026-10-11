using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>EXIF orientation import, authoritative rewrite, dimension reconciliation and thumbnail removal.</summary>
public sealed class ExifTiffTests
{
    [Fact]
    public void MinimalBlockContainsOnlyTheOrientation()
    {
        foreach (var orientation in Enum.GetValues<ExifOrientation>())
        {
            var data = ExifTiff.CreateMinimal(orientation);
            Assert.HasCount(26, data);
            Assert.True(ExifTiff.IsValid(data));
            Assert.Equal(orientation, ExifTiff.ReadOrientation(data));
            Assert.False(ExifTiff.HasThumbnail(data));
            Assert.Equal((null, null), ExifTiff.ReadPixelDimensions(data));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrientationIsReadInBothByteOrders(bool bigEndian)
    {
        var data = new TiffBuilder(bigEndian).Ifd0(TiffBuilder.Short(0x0112, 6)).Build();
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(65535)]
    public void InvalidOrientationValuesAreIgnored(int value)
    {
        // Decoders keep TopLeft: an out-of-range tag is not guessed into a valid orientation
        var data = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(0x0112, (ushort)value)).Build();
        Assert.True(ExifTiff.IsValid(data));
        Assert.Null(ExifTiff.ReadOrientation(data));
    }

    [Fact]
    public void MissingOrientationIsUnknown()
    {
        var data = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Ascii(0x0131, "software")).Build();
        Assert.Null(ExifTiff.ReadOrientation(data));
    }

    [Fact]
    public void MalformedDataIsRejected()
    {
        var valid = ExifTiff.CreateMinimal(ExifOrientation.TopLeft);
        byte[] prefixed = [.. "Exif\0\0"u8, .. valid];
        Assert.False(ExifTiff.IsValid(prefixed)); // container prefixes are not part of EXIF data
        Assert.False(ExifTiff.IsValid([]));
        Assert.False(ExifTiff.IsValid("II*\0"u8));
        Assert.False(ExifTiff.IsValid(valid.AsSpan(0, 20))); // truncated IFD

        var badOffset = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(badOffset.AsSpan(4), 1000);
        Assert.False(ExifTiff.IsValid(badOffset));

        var outOfBoundsValue = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Ascii(0x0131, "a long software name")).Build();
        BinaryPrimitives.WriteUInt32LittleEndian(outOfBoundsValue.AsSpan(8 + 2 + 8), 5000);
        Assert.False(ExifTiff.IsValid(outOfBoundsValue));

        var cycle = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(cycle.AsSpan(22), 8); // IFD0 links to itself
        Assert.False(ExifTiff.IsValid(cycle));

        Assert.Null(ExifTiff.ReadOrientation(prefixed));
        var exception = Assert.Throws<InvalidImageContentException>(() => ExifTiff.Rewrite(prefixed, ExifOrientation.TopLeft, null, removeThumbnail: false, ImageFormat.Jpeg));
        Assert.Equal(ImageFormat.Jpeg, exception.Format);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RewriteReplacesTheOrientationInPlace(bool bigEndian)
    {
        var source = new TiffBuilder(bigEndian).Ifd0(TiffBuilder.Short(0x0112, 1), TiffBuilder.Ascii(0x0131, "camera firmware")).Build();
        var copy = source.ToArray();
        var result = ExifTiff.Rewrite(source, ExifOrientation.LeftBottom, null, removeThumbnail: false);
        Assert.Equal(copy, source); // the input is never modified
        Assert.HasCount(source.Length, result);
        Assert.Equal(ExifOrientation.LeftBottom, ExifTiff.ReadOrientation(result));
        Assert.Contains("camera firmware", Encoding.ASCII.GetString(result), StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteNormalizesANonShortOrientationEntry()
    {
        var source = new TiffBuilder(bigEndian: true).Ifd0(TiffBuilder.Long(0x0112, 3)).Build();
        var result = ExifTiff.Rewrite(source, ExifOrientation.TopRight, null, removeThumbnail: false);
        Assert.Equal(ExifOrientation.TopRight, ExifTiff.ReadOrientation(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RewriteAddsAMissingOrientationWithoutBreakingOffsets(bool bigEndian)
    {
        var source = new TiffBuilder(bigEndian)
            .Ifd0(TiffBuilder.Ascii(0x010F, "maker name"), TiffBuilder.Ascii(0x0131, "software name"))
            .ExifIfd(TiffBuilder.Short(0xA002, 3), TiffBuilder.Short(0xA003, 2))
            .Build();
        var result = ExifTiff.Rewrite(source, ExifOrientation.BottomRight, new Size(3, 2), removeThumbnail: false);
        Assert.True(ExifTiff.IsValid(result));
        Assert.Equal(ExifOrientation.BottomRight, ExifTiff.ReadOrientation(result));
        Assert.Equal((3u, 2u), ExifTiff.ReadPixelDimensions(result));

        // Out-of-line values and the Exif IFD pointer of the rebuilt IFD0 still point to the original data
        var text = Encoding.ASCII.GetString(result);
        Assert.Contains("maker name", text, StringComparison.Ordinal);
        Assert.Contains("software name", text, StringComparison.Ordinal);
        Assert.Equal(source.AsSpan(8).ToArray(), result.AsSpan(8, source.Length - 8).ToArray()); // only the header offset changes
        Assert.Equal(0, ReadIfd0Offset(result, bigEndian) % 2); // word-aligned
    }

    [Fact]
    public void RewriteWithoutOrientationKeepsTheStoredTag()
    {
        var source = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(0x0112, 5)).Build();
        Assert.Equal(source, ExifTiff.Rewrite(source, orientation: null, null, removeThumbnail: false));
    }

    [Fact]
    public void DimensionsAreReconciledAndWidenedWhenNeeded()
    {
        var source = new TiffBuilder(bigEndian: false)
            .Ifd0(TiffBuilder.Short(0x0100, 640), TiffBuilder.Short(0x0101, 480), TiffBuilder.Short(0x0112, 1))
            .ExifIfd(TiffBuilder.Short(0xA002, 640), TiffBuilder.Long(0xA003, 480))
            .Build();
        var result = ExifTiff.Rewrite(source, null, new Size(70_000, 3), removeThumbnail: false);
        Assert.Equal((70_000u, 3u), ExifTiff.ReadPixelDimensions(result)); // SHORT widened to LONG for 70000
        Assert.True(ExifTiff.ExifStructure.TryParse(result, out var structure, out _));
        var width = ExifTiff.ExifStructure.Find(structure.Ifd0, ExifTiff.ImageWidthTag)!.Value;
        var height = ExifTiff.ExifStructure.Find(structure.Ifd0, ExifTiff.ImageLengthTag)!.Value;
        Assert.Equal(ExifTiff.TypeLong, width.Type);
        Assert.Equal(ExifTiff.TypeShort, height.Type);
        Assert.True(structure.TryReadUnsigned(result, width, out var w));
        Assert.True(structure.TryReadUnsigned(result, height, out var h));
        Assert.Equal((70_000u, 3u), (w, h));
    }

    [Fact]
    public void DimensionTagsAreNotInvented()
    {
        var source = ExifTiff.CreateMinimal(ExifOrientation.TopLeft);
        Assert.Equal(source, ExifTiff.Rewrite(source, null, new Size(10, 10), removeThumbnail: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JpegThumbnailIsRemovedAndZeroed(bool bigEndian)
    {
        var thumbnail = "OLD-THUMBNAIL-CONTENT"u8.ToArray();
        var source = new TiffBuilder(bigEndian).Ifd0(TiffBuilder.Short(0x0112, 6)).Thumbnail(thumbnail).Build();
        Assert.True(ExifTiff.HasThumbnail(source));

        var result = ExifTiff.Rewrite(source, null, null, removeThumbnail: true);
        Assert.True(ExifTiff.IsValid(result));
        Assert.False(ExifTiff.HasThumbnail(result));
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(result));
        Assert.DoesNotContain("OLD-THUMBNAIL", Encoding.ASCII.GetString(result), StringComparison.Ordinal);
        Assert.HasCountLessThan(source.Length, result); // the trailing IFD1 and thumbnail are dropped
    }

    [Fact]
    public void StripThumbnailIsZeroedEvenWhenNotAtTheEnd()
    {
        var source = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(0x0112, 1)).StripThumbnail("STRIP-ONE"u8.ToArray(), "STRIP-TWO"u8.ToArray()).Trailer("KEEP-ME"u8.ToArray()).Build();
        var result = ExifTiff.Rewrite(source, ExifOrientation.TopLeft, null, removeThumbnail: true);
        var text = Encoding.ASCII.GetString(result);
        Assert.DoesNotContain("STRIP-", text, StringComparison.Ordinal);
        Assert.Contains("KEEP-ME", text, StringComparison.Ordinal);
        Assert.False(ExifTiff.HasThumbnail(result));
    }

    [Fact]
    public void ThumbnailRemovalAndMissingOrientationCombine()
    {
        var source = new TiffBuilder(bigEndian: true).Ifd0(TiffBuilder.Ascii(0x0131, "software")).Thumbnail("THUMB"u8.ToArray()).Build();
        var result = ExifTiff.Rewrite(source, ExifOrientation.LeftTop, null, removeThumbnail: true);
        Assert.True(ExifTiff.IsValid(result));
        Assert.Equal(ExifOrientation.LeftTop, ExifTiff.ReadOrientation(result));
        Assert.False(ExifTiff.HasThumbnail(result));
        Assert.DoesNotContain("THUMB", Encoding.ASCII.GetString(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ImageMetadataReconcilesGeometry()
    {
        var source = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(0x0112, 8)).ExifIfd(TiffBuilder.Short(0xA002, 4), TiffBuilder.Short(0xA003, 3)).Thumbnail("THUMB"u8.ToArray()).Build();
        var profile = new ExifProfile(new MetadataBlob(source));
        var metadata = new ImageMetadata { ExifProfile = profile, Orientation = ExifOrientation.LeftBottom };
        metadata.ReconcileGeometry(new Size(2, 1));
        Assert.NotSame(profile, metadata.ExifProfile);
        Assert.Equal(source, profile.Data.ToArray()); // the previous profile is immutable
        Assert.Equal((2u, 1u), ExifTiff.ReadPixelDimensions(metadata.ExifProfile!.Data.Span));
        Assert.False(ExifTiff.HasThumbnail(metadata.ExifProfile.Data.Span));
        Assert.Equal(ExifOrientation.LeftBottom, metadata.Orientation);

        // Without EXIF or with malformed EXIF nothing changes
        var empty = new ImageMetadata();
        empty.ReconcileGeometry(new Size(1, 1));
        Assert.Null(empty.ExifProfile);
        var malformed = new ExifProfile(new MetadataBlob([1, 2, 3]));
        var withMalformed = new ImageMetadata { ExifProfile = malformed };
        withMalformed.ReconcileGeometry(new Size(1, 1));
        Assert.Same(malformed, withMalformed.ExifProfile);
    }

    private static long ReadIfd0Offset(byte[] data, bool bigEndian) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
}
