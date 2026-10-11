using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Container rules that are cheaper to express in code than as committed files: the AV1 payloads of corpus fixtures are
/// placed in other containers by <see cref="AvifFileBuilder"/> (no tool, no new file) and decoded by the library.
/// <list type="bullet">
/// <item><description>
/// A container around the same payloads must give the frames, the durations and the metadata of the fixture: the
/// expectations are the committed references of the fixture.
/// </description></item>
/// <item><description>
/// The matrix coefficients and the range of a <c>colr</c> box written over the payload must give the conversion of
/// <see cref="ReferenceYuvConverter"/> (exact rationals) within one unit. Its input planes are decoded by the AV1 decoder of
/// the library, which the AV1 vectors verify against two independent decoders, and the test first checks that these planes
/// reproduce the committed references of the unmodified fixtures: the conversion is what is under test here.
/// </description></item>
/// </list>
/// </summary>
public sealed class AvifContainerConformanceTests
{
    private static readonly string[] MatrixSources =
    [
        "avif/lossy-444-bt601-full", "avif/lossy-422-bt709-limited", "avif/lossy-420-bt601-odd", "avif/lossy-420-chroma-vertical",
        "avif/lossy-420-chroma-colocated", "avif/lossy-420-bt2020-limited-10bit", "avif/lossy-422-bt709-full-12bit",
    ];

    public static TheoryData<string> MatrixSourceFixtures => [.. MatrixSources];

    public static TheoryData<string, int, bool> MatrixCases
    {
        get
        {
            var data = new TheoryData<string, int, bool>();
            foreach (var source in MatrixSources)
            {
                // BT.709, unspecified, FCC, BT.470BG, BT.601, SMPTE 240M, YCgCo, BT.2020 non-constant luminance
                foreach (var matrix in new[] { 1, 2, 4, 5, 6, 7, 8, 9 })
                {
                    data.Add(source, matrix, true);
                    data.Add(source, matrix, false);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MatrixSourceFixtures))]
    public void PlanesOfTheLibraryReproduceTheCommittedReference(string id)
    {
        // The committed reference comes from dav1d and libaom planes; the same conversion of the planes the library decodes
        // must give the same bytes
        var fixture = GoldenCorpus.Default.Get(id);
        var cicp = fixture.Expected.Cicp!;
        var expected = fixture.GetFrame(0, fixture.CanonicalLayout);
        var converted = ConvertPrimaryItem(fixture.ReadInput(), cicp.MatrixCoefficients, cicp.FullRange, expected.Layout.MaxSampleValue);
        for (var i = 0; i < expected.Width * expected.Height; i++)
        {
            var (x, y) = (i % expected.Width, i / expected.Width);
            Assert.Equal((converted[3 * i], converted[(3 * i) + 1], converted[(3 * i) + 2]), (expected.GetSample(x, y, 0), expected.GetSample(x, y, 1), expected.GetSample(x, y, 2)));
        }
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void ColorBoxConvertsWithItsMatrixAndItsRange(string id, int matrix, bool fullRange)
    {
        var builder = AvifFileBuilder.From(GoldenCorpus.Default.Get(id).ReadInput());
        ReplaceProperty(builder, builder.GetItem(1), "colr", AvifFileBuilder.ColorNclx(1, 13, matrix, fullRange));
        var data = builder.Build();

        using var image = Image.Load(data);
        Assert.Equal(new CicpColorDescription(1, 13, (byte)matrix, fullRange), image.Metadata.Cicp);
        var actual = ImageSnapshots.Capture(image).Frames[0].Pixels;
        var maximum = actual.Layout.MaxSampleValue;
        var expected = ConvertPrimaryItem(data, matrix, fullRange, maximum);
        var unit = actual.Layout.ScaleFrom8Bit;
        var worst = 0;
        for (var i = 0; i < actual.Width * actual.Height; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                worst = Math.Max(worst, Math.Abs(expected[(3 * i) + channel] - actual.GetSample(i % actual.Width, i / actual.Width, channel)));
            }
        }

        Assert.True(worst <= unit, string.Create(CultureInfo.InvariantCulture, $"{id} with matrix coefficients {matrix}, {(fullRange ? "full" : "limited")} range: a sample is {worst} away from the exact conversion (one unit is {unit})."));
    }

    [Fact]
    public void RangeOfTheColorBoxAppliesToUnmatrixedSamples()
    {
        // Monochrome and identity-matrix samples have no matrix but they have a range: a colr box that declares the limited
        // range scales 16..235 to the full range of the output (ITU-T H.273, section 8.3, equations 26 to 31), clipped
        foreach (var id in new[] { "avif/lossless-gray8", "avif/lossless-rgb8" })
        {
            var fixture = GoldenCorpus.Default.Get(id);
            var full = fixture.GetFrame(0, RawPixelLayout.Rgba8);
            var builder = AvifFileBuilder.From(fixture.ReadInput());
            var cicp = fixture.Expected.Cicp!;
            ReplaceProperty(builder, builder.GetItem(1), "colr", AvifFileBuilder.ColorNclx(cicp.ColorPrimaries, cicp.TransferCharacteristics, cicp.MatrixCoefficients, fullRange: false));
            using var image = Image.Load<Rgba32>(builder.Build());
            Assert.Equal(new CicpColorDescription((byte)cicp.ColorPrimaries, (byte)cicp.TransferCharacteristics, (byte)cicp.MatrixCoefficients, fullRange: false), image.Metadata.Cicp);
            for (var y = 0; y < full.Height; y++)
            {
                for (var x = 0; x < full.Width; x++)
                {
                    // The full-range reference holds the decoded samples themselves (8 bits, no matrix)
                    var pixel = image.Frames[0][x, y];
                    var expected = (
                        ReferenceYuvConverter.ScaleSample(full.GetSample(x, y, 0), 8, fullRange: false, 255),
                        ReferenceYuvConverter.ScaleSample(full.GetSample(x, y, 1), 8, fullRange: false, 255),
                        ReferenceYuvConverter.ScaleSample(full.GetSample(x, y, 2), 8, fullRange: false, 255));
                    Assert.Equal(expected, ((int)pixel.R, (int)pixel.G, (int)pixel.B));
                }
            }
        }
    }

    [Fact]
    public void BitstreamDescribesTheColorsOfAnIdentityImageWithoutColorBox()
    {
        // avif/lossless-rgb8 without its colr box: the sequence header says sRGB primaries and transfer with the identity
        // matrix, so the planes are still G, B and R
        var fixture = GoldenCorpus.Default.Get("avif/lossless-rgb8");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var item = builder.GetItem(1);
        item.Associations.RemoveAt(FindAssociation(builder, item, "colr"));
        var data = builder.Build();
        Assert.Null(ReferenceAvif.Parse(data).PrimaryItem!.Nclx);

        using var image = Image.Load(data);
        Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), image.Metadata.Cicp);
        AssertSameFrame(fixture, 0, image);
        var full = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(ImageColorModel.Rgb, full.ColorModel);
        Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), full.Metadata.Cicp);

        // Without 'colr' box, a header identification reads the sequence header of the bitstream, and stops there
        var header = Image.Identify(data);
        Assert.Equal(ImageColorModel.Rgb, header.ColorModel);
        Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), header.Metadata.Cicp);

        // When the bitstream comes before the 'meta' box, it went by before the walk knew it needed it: the color model is
        // unknown, never guessed
        var mediaFirst = AvifFileBuilder.From(data);
        mediaFirst.Layout.Clear();
        mediaFirst.Layout.Add(AvifFileBuilder.FileTypeBox);
        mediaFirst.Layout.Add(AvifFileBuilder.MediaDataBox);
        mediaFirst.Layout.Add(AvifFileBuilder.MetaBox);
        var early = Image.Identify(mediaFirst.Build());
        Assert.Equal(ImageColorModel.Unknown, early.ColorModel);
        Assert.Null(early.Metadata.Cicp);
    }

    [Fact]
    public void TransformationsOfARealImageAreComposedInAssociationOrder()
    {
        // avif/irot-1-imir-1 associates irot then imir (orientation 7); the other order is orientation 5; the pixels are the same
        var fixture = GoldenCorpus.Default.Get("avif/irot-1-imir-1");
        Assert.Equal(7, fixture.Expected.Orientation);
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var item = builder.GetItem(1);
        var (rotation, mirror) = (FindAssociation(builder, item, "irot"), FindAssociation(builder, item, "imir"));
        (item.Associations[rotation], item.Associations[mirror]) = (item.Associations[mirror], item.Associations[rotation]);
        var data = builder.Build();
        Assert.Equal(5, ReferenceAvif.Parse(data).PrimaryItem!.Orientation);

        using var image = Image.Load(data);
        Assert.Equal(ExifOrientation.LeftTop, image.Metadata.Orientation);
        AssertSameFrame(fixture, 0, image);
    }

    [Theory]
    [InlineData("stco", "stsz", int.MaxValue, 1)]
    [InlineData("co64", "stsz", int.MaxValue, 1)]
    [InlineData("stco", "stz2", int.MaxValue, 1)]
    [InlineData("stco", "stsz", 1, 1)] // one sample per chunk: as many chunks as samples
    [InlineData("stco", "stsz", 2, 1)] // two samples per chunk and a shorter last chunk: two runs in stsc
    [InlineData("co64", "stz2", 2, 0)] // everything at once, with 32-bit track and media headers
    public void SampleTablesLocateTheSamplesWhateverTheirForm(string chunkOffsets, string sampleSizes, int samplesPerChunk, int headerVersion)
    {
        var fixture = GoldenCorpus.Default.Get("avif/sequence-lossless-infinite");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        foreach (var track in builder.Tracks)
        {
            track.ChunkOffsetBox = chunkOffsets;
            track.SampleSizeBox = sampleSizes;
            track.SamplesPerChunk = samplesPerChunk;
            track.HeaderVersion = headerVersion;
        }

        var data = builder.Build();
        var reference = ReferenceAvif.Parse(data).ColorTrack!;
        Assert.Equal((chunkOffsets, sampleSizes, 3), (reference.ChunkOffsetBox, reference.SampleSizeBox, reference.Samples.Count));

        // Same frames, durations and plays as the fixture, eagerly and frame by frame
        using var image = Image.Load(data);
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(data));
        for (var i = 0; i < 3; i++)
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            GoldenAssert.FrameMatches(fixture, i, ImageSnapshots.CaptureFrame(frame.Frames[0]));
        }

        Assert.Null(reader.ReadFrame());
    }

    [Theory]
    [InlineData(false)] // the data of the items are in the 'mdat' box, with the samples
    [InlineData(true)] // or in the 'idat' box of the 'meta' box
    public void SequenceTakesItsExifAndXmpFromTheMetaBoxOfTheFile(bool inItemData)
    {
        // The fixture has no metadata item and its track has no 'meta' box: the items added to the 'meta' box of the file,
        // which precedes the 'moov' box, are the metadata of the sequence
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"></x:xmpmeta><?xpacket end=\"w\"?>"u8.ToArray();
        var fixture = GoldenCorpus.Default.Get("avif/sequence-lossless-infinite");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var exifItem = builder.AddItem(100, "Exif", AvifFileBuilder.ExifItemData(tiff));
        var xmpItem = builder.AddItem(101, "mime", xmp);
        xmpItem.ContentType = "application/rdf+xml";
        if (inItemData)
        {
            exifItem.Storage = AvifFileBuilder.ItemStorage.ItemData;
            xmpItem.Storage = AvifFileBuilder.ItemStorage.ItemData;
        }

        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
        Assert.NotNull(file.ColorTrack);
        Assert.True(file.HasBrand("avis"));

        using var image = Image.Load(data);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal(Convert.ToHexString(tiff), Convert.ToHexString(image.Metadata.ExifProfile!.Data.Span));
        Assert.Equal(Convert.ToHexString(xmp), Convert.ToHexString(image.Metadata.XmpProfile!.Data.Span));

        // The orientation of the Exif data is not the orientation of the images: the container gives it
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);

        var full = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(Convert.ToHexString(tiff), Convert.ToHexString(full.Metadata.ExifProfile!.Data.Span));
        Assert.Equal(Convert.ToHexString(xmp), Convert.ToHexString(full.Metadata.XmpProfile!.Data.Span));

        // The items are data of the file: a header identification does not read them
        var header = Image.Identify(data);
        Assert.Null(header.Metadata.ExifProfile);
        Assert.Null(header.Metadata.XmpProfile);

        // A reader gives the metadata with the images that follow their data, at the latest with the last one
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(data));
        Image<Rgb24>? last = null;
        try
        {
            while (reader.ReadFrame() is { } frame)
            {
                last?.Dispose();
                last = frame;
            }

            Assert.NotNull(last);
            Assert.Equal(Convert.ToHexString(xmp), Convert.ToHexString(last.Metadata.XmpProfile!.Data.Span));
        }
        finally
        {
            last?.Dispose();
        }
    }

    [Fact]
    public void CompactSampleSizesOfEightBitsAndInterleavedTracks()
    {
        // The alpha samples of the fixture are shorter than 256 bytes: an 8-bit stz2 table; the color samples need 16 bits.
        // One sample per chunk interleaves the samples of the two tracks in the mdat box
        var fixture = GoldenCorpus.Default.Get("avif/sequence-alpha-repeat-2");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        Assert.HasCount(2, builder.Tracks);
        var alpha = builder.Tracks.Single(track => track.HandlerType == "auxv");
        Assert.All(alpha.Samples, sample => Assert.HasCountLessThan(256, sample.Data));
        foreach (var track in builder.Tracks)
        {
            track.SampleSizeBox = "stz2";
            track.CompactSizeBits = ReferenceEquals(track, alpha) ? 8 : 16;
            track.SamplesPerChunk = 1;
        }

        using var image = Image.Load(builder.Build());
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
    }

    [Fact]
    public void TracksAreSelectedByTheirHandlerAndTheirCodec()
    {
        // The alpha track first, and a track of another codec before the image sequence: the pict track of AV1 images is presented
        var fixture = GoldenCorpus.Default.Get("avif/sequence-alpha-repeat-2");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var other = new AvifFileBuilder.Track(9, "pict", 4, 4) { SampleEntryType = "hvc1", Timescale = 1000, Duration = 5 };
        other.Samples.Add(([1, 2, 3], 5));
        var tracks = builder.Tracks.ToList();
        builder.Tracks.Clear();
        builder.Tracks.Add(other);
        builder.Tracks.Add(tracks.Single(track => track.HandlerType == "auxv"));
        builder.Tracks.Add(tracks.Single(track => track.HandlerType == "pict"));

        using var image = Image.Load(builder.Build());
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
    }

    [Fact]
    public void FileWithoutTheSequenceBrandPresentsThePrimaryItemOfASequenceFile()
    {
        // avifenc stores the first image of a sequence as the primary item of the meta box; without the avis brand the
        // file is a still image
        var fixture = GoldenCorpus.Default.Get("avif/sequence-lossless-infinite");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        builder.MajorBrand = "avif";
        builder.CompatibleBrands.Clear();
        builder.CompatibleBrands.Add("avif");
        builder.CompatibleBrands.Add("mif1");
        var data = builder.Build();
        Assert.False(ReferenceAvif.Parse(data).IsSequence);

        using var image = Image.Load(data);
        Assert.Single(image.Frames);
        Assert.Null(image.Animation);
        Assert.Equal(FrameDuration.Zero, image.Frames[0].Metadata.Duration);
        AssertSameFrame(fixture, 0, image);
    }

    [Fact]
    public void GridDescriptorWith32BitFieldsAndSharedTiles()
    {
        var fixture = GoldenCorpus.Default.Get("avif/grid-2x2-lossless");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        builder.GetItem(1).Data = AvifFileBuilder.GridDescriptor(2, 2, 128, 128, largeFields: true);
        using (var image = Image.Load(builder.Build()))
        {
            AssertSameFrame(fixture, 0, image);
        }

        // The four cells refer to the first tile: the top-left quarter of the fixture, four times
        var first = ReferenceAvif.Parse(fixture.ReadInput()).GetGridTiles(ReferenceAvif.Parse(fixture.ReadInput()).PrimaryItem!)[0].Id;
        var index = builder.References.Select((reference, i) => (reference, i)).Single(pair => pair.reference.Type == "dimg").i;
        builder.References[index] = ("dimg", 1, [first, first, first, first]);
        using var repeated = Image.Load<Rgb24>(builder.Build());
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgb8);
        for (var y = 0; y < 128; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                var pixel = repeated.Frames[0][x, y];
                Assert.Equal((reference.GetSample(x % 64, y % 64, 0), reference.GetSample(x % 64, y % 64, 1), reference.GetSample(x % 64, y % 64, 2)), ((int)pixel.R, (int)pixel.G, (int)pixel.B));
            }
        }
    }

    [Fact]
    public void ExifItemWithAnOffsetToItsTiffHeader()
    {
        // ISO/IEC 23008-12, annex A.2.1: the item starts with the offset of the TIFF header from the end of that field
        var fixture = GoldenCorpus.Default.Get("avif/metadata-exif-xmp-icc");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var exif = builder.Items.Single(item => item.Type == "Exif");
        var tiff = exif.Data[4..];
        exif.Data = AvifFileBuilder.Concat(AvifFileBuilder.U32(6), "Exif\0\0"u8.ToArray(), tiff);
        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
        Assert.Equal(tiff, file.ReadExifPayload(file.FindDescriptions(1).Single(item => item.Type == "Exif")));

        using var image = Image.Load(data);
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
    }

    [Fact]
    public void FileTypeBoxWithA64BitSizeIsAnAvifFile()
    {
        // ISO/IEC 14496-12, section 4.2: any box may store its size in the 64-bit field, the file type box included
        var builder = AvifFileBuilder.From(GoldenCorpus.Default.Get("avif/lossless-rgb8").ReadInput());
        builder.LargeSizeBoxes.Add(AvifFileBuilder.FileTypeBox);
        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
        Assert.True(file.TopLevelBoxes[0] is { Type: "ftyp", HasLargeSize: true } && file.HasBrand("avif"));
        Assert.Equal(ImageFormat.Avif, Image.DetectFormat(data));
        using var image = Image.Load(data);
        AssertSameFrame(GoldenCorpus.Default.Get("avif/lossless-rgb8"), 0, image);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Layered images: the a1op, lsel and a1lx properties around the payloads of the layered fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private const string ThreeLayers = "avif/layered-3-quality-rgb";

    private static void AddEssential(AvifFileBuilder builder, AvifFileBuilder.Item item, byte[] box) => item.Associations.Add((builder.AddProperty(box), true));

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(0x100)]
    [InlineData(0x7FFF)]
    [InlineData(0xFFFE)]
    public void LayerSelectorAboveTheLayersOfAv1IsInvalidContent(int layer)
    {
        // AV1 has four spatial layers: a layer_id of 4 to 0xFFFE names none, on a layered image and on any other image
        // (ISO/IEC 23008-12, section 6.5.11; AV1 Image File Format, section 2.3.2.2). The property is read with the item
        // model: a header identification reports it
        foreach (var source in new[] { ThreeLayers, "avif/lossless-rgb8" })
        {
            var builder = AvifFileBuilder.From(GoldenCorpus.Default.Get(source).ReadInput());
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector((ushort)layer));
            var data = builder.Build();
            Assert.Equal(layer, ReferenceAvif.Parse(data).PrimaryItem!.LayerSelector);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose());
        }
    }

    [Theory]
    [InlineData(0, "avif/layered-lsel-0")]
    [InlineData(1, "avif/layered-lsel-1")]
    [InlineData(2, ThreeLayers)]
    [InlineData(0xFFFF, ThreeLayers)]
    public void LayerSelectorNeedNotBeEssentialAndTheLayerSizesAreIgnored(int layer, string expected)
    {
        // The frame of the selected layer is the image whether or not the property is marked essential, and whatever the
        // a1lx property says about the sizes of the layers: without it, and with sizes that are not those of the layers
        var source = GoldenCorpus.Default.Get(ThreeLayers).ReadInput();
        Assert.NotNull(ReferenceAvif.Parse(source).PrimaryItem!.LayerSizes);
        foreach (var layerSizes in new byte[]?[] { null, [0, 0, 1, 0, 1, 0, 0], [1, 0, 0, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0], [] })
        {
            var builder = AvifFileBuilder.From(source);
            var item = builder.GetItem(1);
            item.Associations.Add((builder.AddProperty(AvifFileBuilder.LayerSelector((ushort)layer)), false));
            var index = FindAssociation(builder, item, "a1lx");
            if (layerSizes is null)
                item.Associations.RemoveAt(index);
            else if (layerSizes.Length > 0)
                item.Associations[index] = (builder.AddProperty(AvifFileBuilder.Box("a1lx", layerSizes)), false);

            var data = builder.Build();
            var file = ReferenceAvif.Parse(data);
            Assert.Equal(layer, file.PrimaryItem!.LayerSelector);
            Assert.Equal(layerSizes is null, file.PrimaryItem.LayerSizes is null);
            using var image = Image.Load(data);
            AssertSameFrame(GoldenCorpus.Default.Get(expected), 0, image);
        }
    }

    [Theory]
    [InlineData(0, null, ThreeLayers)]
    [InlineData(1, null, "avif/layered-a1op-1")]
    [InlineData(2, null, "avif/layered-a1op-2")]
    [InlineData(0, 2, ThreeLayers)]
    [InlineData(0, 1, "avif/layered-lsel-1")]
    [InlineData(1, 1, "avif/layered-a1op-1")]
    [InlineData(1, 0xFFFF, "avif/layered-a1op-1")]
    [InlineData(2, 0, "avif/layered-a1op-2")]
    [InlineData(2, 0xFFFF, "avif/layered-a1op-2")]
    public void OperatingPointThenLayerSelectTheFrame(int operatingPoint, int? layer, string expected)
    {
        // The operating points of the bitstream are its three layers, its first two layers and its first layer. The image
        // is the frame of the highest layer of the selected operating point, or of the layer that lsel selects in it,
        // whatever the order of the two properties
        foreach (var layerFirst in new[] { false, true })
        {
            var builder = AvifFileBuilder.From(GoldenCorpus.Default.Get(ThreeLayers).ReadInput());
            var item = builder.GetItem(1);
            if (layer is { } selected && layerFirst)
                AddEssential(builder, item, AvifFileBuilder.LayerSelector((ushort)selected));

            AddEssential(builder, item, AvifFileBuilder.OperatingPointSelector((byte)operatingPoint));
            if (layer is { } value && !layerFirst)
                AddEssential(builder, item, AvifFileBuilder.LayerSelector((ushort)value));

            var data = builder.Build();
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Assert.Equal((8, 8), (info.Width, info.Height));
            using var image = Image.Load(data);
            AssertSameFrame(GoldenCorpus.Default.Get(expected), 0, image);
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(0, 3)]
    public void LayerOutsideTheOperatingPointIsInvalidContent(int operatingPoint, int layer)
    {
        // The bitstream has the layer (or, for layer 3, does not), and the selected operating point does not: there is
        // no frame to present. The file is well formed up to the frames: a header identification succeeds
        var builder = AvifFileBuilder.From(GoldenCorpus.Default.Get(ThreeLayers).ReadInput());
        AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector((byte)operatingPoint));
        AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector((ushort)layer));
        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
        var idc = ReferenceObuStream.ReadOperatingPoints(file.ReadItemData(file.PrimaryItem!))[operatingPoint];
        Assert.Equal(0, (idc >> (8 + layer)) & 1);
        Assert.Equal((8, 8), (Image.Identify(data).Width, Image.Identify(data).Height));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(31)]
    [InlineData(255)]
    public void OperatingPointThatTheSequenceHeaderDoesNotHaveIsInvalidContent(int operatingPoint)
    {
        // The sequence header of the bitstream has three operating points. The index is known to be wrong once the sequence
        // header is read: by a full scan and by a load, not by a header identification of a file that has a colr box
        var builder = AvifFileBuilder.From(GoldenCorpus.Default.Get(ThreeLayers).ReadInput());
        AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector((byte)operatingPoint));
        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
        Assert.HasCount(3, ReferenceObuStream.ReadOperatingPoints(file.ReadItemData(file.PrimaryItem!)));
        Assert.NotNull(file.PrimaryItem!.Nclx);
        Assert.Equal((8, 8), (Image.Identify(data).Width, Image.Identify(data).Height));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose());
    }

    [Fact]
    public void GridItemAcceptsTheSelectorsThatSelectNothing()
    {
        // On a derived image, an lsel property that selects no layer (0xFFFF) and an a1op property that selects the default
        // operating point change nothing; any other value is invalid (the fixtures invalid/avif/lsel-on-grid and
        // a1op-on-grid, and every other layer and operating point here)
        var fixture = GoldenCorpus.Default.Get("avif/layered-grid-2x1");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0xFFFF));
        AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(0));
        var data = builder.Build();
        Assert.Equal(("grid", 0xFFFF, 0), (ReferenceAvif.Parse(data).PrimaryItem!.Type, ReferenceAvif.Parse(data).PrimaryItem!.LayerSelector, ReferenceAvif.Parse(data).PrimaryItem!.OperatingPoint));
        using (var image = Image.Load(data))
        {
            AssertSameFrame(fixture, 0, image);
        }

        foreach (var box in new[] { AvifFileBuilder.LayerSelector(1), AvifFileBuilder.LayerSelector(3), AvifFileBuilder.OperatingPointSelector(1), AvifFileBuilder.OperatingPointSelector(255) })
        {
            var invalid = AvifFileBuilder.From(fixture.ReadInput());
            AddEssential(invalid, invalid.GetItem(1), box);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(invalid.Build()));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(invalid.Build()).Dispose());
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(0, 2)]
    public void TilesOfAGridSelectTheirOwnOperatingPoint(int firstOperatingPoint, int secondOperatingPoint)
    {
        // A grid of two tiles that are the same coded payload, the three layers of avif/layered-3-quality-rgb: the two
        // tiles have the same bitstream, hence the same sequence header, and only differ by their a1op property. Each tile
        // gives the frame of the highest layer of its own operating point: the committed references of the fixtures that
        // select the same operating point of the same payload
        var source = GoldenCorpus.Default.Get(ThreeLayers);
        var builder = AvifFileBuilder.From(source.ReadInput());
        var first = builder.GetItem(1);
        first.Id = 2;
        first.IsHidden = true;
        var grid = builder.AddItem(1, "grid", AvifFileBuilder.GridDescriptor(1, 2, 16, 8), builder.AddProperty(AvifFileBuilder.ImageSize(16, 8)));
        grid.Associations.Add(first.Associations[FindAssociation(builder, first, "colr")]);
        var second = builder.AddItem(3, "av01", first.Data);
        second.IsHidden = true;
        foreach (var association in first.Associations)
        {
            second.Associations.Add(association);
        }

        AddEssential(builder, first, AvifFileBuilder.OperatingPointSelector((byte)firstOperatingPoint));
        AddEssential(builder, second, AvifFileBuilder.OperatingPointSelector((byte)secondOperatingPoint));
        builder.References.Add(("dimg", 1, [2, 3]));
        var data = builder.Build();
        var file = ReferenceAvif.Parse(data);
        var tiles = file.GetGridTiles(file.PrimaryItem!);
        Assert.Equal([firstOperatingPoint, secondOperatingPoint], tiles.Select(tile => tile.OperatingPoint!.Value).ToArray());
        Assert.Equal(Convert.ToHexString(file.ReadItemData(tiles[0])), Convert.ToHexString(file.ReadItemData(tiles[1])));

        string[] layers = [ThreeLayers, "avif/layered-a1op-1", "avif/layered-a1op-2"];
        var left = GoldenCorpus.Default.Get(layers[firstOperatingPoint]).GetFrame(0, RawPixelLayout.Rgb8);
        var right = GoldenCorpus.Default.Get(layers[secondOperatingPoint]).GetFrame(0, RawPixelLayout.Rgb8);
        using var image = Image.Load<Rgb24>(data);
        Assert.Equal((16, 8), (image.Width, image.Height));
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var reference = x < 8 ? left : right;
                var pixel = image.Frames[0][x, y];
                Assert.Equal((reference.GetSample(x % 8, y, 0), reference.GetSample(x % 8, y, 1), reference.GetSample(x % 8, y, 2)), ((int)pixel.R, (int)pixel.G, (int)pixel.B));
            }
        }
    }

    [Fact]
    public void AlphaItemSelectsItsOwnLayer()
    {
        // The color item and the alpha item of avif/layered-progressive-alpha are two layered images. Selecting the last
        // layer of both gives the fixture; an alpha plane whose selected frame is smaller than what its ispe property
        // declares is invalid, like a color image
        var fixture = GoldenCorpus.Default.Get("avif/layered-progressive-alpha");
        var source = ReferenceAvif.Parse(fixture.ReadInput());
        var (colorId, alphaId) = (source.PrimaryItem!.Id, source.AlphaItem!.Id);
        Assert.Equal([0, 1], ReferenceObuStream.GetSpatialLayers(source.ReadItemData(source.AlphaItem)).ToArray());
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        AddEssential(builder, builder.GetItem(colorId), AvifFileBuilder.LayerSelector(1));
        AddEssential(builder, builder.GetItem(alphaId), AvifFileBuilder.LayerSelector(1));
        using (var image = Image.Load(builder.Build()))
        {
            AssertSameFrame(fixture, 0, image);
        }

        var smallerAlpha = AvifFileBuilder.From(fixture.ReadInput());
        AddEssential(smallerAlpha, smallerAlpha.GetItem(alphaId), AvifFileBuilder.LayerSelector(0));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(smallerAlpha.Build()).Dispose());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SampleThatHoldsTwoLayeredTemporalUnitsIsInvalidContent(bool withoutDelimiter)
    {
        // A sample of an image sequence is one temporal unit, which gives one image: the frame of its highest layer. The two
        // samples of the fixture in one sample are two temporal units, with or without the temporal delimiter between them
        // (a frame of layer 0 after a frame of layer 1 starts another temporal unit)
        var fixture = GoldenCorpus.Default.Get("avif/sequence-layered");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var track = builder.Tracks.Single();
        Assert.HasCount(2, track.Samples);
        var second = track.Samples[1].Data;
        if (withoutDelimiter)
            second = ReferenceObuStream.Remove(second, unit => unit.Type == ReferenceObuStream.TemporalDelimiter);

        byte[] merged = [.. track.Samples[0].Data, .. second];
        Assert.Equal([0, 1, 0, 1], ReferenceObuStream.ReadUnits(merged).Where(unit => unit.Type == ReferenceObuStream.Frame).Select(unit => unit.SpatialId).ToArray());
        Assert.Equal(withoutDelimiter ? 1 : 2, ReferenceObuStream.ReadUnits(merged).Count(unit => unit.Type == ReferenceObuStream.TemporalDelimiter));
        track.Samples.Clear();
        track.Samples.Add((merged, 5));
        var data = builder.Build();
        Assert.Equal(1, Image.Identify(data).FrameCount);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose());
    }

    [Fact]
    public void SamplesWithoutTemporalDelimiterGiveTheFrameOfTheirHighestLayer()
    {
        // The samples of an AV1 track need no temporal delimiter (AV1 Codec ISO Media File Format Binding, section 2.4):
        // without it, each sample still gives one image
        var fixture = GoldenCorpus.Default.Get("avif/sequence-layered");
        var builder = AvifFileBuilder.From(fixture.ReadInput());
        var track = builder.Tracks.Single();
        for (var i = 0; i < track.Samples.Count; i++)
        {
            var (sample, duration) = track.Samples[i];
            var stripped = ReferenceObuStream.Remove(sample, unit => unit.Type == ReferenceObuStream.TemporalDelimiter);
            Assert.HasCount(sample.Length - 2, stripped);
            track.Samples[i] = (stripped, duration);
        }

        using var image = Image.Load(builder.Build());
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
    }

    private static void AssertSameFrame(GoldenFixture fixture, int frameIndex, Image image)
    {
        var actual = ImageSnapshots.CaptureFrame(image.Frames[0]);
        Assert.True(fixture.GetFrame(frameIndex, actual.Layout).Span.SequenceEqual(actual.Span), $"The frame differs from frame {frameIndex} of {fixture.Id}.");
    }

    /// <summary>
    /// Decodes the primary item with the AV1 decoder of the library and converts its planes with the exact-rational
    /// converter of the harness. The items of these tests have no alpha plane, grid, clean aperture or film grain.
    /// </summary>
    private static int[] ConvertPrimaryItem(byte[] data, int matrix, bool fullRange, int outputMaximum)
    {
        var file = ReferenceAvif.Parse(data);
        var payload = file.ReadItemData(file.PrimaryItem!);
        var scope = AllocationScope.Create(ImageConfiguration.Default, "avif planes");
        using var decoder = new Av1Decoder(scope);
        decoder.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
        Assert.NotNull(frame);
        try
        {
            Assert.Null(frame.Header.FilmGrain);
            var planes = new ushort[frame.PlaneCount][];
            for (var plane = 0; plane < frame.PlaneCount; plane++)
            {
                var source = frame.GetPlane(plane);
                planes[plane] = new ushort[source.Width * source.Height];
                for (var y = 0; y < source.Height; y++)
                {
                    source.GetRow(y)[..source.Width].CopyTo(planes[plane].AsSpan(y * source.Width, source.Width));
                }
            }

            var sequence = frame.Sequence;
            var luma = frame.GetPlane(0);
            return ReferenceYuvConverter.ConvertToRgb(planes[0], planes[1], planes[2], luma.Width, luma.Height, sequence.BitDepth, sequence.SubsamplingX, sequence.SubsamplingY, sequence.ChromaSamplePosition, matrix, fullRange, outputMaximum);
        }
        finally
        {
            frame.Release();
        }
    }

    private static string PropertyType(byte[] box) => Encoding.Latin1.GetString(box.AsSpan(4, 4));

    private static int FindAssociation(AvifFileBuilder builder, AvifFileBuilder.Item item, string type)
    {
        for (var i = 0; i < item.Associations.Count; i++)
        {
            if (PropertyType(builder.Properties[item.Associations[i].PropertyIndex - 1]) == type)
                return i;
        }

        throw new InvalidOperationException($"The item has no '{type}' property.");
    }

    private static void ReplaceProperty(AvifFileBuilder builder, AvifFileBuilder.Item item, string type, byte[] box)
    {
        var index = FindAssociation(builder, item, type);
        item.Associations[index] = (builder.AddProperty(box), item.Associations[index].IsEssential);
    }
}
