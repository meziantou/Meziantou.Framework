using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Heif;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The HEIC container rules that a header identification applies, on files assembled box by box by
/// <see cref="HeifFileBuilder"/> (no fixture file, no tool): the item model and the track model are read before any coded
/// data, so the payloads are placeholders and the configuration records hold no parameter set. Expected values are written
/// from the specifications (ISO/IEC 23008-12 and its annex B, ISO/IEC 14496-15 for the <c>hvcC</c> record); the independent
/// reader (<see cref="ReferenceHeif"/>) reads the same files. The rules that HEIC shares with AVIF are tested once, in
/// <c>AvifDecoderTests</c>: these tests cover what the codec changes, and that the errors name the right format.
/// </summary>
public sealed class HeicDecoderTests
{
    // A NAL unit of two bytes preceded by its length: never parsed by a header identification
    private static readonly byte[] Payload = [0x00, 0x00, 0x00, 0x02, 0x26, 0x01];

    private static HeifFileBuilder Still(uint width = 8, uint height = 6, int bitDepth = 8, int chromaFormat = 1, byte[]? configuration = null)
    {
        var builder = new HeifFileBuilder { PrimaryItemId = 1, MajorBrand = "heic" };
        SetBrands(builder, "mif1", "heic");
        builder.AddItem(1, "hvc1", Payload,
            builder.AddProperty(HeifFileBuilder.ImageSize(width, height)),
            -builder.AddProperty(configuration ?? HeifFileBuilder.HevcConfiguration(chromaFormat, bitDepth, bitDepth, [])));
        return builder;
    }

    private static HeifFileBuilder Sequence(int frames = 3, string sampleEntryType = "hvc1", Action<HeifFileBuilder.Track>? configure = null)
    {
        var builder = new HeifFileBuilder { MajorBrand = "hevc", MovieTimescale = 1000 };
        SetBrands(builder, "hevc", "msf1");
        builder.Layout.Clear();
        builder.Layout.Add(HeifFileBuilder.FileTypeBox);
        builder.Layout.Add(HeifFileBuilder.MovieBox);
        builder.Layout.Add(HeifFileBuilder.MediaDataBox);
        var track = new HeifFileBuilder.Track(1, "pict", 8, 6) { Timescale = 1000, Duration = (ulong)frames * 100, SampleEntryType = sampleEntryType };
        track.SampleEntryBoxes.Add(HeifFileBuilder.HevcConfiguration(1, 8, 8, []));
        for (var i = 0; i < frames; i++)
        {
            track.Samples.Add((Payload, 100));
        }

        configure?.Invoke(track);
        builder.Tracks.Add(track);
        return builder;
    }

    private static void SetBrands(HeifFileBuilder builder, params string[] compatibleBrands)
    {
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatibleBrands)
        {
            builder.CompatibleBrands.Add(brand);
        }
    }

    [Theory]
    [InlineData(8, 1, null, PixelFormat.Rgb24, ImageColorModel.Unknown)] // no 'colr' box and no parameter set in the record: nothing gives the matrix
    [InlineData(8, 2, null, PixelFormat.Rgb24, ImageColorModel.Unknown)]
    [InlineData(8, 3, ReferenceHeif.HevcAlphaAuxiliaryType, PixelFormat.Rgba32, ImageColorModel.Unknown)]
    [InlineData(8, 0, null, PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData(8, 0, ReferenceHeif.AlphaAuxiliaryType, PixelFormat.La16, ImageColorModel.GrayscaleAlpha)]
    [InlineData(10, 1, null, PixelFormat.Rgb48, ImageColorModel.Unknown)]
    [InlineData(12, 3, ReferenceHeif.HevcAlphaAuxiliaryType, PixelFormat.Rgba64, ImageColorModel.Unknown)]
    [InlineData(10, 0, null, PixelFormat.Gray16, ImageColorModel.Grayscale)]
    [InlineData(12, 0, ReferenceHeif.AlphaAuxiliaryType, PixelFormat.La32, ImageColorModel.GrayscaleAlpha)]
    public void HeaderGivesTheSizeAndThePixelFormatOfTheItemProperties(int bitDepth, int chromaFormat, string? alphaType, PixelFormat pixelFormat, ImageColorModel colorModel)
    {
        var builder = Still(13, 7, bitDepth, chromaFormat);
        if (alphaType is not null)
        {
            // An alpha plane is a monochrome image, or a 4:2:0 image whose chroma is not used
            builder.AddItem(2, "hvc1", Payload,
                builder.AddProperty(HeifFileBuilder.ImageSize(13, 7)),
                -builder.AddProperty(HeifFileBuilder.HevcConfiguration(chromaFormat is 0 ? 1 : 0, bitDepth, bitDepth, [])),
                builder.AddProperty(HeifFileBuilder.AuxiliaryType(alphaType)));
            builder.References.Add(("auxl", 2, [1]));
        }

        var data = builder.Build();
        Assert.Equal(ImageFormat.Heic, Image.DetectFormat(data));
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Heic, info.Format);
        Assert.Equal(ImageFormat.Heic, info.Metadata.SourceFormat);
        Assert.Equal((13, 7), (info.Width, info.Height));
        Assert.Equal(pixelFormat, info.PixelFormat);
        Assert.Equal(colorModel, info.ColorModel);
        Assert.Equal(bitDepth, info.BitsPerComponent);
        Assert.Equal(alphaType is not null, info.MayHaveTransparency);
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.Null(info.Animation);
        Assert.Null(info.Metadata.Cicp);

        var file = ReferenceHeif.Parse(data);
        Assert.Equal((13L, 7L), file.PrimaryItem!.Size);
        Assert.Equal(alphaType is not null, file.AlphaItem is not null);
        var configuration = file.PrimaryItem.HevcConfiguration!;
        Assert.Equal((bitDepth, bitDepth, chromaFormat, 4), (configuration.BitDepthLuma, configuration.BitDepthChroma, configuration.ChromaFormat, configuration.NalUnitLengthSize));
        Assert.Empty(configuration.Arrays);
    }

    [Theory]
    [InlineData(1, 6, true, ImageColorModel.YCbCr)]
    [InlineData(9, 9, false, ImageColorModel.YCbCr)]
    [InlineData(1, 0, true, ImageColorModel.Rgb)] // the identity matrix: the planes are G, B and R
    public void TheColorInformationPropertyDescribesTheColors(int primaries, int matrix, bool fullRange, ImageColorModel colorModel)
    {
        var builder = Still(chromaFormat: 3);
        builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.ColorNclx(primaries, 13, matrix, fullRange)), false));
        var info = Image.Identify(builder.Build());
        Assert.Equal(colorModel, info.ColorModel);
        Assert.Equal(new CicpColorDescription((byte)primaries, 13, (byte)matrix, fullRange), info.Metadata.Cicp);
    }

    [Theory]
    [InlineData("heic", new[] { "mif1", "heic" })]
    [InlineData("heix", new[] { "mif1", "heix" })]
    [InlineData("heim", new[] { "heim" })]
    [InlineData("heis", new[] { "heis" })]
    [InlineData("mif1", new[] { "mif1", "heic" })] // a compatible brand is enough
    [InlineData("mif1", new[] { "miaf", "heix" })]
    [InlineData("miaf", new[] { "heic" })]
    [InlineData("heic", new string[0])]
    public void BrandsOfTheFileTypeBoxSelectHeic(string majorBrand, string[] compatibleBrands)
    {
        var builder = Still();
        builder.MajorBrand = majorBrand;
        SetBrands(builder, compatibleBrands);
        var data = builder.Build();
        Assert.Equal(ImageFormat.Heic, Image.DetectFormat(data));
        Assert.Equal(ImageFormat.Heic, Image.Identify(data).Format);
    }

    [Theory]
    [InlineData("mif1", new[] { "heic", "avif" })]
    [InlineData("avif", new[] { "avif", "heic" })]
    [InlineData("heic", new[] { "heic", "avif" })]
    public void TheCodecOfTheImageSelectsTheFormatOfAFileOfBothBrands(string majorBrand, string[] compatibleBrands)
    {
        var builder = Still();
        builder.MajorBrand = majorBrand;
        SetBrands(builder, compatibleBrands);
        var info = Image.Identify(builder.Build());
        Assert.Equal(ImageFormat.Heic, info.Format);
        Assert.Equal(ImageFormat.Heic, info.Metadata.SourceFormat);
    }

    [Theory]
    [InlineData("avif", new[] { "avif", "mif1", "miaf" })]
    [InlineData("mif1", new[] { "mif1", "avif" })]
    public void AnHevcImageInAFileOfAvifBrandsIsNotRead(string majorBrand, string[] compatibleBrands)
    {
        var builder = Still();
        builder.MajorBrand = majorBrand;
        SetBrands(builder, compatibleBrands);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Avif, exception.Format);
        Assert.Equal("image codec", exception.Feature);
    }

    [Theory]
    [InlineData("av01")]
    [InlineData("avc1")]
    [InlineData("hev1")] // the type of a sample entry, not of an item
    [InlineData("lhv1")] // a layer of a layered HEVC bitstream
    [InlineData("jpeg")]
    public void AnImageOfAnotherCodecIsNotRead(string itemType)
    {
        var builder = Still();
        builder.GetItem(1).Type = itemType;
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.Equal("image codec", exception.Feature);
        Assert.Contains("not as HEVC", exception.Message);
    }

    [Fact]
    public void AGridOfHevcTilesIsDescribedByItsProperties()
    {
        // The layout of the files that phones write: tiles that share their configuration record and their size
        var builder = new HeifFileBuilder { PrimaryItemId = 10, MajorBrand = "heic" };
        SetBrands(builder, "mif1", "heic");
        var tileSize = builder.AddProperty(HeifFileBuilder.ImageSize(8, 8));
        var configuration = -builder.AddProperty(HeifFileBuilder.HevcConfiguration(1, 8, 8, []));
        builder.AddItem(1, "hvc1", Payload, tileSize, configuration);
        builder.AddItem(2, "hvc1", Payload, tileSize, configuration);
        builder.AddItem(10, "grid", HeifFileBuilder.GridDescriptor(1, 2, 13, 7), builder.AddProperty(HeifFileBuilder.ImageSize(13, 7)));
        builder.References.Add(("dimg", 10, [1, 2]));

        var info = Image.Identify(builder.Build());
        Assert.Equal(ImageFormat.Heic, info.Format);
        Assert.Equal((13, 7), (info.Width, info.Height));
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);

        // A tile of another codec
        builder.GetItem(2).Type = "av01";
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.Equal("grid of derived images", exception.Feature);

        // A tile that is not an item of the file
        builder.Items.Remove(builder.GetItem(2));
        var invalid = Assert.Throws<InvalidImageContentException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Heic, invalid.Format);
    }

    [Fact]
    public void AnImageWithoutConfigurationRecordIsInvalid()
    {
        var builder = new HeifFileBuilder { PrimaryItemId = 1, MajorBrand = "heic" };
        SetBrands(builder, "mif1", "heic");
        builder.AddItem(1, "hvc1", Payload, builder.AddProperty(HeifFileBuilder.ImageSize(8, 6)));
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.Equal("The HEIC file is invalid: an HEVC image has no configuration record.", exception.Message);
    }

    [Fact]
    public void TheConfigurationRecordIsValidated()
    {
        static byte[] Record(Action<byte[]>? patch = null, int length = int.MaxValue)
        {
            // The payload of an 'hvcC' box with one array of one NAL unit of 3 bytes
            var box = HeifFileBuilder.HevcConfiguration(1, 8, 8, [(33, [[0x42, 0x01, 0x01]])]);
            var payload = box[8..];
            patch?.Invoke(payload);
            return HeifFileBuilder.Box("hvcC", payload[..Math.Min(length, payload.Length)]);
        }

        Assert.Equal(ImageFormat.Heic, Image.Identify(Still(configuration: Record()).Build()).Format);

        // configurationVersion
        var version = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Still(configuration: Record(payload => payload[0] = 2)).Build()));
        Assert.Equal((ImageFormat.Heic, "configuration record version"), (version.Format, version.Feature));

        // lengthSizeMinusOne is 0, 1 or 3
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Still(configuration: Record(payload => payload[21] = 0xFE)).Build()));
        Assert.Equal(ImageFormat.Heic, Image.Identify(Still(configuration: Record(payload => payload[21] = 0xFC)).Build()).Format);
        Assert.Equal(ImageFormat.Heic, Image.Identify(Still(configuration: Record(payload => payload[21] = 0xFD)).Build()).Format);

        // The record is cut: in its fixed part, in the header of an array, in a NAL unit
        foreach (var length in new[] { 0, 1, 16, 22, 24, 26, 28, 30 })
        {
            var truncated = Assert.Throws<InvalidImageContentException>(() => Image.Identify(Still(configuration: Record(length: length)).Build()));
            Assert.Equal(ImageFormat.Heic, truncated.Format);
        }

        // A NAL unit has a header of two bytes
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Still(configuration: HeifFileBuilder.HevcConfiguration(1, 8, 8, [(33, [[0x42]])])).Build()));

        // Bit depths: 8 to 12, the same for luma and chroma unless the image is monochrome
        var deep = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Still(configuration: HeifFileBuilder.HevcConfiguration(1, 13, 13, [])).Build()));
        Assert.Equal((ImageFormat.Heic, "bit depth"), (deep.Format, deep.Feature));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Still(configuration: HeifFileBuilder.HevcConfiguration(1, 10, 8, [])).Build()));
        Assert.Equal(10, Image.Identify(Still(configuration: HeifFileBuilder.HevcConfiguration(0, 10, 8, [])).Build()).BitsPerComponent);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0xFFFF, true)] // every layer may be shown while the data arrive
    [InlineData(1, false)]
    public void OnlyTheBaseLayerOfALayeredImageIsRead(int layer, bool isSupported)
    {
        var builder = Still();
        builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.LayerSelector((ushort)layer)), true));
        if (isSupported)
        {
            Assert.Equal(ImageFormat.Heic, Image.Identify(builder.Build()).Format);
        }
        else
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
            Assert.Equal((ImageFormat.Heic, "layered image"), (exception.Format, exception.Feature));
        }
    }

    [Fact]
    public void ContainerErrorsNameTheFormat()
    {
        var withoutPrimaryItem = Still();
        withoutPrimaryItem.PrimaryItemId = null;
        var invalid = Assert.Throws<InvalidImageContentException>(() => Image.Identify(withoutPrimaryItem.Build()));
        Assert.Equal(ImageFormat.Heic, invalid.Format);
        Assert.StartsWith("The HEIC file is invalid: ", invalid.Message, StringComparison.Ordinal);

        // A property that the image requires and that the library does not know
        var essential = Still();
        essential.GetItem(1).Associations.Add((essential.AddProperty(HeifFileBuilder.Box("zzzz", [0])), true));
        var unsupported = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(essential.Build()));
        Assert.Equal((ImageFormat.Heic, "essential item property"), (unsupported.Format, unsupported.Feature));
        Assert.Equal("The HEIC image requires the property 'zzzz'.", unsupported.Message);

        // The same file with a generic major brand: the compatible brands give the format before anything else is read
        var generic = Still();
        generic.PrimaryItemId = null;
        generic.MajorBrand = "mif1";
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Identify(generic.Build())).Format);
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("mif1")]
    public void ATruncatedFileIsInvalidHeicData(string majorBrand)
    {
        var builder = Still();
        builder.MajorBrand = majorBrand;
        var data = builder.Build();
        var file = ReferenceHeif.Parse(data);
        var meta = file.TopLevelBoxes.First(box => box.Type == "meta");

        // Cut in the 'meta' box, after the file type box
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data.AsSpan(0, (int)meta.Offset + 20)));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.StartsWith("The HEIC data is truncated", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hvc1")]
    [InlineData("hev1")] // the parameter sets may also be in the samples
    public void HeaderDescribesAnImageSequence(string sampleEntryType)
    {
        var data = Sequence(frames: 4, sampleEntryType).Build();
        Assert.Equal(ImageFormat.Heic, Image.DetectFormat(data));
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Heic, info.Format);
        Assert.Equal((8, 6), (info.Width, info.Height));
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(4, info.FrameCount);
        Assert.True(info.IsAnimated);

        var file = ReferenceHeif.Parse(data);
        Assert.True(file.IsSequence);
        Assert.Equal(sampleEntryType, file.ColorTrack!.SampleEntryType);
        Assert.Equal(8, file.ColorTrack.HevcConfiguration!.BitDepthLuma);
    }

    [Theory]
    [InlineData("hevc", new[] { "hevc", "msf1" })]
    [InlineData("hevx", new[] { "hevx" })]
    [InlineData("msf1", new[] { "msf1", "hevc" })]
    [InlineData("msf1", new[] { "hevx", "avis" })] // both: the codec of the track decides
    [InlineData("avis", new[] { "avis", "hevc" })]
    public void BrandsOfTheFileTypeBoxSelectAnHeicImageSequence(string majorBrand, string[] compatibleBrands)
    {
        var builder = Sequence();
        builder.MajorBrand = majorBrand;
        SetBrands(builder, compatibleBrands);
        var info = Image.Identify(builder.Build());
        Assert.Equal(ImageFormat.Heic, info.Format);
        Assert.Equal(ImageFormat.Heic, info.Metadata.SourceFormat);
        Assert.Equal(3, info.FrameCount);
    }

    [Fact]
    public void ATrackOfAnImageSequenceIsValidated()
    {
        // No track of a codec that the library reads
        var other = Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryType = "avc1").Build()));
        Assert.Equal(ImageFormat.Heic, other.Format);
        Assert.Equal("The HEIC file is invalid: the image sequence has no track of HEVC images.", other.Message);

        // A track without configuration record
        var bare = Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(configure: track => track.SampleEntryBoxes.Clear()).Build()));
        Assert.Equal(ImageFormat.Heic, bare.Format);
        Assert.Equal("The HEIC file is invalid: an HEVC track has no configuration record.", bare.Message);

        // The record of a track is validated as the one of an item
        var record = HeifFileBuilder.HevcConfiguration(1, 8, 8, [], version: 0);
        var version = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Sequence(configure: track =>
        {
            track.SampleEntryBoxes.Clear();
            track.SampleEntryBoxes.Add(record);
        }).Build()));
        Assert.Equal((ImageFormat.Heic, "configuration record version"), (version.Format, version.Feature));
    }

    [Fact]
    public void TheCompositionOffsetsOfATrackAreValidated()
    {
        static byte[] Build(int[] offsets, int version = 0, long mediaTime = 0)
        {
            return Sequence(frames: offsets.Length, configure: track =>
            {
                track.CompositionOffsets = offsets;
                track.CompositionOffsetsVersion = version;
                if (mediaTime is not 0)
                {
                    track.EditList = new HeifFileBuilder.EditList { IsRepeating = false };
                    track.EditList.Entries.Add(((ulong)offsets.Length * 100, mediaTime, 1, 0));
                }
            }).Build();
        }

        var fullScan = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };

        // The samples I, P, B, B of a group of pictures are presented as I, B, B, P: the reference reader gives this order
        // and the duration of each image
        var reordered = Build([100, 300, 0, 0]);
        Assert.Equal([(0, 100L), (2, 100L), (3, 100L), (1, 100L)], ReferenceHeif.Parse(reordered).ColorTrack!.PresentationOrder);
        Assert.Equal(4, Image.Identify(reordered).FrameCount);

        // The edit of such a track starts at the presentation time of its first image
        Assert.Equal(4, Image.Identify(Build([100, 300, 0, 0], mediaTime: 100)).FrameCount);
        var edit = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Build([100, 300, 0, 0], mediaTime: 50), fullScan));
        Assert.Equal((ImageFormat.Heic, "edit list"), (edit.Format, edit.Feature));

        // Signed offsets
        Assert.Equal([(0, 100L), (2, 100L), (1, 100L)], ReferenceHeif.Parse(Build([0, 100, -100], version: 1)).ColorTrack!.PresentationOrder);

        // Two images at the same time, an image before the start of the track, a box that does not cover every sample
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Identify(Build([100, 0, 0], version: 1), fullScan)).Format);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Build([-100, 0, 0], version: 1), fullScan));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Sequence(frames: 3, configure: track => track.CompositionOffsets = [0, 0]).Build(), fullScan));

        var version = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Build([0, 0, 0], version: 2), fullScan));
        Assert.Equal((ImageFormat.Heic, "ctts box version"), (version.Format, version.Feature));
    }
}
