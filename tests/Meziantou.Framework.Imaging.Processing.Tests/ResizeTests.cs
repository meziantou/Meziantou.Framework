using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Resize. Every expected size and sample below is a literal derived
/// by hand from the contract of section 5.4 (the derivation is in the comments: pixel centers, kernel values, normalized
/// weights, premultiplied sums, rounding). Nothing is computed by the code under test; the conformance tests add an
/// independent high-precision reference (TestHarness/Resampling) over the golden corpus.
/// </summary>
public sealed class ResizeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -----------------------------------------------------------------------------------------------------------------
    // Output size: checked rational arithmetic, nearest rounding with ties up, at least one pixel
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<int, int, int, int, ResizeMode, bool, int, int> SizeCases => new()
    {
        // Contain: s = min(tw / sw, th / sh); the other side is round(side * s), ties up, at least 1
        { 400, 300, 200, 200, ResizeMode.Contain, true, 200, 150 },
        { 300, 400, 200, 200, ResizeMode.Contain, true, 150, 200 },
        { 7, 5, 3, 3, ResizeMode.Contain, true, 3, 2 },           // s = 3/7, 5 * 3/7 = 2.14 -> 2
        { 5, 7, 4, 4, ResizeMode.Contain, true, 3, 4 },           // s = 4/7, 5 * 4/7 = 2.86 -> 3
        { 4, 3, 2, 2, ResizeMode.Contain, true, 2, 2 },           // s = 1/2, 3 * 1/2 = 1.5 -> 2 (tie up)
        { 4, 3, 6, 6, ResizeMode.Contain, true, 6, 5 },           // s = 3/2, 3 * 3/2 = 4.5 -> 5 (tie up)
        { 1000, 1, 10, 10, ResizeMode.Contain, true, 10, 1 },     // 1 * 1/100 = 0.01 -> at least 1
        { 1, 1000, 10, 10, ResizeMode.Contain, true, 1, 10 },
        { 3, 1, 2, 2, ResizeMode.Contain, true, 2, 1 },           // s = 2/3, 1 * 2/3 = 0.67 -> 1
        { 1, 1, 5, 3, ResizeMode.Contain, true, 3, 3 },           // s = 3
        { 65535, 2, 3, 3, ResizeMode.Contain, true, 3, 1 },
        { 4, 3, 6, 6, ResizeMode.Contain, false, 4, 3 },          // s = min(3/2, 1) = 1: unchanged
        { 4, 3, 2, 10, ResizeMode.Contain, false, 2, 2 },         // s = 1/2 (no enlargement needed)
        { 1, 1, 5, 3, ResizeMode.Contain, false, 1, 1 },
        { 7, 5, 7, 9, ResizeMode.Contain, false, 7, 5 },          // s = min(1, 9/5) = 1

        // Stretch: exactly the target
        { 7, 5, 3, 11, ResizeMode.Stretch, true, 3, 11 },
        { 1, 1, 1, 9, ResizeMode.Stretch, true, 1, 9 },
        { 9, 1, 1, 1, ResizeMode.Stretch, true, 1, 1 },
        { 7, 5, 3, 5, ResizeMode.Stretch, false, 3, 5 },
        { 7, 5, 7, 5, ResizeMode.Stretch, false, 7, 5 },

        // Cover: exactly the target, s = max(tw / sw, th / sh)
        { 400, 300, 200, 200, ResizeMode.Cover, true, 200, 200 },
        { 1, 1, 3, 2, ResizeMode.Cover, true, 3, 2 },
        { 1000, 1, 2, 2, ResizeMode.Cover, true, 2, 2 },
        { 400, 300, 300, 300, ResizeMode.Cover, false, 300, 300 }, // s = max(3/4, 1) = 1: a pure crop
        { 400, 300, 301, 300, ResizeMode.Cover, false, 301, 300 }, // s = max(301/400, 1) = 1
        { 5, 3, 1, 1, ResizeMode.Cover, false, 1, 1 },
    };

    [Theory]
    [MemberData(nameof(SizeCases))]
    public void OutputSizeFollowsTheModeRules(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, ResizeMode mode, bool allowUpscaling, int expectedWidth, int expectedHeight)
    {
        var options = new ResizeOptions(targetWidth, targetHeight) { Mode = mode, AllowUpscaling = allowUpscaling };
        Assert.Equal(new Size(expectedWidth, expectedHeight), ResizeGeometry.Compute(new Size(sourceWidth, sourceHeight), options).OutputSize);

        if ((long)sourceWidth * sourceHeight <= 4096)
        {
            using var image = new Image<Gray8>(sourceWidth, sourceHeight, new Gray8(7));
            image.Resize(options, Ct);
            Assert.Equal(new Size(expectedWidth, expectedHeight), image.Size);
            Assert.Equal(new Gray8(7), image.Frames[0][expectedWidth - 1, expectedHeight - 1]); // a constant image stays constant
        }
    }

    [Theory]
    [InlineData(7, 5, 8, 5, ResizeMode.Stretch)]
    [InlineData(7, 5, 3, 6, ResizeMode.Stretch)]
    [InlineData(1, 1, 1, 2, ResizeMode.Stretch)]
    [InlineData(400, 300, 300, 301, ResizeMode.Cover)]   // s = max(3/4, 301/300) > 1
    [InlineData(1, 1, 2, 1, ResizeMode.Cover)]
    [InlineData(3, 5, 4, 4, ResizeMode.Cover)]           // s = max(4/3, 4/5) > 1
    public void StretchAndCoverRejectEnlargementWhenUpscalingIsDisabled(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, ResizeMode mode)
    {
        using var image = new Image<Rgba32>(sourceWidth, sourceHeight, new Rgba32(1, 2, 3, 4));
        var storage = image.Frames[0].Storage;
        var options = new ResizeOptions(targetWidth, targetHeight) { Mode = mode, AllowUpscaling = false };
        Assert.Throws<ArgumentException>("options", () => image.Resize(options, Ct));
        Assert.Equal(new Size(sourceWidth, sourceHeight), image.Size);
        Assert.Same(storage, image.Frames[0].Storage);
    }

    [Fact]
    public void ResizingToTheSameSizeLeavesTheImageUnchanged()
    {
        // No resampling at all: even the hidden color of a transparent pixel is kept
        using var image = new Image<Rgba32>(3, 2, new Rgba32(10, 20, 30, 0));
        var storage = image.Frames[0].Storage;
        foreach (var mode in new[] { ResizeMode.Contain, ResizeMode.Stretch, ResizeMode.Cover })
        {
            image.Resize(new ResizeOptions(3, 2) { Mode = mode, Filter = ResamplingFilter.Lanczos3 }, Ct);
        }

        image.Resize(new ResizeOptions(30, 2) { AllowUpscaling = false }, Ct); // Contain: s = min(10, 1, 1) = 1
        Assert.Same(storage, image.Frames[0].Storage);
        Assert.Equal(new Rgba32(10, 20, 30, 0), image.Frames[0][2, 1]);
    }

    [Fact]
    public void SizeArithmeticDoesNotOverflow()
    {
        // 2 * length * numerator exceeds 64 bits: 128-bit intermediates
        Assert.Equal(new Size(int.MaxValue, 1), ResizeGeometry.Compute(new Size(int.MaxValue - 1, 1), new ResizeOptions(int.MaxValue, int.MaxValue)).OutputSize);
        Assert.Equal(new Size(1, int.MaxValue - 1), ResizeGeometry.Compute(new Size(1, int.MaxValue), new ResizeOptions(int.MaxValue, int.MaxValue - 1)).OutputSize);
        var geometry = ResizeGeometry.Compute(new Size(int.MaxValue, int.MaxValue - 1), new ResizeOptions(int.MaxValue - 2, 3) { Mode = ResizeMode.Cover, Anchor = ResizeAnchor.BottomRight });
        Assert.Equal(new Size(int.MaxValue - 2, 3), geometry.OutputSize);
        Assert.Equal(int.MaxValue - 2, geometry.Y.GetNearestIndex(2));
        Assert.Equal(int.MaxValue - 1, geometry.X.GetNearestIndex(int.MaxValue - 3));
    }

    [Fact]
    public void ResizeRespectsCanvasLimits()
    {
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 8, MaxHeight = 8 } };
        using var image = new Image<Rgba32>(4, 4, configuration);
        var exception = Assert.Throws<ImageResourceLimitException>(() => image.Resize(new ResizeOptions(9, 4) { Mode = ResizeMode.Stretch }, Ct));
        Assert.Equal(ImageResourceLimitKind.Width, exception.Kind);
        Assert.Equal(new Size(4, 4), image.Size);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cover anchors: at scale 1 the window is an exact crop for every kernel (all weights are 0 or 1)
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<ResizeAnchor, int, int> Anchors => new()
    {
        // anchor, first kept column of a 5-wide source (overflow 2), first kept row of a 5-high source
        { ResizeAnchor.TopLeft, 0, 0 },
        { ResizeAnchor.Top, 1, 0 },
        { ResizeAnchor.TopRight, 2, 0 },
        { ResizeAnchor.Left, 0, 1 },
        { ResizeAnchor.Center, 1, 1 },
        { ResizeAnchor.Right, 2, 1 },
        { ResizeAnchor.BottomLeft, 0, 2 },
        { ResizeAnchor.Bottom, 1, 2 },
        { ResizeAnchor.BottomRight, 2, 2 },
    };

    [Theory]
    [MemberData(nameof(Anchors))]
    public void CoverCropsTheOverflowAtTheAnchor(ResizeAnchor anchor, int firstColumn, int firstRow)
    {
        foreach (var filter in Enum.GetValues<ResamplingFilter>())
        {
            // 5x3 -> 3x3: s = max(3/5, 3/3) = 1, the horizontal overflow is 2 columns
            using (var wide = BuildIds(5, 3))
            {
                wide.Resize(new ResizeOptions(3, 3) { Mode = ResizeMode.Cover, Anchor = anchor, Filter = filter }, Ct);
                AssertIds(wide.Frames[0], 3, 3, (x, y) => Id(x + firstColumn, y));
            }

            // 3x5 -> 3x3: the vertical overflow is 2 rows
            using var tall = BuildIds(3, 5);
            tall.Resize(new ResizeOptions(3, 3) { Mode = ResizeMode.Cover, Anchor = anchor, Filter = filter }, Ct);
            AssertIds(tall.Frames[0], 3, 3, (x, y) => Id(x, y + firstRow));
        }
    }

    public static TheoryData<ResamplingFilter, ResizeAnchor, int[]> HalfPixelCases => new()
    {
        // Source 4x1 [10, 20, 40, 80] -> Cover 3x1 (s = 1). Left: c = d; Right: c = d + 1; Center: c = d + 0.5 (overflow 1/2 pixel)
        { ResamplingFilter.NearestNeighbor, ResizeAnchor.Left, [10, 20, 40] },
        { ResamplingFilter.NearestNeighbor, ResizeAnchor.Right, [20, 40, 80] },
        { ResamplingFilter.NearestNeighbor, ResizeAnchor.Center, [20, 40, 80] },  // u = d + 1 is a pixel boundary: the higher pixel
        { ResamplingFilter.Bicubic, ResizeAnchor.Left, [10, 20, 40] },
        { ResamplingFilter.Lanczos3, ResizeAnchor.Right, [20, 40, 80] },

        // Bilinear: (v[d] + v[d + 1]) / 2
        { ResamplingFilter.Bilinear, ResizeAnchor.Center, [15, 30, 60] },

        // Catmull-Rom at distances 1.5, 0.5, 0.5, 1.5: K = -0.0625, 0.5625 -> weights (-1, 9, 9, -1) / 16, edges clamped
        // d0: (-10 + 90 + 180 - 40) / 16 = 13.75 -> 14; d1: (-10 + 180 + 360 - 80) / 16 = 28.125 -> 28; d2: (-20 + 360 + 720 - 80) / 16 = 61.25 -> 61
        { ResamplingFilter.Bicubic, ResizeAnchor.Center, [14, 28, 61] },

        // Lanczos3 at distances 2.5, 1.5, 0.5 (x2): K = 0.24/pi^2, -4/(3 pi^2), 6/pi^2 -> normalized weights (9, -50, 225, 225, -50, 9) / 368
        // d0: (90 - 500 + 2250 + 4500 - 2000 + 720) / 368 = 13.75 -> 14; d1: 9810 / 368 = 26.66 -> 27; d2: 22810 / 368 = 61.98 -> 62
        { ResamplingFilter.Lanczos3, ResizeAnchor.Center, [14, 27, 62] },
    };

    [Theory]
    [MemberData(nameof(HalfPixelCases))]
    public void CoverWithAHalfPixelOverflowSamplesBetweenPixels(ResamplingFilter filter, ResizeAnchor anchor, int[] expected)
    {
        using var image = BuildGray8([10, 20, 40, 80]);
        image.Resize(new ResizeOptions(3, 1) { Mode = ResizeMode.Cover, Anchor = anchor, Filter = filter }, Ct);
        AssertGray8(image, expected);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Kernels, pixel centers, edge clamping, support widening, rounding
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void KernelValuesMatchTheirDefinitions()
    {
        // Closed forms: Lanczos3(1/2) = 6/pi^2, (3/2) = -4/(3 pi^2), (5/2) = 0.24/pi^2; Catmull-Rom(1/4) = 111/128, (3/4) = 29/128, (5/4) = -9/128, (7/4) = -3/128
        Assert.Equal(0.6079271018540267, ResamplingKernels.Lanczos3(0.5), 14);
        Assert.Equal(-0.13509491152311703, ResamplingKernels.Lanczos3(-1.5), 14);
        Assert.Equal(0.024317084074161065, ResamplingKernels.Lanczos3(2.5), 14);
        Assert.Equal(1, ResamplingKernels.Lanczos3(0));
        foreach (var x in new[] { 1.0, 2.0, -1.0, 3.0, 3.5, -7.0 })
        {
            Assert.Equal(0, ResamplingKernels.Lanczos3(x));
            Assert.Equal(0, ResamplingKernels.CatmullRom(x));
            Assert.Equal(0, ResamplingKernels.Triangle(x));
        }

        Assert.Equal(111.0 / 128, ResamplingKernels.CatmullRom(0.25));
        Assert.Equal(29.0 / 128, ResamplingKernels.CatmullRom(-0.75));
        Assert.Equal(-9.0 / 128, ResamplingKernels.CatmullRom(1.25));
        Assert.Equal(-3.0 / 128, ResamplingKernels.CatmullRom(1.75));
        Assert.Equal(0.5625, ResamplingKernels.CatmullRom(0.5));
        Assert.Equal(-0.0625, ResamplingKernels.CatmullRom(1.5));
        Assert.Equal(0.75, ResamplingKernels.Triangle(-0.25));
    }

    public static TheoryData<ResamplingFilter, int[], int[]> UpsamplingCases => new()
    {
        // 2 -> 4 pixels: centers c = (2d + 1) / 4 - 1/2 = -0.25, 0.25, 0.75, 1.25; indices outside the source are clamped
        // Nearest: floor(c + 1/2) = 0, 0, 1, 1
        { ResamplingFilter.NearestNeighbor, [0, 100], [0, 0, 100, 100] },

        // Bilinear: weights (0.25, 0.75) and (0.75, 0.25): 0, 25, 75, 100
        { ResamplingFilter.Bilinear, [0, 100], [0, 25, 75, 100] },

        // Exact ties round upward: 0.25 * 2 = 0.5 -> 1, 0.75 * 2 = 1.5 -> 2
        { ResamplingFilter.Bilinear, [0, 2], [0, 1, 2, 2] },

        // Catmull-Rom, d1 (c = 0.25): weights -9, 111, 29, -3 (/128) on pixels -1, 0, 1, 2 -> 255 * (29 - 3) / 128 = 51.80 -> 52;
        // d2 = 255 * (111 - 9) / 128 = 203.20 -> 203; d0 = 255 * -9 / 128 < 0 -> 0; d3 = 255 * 137 / 128 > 255 -> 255 (clamped overshoot)
        { ResamplingFilter.Bicubic, [0, 255], [0, 52, 203, 255] },
    };

    [Theory]
    [MemberData(nameof(UpsamplingCases))]
    public void UpsamplingUsesPixelCentersAndClampedEdges(ResamplingFilter filter, int[] source, int[] expected)
    {
        using var image = BuildGray8(source);
        image.Resize(new ResizeOptions(expected.Length, 1) { Mode = ResizeMode.Stretch, Filter = filter }, Ct);
        AssertGray8(image, expected);

        // The same along the vertical axis
        using var column = new Image<Gray8>(1, source.Length);
        for (var y = 0; y < source.Length; y++)
        {
            column.Frames[0][0, y] = new Gray8((byte)source[y]);
        }

        column.Resize(new ResizeOptions(1, expected.Length) { Mode = ResizeMode.Stretch, Filter = filter }, Ct);
        for (var y = 0; y < expected.Length; y++)
        {
            Assert.Equal(expected[y], column.Frames[0][0, y].Value);
        }
    }

    public static TheoryData<ResamplingFilter, int[], int[]> DownsamplingCases => new()
    {
        // 4 -> 2 pixels: c = 0.5 and 2.5, filter scale f = 2 (support doubled), weights K((i - c) / 2) normalized
        // Nearest: floor(u) with u = 1 and 3 (boundaries pick the higher pixel)
        { ResamplingFilter.NearestNeighbor, [0, 80, 160, 240], [80, 240] },

        // Bilinear: raw K = 0.25, 0.75, 0.75, 0.25 on pixels -1..2, sum 2 -> 0.125, 0.375, 0.375, 0.125; pixel -1 clamps to 0:
        // d0 = 0.5 * 0 + 0.375 * 80 + 0.125 * 160 = 50; d1 = 0.125 * 80 + 0.375 * 160 + 0.5 * 240 = 190
        { ResamplingFilter.Bilinear, [0, 80, 160, 240], [50, 190] },

        // Catmull-Rom: raw K on pixels -3..4 = (-3, -9, 29, 111, 111, 29, -9, -3) / 128, sum 2; clamped weights of pixels 0..3:
        // 0.5, 0.43359375, 0.11328125, -0.046875 -> d0 = 34.6875 + 18.125 - 11.25 = 41.5625 -> 42; d1 = 240 - 41.5625 -> 198
        { ResamplingFilter.Bicubic, [0, 80, 160, 240], [42, 198] },

        // 3 -> 1: c = 1, f = 3, support 3; bilinear raw K((i - 1) / 3) on -1..3 = 1/3, 2/3, 1, 2/3, 1/3 (sum 3); pixel 0 gets
        // 1/9 + 2/9, pixel 1 3/9, pixel 2 3/9 -> (3 * 30 + 3 * 60 + 3 * 120) / 9 = 70
        { ResamplingFilter.Bilinear, [30, 60, 120], [70] },
    };

    [Theory]
    [MemberData(nameof(DownsamplingCases))]
    public void DownsamplingWidensTheSupportAndNormalizesWeights(ResamplingFilter filter, int[] source, int[] expected)
    {
        using var image = BuildGray8(source);
        image.Resize(new ResizeOptions(expected.Length, 1) { Mode = ResizeMode.Stretch, Filter = filter }, Ct);
        AssertGray8(image, expected);
    }

    [Fact]
    public void TwoDimensionalFilteringIsSeparable()
    {
        // 2x2 -> 4x4 bilinear: each output is the product of the 1-D weights; with source [[0, 0], [0, 160]]:
        // out(x, y) = 160 * wx(x) * wy(y) with w = 0, 0.25, 0.75, 1 -> out(1, 2) = 160 * 0.25 * 0.75 = 30, out(2, 2) = 90, out(3, 1) = 40
        using var image = new Image<Gray8>(2, 2);
        image.Frames[0][1, 1] = new Gray8(160);
        image.Resize(new ResizeOptions(4, 4) { Filter = ResamplingFilter.Bilinear }, Ct);
        int[][] expected = [[0, 0, 0, 0], [0, 10, 30, 40], [0, 30, 90, 120], [0, 40, 120, 160]];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(expected[y][x], image.Frames[0][x, y].Value);
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Alpha: premultiplied filtering, transparent black, no fringes
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TransparentPixelsDoNotBleedTheirHiddenColor()
    {
        // Opaque red next to transparent green, bilinear 2 -> 4 (weights 1 | 0.75, 0.25 | 0.25, 0.75 | 0, 1):
        // A = 255, 191.25 -> 191, 63.75 -> 64, 0; premultiplied green is 0, so G stays 0 (straight filtering would give 64 and 191)
        Rgba32[] expected = [new(255, 0, 0, 255), new(255, 0, 0, 191), new(255, 0, 0, 64), new(0, 0, 0, 0)];
        using (var image = new Image<Rgba32>(2, 1))
        {
            image.Frames[0][0, 0] = new Rgba32(255, 0, 0, 255);
            image.Frames[0][1, 0] = new Rgba32(0, 255, 0, 0);
            image.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            Assert.Equal(expected, CopyRow(image.Frames[0], 0));
        }

        using (var image = new Image<Bgra32>(2, 1))
        {
            image.Frames[0][0, 0] = new Bgra32(255, 0, 0, 255);
            image.Frames[0][1, 0] = new Bgra32(0, 255, 0, 0);
            image.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            Assert.Equal([new Bgra32(255, 0, 0, 255), new Bgra32(255, 0, 0, 191), new Bgra32(255, 0, 0, 64), new Bgra32(0, 0, 0, 0)], CopyRow(image.Frames[0], 0));
        }

        // 16-bit: A = 49151.25 -> 49151, 16383.75 -> 16384
        using (var image = new Image<Rgba64>(2, 1))
        {
            image.Frames[0][0, 0] = new Rgba64(65535, 0, 0, 65535);
            image.Frames[0][1, 0] = new Rgba64(0, 65535, 0, 0);
            image.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            Assert.Equal([new Rgba64(65535, 0, 0, 65535), new Rgba64(65535, 0, 0, 49151), new Rgba64(65535, 0, 0, 16384), new Rgba64(0, 0, 0, 0)], CopyRow(image.Frames[0], 0));
        }
    }

    [Fact]
    public void ColorsAreWeightedByAlpha()
    {
        // (200, 0, 0, 100) next to (0, 100, 0, 200), bilinear 2 -> 4:
        // d1 (0.75, 0.25): A = 75 + 50 = 125, R = 0.75 * 200 * 100 / 125 = 120, G = 0.25 * 100 * 200 / 125 = 40
        // d2 (0.25, 0.75): A = 25 + 150 = 175, R = 5000 / 175 = 28.57 -> 29, G = 15000 / 175 = 85.71 -> 86
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(200, 0, 0, 100);
        image.Frames[0][1, 0] = new Rgba32(0, 100, 0, 200);
        image.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
        Assert.Equal([new Rgba32(200, 0, 0, 100), new Rgba32(120, 40, 0, 125), new Rgba32(29, 86, 0, 175), new Rgba32(0, 100, 0, 200)], CopyRow(image.Frames[0], 0));
    }

    [Fact]
    public void HiddenColorsNeverAffectTheResult()
    {
        foreach (var filter in Enum.GetValues<ResamplingFilter>())
        {
            using var a = new Image<Rgba32>(5, 4, new Rgba32(0, 0, 0, 0));
            using var b = new Image<Rgba32>(5, 4, new Rgba32(255, 128, 7, 0));
            foreach (var image in new[] { a, b })
            {
                image.Frames[0][2, 1] = new Rgba32(40, 80, 120, 255);
                image.Frames[0][3, 2] = new Rgba32(200, 10, 60, 30);
                image.Resize(new ResizeOptions(3, 7) { Mode = ResizeMode.Stretch, Filter = filter }, Ct);
            }

            var pixelsA = CopyAll(a.Frames[0]);
            Assert.Equal(pixelsA, CopyAll(b.Frames[0]));
            Assert.All(pixelsA, pixel => Assert.True(pixel.A != 0 || pixel == default, "Zero-alpha pixels are transparent black"));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void OpaqueFramesAreFilteredAsTheirColorsAndKeepTheMaximumAlpha()
    {
        // Premultiplying by a constant alpha, then dividing by the filtered alpha, only adds roundings: when every pixel read
        // is opaque, each color is filtered like a gray image of that color, bit for bit, and alpha stays the maximum
        var random = new Random(5);
        var pixels8 = new Rgba32[29 * 19];
        var pixels16 = new Rgba64[29 * 19];
        for (var i = 0; i < pixels8.Length; i++)
        {
            pixels8[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            pixels16[i] = new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), 65535);
        }

        foreach (var filter in new[] { ResamplingFilter.Bilinear, ResamplingFilter.Bicubic, ResamplingFilter.Lanczos3 })
        {
            foreach (var (width, height) in new[] { (11, 7), (64, 45) })
            {
                var options = new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, Filter = filter, AllowUpscaling = true };
                using var rgba32 = Image.ImportPixelData<Rgba32>(pixels8, 29, 19);
                using var rgba64 = Image.ImportPixelData<Rgba64>(pixels16, 29, 19);
                rgba32.Resize(options, Ct);
                rgba64.Resize(options, Ct);
                var result8 = CopyAll(rgba32.Frames[0]);
                var result16 = CopyAll(rgba64.Frames[0]);
                Assert.All(result8, pixel => Assert.Equal(255, pixel.A));
                Assert.All(result16, pixel => Assert.Equal(65535, pixel.A));
                for (var c = 0; c < 3; c++)
                {
                    using var gray8 = Image.ImportPixelData<Gray8>([.. pixels8.Select(pixel => new Gray8(Channel(pixel.R, pixel.G, pixel.B, c)))], 29, 19);
                    using var gray16 = Image.ImportPixelData<Gray16>([.. pixels16.Select(pixel => new Gray16(Channel(pixel.R, pixel.G, pixel.B, c)))], 29, 19);
                    gray8.Resize(options, Ct);
                    gray16.Resize(options, Ct);
                    Assert.Equal(CopyAll(gray8.Frames[0]).Select(pixel => (int)pixel.Value), result8.Select(pixel => (int)Channel(pixel.R, pixel.G, pixel.B, c)));
                    Assert.Equal(CopyAll(gray16.Frames[0]).Select(pixel => (int)pixel.Value), result16.Select(pixel => (int)Channel(pixel.R, pixel.G, pixel.B, c)));
                }
            }
        }

        // One transparent pixel read: the frame is filtered premultiplied, alpha drops around it and its hidden color does
        // not bleed
        var lanczos = new ResizeOptions(11, 7) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 };
        pixels8[(9 * 29) + 14] = new Rgba32(255, 0, 255, 0);
        using var red = Image.ImportPixelData<Rgba32>(pixels8, 29, 19);
        pixels8[(9 * 29) + 14] = new Rgba32(0, 255, 0, 0);
        using var green = Image.ImportPixelData<Rgba32>(pixels8, 29, 19);
        red.Resize(lanczos, Ct);
        green.Resize(lanczos, Ct);
        Assert.Equal(CopyAll(red.Frames[0]), CopyAll(green.Frames[0]));
        Assert.Contains(CopyAll(red.Frames[0]), pixel => pixel.A < 255);

        static T Channel<T>(T r, T g, T b, int c) => c switch { 0 => r, 1 => g, _ => b };
    }

    [Fact]
    public void FixedPointWeightsAddUpToOneAndGiveTheResidueToTheLargestRemainders()
    {
        // Three weights of a third: 5461.33 each rounds to 5461, the residue of 1 goes to the first of the equal remainders
        Span<short> thirds = stackalloc short[3];
        Assert.True(FixedPointWeights.Quantize([1 / 3d, 1 / 3d, 1 / 3d], thirds));
        Assert.Equal([5462, 5461, 5461], thirds.ToArray());

        // Catmull-Rom at a quarter pixel: -0.0703125, 0.8671875, 0.2265625, -0.0234375 are multiples of 2^-14: exact
        Span<short> exact = stackalloc short[4];
        Assert.True(FixedPointWeights.Quantize([-0.0703125, 0.8671875, 0.2265625, -0.0234375], exact));
        Assert.Equal([-1152, 14208, 3712, -384], exact.ToArray());

        // 0.6 and 0.4 scale to 9830.4 and 6553.6, which round to 9830 and 6554: no residue. 0.30001 x 3 + 0.09997 round
        // to 4915 x 3 + 1638 = 16383: the residue goes to the one that lost the most (0.09997 * 2^14 = 1637.9)
        Span<short> pair = stackalloc short[2];
        Assert.True(FixedPointWeights.Quantize([0.6, 0.4], pair));
        Assert.Equal([9830, 6554], pair.ToArray());
        Span<short> four = stackalloc short[4];
        Assert.True(FixedPointWeights.Quantize([0.30001, 0.30001, 0.30001, 0.09997], four));
        Assert.Equal(FixedPointWeights.One, four.ToArray().Sum(weight => weight));
    }

    [Fact]
    public void EightBitResizesStayWithinOneUnitOfTheDoublePrecisionPath()
    {
        // Patterns that drive the sums of the integer path to the ends of its range: stripes of 0 and 255 whose period
        // matches the negative lobes, a lone bright pixel, a step; with every filter, downscales and upscales. The integer
        // path cannot overflow (the headroom is checked for the weights of each plan) and stays within one unit of the
        // double-precision path; opaque alpha stays at its maximum
        foreach (var filter in new[] { ResamplingFilter.Bilinear, ResamplingFilter.Bicubic, ResamplingFilter.Lanczos3 })
        {
            foreach (var pattern in new Func<int, int, byte>[] { (x, y) => (byte)((((x / 2) + (y / 2)) % 2) == 0 ? 255 : 0), (x, y) => (byte)(x == 17 && y == 9 ? 255 : 0), (x, _) => (byte)(x < 20 ? 0 : 255), (x, y) => (byte)((x + y) % 3 == 0 ? 255 : 0) })
            {
                foreach (var (width, height) in new[] { (13, 11), (12, 7), (80, 47), (23, 19) })
                {
                    var pixels = new Rgba32[40 * 21];
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var value = pattern(i % 40, i / 40);
                        pixels[i] = new Rgba32(value, (byte)(255 - value), value, 255);
                    }

                    var options = new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, Filter = filter, AllowUpscaling = true };
                    using var integer = Image.ImportPixelData<Rgba32>(pixels, 40, 21);
                    using var reference = Image.ImportPixelData<Rgba32>(pixels, 40, 21);
                    integer.Resize(options, Ct);
                    Resampler.TestDisableFixedPoint = true;
                    try
                    {
                        reference.Resize(options, Ct);
                    }
                    finally
                    {
                        Resampler.TestDisableFixedPoint = false;
                    }

                    var actual = CopyAll(integer.Frames[0]);
                    var expected = CopyAll(reference.Frames[0]);
                    for (var i = 0; i < actual.Length; i++)
                    {
                        Assert.InRange(actual[i].R - expected[i].R, -1, 1);
                        Assert.InRange(actual[i].G - expected[i].G, -1, 1);
                        Assert.InRange(actual[i].B - expected[i].B, -1, 1);
                        Assert.Equal(255, actual[i].A);
                    }
                }
            }
        }
    }

    [Fact]
    public void OnlyOpaqueEightBitPixelsFilteredAsStoredTakeTheIntegerPath()
    {
        // Translucent pixels, linear light and 16-bit samples keep the double-precision path, bit for bit
        var pixels = new Rgba32[19 * 13];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i * 37), (byte)(i * 11), (byte)(i * 5), (byte)(i == 100 ? 254 : 255));
        }

        foreach (var workingSpace in new[] { ResizeWorkingSpace.Encoded, ResizeWorkingSpace.LinearSrgb })
        {
            var options = new ResizeOptions(7, 29) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3, WorkingSpace = workingSpace };
            Assert.Equal(Resize(pixels, options, disable: true), Resize(pixels, options, disable: false));
        }

        // An opaque frame of 8-bit samples can take it, a 16-bit one cannot
        var opaque = new ResizeOptions(7, 29) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 };
        using (var plan = new ResizePlan(ResizeGeometry.Compute(new Size(19, 13), opaque), ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32))
        using (var image = new Image<Rgba32>(4, 4))
        {
            plan.Prepare(image.Owner.Scope);
            Assert.NotNull(plan.XFixed);
        }

        using (var plan = new ResizePlan(ResizeGeometry.Compute(new Size(19, 13), opaque), ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba64))
        using (var image = new Image<Rgba64>(4, 4))
        {
            plan.Prepare(image.Owner.Scope);
            Assert.Null(plan.XFixed);
        }

        using (var plan = new ResizePlan(ResizeGeometry.Compute(new Size(19, 13), opaque), ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgb24))
        using (var image = new Image<Rgb24>(4, 4))
        {
            plan.Prepare(image.Owner.Scope);
            Assert.NotNull(plan.XFixed);
            Assert.Equal(6, plan.FixedPrecision);
        }

        static Rgba32[] Resize(Rgba32[] source, ResizeOptions options, bool disable)
        {
            using var image = Image.ImportPixelData<Rgba32>(source, 19, 13);
            Resampler.TestDisableFixedPoint = disable;
            try
            {
                image.Resize(options, Ct);
            }
            finally
            {
                Resampler.TestDisableFixedPoint = false;
            }

            return CopyAll(image.Frames[0]);
        }
    }

    [Fact]
    public void OnlyThePixelsReadDecideWhetherAFrameIsFilteredAsOpaque()
    {
        // A 10x4 source covered to 4x4 keeps columns 3 to 6 at scale 1: bilinear reads one source column per output column,
        // Lanczos3 two more on each side
        using var image = new Image<Rgba32>(10, 4, new Rgba32(10, 20, 30, 255));
        var storage = image.Frames[0].Storage;
        using var bilinear = Plan(ResamplingFilter.Bilinear);
        using var lanczos = Plan(ResamplingFilter.Lanczos3);
        using var nearest = Plan(ResamplingFilter.NearestNeighbor);
        Assert.Equal(WorkingAlpha.Opaque, Resampler.GetWorkingAlpha<Rgba32>(storage, bilinear));
        Assert.Equal(WorkingAlpha.Opaque, Resampler.GetWorkingAlpha<Rgba32>(storage, lanczos));

        image.Frames[0][1, 2] = new Rgba32(10, 20, 30, 254);
        Assert.Equal(WorkingAlpha.Opaque, Resampler.GetWorkingAlpha<Rgba32>(storage, bilinear));
        Assert.Equal(WorkingAlpha.Premultiplied, Resampler.GetWorkingAlpha<Rgba32>(storage, lanczos));

        image.Frames[0][1, 2] = new Rgba32(10, 20, 30, 255);
        image.Frames[0][6, 3] = new Rgba32(10, 20, 30, 0);
        Assert.Equal(WorkingAlpha.Premultiplied, Resampler.GetWorkingAlpha<Rgba32>(storage, bilinear));

        // Nearest neighbor copies pixels: it never filters
        image.Frames[0][6, 3] = new Rgba32(10, 20, 30, 255);
        Assert.Equal(WorkingAlpha.Premultiplied, Resampler.GetWorkingAlpha<Rgba32>(storage, nearest));

        ResizePlan Plan(ResamplingFilter filter)
        {
            var options = new ResizeOptions(4, 4) { Mode = ResizeMode.Cover, Filter = filter };
            var plan = new ResizePlan(ResizeGeometry.Compute(new Size(10, 4), options), filter, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32);
            plan.Prepare(image.Owner.Scope);
            return plan;
        }
    }

    [Fact]
    public void NearestNeighborCopiesPixelsExactlyAndClearsTransparentOnes()
    {
        using var image = new Image<Rgba64>(2, 1);
        image.Frames[0][0, 0] = new Rgba64(0x1234, 0x5678, 0x9ABC, 0x0001);
        image.Frames[0][1, 0] = new Rgba64(0xFFFF, 0x0001, 0x8000, 0);
        image.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.NearestNeighbor }, Ct);
        Assert.Equal([new Rgba64(0x1234, 0x5678, 0x9ABC, 0x0001), new Rgba64(0x1234, 0x5678, 0x9ABC, 0x0001), default, default], CopyRow(image.Frames[0], 0));
    }

    [Fact]
    public void NearestNeighborRepeatsSourceRowsWhenUpscalingVertically()
    {
        // Output rows that select the same source row are copied from the previous output row (zero alpha already cleared)
        using var image = new Image<Rgba32>(2, 2);
        image.Frames[0][0, 0] = new Rgba32(1, 2, 3, 4);
        image.Frames[0][1, 0] = new Rgba32(5, 6, 7, 0);
        image.Frames[0][0, 1] = new Rgba32(8, 9, 10, 255);
        image.Frames[0][1, 1] = new Rgba32(11, 12, 13, 14);
        image.Resize(new ResizeOptions(3, 5) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.NearestNeighbor }, Ct);

        // Source rows 0, 0, 1, 1, 1 and columns 0, 1, 1 (a center on a boundary selects the higher index)
        Rgba32[] first = [new(1, 2, 3, 4), default, default];
        Rgba32[] second = [new(8, 9, 10, 255), new(11, 12, 13, 14), new(11, 12, 13, 14)];
        Assert.Equal(first, CopyRow(image.Frames[0], 0));
        Assert.Equal(first, CopyRow(image.Frames[0], 1));
        Assert.Equal(second, CopyRow(image.Frames[0], 2));
        Assert.Equal(second, CopyRow(image.Frames[0], 3));
        Assert.Equal(second, CopyRow(image.Frames[0], 4));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // 16-bit precision (never through Rgba32)
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SixteenBitSamplesKeepTheirLowBits()
    {
        // Bilinear 2 -> 4: 0.75 a + 0.25 b and 0.25 a + 0.75 b
        using (var gray = new Image<Gray16>(2, 1))
        {
            gray.Frames[0][0, 0] = new Gray16(1000);
            gray.Frames[0][1, 0] = new Gray16(1001);
            gray.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            Assert.Equal([new Gray16(1000), new Gray16(1000), new Gray16(1001), new Gray16(1001)], CopyRow(gray.Frames[0], 0)); // 1000.25, 1000.75
        }

        using (var gray = new Image<Gray16>(2, 1))
        {
            gray.Frames[0][1, 0] = new Gray16(3);
            gray.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            Assert.Equal([new Gray16(0), new Gray16(1), new Gray16(2), new Gray16(3)], CopyRow(gray.Frames[0], 0)); // 0.75 -> 1, 2.25 -> 2
        }

        using var color = new Image<Rgba64>(2, 1);
        color.Frames[0][0, 0] = new Rgba64(1, 2, 3, 65535);
        color.Frames[0][1, 0] = new Rgba64(5, 6, 7, 65534);
        color.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);

        // d1: A = 0.75 * 65535 + 0.25 * 65534 = 65534.75 -> 65535; R = (0.75 * 65535 + 0.25 * 5 * 65534) / 65534.75 = 2.0000 -> 2 ...
        Assert.Equal([new Rgba64(1, 2, 3, 65535), new Rgba64(2, 3, 4, 65535), new Rgba64(4, 5, 6, 65534), new Rgba64(5, 6, 7, 65534)], CopyRow(color.Frames[0], 0));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Working space
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LinearWorkingSpaceAveragesLightInsteadOfCodeValues()
    {
        // 2 -> 1 bilinear: weights 0.5, 0.5. Encoded: 127.5 -> 128. Linear: Encode(0.5) = 1.055 * 0.5^(1/2.4) - 0.055 = 0.735357
        // -> 255 * 0.735357 = 187.52 -> 188, and 65535 * 0.735357 = 48191.62 -> 48192
        using (var encoded = BuildGray8([0, 255]))
        {
            encoded.Resize(new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            AssertGray8(encoded, [128]);
        }

        using (var linear = BuildGray8([0, 255]))
        {
            linear.Resize(new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear, WorkingSpace = ResizeWorkingSpace.LinearSrgb }, Ct);
            AssertGray8(linear, [188]);
        }

        using (var linear16 = new Image<Gray16>(2, 1))
        {
            linear16.Frames[0][1, 0] = new Gray16(65535);
            linear16.Resize(new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear, WorkingSpace = ResizeWorkingSpace.LinearSrgb }, Ct);
            Assert.Equal(48192, linear16.Frames[0][0, 0].Value);
        }

        // Alpha is linear in both spaces and a transparent pixel contributes nothing: (255, 255, 255, 255) + (0, 0, 0, 0)
        // -> A = 127.5 -> 128 and the color stays white
        foreach (var space in new[] { ResizeWorkingSpace.Encoded, ResizeWorkingSpace.LinearSrgb })
        {
            using var image = new Image<Rgba32>(2, 1);
            image.Frames[0][0, 0] = new Rgba32(255, 255, 255, 255);
            image.Resize(new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear, WorkingSpace = space }, Ct);
            Assert.Equal(new Rgba32(255, 255, 255, 128), image.Frames[0][0, 0]);
        }
    }

    /// <summary>
    /// The linear working space decodes the samples with the transfer function that the image declares. Black and white
    /// average to half of the light, which each function encodes to another sample: <c>65535 * Encode(0.5)</c>, computed
    /// with the <c>decimal</c> module of Python from the functions of Rec. ITU-T H.273 (BT.709: 46230.72; gamma 2.2:
    /// 47823.51; gamma 2.8: 51163.78; 100:1 logarithm: 55671.00; sRGB: 48191.62; PQ: 60721.24; HLG: 57123.15).
    /// </summary>
    [Theory]
    [InlineData(1, 46231)]
    [InlineData(4, 47824)]
    [InlineData(5, 51164)]
    [InlineData(6, 46231)] // the function of BT.709
    [InlineData(9, 55671)]
    [InlineData(13, 48192)]
    [InlineData(14, 46231)]
    [InlineData(16, 60721)]
    [InlineData(18, 57123)]
    [InlineData(2, 48192)] // unspecified: sRGB, like an image without description
    [InlineData(8, 32768)] // linear: filtered as stored (32767.5, and ties round up)
    public void LinearWorkingSpaceUsesTheTransferCharacteristicsOfTheImage(byte transfer, int expected)
    {
        var options = new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear, WorkingSpace = ResizeWorkingSpace.LinearSrgb };
        foreach (var primaries in new byte[] { 1, 9, 12 })
        {
            // The primaries do not change how the light of a channel adds up
            using var image = new Image<Gray16>(2, 1);
            image.Frames[0][1, 0] = new Gray16(65535);
            var cicp = new CicpColorDescription(primaries, transfer, 0, fullRange: true);
            image.Metadata.Cicp = cicp;
            image.Resize(options, Ct);
            Assert.Equal(expected, image.Frames[0][0, 0].Value);
            Assert.Equal(cicp, image.Metadata.Cicp);
        }

        // The colors of an image with alpha, and the convolution, use the same function
        using (var color = new Image<Rgba64>(2, 1))
        {
            color.Frames[0][0, 0] = new Rgba64(0, 65535, 0, 65535);
            color.Frames[0][1, 0] = new Rgba64(65535, 0, 0, 65535);
            color.Metadata.Cicp = new CicpColorDescription(9, transfer, 0, fullRange: true);
            color.Resize(options, Ct);
            Assert.Equal(new Rgba64((ushort)expected, (ushort)expected, 0, 65535), color.Frames[0][0, 0]);
        }

        using (var convolved = new Image<Gray16>(2, 1))
        {
            convolved.Frames[0][1, 0] = new Gray16(65535);
            convolved.Metadata.Cicp = new CicpColorDescription(9, transfer, 0, fullRange: true);

            // Half of the pixel and half of its left neighbor, which wraps around to the white pixel
            convolved.Convolve(new ConvolutionOptions(new ConvolutionKernel(3, 1, [0.5, 0.5, 0])) { EdgeMode = ConvolutionEdgeMode.Wrap, WorkingSpace = ConvolutionWorkingSpace.LinearSrgb }, Ct);
            Assert.Equal(expected, convolved.Frames[0][0, 0].Value);
        }
    }

    [Fact]
    public void LinearWorkingSpaceOfACicpDescriptionFollowsTheOtherColorLabels()
    {
        var options = new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear, WorkingSpace = ResizeWorkingSpace.LinearSrgb };
        var pq = new CicpColorDescription(9, 16, 0, fullRange: true);

        // An ICC profile describes the colors in the place of the CICP description: a recognized sRGB profile is decoded
        // as sRGB, and another profile is still rejected
        using (var image = new Image<Gray16>(2, 1))
        {
            image.Frames[0][1, 0] = new Gray16(65535);
            image.Metadata.Cicp = pq;
            image.Metadata.IccProfile = IccProfiles.Gray(IccProfiles.SrgbParametricCurve());
            image.Resize(options, Ct);
            Assert.Equal(48192, image.Frames[0][0, 0].Value);
        }

        using (var image = new Image<Rgba32>(2, 1))
        {
            image.Metadata.Cicp = pq;
            image.Metadata.IccProfile = IccProfiles.Rgb(IccProfiles.DisplayP3Colorants, IccProfiles.SrgbParametricCurve());
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Resize(options, Ct));
        }

        // Samples labeled as linear light are filtered as stored, whatever the description says
        using (var image = new Image<Gray16>(2, 1))
        {
            image.Frames[0][1, 0] = new Gray16(65535);
            image.Metadata.Cicp = pq;
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
            image.Resize(options, Ct);
            Assert.Equal(32768, image.Frames[0][0, 0].Value);
        }

        // Reserved transfer characteristics have no function: nothing is changed, and the encoded working space still works
        foreach (var reserved in new byte[] { 0, 3, 19, 255 })
        {
            using var image = new Image<Gray16>(2, 1);
            image.Frames[0][1, 0] = new Gray16(65535);
            image.Metadata.Cicp = new CicpColorDescription(1, reserved, 0, fullRange: true);
            var storage = image.Frames[0].Storage;
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Resize(options, Ct));
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Convolve(new ConvolutionOptions(new ConvolutionKernel(3, 1, [0.5, 0, 0.5])) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb }, Ct));
            Assert.Same(storage, image.Frames[0].Storage);
            image.Resize(new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bilinear }, Ct);
            Assert.Equal(32768, image.Frames[0][0, 0].Value);
        }
    }

    [Fact]
    public void LinearWorkingSpaceRequiresUntaggedOrRecognizedSrgbPixels()
    {
        var linear = new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, WorkingSpace = ResizeWorkingSpace.LinearSrgb };
        var encoded = new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch };
        var displayP3 = IccProfiles.Rgb(IccProfiles.DisplayP3Colorants, IccProfiles.SrgbParametricCurve());
        foreach (var profile in new[]
        {
            displayP3,
            IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.GammaCurve(2.2)),
            IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.IdentityCurve()),
            IccProfiles.Gray(IccProfiles.SrgbParametricCurve()), // gray profile on color pixels
            new IccProfile(MetadataBlob.FromOwnedArray(IccProfiles.Header("RGB ", tagCount: 0))),
            new IccProfile(MetadataBlob.FromOwnedArray([1, 2, 3])),
        })
        {
            using var image = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255));
            image.Metadata.IccProfile = profile;
            var storage = image.Frames[0].Storage;
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Resize(linear, Ct));
            Assert.Same(storage, image.Frames[0].Storage);

            // The encoded working space never interprets the profile, which is kept
            image.Resize(encoded, Ct);
            Assert.Same(profile, image.Metadata.IccProfile);
        }

        foreach (var profile in new[]
        {
            IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.SrgbParametricCurve()),
            IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.SrgbSampledCurve(1024)),
            IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.SrgbSampledCurve(26)),
        })
        {
            using var image = new Image<Rgba64>(2, 2, new Rgba64(10, 20, 30, 65535));
            image.Metadata.IccProfile = profile;
            image.Resize(linear, Ct);
            Assert.Equal(new Rgba64(10, 20, 30, 65535), image.Frames[0][0, 0]);
            Assert.Same(profile, image.Metadata.IccProfile);
        }

        // Gray pixels: an sGray profile is recognized, an RGB one is not
        using (var gray = new Image<Gray8>(2, 2, new Gray8(9)))
        {
            gray.Metadata.IccProfile = IccProfiles.Gray(IccProfiles.SrgbParametricCurve());
            gray.Resize(linear, Ct);
            Assert.Equal(new Gray8(9), gray.Frames[0][0, 0]);
        }

        using (var gray = new Image<Gray16>(2, 2))
        {
            gray.Metadata.IccProfile = IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.SrgbParametricCurve());
            Assert.Throws<UnsupportedImageFeatureException>(() => gray.Resize(linear, Ct));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Whole-image transactions
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryFrameAndThePosterAreResizedAndKeepTheirIdentity()
    {
        using var image = BuildIds(5, 3, frameCount: 3);
        image.SetPosterFrame(image.Frames[1]);
        image.PosterFrame![0, 0] = new Rgba64(9, 9, 9, 65535);
        image.Frames[2].Metadata.Duration = new FrameDuration(1, 7);
        image.Animation = new AnimationMetadata { TotalPlays = 4 };
        var frames = ((IEnumerable<ImageFrame<Rgba64>>)image.Frames).ToArray();
        var poster = image.PosterFrame;

        // Cover 3x3 at scale 1 is an exact crop of columns 1..3 for every frame and the poster
        image.Resize(new ResizeOptions(3, 3) { Mode = ResizeMode.Cover }, Ct);

        Assert.Equal(new Size(3, 3), image.Size);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frames[i], image.Frames[i]);
            AssertIds(frames[i], 3, 3, (x, y) => Id(x + 1, y, i));
        }

        Assert.Same(poster, image.PosterFrame);
        AssertIds(poster, 3, 3, (x, y) => Id(x + 1, y, 1));
        Assert.Equal(new FrameDuration(1, 7), frames[2].Metadata.Duration);
        Assert.Equal(4, image.Animation!.TotalPlays);

        // Filtered resize: every frame is resized like a single-frame image
        image.Resize(new ResizeOptions(5, 2) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 }, Ct);
        for (var i = 0; i < frames.Length; i++)
        {
            using var single = BuildIds(5, 3, frameCount: 1, frameOffset: i);
            single.Resize(new ResizeOptions(3, 3) { Mode = ResizeMode.Cover }, Ct);
            single.Resize(new ResizeOptions(5, 2) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 }, Ct);
            Assert.Equal(CopyAll(single.Frames[0]), CopyAll(frames[i]));
        }
    }

    [Fact]
    public void ResizeReleasesTheOriginalStoragesAndTheScratch()
    {
        using var image = BuildIds(5, 3, frameCount: 2);
        image.SetPosterFrame(image.Frames[0]);
        var scope = image.Owner.Scope;
        image.Resize(new ResizeOptions(40, 2) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 }, Ct);
        var diagnostics = scope.GetDiagnostics();
        Assert.Equal(3, image.Owner.LiveStorageCount);
        Assert.Equal(3, diagnostics.LiveAllocations);
        Assert.Equal(0, diagnostics.ReservedBytes);
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.Temporary));
    }

    [Fact]
    public void TheBudgetCoversOriginalReplacementAndScratchStorageTogether()
    {
        var options = new ResizeOptions(7, 9) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bicubic };

        // Measure an unconstrained run: the peak holds the originals, the replacements and the scratch at once. The tables of
        // the integer path of 8-bit samples are optional (skipped when they do not fit): the probe runs without them
        long before, after, peak;
        using (var probe = BuildWithPool(new SlabPool(), ImageConfiguration.Default))
        {
            before = probe.Owner.Scope.LiveBytes;
            Resampler.TestDisableFixedPoint = true;
            try
            {
                probe.Resize(options, Ct);
            }
            finally
            {
                Resampler.TestDisableFixedPoint = false;
            }

            after = probe.Owner.Scope.LiveBytes;
            peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
        }

        Assert.True(peak > before + after, "The scratch storage is charged in addition to the original and replacement storages");

        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } };
        using (var image = BuildWithPool(new SlabPool(), tight))
        {
            var snapshot = ResizeState.Capture(image);
            var exception = Assert.Throws<ImageResourceLimitException>(() => image.Resize(options, Ct));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            snapshot.AssertUnchanged(image);
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak } };
        using (var image = BuildWithPool(new SlabPool(), exact))
        {
            image.Resize(options, Ct);
            Assert.Equal(new Size(7, 9), image.Size);
            Assert.Equal(after, image.Owner.Scope.LiveBytes);
        }
    }

    [Fact]
    public void AllocationFailureAtAnyRentalBeforeCommitLeavesTheImageUnchanged()
    {
        foreach (var filter in Enum.GetValues<ResamplingFilter>())
        {
            var options = new ResizeOptions(5, 4) { Mode = ResizeMode.Stretch, Filter = filter };
            var rentals = CountRentals(options);
            Assert.True(rentals > 3, "Replacements and scratch are rented");
            for (var failing = 1; failing <= rentals; failing++)
            {
                var pool = new SlabPool();
                using var image = BuildWithPool(pool, ImageConfiguration.Default);
                var snapshot = ResizeState.Capture(image);
                var count = 0;
                var target = failing;
                pool.RentFailureInjector = _ => ++count == target ? new InjectedAllocationFailureException() : null;
                Assert.Throws<InjectedAllocationFailureException>(() => image.Resize(options, Ct));
                pool.RentFailureInjector = null;
                snapshot.AssertUnchanged(image);

                image.Resize(options, Ct); // still usable
                Assert.Equal(new Size(5, 4), image.Size);
            }
        }
    }

    [Fact]
    public void CancellationBeforeCommitLeavesTheImageUnchanged()
    {
        var options = new ResizeOptions(4, 5) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 };
        var rentals = CountRentals(options);
        for (var cancelAt = 1; cancelAt <= rentals; cancelAt++)
        {
            var pool = new SlabPool();
            using var image = BuildWithPool(pool, ImageConfiguration.Default);
            var snapshot = ResizeState.Capture(image);
            using var cancellation = new CancellationTokenSource();
            var count = 0;
            var target = cancelAt;
            pool.RentFailureInjector = _ =>
            {
                if (++count == target)
                {
                    cancellation.Cancel();
                }

                return null;
            };

            Assert.ThrowsAny<OperationCanceledException>(() => image.Resize(options, cancellation.Token));
            pool.RentFailureInjector = null;
            snapshot.AssertUnchanged(image);
        }

        using (var image = BuildWithPool(new SlabPool(), ImageConfiguration.Default))
        {
            var snapshot = ResizeState.Capture(image);
            Assert.ThrowsAny<OperationCanceledException>(() => image.Resize(options, new CancellationToken(canceled: true)));
            snapshot.AssertUnchanged(image);
        }
    }

    [Fact]
    public void ActiveLeasesDisposedImagesAndNullArgumentsAreRejected()
    {
        using (var image = BuildIds(3, 2, frameCount: 2))
        {
            image.Frames[1].ProcessPixelRows(image, static (_, image) =>
                Assert.Throws<InvalidOperationException>(() => image.Resize(new ResizeOptions(1, 1), Ct)));
            Assert.Equal(new Size(3, 2), image.Size);
            Assert.Throws<ArgumentNullException>("options", () => image.Resize(null!, Ct));
        }

        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Resize(new ResizeOptions(1, 1), Ct));
        var disposed = BuildIds(3, 2);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.Resize(new ResizeOptions(1, 1), Ct));
    }

    [Fact]
    public void ResizeReconcilesTheMetadata()
    {
        using var image = BuildIds(5, 3);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.ExifProfile = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: true)
            .Ifd0(TiffBuilder.Short(ExifTiff.OrientationTag, 6), TiffBuilder.Short(ExifTiff.ImageWidthTag, 5), TiffBuilder.Short(ExifTiff.ImageLengthTag, 3))
            .ExifIfd(TiffBuilder.Short(ExifTiff.PixelXDimensionTag, 5), TiffBuilder.Long(ExifTiff.PixelYDimensionTag, 3))
            .Thumbnail("STALE-THUMBNAIL"u8.ToArray())
            .Build()));
        var icc = IccProfiles.Rgb(IccProfiles.SrgbColorants, IccProfiles.SrgbParametricCurve());
        image.Metadata.IccProfile = icc;
        image.Metadata.Resolution = new ImageResolution(72, 72);

        image.Resize(new ResizeOptions(70000, 2) { Mode = ResizeMode.Contain }, Ct); // s = 2/3: 3.33 -> 3 x 2

        Assert.Equal(new Size(3, 2), image.Size);
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.Equal(((uint?)3, (uint?)2), ExifTiff.ReadPixelDimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(exif));
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        Assert.Same(icc, image.Metadata.IccProfile);
        Assert.Equal(new ImageResolution(72, 72), image.Metadata.Resolution); // the physical resolution is not rescaled
    }

    [Fact]
    public void FailedResizeKeepsTheMetadata()
    {
        var pool = new SlabPool();
        using var image = BuildWithPool(pool, ImageConfiguration.Default);
        var exif = image.Metadata.ExifProfile;
        pool.RentFailureInjector = _ => new InjectedAllocationFailureException();
        Assert.Throws<InjectedAllocationFailureException>(() => image.Resize(new ResizeOptions(2, 2), Ct));
        pool.RentFailureInjector = null;
        Assert.Same(exif, image.Metadata.ExifProfile);
        Assert.True(ExifTiff.HasThumbnail(exif!.Data.Span));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Implementation strategies must agree bit for bit
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<int, int, int, int, ResamplingFilter> StrategyCases => new()
    {
        { 9, 40, 4, 3, ResamplingFilter.Lanczos3 },  // strong vertical downsampling (scatter by default)
        { 9, 40, 4, 39, ResamplingFilter.Bicubic },
        { 3, 4, 7, 17, ResamplingFilter.Bilinear },  // upsampling (gather by default)
        { 17, 33, 5, 1, ResamplingFilter.Bicubic },
    };

    [Theory]
    [MemberData(nameof(StrategyCases))]
    public void GatherAndScatterVerticalPassesAreBitIdentical(int width, int height, int targetWidth, int targetHeight, ResamplingFilter filter)
    {
        var results = new List<Rgba64[]>();
        foreach (var strategy in new ResizeVerticalStrategy?[] { null, ResizeVerticalStrategy.Gather, ResizeVerticalStrategy.Scatter })
        {
            foreach (var layout in new PixelStorageLayoutOptions?[] { null, new() { RowAlignment = 16, TargetSlabBytes = 64 } })
            {
                using var image = BuildGradient(width, height, layout);
                ResizePlan.TestStrategyOverride = strategy;
                try
                {
                    image.Resize(new ResizeOptions(targetWidth, targetHeight) { Mode = ResizeMode.Stretch, Filter = filter }, Ct);
                }
                finally
                {
                    ResizePlan.TestStrategyOverride = null;
                }

                results.Add(CopyAll(image.Frames[0]));
            }
        }

        Assert.All(results, result => Assert.Equal(results[0], result));
    }

    [Fact]
    public void ThePlanPicksGatherUnlessItsRingIsLarge()
    {
        using var image = new Image<Rgba32>(4, 4);
        using (var down = new ResizePlan(ResizeGeometry.Compute(new Size(4, 400), new ResizeOptions(4, 4) { Mode = ResizeMode.Stretch }), ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32))
        {
            down.Prepare(image.Owner.Scope);
            // Each output row reads up to 2 * 3 * 100 source rows (all 400 here once clamped): a ring of 400 narrow rows is small
            Assert.Equal(ResizeVerticalStrategy.Gather, down.Strategy);
            Assert.Equal(400, down.YWeights!.MaxCount);
            Assert.Equal(400, down.RowCount);
        }

        using (var wide = new ResizePlan(ResizeGeometry.Compute(new Size(2000, 400), new ResizeOptions(2000, 4) { Mode = ResizeMode.Stretch }), ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32))
        {
            wide.Prepare(image.Owner.Scope);
            // 400 rows of 2000 pixels would be 25.6 MB, while the 4 output rows overlap
            Assert.Equal(ResizeVerticalStrategy.Scatter, wide.Strategy);
            Assert.Equal(4, wide.RowCount);
        }

        // The ring of gather is not picked when only the accumulators of scatter fit the allocation limit
        var geometry = ResizeGeometry.Compute(new Size(4, 400), new ResizeOptions(4, 4) { Mode = ResizeMode.Stretch });
        long scatterBytes;
        using (var scatter = new ResizePlan(geometry, ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32))
        {
            ResizePlan.TestStrategyOverride = ResizeVerticalStrategy.Scatter;
            try
            {
                scatter.Prepare(image.Owner.Scope);
            }
            finally
            {
                ResizePlan.TestStrategyOverride = null;
            }

            scatterBytes = scatter.ScratchBytes;
        }

        var limited = new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = scatterBytes });
        using (var tight = new ResizePlan(geometry, ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32))
        {
            tight.Prepare(limited);
            Assert.Equal(ResizeVerticalStrategy.Scatter, tight.Strategy);
            Assert.Equal(scatterBytes, tight.ScratchBytes);
        }

        Assert.Equal(0, limited.LiveBytes);

        using var up = new ResizePlan(ResizeGeometry.Compute(new Size(4, 4), new ResizeOptions(4, 400) { Mode = ResizeMode.Stretch }), ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32);
        up.Prepare(image.Owner.Scope);
        Assert.Equal(ResizeVerticalStrategy.Gather, up.Strategy);
        Assert.Equal(4, up.RowCount); // the whole 4-row source
        Assert.True(up.ScratchBytes > 0);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cursor hotspots
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4, 8, 0, 0)] // the top-left corner stays the top-left corner, whatever the scale
    [InlineData(4, 8, 3, 6)] // floor(3 * 8 / 4)
    [InlineData(4, 2, 0, 0)]
    [InlineData(4, 2, 1, 0)] // floor(1 * 2 / 4)
    [InlineData(4, 2, 2, 1)]
    [InlineData(4, 2, 3, 1)]
    [InlineData(3, 2, 1, 0)] // floor(1 * 2 / 3)
    [InlineData(3, 2, 2, 1)] // floor(2 * 2 / 3)
    [InlineData(5, 6, 3, 3)] // floor(3 * 6 / 5)
    [InlineData(5, 6, 4, 4)] // floor(4 * 6 / 5)
    [InlineData(1, 7, 0, 0)]
    [InlineData(7, 1, 6, 0)]
    public void AResizeScalesTheHotspotLikeTheCornerOfItsPixel(int source, int output, int hotspot, int expected)
    {
        using var image = BuildIds(source, source);
        image.Frames[0].Metadata.Hotspot = new Point(hotspot, hotspot);
        image.Resize(new ResizeOptions(output, output) { Mode = ResizeMode.Stretch, AllowUpscaling = true }, Ct);
        Assert.Equal(new Point(expected, expected), image.Frames[0].Metadata.Hotspot);
    }

    [Fact]
    public void TheHotspotIsScaledIndependentlyOnEachAxisAndForEachFrame()
    {
        using var image = BuildIds(4, 2, frameCount: 2);
        image.Frames[0].Metadata.Hotspot = new Point(3, 1);
        image.Resize(new ResizeOptions(8, 2) { Mode = ResizeMode.Stretch, AllowUpscaling = true }, Ct);
        Assert.Equal(new Point(6, 1), image.Frames[0].Metadata.Hotspot);
        Assert.Null(image.Frames[1].Metadata.Hotspot);

        // Contain 8x2 -> 4x1 scales both axes by one half
        image.Frames[1].Metadata.Hotspot = new Point(7, 1);
        image.Resize(new ResizeOptions(4, 4) { Mode = ResizeMode.Contain }, Ct);
        Assert.Equal(new Size(4, 1), image.Size);
        Assert.Equal(new Point(3, 0), image.Frames[0].Metadata.Hotspot);
        Assert.Equal(new Point(3, 0), image.Frames[1].Metadata.Hotspot);

        // A resize that keeps the size keeps the hotspot
        image.Resize(new ResizeOptions(4, 1) { Mode = ResizeMode.Stretch }, Ct);
        Assert.Equal(new Point(3, 0), image.Frames[0].Metadata.Hotspot);
    }

    [Theory]
    [InlineData(ResizeAnchor.Left, 0, 0)]
    [InlineData(ResizeAnchor.Left, 2, 2)]
    [InlineData(ResizeAnchor.Left, 3, null)]
    [InlineData(ResizeAnchor.Center, 0, null)]
    [InlineData(ResizeAnchor.Center, 1, 0)]
    [InlineData(ResizeAnchor.Center, 3, 2)]
    [InlineData(ResizeAnchor.Center, 4, null)]
    [InlineData(ResizeAnchor.Right, 1, null)]
    [InlineData(ResizeAnchor.Right, 2, 0)]
    [InlineData(ResizeAnchor.Right, 4, 2)]
    public void ACoverResizeMovesTheHotspotWithTheKeptRegionOrFails(ResizeAnchor anchor, int column, int? expectedColumn)
    {
        // 5x3 -> 3x3 keeps three of the five columns, at the anchor
        using var image = BuildIds(5, 3);
        image.Frames[0].Metadata.Hotspot = new Point(column, 2);
        var options = new ResizeOptions(3, 3) { Mode = ResizeMode.Cover, Anchor = anchor };
        if (expectedColumn is { } expected)
        {
            image.Resize(options, Ct);
            Assert.Equal(new Point(expected, 2), image.Frames[0].Metadata.Hotspot);
            Assert.Equal(Id(column, 2), image.Frames[0][expected, 2]); // it still designates the same pixel
            return;
        }

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Resize(options, Ct));
        Assert.Equal("Cursor hotspot outside the kept region", exception.Feature);
        Assert.Equal(new Size(5, 3), image.Size);
        Assert.Equal(new Point(column, 2), image.Frames[0].Metadata.Hotspot);
        AssertIds(image.Frames[0], 5, 3, (x, y) => Id(x, y));
    }

    [Theory]
    [InlineData(0, 0)] // half of this pixel is kept: the hotspot stays on the first column
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)] // half of this pixel is kept too
    public void AHotspotOnAPartlyKeptPixelStaysInsideTheFrame(int column, int expectedColumn)
    {
        // 4x1 -> 3x1 centered keeps the source interval [0.5, 3.5)
        using var image = BuildIds(4, 1);
        image.Frames[0].Metadata.Hotspot = new Point(column, 0);
        image.Resize(new ResizeOptions(3, 1) { Mode = ResizeMode.Cover, Anchor = ResizeAnchor.Center }, Ct);
        Assert.Equal(new Point(expectedColumn, 0), image.Frames[0].Metadata.Hotspot);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A distinct 16-bit pixel per (x, y, frame): low bits and non-trivial alpha.</summary>
    private static Rgba64 Id(int x, int y, int frame = 0)
        => new((ushort)((x * 0x1001) + (y * 0x0110) + frame + 1), (ushort)(0x8000 + (frame * 0x0303) + (y * 7) + x), (ushort)(0xFFFF - (x * 3) - (y * 5)), (ushort)(0x4000 + (x * 11) + (y * 13) + frame));

    private static Image<Rgba64> BuildIds(int width, int height, int frameCount = 1, int frameOffset = 0)
    {
        var image = new Image<Rgba64>(width, height);
        for (var i = 0; i < frameCount; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    frame[x, y] = Id(x, y, i + frameOffset);
                }
            }
        }

        return image;
    }

    private static Image<Rgba64> BuildGradient(int width, int height, PixelStorageLayoutOptions? layout)
    {
        var image = new Image<Rgba64>(ImageConfiguration.Default, new Size(width, height), scope: null, layout);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new Rgba64((ushort)(x * 7919), (ushort)(y * 1543), (ushort)((x * y * 97) + 3), (ushort)((x + y) % 3 == 0 ? 0 : 20000 + (x * 1000) + y));
            }
        }

        return image;
    }

    private static Image<Gray8> BuildGray8(int[] row)
    {
        var image = new Image<Gray8>(row.Length, 1);
        for (var x = 0; x < row.Length; x++)
        {
            image.Frames[0][x, 0] = new Gray8((byte)row[x]);
        }

        return image;
    }

    private static void AssertGray8(Image<Gray8> image, int[] expected)
    {
        Assert.Equal(new Size(expected.Length, 1), image.Size);
        Assert.Equal(expected, CopyRow(image.Frames[0], 0).Select(pixel => (int)pixel.Value).ToArray());
    }

    private static void AssertIds(ImageFrame<Rgba64> frame, int width, int height, Func<int, int, Rgba64> expected)
    {
        Assert.Equal(new Size(width, height), frame.Size);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (frame[x, y] != expected(x, y))
                    Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Pixel ({x},{y}): expected {expected(x, y)}, got {frame[x, y]}."));
            }
        }
    }

    private static TPixel[] CopyRow<TPixel>(ImageFrame<TPixel> frame, int y)
        where TPixel : unmanaged
        => [.. Enumerable.Range(0, frame.Width).Select(x => frame[x, y])];

    private static TPixel[] CopyAll<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    /// <summary>A two-frame 4x3 Rgba32 image with a poster and EXIF, charged to a scope that uses <paramref name="pool"/>.</summary>
    private static Image<Rgba32> BuildWithPool(SlabPool pool, ImageConfiguration configuration)
    {
        var image = new Image<Rgba32>(configuration, new Size(4, 3), new AllocationScope(configuration.Limits, pool), layoutOptions: null);
        for (var i = 0; i < 2; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    frame[x, y] = new Rgba32((byte)(x * 60), (byte)(y * 100), (byte)(i * 200), (byte)(255 - (x * 40) - i));
                }
            }
        }

        image.SetPosterFrame(image.Frames[1]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 3);
        image.Frames[0].Metadata.Hotspot = new Point(3, 2);
        image.Metadata.ExifProfile = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: false)
            .Ifd0(TiffBuilder.Short(ExifTiff.ImageWidthTag, 4), TiffBuilder.Short(ExifTiff.ImageLengthTag, 3))
            .Thumbnail("THUMB"u8.ToArray())
            .Build()));
        return image;
    }

    /// <summary>Counts the pool rentals of a successful resize of <see cref="BuildWithPool"/> (replacements and scratch).</summary>
    private static int CountRentals(ResizeOptions options)
    {
        var pool = new SlabPool();
        using var image = BuildWithPool(pool, ImageConfiguration.Default);
        var count = 0;
        pool.RentFailureInjector = _ =>
        {
            count++;
            return null;
        };

        image.Resize(options, Ct);
        return count;
    }

    /// <summary>Everything a failed resize must preserve.</summary>
    private sealed class ResizeState
    {
        private Size _size;
        private ImageFrame<Rgba32>[] _frames = [];
        private PixelStorage[] _storages = [];
        private Rgba32[][] _pixels = [];
        private ImageFrame<Rgba32>? _poster;
        private PixelStorage? _posterStorage;
        private Rgba32[] _posterPixels = [];
        private ExifProfile? _exif;
        private long _liveBytes;
        private int _liveStorages;

        public static ResizeState Capture(Image<Rgba32> image)
        {
            var frames = ((IEnumerable<ImageFrame<Rgba32>>)image.Frames).ToArray();
            return new ResizeState
            {
                _size = image.Size,
                _frames = frames,
                _storages = [.. frames.Select(frame => frame.Storage)],
                _pixels = [.. frames.Select(CopyAll)],
                _poster = image.PosterFrame,
                _posterStorage = image.PosterFrame?.Storage,
                _posterPixels = image.PosterFrame is null ? [] : CopyAll(image.PosterFrame),
                _exif = image.Metadata.ExifProfile,
                _liveBytes = image.Owner.Scope.LiveBytes,
                _liveStorages = image.Owner.LiveStorageCount,
            };
        }

        public void AssertUnchanged(Image<Rgba32> image)
        {
            Assert.Equal(_size, image.Size);
            Assert.Equal(_frames.Length, image.Frames.Count);
            for (var i = 0; i < _frames.Length; i++)
            {
                Assert.Same(_frames[i], image.Frames[i]);
                Assert.Same(_storages[i], image.Frames[i].Storage);
                Assert.Equal(_pixels[i], CopyAll(image.Frames[i]));
            }

            Assert.Same(_poster, image.PosterFrame);
            Assert.Same(_posterStorage, image.PosterFrame?.Storage);
            if (_poster is not null)
            {
                Assert.Equal(_posterPixels, CopyAll(image.PosterFrame!));
            }

            Assert.Same(_exif, image.Metadata.ExifProfile);
            Assert.Equal(_liveBytes, image.Owner.Scope.LiveBytes);
            Assert.Equal(_liveStorages, image.Owner.LiveStorageCount);
            Assert.Equal(0, image.Owner.Scope.GetDiagnostics().ReservedBytes);
            Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }
    }

    /// <summary>Synthetic ICC profiles (header, tag table, XYZ colorants and tone curves) built from ICC.1:2010.</summary>
    internal static class IccProfiles
    {
        public static readonly double[][] SrgbColorants = [[0.4361, 0.2225, 0.0139], [0.3851, 0.7169, 0.0971], [0.1431, 0.0606, 0.7141]];

        public static readonly double[][] DisplayP3Colorants = [[0.5151, 0.2412, -0.0011], [0.2919, 0.6922, 0.0419], [0.1572, 0.0666, 0.7841]];

        /// <summary>A parametric curve of type 3 with the IEC 61966-2-1 parameters (g, a, b, c, d) = (2.4, 1/1.055, 0.055/1.055, 1/12.92, 0.04045).</summary>
        public static byte[] SrgbParametricCurve() => Parametric(3, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045);

        public static byte[] GammaCurve(double gamma) => Parametric(0, gamma);

        public static byte[] IdentityCurve() => [.. "curv"u8, 0, 0, 0, 0, 0, 0, 0, 0];

        /// <summary>A sampled curve of the sRGB decoding function, computed here from its definition.</summary>
        public static byte[] SrgbSampledCurve(int count)
        {
            var data = new byte[12 + (2 * count)];
            "curv"u8.CopyTo(data);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)count);
            for (var i = 0; i < count; i++)
            {
                var v = (double)i / (count - 1);
                var linear = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + (2 * i)), (ushort)Math.Round(linear * 65535, MidpointRounding.AwayFromZero));
            }

            return data;
        }

        public static IccProfile Rgb(double[][] colorants, byte[] curve)
            => Build("RGB ", [("rXYZ", Xyz(colorants[0])), ("gXYZ", Xyz(colorants[1])), ("bXYZ", Xyz(colorants[2])), ("rTRC", curve), ("gTRC", curve), ("bTRC", curve)]);

        public static IccProfile Gray(byte[] curve) => Build("GRAY", [("kTRC", curve)]);

        public static byte[] Header(string colorSpace, int tagCount)
        {
            var data = new byte[132 + (12 * tagCount)];
            Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
            "XYZ "u8.CopyTo(data.AsSpan(20));
            "acsp"u8.CopyTo(data.AsSpan(36));
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(128), (uint)tagCount);
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
            return data;
        }

        private static IccProfile Build(string colorSpace, (string Signature, byte[] Data)[] tags)
        {
            var header = Header(colorSpace, tags.Length);
            var body = new List<byte>(header);
            for (var i = 0; i < tags.Length; i++)
            {
                while (body.Count % 4 != 0)
                {
                    body.Add(0);
                }

                var entry = header.AsSpan(132 + (12 * i), 12);
                Encoding.ASCII.GetBytes(tags[i].Signature).CopyTo(entry);
                BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)body.Count);
                BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
                body.AddRange(tags[i].Data);
            }

            var data = body.ToArray();
            header.AsSpan(128, 4 + (12 * tags.Length)).CopyTo(data.AsSpan(128));
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
            return new IccProfile(MetadataBlob.FromOwnedArray(data));
        }

        private static byte[] Xyz(double[] value)
        {
            var data = new byte[20];
            "XYZ "u8.CopyTo(data);
            for (var i = 0; i < 3; i++)
            {
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8 + (4 * i)), (int)Math.Round(value[i] * 65536, MidpointRounding.AwayFromZero));
            }

            return data;
        }

        private static byte[] Parametric(ushort function, params double[] parameters)
        {
            var data = new byte[12 + (4 * parameters.Length)];
            "para"u8.CopyTo(data);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), function);
            for (var i = 0; i < parameters.Length; i++)
            {
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12 + (4 * i)), (int)Math.Round(parameters[i] * 65536, MidpointRounding.AwayFromZero));
            }

            return data;
        }
    }
}
