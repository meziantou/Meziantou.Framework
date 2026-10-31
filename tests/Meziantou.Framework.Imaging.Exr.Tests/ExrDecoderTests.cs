using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.Internals;
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
    private const int Rle = 1;
    private const int Zips = 2;
    private const int Zip = 3;
    private const int Piz = 4;
    private const int Pxr24 = 5;
    private const int B44 = 6;
    private const int B44A = 7;

    private static readonly byte[] B44NoDifferences = [0x02, 0x08, 0x20, 0x82, 0x08, 0x20, 0x82, 0x08, 0x20, 0x82, 0x08, 0x20];

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
    public void TheWindowsAreInTheMetadata()
    {
        var data = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R"))
            .Set("dataWindow", "box2i", ExrFileBuilder.Box(-3, 4, -2, 4))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(-5, 0, 8, 6))
            .AddScanLineChunk(4, ExrFileBuilder.Halves(5f, 6f))
            .Build();
        var expected = new OpenExrWindows(new Point(-3, 4), new Point(-2, 4), new Point(-5, 0), new Point(8, 6));
        using var image = Image.Load<RgbaHalf>(data);
        Assert.Equal(expected, image.Metadata.OpenExrWindows);
        Assert.Equal(expected, image.Metadata.Clone().OpenExrWindows);
        Assert.Equal(expected, Image.Identify(data).Metadata.OpenExrWindows);
        Assert.Equal((Half)6f, image.Frames[0][3, 4].R);

        // Informational: the encoder writes both windows from (0, 0)
        using var stream = new MemoryStream();
        image.Save(stream, new Formats.OpenExrEncoder());
        Assert.Equal(new OpenExrWindows(Point.Empty, new Point(13, 6), Point.Empty, new Point(13, 6)), Image.Identify(stream.ToArray()).Metadata.OpenExrWindows);

        Assert.Throws<ArgumentException>(() => new OpenExrWindows(new Point(1, 0), Point.Empty, Point.Empty, Point.Empty));
        Assert.Throws<ArgumentException>(() => new OpenExrWindows(Point.Empty, Point.Empty, Point.Empty, new Point(0, -1)));
    }

    [Fact]
    public void ALayerIsDecodedAndTheOtherChannelsAreIgnored()
    {
        // B, G, R, Z, diffuse.G, diffuse.R (in name order): a FLOAT depth, and the colors of a second layer
        var line = Concat(ExrFileBuilder.Halves(0.25f, 0.5f), ExrFileBuilder.Halves(1f, 2f), ExrFileBuilder.Halves(3f, 4f), ExrFileBuilder.Floats(100f, 200f), ExrFileBuilder.Halves(5f, 6f), ExrFileBuilder.Halves(7f, 8f));
        var data = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R"), HalfChannel("G"), HalfChannel("B"), ExrChannelDescription.FloatingPoint("Z"), HalfChannel("diffuse.R"), HalfChannel("diffuse.G"))
            .AddScanLineChunk(0, line)
            .Build();

        // Every channel is decoded by default: the depth and the layer would be lost
        Assert.Equal("Extra channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data)).Feature);
        Assert.Equal("Extra channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);

        // The default layer: HALF colors, the FLOAT depth is ignored
        var defaultLayer = LayerOptions(string.Empty);
        var info = Image.Identify(data, new ImageIdentifyOptions { Configuration = defaultLayer.Configuration });
        Assert.Equal(PixelFormat.RgbaHalf, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Equal(16, info.BitsPerComponent);
        using (var image = Image.Load(data, defaultLayer))
        {
            var half = Assert.IsType<Image<RgbaHalf>>(image);
            Assert.Equal(new RgbaHalf((Half)3f, (Half)1f, (Half)0.25f, (Half)1f), half.Frames[0][0, 0]);
            Assert.Equal(new RgbaHalf((Half)4f, (Half)2f, (Half)0.5f, (Half)1f), half.Frames[0][1, 0]);
        }

        // A named layer: its missing blue is 0
        using (var image = Image.Load<RgbaHalf>(data, LayerOptions("diffuse")))
        {
            Assert.Equal(new RgbaHalf((Half)7f, (Half)5f, (Half)0f, (Half)1f), image.Frames[0][0, 0]);
            Assert.Equal(new RgbaHalf((Half)8f, (Half)6f, (Half)0f, (Half)1f), image.Frames[0][1, 0]);
        }

        // A layer that is not in the file is never replaced by another one
        var missing = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data, LayerOptions("specular")));
        Assert.Equal("Missing layer", missing.Feature);
        Assert.Contains("the layer 'specular'", missing.Message, StringComparison.Ordinal);
        Assert.Contains("the default layer, the layer 'diffuse'", missing.Message, StringComparison.Ordinal);
        Assert.Equal("Missing layer", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = LayerOptions("diff").Configuration })).Feature);

        using var reader = Image.OpenReader<RgbaHalf>(new MemoryStream(data), new ImageReaderOptions { Configuration = LayerOptions("diffuse").Configuration });
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal((Half)8f, frame.Frames[0][1, 0].R);
    }

    [Fact]
    public void TheChannelsOfALayerMustDescribeItsColorsUnambiguously()
    {
        // Luminance and RGB in the same layer: one of them would be lost
        var mixed = ScanLineFile(1, 1, None, [HalfChannel("light.B"), HalfChannel("light.Y"), HalfChannel("R")], [1f, 2f, 3f]);
        Assert.Equal("Extra channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(mixed, LayerOptions("light"))).Feature);
        using (var image = Image.Load<RgbaHalf>(mixed, LayerOptions(string.Empty)))
        {
            // R comes first in name order
            Assert.Equal(new RgbaHalf((Half)1f, (Half)0f, (Half)0f, (Half)1f), image.Frames[0][0, 0]);
        }

        // The components of a vector, or CIE XYZ colors: Y is not a luminance there
        var normals = ScanLineFile(1, 1, None, [HalfChannel("R"), HalfChannel("N.X"), HalfChannel("N.Y"), HalfChannel("N.Z"), HalfChannel("Y"), HalfChannel("Z")], [1f, 2f, 3f, 4f, 5f, 6f]);
        Assert.Equal("Ambiguous layer", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(normals, LayerOptions("N"))).Feature);
        Assert.Equal("Extra channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(normals, LayerOptions(string.Empty))).Feature);
        var luminanceAndDepth = ScanLineFile(1, 1, None, [HalfChannel("Y"), HalfChannel("Z")], [5f, 6f]);
        using (var image = Image.Load<RgbaHalf>(luminanceAndDepth, LayerOptions(string.Empty)))
        {
            Assert.Equal(new RgbaHalf((Half)5f, (Half)5f, (Half)5f, (Half)1f), image.Frames[0][0, 0]);
        }

        // Alpha alone, in a layer whose other channels are not colors
        var alpha = ScanLineFile(1, 1, None, [HalfChannel("R"), HalfChannel("mask.A"), HalfChannel("mask.Z")], [0.5f, 1f, 2f]);
        Assert.Equal("Alpha-only image", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(alpha, LayerOptions("mask"))).Feature);

        // Chroma channels, sub-sampled: their colors cannot be decoded, and ignoring them would lose the colors
        var chroma = ExrFileBuilder.ScanLines(2, 2, None, HalfChannel("Y"), new ExrChannelDescription("RY", 1, XSampling: 2, YSampling: 2), new ExrChannelDescription("BY", 1, XSampling: 2, YSampling: 2))
            .AddScanLineChunk(0, Concat(ExrFileBuilder.Halves(0.1f), ExrFileBuilder.Halves(0.2f), ExrFileBuilder.Halves(1f, 2f)))
            .AddScanLineChunk(1, ExrFileBuilder.Halves(3f, 4f))
            .Build();
        Assert.Equal("Extra channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(chroma)).Feature);
        Assert.Equal("Luminance-chroma channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(chroma, new ImageIdentifyOptions { Configuration = LayerOptions(string.Empty).Configuration })).Feature);

        // A channel of the layer that is sub-sampled
        var subsampled = ExrFileBuilder.ScanLines(2, 1, None, new ExrChannelDescription("R", 1, XSampling: 2))
            .AddScanLineChunk(0, ExrFileBuilder.Halves(1f))
            .Build();
        Assert.Equal("Sub-sampled channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(subsampled, LayerOptions(string.Empty))).Feature);

        // Names are compared exactly: the samples of R come before those of r
        var lowercase = ScanLineFile(1, 1, None, [HalfChannel("r"), HalfChannel("R")], [1f, 2f]);
        using (var image = Image.Load<RgbaHalf>(lowercase, LayerOptions(string.Empty)))
        {
            Assert.Equal(new RgbaHalf((Half)1f, (Half)0f, (Half)0f, (Half)1f), image.Frames[0][0, 0]);
        }
    }

    [Fact]
    public void IgnoredChannelsMaySkipLinesOfAChunk()
    {
        // ZIP chunks of 16 lines, stored uncompressed: C is sampled on the even lines and columns only, so the odd lines
        // of the chunk hold only the samples of R
        var data = ExrFileBuilder.ScanLines(4, 4, Zip, new ExrChannelDescription("C", 1, XSampling: 2, YSampling: 2), HalfChannel("R"))
            .AddScanLineChunk(0, Concat(
                ExrFileBuilder.Halves(-1f, -2f), ExrFileBuilder.Halves(0f, 1f, 2f, 3f),
                ExrFileBuilder.Halves(10f, 11f, 12f, 13f),
                ExrFileBuilder.Halves(-3f, -4f), ExrFileBuilder.Halves(20f, 21f, 22f, 23f),
                ExrFileBuilder.Halves(30f, 31f, 32f, 33f)))
            .Build();
        Assert.Equal("Extra channels", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
        using var image = Image.Load<RgbaHalf>(data, LayerOptions(string.Empty));
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal((Half)((10 * y) + x), image.Frames[0][x, y].R);
            }
        }
    }

    [Fact]
    public void ADataWindowLargerThanTheDisplayWindowIsCroppedOrDecodedWhole()
    {
        // Data window (0, 0) - (3, 1), display window (1, 0) - (2, 1): an overscan of one column on each side
        var data = ExrFileBuilder.ScanLines(4, 2, None, HalfChannel("R"))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(1, 0, 2, 1))
            .AddScanLineChunk(0, ExrFileBuilder.Halves(0f, 1f, 2f, 3f))
            .AddScanLineChunk(1, ExrFileBuilder.Halves(10f, 11f, 12f, 13f))
            .Build();
        Assert.Equal("Pixels outside the display window", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);

        var crop = WindowOptions(OpenExrWindowMode.CropToDisplayWindow);
        var info = Image.Identify(data, new ImageIdentifyOptions { Configuration = crop.Configuration });
        Assert.Equal(new Size(2, 2), info.Size);
        Assert.Equal(false, info.MayHaveTransparency);
        using (var image = Image.Load<RgbaHalf>(data, crop))
        {
            Assert.Equal(new Size(2, 2), image.Size);
            Assert.Equal(new RgbaHalf((Half)1f, (Half)0f, (Half)0f, (Half)1f), image.Frames[0][0, 0]);
            Assert.Equal((Half)2f, image.Frames[0][1, 0].R);
            Assert.Equal((Half)11f, image.Frames[0][0, 1].R);
            Assert.Equal((Half)12f, image.Frames[0][1, 1].R);
            Assert.Equal(new OpenExrWindows(Point.Empty, new Point(3, 1), new Point(1, 0), new Point(2, 1)), image.Metadata.OpenExrWindows);
        }

        var whole = WindowOptions(OpenExrWindowMode.DataWindow);
        Assert.Equal(new Size(4, 2), Image.Identify(data, new ImageIdentifyOptions { Configuration = whole.Configuration }).Size);
        using (var image = Image.Load<RgbaHalf>(data, whole))
        {
            Assert.Equal(new Size(4, 2), image.Size);
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal((Half)x, image.Frames[0][x, 0].R);
                Assert.Equal((Half)(10 + x), image.Frames[0][x, 1].R);
            }
        }

        // The data window misses the display window entirely: the cropped image is transparent
        var apart = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R"))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(5, 5, 7, 6))
            .AddScanLineChunk(0, ExrFileBuilder.Halves(1f, 2f))
            .Build();
        Assert.Equal(true, Image.Identify(apart, new ImageIdentifyOptions { Configuration = crop.Configuration }).MayHaveTransparency);
        using (var image = Image.Load<RgbaHalf>(apart, crop))
        {
            Assert.Equal(new Size(3, 2), image.Size);
            Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(default, image.Frames[0][i % 3, i / 3]));
        }

        // The data window inside the display window, decoded alone
        var inside = ExrFileBuilder.ScanLines(2, 1, None, HalfChannel("R"))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(-1, -1, 2, 1))
            .AddScanLineChunk(0, ExrFileBuilder.Halves(5f, 6f))
            .Build();
        Assert.Equal(false, Image.Identify(inside, new ImageIdentifyOptions { Configuration = whole.Configuration }).MayHaveTransparency);
        using (var image = Image.Load<RgbaHalf>(inside, whole))
        {
            Assert.Equal(new Size(2, 1), image.Size);
            Assert.Equal((Half)6f, image.Frames[0][1, 0].R);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrDecodingOptions { WindowMode = (OpenExrWindowMode)3 });
        Assert.Throws<ArgumentNullException>(() => new ImageConfiguration { OpenExrDecoding = null! });
        Assert.Same(OpenExrDecodingOptions.Default, ImageConfiguration.Default.OpenExrDecoding);
        Assert.Null(OpenExrDecodingOptions.Default.Layer);
        Assert.Equal(OpenExrWindowMode.DisplayWindow, OpenExrDecodingOptions.Default.WindowMode);
    }

    [Fact]
    public void TilesAreCroppedToTheDisplayWindow()
    {
        // 4x4 pixels of 2x2 tiles; the display window (1, 1) - (2, 2) takes one pixel of each tile
        var data = Tiles(builder => builder.Set("displayWindow", "box2i", ExrFileBuilder.Box(1, 1, 2, 2)));
        Assert.Equal("Pixels outside the display window", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data)).Feature);
        using (var image = Image.Load<RgbaHalf>(data, WindowOptions(OpenExrWindowMode.CropToDisplayWindow)))
        {
            Assert.Equal(new Size(2, 2), image.Size);
            Assert.Equal((Half)5f, image.Frames[0][0, 0].R);
            Assert.Equal((Half)6f, image.Frames[0][1, 0].R);
            Assert.Equal((Half)9f, image.Frames[0][0, 1].R);
            Assert.Equal((Half)10f, image.Frames[0][1, 1].R);
        }

        using (var image = Image.Load<RgbaHalf>(data, WindowOptions(OpenExrWindowMode.DataWindow)))
        {
            Assert.Equal(new Size(4, 4), image.Size);
            Assert.Equal((Half)15f, image.Frames[0][3, 3].R);
        }
    }

    [Theory]
    [InlineData(Zip, false)]
    [InlineData(Piz, false)]
    [InlineData(Pxr24, true)]
    [InlineData(B44, true)]
    public void LayersAndCropsDecodedByWorkersHaveThePixelsOfOneWorker(int compression, bool tiled)
    {
        // Colors next to a FLOAT depth, with a display window inside the data window and across chunks and tiles
        var builder = WorkerFile(compression, tiled, [HalfChannel("B"), HalfChannel("G"), HalfChannel("R"), ExrChannelDescription.FloatingPoint("Z")]);
        var data = builder.Set("displayWindow", "box2i", ExrFileBuilder.Box(37, 21, 280, 230)).Build();
        var options = new OpenExrDecodingOptions { Layer = string.Empty, WindowMode = OpenExrWindowMode.CropToDisplayWindow };
        var expected = Decode(data, options, workers: 1);
        Assert.Equal(new Size(244, 210), expected.Size);
        Assert.Equal(expected.Pixels, Decode(data, options, workers: 16).Pixels);

        // The cropped pixels are those of the data window
        var whole = Decode(data, new OpenExrDecodingOptions { Layer = string.Empty, WindowMode = OpenExrWindowMode.DataWindow }, workers: 16);
        Assert.Equal(new Size(300, 251), whole.Size);
        for (var y = 0; y < expected.Size.Height; y++)
        {
            Assert.Equal(whole.Pixels.AsSpan((((y + 21) * 300) + 37) * 8, 244 * 8), expected.Pixels.AsSpan(y * 244 * 8, 244 * 8));
        }

        static (Size Size, byte[] Pixels) Decode(byte[] data, OpenExrDecodingOptions exr, int workers)
        {
            using var image = Image.Load<RgbaHalf>(new MemoryStream(data), new ImageDecodeOptions { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers, OpenExrDecoding = exr } });
            var bytes = new byte[image.Width * image.Height * 8];
            image.Frames[0].CopyPixelBytesTo(bytes);
            return (image.Size, bytes);
        }
    }

    [Fact]
    public void IgnoredChannelsCannotMakeAChunkLargerThanABuffer()
    {
        // ZIP chunks of 16 lines of 10,000 pixels: 3,500 FLOAT channels make 2.24 GB per chunk
        ExrChannelDescription[] channels = [HalfChannel("R"), .. Enumerable.Range(0, 3500).Select(i => ExrChannelDescription.FloatingPoint(string.Create(CultureInfo.InvariantCulture, $"aov.c{i:D4}")))];
        var data = ExrFileBuilder.ScanLines(10_000, 16, Zip, channels).Build();
        Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = LayerOptions(string.Empty).Configuration }));
    }

    [Fact]
    public void TheDataWindowCountsAgainstTheLimitsWhenItIsNotInsideTheImage()
    {
        // A 4x2 data window, a 2x2 display window
        var data = ExrFileBuilder.ScanLines(4, 2, None, HalfChannel("R"))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(1, 0, 2, 1))
            .AddScanLineChunk(0, ExrFileBuilder.Halves(0f, 1f, 2f, 3f))
            .AddScanLineChunk(1, ExrFileBuilder.Halves(10f, 11f, 12f, 13f))
            .Build();
        foreach (var mode in (OpenExrWindowMode[])[OpenExrWindowMode.CropToDisplayWindow, OpenExrWindowMode.DataWindow])
        {
            var atLimit = new ImageConfiguration { OpenExrDecoding = new OpenExrDecodingOptions { WindowMode = mode }, Limits = new ImageResourceLimits { MaxFramePixels = 8 } };
            using (Image.Load(data, new ImageDecodeOptions { Configuration = atLimit }))
            {
            }

            var overLimit = new ImageConfiguration { OpenExrDecoding = new OpenExrDecodingOptions { WindowMode = mode }, Limits = new ImageResourceLimits { MaxFramePixels = 7 } };
            Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = overLimit })).Kind);
            Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = overLimit })).Kind);
        }
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

    /// <summary>
    /// The chunks of an image of at least 65,536 pixels are decoded by several workers when the configuration allows it: the
    /// pixels are those of one worker for every compression, for scan lines and tiles (in another order than the table, with
    /// bytes between them) whose last chunks are partial, for half, float and uint channels with alpha, for half channels
    /// without alpha (written in the frame directly), for a typed load (written in a staging image) and for a stream.
    /// </summary>
    [Theory]
    [InlineData(None)]
    [InlineData(Rle)]
    [InlineData(Zips)]
    [InlineData(Zip)]
    [InlineData(Piz)]
    [InlineData(Pxr24)]
    [InlineData(B44)]
    [InlineData(B44A)]
    public void ChunksDecodedByWorkersHaveThePixelsOfOneWorker(int compression)
    {
        ExrChannelDescription[] mixed = [ExrChannelDescription.Half("A"), ExrChannelDescription.FloatingPoint("B"), ExrChannelDescription.UnsignedInt("G"), ExrChannelDescription.Half("R")];
        ExrChannelDescription[] colors = [ExrChannelDescription.Half("B"), ExrChannelDescription.Half("G"), ExrChannelDescription.Half("R")];
        foreach (var tiled in (bool[])[false, true])
        {
            foreach (var channels in (ExrChannelDescription[][])[mixed, colors])
            {
                var data = WorkerFile(compression, tiled, channels).Build();
                var expected = DecodePixels(data, workers: 1, fromStream: false);
                foreach (var workers in (int[])[2, 3, 16])
                {
                    Assert.Equal(expected, DecodePixels(data, workers, fromStream: false));
                    Assert.Equal(expected, DecodePixels(data, workers, fromStream: true));
                }
            }

            var typed = WorkerFile(compression, tiled, mixed).Build();
            Assert.Equal(DecodeClipped(typed, workers: 1), DecodeClipped(typed, workers: 16));
        }

        static byte[] DecodeClipped(byte[] data, int workers)
        {
            var options = new ImageDecodeOptions { Configuration = Workers(workers), Conversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } };
            using var image = Image.Load<Rgba64>(data, options);
            var bytes = new byte[image.Width * image.Height * 8];
            image.Frames[0].CopyPixelBytesTo(bytes);
            return bytes;
        }
    }

    [Fact]
    public void ChunksOfARebuiltOffsetTableAreDecodedByWorkersAsByOne()
    {
        // The lenient decoding finds the chunks by walking them, so the workers' batches cannot read ahead to the offset of a
        // later chunk: the pixels, and the error of the first chunk that fails, are still those of one worker
        ExrChannelDescription[] colors = [ExrChannelDescription.Half("B"), ExrChannelDescription.Half("G"), ExrChannelDescription.Half("R")];
        var garbage = Enumerable.Range(0, 1000).Select(i => (byte)(i * 37)).ToArray();
        foreach (var tiled in (bool[])[false, true])
        {
            var complete = WorkerFile(Zip, tiled, colors);
            complete.GapBefore.Clear();
            var expected = DecodePixels(complete.Build(), workers: 1, fromStream: false);
            complete.Offsets = new long[complete.Chunks.Count];
            var rebuilt = complete.Build();
            Assert.Throws<InvalidImageContentException>(() => Image.Load(rebuilt));

            var corrupt = WorkerFile(Zip, tiled, colors);
            corrupt.GapBefore.Clear();
            corrupt.Offsets = new long[corrupt.Chunks.Count];
            corrupt.Chunks[5] = new ExrChunk(corrupt.Chunks[5].Coordinates, garbage);
            var corrupted = corrupt.Build();
            var error = Assert.Throws<InvalidImageContentException>(() => Load(corrupted, workers: 1, fromStream: false)).Message;
            foreach (var workers in (int[])[1, 2, 16])
            {
                foreach (var fromStream in (bool[])[false, true])
                {
                    using (var image = Load(rebuilt, workers, fromStream))
                    {
                        var bytes = new byte[expected.Length];
                        image.Frames[0].CopyPixelBytesTo(bytes);
                        Assert.Equal(expected, bytes);
                    }

                    Assert.Equal(error, Assert.Throws<InvalidImageContentException>(() => Load(corrupted, workers, fromStream)).Message);
                }
            }
        }

        static Image Load(byte[] data, int workers, bool fromStream)
        {
            var options = new ImageDecodeOptions { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers, DecodingStrictness = DecodingStrictness.Lenient } };
            return fromStream ? Image.Load(new MemoryStream(data), options) : Image.Load(data, options);
        }
    }

    /// <summary>
    /// The error of a file decoded by workers is the one of one worker: the error of the first chunk of the file that fails,
    /// whether a later chunk fails another way, has the coordinates of another chunk, or the input ends.
    /// </summary>
    [Fact]
    public void WorkersReportTheErrorOfTheFirstChunkOfTheFileThatFails()
    {
        // 16 ZIP chunks: garbage, data that produce too few bytes, a UINT sample that a float cannot hold
        var garbage = Enumerable.Range(0, 1000).Select(i => (byte)(i * 37)).ToArray();
        var short3 = Zlib(new byte[10]);
        ExrChannelDescription[] colors = [ExrChannelDescription.Half("B"), ExrChannelDescription.Half("G"), ExrChannelDescription.Half("R")];
        AssertSameError(Corrupt(colors, builder => SetData(builder, 5, garbage)), typeof(InvalidImageContentException));
        AssertSameError(Corrupt(colors, builder => SetData(SetData(builder, 3, short3), 9, garbage)), typeof(InvalidImageContentException), "produce 10 bytes");
        AssertSameError(Corrupt(colors, builder => SetData(SetData(builder, 3, garbage), 9, short3)), typeof(InvalidImageContentException), "malformed");
        AssertSameError(Corrupt(colors, builder => SetData(builder, 3, short3).Chunks[12] = new ExrChunk([0], builder.Chunks[12].Data)), typeof(InvalidImageContentException), "produce 10 bytes");
        AssertSameError(Corrupt(colors, builder => builder.Chunks[12] = new ExrChunk([0], builder.Chunks[12].Data)), typeof(InvalidImageContentException), "is the chunk 0");

        // The order of the file: the last chunk of the table is the first one read
        AssertSameError(
            Corrupt(colors, builder =>
            {
                builder.FileOrder = [.. Enumerable.Range(0, builder.Chunks.Count).Reverse()];
                SetData(SetData(builder, 3, garbage), 12, short3);
            }),
            typeof(InvalidImageContentException),
            "produce 10 bytes");

        // A sample a worker cannot convert
        ExrChannelDescription[] mixed = [ExrChannelDescription.Half("A"), ExrChannelDescription.FloatingPoint("B"), ExrChannelDescription.UnsignedInt("G"), ExrChannelDescription.Half("R")];
        var large = WorkerFile(Zip, tiled: false, mixed, uintSample: (x, y) => x == 7 && y == 40 ? (1u << 24) + 1 : (uint)(x + y));
        AssertSameError(large.Build(), typeof(UnsupportedImageFeatureException));
        AssertSameError(SetData(large, 6, garbage).Build(), typeof(UnsupportedImageFeatureException));
        AssertSameError(SetData(large, 1, garbage).Build(), typeof(InvalidImageContentException));

        // An input that ends in the middle of the chunks, with or without a chunk that fails before
        var data = WorkerFile(Zip, tiled: false, colors).Build();
        AssertSameError(data[..(data.Length / 2)], typeof(InvalidImageContentException));
        var corrupt = Corrupt(colors, builder => SetData(builder, 4, short3));
        AssertSameError(corrupt[..(corrupt.Length * 3 / 4)], typeof(InvalidImageContentException), "produce 10 bytes");

        static byte[] Corrupt(ExrChannelDescription[] channels, Action<ExrFileBuilder> change)
        {
            var builder = WorkerFile(Zip, tiled: false, channels);
            change(builder);
            return builder.Build();
        }

        static ExrFileBuilder SetData(ExrFileBuilder builder, int chunk, byte[] data)
        {
            builder.Chunks[chunk] = new ExrChunk(builder.Chunks[chunk].Coordinates, data);
            return builder;
        }
    }

    /// <summary>
    /// The workers stop when the decoding is canceled, and the limits fail a decoding by workers as they fail one worker: the
    /// memory of the frame, and the encoded bytes (after the error of a chunk before the limit).
    /// </summary>
    [Fact]
    public async Task WorkersHonorCancellationAndLimits()
    {
        ExrChannelDescription[] colors = [ExrChannelDescription.Half("B"), ExrChannelDescription.Half("G"), ExrChannelDescription.Half("R")];
        var data = WorkerFile(Piz, tiled: false, colors).Build();
        foreach (var workers in (int[])[1, 16])
        {
            await AssertCanceled(data, workers);

            var memory = new ImageConfiguration { MaxDegreeOfParallelism = workers, Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 512 * 1024 } };
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = memory })).Kind);

            var encodedBytes = new ImageConfiguration { MaxDegreeOfParallelism = workers, Limits = new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 } };
            Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = encodedBytes })).Kind);
            Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(new MemoryStream(data), new ImageDecodeOptions { Configuration = encodedBytes })).Kind);

            var builder = WorkerFile(Piz, tiled: false, colors);
            builder.Chunks[1] = new ExrChunk(builder.Chunks[1].Coordinates, builder.Chunks[1].Data[..^1]);
            var corrupt = builder.Build();
            var limited = new ImageConfiguration { MaxDegreeOfParallelism = workers, Limits = new ImageResourceLimits { MaxEncodedBytes = corrupt.Length - 1 } };
            Assert.Throws<InvalidImageContentException>(() => Image.Load(corrupt, new ImageDecodeOptions { Configuration = limited }));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(new MemoryStream(corrupt), new ImageDecodeOptions { Configuration = limited }));
        }

        static async Task AssertCanceled(byte[] data, int workers)
        {
            using var cancellation = new CancellationTokenSource();
            using var stream = new CancelingStream(data, data.Length / 2, cancellation);
            await Assert.ThrowsAny<OperationCanceledException>(() => Image.LoadAsync(stream, new ImageDecodeOptions { Configuration = Workers(workers) }, cancellation.Token));
        }
    }

    private static ExrChannelDescription HalfChannel(string name) => ExrChannelDescription.Half(name);

    private static ImageDecodeOptions LayerOptions(string layer) => new() { Configuration = new ImageConfiguration { OpenExrDecoding = new OpenExrDecodingOptions { Layer = layer } } };

    private static ImageDecodeOptions WindowOptions(OpenExrWindowMode mode) => new() { Configuration = new ImageConfiguration { OpenExrDecoding = new OpenExrDecodingOptions { WindowMode = mode } } };

    private static ImageConfiguration Workers(int workers) => new() { MaxDegreeOfParallelism = workers };

    private static byte[] DecodePixels(byte[] data, int workers, bool fromStream)
    {
        var options = new ImageDecodeOptions { Configuration = Workers(workers) };
        using var image = fromStream ? Image.Load(new MemoryStream(data), options) : Image.Load(data, options);
        var bytes = new byte[image.Width * image.Height * (image is Image<RgbaHalf> ? 8 : 16)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    /// <summary>Checks that one worker and 16 report the same error, from a span and from a stream.</summary>
    private static void AssertSameError(byte[] data, Type type, string? message = null)
    {
        var expected = Assert.ThrowsAny<Exception>(() => Image.Load(data, new ImageDecodeOptions { Configuration = Workers(1) }));
        Assert.Equal(type, expected.GetType());
        if (message is not null)
        {
            Assert.Contains(message, expected.Message, StringComparison.Ordinal);
        }

        foreach (var fromStream in (bool[])[false, true])
        {
            foreach (var workers in (int[])[1, 16])
            {
                var actual = Assert.ThrowsAny<Exception>(() => DecodePixels(data, workers, fromStream));
                Assert.Equal(expected.GetType(), actual.GetType());
                Assert.Equal(expected.Message, actual.Message);
            }
        }
    }

    /// <summary>
    /// A 300x251 file (75,300 pixels, more than several workers need) of the given channels, whose last chunks are partial:
    /// scan lines, or tiles of 64x48 written in the reverse order of the table with bytes between them. The chunks are
    /// compressed by the codecs of the library (PXR24 and B44 by the encoders of this test, B44 with blocks of one value).
    /// </summary>
    private static ExrFileBuilder WorkerFile(int compression, bool tiled, ExrChannelDescription[] channels, Func<int, int, uint>? uintSample = null)
    {
        const int Width = 300;
        const int Height = 251;
        const int TileWidth = 64;
        const int TileHeight = 48;
        var builder = ExrFileBuilder.ScanLines(Width, Height, compression, channels);
        var layoutChannels = channels.Select(channel => new ExrChannel(channel.Name, (ExrPixelType)channel.PixelType, PLinear: false, XSampling: 1, YSampling: 1)).ToArray();
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr worker file");
        using var piz = new ExrPiz(scope, Width * 32 * channels.Length * 4, encoder: true);
        if (tiled)
        {
            builder.VersionField = 2 | 0x200;
            builder.Set("tiles", "tiledesc", ExrFileBuilder.TileDescription(TileWidth, TileHeight, levelMode: 0, roundingMode: 0));
            var tilesX = (Width + TileWidth - 1) / TileWidth;
            var tilesY = (Height + TileHeight - 1) / TileHeight;
            for (var ty = 0; ty < tilesY; ty++)
            {
                for (var tx = 0; tx < tilesX; tx++)
                {
                    var width = Math.Min(TileWidth, Width - (tx * TileWidth));
                    var height = Math.Min(TileHeight, Height - (ty * TileHeight));
                    var layout = new ExrChunkLayout(layoutChannels, width, 0, height);
                    builder.AddTile(tx, ty, 0, 0, Compress(compression, layout, Samples(tx * TileWidth, ty * TileHeight, width, height), piz));
                }
            }

            builder.FileOrder = [.. Enumerable.Range(0, builder.Chunks.Count).Reverse()];
            builder.GapBefore[3] = 5;
            builder.Trailer = new byte[] { 1, 2, 3 };
            return builder;
        }

        var linesPerChunk = compression switch
        {
            Zip or Pxr24 => 16,
            Piz or B44 or B44A => 32,
            _ => 1,
        };

        for (var y = 0; y < Height; y += linesPerChunk)
        {
            var lines = Math.Min(linesPerChunk, Height - y);
            var layout = new ExrChunkLayout(layoutChannels, Width, y, lines);
            builder.AddScanLineChunk(y, Compress(compression, layout, Samples(0, y, Width, lines), piz));
        }

        return builder;

        // For each line, the samples of each channel in name order: halves up to 4, floats, and integers
        byte[] Samples(int x0, int y0, int width, int lines)
        {
            using var stream = new MemoryStream();
            Span<byte> sample = stackalloc byte[4];
            for (var y = y0; y < y0 + lines; y++)
            {
                for (var c = 0; c < channels.Length; c++)
                {
                    for (var x = x0; x < x0 + width; x++)
                    {
                        var hash = (uint)((x * 73856093) ^ (y * 19349663) ^ (c * 83492791)) * 2654435761u;
                        switch (channels[c].PixelType)
                        {
                            case 1:
                                BinaryPrimitives.WriteUInt16LittleEndian(sample, BitConverter.HalfToUInt16Bits((Half)((hash >> 20) / 1024f)));
                                stream.Write(sample[..2]);
                                break;

                            case 2:
                                BinaryPrimitives.WriteSingleLittleEndian(sample, ((hash >> 8) / 65536f) - 100f);
                                stream.Write(sample);
                                break;

                            default:
                                BinaryPrimitives.WriteUInt32LittleEndian(sample, uintSample?.Invoke(x, y) ?? (hash >> 12));
                                stream.Write(sample);
                                break;
                        }
                    }
                }
            }

            return stream.ToArray();
        }
    }

    /// <summary>Compresses the uncompressed data of a chunk; data that do not shrink are stored as they are.</summary>
    private static byte[] Compress(int compression, in ExrChunkLayout layout, byte[] data, ExrPiz piz)
    {
        var stored = compression switch
        {
            Rle => RunLengths(data),
            Zips or Zip => Zlib(Predicted(data)),
            Piz => Pizzed(data, layout, piz),
            Pxr24 => Zlib(Pxr24Planes(data, layout)),
            B44 or B44A => B44Blocks(data, layout, compression == B44A),
            _ => data,
        };

        return stored.Length >= data.Length ? data : stored;

        static byte[] Predicted(byte[] data)
        {
            var predicted = new byte[data.Length];
            ExrPredictor.Encode(data, predicted);
            return predicted;
        }

        static byte[] RunLengths(byte[] data)
        {
            var stored = new byte[data.Length];
            var length = ExrRle.Encode(Predicted(data), stored);
            return length < 0 ? data : stored[..length];
        }

        static byte[] Pizzed(byte[] data, in ExrChunkLayout layout, ExrPiz piz)
        {
            var stored = new byte[(data.Length * 2) + 100_000];
            return stored[..piz.Compress(data, stored, layout)];
        }
    }

    /// <summary>
    /// The byte planes of PXR24 (compression notes, section 6): for each line and channel, the differences of the samples
    /// (floats reduced to their 24 high bits), most significant byte plane first.
    /// </summary>
    private static byte[] Pxr24Planes(byte[] data, in ExrChunkLayout layout)
    {
        var planes = new byte[ExrPxr24.GetInflatedLength(layout)];
        var input = 0;
        var output = 0;
        for (var line = 0; line < layout.Lines; line++)
        {
            for (var c = 0; c < layout.Channels.Count; c++)
            {
                var n = layout.Width;
                var type = layout.Channels[c].PixelType;
                var bytes = type switch
                {
                    ExrPixelType.Half => 2,
                    ExrPixelType.UInt => 4,
                    _ => 3,
                };

                uint previous = 0;
                for (var i = 0; i < n; i++)
                {
                    var value = type == ExrPixelType.Half ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(input + (2 * i))) : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(input + (4 * i)));
                    if (type == ExrPixelType.Float)
                    {
                        value >>= 8;
                    }

                    var difference = value - previous;
                    previous = value;
                    for (var b = 0; b < bytes; b++)
                    {
                        planes[output + (b * n) + i] = (byte)(difference >> (8 * (bytes - 1 - b)));
                    }
                }

                input += n * (type == ExrPixelType.Half ? 2 : 4);
                output += n * bytes;
            }
        }

        return planes;
    }

    /// <summary>
    /// B44 data (compression notes, section 7) whose blocks of 4x4 half samples are one value, the first sample of the
    /// block: 14-byte blocks with no difference, or the 3-byte form of B44A. Other channels are stored as their planes.
    /// </summary>
    private static byte[] B44Blocks(byte[] data, in ExrChunkLayout layout, bool flat)
    {
        var planes = new byte[data.Length];
        var position = 0;
        var offset = 0;
        for (var c = 0; c < layout.Channels.Count; c++)
        {
            var size = layout.Channels[c].BytesPerSample;
            var lineLength = data.Length / layout.Lines;
            for (var line = 0; line < layout.Lines; line++)
            {
                data.AsSpan((line * lineLength) + offset, layout.Width * size).CopyTo(planes.AsSpan(position));
                position += layout.Width * size;
            }

            offset += layout.Width * size;
        }

        using var stream = new MemoryStream();
        position = 0;
        foreach (var channel in layout.Channels)
        {
            if (channel.PixelType != ExrPixelType.Half)
            {
                stream.Write(planes, position, 4 * layout.Width * layout.Lines);
                position += 4 * layout.Width * layout.Lines;
                continue;
            }

            for (var by = 0; by < layout.Lines; by += 4)
            {
                for (var bx = 0; bx < layout.Width; bx += 4)
                {
                    var key = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(planes.AsSpan(position + (2 * ((by * layout.Width) + bx)))) | 0x8000);
                    stream.WriteByte((byte)(key >> 8));
                    stream.WriteByte((byte)key);
                    if (flat)
                    {
                        stream.WriteByte(0xFC);
                    }
                    else
                    {
                        // The shift 0, then 15 differences of 0 (32), 6 bits each
                        stream.Write(B44NoDifferences);
                    }
                }
            }

            position += 2 * layout.Width * layout.Lines;
        }

        return stream.ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return stream.ToArray();
    }

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

    /// <summary>A stream that cancels a token once it has given a number of bytes.</summary>
    private sealed class CancelingStream(byte[] data, int threshold, CancellationTokenSource cancellation) : MemoryStream(data, writable: false)
    {
        // The span and asynchronous reads of a derived memory stream end in this one
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (Position >= threshold)
            {
                cancellation.Cancel();
            }

            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer, offset, count));
    }
}
