using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The AVIF container rules that a header identification applies, on files assembled box by box by
/// <see cref="AvifFileBuilder"/> (no fixture file, no tool): the item model and the track model are read before any coded
/// data, so the payloads are placeholders. Expected values are written from the specifications; the independent reader
/// (<see cref="ReferenceAvif"/>) reads the same files. The oracles of the corpus (<see cref="ReferenceYuvConverter"/>,
/// the orientation and clean aperture arithmetic of <see cref="ReferenceAvif"/>) are checked here against hand-computed
/// values. Decoding is covered by <c>Conformance/AvifDecoderConformanceTests</c> and <c>Conformance/AvifContainerConformanceTests</c>.
/// </summary>
public sealed class AvifDecoderTests
{
    // A temporal delimiter and bytes that are never parsed by a header identification
    private static readonly byte[] Payload = [0x12, 0x00, 0x0A, 0x01, 0x00];

    private static AvifFileBuilder Still(uint width = 8, uint height = 6, int bitDepth = 8, bool monochrome = false, int subsampling = 0)
    {
        var builder = new AvifFileBuilder { PrimaryItemId = 1 };
        builder.AddItem(1, "av01", Payload,
            builder.AddProperty(AvifFileBuilder.ImageSize(width, height)),
            -builder.AddProperty(AvifFileBuilder.Av1Configuration(subsampling == 0 && !monochrome ? 1 : 0, 0, bitDepth, monochrome, monochrome ? 1 : subsampling, monochrome ? 1 : subsampling)));
        return builder;
    }

    private static AvifFileBuilder Sequence(int frames = 3, Action<AvifFileBuilder.Track>? configure = null)
    {
        var builder = new AvifFileBuilder { MajorBrand = "avis", MovieTimescale = 1000 };
        builder.CompatibleBrands.Clear();
        builder.CompatibleBrands.Add("avis");
        builder.CompatibleBrands.Add("msf1");
        builder.Layout.Clear();
        builder.Layout.Add(AvifFileBuilder.FileTypeBox);
        builder.Layout.Add(AvifFileBuilder.MovieBox);
        builder.Layout.Add(AvifFileBuilder.MediaDataBox);
        var track = new AvifFileBuilder.Track(1, "pict", 8, 6) { Timescale = 1000, Duration = (ulong)frames * 100 };
        track.SampleEntryBoxes.Add(AvifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
        for (var i = 0; i < frames; i++)
        {
            track.Samples.Add((Payload, 100));
        }

        configure?.Invoke(track);
        builder.Tracks.Add(track);
        return builder;
    }

    private static AvifFileBuilder.EditList Edits(bool repeating, ulong segmentDuration, int version = 1, long mediaTime = 0, short rate = 1, short rateFraction = 0)
    {
        var edits = new AvifFileBuilder.EditList { IsRepeating = repeating, Version = version };
        edits.Entries.Add((segmentDuration, mediaTime, rate, rateFraction));
        return edits;
    }

    [Theory]
    [InlineData(8, false, false, PixelFormat.Rgb24, ImageColorModel.Unknown)] // no 'colr' box: the bitstream, which a header identification does not read, gives the matrix
    [InlineData(8, false, true, PixelFormat.Rgba32, ImageColorModel.Unknown)]
    [InlineData(8, true, false, PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData(8, true, true, PixelFormat.Rgba32, ImageColorModel.GrayscaleAlpha)]
    [InlineData(10, false, false, PixelFormat.Rgba64, ImageColorModel.Unknown)]
    [InlineData(12, false, true, PixelFormat.Rgba64, ImageColorModel.Unknown)]
    [InlineData(10, true, false, PixelFormat.Gray16, ImageColorModel.Grayscale)]
    [InlineData(12, true, true, PixelFormat.Rgba64, ImageColorModel.GrayscaleAlpha)]
    public void HeaderGivesTheSizeAndThePixelFormatOfTheItemProperties(int bitDepth, bool monochrome, bool alpha, PixelFormat pixelFormat, ImageColorModel colorModel)
    {
        var builder = Still(13, 7, bitDepth, monochrome);
        if (alpha)
        {
            builder.AddItem(2, "av01", Payload,
                builder.AddProperty(AvifFileBuilder.ImageSize(13, 7)),
                -builder.AddProperty(AvifFileBuilder.Av1Configuration(0, 0, bitDepth, monochrome: true, 1, 1)),
                builder.AddProperty(AvifFileBuilder.AuxiliaryType(ReferenceAvif.AlphaAuxiliaryType)));
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

        var file = ReferenceAvif.Parse(data);
        Assert.Equal((13L, 7L), file.PrimaryItem!.Size);
        Assert.Equal(alpha, file.AlphaItem is not null);
        Assert.Equal(bitDepth, file.PrimaryItem.Configuration!.BitDepth);
    }

    [Fact]
    public void AuxiliaryImageThatIsNotAlphaIsNotRead()
    {
        // A depth map is an auxiliary image too; only the alpha type gives the image an alpha channel
        var builder = Still();
        builder.AddItem(2, "av01", Payload,
            builder.AddProperty(AvifFileBuilder.ImageSize(4, 3)),
            -builder.AddProperty(AvifFileBuilder.Av1Configuration(0, 0, 8, monochrome: true, 1, 1)),
            builder.AddProperty(AvifFileBuilder.AuxiliaryType("urn:mpeg:mpegB:cicp:systems:auxiliary:depth")));
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
            item.Associations.Add((builder.AddProperty(AvifFileBuilder.Rotation(turns)), true));

        if (mirror is { } axis)
            item.Associations.Add((builder.AddProperty(AvifFileBuilder.Mirror(axis)), true));

        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal(expected, info.Metadata.Orientation);
        Assert.Equal((8, 6), (info.Width, info.Height)); // reported, never applied
        Assert.Equal((int)expected, ReferenceAvif.Parse(data).PrimaryItem!.Orientation);
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
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Mirror(mirror)), true));
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Rotation(rotation)), true));
        var data = builder.Build();
        Assert.Equal(expected, Image.Identify(data).Metadata.Orientation);
        Assert.Equal((int)expected, ReferenceAvif.Parse(data).PrimaryItem!.Orientation);
    }

    [Fact]
    public void CleanApertureCropsTheImageAndPrecedesTheOrientation()
    {
        // An 8x6 image; aperture 5x4 whose center is 1/2 sample right of the center: left = (8 - 5) / 2 + 1/2 = 2, top = 1
        var builder = Still();
        var item = builder.GetItem(1);
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.CleanAperture(5, 1, 4, 1, 1, 2, 0, 1)), true));
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Rotation(1)), true));
        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal((5, 4), (info.Width, info.Height));
        Assert.Equal(ExifOrientation.LeftBottom, info.Metadata.Orientation);
        Assert.Equal((2, 1, 5, 4), ReferenceAvif.Parse(data).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
    }

    [Theory]
    [InlineData(5u, 2u, 4u, 1u, 0, 1u, 0, 1u)] // 5/2 samples wide
    [InlineData(5u, 1u, 4u, 1u, 0, 1u, 0, 1u)] // left = (8 - 5) / 2 = 3/2
    [InlineData(4u, 1u, 4u, 1u, 1, 3u, 0, 1u)] // left = 2 + 1/3
    [InlineData(4u, 1u, 3u, 1u, 0, 1u, 0, 1u)] // top = (6 - 3) / 2 = 3/2
    public void CleanApertureThatDoesNotSelectWholeSamplesIsNotSupported(uint widthN, uint widthD, uint heightN, uint heightD, int horizontalN, uint horizontalD, int verticalN, uint verticalD)
    {
        var builder = Still();
        builder.GetItem(1).Associations.Add((builder.AddProperty(AvifFileBuilder.CleanAperture(widthN, widthD, heightN, heightD, horizontalN, horizontalD, verticalN, verticalD)), true));
        var data = builder.Build();
        Assert.Null(ReferenceAvif.Parse(data).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
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
        builder.GetItem(1).Associations.Add((builder.AddProperty(AvifFileBuilder.CleanAperture(widthN, widthD, heightN, heightD, horizontalN, horizontalD, verticalN, verticalD)), true));
        Assert.ThrowsAny<ImageException>(() => Image.Identify(builder.Build()));
    }

    [Fact]
    public void ColorBoxesAreReadFromThePrimaryItem()
    {
        var builder = Still(bitDepth: 10, subsampling: 1);
        var icc = new byte[132];
        "RGB "u8.CopyTo(icc.AsSpan(16));
        var item = builder.GetItem(1);
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.ColorNclx(9, 16, 9, fullRange: false)), false));
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.ColorProfile(icc)), false));
        var data = builder.Build();
        var info = Image.Identify(data);
        Assert.Equal(new CicpColorDescription(9, 16, 9, fullRange: false), info.Metadata.Cicp);
        Assert.Equal("9/16/9 limited", info.Metadata.Cicp!.ToString());
        Assert.Equal(new ReferenceAvif.ColorDescription(9, 16, 9, FullRange: false), ReferenceAvif.Parse(data).PrimaryItem!.Nclx);
        Assert.Equal(132, ReferenceAvif.Parse(data).PrimaryItem!.IccProfile!.Value.Length);
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
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Box("clli", ContentLightLevelPayload)), false));
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Box("mdcv", MasteringDisplayPayload)), false));
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Box("pasp", PixelAspectRatioPayload)), false));
        var metadata = Image.Identify(builder.Build()).Metadata;
        Assert.Equal(new ContentLightLevel(1000, 400), metadata.ContentLightLevel);
        Assert.Equal(new MasteringDisplayColorVolume(redX: 35400, redY: 14600, greenX: 8500, greenY: 39850, blueX: 6550, blueY: 2300, whitePointX: 15635, whitePointY: 16450, maxLuminance: 10_000_000, minLuminance: 50), metadata.MasteringDisplay);
        Assert.Equal(new PixelAspectRatio(4, 3), metadata.PixelAspectRatio);

        // The first property of a type describes the image; a property marked essential is one the reader understands
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Box("clli", [0x00, 0x01, 0x00, 0x02])), true));
        item.Associations.Add((builder.AddProperty(AvifFileBuilder.Box("pasp", [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01])), true));
        metadata = Image.Identify(builder.Build()).Metadata;
        Assert.Equal(new ContentLightLevel(1000, 400), metadata.ContentLightLevel);
        Assert.Equal(new PixelAspectRatio(4, 3), metadata.PixelAspectRatio);

        // Square pixels are stored as they are declared, and the unknown light level (0) is a value
        var square = Still();
        square.GetItem(1).Associations.Add((square.AddProperty(AvifFileBuilder.Box("pasp", [0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x05])), false));
        square.GetItem(1).Associations.Add((square.AddProperty(AvifFileBuilder.Box("clli", [0x00, 0x00, 0x00, 0x00])), false));
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
        builder.GetItem(1).Associations.Add((builder.AddProperty(AvifFileBuilder.Box(type, payload[..length])), false));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(builder.Build()));

        var sequence = Sequence(configure: track => track.SampleEntryBoxes.Add(AvifFileBuilder.Box(type, payload[..length]))).Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(sequence));
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 0u)]
    [InlineData(0u, 0u)]
    public void PixelAspectRatioWithAZeroTermIsInvalid(uint horizontalSpacing, uint verticalSpacing)
    {
        var box = AvifFileBuilder.Box("pasp", AvifFileBuilder.U32(horizontalSpacing), AvifFileBuilder.U32(verticalSpacing));
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
            track.SampleEntryBoxes.Add(AvifFileBuilder.Box("pasp", PixelAspectRatioPayload));
            track.SampleEntryBoxes.Add(AvifFileBuilder.Box("clli", ContentLightLevelPayload));
            track.SampleEntryBoxes.Add(AvifFileBuilder.Box("mdcv", MasteringDisplayPayload));
        }).Build();
        var metadata = Image.Identify(data).Metadata;
        Assert.Equal(new ContentLightLevel(1000, 400), metadata.ContentLightLevel);
        Assert.Equal(new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 10_000_000, 50), metadata.MasteringDisplay);
        Assert.Equal(new PixelAspectRatio(4, 3), metadata.PixelAspectRatio);

        // The independent reader sees the same boxes
        var track = ReferenceAvif.Parse(data).ColorTrack!;
        Assert.Equal(["av1C", "pasp", "clli", "mdcv"], track.SampleEntryBoxes.Select(box => box.Type));
        Assert.Equal(MasteringDisplayPayload, track.FindSampleEntryBox("mdcv")!.Payload.ToArray());
    }

    [Theory]
    [InlineData("avif", new[] { "avif", "mif1", "miaf" }, true)]
    [InlineData("mif1", new[] { "mif1", "avif" }, true)] // a compatible brand is enough
    [InlineData("miaf", new[] { "avif" }, true)]
    [InlineData("MA1B", new[] { "avif", "MA1B" }, true)]
    [InlineData("mif1", new[] { "mif1", "heic" }, false)] // HEIF, not AVIF
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
        Assert.Equal(isAvif, ReferenceAvif.Parse(data).HasBrand("avif"));
        if (isAvif)
        {
            Assert.Equal(ImageFormat.Avif, Image.Identify(data).Format);
        }
        else
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.Identify(data));
        }
    }

    [Fact]
    public void FileWithTheSequenceBrandPresentsItsTrackAndAnyOtherFileItsPrimaryItem()
    {
        // The same boxes with and without the avis brand: a 13x7 primary item and a track of three 8x6 images
        var builder = Sequence();
        builder.Layout.Insert(1, AvifFileBuilder.MetaBox);
        builder.PrimaryItemId = 1;
        builder.AddItem(1, "av01", Payload, builder.AddProperty(AvifFileBuilder.ImageSize(13, 7)), -builder.AddProperty(AvifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0)));
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
        builder.ExtraBoxes["free"] = AvifFileBuilder.Box("free", new byte[3]);
        builder.ExtraBoxes["large"] = AvifFileBuilder.LargeBox("skip", new byte[7]);
        builder.ExtraBoxes["uuid"] = AvifFileBuilder.Box("uuid", new byte[16], [1, 2, 3]);
        builder.ExtraBoxes["unknown"] = AvifFileBuilder.Box("abcd", [9]);
        builder.Layout.Clear();
        foreach (var box in new[] { AvifFileBuilder.FileTypeBox, "free", "large", "uuid", "unknown", AvifFileBuilder.MetaBox, "free", AvifFileBuilder.MediaDataBox })
        {
            builder.Layout.Add(box);
        }

        builder.LargeSizeBoxes.Add(AvifFileBuilder.MetaBox);
        builder.MediaDataExtendsToEnd = true;
        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
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
        twice.Layout.Insert(1, AvifFileBuilder.FileTypeBox);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(twice.Build()));

        // A file type box that is not a list of four-character codes
        var odd = Still();
        odd.ExtraBoxes["odd"] = AvifFileBuilder.Box("ftyp", AvifFileBuilder.Ascii("avif"), new byte[4], AvifFileBuilder.Ascii("avi"));
        odd.Layout[0] = "odd";
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(odd.Build()));

        // A 64-bit size smaller than the 16 bytes of its own header
        var small = Still();
        small.ExtraBoxes["bad"] = AvifFileBuilder.Concat(AvifFileBuilder.U32(1), AvifFileBuilder.Ascii("free"), AvifFileBuilder.U64(12));
        small.Layout.Insert(1, "bad");
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(small.Build()));

        // No meta box at all: the file ends before any image is described
        var empty = Still();
        empty.Layout.Remove(AvifFileBuilder.MetaBox);
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
        AvifFileBuilder Grid(Action<AvifFileBuilder>? change = null)
        {
            var builder = new AvifFileBuilder { PrimaryItemId = 1 };
            var size = builder.AddProperty(AvifFileBuilder.ImageSize(8, 6));
            var configuration = builder.AddProperty(AvifFileBuilder.Av1Configuration(1, 0, 8, monochrome: false, 0, 0));
            builder.AddItem(1, "grid", AvifFileBuilder.GridDescriptor(1, 2, 16, 6), builder.AddProperty(AvifFileBuilder.ImageSize(16, 6)));
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

        // A tile that is not a coded image, and a tile with a transformation of its own
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Grid(builder => builder.GetItem(3).Type = "grid").Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Grid(builder => builder.GetItem(3).Associations.Add((builder.AddProperty(AvifFileBuilder.Rotation(1)), true))).Build()));

        // Tiles of different bit depths
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Grid(builder =>
        {
            var tile = builder.GetItem(3);
            tile.Associations[1] = (builder.AddProperty(AvifFileBuilder.Av1Configuration(1, 0, 10, monochrome: false, 0, 0)), true);
        }).Build()));
    }

    [Fact]
    public void HeaderOfAnImageSequenceGivesItsSizeAndItsFrameCount()
    {
        var data = Sequence(frames: 4, track => track.SampleEntryBoxes.Add(AvifFileBuilder.ColorNclx(1, 13, 6, fullRange: true))).Build();
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Avif, info.Format);
        Assert.Equal((8, 6), (info.Width, info.Height));
        Assert.Equal(4, info.FrameCount);
        Assert.True(info.IsAnimated);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(new CicpColorDescription(1, 13, 6, fullRange: true), info.Metadata.Cicp);
        Assert.Equal(ExifOrientation.TopLeft, info.Metadata.Orientation);

        var track = ReferenceAvif.Parse(data).ColorTrack!;
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
        var track = ReferenceAvif.Parse(data).ColorTrack!;
        Assert.Equal(expectedPlays, track.TotalPlays);
        Assert.Equal(expectedPlays, EditListExpectation.GetTotalPlays(track.EditList is null ? null : new EditListExpectation
        {
            Repeating = track.EditList.IsRepeating,
            SegmentDuration = track.EditList.Entries[0].SegmentDuration is { } segment ? (long)segment : null,
            TrackDuration = track.Duration is { } duration ? (long)duration : null,
        }));
    }

    [Fact]
    public void EditListsThatDoNotPlayTheWholeTrackAreNotSupported()
    {
        // Two edits; an edit that starts after the first sample; an empty edit; a rate other than 1
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track =>
        {
            track.EditList = Edits(repeating: false, 150);
            track.EditList.Entries.Add((150, 0, 1, 0));
        }).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, mediaTime: 100)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, version: 0, mediaTime: -1)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, rate: 2)).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track => track.EditList = Edits(repeating: false, 300, rate: 0, rateFraction: 0x4000)).Build()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditThatEndsBeforeTheLastImageDoesNotPlayTheWholeTrack(bool repeating)
    {
        // Three images of 100 units; the edit presents the first 100 units of the media, that is the first image only
        // (ISO/IEC 14496-12, section 8.6.6: segment_duration is the duration of the edit). The reader has no way to present
        // one image out of three as "the sequence": such an edit list is one of those that are not supported
        var data = Sequence(frames: 3, track =>
        {
            track.Duration = 300;
            track.EditList = Edits(repeating, segmentDuration: 100);
        }).Build();
        var track = ReferenceAvif.Parse(data).ColorTrack!;
        Assert.Equal(300L, track.Samples.Sum(sample => (long)sample.Duration));
        Assert.Equal(100UL, track.EditList!.Entries[0].SegmentDuration);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
    }

    [Fact]
    public void AlphaTrackIsTheAuxiliaryTrackOfTheColorTrack()
    {
        AvifFileBuilder WithAlpha(Action<AvifFileBuilder.Track, AvifFileBuilder.Track>? change = null)
        {
            var builder = Sequence();
            var alpha = new AvifFileBuilder.Track(2, "auxv", 8, 6) { Timescale = 1000, Duration = 300 };
            alpha.SampleEntryBoxes.Add(AvifFileBuilder.Av1Configuration(0, 0, 8, monochrome: true, 1, 1));
            alpha.SampleEntryBoxes.Add(AvifFileBuilder.FullBox("auxi", 0, 0, Encoding.ASCII.GetBytes(ReferenceAvif.AlphaAuxiliaryType), [0]));
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
        Assert.Equal(2u, ReferenceAvif.Parse(data).AlphaTrack!.Id);

        // An auxiliary track of another kind is not an alpha plane
        var depth = WithAlpha((_, alpha) => alpha.SampleEntryBoxes[1] = AvifFileBuilder.FullBox("auxi", 0, 0, "urn:mpeg:mpegB:cicp:systems:auxiliary:depth"u8.ToArray(), [0])).Build();
        Assert.Equal(PixelFormat.Rgb24, Image.Identify(depth).PixelFormat);
        Assert.Null(ReferenceAvif.Parse(depth).AlphaTrack);

        // Premultiplied alpha, an alpha plane of another size: not supported; another number of images: invalid
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((color, _) => color.References.Add(("prem", [2]))).Build()));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(WithAlpha((_, alpha) => alpha.Width = 4).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(WithAlpha((_, alpha) => alpha.Samples.RemoveAt(2)).Build()));
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
        bare.Layout.Remove(AvifFileBuilder.MovieBox);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(bare.Build()));

        // A movie fragment is not supported
        var fragmented = Sequence();
        fragmented.ExtraBoxes["moof"] = AvifFileBuilder.Box("moof");
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

    [Fact]
    public void ReferenceConverterRejectsWhatTheLibraryRejects()
    {
        Assert.False(ReferenceYuvConverter.IsSupportedMatrix(3));
        Assert.False(ReferenceYuvConverter.IsSupportedMatrix(10));
        Assert.False(ReferenceYuvConverter.IsSupportedMatrix(14));
        Assert.Throws<NotSupportedException>(() => ReferenceYuvConverter.ConvertToRgb([0], [0], [0], 1, 1, 8, 0, 0, 0, 14, fullRange: true, 255));
        Assert.Throws<ArgumentException>(() => ReferenceYuvConverter.ConvertToRgb([0, 0], [0], [0], 2, 1, 8, 1, 0, 0, 0, fullRange: true, 255));
    }

    [Fact]
    public void BuilderAndIndependentReaderAgreeOnEveryStorageForm()
    {
        // The items of one file in the mdat box, in the idat box and in several extents, with a nonzero Exif offset
        var builder = Still();
        var exif = builder.AddItem(2, "Exif", AvifFileBuilder.Concat(AvifFileBuilder.U32(3), [0xAA, 0xBB, 0xCC], "II*\0"u8.ToArray(), [8, 0, 0, 0, 0, 0]));
        exif.Storage = AvifFileBuilder.ItemStorage.ItemData;
        var xmp = builder.AddItem(3, "mime", "<x:xmpmeta/>"u8.ToArray());
        xmp.ContentType = "application/rdf+xml";
        xmp.ExtentLengths.Add(4);
        xmp.ExtentLengths.Add(8);
        builder.References.Add(("cdsc", 2, [1]));
        builder.References.Add(("cdsc", 3, [1]));
        builder.MediaDataPrefix = [1, 2, 3];
        var file = ReferenceAvif.Parse(builder.Build());
        Assert.Equal(Payload, file.ReadItemData(file.PrimaryItem!));
        Assert.Equal(1, file.FindItem(2)!.ConstructionMethod);
        Assert.Equal("II*\0"u8.ToArray().Concat(new byte[] { 8, 0, 0, 0, 0, 0 }), file.ReadExifPayload(file.FindItem(2)!));
        Assert.Equal(2, file.FindItem(3)!.Extents.Count);
        Assert.Equal("<x:xmpmeta/>", Encoding.ASCII.GetString(file.ReadItemData(file.FindItem(3)!)));
        Assert.Equal([2u, 3u], file.FindDescriptions(1).Select(item => item.Id).ToArray());

        // A round trip through the builder keeps the description
        var copy = ReferenceAvif.Parse(AvifFileBuilder.From(builder.Build()).Build());
        Assert.Equal(file.Items.Select(item => (item.Id, item.Type, item.ConstructionMethod)), copy.Items.Select(item => (item.Id, item.Type, item.ConstructionMethod)));
        Assert.Equal("<x:xmpmeta/>", Encoding.ASCII.GetString(copy.ReadItemData(copy.FindItem(3)!)));
    }
}
