using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Heif;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Container rules that are cheaper to express in code than as committed files: the HEVC payloads of corpus fixtures are
/// placed in other containers by <see cref="HeifFileBuilder"/> (no tool, no new file) and decoded by the library.
/// <list type="bullet">
/// <item><description>
/// A container around the same payloads must give the frames, the durations and the metadata of the fixture: the
/// expectations are the committed references of the fixture.
/// </description></item>
/// <item><description>
/// The matrix coefficients and the range of a <c>colr</c> box written over the payload must give the conversion of
/// <see cref="ReferenceYuvConverter"/> (exact rationals) within one unit, with the chroma sample position that Rec. ITU-T
/// H.265 gives to the picture. Its input planes are decoded by the HEVC decoder of the library, which the HEVC vectors
/// verify against three independent decoders, and the test first checks that these planes reproduce the committed
/// references of the unmodified fixtures: the conversion is what is under test here.
/// </description></item>
/// </list>
/// </summary>
public sealed class HeicContainerConformanceTests
{
    // 4:4:4, 4:2:2 and 4:2:0 with each chroma sample location of a frame, at 8 and 10 bits; pictures with and without a
    // conformance window. None has a colr box, an alpha plane, a grid or a clean aperture
    private static readonly string[] MatrixSources =
    [
        "heic/no-colr-no-signal-444", "heic/no-colr-bitstream-bt709-limited", "heic/window-422", "heic/chroma-loc-0-420", "heic/chroma-loc-1-420", "heic/chroma-loc-2-420",
        "heic/window-420-bt2020-limited-10bit",
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
        // The committed reference comes from the planes of the reference decoders; the same conversion of the planes the
        // library decodes must give the same bytes
        var fixture = GoldenCorpus.Default.Get(id);
        var matrix = fixture.Expected.Cicp?.MatrixCoefficients ?? 2;
        var fullRange = fixture.Entry.HasFeature("heic.range=full");
        var expected = fixture.GetFrame(0, fixture.CanonicalLayout);
        var converted = ConvertPrimaryItem(fixture.ReadInput(), matrix, fullRange, expected.Layout.MaxSampleValue);
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
        var builder = HeifFileBuilder.From(GoldenCorpus.Default.Get(id).ReadInput());
        builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.ColorNclx(1, 13, matrix, fullRange)), false));
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void LengthOfTheNalUnitsHasTheSizeTheRecordGives(int lengthSize)
    {
        // The NAL units of heic/nclx-overrides-bitstream, preceded by lengths of 1, 2 or 4 bytes as the record announces
        var fixture = GoldenCorpus.Default.Get("heic/nclx-overrides-bitstream");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        var item = builder.GetItem(1);
        var index = FindAssociation(builder, item, "hvcC");
        var record = (byte[])builder.Properties[item.Associations[index].PropertyIndex - 1].Clone();
        var units = ReferenceHeif.HevcConfiguration.Parse(record.AsSpan(8)).SplitNalUnits(item.Data);
        record[8 + 21] = (byte)((record[8 + 21] & 0xFC) | (lengthSize - 1));
        item.Associations[index] = (builder.AddProperty(record), true);
        item.Data = [.. units.SelectMany(unit =>
        {
            var length = new byte[lengthSize];
            for (var i = 0; i < lengthSize; i++)
            {
                length[i] = (byte)(unit.Length >> (8 * (lengthSize - 1 - i)));
            }

            return length.Concat(unit.ToArray());
        })];
        var data = builder.Build();
        Assert.Equal(lengthSize, ReferenceHeif.Parse(data).PrimaryItem!.HevcConfiguration!.NalUnitLengthSize);

        using var image = Image.Load(data);
        AssertSameFrame(fixture, 0, image);
    }

    [Theory]
    [InlineData("stco", "stsz", int.MaxValue, 1)]
    [InlineData("co64", "stsz", int.MaxValue, 1)]
    [InlineData("stco", "stz2", int.MaxValue, 1)]
    [InlineData("stco", "stsz", 1, 1)] // one sample per chunk: as many chunks as samples
    [InlineData("stco", "stsz", 3, 1)] // three samples per chunk and a shorter last chunk: two runs in stsc
    [InlineData("co64", "stz2", 3, 0)] // everything at once, with 32-bit track and media headers
    public void SampleTablesLocateTheSamplesWhateverTheirForm(string chunkOffsets, string sampleSizes, int samplesPerChunk, int headerVersion)
    {
        // The sequence with B pictures: its composition offsets go with the samples, wherever their chunks are
        var fixture = GoldenCorpus.Default.Get("heic/sequence-window-reordered-2-plays");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        foreach (var track in builder.Tracks)
        {
            track.ChunkOffsetBox = chunkOffsets;
            track.SampleSizeBox = sampleSizes;
            track.SamplesPerChunk = samplesPerChunk;
            track.HeaderVersion = headerVersion;
        }

        var data = builder.Build();
        var reference = ReferenceHeif.Parse(data).ColorTrack!;
        Assert.Equal((chunkOffsets, sampleSizes, 8), (reference.ChunkOffsetBox, reference.SampleSizeBox, reference.Samples.Count));
        AssertSameSequence(fixture, data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(1000)]
    public void CompositionOffsetsOnlyOrderTheImages(int shift)
    {
        // The same offsets plus a constant: every image is presented later by the same time, so the order and the
        // durations are those of the fixture. The edit list starts at the presentation time of the first image
        var fixture = GoldenCorpus.Default.Get("heic/sequence-window-reordered-2-plays");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        var track = builder.Tracks[0];
        track.CompositionOffsets = [.. track.CompositionOffsets!.Select(offset => offset + shift)];
        var edit = track.EditList!.Entries[0];
        track.EditList.Entries[0] = (edit.SegmentDuration, edit.MediaTime + shift, edit.MediaRateInteger, edit.MediaRateFraction);
        AssertSameSequence(fixture, builder.Build());
    }

    [Fact]
    public void SignedCompositionOffsetsOrderTheImages()
    {
        // Version 1 of the ctts box: the offsets are signed, and the first image is presented at time zero when the
        // smallest one is the opposite of the decoding delay
        var fixture = GoldenCorpus.Default.Get("heic/sequence-no-edit-list-reordered");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        var track = builder.Tracks[0];
        var first = track.CompositionOffsets![0];
        track.CompositionOffsets = [.. track.CompositionOffsets.Select(offset => offset - first)];
        track.CompositionOffsetsVersion = 1;
        Assert.Contains(track.CompositionOffsets, offset => offset < 0);
        var data = builder.Build();
        Assert.Contains(ReferenceHeif.Parse(data).ColorTrack!.CompositionOffsets!, offset => offset < 0);
        AssertSameSequence(fixture, data);
    }

    [Theory]
    [InlineData("hvc1")]
    [InlineData("hev1")]
    public void BothSampleEntriesOfHevcAreAnImageSequence(string sampleEntry)
    {
        // The parameter sets of this track are in its configuration record: under an hev1 sample entry, which also allows
        // them in the samples, the sequence is the same
        var fixture = GoldenCorpus.Default.Get("heic/sequence-window-rgb");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        builder.Tracks[0].SampleEntryType = sampleEntry;
        var data = builder.Build();
        Assert.Equal(sampleEntry, ReferenceHeif.Parse(data).ColorTrack!.SampleEntryType);
        AssertSameSequence(fixture, data);
    }

    [Theory]
    [InlineData("heic", "heic", "mif1")]
    [InlineData("heix", "mif1", "heix", "miaf")]
    [InlineData("mif1", "mif1", "heic")]
    [InlineData("miaf", "heix", "mif1")]
    [InlineData("mif1", "avif", "heic")] // both formats are declared: the item is an hvc1 item
    [InlineData("avif", "avif", "mif1", "heix")]
    public void BrandsOfAnHevcImageMakeAnHeicFile(string major, params string[] compatible)
    {
        var fixture = GoldenCorpus.Default.Get("heic/nclx-overrides-bitstream");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        SetBrands(builder, major, compatible);
        var data = builder.Build();
        using var image = Image.Load(data);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.Equal(ImageFormat.Heic, Image.Identify(data).Format);
        AssertSameFrame(fixture, 0, image);
    }

    [Theory]
    [InlineData("hevc", "hevc", "msf1")]
    [InlineData("hevx", "msf1", "hevx", "iso8")]
    [InlineData("msf1", "msf1", "hevc")]
    [InlineData("msf1", "avis", "hevx")] // both formats are declared: the track is an hvc1 track
    public void BrandsOfAnHevcTrackMakeAnHeicSequence(string major, params string[] compatible)
    {
        var fixture = GoldenCorpus.Default.Get("heic/sequence-window-rgb");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        SetBrands(builder, major, compatible);
        var data = builder.Build();
        Assert.Equal(ImageFormat.Heic, Image.Identify(data).Format);
        AssertSameSequence(fixture, data);
    }

    [Fact]
    public void FileWithoutTheSequenceBrandPresentsThePrimaryItemOfASequenceFile()
    {
        // heif-enc stores an image of a sequence as the primary item of the meta box; without the hevc or hevx brand the
        // file is a still image, and with it the item is not read
        var fixture = GoldenCorpus.Default.Get("heic/sequence-lossless-3-plays");
        Assert.True(fixture.Entry.HasFeature("heic.seq.fileImageNotRead"));
        var source = ReferenceHeif.Parse(fixture.ReadInput());
        Assert.NotNull(source.PrimaryItem);
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        SetBrands(builder, "heix", "mif1", "heix", "miaf");
        var data = builder.Build();
        Assert.False(ReferenceHeif.Parse(data).IsSequence);

        using var image = Image.Load(data);
        Assert.Single(image.Frames);
        Assert.Null(image.Animation);
        Assert.Equal(FrameDuration.Zero, image.Frames[0].Metadata.Duration);
        Assert.Equal((fixture.Expected.Width, fixture.Expected.Height), (image.Width, image.Height));
    }

    [Fact]
    public void TracksAreSelectedByTheirHandlerAndTheirCodec()
    {
        // The alpha track first, and a track of another codec before the image sequence: the pict track of HEVC images is presented
        var fixture = GoldenCorpus.Default.Get("heic/sequence-alpha-lossless");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        var other = new HeifFileBuilder.Track(9, "pict", 4, 4) { SampleEntryType = "av01", Timescale = 1000, Duration = 5 };
        other.Samples.Add(([1, 2, 3], 5));
        var tracks = builder.Tracks.ToList();
        Assert.Contains(tracks, track => track.HandlerType == "auxv");
        builder.Tracks.Clear();
        builder.Tracks.Add(other);
        foreach (var track in tracks.OrderBy(track => track.HandlerType == "pict"))
        {
            builder.Tracks.Add(track);
        }

        using var image = Image.Load(builder.Build());
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
    }

    [Fact]
    public void GridDescriptorWith32BitFields()
    {
        var fixture = GoldenCorpus.Default.Get("heic/grid-2x2-lossless");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        var grid = builder.GetItem(builder.PrimaryItemId!.Value);
        grid.Data = HeifFileBuilder.GridDescriptor(2, 2, (uint)fixture.Expected.Width, (uint)fixture.Expected.Height, largeFields: true);
        using var image = Image.Load(builder.Build());
        AssertSameFrame(fixture, 0, image);
    }

    [Fact]
    public void AuxiliaryImagesOtherThanAlphaAndThumbnailsAreNotRead()
    {
        // The phone layout without its thumbnail and without its depth map is the same image; and an alpha plane under
        // the auxiliary type of a depth map is not an alpha plane: the image is opaque
        var fixture = GoldenCorpus.Default.Get("heic/phone-grid-420");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        var hidden = builder.References.Where(reference => reference.Type is "thmb" or "auxl").ToList();
        Assert.HasCount(2, hidden);
        foreach (var reference in hidden)
        {
            builder.References.Remove(reference);
            builder.Items.Remove(builder.GetItem(reference.FromItemId));
        }

        using (var image = Image.Load(builder.Build()))
        {
            AssertSameFrame(fixture, 0, image);
        }

        var withAlpha = GoldenCorpus.Default.Get("heic/alpha-mpegb-urn");
        var opaque = HeifFileBuilder.From(withAlpha.ReadInput());
        ReplaceProperty(opaque, opaque.GetItem(2), "auxC", HeifFileBuilder.AuxiliaryType("urn:mpeg:hevc:2015:auxid:2"));
        using (var image = Image.Load(opaque.Build()))
        {
            Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
            var plain = GoldenCorpus.Default.Get("heic/no-colr-no-signal-444");
            AssertSameFrame(plain, 0, image);
        }

        // The same alpha plane under the auxiliary type of HEVC
        var hevc = HeifFileBuilder.From(withAlpha.ReadInput());
        ReplaceProperty(hevc, hevc.GetItem(2), "auxC", HeifFileBuilder.AuxiliaryType(ReferenceHeif.HevcAlphaAuxiliaryType));
        using (var image = Image.Load(hevc.Build()))
        {
            AssertSameFrame(withAlpha, 0, image);
        }
    }

    [Fact]
    public void ParameterSetsThatPrecedeTheMetaBoxAreNotNeededByAHeaderIdentification()
    {
        // The parameter sets of this item are in its data only, which precede the meta box: a header identification has
        // let them go by, and reports an unknown color model rather than a guess; a full scan and a load read them
        var fixture = GoldenCorpus.Default.Get("heic/parameter-sets-in-band");
        var builder = HeifFileBuilder.From(fixture.ReadInput());
        builder.Layout.Clear();
        foreach (var box in new[] { HeifFileBuilder.FileTypeBox, HeifFileBuilder.MediaDataBox, HeifFileBuilder.MetaBox })
        {
            builder.Layout.Add(box);
        }

        var data = builder.Build();
        var header = Image.Identify(new MemoryStream(data));
        Assert.Equal((fixture.Expected.Width, fixture.Expected.Height), (header.Width, header.Height));
        Assert.Equal(ImageColorModel.Unknown, header.ColorModel);
        Assert.Null(header.Metadata.Cicp);
        Assert.Equal(ImageColorModel.YCbCr, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).ColorModel);
        using var image = Image.Load(data);
        AssertSameFrame(fixture, 0, image);
    }

    private static void SetBrands(HeifFileBuilder builder, string major, params string[] compatible)
    {
        builder.MajorBrand = major;
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatible)
        {
            builder.CompatibleBrands.Add(brand);
        }
    }

    private static void AssertSameFrame(GoldenFixture fixture, int frameIndex, Image image)
    {
        var actual = ImageSnapshots.CaptureFrame(image.Frames[0]);
        GoldenAssert.FrameMatches(fixture, frameIndex, actual);
    }

    /// <summary>Same frames, durations and plays as the fixture, eagerly and frame by frame.</summary>
    private static void AssertSameSequence(GoldenFixture fixture, byte[] data)
    {
        using var image = Image.Load(data);
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(data));
        for (var i = 0; i < fixture.Expected.FrameCount; i++)
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            GoldenAssert.FrameMatches(fixture, i, ImageSnapshots.CaptureFrame(frame.Frames[0]));
            Assert.Equal(image.Frames[i].Metadata.Duration, frame.Frames[0].Metadata.Duration);
        }

        Assert.Null(reader.ReadFrame());
    }

    /// <summary>
    /// Decodes the primary item with the HEVC decoder of the library, crops its planes to the conformance window (whose
    /// left and top offsets are zero in these files) and converts them with the exact-rational converter of the harness,
    /// at the chroma sample position of the picture. The items of these tests have no alpha plane, grid or clean aperture.
    /// </summary>
    private static int[] ConvertPrimaryItem(byte[] data, int matrix, bool fullRange, int outputMaximum)
    {
        var file = ReferenceHeif.Parse(data);
        var item = file.PrimaryItem!;
        var configuration = item.HevcConfiguration!;
        Assert.Equal(4, configuration.NalUnitLengthSize);

        // The NAL units of the record, then those of the item, each after its length
        var input = new List<byte>();
        foreach (var unit in configuration.NalUnits)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)unit.Length);
            input.AddRange(length);
            input.AddRange(unit.ToArray());
        }

        input.AddRange(file.ReadItemData(item));
        var bytes = input.ToArray();
        var scope = AllocationScope.Create(ImageConfiguration.Default, "heic planes");
        using var decoder = new HevcDecoder(scope);
        decoder.SetInput(bytes, 0, bytes.Length, 4);
        var picture = decoder.DecodeNextOutputPicture(TestContext.Current.CancellationToken);
        if (picture is null)
        {
            decoder.EndOfSequence();
            picture = decoder.DecodeNextOutputPicture(TestContext.Current.CancellationToken);
        }

        Assert.NotNull(picture);
        try
        {
            var sps = picture.Sps;
            var (width, height) = (sps.OutputWidth, sps.OutputHeight);
            var subsamplingX = sps.SubWidthC is 2 ? 1 : 0;
            var subsamplingY = sps.SubHeightC is 2 ? 1 : 0;
            Assert.Equal(3, picture.PlaneCount);
            var planes = new ushort[3][];
            for (var plane = 0; plane < 3; plane++)
            {
                var source = picture.GetPlane(plane);
                var (planeWidth, planeHeight) = plane is 0 ? (width, height) : (width >> subsamplingX, height >> subsamplingY);
                planes[plane] = new ushort[planeWidth * planeHeight];
                for (var y = 0; y < planeHeight; y++)
                {
                    source.GetRow(y)[..planeWidth].CopyTo(planes[plane].AsSpan(y * planeWidth, planeWidth));
                }
            }

            var location = sps.Vui is { ChromaLocInfoPresentFlag: true } vui ? vui.ChromaSampleLocTypeTopField : 0;
            var (cositedHorizontally, cositedVertically) = ReferenceYuvConverter.GetHevcChromaSiting(sps.ChromaFormatIdc, location);
            return ReferenceYuvConverter.ConvertToRgb(planes[0], planes[1], planes[2], width, height, sps.BitDepth, subsamplingX, subsamplingY, cositedHorizontally, cositedVertically, matrix, fullRange, outputMaximum);
        }
        finally
        {
            picture.Release();
        }
    }

    private static string PropertyType(byte[] box) => Encoding.Latin1.GetString(box.AsSpan(4, 4));

    private static int FindAssociation(HeifFileBuilder builder, HeifFileBuilder.Item item, string type)
    {
        for (var i = 0; i < item.Associations.Count; i++)
        {
            if (PropertyType(builder.Properties[item.Associations[i].PropertyIndex - 1]) == type)
                return i;
        }

        throw new InvalidOperationException($"The item has no '{type}' property.");
    }

    private static void ReplaceProperty(HeifFileBuilder builder, HeifFileBuilder.Item item, string type, byte[] box)
    {
        var index = FindAssociation(builder, item, type);
        item.Associations[index] = (builder.AddProperty(box), item.Associations[index].IsEssential);
    }
}
