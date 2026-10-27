using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Histograms, global and adaptive histogram equalization, and auto-levels. The expectations are hand-computed from the
/// definitions of the mappings, or come from independent implementations: the outputs of OpenCV 4.10.0
/// (<c>cv2.equalizeHist</c>, <c>cv2.createCLAHE</c>) and scikit-image 0.24.0 (<c>exposure.equalize_adapthist</c>) on
/// <see cref="CreatePattern8"/> and <see cref="CreatePattern16"/>, kept as literals (little-endian samples for 16 bits). They were
/// produced in a <c>python:3.12-slim</c> container with <c>opencv-python-headless==4.10.0.84</c>,
/// <c>scikit-image==0.24.0</c> and <c>numpy==2.0.2</c>.
/// </summary>
public sealed class HistogramTests
{
    private const int PatternWidth = 64;
    private const int PatternHeight = 48;

    // cv2.equalizeHist(pattern8)
    private const string OpenCvEqualized8 = "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";

    // cv2.createCLAHE(clipLimit=3.0, tileGridSize=(4, 3)).apply(pattern8): 16 x 16 tiles
    private const string OpenCvClahe8 = "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";

    // cv2.createCLAHE(clipLimit=3.0, tileGridSize=(4, 3)).apply(pattern16), 65,536 bins
    private const string OpenCvClahe16 = "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";

    // np.round(exposure.equalize_adapthist(pattern8, kernel_size=(16, 16), clip_limit=3/256, nbins=256) * 255)
    private const string ScikitImageClahe8 = "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";

    // The documented definition of the adaptive mode, written independently in Python (NumPy for the histograms, a
    // loop over the pixels for the interpolation): 4 x 3 tiles of 16 x 16 pixels, ClipLimit 3, pixel centers
    private const string ModelClahe8 = "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";

    // The same, on pattern16 with 65,536 bins
    private const string ModelClahe16 = "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";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -----------------------------------------------------------------------------------------------------------------
    // Histograms
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ComputeHistogramCountsTheStoredLevelsOfEachChannel()
    {
        using var image = new Image<Rgba32>(2, 2);
        image.Frames[0][0, 0] = new Rgba32(10, 20, 30, 255);
        image.Frames[0][1, 0] = new Rgba32(10, 200, 30, 0);
        image.Frames[0][0, 1] = new Rgba32(255, 255, 255, 128);
        image.Frames[0][1, 1] = new Rgba32(0, 0, 0, 255);

        var red = image.ComputeHistogram(HistogramChannel.Red, Ct);
        Assert.Equal(HistogramChannel.Red, red.Channel);
        Assert.Equal(256, red.BinCount);
        Assert.Equal(4, red.TotalCount);
        Assert.Equal(2, red[10]);
        Assert.Equal(1, red[255]);
        Assert.Equal(1, red[0]);
        Assert.Equal(2, image.ComputeHistogram(HistogramChannel.Blue, Ct)[30]);
        Assert.Equal(1, image.ComputeHistogram(HistogramChannel.Green, Ct)[200]);

        // Fully transparent pixels are counted
        var alpha = image.ComputeHistogram(HistogramChannel.Alpha, Ct);
        Assert.Equal(2, alpha[255]);
        Assert.Equal(1, alpha[128]);
        Assert.Equal(1, alpha[0]);

        // (2126 * 10 + 7152 * 20 + 722 * 30 + 5000) / 10000 = 19; (2126 * 10 + 7152 * 200 + 722 * 30 + 5000) / 10000 = 147
        var luminance = image.ComputeHistogram(HistogramChannel.Luminance, Ct);
        Assert.Equal(1, luminance[19]);
        Assert.Equal(1, luminance[147]);
        Assert.Equal(1, luminance[255]);
        Assert.Equal(1, luminance[0]);
        Assert.Equal(4, luminance.Counts.ToArray().Sum());
        Assert.Throws<ArgumentOutOfRangeException>(() => luminance[256]);
        Assert.Throws<ArgumentOutOfRangeException>(() => luminance[-1]);
    }

    [Fact]
    public void ComputeHistogramHasOneBinPerLevelOf16BitSamples()
    {
        using var image = new Image<Gray16>(3, 1);
        image.Frames[0][0, 0] = new Gray16(1);
        image.Frames[0][1, 0] = new Gray16(1);
        image.Frames[0][2, 0] = new Gray16(65534);
        foreach (var channel in new[] { HistogramChannel.Luminance, HistogramChannel.Red, HistogramChannel.Green, HistogramChannel.Blue })
        {
            var histogram = image.ComputeHistogram(channel, Ct);
            Assert.Equal(65536, histogram.BinCount);
            Assert.Equal(2, histogram[1]);
            Assert.Equal(1, histogram[65534]);
            Assert.Equal(0, histogram[0]);
        }

        // Gray formats are opaque
        Assert.Equal(3, image.ComputeHistogram(HistogramChannel.Alpha, Ct)[65535]);

        using var rgb = new Image<Rgb24>(2, 1, new Rgb24(1, 2, 3));
        Assert.Equal(2, rgb.ComputeHistogram(HistogramChannel.Alpha, Ct)[255]);
    }

    [Fact]
    public void ComputeHistogramOfAnImageCountsEveryFrameAndThePoster()
    {
        using var image = new Image<Gray8>(2, 1, new Gray8(5));
        var second = image.AppendFrame();
        second[0, 0] = new Gray8(9);
        second[1, 0] = new Gray8(5);
        var poster = (ImageFrame<Gray8>)image.SetPosterFrame(image.Frames[0]);
        poster[1, 0] = new Gray8(7);

        var histogram = image.ComputeHistogram(HistogramChannel.Luminance, Ct);
        Assert.Equal(6, histogram.TotalCount);
        Assert.Equal(1, histogram[9]);
        Assert.Equal(1, histogram[7]);
        Assert.Equal(4, histogram[5]);

        var frame = second.ComputeHistogram(HistogramChannel.Luminance, Ct);
        Assert.Equal(2, frame.TotalCount);
        Assert.Equal(1, frame[9]);
        Assert.Equal(1, frame[5]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Global equalization
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void GlobalEqualizationIsTheTextbookMapping()
    {
        // Levels 52 (1), 55 (2), 61 (1), 79 (3), 85 (1): cdf 1, 3, 4, 7, 8; cdf_min = 1, N - cdf_min = 7
        // 52 -> 0, 55 -> round(2 * 255 / 7) = 73, 61 -> round(3 * 255 / 7) = 109, 79 -> round(6 * 255 / 7) = 219, 85 -> 255
        using var image = Gray8Row(52, 55, 55, 61, 79, 79, 79, 85);
        image.Equalize(cancellationToken: Ct);
        Assert.Equal([0, 73, 73, 109, 219, 219, 219, 255], ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void GlobalEqualizationOf16BitSamplesKeepsTheirPrecision()
    {
        // cdf 2, 3, 4 with cdf_min = 2: 30000 -> round(1 * 65535 / 2) = 32768 (ties upward), 65000 -> 65535
        using var image = new Image<Gray16>(4, 1);
        ushort[] values = [1000, 1000, 30000, 65000];
        for (var x = 0; x < values.Length; x++)
        {
            image.Frames[0][x, 0] = new Gray16(values[x]);
        }

        image.Equalize(cancellationToken: Ct);
        Assert.Equal(0, image.Frames[0][0, 0].Value);
        Assert.Equal(0, image.Frames[0][1, 0].Value);
        Assert.Equal(32768, image.Frames[0][2, 0].Value);
        Assert.Equal(65535, image.Frames[0][3, 0].Value);
    }

    [Fact]
    public void GlobalEqualizationMatchesOpenCv()
    {
        using var image = CreatePattern8();
        image.Equalize(cancellationToken: Ct);
        Assert.Equal(Convert.FromBase64String(OpenCvEqualized8), ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void EqualizationOfASingleLevelChangesNothing()
    {
        using var image = Gray8Row(40, 40, 40);
        image.Equalize(cancellationToken: Ct);
        Assert.Equal([40, 40, 40], ReadGray8(image.Frames[0]));
        image.AutoLevels(cancellationToken: Ct);
        Assert.Equal([40, 40, 40], ReadGray8(image.Frames[0]));

        // Only transparent pixels: nothing is counted
        using var transparent = new Image<Rgba32>(2, 1, new Rgba32(10, 20, 30, 0));
        transparent.Equalize(cancellationToken: Ct);
        transparent.AutoLevels(cancellationToken: Ct);
        Assert.Equal(new Rgba32(10, 20, 30, 0), transparent.Frames[0][0, 0]);
    }

    [Fact]
    public void LuminanceEqualizationAddsTheChangeOfLumaToEveryColor()
    {
        // Lumas: (2126 * 100 + 7152 * 50 + 722 * 20 + 5000) / 10000 = 58 and (2126 * 200 + 7152 * 210 + 722 * 250 + 5000) / 10000 = 211
        // Two levels with one pixel each: 58 -> 0, 211 -> 255
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(100, 50, 20, 200);
        image.Frames[0][1, 0] = new Rgba32(200, 210, 250, 255);
        image.Equalize(cancellationToken: Ct);

        // -58 on each color, clamped to 0; +44 on each color, clamped to 255; alpha is kept
        Assert.Equal(new Rgba32(42, 0, 0, 200), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(244, 254, 255, 255), image.Frames[0][1, 0]);
    }

    [Fact]
    public void LuminanceEqualizationOfGrayColorsIsTheEqualizationOfTheGrayImage()
    {
        using var gray = CreatePattern8();
        using var colors = new Image<Bgra32>(PatternWidth, PatternHeight);
        using var wide = new Image<Rgba64>(PatternWidth, PatternHeight);
        for (var y = 0; y < PatternHeight; y++)
        {
            for (var x = 0; x < PatternWidth; x++)
            {
                var value = PatternValue(x, y);
                colors.Frames[0][x, y] = new Bgra32(value, value, value, 255);
            }
        }

        gray.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 }, Ct);
        colors.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3, ColorMode = HistogramColorMode.PerChannel }, Ct);
        var expected = ReadGray8(gray.Frames[0]);
        for (var y = 0; y < PatternHeight; y++)
        {
            for (var x = 0; x < PatternWidth; x++)
            {
                var value = expected[(y * PatternWidth) + x];
                Assert.Equal(new Bgra32(value, value, value, 255), colors.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void PerChannelEqualizationRemapsEachChannelFromItsOwnHistogram()
    {
        using var image = new Image<Rgb24>(3, 1);
        image.Frames[0][0, 0] = new Rgb24(10, 100, 7);
        image.Frames[0][1, 0] = new Rgb24(20, 100, 9);
        image.Frames[0][2, 0] = new Rgb24(30, 200, 9);
        image.Equalize(new HistogramEqualizationOptions { ColorMode = HistogramColorMode.PerChannel }, Ct);

        // Red: cdf 1, 2, 3 (cdf_min 1): 0, 128, 255. Green: cdf 2, 3 (cdf_min 2): 0, 255. Blue: cdf 1, 3: 0, 255
        Assert.Equal(new Rgb24(0, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(128, 0, 255), image.Frames[0][1, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][2, 0]);
    }

    [Fact]
    public void TransparentPixelsAreRemappedButNotCounted()
    {
        using var image = new Image<Rgba64>(4, 1);
        image.Frames[0][0, 0] = new Rgba64(100, 100, 100, 65535);
        image.Frames[0][1, 0] = new Rgba64(200, 200, 200, 65535);
        image.Frames[0][2, 0] = new Rgba64(150, 150, 150, 0);
        image.Frames[0][3, 0] = new Rgba64(50, 50, 50, 0);
        image.Equalize(cancellationToken: Ct);

        // Counted: 100 and 200 (cdf_min 1, N - cdf_min = 1). 150 has the cdf of 100, and 50 is below the lowest level
        Assert.Equal(new Rgba64(0, 0, 0, 65535), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba64(65535, 65535, 65535, 65535), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba64(0, 0, 0, 0), image.Frames[0][2, 0]);
        Assert.Equal(new Rgba64(0, 0, 0, 0), image.Frames[0][3, 0]);
    }

    [Fact]
    public void EachFrameIsEqualizedAloneUnlessTheHistogramIsShared()
    {
        using var image = CreateTwoFrames();
        image.Equalize(cancellationToken: Ct);
        Assert.Equal([0, 255], ReadGray8(image.Frames[0]));
        Assert.Equal([0, 255], ReadGray8(image.Frames[1]));
        Assert.Equal([0, 255], ReadGray8(image.PosterFrame!));

        // Shared: levels 10, 20, 30, 40, 50, 60 once each, cdf_min = 1: (k - 1) * 255 / 5
        using var shared = CreateTwoFrames();
        shared.Equalize(new HistogramEqualizationOptions { ShareHistogramAcrossFrames = true }, Ct);
        Assert.Equal([0, 51], ReadGray8(shared.Frames[0]));
        Assert.Equal([102, 153], ReadGray8(shared.Frames[1]));
        Assert.Equal([204, 255], ReadGray8(shared.PosterFrame!));

        // One frame is equalized alone, whatever the option
        using var single = CreateTwoFrames();
        single.Frames[1].Equalize(new HistogramEqualizationOptions { ShareHistogramAcrossFrames = true }, Ct);
        Assert.Equal([10, 20], ReadGray8(single.Frames[0]));
        Assert.Equal([0, 255], ReadGray8(single.Frames[1]));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Adaptive equalization (CLAHE)
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AdaptiveEqualizationIsTheDocumentedDefinition()
    {
        using var image = CreatePattern8();
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 }, Ct);
        Assert.Equal(Convert.FromBase64String(ModelClahe8), ReadGray8(image.Frames[0]));

        using var wide = CreatePattern16();
        wide.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 }, Ct);
        Assert.Equal(Convert.FromBase64String(ModelClahe16), ReadGray16LittleEndian(wide.Frames[0]));
    }

    [Theory]
    [InlineData(FlipMode.Horizontal)]
    [InlineData(FlipMode.Vertical)]
    public void AdaptiveEqualizationOfAMirroredFrameIsTheMirroredResult(FlipMode mode)
    {
        // The tiles are interpolated at the pixel centers, so with tiles of equal sizes the result does not depend on the
        // direction of the axes
        var options = new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 };
        using var image = CreatePattern8();
        using var mirrored = image.CloneAs<Gray8>();
        mirrored.Flip(mode, Ct);
        image.Equalize(options, Ct);
        mirrored.Equalize(options, Ct);
        mirrored.Flip(mode, Ct);
        Assert.Equal(ReadGray8(image.Frames[0]), ReadGray8(mirrored.Frames[0]));
    }

    [Fact]
    public void AdaptiveEqualizationMatchesOpenCv()
    {
        // OpenCV computes the same tables, but interpolates them at the top-left corner of each pixel (x / tileWidth - 0.5)
        // instead of its center: the Python model with that convention gives OpenCV's output exactly. The half-pixel
        // shift moves the weights by 1/32 here, up to 2 levels.
        using var image = CreatePattern8();
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 }, Ct);
        AssertClose(Convert.FromBase64String(OpenCvClahe8), ReadGray8(image.Frames[0]), tolerance: 2);
    }

    [Fact]
    public void AdaptiveEqualizationMatchesScikitImage()
    {
        using var image = CreatePattern8();
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 }, Ct);
        // scikit-image stretches the levels to 14 bits before binning them and stretches its result to the full range: it
        // differs from OpenCV by the same 12 levels
        AssertClose(Convert.FromBase64String(ScikitImageClahe8), ReadGray8(image.Frames[0]), tolerance: 12);
    }

    [Fact]
    public void AdaptiveEqualizationOf16BitSamplesMatchesOpenCv()
    {
        using var image = CreatePattern16();
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = 3, TileCountX = 4, TileCountY = 3 }, Ct);

        // The half-pixel shift of OpenCV (see AdaptiveEqualizationMatchesOpenCv), on the 16-bit scale: 430 of 65,535
        AssertClose16(Convert.FromBase64String(OpenCvClahe16), ReadGray16LittleEndian(image.Frames[0]), tolerance: 430);
    }

    [Fact]
    public void AdaptiveEqualizationWithOneTileAndNoLimitIsTheMappingOfTheCumulativeHistogram()
    {
        // Levels 52 (1), 55 (2), 61 (1), 79 (3), 85 (1): round(cdf * 255 / 8) = 32, 96, 128, 223, 255
        using var image = Gray8Row(52, 55, 55, 61, 79, 79, 79, 85);
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, TileCountX = 1, TileCountY = 1, ClipLimit = double.PositiveInfinity }, Ct);
        Assert.Equal([32, 96, 96, 128, 223, 223, 223, 255], ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void AdaptiveEqualizationInterpolatesBetweenTheCentersOfTheTiles()
    {
        // Two tiles of 2 x 1 pixels without limit: [0, 100] and [100, 200]. Tile 0 maps 0 -> 128, 100 -> 255, 200 -> 255;
        // tile 1 maps 0 -> 0, 100 -> 128, 200 -> 255. The centers are at x = 1 and x = 3 (pixel centers 0.5 to 3.5):
        // pixels 0 and 3 take one tile, pixel 1 (center 1.5) is 1/4 of the way, pixel 2 (center 2.5) 3/4 of the way.
        using var image = Gray8Row(0, 100, 100, 200);
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, TileCountX = 2, TileCountY = 1, ClipLimit = double.PositiveInfinity }, Ct);

        // 255 * 3/4 + 128 / 4 = 223.25; 255 / 4 + 128 * 3/4 = 159.75
        Assert.Equal([128, 223, 160, 255], ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void ClipHistogramSpreadsTheExcessEvenly()
    {
        // 4 bins, 20 samples, clip limit 1: at most floor(1 * 20 / 4) = 5 per bin; excess 11: 2 per bin, and the
        // remainder of 3 goes to every max(1, floor(4 / 3)) = 1st bin from bin 0: bins 0, 1 and 2
        long[] histogram = [16, 0, 4, 0];
        ToneCurveMapper.ClipHistogram(histogram, 20, clipLimit: 1);
        Assert.Equal([8, 3, 7, 2], histogram);

        // 21 samples: at most 5 per bin; excess 12, 3 per bin
        histogram = [17, 0, 4, 0];
        ToneCurveMapper.ClipHistogram(histogram, 21, clipLimit: 1);
        Assert.Equal([8, 3, 7, 3], histogram);

        // The limit is at least one sample (floor(3 / 4) = 0); the excess of 2 goes to every 2nd bin: bins 0 and 2
        histogram = [3, 0, 0, 0];
        ToneCurveMapper.ClipHistogram(histogram, 3, clipLimit: 1);
        Assert.Equal([2, 0, 1, 0], histogram);

        // 8 bins, 12 samples: at most 1 per bin; excess 11, 1 per bin, and the remainder of 3 goes to every 2nd bin: 0, 2 and 4
        histogram = [12, 0, 0, 0, 0, 0, 0, 0];
        ToneCurveMapper.ClipHistogram(histogram, 12, clipLimit: 1);
        Assert.Equal([3, 1, 2, 1, 2, 1, 1, 1], histogram);

        // A limit above the total clips nothing
        histogram = [16, 0, 4, 0];
        ToneCurveMapper.ClipHistogram(histogram, 20, clipLimit: double.PositiveInfinity);
        Assert.Equal([16, 0, 4, 0], histogram);
    }

    [Fact]
    public void AdaptiveEqualizationOfAnAnimationCanShareTheTiles()
    {
        using var image = CreateTwoFrames();
        var options = new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, TileCountX = 1, TileCountY = 1, ClipLimit = double.PositiveInfinity, ShareHistogramAcrossFrames = true };
        image.Equalize(options, Ct);

        // Six levels once each: round(k * 255 / 6)
        Assert.Equal([43, 85], ReadGray8(image.Frames[0]));
        Assert.Equal([128, 170], ReadGray8(image.Frames[1]));
        Assert.Equal([213, 255], ReadGray8(image.PosterFrame!));
    }

    [Fact]
    public void AdaptiveEqualizationOfAFrameSmallerThanTheGridUsesOneTilePerPixel()
    {
        using var image = Gray8Row(10, 20);
        image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive, ClipLimit = double.PositiveInfinity }, Ct);

        // Each pixel is its own tile and the center of it: its level is the last one of its histogram
        Assert.Equal([255, 255], ReadGray8(image.Frames[0]));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Auto-levels
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AutoLevelsStretchesTheDarkestAndBrightestLevels()
    {
        // lo = 50, hi = 150: 100 -> round(50 * 255 / 100) = 128 (ties upward)
        using var image = Gray8Row(50, 100, 150, 75);
        image.AutoLevels(cancellationToken: Ct);
        Assert.Equal([0, 128, 255, 64], ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void AutoLevelsIgnoresThePercentilesOfOutliers()
    {
        // 10 samples: more than 10% are at most 20 (2 of 10 is not more than 10%: the first level is 20), more than 10% are at least 90
        using var image = Gray8Row(0, 20, 30, 40, 50, 60, 70, 80, 90, 255);
        image.AutoLevels(new AutoLevelsOptions { LowPercentile = 10, HighPercentile = 90 }, Ct);

        // (v - 20) * 255 / 70
        Assert.Equal([0, 0, 36, 73, 109, 146, 182, 219, 255, 255], ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void AutoLevelsPerChannelRemovesAColorCast()
    {
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(60, 10, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(160, 210, 100, 7);
        image.AutoLevels(new AutoLevelsOptions { ColorMode = HistogramColorMode.PerChannel }, Ct);
        Assert.Equal(new Rgba32(0, 0, 0, 255), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(255, 255, 255, 7), image.Frames[0][1, 0]);

        // Luminance: lumas 20 and 191 (Y = (2126 R + 7152 G + 722 B + 5000) / 10000); 20 -> 0 and 191 -> 255, so the
        // colors get -20 and +64, clamped
        using var luminance = new Image<Rgba32>(2, 1);
        luminance.Frames[0][0, 0] = new Rgba32(60, 10, 0, 255);
        luminance.Frames[0][1, 0] = new Rgba32(160, 210, 100, 7);
        luminance.AutoLevels(cancellationToken: Ct);
        Assert.Equal(new Rgba32(40, 0, 0, 255), luminance.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(224, 255, 164, 7), luminance.Frames[0][1, 0]);
    }

    [Fact]
    public void AutoLevelsOfAnAnimationCanShareTheLevels()
    {
        using var image = CreateTwoFrames();
        image.AutoLevels(new AutoLevelsOptions { ShareHistogramAcrossFrames = true }, Ct);

        // lo = 10, hi = 60: (v - 10) * 255 / 50
        Assert.Equal([0, 51], ReadGray8(image.Frames[0]));
        Assert.Equal([102, 153], ReadGray8(image.Frames[1]));
        Assert.Equal([204, 255], ReadGray8(image.PosterFrame!));

        using var alone = CreateTwoFrames();
        alone.AutoLevels(cancellationToken: Ct);
        Assert.Equal([0, 255], ReadGray8(alone.Frames[0]));
        Assert.Equal([0, 255], ReadGray8(alone.Frames[1]));

        // More than 50% of [0, 128] are at least 0 only: the high level is not above the low one, nothing changes
        alone.Frames[0][1, 0] = new Gray8(128);
        alone.Frames[0].AutoLevels(new AutoLevelsOptions { HighPercentile = 50 }, Ct);
        Assert.Equal([0, 128], ReadGray8(alone.Frames[0]));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Arguments and state
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void InvalidOperationsChangeNothing()
    {
        using var image = Gray8Row(10, 20, 30);
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Equalize());
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).AutoLevels());
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).ComputeHistogram(HistogramChannel.Red));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.ComputeHistogram((HistogramChannel)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].ComputeHistogram((HistogramChannel)(-1)));
        Assert.Throws<ArgumentException>(() => image.AutoLevels(new AutoLevelsOptions { LowPercentile = 50, HighPercentile = 50 }));
        Assert.Throws<ArgumentException>(() => image.Frames[0].AutoLevels(new AutoLevelsOptions { LowPercentile = 60, HighPercentile = 40 }));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.Equalize(cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].AutoLevels(cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => image.ComputeHistogram(HistogramChannel.Luminance, canceled.Token));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Equalize()));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.ComputeHistogram(HistogramChannel.Red)));
        Assert.Equal([10, 20, 30], ReadGray8(image.Frames[0]));

        // The tables are rented before a pixel changes: 8 x 8 tiles of 65,536 levels need 8 MiB
        var limits = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 4L * 1024 * 1024 } };
        using var limited = new Image<Gray16>(64, 64, new Gray16(1000), limits);
        limited.Frames[0][0, 0] = new Gray16(2000);
        Assert.Throws<ImageResourceLimitException>(() => limited.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive }, Ct));
        Assert.Equal(2000, limited.Frames[0][0, 0].Value);
        Assert.Equal(1000, limited.Frames[0][1, 0].Value);
        Assert.Equal(0, limited.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.Equalize());
        Assert.Throws<ObjectDisposedException>(() => disposed.AutoLevels());
        Assert.Throws<ObjectDisposedException>(() => disposed.ComputeHistogram(HistogramChannel.Red));
        Assert.Throws<ObjectDisposedException>(() => frame.Equalize());
        Assert.Throws<ObjectDisposedException>(() => frame.ComputeHistogram(HistogramChannel.Red));
    }

    [Fact]
    public void FloatingPointPixelsHaveNoHistogram()
    {
        using var image = new Image<RgbaFloat>(2, 1, new RgbaFloat(0.25f, 0.5f, 0.75f, 1));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.ComputeHistogram(HistogramChannel.Red));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Equalize());
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].AutoLevels());
        Assert.Equal(new RgbaFloat(0.25f, 0.5f, 0.75f, 1), image.Frames[0][1, 0]);

        using var half = new Image<RgbaHalf>(1, 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => half.Frames[0].Equalize());
    }

    #region Histogram_Clahe
    [Fact]
    public void Histogram_Clahe()
    {
        // A dim, low-contrast image: the levels are between 100 and 131
        using var image = new Image<Rgb24>(64, 64);
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var level = (byte)(100 + ((x + y) % 32));
                    row[x] = new Rgb24(level, level, (byte)(level - 20));
                }
            }
        });

        var before = image.ComputeHistogram(HistogramChannel.Luminance);
        var contrastBefore = before.Counts.LastIndexOfAnyExcept(0L) - before.Counts.IndexOfAnyExcept(0L);

        // Contrast-limited adaptive equalization of the luminance, on a 4 x 4 grid of tiles
        image.Equalize(new HistogramEqualizationOptions
        {
            Mode = HistogramEqualizationMode.Adaptive,
            TileCountX = 4,
            TileCountY = 4,
            ClipLimit = 2,
        });

        // The levels spread over a range more than twice as wide; the clip limit keeps the contrast from growing further
        var after = image.ComputeHistogram(HistogramChannel.Luminance);
        var contrastAfter = after.Counts.LastIndexOfAnyExcept(0L) - after.Counts.IndexOfAnyExcept(0L);
        Assert.InRange(contrastAfter, 2 * contrastBefore, 255);
    }
    #endregion

    private static Image<Gray8> Gray8Row(params byte[] values)
    {
        var image = new Image<Gray8>(values.Length, 1);
        for (var x = 0; x < values.Length; x++)
        {
            image.Frames[0][x, 0] = new Gray8(values[x]);
        }

        return image;
    }

    private static byte[] ReadGray8(ImageFrame frame)
    {
        var typed = (ImageFrame<Gray8>)frame;
        var pixels = new Gray8[typed.Size.Width * typed.Size.Height];
        typed.CopyPixelDataTo(pixels);
        return [.. pixels.Select(pixel => pixel.Value)];
    }

    /// <summary>Frames [10, 20] and [30, 40], and a poster [50, 60].</summary>
    private static Image<Gray8> CreateTwoFrames()
    {
        var image = Gray8Row(10, 20);
        var second = image.AppendFrame();
        second[0, 0] = new Gray8(30);
        second[1, 0] = new Gray8(40);
        var poster = (ImageFrame<Gray8>)image.SetPosterFrame(image.Frames[0]);
        poster[0, 0] = new Gray8(50);
        poster[1, 0] = new Gray8(60);
        return image;
    }

    private static byte PatternValue(int x, int y) => (byte)(64 + (((x * x) + (3 * y * y)) % 97) + (((x * 13) + (y * 7)) % 31) + (x / 6 * 4));

    private static Image<Gray8> CreatePattern8()
    {
        var image = new Image<Gray8>(PatternWidth, PatternHeight);
        for (var y = 0; y < PatternHeight; y++)
        {
            for (var x = 0; x < PatternWidth; x++)
            {
                image.Frames[0][x, y] = new Gray8(PatternValue(x, y));
            }
        }

        return image;
    }

    private static Image<Gray16> CreatePattern16()
    {
        var image = new Image<Gray16>(PatternWidth, PatternHeight);
        for (var y = 0; y < PatternHeight; y++)
        {
            for (var x = 0; x < PatternWidth; x++)
            {
                image.Frames[0][x, y] = new Gray16((ushort)((PatternValue(x, y) * 257) + (((x * 31) + (y * 17)) % 257)));
            }
        }

        return image;
    }

    private static byte[] ReadGray16LittleEndian(ImageFrame<Gray16> frame)
    {
        var result = new byte[2 * frame.Size.Width * frame.Size.Height];
        for (var y = 0; y < frame.Size.Height; y++)
        {
            for (var x = 0; x < frame.Size.Width; x++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2 * ((y * frame.Size.Width) + x)), frame[x, y].Value);
            }
        }

        return result;
    }

    private static void AssertClose(byte[] expected, byte[] actual, int tolerance)
    {
        Assert.HasCount(expected.Length, actual);
        var maximum = 0;
        var differences = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var difference = Math.Abs(expected[i] - actual[i]);
            maximum = Math.Max(maximum, difference);
            differences += difference > 0 ? 1 : 0;
        }

        Assert.True(maximum <= tolerance, $"max difference {maximum}, {differences} of {expected.Length} samples differ");
    }

    private static void AssertClose16(byte[] expected, byte[] actual, int tolerance)
    {
        Assert.HasCount(expected.Length, actual);
        var maximum = 0;
        var differences = 0;
        for (var i = 0; i < expected.Length; i += 2)
        {
            var difference = Math.Abs(BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i)) - BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(i)));
            maximum = Math.Max(maximum, difference);
            differences += difference > 0 ? 1 : 0;
        }

        Assert.True(maximum <= tolerance, $"max difference {maximum}, {differences} of {expected.Length / 2} samples differ");
    }
}
