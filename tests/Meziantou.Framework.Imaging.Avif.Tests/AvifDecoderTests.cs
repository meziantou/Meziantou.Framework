using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The AVIF container rules that a header identification applies, on files assembled box by box by
/// <see cref="HeifFileBuilder"/> (no fixture file, no tool): the item model and the track model are read before any coded
/// data, so the payloads are placeholders. Expected values are written from the specifications; the independent reader
/// (<see cref="ReferenceHeif"/>) reads the same files. The oracles of the corpus (<see cref="ReferenceYuvConverter"/>,
/// the orientation and clean aperture arithmetic of <see cref="ReferenceHeif"/>) are checked here against hand-computed
/// values. Decoding is covered by <c>Conformance/AvifDecoderConformanceTests</c> and <c>Conformance/AvifContainerConformanceTests</c>.
/// </summary>
public sealed class AvifDecoderTests
{
    // A temporal delimiter and bytes that are never parsed by a header identification
    private static readonly byte[] Payload = [0x12, 0x00, 0x0A, 0x01, 0x00];

    private static HeifFileBuilder Still(uint width = 8, uint height = 6, int bitDepth = 8, bool monochrome = false, int subsampling = 0)
    {
        var builder = new HeifFileBuilder { PrimaryItemId = 1 };
        builder.AddItem(1, "av01", Payload,
            builder.AddProperty(HeifFileBuilder.ImageSize(width, height)),
            -builder.AddProperty(HeifFileBuilder.Av1Configuration(subsampling == 0 && !monochrome ? 1 : 0, 0, bitDepth, monochrome, monochrome ? 1 : subsampling, monochrome ? 1 : subsampling)));
        return builder;
    }

    private static HeifFileBuilder Sequence(int frames = 3, Action<HeifFileBuilder.Track>? configure = null)
    {
        var builder = new HeifFileBuilder { MajorBrand = "avis", MovieTimescale = 1000 };
        builder.CompatibleBrands.Clear();
        builder.CompatibleBrands.Add("avis");
        builder.CompatibleBrands.Add("msf1");
        builder.Layout.Clear();
        builder.Layout.Add(HeifFileBuilder.FileTypeBox);
        builder.Layout.Add(HeifFileBuilder.MovieBox);
        builder.Layout.Add(HeifFileBuilder.MediaDataBox);
        var track = new HeifFileBuilder.Track(1, "pict", 8, 6) { Timescale = 1000, Duration = (ulong)frames * 100 };
        track.SampleEntryBoxes.Add(HeifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
        for (var i = 0; i < frames; i++)
        {
            track.Samples.Add((Payload, 100));
        }

        configure?.Invoke(track);
        builder.Tracks.Add(track);
        return builder;
    }

    private static HeifFileBuilder.EditList Edits(bool repeating, ulong segmentDuration, int version = 1, long mediaTime = 0, short rate = 1, short rateFraction = 0)
    {
        var edits = new HeifFileBuilder.EditList { IsRepeating = repeating, Version = version };
        edits.Entries.Add((segmentDuration, mediaTime, rate, rateFraction));
        return edits;
    }

    [Theory]
    [InlineData(8, false, false, PixelFormat.Rgb24, ImageColorModel.Unknown)] // no 'colr' box: the bitstream, which a header identification does not read, gives the matrix
    [InlineData(8, false, true, PixelFormat.Rgba32, ImageColorModel.Unknown)]
    [InlineData(8, true, false, PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData(8, true, true, PixelFormat.La16, ImageColorModel.GrayscaleAlpha)]
    [InlineData(10, false, false, PixelFormat.Rgb48, ImageColorModel.Unknown)]
    [InlineData(12, false, true, PixelFormat.Rgba64, ImageColorModel.Unknown)]
    [InlineData(10, true, false, PixelFormat.Gray16, ImageColorModel.Grayscale)]
    [InlineData(12, true, true, PixelFormat.La32, ImageColorModel.GrayscaleAlpha)]
    public void HeaderGivesTheSizeAndThePixelFormatOfTheItemProperties(int bitDepth, bool monochrome, bool alpha, PixelFormat pixelFormat, ImageColorModel colorModel)
    {
        var builder = Still(13, 7, bitDepth, monochrome);
        if (alpha)
        {
            builder.AddItem(2, "av01", Payload,
                builder.AddProperty(HeifFileBuilder.ImageSize(13, 7)),
                -builder.AddProperty(HeifFileBuilder.Av1Configuration(0, 0, bitDepth, monochrome: true, 1, 1)),
                builder.AddProperty(HeifFileBuilder.AuxiliaryType(ReferenceHeif.AlphaAuxiliaryType)));
            builder.References.Add(("auxl", 2, [1]));
        }

        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Avif, info.Format);
        Assert.Equal((13, 7), (info.Width, info.Height));
        Assert.Equal(pixelFormat, info.PixelFormat);
        Assert.Equal(colorModel, info.ColorModel);
        Assert.Equal(bitDepth, info.BitsPerComponent);
        Assert.Equal(alpha, info.MayHaveTransparency);
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.Null(info.Animation);
        Assert.Null(info.Metadata.Cicp);

        var file = ReferenceHeif.Parse(data);
        Assert.Equal((13L, 7L), file.PrimaryItem!.Size);
        Assert.Equal(alpha, file.AlphaItem is not null);
        Assert.Equal(bitDepth, file.PrimaryItem.Configuration!.BitDepth);
    }

    [Fact]
    public void PremultipliedAlphaIsAReferenceFromThePrimaryItemToItsAlphaPlane()
    {
        static HeifFileBuilder WithAlpha()
        {
            var builder = Still(13, 7);
            builder.AddItem(2, "av01", Payload,
                builder.AddProperty(HeifFileBuilder.ImageSize(13, 7)),
                -builder.AddProperty(HeifFileBuilder.Av1Configuration(0, 0, 8, monochrome: true, 1, 1)),
                builder.AddProperty(HeifFileBuilder.AuxiliaryType(ReferenceHeif.AlphaAuxiliaryType)));
            builder.References.Add(("auxl", 2, [1]));
            return builder;
        }

        // ISO/IEC 23008-12, section 6.6.2.2: the color image refers to the alpha plane that its colors are multiplied by
        var premultiplied = WithAlpha();
        premultiplied.References.Add(("prem", 1, [2]));
        var data = premultiplied.Build();
        Assert.Equal(PixelFormat.Rgba32, Image.Identify(data).PixelFormat);
        Assert.True(ReferenceHeif.Parse(data).IsPremultiplied);
        Assert.False(ReferenceHeif.Parse(WithAlpha().Build()).IsPremultiplied);

        // A reference to an item that is not the alpha plane of the image, or without any alpha plane, is invalid
        var other = WithAlpha();
        other.References.Add(("prem", 1, [3]));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(other.Build()));
        var opaque = Still(13, 7);
        opaque.References.Add(("prem", 1, [2]));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(opaque.Build()));

        // The reference of another item to the primary item says nothing of the primary item
        var reversed = WithAlpha();
        reversed.References.Add(("prem", 2, [1]));
        Assert.Equal(PixelFormat.Rgba32, Image.Identify(reversed.Build()).PixelFormat);
        Assert.False(ReferenceHeif.Parse(reversed.Build()).IsPremultiplied);
    }

    [Theory]
    // color * maximum / alpha, rounded to the nearest integer and clipped, computed by hand:
    // 100 * 255 / 128 = 199.2; 1 * 255 / 2 = 127.5; 127 * 255 / 254 = 127.5; 3 * 255 / 7 = 109.3
    [InlineData(100, 128, 255, 199)]
    [InlineData(1, 2, 255, 128)] // a half rounds up
    [InlineData(127, 254, 255, 128)]
    [InlineData(3, 7, 255, 109)]
    [InlineData(1, 1, 255, 255)]
    [InlineData(0, 1, 255, 0)]
    [InlineData(200, 100, 255, 255)] // a color above its alpha is clipped
    [InlineData(77, 255, 255, 77)] // an opaque pixel is unchanged
    [InlineData(77, 0, 255, 0)] // nothing is left of the color of a transparent pixel
    [InlineData(0, 0, 255, 0)]
    [InlineData(1000, 32768, 65535, 2000)] // 1000 * 65535 / 32768 = 1999.97
    [InlineData(1, 65534, 65535, 1)] // 1.00002
    [InlineData(32767, 65534, 65535, 32768)] // 32767.5: a half rounds up
    [InlineData(40000, 65535, 65535, 40000)]
    public void TheReferenceDivisionOfPremultipliedColorsIsRoundedToNearest(int color, int alpha, int maximum, int expected)
    {
        Assert.Equal(expected, ReferenceYuvConverter.Unpremultiply(color, alpha, maximum));
    }

    [Fact]
    public void AuxiliaryImageThatIsNotAlphaIsNotRead()
    {
        // A depth map is an auxiliary image too; only the alpha type gives the image an alpha channel
        var builder = Still();
        builder.AddItem(2, "av01", Payload,
            builder.AddProperty(HeifFileBuilder.ImageSize(4, 3)),
            -builder.AddProperty(HeifFileBuilder.Av1Configuration(0, 0, 8, monochrome: true, 1, 1)),
            builder.AddProperty(HeifFileBuilder.AuxiliaryType("urn:mpeg:mpegB:cicp:systems:auxiliary:depth")));
        builder.References.Add(("auxl", 2, [1]));
        var info = Image.Identify(builder.Build());
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.False(info.MayHaveTransparency);
    }

    // irot turns anti-clockwise by quarter turns, imir exchanges the top and the bottom (0) or the left and the right (1);
    // the EXIF value is the orientation that displays the stored image the same way (TIFF 6.0, tag 274)
    [Theory]
    [InlineData(0, null, ExifOrientation.TopLeft)]
    [InlineData(1, null, ExifOrientation.LeftBottom)] // a quarter turn anti-clockwise: 8
    [InlineData(2, null, ExifOrientation.BottomRight)] // a half turn: 3
    [InlineData(3, null, ExifOrientation.RightTop)] // a quarter turn clockwise: 6
    [InlineData(null, 0, ExifOrientation.BottomLeft)] // top and bottom exchanged: 4
    [InlineData(null, 1, ExifOrientation.TopRight)] // left and right exchanged: 2
    [InlineData(1, 1, ExifOrientation.RightBottom)] // 7
    [InlineData(1, 0, ExifOrientation.LeftTop)] // 5
    [InlineData(3, 1, ExifOrientation.LeftTop)] // 5
    [InlineData(3, 0, ExifOrientation.RightBottom)] // 7
    [InlineData(2, 0, ExifOrientation.TopRight)] // a half turn then top and bottom exchanged: left and right exchanged
    [InlineData(2, 1, ExifOrientation.BottomLeft)]
    public void RotationThenMirrorIsReportedAsTheOrientation(int? rotation, int? mirror, ExifOrientation expected)
    {
        var builder = Still();
        var item = builder.GetItem(1);
        if (rotation is { } turns)
            item.Associations.Add((builder.AddProperty(HeifFileBuilder.Rotation(turns)), true));

        if (mirror is { } axis)
            item.Associations.Add((builder.AddProperty(HeifFileBuilder.Mirror(axis)), true));

        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal(expected, info.Metadata.Orientation);
        Assert.Equal((8, 6), (info.Width, info.Height)); // reported, never applied
        Assert.Equal((int)expected, ReferenceHeif.Parse(data).PrimaryItem!.Orientation);
    }

    [Theory]
    [InlineData(1, 1, ExifOrientation.LeftTop)] // left and right exchanged, then a quarter turn anti-clockwise: 5 (the other order is 7)
    [InlineData(1, 0, ExifOrientation.RightBottom)]
    [InlineData(3, 1, ExifOrientation.RightBottom)]
    [InlineData(2, 1, ExifOrientation.BottomLeft)] // a half turn commutes with the mirrors
    public void TransformationsAreComposedInTheOrderOfTheirAssociation(int rotation, int mirror, ExifOrientation expected)
    {
        // ISO/IEC 23008-12, section 6.5.1: transformative properties apply in the order of their association
        var builder = Still();
        var item = builder.GetItem(1);
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Mirror(mirror)), true));
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Rotation(rotation)), true));
        var data = builder.Build();
        Assert.Equal(expected, Image.Identify(data).Metadata.Orientation);
        Assert.Equal((int)expected, ReferenceHeif.Parse(data).PrimaryItem!.Orientation);
    }

    [Fact]
    public void CleanApertureCropsTheImageAndPrecedesTheOrientation()
    {
        // An 8x6 image; aperture 5x4 whose center is 1/2 sample right of the center: left = (8 - 5) / 2 + 1/2 = 2, top = 1
        var builder = Still();
        var item = builder.GetItem(1);
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1)), true));
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Rotation(1)), true));
        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal((5, 4), (info.Width, info.Height));
        Assert.Equal(ExifOrientation.LeftBottom, info.Metadata.Orientation);
        Assert.Equal((2, 1, 5, 4), ReferenceHeif.Parse(data).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
    }

    [Theory]
    // An 8x6 image. A quarter turn anti-clockwise presents it as 6x8; an aperture of 2x5 of that presentation is 5 columns
    // and 2 rows of the coded image
    [InlineData(1, null, 2u, 5u, 5, 2, ExifOrientation.LeftBottom)]
    [InlineData(3, null, 2u, 5u, 5, 2, ExifOrientation.RightTop)]
    [InlineData(2, null, 2u, 5u, 2, 5, ExifOrientation.BottomRight)] // a half turn keeps the axes
    [InlineData(null, 0, 2u, 5u, 2, 5, ExifOrientation.BottomLeft)] // and so does a mirroring
    [InlineData(null, 1, 2u, 5u, 2, 5, ExifOrientation.TopRight)]
    [InlineData(1, 1, 2u, 5u, 5, 2, ExifOrientation.RightBottom)]
    public void CleanApertureAfterATransformationIsAPartOfThePresentedImage(int? rotation, int? mirror, uint apertureWidth, uint apertureHeight, int expectedWidth, int expectedHeight, ExifOrientation expectedOrientation)
    {
        // ISO/IEC 23008-12, section 6.5.1: the properties apply in the order of their association. MIAF requires the
        // aperture first; a file that does otherwise is read as it is written
        var builder = Still();
        var item = builder.GetItem(1);
        if (rotation is { } turns)
            item.Associations.Add((builder.AddProperty(HeifFileBuilder.Rotation(turns)), true));

        if (mirror is { } axis)
            item.Associations.Add((builder.AddProperty(HeifFileBuilder.Mirror(axis)), true));

        // Centered in the presented image (an offset of a half sample where the difference of the sizes is odd)
        var presentedWidth = rotation is 1 or 3 ? 6 : 8;
        var presentedHeight = rotation is 1 or 3 ? 8 : 6;
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.CleanAperture(apertureWidth, 1, apertureHeight, 1, (int)((presentedWidth - apertureWidth) % 2), 2, (int)((presentedHeight - apertureHeight) % 2), 2)), true));
        var data = builder.Build();
        var info = Image.Identify(data);

        // The pixels stay as they are coded: the size is the one of the coded samples that the aperture shows
        Assert.Equal((expectedWidth, expectedHeight), (info.Width, info.Height));
        Assert.Equal(expectedOrientation, info.Metadata.Orientation);
        var presented = ReferenceHeif.Parse(data).PrimaryItem!;
        Assert.False(presented.HasMiafTransformOrder);
        Assert.Equal((expectedWidth, expectedHeight), (presented.GetPresentedRectangle(8, 6)!.Value.Width, presented.GetPresentedRectangle(8, 6)!.Value.Height));
    }

    [Fact]
    public void ThePresentedRectangleOfTheReferenceReaderFollowsTheOrderOfTheAssociations()
    {
        // A quarter turn anti-clockwise shows the 8x6 image as 6 columns and 8 rows: the column c of the result is the row c
        // of the image, and the row r of the result its column 7 - r. The centered 4x4 aperture is the columns 1 to 4 and
        // the rows 2 to 5 of the result: the rows 1 to 4 and the columns 5 down to 2 of the image
        var rotated = Still();
        rotated.GetItem(1).Associations.Add((rotated.AddProperty(HeifFileBuilder.Rotation(1)), true));
        rotated.GetItem(1).Associations.Add((rotated.AddProperty(HeifFileBuilder.CleanAperture(4, 1, 4, 1, 0, 1, 0, 1)), true));
        Assert.Equal((2, 1, 4, 4), ReferenceHeif.Parse(rotated.Build()).PrimaryItem!.GetPresentedRectangle(8, 6));

        // The column c of the mirrored image is the column 7 - c of the image: the aperture 5x4 at (2, 1) of the mirrored
        // image is the columns 5 down to 1 and the rows 1 to 4 of the image, whatever follows
        var mirrored = Still();
        mirrored.GetItem(1).Associations.Add((mirrored.AddProperty(HeifFileBuilder.Mirror(1)), true));
        mirrored.GetItem(1).Associations.Add((mirrored.AddProperty(HeifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1)), true));
        mirrored.GetItem(1).Associations.Add((mirrored.AddProperty(HeifFileBuilder.Rotation(3)), true));
        Assert.Equal((1, 1, 5, 4), ReferenceHeif.Parse(mirrored.Build()).PrimaryItem!.GetPresentedRectangle(8, 6));

        // In the order of MIAF, the aperture is the one of the coded image; two apertures compose
        var plain = Still();
        plain.GetItem(1).Associations.Add((plain.AddProperty(HeifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1)), true));
        plain.GetItem(1).Associations.Add((plain.AddProperty(HeifFileBuilder.Rotation(1)), true));
        var item = ReferenceHeif.Parse(plain.Build()).PrimaryItem!;
        Assert.True(item.HasMiafTransformOrder);
        Assert.Equal((2, 1, 5, 4), item.GetPresentedRectangle(8, 6));
        Assert.Equal(item.CleanAperture!.GetRectangle(8, 6), item.GetPresentedRectangle(8, 6));

        // An aperture that does not select whole samples has no rectangle
        var fractional = Still();
        fractional.GetItem(1).Associations.Add((fractional.AddProperty(HeifFileBuilder.Rotation(1)), true));
        fractional.GetItem(1).Associations.Add((fractional.AddProperty(HeifFileBuilder.CleanAperture(5, 2, 4, 1, 0, 1, 0, 1)), true));
        Assert.Null(ReferenceHeif.Parse(fractional.Build()).PrimaryItem!.GetPresentedRectangle(8, 6));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(fractional.Build()));

        // An aperture that is inside the coded image but not inside the rotated one is invalid
        var outside = Still();
        outside.GetItem(1).Associations.Add((outside.AddProperty(HeifFileBuilder.Rotation(1)), true));
        outside.GetItem(1).Associations.Add((outside.AddProperty(HeifFileBuilder.CleanAperture(8, 1, 4, 1, 0, 1, 0, 1)), true));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(outside.Build()));
    }

    [Theory]
    [InlineData(5u, 2u, 4u, 1u, 0, 1u, 0, 1u)] // 5/2 samples wide
    [InlineData(5u, 1u, 4u, 1u, 0, 1u, 0, 1u)] // left = (8 - 5) / 2 = 3/2
    [InlineData(4u, 1u, 4u, 1u, 1, 3u, 0, 1u)] // left = 2 + 1/3
    [InlineData(4u, 1u, 3u, 1u, 0, 1u, 0, 1u)] // top = (6 - 3) / 2 = 3/2
    public void CleanApertureThatDoesNotSelectWholeSamplesIsNotSupported(uint widthN, uint widthD, uint heightN, uint heightD, int horizontalN, uint horizontalD, int verticalN, uint verticalD)
    {
        var builder = Still();
        builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.CleanAperture(widthN, widthD, heightN, heightD, horizontalN, horizontalD, verticalN, verticalD)), true));
        var data = builder.Build();
        Assert.Null(ReferenceHeif.Parse(data).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
        Assert.Equal(ImageFormat.Avif, exception.Format);
    }

    [Theory]
    [InlineData(9u, 1u, 4u, 1u, 0, 1u, 0, 1u)] // wider than the image (left would be -1/2: reported as fractional by a strict reader, outside either way)
    [InlineData(4u, 1u, 4u, 1u, 3, 1u, 0, 1u)] // left = 2 + 3 = 5, right edge at 9
    [InlineData(4u, 1u, 4u, 1u, -3, 1u, 0, 1u)] // left = -1
    [InlineData(4u, 1u, 4u, 0u, 0, 1u, 0, 1u)] // a zero denominator
    [InlineData(0u, 1u, 4u, 1u, 0, 1u, 0, 1u)] // an empty aperture
    public void CleanApertureOutsideTheImageIsRejected(uint widthN, uint widthD, uint heightN, uint heightD, int horizontalN, uint horizontalD, int verticalN, uint verticalD)
    {
        var builder = Still();
        builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.CleanAperture(widthN, widthD, heightN, heightD, horizontalN, horizontalD, verticalN, verticalD)), true));
        Assert.ThrowsAny<ImageException>(() => Image.Identify(builder.Build()));
    }

    [Fact]
    public void ColorBoxesAreReadFromThePrimaryItem()
    {
        var builder = Still(bitDepth: 10, subsampling: 1);
        var icc = new byte[132];
        "RGB "u8.CopyTo(icc.AsSpan(16));
        var item = builder.GetItem(1);
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.ColorNclx(9, 16, 9, fullRange: false)), false));
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.ColorProfile(icc)), false));
        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal(new CicpColorDescription(9, 16, 9, fullRange: false), info.Metadata.Cicp);
        Assert.Equal("9/16/9 limited", info.Metadata.Cicp!.ToString());
        Assert.Equal(new ReferenceHeif.ColorDescription(9, 16, 9, FullRange: false), ReferenceHeif.Parse(data).PrimaryItem!.Nclx);
        Assert.Equal(132, ReferenceHeif.Parse(data).PrimaryItem!.IccProfile!.Value.Length);
    }

    // ISO/IEC 14496-12, sections 12.1.6, 12.1.7 and 12.1.4: big-endian fields, written by hand. MaxCLL 1,000 and MaxPALL
    // 400 cd/m2; the BT.2020 primaries in the order green (8500, 39850), blue (6550, 2300), red (35400, 14600), the D65
    // white point (15635, 16450), then 1,000 and 0.005 cd/m2 in units of 0.0001; pixels of 4 by 3
    private static readonly byte[] ContentLightLevelPayload = [0x03, 0xE8, 0x01, 0x90];
    private static readonly byte[] MasteringDisplayPayload = [0x21, 0x34, 0x9B, 0xAA, 0x19, 0x96, 0x08, 0xFC, 0x8A, 0x48, 0x39, 0x08, 0x3D, 0x13, 0x40, 0x42, 0x00, 0x98, 0x96, 0x80, 0x00, 0x00, 0x00, 0x32];
    private static readonly byte[] PixelAspectRatioPayload = [0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x03];

    [Fact]
    public void LightLevelsMasteringDisplayAndPixelShapeAreReadFromThePrimaryItem()
    {
        // Without the properties, nothing is declared
        var bare = Image.Identify(Still().Build()).Metadata;
        Assert.Null(bare.ContentLightLevel);
        Assert.Null(bare.MasteringDisplay);
        Assert.Null(bare.PixelAspectRatio);

        var builder = Still(bitDepth: 10);
        var item = builder.GetItem(1);
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Box("clli", ContentLightLevelPayload)), false));
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Box("mdcv", MasteringDisplayPayload)), false));
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Box("pasp", PixelAspectRatioPayload)), false));
        var metadata = Image.Identify(builder.Build()).Metadata;
        Assert.Equal(new ContentLightLevel(1000, 400), metadata.ContentLightLevel);
        Assert.Equal(new MasteringDisplayColorVolume(redX: 35400, redY: 14600, greenX: 8500, greenY: 39850, blueX: 6550, blueY: 2300, whitePointX: 15635, whitePointY: 16450, maxLuminance: 10_000_000, minLuminance: 50), metadata.MasteringDisplay);
        Assert.Equal(new PixelAspectRatio(4, 3), metadata.PixelAspectRatio);

        // The first property of a type describes the image; a property marked essential is one the reader understands
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Box("clli", [0x00, 0x01, 0x00, 0x02])), true));
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.Box("pasp", [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01])), true));
        metadata = Image.Identify(builder.Build()).Metadata;
        Assert.Equal(new ContentLightLevel(1000, 400), metadata.ContentLightLevel);
        Assert.Equal(new PixelAspectRatio(4, 3), metadata.PixelAspectRatio);

        // Square pixels are stored as they are declared, and the unknown light level (0) is a value
        var square = Still();
        square.GetItem(1).Associations.Add((square.AddProperty(HeifFileBuilder.Box("pasp", [0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x05])), false));
        square.GetItem(1).Associations.Add((square.AddProperty(HeifFileBuilder.Box("clli", [0x00, 0x00, 0x00, 0x00])), false));
        metadata = Image.Identify(square.Build()).Metadata;
        Assert.Equal(new PixelAspectRatio(5, 5), metadata.PixelAspectRatio);
        Assert.Equal(new ContentLightLevel(0, 0), metadata.ContentLightLevel);
    }

    [Theory]
    [InlineData("clli", 3)]
    [InlineData("mdcv", 23)]
    [InlineData("pasp", 7)]
    public void TruncatedPresentationPropertiesAreInvalid(string type, int length)
    {
        var payload = type switch
        {
            "clli" => ContentLightLevelPayload,
            "mdcv" => MasteringDisplayPayload,
            _ => PixelAspectRatioPayload,
        };

        var builder = Still();
        builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.Box(type, payload[..length])), false));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(builder.Build()));

        var sequence = Sequence(configure: track => track.SampleEntryBoxes.Add(HeifFileBuilder.Box(type, payload[..length]))).Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(sequence));
    }

    [Theory]
    [InlineData(0x00, 0x00, 1, 0, true)]
    [InlineData(0x00, 0x00, 2, 0, false)]
    [InlineData(0x00, 0x00, ushort.MaxValue, 0, false)]
    [InlineData(0x44, 0x00, 2, 8, false)]
    [InlineData(0x44, 0x00, 2, 16, true)]
    [InlineData(0x40, 0x00, ushort.MaxValue, 4, false)]
    public void ItemLocationExtentsAreBoundedByTheBytesOfTheBox(byte sizes, byte baseOffsetSize, ushort extentCount, int extentBytes, bool isValid)
    {
        // An extent without index, offset and length fields takes no byte of the box: its count does not depend on the size of the file
        var builder = Still();
        builder.OmittedBoxes.Add("iloc");
        builder.ExtraMetaBoxes.Add(HeifFileBuilder.FullBox("iloc", 0, 0, [sizes, baseOffsetSize], HeifFileBuilder.U16(1), HeifFileBuilder.U16(1), HeifFileBuilder.U16(0), HeifFileBuilder.U16(extentCount), new byte[extentBytes]));
        var data = builder.Build();
        if (isValid)
        {
            Assert.Equal((8, 6), (Image.Identify(data).Width, Image.Identify(data).Height));
        }
        else
        {
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        }
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 0u)]
    [InlineData(0u, 0u)]
    public void PixelAspectRatioWithAZeroTermIsInvalid(uint horizontalSpacing, uint verticalSpacing)
    {
        var box = HeifFileBuilder.Box("pasp", HeifFileBuilder.U32(horizontalSpacing), HeifFileBuilder.U32(verticalSpacing));
        var builder = Still();
        builder.GetItem(1).Associations.Add((builder.AddProperty(box), false));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(builder.Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryBoxes.Add(box)).Build()));
    }

    [Fact]
    public void LightLevelsMasteringDisplayAndPixelShapeAreReadFromTheSampleEntryOfASequence()
    {
        var bare = Image.Identify(Sequence().Build()).Metadata;
        Assert.Null(bare.ContentLightLevel);
        Assert.Null(bare.MasteringDisplay);
        Assert.Null(bare.PixelAspectRatio);

        var data = Sequence(configure: track =>
        {
            track.SampleEntryBoxes.Add(HeifFileBuilder.Box("pasp", PixelAspectRatioPayload));
            track.SampleEntryBoxes.Add(HeifFileBuilder.Box("clli", ContentLightLevelPayload));
            track.SampleEntryBoxes.Add(HeifFileBuilder.Box("mdcv", MasteringDisplayPayload));
        }).Build();
        var metadata = Image.Identify(data).Metadata;
        Assert.Equal(new ContentLightLevel(1000, 400), metadata.ContentLightLevel);
        Assert.Equal(new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 10_000_000, 50), metadata.MasteringDisplay);
        Assert.Equal(new PixelAspectRatio(4, 3), metadata.PixelAspectRatio);

        // The independent reader sees the same boxes
        var track = ReferenceHeif.Parse(data).ColorTrack!;
        Assert.Equal(["av1C", "pasp", "clli", "mdcv"], track.SampleEntryBoxes.Select(box => box.Type));
        Assert.Equal(MasteringDisplayPayload, track.FindSampleEntryBox("mdcv")!.Payload.ToArray());
    }

    [Theory]
    [InlineData("avif", new[] { "avif", "mif1", "miaf" }, true)]
    [InlineData("mif1", new[] { "mif1", "avif" }, true)] // a compatible brand is enough
    [InlineData("miaf", new[] { "avif" }, true)]
    [InlineData("MA1B", new[] { "avif", "MA1B" }, true)]
    [InlineData("mif1", new[] { "mif1", "miaf" }, false)] // HEIF, of no codec that the library reads
    [InlineData("mif1", new string[0], false)]
    public void BrandsOfTheFileTypeBoxSelectAvif(string majorBrand, string[] compatibleBrands, bool isAvif)
    {
        var builder = Still();
        builder.MajorBrand = majorBrand;
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatibleBrands)
        {
            builder.CompatibleBrands.Add(brand);
        }

        var data = builder.Build();
        Assert.Equal(isAvif, ReferenceHeif.Parse(data).HasBrand("avif"));
        if (isAvif)
        {
            Assert.Equal(ImageFormat.Avif, Image.Identify(data).Format);
        }
        else
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.Identify(data));
        }
    }

    [Theory]
    [InlineData("mif1", new[] { "mif1", "heic" })]
    [InlineData("heic", new[] { "mif1", "miaf" })]
    public void AnAv1ImageInAFileOfHeicBrandsIsNotRead(string majorBrand, string[] compatibleBrands)
    {
        // The brands declare images coded with HEVC: the file is an HEIC file whose primary item has another codec
        var builder = Still();
        builder.MajorBrand = majorBrand;
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatibleBrands)
        {
            builder.CompatibleBrands.Add(brand);
        }

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.Equal("image codec", exception.Feature);
    }

    [Theory]
    [InlineData("mif1", new[] { "heic", "avif" })]
    [InlineData("heic", new[] { "heic", "avif" })]
    [InlineData("avif", new[] { "heix", "avif" })]
    public void TheCodecOfTheImageSelectsTheFormatOfAFileOfBothBrands(string majorBrand, string[] compatibleBrands)
    {
        var builder = Still();
        builder.MajorBrand = majorBrand;
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatibleBrands)
        {
            builder.CompatibleBrands.Add(brand);
        }

        var info = Image.Identify(builder.Build());
        Assert.Equal(ImageFormat.Avif, info.Format);
        Assert.Equal(ImageFormat.Avif, info.Metadata.SourceFormat);
    }

    [Fact]
    public void FileWithTheSequenceBrandPresentsItsTrackAndAnyOtherFileItsPrimaryItem()
    {
        // The same boxes with and without the avis brand: a 13x7 primary item and a track of three 8x6 images
        var builder = Sequence();
        builder.Layout.Insert(1, HeifFileBuilder.MetaBox);
        builder.PrimaryItemId = 1;
        builder.AddItem(1, "av01", Payload, builder.AddProperty(HeifFileBuilder.ImageSize(13, 7)), -builder.AddProperty(HeifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0)));
        builder.CompatibleBrands.Add("avif");
        var sequence = Image.Identify(builder.Build());
        Assert.Equal((8, 6, 3), (sequence.Width, sequence.Height, sequence.FrameCount));
        Assert.True(sequence.IsAnimated);

        builder.MajorBrand = "avif";
        builder.CompatibleBrands.Clear();
        builder.CompatibleBrands.Add("avif");
        builder.CompatibleBrands.Add("mif1");
        var still = Image.Identify(builder.Build());
        Assert.Equal((13, 7, 1), (still.Width, still.Height, still.FrameCount));
        Assert.False(still.IsAnimated);
    }

    [Fact]
    public void BoxesAreWalkedWhateverTheirSizeForm()
    {
        // 64-bit sizes, free and unknown boxes, a uuid box (16 more bytes of header), an mdat box that extends to the end
        var builder = Still();
        builder.ExtraBoxes["free"] = HeifFileBuilder.Box("free", new byte[3]);
        builder.ExtraBoxes["large"] = HeifFileBuilder.LargeBox("skip", new byte[7]);
        builder.ExtraBoxes["uuid"] = HeifFileBuilder.Box("uuid", new byte[16], [1, 2, 3]);
        builder.ExtraBoxes["unknown"] = HeifFileBuilder.Box("abcd", [9]);
        builder.Layout.Clear();
        foreach (var box in new[] { HeifFileBuilder.FileTypeBox, "free", "large", "uuid", "unknown", HeifFileBuilder.MetaBox, "free", HeifFileBuilder.MediaDataBox })
        {
            builder.Layout.Add(box);
        }

        builder.LargeSizeBoxes.Add(HeifFileBuilder.MetaBox);
        builder.MediaDataExtendsToEnd = true;
        var data = builder.Build();
        var file = ReferenceHeif.Parse(data);
        Assert.Equal(["ftyp", "free", "skip", "uuid", "abcd", "meta", "free", "mdat"], file.TopLevelBoxes.Select(box => box.Type).ToArray());
        Assert.True(file.TopLevelBoxes[^1].ExtendsToEnd);
        Assert.Equal(Payload, file.ReadItemData(file.PrimaryItem!));
        Assert.Equal((8, 6), (Image.Identify(data).Width, Image.Identify(data).Height));
    }

    [Fact]
    public void MalformedBoxesAreInvalidContent()
    {
        // Two file type boxes
        var twice = Still();
        twice.Layout.Insert(1, HeifFileBuilder.FileTypeBox);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(twice.Build()));

        // A file type box that is not a list of four-character codes
        var odd = Still();
        odd.ExtraBoxes["odd"] = HeifFileBuilder.Box("ftyp", HeifFileBuilder.Ascii("avif"), new byte[4], HeifFileBuilder.Ascii("avi"));
        odd.Layout[0] = "odd";
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(odd.Build()));

        // A 64-bit size smaller than the 16 bytes of its own header
        var small = Still();
        small.ExtraBoxes["bad"] = HeifFileBuilder.Concat(HeifFileBuilder.U32(1), HeifFileBuilder.Ascii("free"), HeifFileBuilder.U64(12));
        small.Layout.Insert(1, "bad");
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(small.Build()));

        // No meta box at all: the file ends before any image is described
        var empty = Still();
        empty.Layout.Remove(HeifFileBuilder.MetaBox);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(empty.Build()));

        // A property index beyond the ipco box
        var association = Still();
        association.GetItem(1).Associations.Add((9, false));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(association.Build()));

        // A zero size
        var zero = Still(width: 0);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(zero.Build()));
    }

    [Fact]
    public void GridsAreValidatedFromTheItemModel()
    {
        HeifFileBuilder Grid(Action<HeifFileBuilder>? change = null)
        {
            var builder = new HeifFileBuilder { PrimaryItemId = 1 };
            var size = builder.AddProperty(HeifFileBuilder.ImageSize(8, 6));
            var configuration = builder.AddProperty(HeifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
            builder.AddItem(1, "grid", HeifFileBuilder.GridDescriptor(1, 2, 16, 6), builder.AddProperty(HeifFileBuilder.ImageSize(16, 6)));
            builder.AddItem(2, "av01", Payload, size, -configuration).IsHidden = true;
            builder.AddItem(3, "av01", Payload, size, -configuration).IsHidden = true;
            builder.References.Add(("dimg", 1, [2, 3]));
            change?.Invoke(builder);
            return builder;
        }

        var info = Image.Identify(Grid().Build());
        Assert.Equal((16, 6), (info.Width, info.Height));
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);

        // No list of tiles
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Grid(builder => builder.References.Clear()).Build()));

        // A tile that is a grid without tiles, a tile of another codec, and a coded tile with a transformation of its own
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Grid(builder => builder.GetItem(3).Type = "grid").Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Grid(builder => builder.GetItem(3).Type = "hvc1").Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Grid(builder => builder.GetItem(3).Associations.Add((builder.AddProperty(HeifFileBuilder.Rotation(1)), true))).Build()));

        // Tiles of different bit depths
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Grid(builder =>
        {
            var tile = builder.GetItem(3);
            tile.Associations[1] = (builder.AddProperty(HeifFileBuilder.Av1Configuration(1, 0, 10, monochrome: false, 0, 0)), true);
        }).Build()));
    }

    [Fact]
    public void TheImagesOfAnImageThatIsBuiltAreCountedOnceForEachUse()
    {
        // An overlay of 127 uses of an overlay of 128 uses of one coded image: 1 + 127 x (1 + 128) = 16,384 images, from
        // three items. One more use of the coded image is one image too many
        HeifFileBuilder Overlays(bool extraUse)
        {
            var builder = new HeifFileBuilder { PrimaryItemId = 1 };
            var size = builder.AddProperty(HeifFileBuilder.ImageSize(8, 6));
            var configuration = builder.AddProperty(HeifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
            uint[] inputs = extraUse ? [.. Enumerable.Repeat(2u, 127), 3] : [.. Enumerable.Repeat(2u, 127)];
            builder.AddItem(1, "iovl", OverlayDescriptor(8, 6, inputs.Length), size);
            builder.AddItem(2, "iovl", OverlayDescriptor(8, 6, 128), size).IsHidden = true;
            builder.AddItem(3, "av01", Payload, size, -configuration).IsHidden = true;
            builder.References.Add(("dimg", 1, inputs));
            builder.References.Add(("dimg", 2, [.. Enumerable.Repeat(3u, 128)]));
            return builder;
        }

        var info = Image.Identify(Overlays(extraUse: false).Build());
        Assert.Equal((8, 6), (info.Width, info.Height));

        var data = Overlays(extraUse: true).Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data).Dispose());
    }

    [Fact]
    public void ATreeThatGrowsExponentiallyFromAFewItemsIsRejectedBeforeItIsExpanded()
    {
        // Eight overlays, each of 1,024 uses of the next one, then a coded image: 1,024^8 coded images from nine items
        var builder = new HeifFileBuilder { PrimaryItemId = 1 };
        var size = builder.AddProperty(HeifFileBuilder.ImageSize(8, 6));
        var configuration = builder.AddProperty(HeifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
        for (var id = 1u; id <= 8; id++)
        {
            builder.AddItem(id, "iovl", OverlayDescriptor(8, 6, 1024), size).IsHidden = id is not 1;
            builder.References.Add(("dimg", id, [.. Enumerable.Repeat(id + 1, 1024)]));
        }

        builder.AddItem(9, "av01", Payload, size, -configuration).IsHidden = true;
        var data = builder.Build();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
        Assert.Contains("16384", exception.Message, StringComparison.Ordinal);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data).Dispose());
    }

    [Fact]
    public void AnImageThatSeveralDerivationsUseIsAsDeepAsItsDeepestUse()
    {
        // Item 11 is seven identity derivations of the coded image 20 (items 11 to 17). Under the primary overlay, the
        // tree has eight derivations; under the overlay 2 too, it has nine, even when the primary overlay used it first
        HeifFileBuilder Tree(params uint[] inputs)
        {
            var builder = new HeifFileBuilder { PrimaryItemId = 1 };
            var size = builder.AddProperty(HeifFileBuilder.ImageSize(8, 6));
            var configuration = builder.AddProperty(HeifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
            builder.AddItem(1, "iovl", OverlayDescriptor(8, 6, inputs.Length), size);
            builder.AddItem(2, "iovl", OverlayDescriptor(8, 6, 1), size).IsHidden = true;
            builder.References.Add(("dimg", 1, inputs));
            builder.References.Add(("dimg", 2, [11]));
            for (var id = 11u; id <= 17; id++)
            {
                builder.AddItem(id, "iden", [], size).IsHidden = true;
                builder.References.Add(("dimg", id, [id is 17 ? 20 : id + 1]));
            }

            builder.AddItem(20, "av01", Payload, size, -configuration).IsHidden = true;
            return builder;
        }

        Assert.Equal(8, Image.Identify(Tree(11, 11).Build()).Width);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Tree(2).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Tree(11, 2).Build()));
    }

    [Fact]
    public void AnImageThatSeveralDerivationsUseIsRenderedForEachUse()
    {
        // An overlay of the same coded image at two places, and of an identity derivation that mirrors it between them
        using var source = HeifEncoderTestImages.CreateSmoothImage(8, 4, transparent: false);
        using var stream = new MemoryStream();
        source.Save(stream, new AvifEncoder { Compression = AvifCompression.Lossy, Effort = 0 });
        var encoded = stream.ToArray();
        var builder = HeifFileBuilder.From(encoded);
        var coded = builder.Items.Single(item => item.Type == "av01");
        coded.IsHidden = true;
        var size = builder.AddProperty(HeifFileBuilder.ImageSize(8, 4));
        var mirror = builder.AddProperty(HeifFileBuilder.Mirror(1));
        builder.AddItem(100, "iovl", OverlayDescriptor(24, 4, (0, 0), (8, 0), (16, 0)), builder.AddProperty(HeifFileBuilder.ImageSize(24, 4)));
        builder.AddItem(101, "iden", [], size, -mirror).IsHidden = true;
        builder.References.Add(("dimg", 100, [coded.Id, 101, coded.Id]));
        builder.References.Add(("dimg", 101, [coded.Id]));
        builder.PrimaryItemId = 100;

        using var expected = Image.Load<Rgba32>(encoded);
        using var image = Image.Load<Rgba32>(builder.Build());
        Assert.Equal((24, 4), (image.Width, image.Height));
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal(expected.Frames[0][x, y], image.Frames[0][x, y]);
                Assert.Equal(expected.Frames[0][7 - x, y], image.Frames[0][8 + x, y]);
                Assert.Equal(expected.Frames[0][x, y], image.Frames[0][16 + x, y]);
            }
        }
    }

    // ISO/IEC 23008-12, section 6.6.2.2.2: version 0, 16-bit fields, a transparent fill color, the size of the canvas, then
    // the offset of each input image
    private static byte[] OverlayDescriptor(ushort width, ushort height, params (short X, short Y)[] offsets)
    {
        var parts = new List<byte[]> { new byte[2], new byte[8], HeifFileBuilder.U16(width), HeifFileBuilder.U16(height) };
        foreach (var (x, y) in offsets)
        {
            parts.Add(HeifFileBuilder.U16(unchecked((ushort)x)));
            parts.Add(HeifFileBuilder.U16(unchecked((ushort)y)));
        }

        return HeifFileBuilder.Concat([.. parts]);
    }

    private static byte[] OverlayDescriptor(ushort width, ushort height, int inputs) => OverlayDescriptor(width, height, [.. Enumerable.Repeat(((short)0, (short)0), inputs)]);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(64)]
    public void TheTilesOfAGridAreDecodedByWorkersAsByOne(int workers)
    {
        // Batches of 3 tiles span two rows of 5 tiles; 64 workers are more than the 20 tiles. The alpha plane is a grid too
        var data = CreateLargeGrid(transparent: true);
        using var expected = Image.Load(data);
        using var image = Image.Load(data, WithWorkers(workers));
        Assert.Equal((320, 256), (image.Width, image.Height));
        Assert.Equal(ImageSnapshots.CaptureFrame(expected.Frames[0]).ToArray(), ImageSnapshots.CaptureFrame(image.Frames[0]).ToArray());

        // A forward-only stream gives the same image
        using var stream = new MemoryStream(data);
        using var streamed = Image.Load(stream, WithWorkers(workers));
        Assert.Equal(ImageSnapshots.CaptureFrame(expected.Frames[0]).ToArray(), ImageSnapshots.CaptureFrame(streamed.Frames[0]).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public void AGridDecodedByWorkersFailsWithTheErrorOfItsFirstFailingTile(int workers)
    {
        // Tile 6 is truncated and tile 13 has no frame (a temporal delimiter alone): the error is the one of tile 6 alone,
        // whichever tile fails first
        var data = CreateLargeGrid(transparent: false);
        var first = HeifFileBuilder.From(data);
        var tiles = first.References.Single(reference => reference.Type == "dimg").ToItemIds;
        first.GetItem(tiles[6]).Data = first.GetItem(tiles[6]).Data[..^8];
        var expected = Assert.Throws<InvalidImageContentException>(() => Image.Load(first.Build()));

        var both = HeifFileBuilder.From(data);
        both.GetItem(tiles[6]).Data = both.GetItem(tiles[6]).Data[..^8];
        both.GetItem(tiles[13]).Data = TemporalDelimiter;
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(both.Build(), WithWorkers(workers)));
        Assert.Equal(expected.Message, exception.Message);

        // Tile 13 alone fails differently
        var last = HeifFileBuilder.From(data);
        last.GetItem(tiles[13]).Data = TemporalDelimiter;
        var other = Assert.Throws<InvalidImageContentException>(() => Image.Load(last.Build(), WithWorkers(workers)));
        Assert.NotEqual(expected.Message, other.Message);
    }

    // A temporal unit with a temporal delimiter and no frame
    private static byte[] TemporalDelimiter => [0x12, 0x00];

    private static ImageDecodeOptions WithWorkers(int workers) => new() { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers } };

    // A grid of 5x4 tiles of 64x64: from 16 tiles, the grid has the 65,536 samples that a decoding by several workers needs
    private static byte[] CreateLargeGrid(bool transparent)
    {
        using var image = HeifEncoderTestImages.CreateSmoothImage(320, 256, transparent);
        using var stream = new MemoryStream();
        image.Save(stream, new AvifEncoder { Compression = AvifCompression.Lossy, GridTileSize = new Size(64, 64), Effort = 0 });
        return stream.ToArray();
    }

    [Fact]
    public void HeaderOfAnImageSequenceGivesItsSizeAndItsFrameCount()
    {
        var data = Sequence(frames: 4, track => track.SampleEntryBoxes.Add(HeifFileBuilder.ColorNclx(1, 13, 6, fullRange: true))).Build();
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Avif, info.Format);
        Assert.Equal((8, 6), (info.Width, info.Height));
        Assert.Equal(4, info.FrameCount);
        Assert.True(info.IsAnimated);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(new CicpColorDescription(1, 13, 6, fullRange: true), info.Metadata.Cicp);
        Assert.Equal(ExifOrientation.TopLeft, info.Metadata.Orientation);

        var track = ReferenceHeif.Parse(data).ColorTrack!;
        Assert.Equal((8, 6, 4), (track.Width, track.Height, track.Samples.Count));
        Assert.All(track.Samples, sample => Assert.Equal((Payload.Length, 100u), (sample.Size, sample.Duration)));
    }

    [Fact]
    public void SequenceOfOneImageIsNotAnAnimation()
    {
        var info = Image.Identify(Sequence(frames: 1, track => track.EditList = Edits(repeating: true, 100)).Build());
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.Null(info.Animation);
    }

    // AV1 Image File Format, section 3.2: the edit list gives the duration of one play, the track header the duration of
    // all of them. trackDuration and segmentDuration are in units of the time scale of the movie; null writes every bit
    [Theory]
    [InlineData(null, false, 0UL, 300UL, null)] // no edit list: the repetitions are not known, forever
    [InlineData(1, false, 300UL, 300UL, 1)] // an edit list that does not repeat: once
    [InlineData(1, false, 300UL, 900UL, 1)] // whatever the track duration
    [InlineData(1, true, 300UL, null, null)] // repeating, indefinite track duration: forever
    [InlineData(0, true, 300UL, null, null)] // the same with 32-bit fields (0xFFFFFFFF)
    [InlineData(1, true, 300UL, 300UL, 1)]
    [InlineData(1, true, 300UL, 900UL, 3)] // exactly three plays
    [InlineData(0, true, 300UL, 900UL, 3)]
    [InlineData(1, true, 300UL, 901UL, 4)] // a fourth play that is cut short: rounded up
    [InlineData(1, true, 300UL, 899UL, 3)]
    [InlineData(1, true, 300UL, 1UL, 1)]
    [InlineData(1, true, 300UL, 300UL * int.MaxValue, int.MaxValue)] // the largest count
    [InlineData(1, true, 300UL, 300UL * 2147483648UL, null)] // above 2^31 - 1: forever
    [InlineData(1, true, 300UL, 300UL << 40, null)]
    [InlineData(1, true, 600UL, (ulong)long.MaxValue, null)] // what the FFmpeg muxer writes
    public void PlaysComeFromTheEditListAndTheTrackDuration(int? editListVersion, bool repeating, ulong segmentDuration, ulong? trackDuration, int? expectedPlays)
    {
        var data = Sequence(frames: 3, track =>
        {
            track.Duration = trackDuration ?? ulong.MaxValue;
            track.HeaderVersion = editListVersion ?? 1;
            track.EditList = editListVersion is { } version ? Edits(repeating, segmentDuration, version) : null;
        }).Build();
        var info = Image.Identify(data);
        Assert.NotNull(info.Animation);
        Assert.Equal(expectedPlays, info.Animation.TotalPlays);

        // The independent reader and the manifest rule compute the same count from the fields
        var track = ReferenceHeif.Parse(data).ColorTrack!;
        Assert.Equal(expectedPlays, track.TotalPlays);
        Assert.Equal(expectedPlays, EditListExpectation.GetTotalPlays(track.EditList is null ? null : new EditListExpectation
        {
            Repeating = track.EditList.IsRepeating,
            SegmentDuration = track.EditList.Entries[0].SegmentDuration is { } segment ? (long)segment : null,
            TrackDuration = track.Duration is { } duration ? (long)duration : null,
        }));
    }

    [Fact]
    public void EditListsThatDoNotPlayTheMediaForwardAreNotSupported()
    {
        // An empty edit; a rate other than 1; edits that are not in the order of the media, or that overlap
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, version: 0, mediaTime: -1)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, rate: 2)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, rate: 0, rateFraction: 0x4000)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track =>
        {
            track.EditList = Edits(repeating: false, 150, mediaTime: 100);
            track.EditList.Entries.Add((100, 0, 1, 0));
        }).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track =>
        {
            track.EditList = Edits(repeating: false, 150);
            track.EditList.Entries.Add((150, 149, 1, 0));
        }).Build()));

        // A media time below -1 is not one, and an edit list that shows no image is not a sequence
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, mediaTime: -2)).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, mediaTime: 5000)).Build()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditThatEndsBeforeTheLastImageShowsThePartOfTheTrackItCovers(bool repeating)
    {
        // Three images of 100 units; the edit presents the first 100 units of the media, that is the first image only
        // (ISO/IEC 14496-12, section 8.6.6: segment_duration is the duration of the edit)
        var data = Sequence(frames: 3, track =>
        {
            track.Duration = 300;
            track.EditList = Edits(repeating, segmentDuration: 100);
        }).Build();
        var track = ReferenceHeif.Parse(data).ColorTrack!;
        Assert.Equal(300L, track.Samples.Sum(sample => (long)sample.Duration));
        Assert.Equal(100UL, track.EditList!.Entries[0].SegmentDuration);
        Assert.Equal([0], track.GetPresentation(1000).Select(shown => shown.SampleIndex));
        Assert.Equal(1, Image.Identify(data).FrameCount);
        Assert.Null(Image.Identify(data).Animation);
    }

    /// <summary>
    /// The images that an edit list shows and the time each one is shown for: three images of 100 units of a time scale
    /// of 1000, and a movie whose time scale is 1000 too. The expected values are counted by hand from the intervals.
    /// </summary>
    [Fact]
    public void EditListsShowImagesForTheTimeTheirEditsCover()
    {
        // Two edits, [0, 150) and [150, 300): together they are the whole media
        AssertFrames([(150, 0), (150, 150)], 3);

        // An edit that starts inside the first image, at 100/3 of it: three images, the first one shown less
        AssertFrames([(300, 33)], 3);

        // [50, 120) and [250, 300): the first image for 50 units, the second for 20, the third for 50
        AssertFrames([(70, 50), (50, 250)], 3);

        // [100, 200): the second image only; [200, 500): the third one, to the end of the media
        AssertFrames([(100, 100)], 1);
        AssertFrames([(300, 200)], 1);

        // The count of a header identification, which does not walk the samples, against the independent reader
        void AssertFrames((ulong Duration, long MediaTime)[] edits, int expected)
        {
            var data = Sequence(frames: 3, track =>
            {
                track.Duration = 300;
                track.EditList = Edits(repeating: false, edits[0].Duration, mediaTime: edits[0].MediaTime);
                foreach (var (duration, mediaTime) in edits.Skip(1))
                {
                    track.EditList.Entries.Add((duration, mediaTime, 1, 0));
                }
            }).Build();
            Assert.Equal(expected, ReferenceHeif.Parse(data).ColorTrack!.GetPresentation(1000).Count);
            Assert.Equal(expected, Image.Identify(data).FrameCount);
        }
    }

    [Fact]
    public void AlphaTrackIsTheAuxiliaryTrackOfTheColorTrack()
    {
        HeifFileBuilder WithAlpha(Action<HeifFileBuilder.Track, HeifFileBuilder.Track>? change = null)
        {
            var builder = Sequence();
            var alpha = new HeifFileBuilder.Track(2, "auxv", 8, 6) { Timescale = 1000, Duration = 300 };
            alpha.SampleEntryBoxes.Add(HeifFileBuilder.Av1Configuration(0, 0, 8, monochrome: true, 1, 1));
            alpha.SampleEntryBoxes.Add(HeifFileBuilder.FullBox("auxi", 0, 0, Encoding.ASCII.GetBytes(ReferenceHeif.AlphaAuxiliaryType), [0]));
            alpha.References.Add(("auxl", [1]));
            for (var i = 0; i < 3; i++)
            {
                alpha.Samples.Add((Payload, 100));
            }

            builder.Tracks.Add(alpha);
            change?.Invoke(builder.Tracks[0], alpha);
            return builder;
        }

        var data = WithAlpha().Build();
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.True(info.MayHaveTransparency);
        Assert.Equal(2u, ReferenceHeif.Parse(data).AlphaTrack!.Id);

        // An auxiliary track of another kind is not an alpha plane
        var depth = WithAlpha((_, alpha) => alpha.SampleEntryBoxes[1] = HeifFileBuilder.FullBox("auxi", 0, 0, "urn:mpeg:mpegB:cicp:systems:auxiliary:depth"u8.ToArray(), [0])).Build();
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(depth).PixelFormat);
        Assert.Null(ReferenceHeif.Parse(depth).AlphaTrack);

        // Colors that are premultiplied by the alpha track are read; by a track that is not the alpha track, it is invalid
        var premultiplied = WithAlpha((color, _) => color.References.Add(("prem", [2]))).Build();
        Assert.Equal(PixelFormat.Rgba32, Image.Identify(premultiplied).PixelFormat);
        Assert.True(ReferenceHeif.Parse(premultiplied).IsPremultiplied);
        Assert.False(ReferenceHeif.Parse(data).IsPremultiplied);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(WithAlpha((color, _) => color.References.Add(("prem", [7]))).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.References.Add(("prem", [2]))).Build()));

        // An alpha plane of another size: not supported; another number of images: invalid
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((_, alpha) => alpha.Width = 4).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(WithAlpha((_, alpha) => alpha.Samples.RemoveAt(2)).Build()));
    }

    [Theory]
    // ISO/IEC 14496-12, section 8.3.2: the matrix maps the point (p, q) of an image to (a p + c q + x, b p + d q + y).
    // The orientations are derived by hand from the definition of the EXIF tag (where the row 0 and the column 0 of the
    // stored image are displayed)
    [InlineData(1, 0, 0, 1, ExifOrientation.TopLeft)]
    [InlineData(-1, 0, 0, 1, ExifOrientation.TopRight)] // p' = -p: the column 0 is the right side
    [InlineData(-1, 0, 0, -1, ExifOrientation.BottomRight)] // a half turn
    [InlineData(1, 0, 0, -1, ExifOrientation.BottomLeft)] // q' = -q: the row 0 is the bottom
    [InlineData(0, 1, 1, 0, ExifOrientation.LeftTop)] // p' = q, q' = p: the row 0 is the left side, the column 0 the top
    [InlineData(0, 1, -1, 0, ExifOrientation.RightTop)] // p' = -q, q' = p: the row 0 is the right side, a quarter turn clockwise
    [InlineData(0, -1, -1, 0, ExifOrientation.RightBottom)] // p' = -q, q' = -p
    [InlineData(0, -1, 1, 0, ExifOrientation.LeftBottom)] // p' = q, q' = -p: a quarter turn anti-clockwise
    public void TheMatrixOfTheTrackHeaderIsTheOrientationOfASequence(int a, int b, int c, int d, ExifOrientation expected)
    {
        const int One = 0x00010000;
        foreach (var version in new[] { 0, 1 })
        {
            // Whatever the translation, which only moves the result
            foreach (var (x, y) in new[] { (0, 0), (8 * One, 6 * One), (-3 * One, 7) })
            {
                var data = Sequence(configure: track =>
                {
                    track.HeaderVersion = version;
                    track.Matrix = [a * One, b * One, 0, c * One, d * One, 0, x, y, 0x40000000];
                }).Build();
                var info = Image.Identify(data);
                Assert.Equal(expected, info.Metadata.Orientation);
                Assert.Equal((8, 6), (info.Width, info.Height)); // reported, never applied
                Assert.Equal((int)expected, ReferenceHeif.Parse(data).ColorTrack!.Orientation);
            }
        }
    }

    [Theory]
    [InlineData(0x00020000, 0, 0, 0x00020000, 0, 0, 0x40000000)] // twice the size
    [InlineData(0x00010000, 0x00008000, 0, 0x00010000, 0, 0, 0x40000000)] // a shear
    [InlineData(0x0000B505, 0x0000B505, -0x0000B505, 0x0000B505, 0, 0, 0x40000000)] // a turn of 45 degrees
    [InlineData(0, 0x00010000, 0, 0, 0, 0, 0x40000000)] // no inverse
    [InlineData(0, 0x00010000, -0x00010000, 0, 0x00010000, 0, 0x40000000)] // a projective term
    [InlineData(0, 0x00010000, -0x00010000, 0, 0, 0, 0x20000000)] // a divisor of one half
    public void AMatrixThatIsNotAQuarterTurnOrAMirrorIsNotAnOrientation(int a, int b, int c, int d, int u, int v, int w)
    {
        // Such a matrix places the track in the presentation of a player: the images are returned as they are stored
        var data = Sequence(configure: track => track.Matrix = [a, b, u, c, d, v, 0, 0, w]).Build();
        Assert.Equal(ExifOrientation.TopLeft, Image.Identify(data).Metadata.Orientation);
        Assert.Equal(1, ReferenceHeif.Parse(data).ColorTrack!.Orientation);
    }

    [Fact]
    public void TheCleanApertureOfTheSampleEntryCropsTheImagesOfASequence()
    {
        // 8x6 images; an aperture of 5x4 whose center is half a sample right of the center: left = (8 - 5) / 2 + 1/2 = 2, top = 1
        var data = Sequence(configure: track => track.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1))).Build();
        var info = Image.Identify(data);
        Assert.Equal((5, 4), (info.Width, info.Height));
        Assert.Equal(3, info.FrameCount);
        Assert.Equal((2, 1, 5, 4), ReferenceHeif.Parse(data).ColorTrack!.CleanAperture!.GetRectangle(8, 6));

        // The aperture applies before the matrix of the track header, which stays the orientation
        var rotated = Sequence(configure: track =>
        {
            track.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1));
            track.Matrix = [0, 0x00010000, 0, -0x00010000, 0, 0, 0, 0, 0x40000000];
        }).Build();
        info = Image.Identify(rotated);
        Assert.Equal((5, 4), (info.Width, info.Height));
        Assert.Equal(ExifOrientation.RightTop, info.Metadata.Orientation);

        // An aperture that does not select whole samples is not supported; one that is not inside the images is invalid
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(5, 2, 4, 1, 0, 1, 0, 1))).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(9, 1, 4, 1, 1, 2, 0, 1))).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(5, 1, 4, 0, 1, 2, 0, 1))).Build()));
    }

    [Fact]
    public void TheAlphaTrackHasTheTransformationsOfTheColorTrackOrNone()
    {
        int[] quarterTurn = [0, 0x00010000, 0, -0x00010000, 0, 0, 0, 0, 0x40000000];
        int[] halfTurn = [-0x00010000, 0, 0, 0, -0x00010000, 0, 0, 0, 0x40000000];
        var aperture = HeifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1);
        static HeifFileBuilder WithAlpha(Action<HeifFileBuilder.Track, HeifFileBuilder.Track> change)
        {
            var builder = Sequence();
            var alpha = new HeifFileBuilder.Track(2, "auxv", 8, 6) { Timescale = 1000, Duration = 300 };
            alpha.SampleEntryBoxes.Add(HeifFileBuilder.Av1Configuration(0, 0, 8, monochrome: true, 1, 1));
            alpha.SampleEntryBoxes.Add(HeifFileBuilder.FullBox("auxi", 0, 0, System.Text.Encoding.UTF8.GetBytes(ReferenceHeif.AlphaAuxiliaryType), [0]));
            alpha.References.Add(("auxl", [1]));
            for (var i = 0; i < 3; i++)
            {
                alpha.Samples.Add((Payload, 100));
            }

            builder.Tracks.Add(alpha);
            change(builder.Tracks[0], alpha);
            return builder;
        }

        // The same transformations on both tracks, or on the color track only
        var both = Image.Identify(WithAlpha((color, alpha) =>
        {
            color.Matrix = quarterTurn;
            alpha.Matrix = quarterTurn;
            color.SampleEntryBoxes.Add(aperture);
            alpha.SampleEntryBoxes.Add(aperture);
        }).Build());
        Assert.Equal((5, 4), (both.Width, both.Height));
        Assert.Equal(ExifOrientation.RightTop, both.Metadata.Orientation);
        Assert.Equal(PixelFormat.Rgba32, both.PixelFormat);
        var colorOnly = Image.Identify(WithAlpha((color, _) =>
        {
            color.Matrix = quarterTurn;
            color.SampleEntryBoxes.Add(aperture);
        }).Build());
        Assert.Equal((5, 4), (colorOnly.Width, colorOnly.Height));
        Assert.Equal(ExifOrientation.RightTop, colorOnly.Metadata.Orientation);

        // Other ones are not supported
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((color, alpha) =>
        {
            color.Matrix = quarterTurn;
            alpha.Matrix = halfTurn;
        }).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((_, alpha) => alpha.Matrix = halfTurn).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((_, alpha) => alpha.SampleEntryBoxes.Add(aperture)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((color, alpha) =>
        {
            color.SampleEntryBoxes.Add(aperture);
            alpha.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(4, 1, 4, 1, 0, 1, 0, 1));
        }).Build()));
    }

    [Fact]
    public void SequenceWithoutUsableTrackIsInvalid()
    {
        // The avis brand promises an image sequence: a track of another codec, of another handler, without samples, or a
        // zero time scale is not one
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryType = "hvc1").Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.HandlerType = "vide").Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(frames: 0).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.Timescale = 0).Build()));

        // No moov box at all
        var bare = Sequence();
        bare.Layout.Remove(HeifFileBuilder.MovieBox);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(bare.Build()));

        // A movie fragment is not supported
        var fragmented = Sequence();
        fragmented.ExtraBoxes["moof"] = HeifFileBuilder.Box("moof");
        fragmented.Layout.Insert(1, "moof");
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(fragmented.Build()));
    }

    // ITU-T H.273, section 8.3: E'R = E'Y + 2 (1 - KR) E'PR, E'B = E'Y + 2 (1 - KB) E'PB, E'G from the luma equation.
    // Full range: E'Y = Y / 255, E'P = (C - 128) / 255. Limited range: E'Y = (Y - 16) / 219, E'P = (C - 128) / 224
    [Theory]
    [InlineData(6, true, 255, 128, 128, 255, 255, 255)]
    [InlineData(6, true, 0, 128, 128, 0, 0, 0)]
    [InlineData(6, true, 76, 85, 255, 254, 0, 0)] // R = 76 + 1.402 x 127 = 254.05, G = 76 + 0.344136 x 43 - 0.714136 x 127 = 0.10, B = 76 - 1.772 x 43 = -0.2
    [InlineData(6, true, 100, 128, 200, 201, 49, 100)] // R = 100 + 1.402 x 72 = 200.94, G = 100 - 0.714136 x 72 = 48.58
    [InlineData(2, true, 100, 128, 200, 201, 49, 100)] // unspecified is converted as BT.601
    [InlineData(5, true, 100, 128, 200, 201, 49, 100)] // BT.470BG has the weights of BT.601
    [InlineData(6, false, 16, 128, 128, 0, 0, 0)]
    [InlineData(6, false, 235, 128, 128, 255, 255, 255)]
    [InlineData(6, false, 126, 128, 128, 128, 128, 128)] // 110 / 219 x 255 = 128.08
    [InlineData(6, false, 81, 90, 240, 254, 0, 0)] // R = (65 / 219 + 1.402 x 0.5) x 255 = 254.44
    [InlineData(1, true, 100, 128, 200, 213, 66, 100)] // BT.709: R = 100 + 1.5748 x 72 = 213.39, G = 100 - 0.468124 x 72 = 66.30
    [InlineData(9, true, 100, 128, 200, 206, 59, 100)] // BT.2020: R = 100 + 1.4746 x 72 = 206.17, G = 100 - 0.571353 x 72 = 58.86
    [InlineData(4, true, 100, 128, 200, 201, 49, 100)] // FCC: R = 100 + 1.40 x 72 = 200.8, G = 100 - (2 x 0.3 x 0.7 / 0.59) x 72 = 48.75
    [InlineData(7, true, 100, 128, 200, 213, 66, 100)] // SMPTE 240M: R = 100 + 1.576 x 72 = 213.47, G = 100 - (2 x 0.212 x 0.788 / 0.701) x 72 = 65.68
    [InlineData(8, true, 128, 178, 128, 78, 178, 78)] // YCgCo: G = Y + Cg, R = B = Y - Cg
    [InlineData(8, true, 128, 128, 178, 178, 128, 78)] // R = Y - Cg + Co, B = Y - Cg - Co
    [InlineData(0, true, 10, 20, 30, 30, 10, 20)] // identity: the planes are G, B, R
    public void ReferenceConverterAppliesTheEquationsOfH273(int matrix, bool fullRange, int y, int u, int v, int red, int green, int blue)
    {
        var rgb = ReferenceYuvConverter.ConvertToRgb([(ushort)y], [(ushort)u], [(ushort)v], 1, 1, 8, 0, 0, ReferenceYuvConverter.ChromaUnknown, matrix, fullRange, 255);
        Assert.Equal([red, green, blue], rgb);
    }

    // ITU-T H.273, section 8.3, MatrixCoefficients 11 (SMPTE ST 2085): E'Y = E'G, E'PB = (0.986566 E'B - E'Y) / 2 and
    // E'PR = (E'R - 0.991902 E'Y) / 2, hence G = Y, B = (Y + 2 PB) / 0.986566 and R = 0.991902 Y + 2 PR, computed by hand
    [Theory]
    [InlineData(true, 100, 140, 90, 23, 100, 126)] // R = 99.19 - 76 = 23.19, B = (100 + 24) / 0.986566 = 125.69
    [InlineData(true, 200, 100, 150, 242, 200, 146)] // R = 198.38 + 44 = 242.38, B = (200 - 56) / 0.986566 = 145.96
    [InlineData(true, 128, 128, 128, 127, 128, 130)] // a gray of the planes is not a gray: R = 126.96, B = 129.74
    [InlineData(false, 126, 140, 120, 109, 128, 158)] // (110 / 219, 12 / 224, -8 / 224): R = 108.83, G = 128.08, B = 157.52
    public void ReferenceConverterAppliesTheEquationsOfSmpte2085(bool fullRange, int y, int u, int v, int red, int green, int blue)
    {
        var rgb = ReferenceYuvConverter.ConvertToRgb([(ushort)y], [(ushort)u], [(ushort)v], 1, 1, 8, 0, 0, ReferenceYuvConverter.ChromaUnknown, 11, fullRange, 255);
        Assert.Equal([red, green, blue], rgb);
    }

    [Fact]
    public void ReferenceConverterDerivesTheChromaticityDerivedMatrixFromThePrimaries()
    {
        // KR and KB are the luminance of the red and of the blue primary: for BT.709 and BT.2020, the values that the
        // recommendations round to 0.2126 and 0.0722, and to 0.2627 and 0.0593 (40 digits computed apart from the library)
        var bt709 = ReferenceYuvConverter.GetLuminanceWeights(1)!.Value;
        Assert.Equal(0.212639005872, (double)bt709.Kr / (double)bt709.Denominator, 1e-11);
        Assert.Equal(0.072192315361, (double)bt709.Kb / (double)bt709.Denominator, 1e-11);
        var bt2020 = ReferenceYuvConverter.GetLuminanceWeights(9)!.Value;
        Assert.Equal(0.262700212011, (double)bt2020.Kr / (double)bt2020.Denominator, 1e-11);
        Assert.Equal(0.059301716470, (double)bt2020.Kb / (double)bt2020.Denominator, 1e-11);
        var displayP3 = ReferenceYuvConverter.GetLuminanceWeights(12)!.Value;
        Assert.Equal(0.228974564070, (double)displayP3.Kr / (double)displayP3.Denominator, 1e-11);
        Assert.Equal(0.079286914094, (double)displayP3.Kb / (double)displayP3.Denominator, 1e-11);

        // The primaries of CIE XYZ: the luminance is the second component
        var xyz = ReferenceYuvConverter.GetLuminanceWeights(10)!.Value;
        Assert.True(xyz.Kr.IsZero && xyz.Kb.IsZero);
        Assert.Null(ReferenceYuvConverter.GetLuminanceWeights(2));
        Assert.Null(ReferenceYuvConverter.GetLuminanceWeights(3));

        // An 8-bit sample is the same as with the rounded weights of BT.709: R = 100 + 1.574722 x 72 = 213.38, G = 66.29
        Assert.Equal([213, 66, 100], ReferenceYuvConverter.ConvertToRgb([100], [128], [200], 1, 1, 8, 0, 0, 0, 12, fullRange: true, 255, colorPrimaries: 1));

        // A 12-bit sample at 16 bits is not: G = 23538.36 with the exact weights, 23540.38 with 0.2126 and 0.0722
        Assert.Equal([65535, 23538, 16502], ReferenceYuvConverter.ConvertToRgb([2048], [1500], [3500], 1, 1, 12, 0, 0, 0, 12, fullRange: true, 65535, colorPrimaries: 1));
        Assert.Equal([65535, 23540, 16502], ReferenceYuvConverter.ConvertToRgb([2048], [1500], [3500], 1, 1, 12, 0, 0, 0, 1, fullRange: true, 65535));

        // Primaries without chromaticities give no matrix
        Assert.Throws<NotSupportedException>(() => ReferenceYuvConverter.ConvertToRgb([0], [0], [0], 1, 1, 8, 0, 0, 0, 12, fullRange: true, 255, colorPrimaries: 2));
    }

    [Fact]
    public void ReferenceConverterScalesDeepSamplesExactly()
    {
        // v x 65535 / 1023 and v x 65535 / 4095, rounded to nearest: 512 -> 32799.53, 1 -> 64.06, 2048 -> 32775.50
        Assert.Equal(65535, ReferenceYuvConverter.ScaleSample(1023, 10, fullRange: true, 65535));
        Assert.Equal(32800, ReferenceYuvConverter.ScaleSample(512, 10, fullRange: true, 65535));
        Assert.Equal(64, ReferenceYuvConverter.ScaleSample(1, 10, fullRange: true, 65535));
        Assert.Equal(32776, ReferenceYuvConverter.ScaleSample(2048, 12, fullRange: true, 65535));
        Assert.Equal(16, ReferenceYuvConverter.ScaleSample(1, 12, fullRange: true, 65535));
        Assert.Equal(200, ReferenceYuvConverter.ScaleSample(200, 8, fullRange: true, 255));

        // Limited range: (v - 16 x 4) / (219 x 4), clipped
        Assert.Equal(0, ReferenceYuvConverter.ScaleSample(60, 10, fullRange: false, 65535));
        Assert.Equal(65535, ReferenceYuvConverter.ScaleSample(940, 10, fullRange: false, 65535));
        Assert.Equal(32768, ReferenceYuvConverter.ScaleSample(502, 10, fullRange: false, 65535)); // 438 / 876 = 1/2: 32767.5 rounds up

        // A 10-bit identity image at 16 bits
        Assert.Equal([65535, 0, 32800], ReferenceYuvConverter.ConvertToRgb([0], [512], [1023], 1, 1, 10, 0, 0, 0, 0, fullRange: true, 65535));
    }

    [Fact]
    public void ReferenceConverterUpsamplesChromaAtTheDeclaredPosition()
    {
        // A 4x4 image whose 2x2 chroma plane is 0 16 / 32 48
        ushort[] chroma = [0, 16, 32, 48];

        // Centered: weights 9, 3, 3, 1 over 16 around each luma sample, edges replicated
        var centered = ReferenceYuvConverter.UpsampleChroma(chroma, 4, 4, 1, 1, ReferenceYuvConverter.ChromaUnknown);
        Assert.Equal([
            0, 4, 12, 16,
            8, 12, 20, 24,
            24, 28, 36, 40,
            32, 36, 44, 48,
        ], centered);

        // Co-located with the top-left luma sample: the other luma samples are halfway to the next chroma sample
        var colocated = ReferenceYuvConverter.UpsampleChroma(chroma, 4, 4, 1, 1, ReferenceYuvConverter.ChromaColocated);
        Assert.Equal([
            0, 8, 16, 16,
            16, 24, 32, 32,
            32, 40, 48, 48,
            32, 40, 48, 48,
        ], colocated);

        // Vertical: on the luma columns like the co-located samples, between the luma rows like the centered ones
        var vertical = ReferenceYuvConverter.UpsampleChroma(chroma, 4, 4, 1, 1, ReferenceYuvConverter.ChromaVertical);
        Assert.Equal([
            0, 8, 16, 16,
            8, 16, 24, 24,
            24, 32, 40, 40,
            32, 40, 48, 48,
        ], vertical);

        // 4:2:2: centered between two luma columns (3, 1 over 4), whatever the declared position; an odd width replicates the edge
        Assert.Equal([0, 4, 12, 16, 16], ReferenceYuvConverter.UpsampleChroma([0, 16, 16], 5, 1, 1, 0, ReferenceYuvConverter.ChromaColocated));

        // Halves round up: (3 x 2 + 0) / 4 = 1.5 -> 2, (3 x 0 + 2) / 4 = 0.5 -> 1
        Assert.Equal([2, 2, 1, 0], ReferenceYuvConverter.UpsampleChroma([2, 0], 4, 1, 1, 0, 0));
    }

    /// <summary>
    /// The conversions that go through the transfer characteristics, against a calculation that does not share anything
    /// with the harness: the equations of ITU-T H.273 evaluated with the <c>decimal</c> module of Python at 50 digits
    /// (its own <c>ln</c>, <c>exp</c> and power functions, and a Gaussian elimination for the matrices of ICtCp and for
    /// the luminance of the primaries). The samples include neutral colors (which every matrix keeps), colors outside of
    /// the gamut (which are clipped) and the ends of the range.
    /// </summary>
    [Theory]
    [MemberData(nameof(TransferMatrixLiterals))]
    public void ReferenceConverterMatchesAnIndependentCalculationOfTheMatricesThatGoThroughTheTransferFunction(int matrix, int primaries, int transfer, bool fullRange, int bitDepth, int[] expected)
    {
        ushort[][] samples = bitDepth == 8
            ? [[128, 128, 128], [200, 90, 160], [60, 180, 100], [235, 128, 128], [16, 128, 128], [120, 40, 230], [255, 255, 255], [0, 0, 0]]
            : [[512, 512, 512], [800, 360, 640], [240, 720, 400], [940, 512, 512], [64, 512, 512], [480, 160, 920]];
        Assert.HasCount(3 * samples.Length, expected);
        for (var i = 0; i < samples.Length; i++)
        {
            var actual = ReferenceYuvConverter.ConvertToRgb([samples[i][0]], [samples[i][1]], [samples[i][2]], 1, 1, bitDepth, 0, 0, 0, matrix, fullRange, bitDepth == 8 ? 255 : 65535, primaries, transfer);
            Assert.Equal(expected.AsSpan(3 * i, 3).ToArray(), actual);
        }
    }

    public static TheoryData<int, int, int, bool, int, int[]> TransferMatrixLiterals => new()
    {
        // BT.2020 constant luminance, with the transfer characteristics of BT.2020 (10-bit)
        { 10, 9, 14, true, 8, [128, 128, 128, 232, 192, 126, 12, 61, 142, 235, 235, 235, 16, 16, 16, 221, 48, 0, 255, 255, 255, 0, 0, 0] },
        { 10, 9, 14, true, 10, [32800, 32800, 32800, 59399, 49099, 32355, 3047, 15636, 36454, 60218, 60218, 60218, 4100, 4100, 4100, 56725, 12327, 0] },
        { 10, 9, 14, false, 8, [130, 130, 130, 250, 204, 130, 0, 50, 145, 255, 255, 255, 0, 0, 0, 236, 18, 0, 255, 255, 255, 0, 0, 0] },
        { 10, 9, 14, false, 10, [33516, 33516, 33516, 64366, 52551, 33490, 0, 12796, 37234, 65535, 65535, 65535, 0, 0, 0, 60779, 4541, 0] },

        // Chromaticity-derived constant luminance, with the Display P3 primaries and the sRGB transfer characteristics
        { 13, 12, 13, true, 8, [128, 128, 128, 231, 195, 127, 10, 55, 132, 235, 235, 235, 16, 16, 16, 219, 55, 0, 255, 255, 255, 0, 0, 0] },
        { 13, 12, 13, true, 10, [32800, 32800, 32800, 59190, 49991, 32470, 2580, 14067, 33712, 60218, 60218, 60218, 4100, 4100, 4100, 56060, 13983, 0] },
        { 13, 12, 13, false, 8, [130, 130, 130, 250, 208, 131, 0, 40, 133, 255, 255, 255, 0, 0, 0, 234, 17, 0, 255, 255, 255, 0, 0, 0] },
        { 13, 12, 13, false, 10, [33516, 33516, 33516, 64127, 53564, 33620, 0, 10362, 34103, 65535, 65535, 65535, 0, 0, 0, 60019, 4448, 0] },

        // ICtCp with PQ
        { 14, 9, 16, true, 8, [128, 128, 128, 214, 194, 161, 48, 44, 100, 235, 235, 235, 16, 16, 16, 153, 65, 0, 255, 202, 255, 0, 20, 0] },
        { 14, 9, 16, true, 10, [32800, 32800, 32800, 54816, 49804, 41240, 12381, 11300, 25720, 60218, 60218, 60218, 4100, 4100, 4100, 39272, 16870, 0] },
        { 14, 9, 16, false, 8, [130, 130, 130, 230, 207, 169, 40, 0, 97, 255, 255, 255, 0, 0, 0, 157, 0, 0, 255, 255, 255, 0, 0, 0] },
        { 14, 9, 16, false, 10, [33516, 33516, 33516, 59021, 53303, 43323, 10307, 0, 24920, 65535, 65535, 65535, 0, 0, 0, 40399, 0, 0] },

        // ICtCp with HLG, whose coefficients are other ones
        { 14, 9, 18, true, 8, [128, 128, 128, 226, 188, 126, 31, 52, 136, 235, 235, 235, 16, 16, 16, 182, 64, 0, 255, 137, 255, 0, 41, 0] },
        { 14, 9, 18, true, 10, [32800, 32800, 32800, 57836, 48296, 32337, 7907, 13440, 34776, 60218, 60218, 60218, 4100, 4100, 4100, 46781, 16388, 0] },
        { 14, 9, 18, false, 8, [130, 130, 130, 243, 200, 127, 13, 39, 137, 255, 255, 255, 0, 0, 0, 188, 48, 0, 255, 234, 255, 0, 20, 0] },
        { 14, 9, 18, false, 10, [33516, 33516, 33516, 62380, 51474, 32679, 3357, 9956, 35209, 65535, 65535, 65535, 0, 0, 0, 48428, 12450, 0] },
    };

    [Fact]
    public void ReferenceConverterRejectsWhatTheLibraryRejects()
    {
        Assert.False(ReferenceYuvConverter.IsSupportedMatrix(3));
        Assert.False(ReferenceYuvConverter.IsSupportedMatrix(15));
        for (var matrix = 4; matrix <= 14; matrix++)
        {
            Assert.True(ReferenceYuvConverter.IsSupportedMatrix(matrix));
        }

        // The matrices that go through the transfer characteristics need them, ICtCp needs PQ or HLG, and the
        // chromaticity-derived ones need primaries
        Assert.Throws<NotSupportedException>(() => ReferenceYuvConverter.ConvertToRgb([0], [0], [0], 1, 1, 8, 0, 0, 0, 3, fullRange: true, 255));
        Assert.Throws<NotSupportedException>(() => ReferenceYuvConverter.ConvertToRgb([0], [0], [0], 1, 1, 8, 0, 0, 0, 10, fullRange: true, 255, colorPrimaries: 9));
        Assert.Throws<NotSupportedException>(() => ReferenceYuvConverter.ConvertToRgb([0], [0], [0], 1, 1, 8, 0, 0, 0, 13, fullRange: true, 255, colorPrimaries: 2, transferCharacteristics: 13));
        Assert.Throws<NotSupportedException>(() => ReferenceYuvConverter.ConvertToRgb([0], [0], [0], 1, 1, 8, 0, 0, 0, 14, fullRange: true, 255, colorPrimaries: 9, transferCharacteristics: 13));
        Assert.Throws<ArgumentException>(() => ReferenceYuvConverter.ConvertToRgb([0, 0], [0], [0], 2, 1, 8, 1, 0, 0, 0, fullRange: true, 255));
    }

    [Fact]
    public void BuilderAndIndependentReaderAgreeOnEveryStorageForm()
    {
        // The items of one file in the mdat box, in the idat box and in several extents, with a nonzero Exif offset
        var builder = Still();
        var exif = builder.AddItem(2, "Exif", HeifFileBuilder.Concat(HeifFileBuilder.U32(3), [0xAA, 0xBB, 0xCC], "II*\0"u8.ToArray(), [8, 0, 0, 0, 0, 0]));
        exif.Storage = HeifFileBuilder.ItemStorage.ItemData;
        var xmp = builder.AddItem(3, "mime", "<x:xmpmeta/>"u8.ToArray());
        xmp.ContentType = "application/rdf+xml";
        xmp.ExtentLengths.Add(4);
        xmp.ExtentLengths.Add(8);
        builder.References.Add(("cdsc", 2, [1]));
        builder.References.Add(("cdsc", 3, [1]));
        builder.MediaDataPrefix = [1, 2, 3];
        var file = ReferenceHeif.Parse(builder.Build());
        Assert.Equal(Payload, file.ReadItemData(file.PrimaryItem!));
        Assert.Equal(1, file.FindItem(2)!.ConstructionMethod);
        Assert.Equal("II*\0"u8.ToArray().Concat(new byte[] { 8, 0, 0, 0, 0, 0 }), file.ReadExifPayload(file.FindItem(2)!));
        Assert.Equal(2, file.FindItem(3)!.Extents.Count);
        Assert.Equal("<x:xmpmeta/>", Encoding.ASCII.GetString(file.ReadItemData(file.FindItem(3)!)));
        Assert.Equal([2u, 3u], file.FindDescriptions(1).Select(item => item.Id).ToArray());

        // A round trip through the builder keeps the description
        var copy = ReferenceHeif.Parse(HeifFileBuilder.From(builder.Build()).Build());
        Assert.Equal(file.Items.Select(item => (item.Id, item.Type, item.ConstructionMethod)), copy.Items.Select(item => (item.Id, item.Type, item.ConstructionMethod)));
        Assert.Equal("<x:xmpmeta/>", Encoding.ASCII.GetString(copy.ReadItemData(copy.FindItem(3)!)));
    }
}
