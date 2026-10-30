using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Exr;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// OpenEXR decoding of files assembled by <see cref="ExrFileBuilder"/> with uncompressed chunks and literal samples (OpenEXR
/// File Layout): the header and its validation, the offset table and the order of the chunks, the data and display
/// windows, the roles of the channels and their pixel types, tiles and their levels, the color labels, the errors, the
/// limits and the readers. The compressed files OpenEXR writes are compared in the conformance tests.
/// </summary>
public sealed class ExrDecoderTests
{
    private const int None = 0;
    private const int Zip = 3;

    [Fact]
    public void HalfSamplesLoadAsRgbaHalfExactly()
    {
        // 2x2, channels B, G, R: per line, each channel's samples
        var data = ScanLineFile(2, 2, None, [HalfChannel("B"), HalfChannel("G"), HalfChannel("R")],
            [0f, 0.25f, 0.125f, 2f, 1.5f, -1f],
            [3f, 4f, 5f, 6f, 65504f, -0.0f]);
        using var image = Image.Load(data);
        var half = Assert.IsType<Image<RgbaHalf>>(image);
        Assert.Equal(new RgbaHalf((Half)1.5f, (Half)0.125f, (Half)0f, (Half)1f), half.Frames[0][0, 0]);
        Assert.Equal(new RgbaHalf((Half)(-1f), (Half)2f, (Half)0.25f, (Half)1f), half.Frames[0][1, 0]);
        Assert.Equal(new RgbaHalf((Half)65504f, (Half)5f, (Half)3f, (Half)1f), half.Frames[0][0, 1]);
        Assert.True(Half.IsNegative(half.Frames[0][1, 1].R));
        Assert.Equal(ImageFormat.OpenExr, image.Metadata.SourceFormat);
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        Assert.Null(image.Metadata.Cicp);
        Assert.Null(image.Metadata.IccProfile);

        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.OpenExr, info.Format);
        Assert.Equal(new Size(2, 2), info.Size);
        Assert.Equal(PixelFormat.RgbaHalf, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Equal(16, info.BitsPerComponent);
        Assert.Equal(1, info.FrameCount);
        Assert.Equal(false, info.MayHaveTransparency);
        Assert.Equal(ImageFormat.OpenExr, Image.DetectFormat(data));
    }

    [Fact]
    public void PremultipliedColorsAreDividedByAlpha()
    {
        // HALF samples with alpha load as RgbaFloat: the quotients keep the precision that gives the halves back
        var data = ScanLineFile(4, 1, None, [HalfChannel("A"), HalfChannel("B"), HalfChannel("G"), HalfChannel("R")],
            [0.5f, 0f, 0.3330078f, 1f, 0.25f, 0.5f, 0.1f, 0f, 1f, 2f, 0.2f, 0.75f, 1.5f, 3f, 0.0999756f, 2f]);
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.RgbaFloat, info.PixelFormat);
        Assert.Equal(16, info.BitsPerComponent);
        Assert.Equal(ImageColorModel.Rgba, info.ColorModel);
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(new RgbaFloat(3f, 2f, 0.5f, 0.5f), image.Frames[0][0, 0]);

        // A pixel whose alpha is 0 keeps its color: the light a transparent object emits
        Assert.Equal(new RgbaFloat(3f, 2f, 0.5f, 0f), image.Frames[0][1, 0]);
        Assert.Equal((float)((double)(float)(Half)0.2f / (float)(Half)0.3330078f), image.Frames[0][2, 0].G);
        Assert.Equal((float)((double)(float)(Half)0.0999756f / (float)(Half)0.3330078f), image.Frames[0][2, 0].R);
        Assert.Equal(new RgbaFloat(2f, 0.75f, 0f, 1f), image.Frames[0][3, 0]);

        // Multiplied back by alpha and rounded to a half, each quotient is the stored half
        Assert.Equal((Half)0.0999756f, (Half)((double)image.Frames[0][2, 0].R * (float)(Half)0.3330078f));
    }

    [Fact]
    public void TheSamplesFollowTheOrderOfTheNamesWhateverTheOrderOfTheList()
    {
        // The list says R then G: the samples of a line are G then R
        var data = ScanLineFile(1, 1, None, [HalfChannel("R"), HalfChannel("G")], [2f, 3f]);
        using var image = Image.Load<RgbaHalf>(data);
        Assert.Equal(new RgbaHalf((Half)3f, (Half)2f, (Half)0f, (Half)1f), image.Frames[0][0, 0]);
    }

    [Fact]
    public void MissingColorsAreZeroAndAMissingAlphaIsOne()
    {
        using (var red = Image.Load<RgbaHalf>(ScanLineFile(1, 1, None, [HalfChannel("R")], [0.75f])))
        {
            Assert.Equal(new RgbaHalf((Half)0.75f, (Half)0f, (Half)0f, (Half)1f), red.Frames[0][0, 0]);
        }

        var gray = ScanLineFile(2, 1, None, [HalfChannel("Y")], [0.5f, 4f]);
        using (var image = Image.Load<RgbaHalf>(gray))
        {
            Assert.Equal(new RgbaHalf((Half)4f, (Half)4f, (Half)4f, (Half)1f), image.Frames[0][1, 0]);
        }

        Assert.Equal(ImageColorModel.Grayscale, Image.Identify(gray).ColorModel);
        Assert.Equal(false, Image.Identify(gray).MayHaveTransparency);
        Assert.Equal(ImageColorModel.GrayscaleAlpha, Image.Identify(ScanLineFile(1, 1, None, [HalfChannel("A"), HalfChannel("Y")], [0f, 1f])).ColorModel);
        Assert.Equal(ImageColorModel.Rgb, Image.Identify(ScanLineFile(1, 1, None, [HalfChannel("B"), HalfChannel("G"), HalfChannel("R")], [0f, 1f, 1f])).ColorModel);
    }

    [Fact]
    public void FloatAndUIntSamplesLoadAsRgbaFloat()
    {
        // B FLOAT, G UINT, R HALF: 32-bit samples need RgbaFloat; UINT samples become the number of the same value
        var line = Concat(ExrFileBuilder.Floats(-3.5f, float.PositiveInfinity), ExrFileBuilder.UInts(7, 16777218), ExrFileBuilder.Halves(0.5f, 2f));
        var data = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R"), ExrChannelDescription.UnsignedInt("G"), ExrChannelDescription.FloatingPoint("B")).AddScanLineChunk(0, line).Build();
        using var image = Image.Load(data);
        var full = Assert.IsType<Image<RgbaFloat>>(image);
        Assert.Equal(new RgbaFloat(0.5f, 7f, -3.5f, 1f), full.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(2f, 16777218f, float.PositiveInfinity, 1f), full.Frames[0][1, 0]);
        Assert.Equal(32, Image.Identify(data).BitsPerComponent);
        Assert.Equal(PixelFormat.RgbaFloat, Image.Identify(data).PixelFormat);

        // 2^24 + 1 has no float
        var inexact = ExrFileBuilder.ScanLines(1, 1, None, ExrChannelDescription.UnsignedInt("R")).AddScanLineChunk(0, ExrFileBuilder.UInts(16777217)).Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(inexact));
    }

    [Fact]
    public void TheDataWindowIsPlacedInTheDisplayWindow()
    {
        // Display window (-1, -1) - (2, 1): 4x3; data window (0, 0) - (1, 0)
        var data = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R"))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(-1, -1, 2, 1))
            .AddScanLineChunk(0, ExrFileBuilder.Halves(5f, 6f))
            .Build();
        using var image = Image.Load<RgbaHalf>(data);
        Assert.Equal(new Size(4, 3), image.Size);
        Assert.Equal(new RgbaHalf((Half)5f, (Half)0f, (Half)0f, (Half)1f), image.Frames[0][1, 1]);
        Assert.Equal(new RgbaHalf((Half)6f, (Half)0f, (Half)0f, (Half)1f), image.Frames[0][2, 1]);
        Assert.Equal(default, image.Frames[0][0, 0]);
        Assert.Equal(default, image.Frames[0][3, 2]);
        Assert.Equal(true, Image.Identify(data).MayHaveTransparency);

        // Pixels outside the display window would be lost
        var outside = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R")).Set("displayWindow", "box2i", ExrFileBuilder.Box(0, 0, 0, 0)).AddScanLineChunk(0, ExrFileBuilder.Halves(5f, 6f)).Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(outside));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(outside));
    }

    [Fact]
    public void ChunksAreReadInFileOrderWhateverTheirOrderInTheTable()
    {
        // ZIP: 16 lines per chunk; a chunk whose data have the size of its samples is stored as it is
        var builder = ExrFileBuilder.ScanLines(1, 20, Zip, HalfChannel("R"))
            .Set("lineOrder", "lineOrder", [1])
            .AddScanLineChunk(0, ExrFileBuilder.Halves([.. Enumerable.Range(0, 16).Select(i => (float)i)]))
            .AddScanLineChunk(16, ExrFileBuilder.Halves(16f, 17f, 18f, 19f));
        builder.FileOrder = [1, 0];
        builder.GapBefore[0] = 3;
        builder.Trailer = new byte[] { 1, 2, 3 };
        using var image = Image.Load<RgbaHalf>(builder.Build());
        for (var y = 0; y < 20; y++)
        {
            Assert.Equal((Half)y, image.Frames[0][0, y].R);
        }
    }

    [Fact]
    public void ChunksMustBeWhereTheTableSaysAndCarryTheirCoordinates()
    {
        Assert.Throws<InvalidImageContentException>(() => Image.Load(TwoChunks(builder => builder.Chunks[1] = new ExrChunk([0], builder.Chunks[1].Data))));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(TwoChunks(builder => builder.Offsets = [0, 100])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(TwoChunks(builder => builder.Offsets = [500, 500])));

        // The first chunk is one byte longer: the second one starts inside it
        Assert.Throws<InvalidImageContentException>(() => Image.Load(TwoChunks(builder => builder.Chunks[0] = new ExrChunk([0], [.. builder.Chunks[0].Data, 0]))));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(TwoChunks(builder => builder.Chunks[0] = new ExrChunk([0], builder.Chunks[0].Data[..^2]))));

        static byte[] TwoChunks(Action<ExrFileBuilder> change)
        {
            var builder = ExrFileBuilder.ScanLines(1, 2, None, HalfChannel("R")).AddScanLineChunk(0, ExrFileBuilder.Halves(1f)).AddScanLineChunk(1, ExrFileBuilder.Halves(2f));
            change(builder);
            return builder.Build();
        }
    }

    // The lenient rebuilds of the offset table are the ones OpenEXR 3.3.11 makes, through ImageMagick 7.1.2 and
    // exrmaketiled: the same pixels as the complete table. Apple ImageIO rejects every incomplete table

    public static TheoryData<string> RebuiltTables => ["zeros", "last zero", "middle zero", "before the table", "negative", "decreasing", "random", "tiles", "levels"];

    [Theory]
    [MemberData(nameof(RebuiltTables))]
    public void LenientDecodingRebuildsAnIncompleteOffsetTable(string name)
    {
        var (data, size) = name switch
        {
            "zeros" => (FourLines(builder => builder.Offsets = [0, 0, 0, 0]), new Size(2, 4)),
            "last zero" => (FourLines(builder => builder.Offsets = [.. Offsets(builder)[..3], 0]), new Size(2, 4)),
            "middle zero" => (FourLines(builder => builder.Offsets = [Offsets(builder)[0], 0, .. Offsets(builder)[2..]]), new Size(2, 4)),
            "before the table" => (FourLines(builder => builder.Offsets = [Offsets(builder)[0], 1, .. Offsets(builder)[2..]]), new Size(2, 4)),
            "negative" => (FourLines(builder => builder.Offsets = [Offsets(builder)[0], -5, .. Offsets(builder)[2..]]), new Size(2, 4)),
            "decreasing" => (FourLines(builder =>
            {
                builder.Set("lineOrder", "lineOrder", [1]);
                builder.FileOrder = [3, 2, 1, 0];
                builder.Offsets = [0, 0, 0, 0];
            }), new Size(2, 4)),
            "random" => (FourLines(builder =>
            {
                builder.Set("lineOrder", "lineOrder", [2]);
                builder.Offsets = [0, 0, 0, 0];
                builder.Trailer = new byte[] { 1, 2, 3 };
            }), new Size(2, 4)),
            "tiles" => (Tiles(builder =>
            {
                builder.FileOrder = [3, 1, 2, 0];
                builder.Offsets = [0, 0, 0, 0];
            }), new Size(4, 4)),
            _ => (MipmapLevels(builder =>
            {
                builder.FileOrder = [4, 2, 0, 3, 1];
                builder.Offsets = [0, 0, 0, 0, 0];
            }), new Size(3, 3)),
        };

        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));

        using (var image = Image.Load<RgbaHalf>(data, LenientOptions))
        {
            Assert.Equal(size, image.Size);
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    Assert.Equal(ExpectedSample(name, x, y), image.Frames[0][x, y].R);
                }
            }
        }

        var info = Image.Identify(data, new ImageIdentifyOptions { Configuration = LenientConfiguration, Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(size, info.Size);
        Assert.Equal(1, info.FrameCount);

        using var reader = Image.OpenReader<RgbaHalf>(new MemoryStream(data), new ImageReaderOptions { Configuration = LenientConfiguration });
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(ExpectedSample(name, size.Width - 1, size.Height - 1), frame.Frames[0][size.Width - 1, size.Height - 1].R);

        static Half ExpectedSample(string name, int x, int y) => name switch
        {
            "tiles" => (Half)((y * 4) + x),
            "levels" => (Half)((y * 3) + x),
            _ => (Half)((y + 1) * (x == 0 ? 1 : 10)),
        };
    }

    [Fact]
    public void LenientDecodingDoesNotRebuildATableFromMisplacedOrMissingChunks()
    {
        // OpenEXR rejects them too: scan lines out of their line order, a gap between two chunks, a missing or repeated
        // chunk, a truncated one, and valid entries that disagree with the chunks
        AssertRejectedInBothModes(FourLines(builder => (builder.FileOrder, builder.Offsets) = ([2, 0, 3, 1], [0, 0, 0, 0])));
        AssertRejectedInBothModes(FourLines(builder =>
        {
            builder.Set("lineOrder", "lineOrder", [1]);
            builder.Offsets = [0, 0, 0, 0];
        }));
        AssertRejectedInBothModes(FourLines(builder =>
        {
            builder.Set("lineOrder", "lineOrder", [2]);
            (builder.FileOrder, builder.Offsets) = ([2, 0, 3, 1], [0, 0, 0, 0]);
        }));
        AssertRejectedInBothModes(FourLines(builder => (builder.Offsets, builder.GapBefore[2]) = ([0, 0, 0, 0], 5)));
        AssertRejectedInBothModes(FourLines(builder => (builder.Offsets, builder.OffsetCount) = ([0, 0, 0, 0], 4), lineCount: 3));
        AssertRejectedInBothModes(FourLines(builder => (builder.Offsets, builder.Chunks[2]) = ([0, 0, 0, 0], new ExrChunk([1], ExrFileBuilder.Halves(7f, 7f)))));
        AssertRejectedInBothModes(FourLines(builder => builder.Offsets = [0, .. Offsets(builder)[1..2], Offsets(builder)[3], Offsets(builder)[2]]));
        AssertRejectedInBothModes(FourLines(builder =>
        {
            builder.FileOrder = [3, 2, 1, 0];
            builder.Offsets = [0, .. Offsets(builder)[1..]];
        }));
        AssertRejectedInBothModes(Tiles(builder => (builder.Chunks[2], builder.Offsets) = (builder.Chunks[1], [0, 0, 0, 0])));
        AssertRejectedInBothModes(FourLines(builder => builder.Offsets = [0, 0, 0, 0])[..^1]);

        // OpenEXR rebuilds a table whose entry is past the end of the file; the input is read forward without knowing its
        // length, so that entry is a truncated file, in both modes
        AssertRejectedInBothModes(FourLines(builder => builder.Offsets = [.. Offsets(builder)[..2], 100_000, Offsets(builder)[3]]));

        static void AssertRejectedInBothModes(byte[] data)
        {
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data, LenientOptions));
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = LenientConfiguration, Mode = ImageIdentifyMode.FullScan }));
        }
    }

    [Fact]
    public void LenientDecodingKeepsTheResourceLimits()
    {
        var data = FourLines(builder => builder.Offsets = [0, 0, 0, 0]);
        using (Image.Load(data, Lenient(new ImageResourceLimits { MaxEncodedBytes = data.Length, MaxFramePixels = 8 })))
        {
        }

        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Lenient(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Lenient(new ImageResourceLimits { MaxFramePixels = 7 }))).Kind);

        static ImageDecodeOptions Lenient(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient, Limits = limits } };
    }

    [Fact]
    public void TilesAreDecodedFromTheFirstLevelAndTheOtherLevelsAreSkipped()
    {
        // 3x3 in tiles of 2x2, MIPMAP_LEVELS (ROUND_DOWN): level 0 has 2x2 tiles, level 1 (1x1) one tile
        var builder = new ExrFileBuilder { VersionField = 2 | 0x200 };
        foreach (var attribute in ExrFileBuilder.ScanLines(3, 3, None, HalfChannel("R")).Attributes)
        {
            builder.Attributes.Add(attribute);
        }

        builder.Set("tiles", "tiledesc", ExrFileBuilder.TileDescription(2, 2, levelMode: 1, roundingMode: 0));
        builder.AddTile(0, 0, 0, 0, ExrFileBuilder.Halves(0f, 1f, 3f, 4f));
        builder.AddTile(1, 0, 0, 0, ExrFileBuilder.Halves(2f, 5f));
        builder.AddTile(0, 1, 0, 0, ExrFileBuilder.Halves(6f, 7f));
        builder.AddTile(1, 1, 0, 0, ExrFileBuilder.Halves(8f));
        builder.AddTile(0, 0, 1, 1, ExrFileBuilder.Halves(100f));
        builder.FileOrder = [3, 4, 1, 0, 2];
        var data = builder.Build();
        using (var image = Image.Load<RgbaHalf>(data))
        {
            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 3; x++)
                {
                    Assert.Equal((Half)((y * 3) + x), image.Frames[0][x, y].R);
                }
            }
        }

        Assert.Equal(1, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).FrameCount);

        // A tile of a level the image does not have, and the table entry of another tile
        builder.Chunks[4] = new ExrChunk([0, 0, 2, 2], ExrFileBuilder.Halves(100f));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
        builder.Chunks[4] = new ExrChunk([1, 1, 0, 0], ExrFileBuilder.Halves(100f));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
    }

    [Fact]
    public void RipmapLevelsAreOrderedByRowsOfLevels()
    {
        // 2x2 in tiles of 1x1, RIPMAP_LEVELS: levels (0, 0) 4 tiles, (1, 0) 2, (0, 1) 2, (1, 1) 1
        var builder = new ExrFileBuilder { VersionField = 2 | 0x200 };
        foreach (var attribute in ExrFileBuilder.ScanLines(2, 2, None, HalfChannel("R")).Attributes)
        {
            builder.Attributes.Add(attribute);
        }

        builder.Set("tiles", "tiledesc", ExrFileBuilder.TileDescription(1, 1, levelMode: 2, roundingMode: 0));
        builder.AddTile(0, 0, 0, 0, ExrFileBuilder.Halves(1f)).AddTile(1, 0, 0, 0, ExrFileBuilder.Halves(2f)).AddTile(0, 1, 0, 0, ExrFileBuilder.Halves(3f)).AddTile(1, 1, 0, 0, ExrFileBuilder.Halves(4f));
        builder.AddTile(0, 0, 1, 0, ExrFileBuilder.Halves(0f)).AddTile(0, 1, 1, 0, ExrFileBuilder.Halves(0f));
        builder.AddTile(0, 0, 0, 1, ExrFileBuilder.Halves(0f)).AddTile(1, 0, 0, 1, ExrFileBuilder.Halves(0f));
        builder.AddTile(0, 0, 1, 1, ExrFileBuilder.Halves(0f));
        using var image = Image.Load<RgbaHalf>(builder.Build());
        Assert.Equal((Half)4f, image.Frames[0][1, 1].R);
    }

    [Fact]
    public void ChromaticitiesLabelTheSamples()
    {
        using (var bt709 = Image.Load(WithChromaticities(0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, 0.3127f, 0.3290f)))
        {
            Assert.Equal(ColorTransferFunction.Linear, bt709.Metadata.TransferFunction);
            Assert.Null(bt709.Metadata.Cicp);
        }

        using (var bt2020 = Image.Load(WithChromaticities(0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f, 0.3127f, 0.3290f)))
        {
            Assert.Equal(new CicpColorDescription(9, 8, 0, fullRange: true), bt2020.Metadata.Cicp);
            Assert.Null(bt2020.Metadata.IccProfile);
        }

        using (var p3 = Image.Load(WithChromaticities(0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f, 0.314f, 0.351f)))
        {
            Assert.Equal(new CicpColorDescription(11, 8, 0, fullRange: true), p3.Metadata.Cicp);
        }

        // ACES (AP0) has no code point: a profile with linear curves describes it
        using (var aces = Image.Load(WithChromaticities(0.7347f, 0.2653f, 0f, 1f, 0.0001f, -0.077f, 0.32168f, 0.33767f)))
        {
            Assert.Null(aces.Metadata.Cicp);
            Assert.NotNull(aces.Metadata.IccProfile);
            Assert.Equal(IccProfileColorSpace.Rgb, aces.Metadata.IccProfile.ColorSpace);
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(WithChromaticities(0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0f)));

        static byte[] WithChromaticities(params float[] values)
            => ExrFileBuilder.ScanLines(1, 1, None, HalfChannel("R")).Set("chromaticities", "chromaticities", ExrFileBuilder.Floats(values)).AddScanLineChunk(0, ExrFileBuilder.Halves(1f)).Build();
    }

    [Fact]
    public void HeaderErrorsAreReportedByIdentification()
    {
        AssertInvalid(builder => builder.Remove("channels"));
        AssertInvalid(builder => builder.Remove("dataWindow"));
        AssertInvalid(builder => builder.Remove("lineOrder"));
        AssertInvalid(builder => builder.Attributes.Add(builder.Attributes[1]));
        AssertInvalid(builder => builder.Set("compression", "int", [0, 0, 0, 0]));
        AssertInvalid(builder => builder.Set("compression", "compression", [14]));
        AssertInvalid(builder => builder.Set("lineOrder", "lineOrder", [3]));
        AssertInvalid(builder => builder.Set("dataWindow", "box2i", ExrFileBuilder.Box(0, 0, -1, 0)));
        AssertInvalid(builder => builder.Set("dataWindow", "box2i", [0, 0, 0, 0]));
        AssertInvalid(builder => builder.Set("channels", "chlist", [0]));
        AssertInvalid(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(HalfChannel("R"), HalfChannel("R"))));
        AssertInvalid(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(new ExrChannelDescription("R", 3))));
        AssertInvalid(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(new ExrChannelDescription("R", 1, XSampling: 0))));
        AssertInvalid(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(new ExrChannelDescription("R", 1, YSampling: 2))));
        AssertInvalid(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(HalfChannel("R"))[..^1]));
        AssertInvalid(builder => builder.Set(new string('n', 32), "int", [0, 0, 0, 0]));
        AssertInvalid(builder => builder.Set("type", "string", ExrFileBuilder.Text("tiledimage")));
        AssertInvalid(builder => builder.Set("chunkCount", "int", ExrFileBuilder.Ints(2)));

        AssertUnsupported(builder => builder.Set("compression", "compression", [8]));
        AssertUnsupported(builder => builder.Set("compression", "compression", [13]));
        AssertUnsupported(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(HalfChannel("R"), HalfChannel("Z"))));
        AssertUnsupported(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(HalfChannel("diffuse.R"))));
        AssertUnsupported(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(HalfChannel("R"), HalfChannel("Y"))));
        AssertUnsupported(builder => builder.Set("channels", "chlist", ExrFileBuilder.ChannelList(HalfChannel("A"))));
        AssertUnsupported(builder => builder.Set("type", "string", ExrFileBuilder.Text("deepscanline")));
        AssertUnsupported(builder => builder.VersionField = 1);
        AssertUnsupported(builder => builder.VersionField = 2 | 0x800);
        AssertUnsupported(builder => builder.VersionField = 2 | 0x1000);
        AssertUnsupported(builder => builder.VersionField = 2 | 0x2000);

        // Long names need the flag; other attributes are skipped
        Assert.Equal(new Size(1, 1), Image.Identify(Build(builder => { builder.VersionField = 2 | 0x400; builder.Set(new string('n', 200), "int", [0, 0, 0, 0]); })).Size);
        Assert.Equal(new Size(1, 1), Image.Identify(Build(builder => builder.Set("comments", "string", ExrFileBuilder.Text("hello")))).Size);

        static void AssertInvalid(Action<ExrFileBuilder> change)
        {
            var data = Build(change);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        }

        static void AssertUnsupported(Action<ExrFileBuilder> change)
        {
            var data = Build(change);
            Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
            Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        }

        static byte[] Build(Action<ExrFileBuilder> change)
        {
            var builder = ExrFileBuilder.ScanLines(1, 1, None, HalfChannel("R")).AddScanLineChunk(0, ExrFileBuilder.Halves(1f));
            change(builder);
            return builder.Build();
        }
    }

    [Fact]
    public void AnUncompressedChunkMustHoldItsSamples()
    {
        var data = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R")).AddScanLineChunk(0, ExrFileBuilder.Halves(1f)).Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Equal(new Size(2, 1), Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Size);
    }

    [Fact]
    public void EveryProperPrefixIsAnError()
    {
        var data = ScanLineFile(3, 2, None, [HalfChannel("G"), HalfChannel("R")], [1f, 2f, 3f, 4f, 5f, 6f], [7f, 8f, 9f, 10f, 11f, 12f]);
        for (var length = 0; length < data.Length; length++)
        {
            var prefix = data[..length];
            var exception = Record.Exception(() => Image.Load(prefix));
            Assert.True(exception is InvalidImageContentException or UnknownImageFormatException, $"{length}: {exception}");
        }
    }

    [Fact]
    public void TypedLoadsConvertTheWholeImage()
    {
        var data = ScanLineFile(2, 1, None, [HalfChannel("G"), HalfChannel("R")], [0.5f, 1f, 0.25f, 4f]);

        // 4 is beyond the range of integers: rejected, clipped, or tone mapped with the peak of the image
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgba64>(data));
        using (var clipped = Image.Load<Rgba64>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } }))
        {
            Assert.Equal(new Rgba64(16384, 32768, 0, 65535), clipped.Frames[0][0, 0]);
            Assert.Equal(new Rgba64(65535, 65535, 0, 65535), clipped.Frames[0][1, 0]);
        }

        using (var toneMapped = Image.Load<Rgba64>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() } }))
        {
            // The peak of the image is its white, and the other components are scaled with the largest one
            var pixel = toneMapped.Frames[0][1, 0];
            Assert.Equal((ushort)65535, pixel.R);
            Assert.True(pixel.G is > 0 and < 65535);
        }

        using var full = Image.Load<RgbaFloat>(data);
        Assert.Equal(new RgbaFloat(4f, 1f, 0f, 1f), full.Frames[0][1, 0]);
        Assert.Equal(ColorTransferFunction.Linear, full.Metadata.TransferFunction);
    }

    [Fact]
    public void ReadersReadTheImage()
    {
        var data = ScanLineFile(2, 2, None, [HalfChannel("R")], [1f, 2f], [3f, 4f]);
        using var reader = Image.OpenReader<RgbaHalf>(new MemoryStream(data));
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal((Half)4f, frame.Frames[0][1, 1].R);
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void LimitsAreEnforced()
    {
        var data = ScanLineFile(3, 2, None, [HalfChannel("R")], [1f, 2f, 3f], [4f, 5f, 6f]);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxWidth = 2 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxHeight = 1 } } })).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        using (Image.Load(data, Limits(new ImageResourceLimits { MaxEncodedBytes = data.Length, MaxWidth = 3, MaxHeight = 2, MaxFramePixels = 6 })))
        {
        }

        // The attributes are metadata
        var commented = ExrFileBuilder.ScanLines(1, 1, None, HalfChannel("R")).Set("comments", "string", new byte[5000]).AddScanLineChunk(0, ExrFileBuilder.Halves(1f)).Build();
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(commented, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 4096 } } })).Kind);

        // A huge display window fails before anything is allocated
        var huge = ExrFileBuilder.ScanLines(1, 1, None, HalfChannel("R")).Set("displayWindow", "box2i", ExrFileBuilder.Box(int.MinValue, 0, int.MaxValue, 0)).AddScanLineChunk(0, ExrFileBuilder.Halves(1f)).Build();
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(huge)).Kind);

        static ImageDecodeOptions Limits(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    private static ExrChannelDescription HalfChannel(string name) => ExrChannelDescription.Half(name);

    private static readonly ImageConfiguration LenientConfiguration = new() { DecodingStrictness = DecodingStrictness.Lenient };

    private static readonly ImageDecodeOptions LenientOptions = new() { Configuration = LenientConfiguration };

    /// <summary>A 2x4 scan-line file, one line per chunk: the samples of the line y are y + 1 and 10 (y + 1).</summary>
    private static byte[] FourLines(Action<ExrFileBuilder> change, int lineCount = 4)
    {
        var builder = ExrFileBuilder.ScanLines(2, 4, None, HalfChannel("R"));
        for (var y = 0; y < lineCount; y++)
        {
            builder.AddScanLineChunk(y, ExrFileBuilder.Halves(y + 1, 10 * (y + 1)));
        }

        change(builder);
        return builder.Build();
    }

    /// <summary>A 4x4 file of 2x2 tiles of one level: the sample of the pixel (x, y) is 4y + x.</summary>
    private static byte[] Tiles(Action<ExrFileBuilder> change)
    {
        var builder = new ExrFileBuilder { VersionField = 2 | 0x200 };
        foreach (var attribute in ExrFileBuilder.ScanLines(4, 4, None, HalfChannel("R")).Attributes)
        {
            builder.Attributes.Add(attribute);
        }

        builder.Set("tiles", "tiledesc", ExrFileBuilder.TileDescription(2, 2, levelMode: 0, roundingMode: 0));
        for (var tileY = 0; tileY < 2; tileY++)
        {
            for (var tileX = 0; tileX < 2; tileX++)
            {
                var top = 2 * tileY;
                var left = 2 * tileX;
                builder.AddTile(tileX, tileY, 0, 0, ExrFileBuilder.Halves((4 * top) + left, (4 * top) + left + 1, (4 * (top + 1)) + left, (4 * (top + 1)) + left + 1));
            }
        }

        change(builder);
        return builder.Build();
    }

    /// <summary>A 3x3 file of 2x2 tiles with mipmap levels: the sample of the pixel (x, y) of the first level is 3y + x.</summary>
    private static byte[] MipmapLevels(Action<ExrFileBuilder> change)
    {
        var builder = new ExrFileBuilder { VersionField = 2 | 0x200 };
        foreach (var attribute in ExrFileBuilder.ScanLines(3, 3, None, HalfChannel("R")).Attributes)
        {
            builder.Attributes.Add(attribute);
        }

        builder.Set("tiles", "tiledesc", ExrFileBuilder.TileDescription(2, 2, levelMode: 1, roundingMode: 0));
        builder.AddTile(0, 0, 0, 0, ExrFileBuilder.Halves(0f, 1f, 3f, 4f));
        builder.AddTile(1, 0, 0, 0, ExrFileBuilder.Halves(2f, 5f));
        builder.AddTile(0, 1, 0, 0, ExrFileBuilder.Halves(6f, 7f));
        builder.AddTile(1, 1, 0, 0, ExrFileBuilder.Halves(8f));
        builder.AddTile(0, 0, 1, 1, ExrFileBuilder.Halves(100f));
        change(builder);
        return builder.Build();
    }

    /// <summary>Gets the offsets of the chunks of a builder, as its table would give them.</summary>
    private static long[] Offsets(ExrFileBuilder builder)
    {
        var data = builder.Build();
        var entries = builder.OffsetCount ?? builder.Chunks.Count;
        var tableStart = data.Length - builder.Chunks.Sum(static chunk => (4 * chunk.Coordinates.Count) + 4 + chunk.Data.Length) - builder.Trailer.Length - builder.GapBefore.Values.Sum() - (8 * entries);
        return [.. Enumerable.Range(0, entries).Select(i => BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableStart + (8 * i))))];
    }

    /// <summary>A scan-line file of HALF channels without compression: the samples of each line, channel after channel in name order.</summary>
    private static byte[] ScanLineFile(int width, int height, int compression, ExrChannelDescription[] channels, params float[][] lines)
    {
        var builder = ExrFileBuilder.ScanLines(width, height, compression, channels);
        for (var y = 0; y < lines.Length; y++)
        {
            builder.AddScanLineChunk(y, ExrFileBuilder.Halves(lines[y]));
        }

        return builder.Build();
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
