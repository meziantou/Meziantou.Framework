using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Convolve. Every expected sample below is a literal derived by hand from the
/// contract of section 5.5 (the derivation is in the comments: which neighbors a weight reads, what the edge mode
/// designates outside the image, premultiplied sums, rounding and clamping). Nothing is computed by the code under test;
/// the conformance tests add an independent high-precision reference (TestHarness/Convolution) over the golden corpus.
/// </summary>
public sealed class ConvolutionTests
{
    private static readonly ConvolutionEdgeMode[] EdgeModes = [ConvolutionEdgeMode.Clamp, ConvolutionEdgeMode.Mirror, ConvolutionEdgeMode.Wrap, ConvolutionEdgeMode.Zero];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<PixelFormat> AllFormats => [.. PixelFormats.All];

    // -----------------------------------------------------------------------------------------------------------------
    // The matrix: applied as written, weights used as given
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void TheIdentityKernelKeepsVisiblePixelsForEveryPixelType(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.Rgba32: CheckIdentity<Rgba32>(); break;
            case PixelFormat.Bgra32: CheckIdentity<Bgra32>(); break;
            case PixelFormat.Rgb24: CheckIdentity<Rgb24>(); break;
            case PixelFormat.Rgba64: CheckIdentity<Rgba64>(); break;
            case PixelFormat.Gray8: CheckIdentity<Gray8>(); break;
            case PixelFormat.Gray16: CheckIdentity<Gray16>(); break;
            case PixelFormat.La16: CheckIdentity<La16>(); break;
            case PixelFormat.La32: CheckIdentity<La32>(); break;
            case PixelFormat.Rgb48: CheckIdentity<Rgb48>(); break;
            case PixelFormat.A8: CheckIdentity<A8>(); break;
            case PixelFormat.RgbaFloat: CheckIdentity<RgbaFloat>(); break;
            case PixelFormat.RgbaHalf: CheckIdentity<RgbaHalf>(); break;
            case PixelFormat.Argb32: CheckIdentity<Argb32>(); break;
            case PixelFormat.Abgr32: CheckIdentity<Abgr32>(); break;
            case PixelFormat.Bgr565: CheckIdentity<Bgr565>(); break;
            case PixelFormat.Rgba1010102: CheckIdentity<Rgba1010102>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Fact]
    public void TheKernelIsAppliedAsWrittenNotFlipped()
    {
        //   10 20 30
        //   40 50 60
        //   70 80 90
        int[][] source = [[10, 20, 30], [40, 50, 60], [70, 80, 90]];

        // Only the top-left weight: every pixel takes its top-left neighbor, out(x, y) = src(x - 1, y - 1), edges clamped.
        // A flipped (true convolution) kernel would read the bottom-right neighbor instead.
        using (var image = BuildGray8(source))
        {
            image.Convolve(Options(3, 3, [1, 0, 0, 0, 0, 0, 0, 0, 0]), Ct);
            AssertGray8(image.Frames[0], [[10, 10, 20], [10, 10, 20], [40, 40, 50]]);
        }

        // Only the middle-right weight: out(x, y) = src(x + 1, y)
        using (var image = BuildGray8(source))
        {
            image.Convolve(Options(3, 3, [0, 0, 0, 0, 0, 1, 0, 0, 0]), Ct);
            AssertGray8(image.Frames[0], [[20, 30, 30], [50, 60, 60], [80, 90, 90]]);
        }
    }

    [Fact]
    public void WeightsAreNotNormalized()
    {
        // A single pixel under a 3x3 matrix of ones: the clamped, mirrored and wrapped neighbors are all that pixel, so
        // the sum is 9 * 10 = 90 (a normalized matrix would give 10); with Zero only the pixel itself contributes
        (ConvolutionEdgeMode Mode, int Expected)[] cases = [(ConvolutionEdgeMode.Clamp, 90), (ConvolutionEdgeMode.Mirror, 90), (ConvolutionEdgeMode.Wrap, 90), (ConvolutionEdgeMode.Zero, 10)];
        foreach (var (mode, expected) in cases)
        {
            using var image = BuildGray8([[10]]);
            image.Convolve(Options(3, 3, [1, 1, 1, 1, 1, 1, 1, 1, 1], mode), Ct);
            AssertGray8(image.Frames[0], [[expected]]);
        }
    }

    [Fact]
    public void ABoxBlurAveragesTheNeighborhood()
    {
        //    9 18 27
        //   36 45 54
        //   63 72 81
        int[][] source = [[9, 18, 27], [36, 45, 54], [63, 72, 81]];
        double[] box = [1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d];

        // Zero: only the pixels inside the image are summed. Corner (0, 0): (9 + 18 + 36 + 45) / 9 = 12; top edge (1, 0):
        // (9 + 18 + 27 + 36 + 45 + 54) / 9 = 21; (2, 0): 144 / 9 = 16; (0, 1): 243 / 9 = 27; center: 405 / 9 = 45;
        // (2, 1): 297 / 9 = 33; (0, 2): 216 / 9 = 24; (1, 2): 351 / 9 = 39; (2, 2): 252 / 9 = 28
        using (var image = BuildGray8(source))
        {
            image.Convolve(Options(3, 3, box, ConvolutionEdgeMode.Zero), Ct);
            AssertGray8(image.Frames[0], [[12, 21, 16], [27, 45, 33], [24, 39, 28]]);
        }

        // Clamp: the edge pixels are repeated. Corner (0, 0): (4 * 9 + 2 * 18 + 2 * 36 + 45) / 9 = 189 / 9 = 21; (1, 0):
        // (2 * (9 + 18 + 27) + 36 + 45 + 54) / 9 = 243 / 9 = 27; (2, 0): 297 / 9 = 33; (0, 1): 351 / 9 = 39; (2, 1):
        // 459 / 9 = 51; (0, 2): 513 / 9 = 57; (1, 2): 567 / 9 = 63; (2, 2): 621 / 9 = 69.
        // Mirror reads the same pixels one step outside the image (the edge pixel is repeated).
        foreach (var mode in new[] { ConvolutionEdgeMode.Clamp, ConvolutionEdgeMode.Mirror })
        {
            using var image = BuildGray8(source);
            image.Convolve(Options(3, 3, box, mode), Ct);
            AssertGray8(image.Frames[0], [[21, 27, 33], [39, 45, 51], [57, 63, 69]]);
        }

        // Wrap: every 3x3 window of the tiled image holds each pixel once: 405 / 9 = 45 everywhere
        using (var image = BuildGray8(source))
        {
            image.Convolve(Options(3, 3, box, ConvolutionEdgeMode.Wrap), Ct);
            AssertGray8(image.Frames[0], [[45, 45, 45], [45, 45, 45], [45, 45, 45]]);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Edges
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<ConvolutionEdgeMode, int[], int[]> EdgeCases => new()
    {
        // Source a b c d = 10 20 30 40. First expectation: out(x) = src(x - 2); second: out(x) = src(x + 2).
        { ConvolutionEdgeMode.Clamp, [10, 10, 10, 20], [30, 40, 40, 40] },  // a a | a b c d | d d
        { ConvolutionEdgeMode.Mirror, [20, 10, 10, 20], [30, 40, 40, 30] }, // b a | a b c d | d c
        { ConvolutionEdgeMode.Wrap, [30, 40, 10, 20], [30, 40, 10, 20] },   // c d | a b c d | a b
        { ConvolutionEdgeMode.Zero, [0, 0, 10, 20], [30, 40, 0, 0] },       // 0 0 | a b c d | 0 0
    };

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void EdgeModesChooseThePixelsReadOutsideTheImage(ConvolutionEdgeMode mode, int[] expectedFromLeft, int[] expectedFromRight)
    {
        int[] source = [10, 20, 30, 40];

        // Horizontally: a 5x1 matrix whose only weight is the leftmost (reads 2 pixels to the left) or the rightmost one
        using (var image = BuildGray8([source]))
        {
            image.Convolve(Options(5, 1, [1, 0, 0, 0, 0], mode), Ct);
            AssertGray8(image.Frames[0], [expectedFromLeft]);
        }

        using (var image = BuildGray8([source]))
        {
            image.Convolve(Options(5, 1, [0, 0, 0, 0, 1], mode), Ct);
            AssertGray8(image.Frames[0], [expectedFromRight]);
        }

        // Vertically: the same image as a column, with the 1x5 matrices
        using (var image = BuildGray8(Column(source)))
        {
            image.Convolve(Options(1, 5, [1, 0, 0, 0, 0], mode), Ct);
            AssertGray8(image.Frames[0], Column(expectedFromLeft));
        }

        using (var image = BuildGray8(Column(source)))
        {
            image.Convolve(Options(1, 5, [0, 0, 0, 0, 1], mode), Ct);
            AssertGray8(image.Frames[0], Column(expectedFromRight));
        }
    }

    public static TheoryData<ConvolutionEdgeMode, int[], int[]> LargeKernelCases => new()
    {
        // Source a b = 10 20 under a 7-wide matrix. First expectation: out(x) = src(x - 3); second: out(x) = src(x + 3).
        { ConvolutionEdgeMode.Clamp, [10, 10], [20, 20] },
        { ConvolutionEdgeMode.Mirror, [20, 20], [10, 10] }, // ... a b b a | a b | b a a b ...: src(-3) = b, src(-2) = b, src(3) = a, src(4) = a
        { ConvolutionEdgeMode.Wrap, [20, 10], [20, 10] },   // ... b a b | a b | a b a ...: src(-3) = b, src(-2) = a, src(3) = b, src(4) = a
        { ConvolutionEdgeMode.Zero, [0, 0], [0, 0] },
    };

    [Theory]
    [MemberData(nameof(LargeKernelCases))]
    public void KernelsLargerThanTheImageReadSeveralPeriodsOutside(ConvolutionEdgeMode mode, int[] expectedFromLeft, int[] expectedFromRight)
    {
        int[] source = [10, 20];
        using (var image = BuildGray8([source]))
        {
            image.Convolve(Options(7, 1, [1, 0, 0, 0, 0, 0, 0], mode), Ct);
            AssertGray8(image.Frames[0], [expectedFromLeft]);
        }

        using (var image = BuildGray8([source]))
        {
            image.Convolve(Options(7, 1, [0, 0, 0, 0, 0, 0, 1], mode), Ct);
            AssertGray8(image.Frames[0], [expectedFromRight]);
        }

        using (var image = BuildGray8(Column(source)))
        {
            image.Convolve(Options(1, 7, [1, 0, 0, 0, 0, 0, 0], mode), Ct);
            AssertGray8(image.Frames[0], Column(expectedFromLeft));
        }

        using (var image = BuildGray8(Column(source)))
        {
            image.Convolve(Options(1, 7, [0, 0, 0, 0, 0, 0, 1], mode), Ct);
            AssertGray8(image.Frames[0], Column(expectedFromRight));
        }
    }

    public static TheoryData<ConvolutionEdgeMode, int[], int[]> InPlaceCases => new()
    {
        // A column 1 2 ... 9. First expectation: out(y) = src(y - 2); second: out(y) = src(y + 2).
        { ConvolutionEdgeMode.Clamp, [1, 1, 1, 2, 3, 4, 5, 6, 7], [3, 4, 5, 6, 7, 8, 9, 9, 9] },
        { ConvolutionEdgeMode.Mirror, [2, 1, 1, 2, 3, 4, 5, 6, 7], [3, 4, 5, 6, 7, 8, 9, 9, 8] },
        { ConvolutionEdgeMode.Wrap, [8, 9, 1, 2, 3, 4, 5, 6, 7], [3, 4, 5, 6, 7, 8, 9, 1, 2] },
        { ConvolutionEdgeMode.Zero, [0, 0, 1, 2, 3, 4, 5, 6, 7], [3, 4, 5, 6, 7, 8, 9, 0, 0] },
    };

    [Theory]
    [MemberData(nameof(InPlaceCases))]
    public void RowsAreReadBeforeTheyAreRewritten(ConvolutionEdgeMode mode, int[] expectedFromAbove, int[] expectedFromBelow)
    {
        // More rows than the matrix: the image is rewritten in place, yet every output row is computed from original rows,
        // including the rows already rewritten above (first matrix) and the first rows read again at the bottom (Wrap)
        int[] source = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        using (var image = BuildGray8(Column(source)))
        {
            image.Convolve(Options(1, 5, [1, 0, 0, 0, 0], mode), Ct);
            AssertGray8(image.Frames[0], Column(expectedFromAbove));
        }

        using (var image = BuildGray8(Column(source)))
        {
            image.Convolve(Options(1, 5, [0, 0, 0, 0, 1], mode), Ct);
            AssertGray8(image.Frames[0], Column(expectedFromBelow));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Rounding, clamping and precision
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SumsAreRoundedToNearestWithTiesUpward()
    {
        // out(x) = w * src(x - 1) + w * src(x)
        // w = 1/2 over 0 1 0 0: 0, 0.5 -> 1 (tie upward), 0.5 -> 1, 0
        using (var image = BuildGray8([[0, 1, 0, 0]]))
        {
            image.Convolve(Options(3, 1, [0.5, 0.5, 0]), Ct);
            AssertGray8(image.Frames[0], [[0, 1, 1, 0]]);
        }

        // w = 1/4 over 0 1 0 0: 0.25 -> 0; over 0 3 0 0: 0.75 -> 1
        using (var image = BuildGray8([[0, 1, 0, 0]]))
        {
            image.Convolve(Options(3, 1, [0.25, 0.25, 0]), Ct);
            AssertGray8(image.Frames[0], [[0, 0, 0, 0]]);
        }

        using (var image = BuildGray8([[0, 3, 0, 0]]))
        {
            image.Convolve(Options(3, 1, [0.25, 0.25, 0]), Ct);
            AssertGray8(image.Frames[0], [[0, 1, 1, 0]]);
        }
    }

    [Fact]
    public void SumsAreClampedToTheSampleRange()
    {
        // -1 3 -1 over 10 200 10 (edges clamped): -10 + 30 - 200 = -180 -> 0; -10 + 600 - 10 = 580 -> 255; -200 + 30 - 10 = -180 -> 0
        using (var image = BuildGray8([[10, 200, 10]]))
        {
            image.Convolve(Options(3, 1, [-1, 3, -1]), Ct);
            AssertGray8(image.Frames[0], [[0, 255, 0]]);
        }

        // 16-bit: -1000 + 3000 - 60000 -> 0; -1000 + 180000 - 1000 = 178000 -> 65535
        using var image16 = new Image<Gray16>(3, 1);
        image16.Frames[0][0, 0] = new Gray16(1000);
        image16.Frames[0][1, 0] = new Gray16(60000);
        image16.Frames[0][2, 0] = new Gray16(1000);
        image16.Convolve(Options(3, 1, [-1, 3, -1]), Ct);
        AssertRow(image16.Frames[0], new Gray16(0), new Gray16(65535), new Gray16(0));
    }

    [Fact]
    public void SixteenBitSamplesAreFilteredAtFullPrecision()
    {
        // out(x) = (src(x - 1) + src(x)) / 2 over 1000 1003 65535: 1000, 1001.5 -> 1002, 33269 (an 8-bit intermediate
        // cannot produce these values)
        using (var image = new Image<Gray16>(3, 1))
        {
            image.Frames[0][0, 0] = new Gray16(1000);
            image.Frames[0][1, 0] = new Gray16(1003);
            image.Frames[0][2, 0] = new Gray16(65535);
            image.Convolve(Options(3, 1, [0.5, 0.5, 0]), Ct);
            AssertRow(image.Frames[0], new Gray16(1000), new Gray16(1002), new Gray16(33269));
        }

        // With alpha, pixel 1 = (p0 + p1) / 2 premultiplied: A = (65535 + 32768) / 2 = 49151.5 -> 49152;
        // R = (65535 * 65535 + 1 * 32768) / 2 / 49151.5 = 43690.11 -> 43690;
        // G = (1 * 65535 + 65535 * 32768) / 2 / 49151.5 = 21845.89 -> 21846;
        // B = (300 * 65535 + 301 * 32768) / 2 / 49151.5 = 300.33 -> 300
        using (var image = new Image<Rgba64>(2, 1))
        {
            image.Frames[0][0, 0] = new Rgba64(65535, 1, 300, 65535);
            image.Frames[0][1, 0] = new Rgba64(1, 65535, 301, 32768);
            image.Convolve(Options(3, 1, [0.5, 0.5, 0]), Ct);
            AssertRow(image.Frames[0], new Rgba64(65535, 1, 300, 65535), new Rgba64(43690, 21846, 300, 49152));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Alpha
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TransparentPixelsDoNotBleedTheirHiddenColor()
    {
        // Opaque red next to transparent green, out(x) = (src(x - 1) + src(x)) / 2 with clamped edges.
        // Pixel 1: A = 127.5 -> 128; premultiplied R = 255 * 255 / 2 = 32512.5, / 127.5 = 255; premultiplied green is 0
        // (straight filtering would give R = G = 128)
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(255, 0, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(0, 255, 0, 0);
        image.Convolve(Options(3, 1, [0.5, 0.5, 0]), Ct);
        AssertRow(image.Frames[0], new Rgba32(255, 0, 0, 255), new Rgba32(255, 0, 0, 128));
    }

    [Fact]
    public void ColorsAreWeightedByAlpha()
    {
        // 1/4 1/2 1/4 over (200, 100, 50, 255) (40, 80, 120, 128) (255, 255, 255, 0), edges clamped:
        // pixel 0 = 3/4 p0 + 1/4 p1: A = 191.25 + 32 = 223.25 -> 223; R = (0.75 * 200 * 255 + 0.25 * 40 * 128) / 223.25
        //   = 39530 / 223.25 = 177.07 -> 177; G = 21685 / 223.25 = 97.13 -> 97; B = 13402.5 / 223.25 = 60.03 -> 60
        // pixel 1 = 1/4 p0 + 1/2 p1 + 1/4 p2: A = 63.75 + 64 = 127.75 -> 128; R = (12750 + 2560) / 127.75 = 119.84 -> 120;
        //   G = (6375 + 5120) / 127.75 = 89.98 -> 90; B = (3187.5 + 7680) / 127.75 = 85.07 -> 85
        // pixel 2 = 1/4 p1 + 3/4 p2: A = 32; only p1 is visible, so the color is p1's
        using var image = new Image<Rgba32>(3, 1);
        image.Frames[0][0, 0] = new Rgba32(200, 100, 50, 255);
        image.Frames[0][1, 0] = new Rgba32(40, 80, 120, 128);
        image.Frames[0][2, 0] = new Rgba32(255, 255, 255, 0);
        image.Convolve(Options(3, 1, [0.25, 0.5, 0.25]), Ct);
        AssertRow(image.Frames[0], new Rgba32(177, 97, 60, 223), new Rgba32(120, 90, 85, 128), new Rgba32(40, 80, 120, 32));
    }

    [Fact]
    public void PixelsWhoseAlphaBecomesZeroAreTransparentBlack()
    {
        // The identity matrix on a transparent pixel with a hidden color: A = 0, so the pixel is (0, 0, 0, 0)
        using var image = new Image<Rgba32>(1, 1, new Rgba32(12, 34, 56, 0));
        image.Convolve(Options(1, 1, [1]), Ct);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][0, 0]);
    }

    [Fact]
    public void PreserveAlphaFiltersStraightColorsAndKeepsAlpha()
    {
        // Same source and matrix as TransparentPixelsDoNotBleedTheirHiddenColor: pixel 1 = (p0 + p1) / 2 on the stored
        // colors, R = G = 127.5 -> 128, and its alpha stays 0 (the hidden color is neither cleared nor ignored)
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(255, 0, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(0, 255, 0, 0);
        image.Convolve(new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0])) { PreserveAlpha = true }, Ct);
        AssertRow(image.Frames[0], new Rgba32(255, 0, 0, 255), new Rgba32(128, 128, 0, 0));
    }

    [Fact]
    public void ZeroSumKernelsNeedPreserveAlphaOnFormatsWithAlpha()
    {
        // -1 0 1: out(x) = src(x + 1) - src(x - 1), edges clamped, over (10, 20, 30) (110, 60, 40) (50, 220, 45):
        // pixel 0 = p1 - p0 = (100, 40, 10); pixel 1 = p2 - p0 = (40, 200, 15); pixel 2 = p2 - p1 = (-60 -> 0, 160, 5)
        Rgba32[] source = [new(10, 20, 30, 255), new(110, 60, 40, 255), new(50, 220, 45, 255)];
        var kernel = Kernel(3, 1, [-1, 0, 1]);

        // Alpha is filtered like the colors: 255 - 255 = 0, so every pixel becomes transparent black
        using (var image = Image.ImportPixelData<Rgba32>(source, 3, 1))
        {
            image.Convolve(new ConvolutionOptions(kernel), Ct);
            AssertRow(image.Frames[0], default, default, default);
        }

        using (var image = Image.ImportPixelData<Rgba32>(source, 3, 1))
        {
            image.Convolve(new ConvolutionOptions(kernel) { PreserveAlpha = true }, Ct);
            AssertRow(image.Frames[0], new Rgba32(100, 40, 10, 255), new Rgba32(40, 200, 15, 255), new Rgba32(0, 160, 5, 255));
        }

        // Bgra32 stores the same components in another order
        using (var image = Image.ImportPixelData<Bgra32>([new(10, 20, 30, 255), new(110, 60, 40, 255), new(50, 220, 45, 255)], 3, 1))
        {
            image.Convolve(new ConvolutionOptions(kernel) { PreserveAlpha = true }, Ct);
            AssertRow(image.Frames[0], new Bgra32(100, 40, 10, 255), new Bgra32(40, 200, 15, 255), new Bgra32(0, 160, 5, 255));
        }

        // A format without alpha has nothing to preserve: the option changes nothing
        foreach (var preserveAlpha in new[] { false, true })
        {
            using var image = Image.ImportPixelData<Rgb24>([new(10, 20, 30), new(110, 60, 40), new(50, 220, 45)], 3, 1);
            image.Convolve(new ConvolutionOptions(kernel) { PreserveAlpha = preserveAlpha }, Ct);
            AssertRow(image.Frames[0], new Rgb24(100, 40, 10), new Rgb24(40, 200, 15), new Rgb24(0, 160, 5));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Working space
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LinearWorkingSpaceAveragesLightInsteadOfCodeValues()
    {
        // out(x) = (src(x - 1) + src(x)) / 2 over 0 255. Encoded: 127.5 -> 128. Linear: Encode(0.5) = 1.055 * 0.5^(1/2.4) - 0.055
        // = 0.735357 -> 255 * 0.735357 = 187.52 -> 188, and 65535 * 0.735357 = 48191.62 -> 48192
        var encoded = new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0]));
        var linear = new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0])) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb };
        using (var image = BuildGray8([[0, 255]]))
        {
            image.Convolve(encoded, Ct);
            AssertGray8(image.Frames[0], [[0, 128]]);
        }

        using (var image = BuildGray8([[0, 255]]))
        {
            image.Convolve(linear, Ct);
            AssertGray8(image.Frames[0], [[0, 188]]);
        }

        using (var image = new Image<Gray16>(2, 1))
        {
            image.Frames[0][1, 0] = new Gray16(65535);
            image.Convolve(linear, Ct);
            AssertRow(image.Frames[0], new Gray16(0), new Gray16(48192));
        }

        // Alpha is linear in both spaces and a transparent pixel contributes nothing: (255, 255, 255, 255) then (9, 9, 9, 0)
        // -> A = 127.5 -> 128 and the color stays white. With PreserveAlpha the colors alone are averaged in linear light:
        // Decode(9 / 255) = 0.002732, (1 + 0.002732) / 2 = 0.501366 -> 255 * Encode(0.501366) = 187.75 -> 188
        foreach (var options in new[] { encoded, linear })
        {
            using var image = Image.ImportPixelData<Rgba32>([new(255, 255, 255, 255), new(9, 9, 9, 0)], 2, 1);
            image.Convolve(options, Ct);
            AssertRow(image.Frames[0], new Rgba32(255, 255, 255, 255), new Rgba32(255, 255, 255, 128));
        }

        using (var image = Image.ImportPixelData<Rgba32>([new(255, 255, 255, 255), new(9, 9, 9, 0)], 2, 1))
        {
            image.Convolve(new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0])) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb, PreserveAlpha = true }, Ct);
            AssertRow(image.Frames[0], new Rgba32(255, 255, 255, 255), new Rgba32(188, 188, 188, 0));
        }
    }

    [Fact]
    public void LinearWorkingSpaceFiltersLinearSamplesAsStored()
    {
        // Samples labeled linear light (QOI) are not decoded again: the result is the encoded one, 127.5 -> 128
        using var image = BuildGray8([[0, 255]]);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        image.Convolve(new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0])) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb }, Ct);
        AssertGray8(image.Frames[0], [[0, 128]]);
    }

    [Fact]
    public void LinearWorkingSpaceRequiresUntaggedOrRecognizedSrgbPixels()
    {
        var linear = new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0])) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb };
        var encoded = new ConvolutionOptions(Kernel(3, 1, [0.5, 0.5, 0]));
        var displayP3 = ResizeTests.IccProfiles.Rgb(ResizeTests.IccProfiles.DisplayP3Colorants, ResizeTests.IccProfiles.SrgbParametricCurve());
        using (var image = Image.ImportPixelData<Rgba32>([new(0, 0, 0, 255), new(255, 255, 255, 255)], 2, 1))
        {
            image.Metadata.IccProfile = displayP3;
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Convolve(linear, Ct));
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].Convolve(linear, Ct));
            AssertRow(image.Frames[0], new Rgba32(0, 0, 0, 255), new Rgba32(255, 255, 255, 255));

            // The encoded working space never interprets the profile, which is kept
            image.Convolve(encoded, Ct);
            AssertRow(image.Frames[0], new Rgba32(0, 0, 0, 255), new Rgba32(128, 128, 128, 255));
            Assert.Same(displayP3, image.Metadata.IccProfile);
        }

        var srgb = ResizeTests.IccProfiles.Rgb(ResizeTests.IccProfiles.SrgbColorants, ResizeTests.IccProfiles.SrgbParametricCurve());
        using (var image = Image.ImportPixelData<Rgba32>([new(0, 0, 0, 255), new(255, 255, 255, 255)], 2, 1))
        {
            image.Metadata.IccProfile = srgb;
            image.Convolve(linear, Ct);
            AssertRow(image.Frames[0], new Rgba32(0, 0, 0, 255), new Rgba32(188, 188, 188, 255));
            Assert.Same(srgb, image.Metadata.IccProfile);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Frames, state and resources
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryFrameAndThePosterAreConvolvedAndKeepTheirIdentity()
    {
        // out(x) = src(x - 1), edges clamped: a b c -> a a b
        using var image = BuildGray8([[10, 20, 30]]);
        Fill(image.AppendFrame(), [[40, 50, 60]]);
        Fill(image.SetPosterFrame(image.Frames[0]), [[70, 80, 90]]);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 7);
        var frames = ((IEnumerable<ImageFrame<Gray8>>)image.Frames).ToArray();
        var storages = frames.Select(frame => frame.Storage).ToArray();
        var poster = image.PosterFrame;

        image.Convolve(Options(3, 1, [1, 0, 0]), Ct);

        Assert.Equal(new Size(3, 1), image.Size);
        Assert.Equal(2, image.Frames.Count);
        Assert.Same(frames[0], image.Frames[0]);
        Assert.Same(frames[1], image.Frames[1]);
        Assert.Same(poster, image.PosterFrame);
        Assert.Same(storages[0], image.Frames[0].Storage);
        Assert.Same(storages[1], image.Frames[1].Storage);
        Assert.Equal(new FrameDuration(1, 7), image.Frames[1].Metadata.Duration);
        AssertGray8(image.Frames[0], [[10, 10, 20]]);
        AssertGray8(image.Frames[1], [[40, 40, 50]]);
        AssertGray8(image.PosterFrame!, [[70, 70, 80]]);
    }

    [Fact]
    public void TheFrameOverloadConvolvesOneFrame()
    {
        using var image = BuildGray8([[10, 20, 30]]);
        Fill(image.AppendFrame(), [[40, 50, 60]]);
        Fill(image.SetPosterFrame(image.Frames[0]), [[70, 80, 90]]);

        image.Frames[1].Convolve(Options(3, 1, [1, 0, 0]), Ct);
        AssertGray8(image.Frames[0], [[10, 20, 30]]);
        AssertGray8(image.Frames[1], [[40, 40, 50]]);
        AssertGray8(image.PosterFrame!, [[70, 80, 90]]);

        image.PosterFrame!.Convolve(Options(3, 1, [0, 0, 1]), Ct);
        AssertGray8(image.PosterFrame!, [[80, 90, 90]]);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Gaussian blur
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AGaussianBlurSpreadsAPixelByTheWeightsOfABell()
    {
        // A deviation of 1: the weights exp(-i^2 / 2) of the 7 pixels within 3, divided by their sum 2.50593:
        // 0.004433, 0.054006, 0.242036, 0.399050 and back. One pixel at 65535 becomes 65535 times the product of the
        // weights of its row and of its column, rounded once: 65535 * 0.399050 * (0.004433, 0.054006, 0.242036, 0.399050)
        // is 115.9, 1412.3, 6329.6, 10436.0 in the row and the column of the pixel
        using var image = new Image<Gray16>(9, 9);
        image.Frames[0][4, 4] = new Gray16(65535);
        image.GaussianBlur(1, cancellationToken: Ct);
        int[] center = [0, 116, 1412, 6330, 10436, 6330, 1412, 116, 0];
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(center[i], image.Frames[0][4, i].Value);
            Assert.Equal(center[i], image.Frames[0][i, 4].Value);
        }

        // 65535 * 0.054006 * 0.242036 = 856.6, in the row two above and the column one right of the pixel;
        // 65535 * 0.004433 * 0.004433 = 1.29
        Assert.Equal(857, image.Frames[0][5, 2].Value);
        Assert.Equal(857, image.Frames[0][2, 5].Value);
        Assert.Equal(1, image.Frames[0][1, 1].Value);
        Assert.Equal(0, image.Frames[0][0, 0].Value);

        // The same at 8 bits: 255 * 0.399050 * (0.004433, 0.054006, 0.242036, 0.399050) is 0.45, 5.50, 24.63, 40.61. Two
        // convolutions would round 255 times each weight first (1, 14, 62, 102), then 102 times each weight: 0, 6, 25, 41
        using var small = new Image<Gray8>(7, 7);
        small.Frames[0][3, 3] = new Gray8(255);
        small.GaussianBlur(1, cancellationToken: Ct);
        Assert.Equal([0, 5, 25, 41, 25, 5, 0], CopyRow(small.Frames[0], 3).Select(pixel => (int)pixel.Value));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void AGaussianBlurIsOneConvolutionByTheProductsOfItsWeights()
    {
        var random = new Random(42);
        var noise = new Rgba32[23 * 17];
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        }

        var twoPassDiffers = 0;
        foreach (var sigma in new[] { 0.4, 1, 2.5 })
        {
            // The weights of the definition, from the exponential of the platform, and the matrix of their products: the
            // blur rounds the same sums once, in another order, so a sample can differ by one where the two computations do
            // not round the same way. Two convolutions round after each direction: they differ more often
            var radius = (int)Math.Ceiling(3 * sigma);
            double[] weights = [.. Enumerable.Range(-radius, (2 * radius) + 1).Select(i => Math.Exp(-(i * i) / (2 * sigma * sigma)))];
            var sum = weights.Sum();
            weights = [.. weights.Select(weight => weight / sum)];
            double[] matrix = [.. weights.SelectMany(vertical => weights.Select(horizontal => vertical * horizontal))];
            foreach (var mode in EdgeModes)
            {
                using var blurred = Image.ImportPixelData<Rgba32>(noise, 23, 17);
                using var convolved = Image.ImportPixelData<Rgba32>(noise, 23, 17);
                using var twoPass = Image.ImportPixelData<Rgba32>(noise, 23, 17);
                blurred.GaussianBlur(sigma, mode, cancellationToken: Ct);
                convolved.Convolve(new ConvolutionOptions(Kernel(weights.Length, weights.Length, matrix)) { EdgeMode = mode }, Ct);
                twoPass.Convolve(new ConvolutionOptions(Kernel(weights.Length, 1, weights)) { EdgeMode = mode }, Ct);
                twoPass.Convolve(new ConvolutionOptions(Kernel(1, weights.Length, weights)) { EdgeMode = mode }, Ct);
                var expected = CopyAll(convolved.Frames[0]);
                var actual = CopyAll(blurred.Frames[0]);
                var rounded = CopyAll(twoPass.Frames[0]);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.True(
                        Math.Abs(expected[i].R - actual[i].R) <= 1 && Math.Abs(expected[i].G - actual[i].G) <= 1 && Math.Abs(expected[i].B - actual[i].B) <= 1 && Math.Abs(expected[i].A - actual[i].A) <= 1,
                        $"sigma {sigma}, {mode}, pixel {i}: expected {expected[i]}, actual {actual[i]}");
                    twoPassDiffers += rounded[i] == actual[i] ? 0 : 1;
                }
            }
        }

        Assert.True(twoPassDiffers > 100, $"{twoPassDiffers} pixels of two rounded convolutions differ from the blur");

        // The weights add up to 1: an image of one color keeps it, whatever the deviation
        using var flat = new Image<Rgba32>(20, 15, new Rgba32(12, 200, 99, 180));
        flat.GaussianBlur(7.3, cancellationToken: Ct);
        Assert.All(CopyAll(flat.Frames[0]), pixel => Assert.Equal(new Rgba32(12, 200, 99, 180), pixel));

        // In linear light, the middle of black and white is lighter than their mean
        using var encoded = new Image<Gray8>(40, 1);
        using var linear = new Image<Gray8>(40, 1);
        for (var x = 20; x < 40; x++)
        {
            encoded.Frames[0][x, 0] = linear.Frames[0][x, 0] = new Gray8(255);
        }

        encoded.GaussianBlur(3, cancellationToken: Ct);
        linear.GaussianBlur(3, workingSpace: ConvolutionWorkingSpace.LinearSrgb, cancellationToken: Ct);
        Assert.InRange(encoded.Frames[0][20, 0].Value, 130, 150);
        Assert.InRange(linear.Frames[0][20, 0].Value, 190, 205);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void AGaussianBlurOfAnOpaqueFrameFiltersItsColorsAndKeepsTheMaximumAlpha()
    {
        // The weights add up to 1: an opaque frame is blurred as its colors, each like a gray image of that color, bit for
        // bit, and alpha stays the maximum
        var random = new Random(8);
        var pixels = new Rgba32[23 * 17];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        }

        foreach (var mode in new[] { ConvolutionEdgeMode.Clamp, ConvolutionEdgeMode.Mirror, ConvolutionEdgeMode.Wrap })
        {
            foreach (var sigma in new[] { 0.8, 2.5 })
            {
                using var rgba = Image.ImportPixelData<Rgba32>(pixels, 23, 17);
                rgba.GaussianBlur(sigma, mode, cancellationToken: Ct);
                var result = CopyAll(rgba.Frames[0]);
                Assert.All(result, pixel => Assert.Equal(255, pixel.A));
                for (var c = 0; c < 3; c++)
                {
                    using var gray = Image.ImportPixelData<Gray8>([.. pixels.Select(pixel => new Gray8(Channel(pixel, c)))], 23, 17);
                    gray.GaussianBlur(sigma, mode, cancellationToken: Ct);
                    Assert.Equal(CopyAll(gray.Frames[0]).Select(pixel => (int)pixel.Value), result.Select(pixel => (int)Channel(pixel, c)));
                }
            }
        }

        // Beyond the image, the zero edge mode reads transparent pixels: the corners lose alpha
        using (var zero = Image.ImportPixelData<Rgba32>(pixels, 23, 17))
        {
            zero.GaussianBlur(1, ConvolutionEdgeMode.Zero, cancellationToken: Ct);
            Assert.True(zero.Frames[0][0, 0].A < 255);
        }

        // One transparent pixel: the frame is filtered premultiplied, alpha drops around it and its hidden color does not bleed
        pixels[(8 * 23) + 11] = new Rgba32(255, 0, 255, 0);
        using var red = Image.ImportPixelData<Rgba32>(pixels, 23, 17);
        pixels[(8 * 23) + 11] = new Rgba32(0, 255, 0, 0);
        using var green = Image.ImportPixelData<Rgba32>(pixels, 23, 17);
        red.GaussianBlur(1.5, cancellationToken: Ct);
        green.GaussianBlur(1.5, cancellationToken: Ct);
        Assert.Equal(CopyAll(red.Frames[0]), CopyAll(green.Frames[0]));
        Assert.True(red.Frames[0][11, 8].A < 255);

        // Only the blur takes this path: a matrix may not add up to 1, and the zero edge mode reads beyond the image
        using var opaque = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 255));
        double[] weights = [0.25, 0.5, 0.25];
        using var blur = new ConvolutionPlan(Kernel(1, 3, weights), ConvolutionEdgeMode.Clamp, preserveAlpha: false, linear: false, PixelFormat.Rgba32, new Size(4, 4), rowWeights: weights);
        using var zeroBlur = new ConvolutionPlan(Kernel(1, 3, weights), ConvolutionEdgeMode.Zero, preserveAlpha: false, linear: false, PixelFormat.Rgba32, new Size(4, 4), rowWeights: weights);
        using var matrix = new ConvolutionPlan(Kernel(3, 1, weights), ConvolutionEdgeMode.Clamp, preserveAlpha: false, linear: false, PixelFormat.Rgba32, new Size(4, 4));
        Assert.Equal(WorkingAlpha.Opaque, Convolver.GetWorkingAlpha<Rgba32>(opaque.Frames[0].Storage, blur));
        Assert.Equal(WorkingAlpha.Premultiplied, Convolver.GetWorkingAlpha<Rgba32>(opaque.Frames[0].Storage, zeroBlur));
        Assert.Equal(WorkingAlpha.Premultiplied, Convolver.GetWorkingAlpha<Rgba32>(opaque.Frames[0].Storage, matrix));
        opaque.Frames[0][3, 3] = new Rgba32(1, 2, 3, 254);
        Assert.Equal(WorkingAlpha.Premultiplied, Convolver.GetWorkingAlpha<Rgba32>(opaque.Frames[0].Storage, blur));

        static byte Channel(Rgba32 pixel, int c) => c switch { 0 => pixel.R, 1 => pixel.G, _ => pixel.B };
    }

    [Fact]
    public void AGaussianBlurAppliesToEveryFrameOrToOne()
    {
        using var image = BuildGray8([[0, 0, 0, 255, 0, 0, 0]]);
        Fill(image.AppendFrame(), [[255, 0, 0, 0, 0, 0, 0]]);
        Fill(image.SetPosterFrame(image.Frames[0]), [[0, 0, 0, 0, 0, 0, 255]]);
        var storage = image.Frames[1].Storage;

        // A row of one pixel: its column is itself, with every weight of the column. The edges are extended: the
        // white pixel of an edge is also the three pixels before it, 255 * 0.699525 = 178.4, and it is three, two and
        // one of the pixels its neighbors read: 255 * 0.300475 = 76.6, 255 * 0.058439 = 14.9 and 255 * 0.004433 = 1.1
        image.GaussianBlur(1, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[1, 14, 62, 102, 62, 14, 1]]);
        AssertGray8(image.Frames[1], [[178, 77, 15, 1, 0, 0, 0]]);
        AssertGray8(image.PosterFrame!, [[0, 0, 0, 1, 15, 77, 178]]);
        Assert.Same(storage, image.Frames[1].Storage);

        // Without deviation, nothing changes; one frame can be blurred on its own
        image.GaussianBlur(0, cancellationToken: Ct);
        image.Frames[1].GaussianBlur(0, cancellationToken: Ct);
        AssertGray8(image.Frames[1], [[178, 77, 15, 1, 0, 0, 0]]);
        // Without pixels beyond the row, the column only has the middle weight: 255 * 0.399050 times each weight of the row
        Fill(image.Frames[1], [[0, 0, 0, 255, 0, 0, 0]]);
        image.Frames[1].GaussianBlur(1, ConvolutionEdgeMode.Zero, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[1, 14, 62, 102, 62, 14, 1]]);
        AssertGray8(image.Frames[1], [[0, 5, 25, 41, 25, 5, 0]]);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void AGaussianBlurValidatesItsArgumentsAndChangesNothingWhenItCannotRun()
    {
        using var image = BuildGray8([[10, 20, 30], [40, 50, 60]]);
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).GaussianBlur(1));
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).GaussianBlur(1));
        Assert.Throws<ArgumentException>(() => image.GaussianBlur(double.NaN));
        Assert.Throws<ArgumentException>(() => image.Frames[0].GaussianBlur(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GaussianBlur(-0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GaussianBlur(10000.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GaussianBlur(1, (ConvolutionEdgeMode)4));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].GaussianBlur(1, workingSpace: (ConvolutionWorkingSpace)2));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.GaussianBlur(1, cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].GaussianBlur(1, cancellationToken: canceled.Token));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.GaussianBlur(1)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.GaussianBlur(0)));
        AssertGray8(image.Frames[0], [[10, 20, 30], [40, 50, 60]]);

        // The scratch of both directions is rented before the first one runs: a blur that cannot have it changes nothing
        using var limited = new Image<Gray8>(8, 64, new Gray8(0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 8 * 64 * 4 } });
        limited.Frames[0][4, 32] = new Gray8(255);
        Assert.Throws<ImageResourceLimitException>(() => limited.GaussianBlur(20, cancellationToken: Ct));
        Assert.Equal(255, limited.Frames[0][4, 32].Value);
        Assert.Equal(0, limited.Frames[0][5, 32].Value);
        Assert.Equal(0, limited.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.GaussianBlur(1));
        Assert.Throws<ObjectDisposedException>(() => disposed.GaussianBlur(0));
        Assert.Throws<ObjectDisposedException>(() => frame.GaussianBlur(1));
    }

    [Fact]
    public void SegmentedAndPaddedStoragesGiveTheSameResult()
    {
        // Rows in several slabs with padding between them: the layout never changes the result
        var kernel = Kernel(3, 5, [0.05, -0.1, 0.05, 0.1, 0.15, 0.1, -0.2, 0.7, -0.2, 0.1, 0.15, 0.1, 0.05, -0.1, 0.05]);
        foreach (var mode in EdgeModes)
        {
            var options = new ConvolutionOptions(kernel) { EdgeMode = mode };
            using var contiguous = BuildGradient(13, 11, layout: null);
            using var segmented = BuildGradient(13, 11, new PixelStorageLayoutOptions { RowAlignment = 64, TargetSlabBytes = 256 });
            Assert.HasCountGreaterThan(1, segmented.Frames[0].Storage.GetSlabCapacities());
            contiguous.Convolve(options, Ct);
            segmented.Convolve(options, Ct);
            Assert.Equal(CopyAll(contiguous.Frames[0]), CopyAll(segmented.Frames[0]));
        }
    }

    [Fact]
    public void TheScratchIsChargedToTheImageAndReleased()
    {
        var options = Options(3, 3, [0, 0.125, 0, 0.125, 0.5, 0.125, 0, 0.125, 0]);

        // Measure an unconstrained run: the peak holds the pixels and the scratch at once
        long before, peak;
        using (var probe = BuildWithPool(new SlabPool(), ImageConfiguration.Default))
        {
            before = probe.Owner.Scope.LiveBytes;
            probe.Convolve(options, Ct);
            peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.Equal(before, probe.Owner.Scope.LiveBytes);
            Assert.Equal(0, probe.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        }

        Assert.True(peak > before, "The scratch storage is charged in addition to the pixel storages");

        // One byte short: the operation fails before any pixel or metadata changes
        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } };
        using (var image = BuildWithPool(new SlabPool(), tight))
        {
            var pixels = CopyAllFrames(image);
            var exif = image.Metadata.ExifProfile;
            var exception = Assert.Throws<ImageResourceLimitException>(() => image.Convolve(options, Ct));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            Assert.Throws<ImageResourceLimitException>(() => image.Frames[0].Convolve(options, Ct));
            Assert.Equal(pixels, CopyAllFrames(image));
            Assert.Same(exif, image.Metadata.ExifProfile);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak } };
        using (var image = BuildWithPool(new SlabPool(), exact))
        {
            image.Convolve(options, Ct);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
        }
    }

    [Fact]
    public void AnAllocationFailureLeavesTheImageUnchanged()
    {
        var options = Options(3, 3, [0, 0.125, 0, 0.125, 0.5, 0.125, 0, 0.125, 0]);
        foreach (var operation in new Action<Image<Rgba32>>[] { image => image.Convolve(options, Ct), image => image.Frames[1].Convolve(options, Ct) })
        {
            var pool = new SlabPool();
            using var image = BuildWithPool(pool, ImageConfiguration.Default);
            var pixels = CopyAllFrames(image);
            var exif = image.Metadata.ExifProfile;
            var live = image.Owner.Scope.LiveBytes;
            var rentals = 0;
            pool.RentFailureInjector = _ =>
            {
                rentals++;
                return new InjectedAllocationFailureException();
            };

            Assert.Throws<InjectedAllocationFailureException>(() => operation(image));
            pool.RentFailureInjector = null;
            Assert.Equal(1, rentals);
            Assert.Equal(pixels, CopyAllFrames(image));
            Assert.Same(exif, image.Metadata.ExifProfile);
            Assert.Equal(live, image.Owner.Scope.LiveBytes);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);

            operation(image); // still usable
            Assert.NotEqual(pixels, CopyAllFrames(image));
            Assert.Equal(live, image.Owner.Scope.LiveBytes);
        }
    }

    [Fact]
    public void CancellationBeforeTheFirstRowLeavesTheImageUnchanged()
    {
        var options = Options(3, 3, [0, 0.125, 0, 0.125, 0.5, 0.125, 0, 0.125, 0]);
        using var image = BuildWithPool(new SlabPool(), ImageConfiguration.Default);
        var pixels = CopyAllFrames(image);
        var exif = image.Metadata.ExifProfile;
        var live = image.Owner.Scope.LiveBytes;
        Assert.ThrowsAny<OperationCanceledException>(() => image.Convolve(options, new CancellationToken(canceled: true)));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Frames[0].Convolve(options, new CancellationToken(canceled: true)));
        Assert.Equal(pixels, CopyAllFrames(image));
        Assert.Same(exif, image.Metadata.ExifProfile);
        Assert.Equal(live, image.Owner.Scope.LiveBytes);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void ACanceledConvolutionLeavesAStructurallyValidImage()
    {
        // Cancellation is observed between row bands and frames: canceling when the second frame starts leaves the first
        // frame convolved (100 / 2 = 50) and the second one as it was
        using var image = new Image<Gray8>(2, 200, new Gray8(100));
        image.AppendFrame(image.Frames[0]);
        using var cancellation = new CancellationTokenSource();
        var started = 0;
        Convolver.TestBandObserver = (_, start) =>
        {
            if (start && ++started == 2)
            {
                cancellation.Cancel();
            }
        };

        try
        {
            var exception = Assert.ThrowsAny<OperationCanceledException>(() => image.Convolve(Options(1, 1, [0.5]), cancellation.Token));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
        }
        finally
        {
            Convolver.TestBandObserver = null;
        }

        Assert.Equal(new Size(2, 200), image.Size);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(new Gray8(50), image.Frames[0][1, 199]);
        Assert.Equal(new Gray8(100), image.Frames[1][1, 199]);

        // The image is still usable
        image.Convolve(Options(1, 1, [0.5]), Ct);
        Assert.Equal(new Gray8(25), image.Frames[0][1, 199]);
        Assert.Equal(new Gray8(50), image.Frames[1][0, 0]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Separable matrices
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SeparableMatricesAreTheProductsOfTheirFactors()
    {
        // The weight at (x, y) is column[y] * row[x]
        var kernel = ConvolutionKernel.CreateSeparable([1, 2, 1], [-1, 0, 1, 0.5, 2]);
        Assert.True(kernel.IsSeparable);
        Assert.Equal(3, kernel.Width);
        Assert.Equal(5, kernel.Height);
        Assert.Equal(-2, kernel[1, 0]);
        Assert.Equal(0, kernel[0, 1]);
        Assert.Equal(0.5, kernel[2, 3]);
        Assert.Equal(4, kernel[1, 4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => kernel[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => kernel[0, 5]);

        // A matrix given weight by weight is recognized when every weight is exactly a product: a box, a binomial, Sobel
        Assert.True(Kernel(3, 3, [.. Enumerable.Repeat(1d / 9, 9)]).IsSeparable);
        Assert.True(Kernel(3, 3, [1, 2, 1, 2, 4, 2, 1, 2, 1]).IsSeparable);
        Assert.True(Kernel(3, 3, [-1, 0, 1, -2, 0, 2, -1, 0, 1]).IsSeparable);
        Assert.False(Kernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]).IsSeparable);
        Assert.False(Kernel(3, 3, [1, 2, 1, 2, 4, 2, 1, 2, 1.000001]).IsSeparable);
        Assert.False(Kernel(3, 3, new double[9]).IsSeparable);
        Assert.False(Kernel(5, 1, [1, 2, 3, 2, 1]).IsSeparable);
        Assert.False(Kernel(1, 3, [1, 2, 1]).IsSeparable);

        Assert.Throws<ArgumentException>(() => ConvolutionKernel.CreateSeparable([], [1]));
        Assert.Throws<ArgumentException>(() => ConvolutionKernel.CreateSeparable([1, 1], [1]));
        Assert.Throws<ArgumentException>(() => ConvolutionKernel.CreateSeparable([1], [1, 2, 3, 4]));
        Assert.Throws<ArgumentException>(() => ConvolutionKernel.CreateSeparable([1, double.NaN, 1], [1]));
        Assert.Throws<ArgumentException>(() => ConvolutionKernel.CreateSeparable([1], [double.PositiveInfinity]));
    }

    [Fact]
    public void SeparableMatricesFilterRowsThenColumns()
    {
        // The binomial 1 2 1 / 4 in both directions spreads a white pixel by 255 / 16 * (1, 2, 1, 2, 4, 2, 1, 2, 1):
        // 15.9, 31.9 and 63.75 round to 16, 32 and 64
        using var image = new Image<Gray8>(5, 5);
        image.Frames[0][2, 2] = new Gray8(255);
        image.Convolve(new ConvolutionOptions(ConvolutionKernel.CreateSeparable([0.25, 0.5, 0.25], [0.25, 0.5, 0.25])), Ct);
        AssertGray8(image.Frames[0], [[0, 0, 0, 0, 0], [0, 16, 32, 16, 0], [0, 32, 64, 32, 0], [0, 16, 32, 16, 0], [0, 0, 0, 0, 0]]);

        // Sobel across a vertical edge: the columns on both sides, times 1 + 2 + 1, clamped
        using var edge = BuildGray8([[0, 0, 255, 255], [0, 0, 255, 255], [0, 0, 255, 255]]);
        edge.Convolve(Options(3, 3, [-1, 0, 1, -2, 0, 2, -1, 0, 1]), Ct);
        AssertGray8(edge.Frames[0], [[0, 255, 255, 0], [0, 255, 255, 0], [0, 255, 255, 0]]);
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void SeparableMatricesSkipZeroWeightsLikeTheMatrix(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckSeparableSkipsZeroWeights<RgbaFloat>();
        }
        else
        {
            CheckSeparableSkipsZeroWeights<RgbaHalf>();
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Box blur
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ABoxBlurAveragesTheSquareAroundEveryPixel()
    {
        // A white pixel is one of the 9 pixels of the squares that contain it: 255 / 9 = 28.3
        using var image = new Image<Gray8>(5, 5);
        image.Frames[0][2, 2] = new Gray8(255);
        image.BoxBlur(1, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 0, 0, 0], [0, 28, 28, 28, 0], [0, 28, 28, 28, 0], [0, 28, 28, 28, 0], [0, 0, 0, 0, 0]]);

        // 16-bit samples keep their precision: 65535 / 9 = 7281.7 and 65535 / 25 = 2621.4
        using var deep = new Image<Gray16>(5, 5);
        deep.Frames[0][2, 2] = new Gray16(65535);
        deep.BoxBlur(1, cancellationToken: Ct);
        Assert.Equal(7282, deep.Frames[0][1, 3].Value);
        Assert.Equal(0, deep.Frames[0][0, 3].Value);
        deep.Frames[0][1, 3] = new Gray16(0);
        deep.Frames[0][2, 2] = new Gray16(65535);
        for (var y = 1; y < 4; y++)
        {
            for (var x = 1; x < 4; x++)
            {
                deep.Frames[0][x, y] = new Gray16((ushort)(x == 2 && y == 2 ? 65535 : 0));
            }
        }

        deep.Frames[0].BoxBlur(2, ConvolutionEdgeMode.Zero, cancellationToken: Ct);
        Assert.All(CopyAll(deep.Frames[0]), pixel => Assert.Equal(2621, pixel.Value));

        // An image of one color keeps it, whatever the radius
        using var flat = new Image<Rgba32>(20, 15, new Rgba32(12, 200, 99, 180));
        flat.BoxBlur(37, cancellationToken: Ct);
        Assert.All(CopyAll(flat.Frames[0]), pixel => Assert.Equal(new Rgba32(12, 200, 99, 180), pixel));
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp, new[] { 30, 90, 175, 230 })]
    [InlineData(ConvolutionEdgeMode.Mirror, new[] { 30, 90, 175, 230 })]
    [InlineData(ConvolutionEdgeMode.Wrap, new[] { 115, 90, 175, 145 })]
    [InlineData(ConvolutionEdgeMode.Zero, new[] { 10, 30, 58, 48 })]
    public void ABoxBlurReadsBeyondTheEdgesByTheEdgeMode(ConvolutionEdgeMode mode, int[] expected)
    {
        // One row 0 90 180 255: the rows above and below are the row itself, except for Zero (9 samples, 6 of them 0).
        // Clamp and Mirror read 0 left and 255 right: (0 + 0 + 90) / 3 = 30 ... (180 + 255 + 255) / 3 = 230; Wrap reads
        // 255 left and 0 right: (255 + 0 + 90) / 3 = 115 and (180 + 255 + 0) / 3 = 145; Zero: 90 / 9, 270 / 9, 525 / 9
        // and 435 / 9
        using var image = BuildGray8([[0, 90, 180, 255]]);
        image.BoxBlur(1, mode, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [expected]);

        // The square is 11 pixels wide, almost three periods of the row: Wrap averages 255 0 90 180 255 0 90 180 255 0 90
        // for the first pixel, 1395 / 11 = 126.8
        using var wide = BuildGray8([[0, 90, 180, 255]]);
        wide.BoxBlur(5, ConvolutionEdgeMode.Wrap, cancellationToken: Ct);
        Assert.Equal(127, wide.Frames[0][0, 0].Value);
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp, 1)]
    [InlineData(ConvolutionEdgeMode.Clamp, 500)]
    [InlineData(ConvolutionEdgeMode.Mirror, 1)]
    [InlineData(ConvolutionEdgeMode.Mirror, 500)]
    [InlineData(ConvolutionEdgeMode.Wrap, 1)]
    [InlineData(ConvolutionEdgeMode.Wrap, 500)]
    [InlineData(ConvolutionEdgeMode.Zero, 1)]
    [InlineData(ConvolutionEdgeMode.Zero, 7)]
    [InlineData(ConvolutionEdgeMode.Zero, 10000)]
    public void ABoxBlurOfOnePixelReadsOnlyThatPixelOrZeros(ConvolutionEdgeMode mode, int radius)
    {
        // Clamp, Mirror and Wrap fill the whole square with the pixel; Zero reads it once among (2 radius + 1)^2 pixels
        using var image = new Image<Rgba32>(1, 1, new Rgba32(10, 20, 30, 180));
        using var deep = new Image<Gray16>(1, 1, new Gray16(65535));
        image.BoxBlur(radius, mode, cancellationToken: Ct);
        deep.BoxBlur(radius, mode, cancellationToken: Ct);
        if (mode is ConvolutionEdgeMode.Zero)
        {
            // 65535 / 9 = 7281.7, 65535 / 225 = 291.3, 65535 / 20001^2 rounds to 0
            var expected = radius switch { 1 => 7282, 7 => 291, _ => 0 };
            Assert.Equal(expected, deep.Frames[0][0, 0].Value);

            // The colors are filtered premultiplied by alpha: the only opaque contribution keeps them; 180 / 9 = 20
            if (radius is 1)
            {
                Assert.Equal(new Rgba32(10, 20, 30, 20), image.Frames[0][0, 0]);
            }
        }
        else
        {
            Assert.Equal(new Rgba32(10, 20, 30, 180), image.Frames[0][0, 0]);
            Assert.Equal(65535, deep.Frames[0][0, 0].Value);
        }
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp, new[] { 108, 125, 142 })]
    [InlineData(ConvolutionEdgeMode.Mirror, new[] { 98, 115, 132 })]
    [InlineData(ConvolutionEdgeMode.Wrap, new[] { 115, 115, 115 })]
    [InlineData(ConvolutionEdgeMode.Zero, new[] { 2, 2, 2 })]
    public void ABoxBlurWiderThanTheImageRepeatsTheEdgeModeAcrossTheSquare(ConvolutionEdgeMode mode, int[] expected)
    {
        // 0 90 255 with a radius of 7: the 15 pixels of a square span the row five times. Clamp reads 0 seven times on the
        // left: (8 * 0 + 90 + 6 * 255) / 15 = 108. Mirror reflects again at each copy (... 90 0 | 0 90 255 | 255 90 0 0 ...):
        // the first square holds 0, 90 and 255 six, five and four times, (450 + 1020) / 15 = 98. Wrap reads five whole
        // periods: 345 / 15 = 115.
        // Zero reads the row once and the other rows and columns are zero: 345 / 225 = 1.5
        using var row = BuildGray8([[0, 90, 255]]);
        row.BoxBlur(7, mode, cancellationToken: Ct);
        AssertGray8(row.Frames[0], [expected]);

        using var column = BuildGray8([[0], [90], [255]]);
        column.BoxBlur(7, mode, cancellationToken: Ct);
        AssertGray8(column.Frames[0], [[expected[0]], [expected[1]], [expected[2]]]);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ABoxBlurIsTheConvolutionByEqualWeights()
    {
        var random = new Random(21);
        var noise = new Rgba32[23 * 17];
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)(i % 5 == 0 ? random.Next(256) : 255));
        }

        foreach (var radius in new[] { 1, 3, 9 })
        {
            var size = (2 * radius) + 1;
            double[] matrix = [.. Enumerable.Repeat(1d / (size * size), size * size)];
            foreach (var mode in EdgeModes)
            {
                using var blurred = Image.ImportPixelData<Rgba32>(noise, 23, 17);
                using var convolved = Image.ImportPixelData<Rgba32>(noise, 23, 17);
                blurred.BoxBlur(radius, mode, cancellationToken: Ct);
                convolved.Convolve(new ConvolutionOptions(Kernel(size, size, matrix)) { EdgeMode = mode }, Ct);
                var expected = CopyAll(convolved.Frames[0]);
                var actual = CopyAll(blurred.Frames[0]);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.True(
                        Math.Abs(expected[i].R - actual[i].R) <= 1 && Math.Abs(expected[i].G - actual[i].G) <= 1 && Math.Abs(expected[i].B - actual[i].B) <= 1 && Math.Abs(expected[i].A - actual[i].A) <= 1,
                        $"radius {radius}, {mode}, pixel {i}: expected {expected[i]}, actual {actual[i]}");
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ABoxBlurOfAnOpaqueFrameKeepsTheMaximumAlphaAndHiddenColorsDoNotBleed()
    {
        var random = new Random(9);
        var pixels = new Rgba32[19 * 13];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        }

        using (var opaque = Image.ImportPixelData<Rgba32>(pixels, 19, 13))
        {
            opaque.BoxBlur(2, ConvolutionEdgeMode.Mirror, cancellationToken: Ct);
            Assert.All(CopyAll(opaque.Frames[0]), pixel => Assert.Equal(255, pixel.A));
        }

        using (var zero = Image.ImportPixelData<Rgba32>(pixels, 19, 13))
        {
            // Beyond the image, the zero edge mode reads transparent pixels: 4 of the 9 pixels of the corner square
            zero.BoxBlur(1, ConvolutionEdgeMode.Zero, cancellationToken: Ct);
            Assert.Equal(113, zero.Frames[0][0, 0].A);
        }

        pixels[(6 * 19) + 9] = new Rgba32(255, 0, 255, 0);
        using var red = Image.ImportPixelData<Rgba32>(pixels, 19, 13);
        pixels[(6 * 19) + 9] = new Rgba32(0, 255, 0, 0);
        using var green = Image.ImportPixelData<Rgba32>(pixels, 19, 13);
        red.BoxBlur(2, cancellationToken: Ct);
        green.BoxBlur(2, cancellationToken: Ct);
        Assert.Equal(CopyAll(red.Frames[0]), CopyAll(green.Frames[0]));

        // 24 opaque pixels and a transparent one: 255 * 24 / 25 = 244.8
        Assert.Equal(245, red.Frames[0][9, 6].A);
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void ABoxBlurOnlySpreadsNonFiniteSamplesToTheSquaresThatContainThem(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckBoxBlurNonFinite<RgbaFloat>();
        }
        else
        {
            CheckBoxBlurNonFinite<RgbaHalf>();
        }
    }

    [Fact]
    public void ABoxBlurAppliesToEveryFrameOrToOneAndValidatesItsArguments()
    {
        using var image = BuildGray8([[0, 0, 0, 90, 0, 0, 0]]);
        Fill(image.AppendFrame(), [[90, 0, 0, 0, 0, 0, 0]]);
        Fill(image.SetPosterFrame(image.Frames[0]), [[0, 0, 0, 0, 0, 0, 90]]);

        // One row, Clamp: (a + b + c) / 3 along the row, the edge pixel read twice at the edges
        image.BoxBlur(1, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 30, 30, 30, 0, 0]]);
        AssertGray8(image.Frames[1], [[60, 30, 0, 0, 0, 0, 0]]);
        AssertGray8(image.PosterFrame!, [[0, 0, 0, 0, 0, 30, 60]]);
        image.BoxBlur(0, cancellationToken: Ct);
        image.Frames[1].BoxBlur(0, cancellationToken: Ct);
        AssertGray8(image.Frames[1], [[60, 30, 0, 0, 0, 0, 0]]);
        image.Frames[1].BoxBlur(1, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 30, 30, 30, 0, 0]]);
        AssertGray8(image.Frames[1], [[50, 30, 10, 0, 0, 0, 0]]);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        Assert.Throws<ArgumentNullException>(() => ((Image)null!).BoxBlur(1));
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).BoxBlur(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.BoxBlur(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].BoxBlur(10001));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.BoxBlur(1, (ConvolutionEdgeMode)4));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].BoxBlur(1, workingSpace: (ConvolutionWorkingSpace)2));
        Assert.Throws<OperationCanceledException>(() => image.BoxBlur(1, cancellationToken: new CancellationToken(canceled: true)));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].BoxBlur(1, cancellationToken: new CancellationToken(canceled: true)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.BoxBlur(1)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.BoxBlur(0)));
        AssertGray8(image.Frames[0], [[0, 0, 30, 30, 30, 0, 0]]);

        // In linear light, a third of white is 0.333, encoded as 156, where the mean code value is 85
        using var linear = BuildGray8([[0, 255, 0]]);
        linear.BoxBlur(1, workingSpace: ConvolutionWorkingSpace.LinearSrgb, cancellationToken: Ct);
        Assert.Equal(156, linear.Frames[0][1, 0].Value);

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.BoxBlur(1));
        Assert.Throws<ObjectDisposedException>(() => disposed.BoxBlur(0));
        Assert.Throws<ObjectDisposedException>(() => frame.BoxBlur(1));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Unsharp mask
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 0, new[] { 50, 50, 34, 216, 200, 200 })]
    [InlineData(2, 0, new[] { 50, 50, 18, 232, 200, 200 })]
    [InlineData(1, 0.05, new[] { 50, 50, 34, 216, 200, 200 })]
    [InlineData(1, 0.1, new[] { 50, 50, 50, 200, 200, 200 })]
    [InlineData(0, 0, new[] { 50, 50, 50, 200, 200, 200 })]
    public void SharpeningAddsTheDifferenceFromTheBlur(double amount, double threshold, int[] expected)
    {
        // A deviation of 0.5: the weights 0.000264, 0.106451, 0.786571, 0.106451, 0.000264. One row: the blur is the one of
        // the row, 66.007 left of the edge and 183.993 right of it (50.040 and 199.960 one pixel further). o + a (o - b):
        // 50 - 16.007 a and 200 + 16.007 a; one pixel further, the difference 0.04 rounds away. A threshold of 0.1 (25.5)
        // keeps the differences of 16, one of 0.05 (12.75) does not
        using var image = BuildGray8([[50, 50, 50, 200, 200, 200]]);
        image.Sharpen(0.5, amount, threshold, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [expected]);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void SharpeningClampsKeepsFlatAreasAndTheAlphaOfOpaqueFrames()
    {
        // The overshoot beyond black and white is clipped
        using var image = BuildGray8([[0, 0, 0, 255, 255, 255]]);
        image.Sharpen(1, 3, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 0, 255, 255, 255]]);

        using var flat = new Image<Rgba64>(9, 7, new Rgba64(1000, 30000, 65000, 40000));
        flat.Sharpen(2, 5, cancellationToken: Ct);
        Assert.All(CopyAll(flat.Frames[0]), pixel => Assert.Equal(new Rgba64(1000, 30000, 65000, 40000), pixel));

        var random = new Random(4);
        var pixels = new Rgba32[11 * 9];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        }

        using var opaque = Image.ImportPixelData<Rgba32>(pixels, 11, 9);
        opaque.Sharpen(1, 2, cancellationToken: Ct);
        Assert.All(CopyAll(opaque.Frames[0]), pixel => Assert.Equal(255, pixel.A));
        Assert.NotEqual(pixels, CopyAll(opaque.Frames[0]));

        // Floating-point colors are not clipped: the overshoot of a step from 0 to 1 is kept
        using var hdr = new Image<RgbaFloat>(6, 1, new RgbaFloat(0, 0, 0, 1));
        for (var x = 3; x < 6; x++)
        {
            hdr.Frames[0][x, 0] = new RgbaFloat(1, 1, 1, 1);
        }

        hdr.Sharpen(0.5, cancellationToken: Ct);
        Assert.InRange(hdr.Frames[0][2, 0].R, -0.1074f, -0.1066f);
        Assert.InRange(hdr.Frames[0][3, 0].R, 1.1066f, 1.1074f);
        Assert.Equal(1, hdr.Frames[0][3, 0].A);
    }

    [Fact]
    public void SharpeningAppliesToEveryFrameOrToOneAndValidatesItsArguments()
    {
        using var image = BuildGray8([[50, 50, 50, 200, 200, 200]]);
        Fill(image.AppendFrame(), [[50, 50, 50, 200, 200, 200]]);
        image.Sharpen(0.5, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[50, 50, 34, 216, 200, 200]]);
        AssertGray8(image.Frames[1], [[50, 50, 34, 216, 200, 200]]);
        Fill(image.Frames[1], [[50, 50, 50, 200, 200, 200]]);
        image.Frames[1].Sharpen(0.5, 2, cancellationToken: Ct);
        image.Frames[1].Sharpen(0, 2, cancellationToken: Ct);
        image.Sharpen(0.5, 0, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[50, 50, 34, 216, 200, 200]]);
        AssertGray8(image.Frames[1], [[50, 50, 18, 232, 200, 200]]);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));

        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Sharpen(1));
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).Sharpen(1));
        Assert.Throws<ArgumentException>(() => image.Sharpen(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Sharpen(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Sharpen(10001));
        Assert.Throws<ArgumentException>(() => image.Sharpen(1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].Sharpen(1, -0.5));
        Assert.Throws<ArgumentException>(() => image.Sharpen(1, 1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Sharpen(1, 1, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].Sharpen(1, 1, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Sharpen(1, edgeMode: (ConvolutionEdgeMode)4));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Sharpen(1, workingSpace: (ConvolutionWorkingSpace)2));
        Assert.Throws<OperationCanceledException>(() => image.Sharpen(1, cancellationToken: new CancellationToken(canceled: true)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Sharpen(1)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Sharpen(1, 0)));
        AssertGray8(image.Frames[0], [[50, 50, 34, 216, 200, 200]]);

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.Sharpen(1));
        Assert.Throws<ObjectDisposedException>(() => disposed.Sharpen(0));
        Assert.Throws<ObjectDisposedException>(() => frame.Sharpen(1));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Median
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AMedianRemovesIsolatedDotsAndKeepsEdges()
    {
        // A white dot and a black dot in gray: every square has at most one of them among 9 grays
        using var image = new Image<Gray8>(5, 5, new Gray8(100));
        image.Frames[0][2, 2] = new Gray8(255);
        image.Frames[0][0, 4] = new Gray8(0);
        image.MedianBlur(1, cancellationToken: Ct);
        Assert.All(CopyAll(image.Frames[0]), pixel => Assert.Equal(100, pixel.Value));

        // A vertical edge: the squares next to it hold 6 pixels of their own side and 3 of the other one
        using var edge = BuildGray8([[0, 0, 0, 255, 255, 255], [0, 0, 0, 255, 255, 255], [0, 0, 0, 255, 255, 255]]);
        edge.MedianBlur(1, cancellationToken: Ct);
        AssertGray8(edge.Frames[0], [[0, 0, 0, 255, 255, 255], [0, 0, 0, 255, 255, 255], [0, 0, 0, 255, 255, 255]]);

        // 16-bit samples: the median is one of them, exactly
        using var deep = new Image<Gray16>(3, 1);
        deep.Frames[0][0, 0] = new Gray16(1001);
        deep.Frames[0][1, 0] = new Gray16(65535);
        deep.Frames[0][2, 0] = new Gray16(1003);
        deep.MedianBlur(1, cancellationToken: Ct);
        Assert.Equal([1001, 1003, 1003], CopyRow(deep.Frames[0], 0).Select(pixel => (int)pixel.Value));

        // One pixel: Clamp reads it 9 times, Zero reads it once among 8 zeros
        using var single = BuildGray8([[200]]);
        single.MedianBlur(1, cancellationToken: Ct);
        Assert.Equal(200, single.Frames[0][0, 0].Value);
        single.MedianBlur(1, ConvolutionEdgeMode.Zero, Ct);
        Assert.Equal(0, single.Frames[0][0, 0].Value);
    }

    [Fact]
    public void AMedianFiltersEverySampleOnItsOwnAlphaIncluded()
    {
        // One row: every column is read three times. The middle pixel: R 10 20 30, G 200 100 0, B 0 50 255, A 255 0 128
        using var image = new Image<Rgba32>(3, 1);
        image.Frames[0][0, 0] = new Rgba32(10, 200, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(20, 100, 50, 0);
        image.Frames[0][2, 0] = new Rgba32(30, 0, 255, 128);
        image.MedianBlur(1, cancellationToken: Ct);
        Assert.Equal(new Rgba32(10, 200, 0, 255), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(20, 100, 50, 128), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(30, 0, 255, 128), image.Frames[0][2, 0]);
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void AMedianOrdersFloatingPointSamplesAsNumbers(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointMedian<RgbaFloat>();
        }
        else
        {
            CheckFloatingPointMedian<RgbaHalf>();
        }
    }

    /// <summary>
    /// The medians of 32-bit floating-point samples are counted in two trees (the high 16 bits of every sample, then the
    /// low 16 bits of the group of the median), and a radius of 1 sorts its windows: both give the sample of the middle
    /// rank of a sorted window, for samples spread over every value, in a single group, along a gradient that changes
    /// groups every few pixels, or among a few groups.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void AFloatingPointMedianIsTheSampleOfTheMiddleRankOfItsWindow(int distribution)
    {
        const int Width = 37;
        const int Height = 23;
        var random = new Random(3754 + distribution);
        var source = new RgbaFloat[Width * Height];
        float[] groups = [-2.5f, 0.1f, 0.75f, 1e30f];
        for (var i = 0; i < source.Length; i++)
        {
            var samples = new float[4];
            for (var c = 0; c < 4; c++)
            {
                samples[c] = distribution switch
                {
                    // Any bits: negative numbers, zeros of both signs, subnormals, infinities, NaN of both signs
                    0 => BitConverter.UInt32BitsToSingle((uint)random.NextInt64(0, 1L << 32)),

                    // The 65,536 floats from 0.5 that share their high 16 bits
                    1 => BitConverter.UInt32BitsToSingle(0x3F00_0000u + (uint)random.Next(65536)),

                    // A gradient with a little noise
                    2 => ((float)(i % Width) / Width) + (float)(random.NextDouble() * 0.01),

                    // A few groups, with any low bits
                    _ => BitConverter.UInt32BitsToSingle((BitConverter.SingleToUInt32Bits(groups[random.Next(groups.Length)]) & 0xFFFF_0000u) | (uint)random.Next(65536)),
                };
            }

            source[i] = new RgbaFloat(samples[0], samples[1], samples[2], samples[3]);
        }

        foreach (var radius in new[] { 1, 2, 3, 6 })
        {
            foreach (var mode in Enum.GetValues<ConvolutionEdgeMode>())
            {
                using var image = Image.ImportPixelData<RgbaFloat>(source, Width, Height);
                image.MedianBlur(radius, mode, Ct);
                var actual = CopyAll(image.Frames[0]);
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            var expected = ReferenceFloatMedian(source, Width, Height, x, y, c, radius, mode);
                            Assert.Equal(expected, BitConverter.SingleToUInt32Bits(GetSample(actual[(y * Width) + x], c)), string.Create(CultureInfo.InvariantCulture, $"radius {radius}, {mode}, pixel ({x}, {y}), sample {c}"));
                        }
                    }
                }
            }
        }

        static float GetSample(RgbaFloat pixel, int channel) => channel switch
        {
            0 => pixel.R,
            1 => pixel.G,
            2 => pixel.B,
            _ => pixel.A,
        };

        // The bits of the sample of rank (n - 1) / 2 of the window, in the order of numbers (-0 below +0, every sample
        // that is not a number above the infinities), a sample that is not a number coming back positive
        static uint ReferenceFloatMedian(RgbaFloat[] source, int width, int height, int x, int y, int channel, int radius, ConvolutionEdgeMode mode)
        {
            var window = new List<uint>();
            for (var j = -radius; j <= radius; j++)
            {
                for (var i = -radius; i <= radius; i++)
                {
                    var sx = MapIndex(x + i, width, mode);
                    var sy = MapIndex(y + j, height, mode);
                    var bits = sx < 0 || sy < 0 ? 0u : BitConverter.SingleToUInt32Bits(GetSample(source[(sy * width) + sx], channel));
                    window.Add(float.IsNaN(BitConverter.UInt32BitsToSingle(bits)) ? bits & 0x7FFF_FFFFu : bits);
                }
            }

            window.Sort((a, b) => OrderKey(a).CompareTo(OrderKey(b)));
            return window[(window.Count - 1) / 2];
        }

        static uint OrderKey(uint bits) => (bits & 0x8000_0000u) != 0 ? ~bits : bits | 0x8000_0000u;

        static int MapIndex(int index, int length, ConvolutionEdgeMode mode)
        {
            if (index >= 0 && index < length)
                return index;

            var period = 2 * length;
            var mirrored = ((index % period) + period) % period;
            return mode switch
            {
                ConvolutionEdgeMode.Clamp => index < 0 ? 0 : length - 1,
                ConvolutionEdgeMode.Mirror => mirrored < length ? mirrored : period - 1 - mirrored,
                ConvolutionEdgeMode.Wrap => ((index % length) + length) % length,
                _ => -1,
            };
        }
    }

    [Fact]
    public void AMedianAppliesToEveryFrameOrToOneAndValidatesItsArguments()
    {
        using var image = BuildGray8([[0, 9, 0, 9, 9, 0]]);
        Fill(image.AppendFrame(), [[9, 0, 9, 0, 0, 9]]);
        Fill(image.SetPosterFrame(image.Frames[0]), [[5, 5, 1, 5, 1, 1]]);

        // One row, Clamp: the median of three neighbors
        image.MedianBlur(1, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 9, 9, 9, 0]]);
        AssertGray8(image.Frames[1], [[9, 9, 0, 0, 0, 9]]);
        AssertGray8(image.PosterFrame!, [[5, 5, 5, 1, 1, 1]]);
        image.MedianBlur(0, cancellationToken: Ct);
        Fill(image.Frames[1], [[9, 0, 9, 0, 0, 9]]);
        image.Frames[1].MedianBlur(1, ConvolutionEdgeMode.Wrap, Ct);
        image.Frames[1].MedianBlur(0, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 9, 9, 9, 0]]);
        AssertGray8(image.Frames[1], [[9, 9, 0, 0, 0, 9]]);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        Assert.Throws<ArgumentNullException>(() => ((Image)null!).MedianBlur(1));
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).MedianBlur(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.MedianBlur(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].MedianBlur(1001));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.MedianBlur(1, (ConvolutionEdgeMode)4));
        Assert.Throws<OperationCanceledException>(() => image.MedianBlur(1, cancellationToken: new CancellationToken(canceled: true)));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].MedianBlur(1, cancellationToken: new CancellationToken(canceled: true)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.MedianBlur(1)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.MedianBlur(0)));
        AssertGray8(image.Frames[0], [[0, 0, 9, 9, 9, 0]]);

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.MedianBlur(1));
        Assert.Throws<ObjectDisposedException>(() => disposed.MedianBlur(0));
        Assert.Throws<ObjectDisposedException>(() => frame.MedianBlur(1));
    }

    /// <summary>
    /// The outputs of OpenCV 5.0.0 (opencv-python-headless, Python 3.12) for one 9x7 gray image, kept as literals:
    /// <c>cv2.blur(img, (3, 3), borderType=cv2.BORDER_REFLECT)</c>, <c>cv2.blur(img, (5, 5), borderType=cv2.BORDER_CONSTANT)</c>,
    /// <c>cv2.medianBlur(img, 3)</c> and <c>cv2.medianBlur(img, 5)</c> (whose border is <c>BORDER_REPLICATE</c>), and the
    /// unsharp mask <c>floor(f + 1.5 (f - b) + 0.5)</c> clipped to [0, 255], where <c>b</c> is
    /// <c>cv2.GaussianBlur(f, (7, 7), 1.0, borderType=cv2.BORDER_REPLICATE)</c> of the image as doubles. Every sample is equal.
    /// </summary>
    [Fact]
    public void BoxBlursMediansAndUnsharpMasksMatchOpenCv()
    {
        int[][] source =
        [
            [12, 40, 80, 120, 160, 200, 230, 250, 255],
            [20, 50, 255, 130, 170, 205, 235, 0, 250],
            [30, 60, 100, 140, 180, 210, 240, 245, 248],
            [200, 190, 180, 0, 160, 150, 140, 130, 120],
            [210, 200, 190, 180, 170, 160, 150, 140, 255],
            [60, 60, 60, 60, 220, 220, 220, 220, 220],
            [0, 60, 60, 60, 220, 220, 220, 220, 90],
        ];

        Check(image => image.BoxBlur(1, ConvolutionEdgeMode.Mirror, cancellationToken: Ct),
        [
            [24, 65, 102, 142, 163, 199, 200, 217, 224],
            [30, 72, 108, 148, 168, 203, 202, 217, 222],
            [89, 121, 123, 146, 149, 188, 173, 179, 179],
            [148, 151, 138, 144, 150, 173, 174, 185, 196],
            [154, 150, 124, 136, 147, 177, 170, 177, 187],
            [96, 100, 103, 136, 168, 200, 197, 193, 190],
            [33, 47, 60, 113, 167, 220, 220, 191, 162],
        ]);
        Check(image => image.BoxBlur(2, ConvolutionEdgeMode.Zero, cancellationToken: Ct),
        [
            [26, 41, 62, 84, 106, 109, 123, 103, 78],
            [49, 64, 91, 111, 131, 132, 151, 124, 94],
            [73, 95, 129, 147, 165, 164, 186, 153, 116],
            [75, 95, 131, 148, 165, 163, 186, 150, 113],
            [66, 84, 122, 140, 156, 171, 191, 153, 114],
            [59, 71, 102, 113, 122, 130, 146, 115, 85],
            [36, 48, 72, 86, 96, 107, 118, 93, 69],
        ]);
        Check(image => image.MedianBlur(1, cancellationToken: Ct),
        [
            [20, 40, 80, 130, 160, 200, 230, 250, 250],
            [30, 50, 100, 140, 170, 205, 230, 245, 250],
            [50, 100, 130, 160, 160, 180, 205, 235, 245],
            [200, 190, 180, 170, 160, 160, 150, 150, 245],
            [200, 190, 180, 170, 160, 160, 150, 150, 220],
            [60, 60, 60, 170, 180, 220, 220, 220, 220],
            [60, 60, 60, 60, 220, 220, 220, 220, 220],
        ]);
        Check(image => image.MedianBlur(2, cancellationToken: Ct),
        [
            [30, 40, 80, 130, 170, 200, 230, 248, 250],
            [40, 60, 120, 150, 160, 170, 205, 235, 248],
            [80, 120, 160, 160, 170, 160, 180, 230, 248],
            [60, 100, 160, 170, 170, 170, 205, 220, 220],
            [60, 60, 140, 160, 170, 180, 220, 220, 220],
            [60, 60, 60, 160, 170, 180, 220, 220, 150],
            [60, 60, 60, 160, 190, 220, 220, 220, 220],
        ]);
        Check(image => image.Sharpen(1, 1.5, cancellationToken: Ct),
        [
            [0, 10, 49, 101, 154, 208, 255, 255, 255],
            [0, 7, 255, 109, 173, 223, 255, 0, 255],
            [0, 0, 60, 142, 209, 249, 255, 255, 255],
            [255, 255, 242, 0, 173, 122, 91, 62, 21],
            [255, 255, 255, 244, 187, 140, 112, 80, 255],
            [15, 7, 0, 0, 255, 255, 253, 255, 255],
            [0, 66, 39, 0, 255, 239, 235, 255, 0],
        ]);

        void Check(Action<Image> operation, int[][] expected)
        {
            using var image = BuildGray8(source);
            operation(image);
            AssertGray8(image.Frames[0], expected);
        }
    }

    [Fact]
    public void TheScratchOfBoxBlursMediansAndUnsharpMasksIsChargedAndAFailureChangesNothing()
    {
        // The frames are gradients, which a median keeps: the zero edge mode changes their edges
        foreach (var operation in new Action<Image<Rgba32>>[] { image => image.BoxBlur(2, cancellationToken: Ct), image => image.Frames[1].MedianBlur(1, ConvolutionEdgeMode.Zero, Ct), image => image.Sharpen(1, cancellationToken: Ct), image => image.DetectEdges(EdgeDetector.Kirsch, cancellationToken: Ct), image => image.Frames[1].DetectEdges(EdgeDetector.Sobel, grayscale: false, cancellationToken: Ct) })
        {
            long before, peak;
            using (var probe = BuildWithPool(new SlabPool(), ImageConfiguration.Default))
            {
                before = probe.Owner.Scope.LiveBytes;
                operation(probe);
                peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
                Assert.Equal(before, probe.Owner.Scope.LiveBytes);
            }

            Assert.True(peak > before, "The scratch storage is charged in addition to the pixel storages");
            using (var image = BuildWithPool(new SlabPool(), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } }))
            {
                var pixels = CopyAllFrames(image);
                var exif = image.Metadata.ExifProfile;
                Assert.Throws<ImageResourceLimitException>(() => operation(image));
                Assert.Equal(pixels, CopyAllFrames(image));
                Assert.Same(exif, image.Metadata.ExifProfile);
                Assert.Equal(before, image.Owner.Scope.LiveBytes);
                Assert.Equal(0, image.Owner.ActiveLeaseCount);
            }

            var pool = new SlabPool();
            using (var image = BuildWithPool(pool, ImageConfiguration.Default))
            {
                var pixels = CopyAllFrames(image);
                pool.RentFailureInjector = _ => new InjectedAllocationFailureException();
                Assert.Throws<InjectedAllocationFailureException>(() => operation(image));
                pool.RentFailureInjector = null;
                Assert.Equal(pixels, CopyAllFrames(image));
                Assert.Equal(before, image.Owner.Scope.LiveBytes);
                operation(image);
                Assert.NotEqual(pixels, CopyAllFrames(image));
                Assert.Equal(before, image.Owner.Scope.LiveBytes);
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Ready-made matrices, bias and edge detection
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The presets hold the published coefficients: the operators of Sobel and Feldman, Prewitt and Scharr, the Laplacians
    /// of OpenCV's <c>cv2.Laplacian</c> (<c>ksize = 1</c> and <c>ksize = 5</c>, documented as the sum of the second
    /// derivatives of the Sobel operator), the classic 5x5 Laplacian of Gaussian with the sign of the Laplacian, and the
    /// usual emboss and sharpen matrices. <c>cv2.filter2D</c> of OpenCV 5.0.0 with the matrices below gives exactly
    /// <c>cv2.Sobel</c>, <c>cv2.Scharr</c> and <c>cv2.Laplacian</c> (checked on the image of
    /// <see cref="EdgeDetectorsAndTheBiasMatchOpenCv"/>).
    /// </summary>
    [Fact]
    public void PresetsHaveThePublishedCoefficients()
    {
        Check(ConvolutionKernel.SobelX, 3, [-1, 0, 1, -2, 0, 2, -1, 0, 1]);
        Check(ConvolutionKernel.SobelY, 3, [-1, -2, -1, 0, 0, 0, 1, 2, 1]);
        Check(ConvolutionKernel.ScharrX, 3, [-3, 0, 3, -10, 0, 10, -3, 0, 3]);
        Check(ConvolutionKernel.ScharrY, 3, [-3, -10, -3, 0, 0, 0, 3, 10, 3]);
        Check(ConvolutionKernel.PrewittX, 3, [-1, 0, 1, -1, 0, 1, -1, 0, 1]);
        Check(ConvolutionKernel.PrewittY, 3, [-1, -1, -1, 0, 0, 0, 1, 1, 1]);
        Check(ConvolutionKernel.Laplacian3x3, 3, [0, 1, 0, 1, -4, 1, 0, 1, 0]);
        Check(ConvolutionKernel.Laplacian5x5, 5, [2, 4, 4, 4, 2, 4, 0, -8, 0, 4, 4, -8, -24, -8, 4, 4, 0, -8, 0, 4, 2, 4, 4, 4, 2]);
        Check(ConvolutionKernel.LaplacianOfGaussian, 5, [0, 0, 1, 0, 0, 0, 1, 2, 1, 0, 1, 2, -16, 2, 1, 0, 1, 2, 1, 0, 0, 0, 1, 0, 0]);
        Check(ConvolutionKernel.Emboss, 3, [-2, -1, 0, -1, 1, 1, 0, 1, 2]);
        Check(ConvolutionKernel.Sharpen3x3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);

        // The gradients are separable: a smoothing times a derivative
        Assert.True(ConvolutionKernel.SobelX.IsSeparable);
        Assert.True(ConvolutionKernel.ScharrY.IsSeparable);
        Assert.True(ConvolutionKernel.PrewittX.IsSeparable);
        Assert.False(ConvolutionKernel.Laplacian3x3.IsSeparable);
        Assert.Same(ConvolutionKernel.SobelX, ConvolutionKernel.SobelX);

        static void Check(ConvolutionKernel kernel, int size, double[] expected)
        {
            Assert.Equal(size, kernel.Width);
            Assert.Equal(size, kernel.Height);
            Assert.Equal(expected, Enumerable.Range(0, size * size).Select(i => kernel[i % size, i / size]).ToArray());
        }
    }

    /// <summary>
    /// The outputs of OpenCV 5.0.0 (opencv-python-headless, Python 3.12) for one 9x7 gray image <c>f</c>, kept as literals:
    /// <c>cv2.magnitude(cv2.Sobel(f, cv2.CV_64F, 1, 0, borderType=cv2.BORDER_REPLICATE), cv2.Sobel(f, cv2.CV_64F, 0, 1, borderType=cv2.BORDER_REPLICATE))</c>,
    /// the same with <c>cv2.Scharr</c> and <c>cv2.BORDER_REFLECT</c>, <c>abs(cv2.Laplacian(f, cv2.CV_64F, ksize=1, borderType=cv2.BORDER_REPLICATE))</c>
    /// and <c>abs(cv2.Laplacian(f, cv2.CV_64F, ksize=5, borderType=cv2.BORDER_CONSTANT))</c>, rounded with
    /// <c>floor(x + 0.5)</c> and clipped to [0, 255], and <c>cv2.filter2D(f, cv2.CV_8U, sobelX, delta=128, borderType=cv2.BORDER_REPLICATE)</c>
    /// for a bias of one half (127.5, which rounds the integer sums up by 128). Every sample is equal.
    /// </summary>
    [Fact]
    public void EdgeDetectorsAndTheBiasMatchOpenCv()
    {
        int[][] source =
        [
            [0, 2, 5, 7, 10, 12, 14, 15, 15],
            [1, 3, 15, 8, 10, 12, 14, 0, 15],
            [1, 3, 6, 8, 11, 13, 15, 15, 15],
            [12, 11, 11, 0, 10, 9, 8, 8, 7],
            [13, 12, 11, 11, 10, 10, 9, 8, 15],
            [3, 3, 3, 3, 13, 13, 13, 13, 13],
            [0, 3, 3, 3, 13, 13, 13, 13, 5],
        ];

        Check(image => image.DetectEdges(EdgeDetector.Sobel, cancellationToken: Ct),
        [
            [9, 32, 30, 16, 19, 16, 15, 30, 21],
            [9, 38, 20, 4, 18, 16, 19, 3, 30],
            [41, 33, 9, 20, 25, 16, 11, 2, 21],
            [45, 35, 28, 10, 22, 13, 22, 20, 9],
            [35, 33, 25, 7, 21, 16, 20, 24, 26],
            [48, 39, 33, 36, 29, 13, 16, 4, 25],
            [13, 9, 0, 40, 40, 0, 0, 25, 34],
        ]);
        Check(image => image.DetectEdges(EdgeDetector.Scharr, edgeMode: ConvolutionEdgeMode.Mirror, cancellationToken: Ct),
        [
            [36, 115, 133, 64, 77, 64, 45, 151, 64],
            [36, 171, 82, 26, 72, 66, 106, 13, 150],
            [169, 135, 51, 97, 95, 66, 49, 38, 90],
            [183, 141, 130, 42, 102, 52, 91, 88, 24],
            [142, 133, 104, 23, 69, 66, 80, 101, 115],
            [196, 153, 131, 159, 128, 51, 64, 33, 115],
            [55, 40, 0, 160, 160, 0, 0, 107, 147],
        ]);
        Check(image => image.DetectEdges(EdgeDetector.Laplacian3x3, cancellationToken: Ct),
        [
            [3, 2, 9, 2, 1, 0, 1, 16, 0],
            [1, 9, 38, 8, 1, 1, 15, 59, 15],
            [13, 9, 13, 7, 3, 5, 10, 22, 8],
            [11, 6, 16, 40, 10, 5, 9, 6, 17],
            [12, 10, 7, 20, 4, 1, 3, 13, 17],
            [7, 9, 8, 18, 13, 3, 4, 5, 6],
            [6, 3, 0, 10, 10, 0, 0, 8, 16],
        ]);
        Check(image => image.DetectEdges(EdgeDetector.Laplacian5x5, edgeMode: ConvolutionEdgeMode.Zero, cancellationToken: Ct),
        [
            [84, 4, 136, 104, 110, 254, 240, 255, 255],
            [162, 12, 255, 208, 178, 255, 226, 214, 255],
            [116, 32, 2, 100, 2, 190, 116, 180, 250],
            [255, 255, 66, 182, 86, 4, 114, 46, 126],
            [255, 255, 162, 16, 54, 6, 160, 68, 216],
            [18, 36, 108, 28, 255, 255, 255, 255, 255],
            [98, 70, 154, 76, 252, 255, 255, 255, 114],
        ]);
        Check(image => image.Convolve(new ConvolutionOptions(ConvolutionKernel.SobelX) { Bias = 0.5 }, Ct),
        [
            [136, 157, 148, 138, 147, 144, 125, 132, 143],
            [136, 166, 148, 128, 146, 144, 109, 131, 158],
            [133, 151, 132, 132, 151, 138, 119, 128, 142],
            [127, 129, 110, 130, 150, 127, 126, 132, 133],
            [125, 123, 115, 135, 145, 124, 123, 139, 141],
            [130, 129, 127, 157, 157, 127, 126, 126, 127],
            [137, 137, 128, 168, 168, 128, 128, 104, 104],
        ]);

        void Check(Action<Image> operation, int[][] expected)
        {
            using var image = BuildGray8(source);
            operation(image);
            AssertGray8(image.Frames[0], expected);
        }
    }

    [Fact]
    public void EdgeDetectorsCombineTheResponsesOfTheirMatrices()
    {
        // A vertical step, edges clamped: every row is 0 0 10, so a pixel reads its row three times (y - 1, y and y + 1).
        // Pixel 1 reads the columns 0 0 10, pixel 2 the columns 0 10 10 (the last one clamped); every vertical gradient is 0.
        // Sobel: (10 + 20 + 10) - 0 = 40 at both; Prewitt: 30 at both.
        // Kirsch: pixel 1, east [-3 -3 5; -3 0 5; -3 -3 5] = 3 * 5 * 10 = 150 (north-east: 50 + 50 - 30 = 70); pixel 2, east
        // (-3 - 3) * 10 + 3 * 5 * 10 = 90, north-east (5 - 3) * 10 + (5 + 5 - 3) * 10 = 90, the largest.
        // Robinson: the largest of the Sobel matrix (40) and the diagonal ones ([0 1 2; -1 0 1; -2 -1 0]: 30 at pixel 1,
        // 30 at pixel 2), whatever their sign: 40.
        // Laplacian: pixel 1 = 10 (the right neighbor); pixel 2 = 0 (left) + 10 (right, clamped) + 10 (up) + 10 (down) - 4 * 10
        // = -10, whose absolute value is 10.
        Check(EdgeDetector.Sobel, [0, 40, 40]);
        Check(EdgeDetector.Prewitt, [0, 30, 30]);
        Check(EdgeDetector.Kirsch, [0, 150, 90]);
        Check(EdgeDetector.Robinson, [0, 40, 40]);
        Check(EdgeDetector.Laplacian3x3, [0, 10, 10]);

        static void Check(EdgeDetector detector, int[] expected)
        {
            using var image = BuildGray8([[0, 0, 10], [0, 0, 10], [0, 0, 10]]);
            image.DetectEdges(detector, cancellationToken: Ct);
            AssertGray8(image.Frames[0], [expected, expected, expected]);
        }
    }

    [Fact]
    public void EdgesKeepAlphaAndTheColorsOfEveryChannelUnlessGrayscale()
    {
        // One row, edges clamped: the Laplacian reads the pixel itself above and below, so it is left + right - 2 center,
        // on the stored colors (the hidden color of the transparent pixel takes part); alpha is kept.
        // Pixel 1: R = -200 -> 200, G = -100 -> 100; pixels 0 and 2: R = 100, G = 50.
        // Grayscale first: the luma of pixel 1 is (2126 * 100 + 7152 * 50 + 5000) / 10000 = 57, so 114 and 57.
        Rgba32[] source = [new(0, 0, 0, 255), new(100, 50, 0, 128), new(0, 0, 0, 0)];
        using (var image = Image.ImportPixelData<Rgba32>(source, 3, 1))
        {
            image.DetectEdges(EdgeDetector.Laplacian3x3, grayscale: false, cancellationToken: Ct);
            AssertRow(image.Frames[0], new Rgba32(100, 50, 0, 255), new Rgba32(200, 100, 0, 128), new Rgba32(100, 50, 0, 0));
        }

        using (var image = Image.ImportPixelData<Rgba32>(source, 3, 1))
        {
            image.DetectEdges(EdgeDetector.Laplacian3x3, cancellationToken: Ct);
            AssertRow(image.Frames[0], new Rgba32(57, 57, 57, 255), new Rgba32(114, 114, 114, 128), new Rgba32(57, 57, 57, 0));
        }

        // 16-bit samples keep their precision: 1000 next to 0 gives 1000, not a multiple of 257
        using (var image = Image.ImportPixelData<Gray16>([new Gray16(0), new Gray16(1000), new Gray16(0)], 3, 1))
        {
            image.DetectEdges(EdgeDetector.Laplacian3x3, cancellationToken: Ct);
            AssertRow(image.Frames[0], new Gray16(1000), new Gray16(2000), new Gray16(1000));
        }
    }

    [Fact]
    public void TheBiasIsAddedToTheStraightColorsAndNeverToAlpha()
    {
        // The identity on (100, 0, 250, 128) with a bias of a quarter (63.75): premultiplied, the colors get 63.75 * 128,
        // and once divided by the alpha 128 they are 163.75 -> 164, 63.75 -> 64 and 313.75 -> 255; alpha stays 128
        using (var image = new Image<Rgba32>(1, 1, new Rgba32(100, 0, 250, 128)))
        {
            image.Convolve(new ConvolutionOptions(Kernel(1, 1, [1])) { Bias = 0.25 }, Ct);
            Assert.Equal(new Rgba32(164, 64, 255, 128), image.Frames[0][0, 0]);
            image.Frames[0][0, 0] = new Rgba32(100, 0, 250, 128);
            image.Convolve(new ConvolutionOptions(Kernel(1, 1, [1])) { Bias = -0.25, PreserveAlpha = true }, Ct);
            Assert.Equal(new Rgba32(36, 0, 186, 128), image.Frames[0][0, 0]);
        }

        // A matrix whose weights add up to zero shows negative sums around mid-gray: 127.5 + (0 - 20) = 107.5 -> 108 and
        // 127.5 - 40 = 87.5 -> 88 (edges clamped); in 16-bit samples, 32767.5 + 20 -> 32788 and 32767.5 + 40 -> 32808
        using (var image = BuildGray8([[0, 20, 40]]))
        {
            image.Convolve(new ConvolutionOptions(Kernel(3, 1, [1, 0, -1])) { Bias = 0.5 }, Ct);
            AssertGray8(image.Frames[0], [[108, 88, 108]]);
        }

        using (var image = Image.ImportPixelData<Gray16>([new Gray16(0), new Gray16(20), new Gray16(40)], 3, 1))
        {
            image.Convolve(new ConvolutionOptions(Kernel(3, 1, [-1, 0, 1])) { Bias = 0.5 }, Ct);
            AssertRow(image.Frames[0], new Gray16(32788), new Gray16(32808), new Gray16(32788));
        }

        // Floating-point samples get the bias as a value (1 is the range)
        using (var image = new Image<RgbaFloat>(1, 1, new RgbaFloat(0.25f, -1, 2, 1)))
        {
            image.Convolve(new ConvolutionOptions(Kernel(1, 1, [1])) { Bias = 0.5 }, Ct);
            Assert.Equal(new RgbaFloat(0.75f, -0.5f, 2.5f, 1), image.Frames[0][0, 0]);
        }
    }

    [Theory]
    [MemberData(nameof(FloatingPointFormats))]
    public void EdgesOfFloatingPointSamplesAreNotClamped(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointEdges<RgbaFloat>();
        }
        else
        {
            CheckFloatingPointEdges<RgbaHalf>();
        }
    }

    [Fact]
    public void EdgeDetectionAppliesToEveryFrameOrToOneAndValidatesItsArguments()
    {
        // One row, edges clamped: the Laplacian is left + right - 2 center
        using var image = BuildGray8([[0, 0, 0, 90, 0, 0, 0]]);
        Fill(image.AppendFrame(), [[90, 0, 0, 0, 0, 0, 0]]);
        Fill(image.SetPosterFrame(image.Frames[0]), [[0, 0, 0, 0, 0, 0, 90]]);
        image.DetectEdges(EdgeDetector.Laplacian3x3, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 90, 180, 90, 0, 0]]);
        AssertGray8(image.Frames[1], [[90, 90, 0, 0, 0, 0, 0]]);
        AssertGray8(image.PosterFrame!, [[0, 0, 0, 0, 0, 90, 90]]);
        image.Frames[1].DetectEdges(EdgeDetector.Laplacian3x3, cancellationToken: Ct);
        AssertGray8(image.Frames[0], [[0, 0, 90, 180, 90, 0, 0]]);
        AssertGray8(image.Frames[1], [[0, 90, 90, 0, 0, 0, 0]]);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        // A flat image has no edge, except with the zero edge mode, which reads black beyond the image (one row: up and
        // down are black too, so 50 + 0 + 0 + 0 - 200 at the ends and 50 + 50 - 200 in the middle)
        using (var flat = BuildGray8([[50, 50, 50]]))
        {
            flat.DetectEdges(EdgeDetector.Laplacian3x3, cancellationToken: Ct);
            AssertGray8(flat.Frames[0], [[0, 0, 0]]);
        }

        using (var flat = BuildGray8([[50, 50, 50]]))
        {
            flat.DetectEdges(EdgeDetector.Laplacian3x3, edgeMode: ConvolutionEdgeMode.Zero, cancellationToken: Ct);
            AssertGray8(flat.Frames[0], [[150, 100, 150]]);
        }

        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).DetectEdges(EdgeDetector.Sobel));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).DetectEdges(EdgeDetector.Sobel));
        Assert.Throws<ArgumentOutOfRangeException>("detector", () => image.DetectEdges((EdgeDetector)8));
        Assert.Throws<ArgumentOutOfRangeException>("detector", () => image.Frames[0].DetectEdges((EdgeDetector)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>("edgeMode", () => image.DetectEdges(EdgeDetector.Sobel, edgeMode: (ConvolutionEdgeMode)4));
        Assert.Throws<ArgumentOutOfRangeException>("edgeMode", () => image.Frames[0].DetectEdges(EdgeDetector.Sobel, edgeMode: (ConvolutionEdgeMode)4));
        Assert.Throws<OperationCanceledException>(() => image.DetectEdges(EdgeDetector.Sobel, cancellationToken: new CancellationToken(canceled: true)));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].DetectEdges(EdgeDetector.Sobel, cancellationToken: new CancellationToken(canceled: true)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.DetectEdges(EdgeDetector.Sobel)));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Frames[0].DetectEdges(EdgeDetector.Sobel)));
        AssertGray8(image.Frames[0], [[0, 0, 90, 180, 90, 0, 0]]);

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.DetectEdges(EdgeDetector.Sobel));
        Assert.Throws<ObjectDisposedException>(() => frame.DetectEdges(EdgeDetector.Sobel));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Arguments
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void KernelsAreValidatedAndImmutable()
    {
        Assert.Throws<ArgumentOutOfRangeException>("width", () => new ConvolutionKernel(0, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => new ConvolutionKernel(-3, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => new ConvolutionKernel(2, 1, [1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => new ConvolutionKernel(1, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => new ConvolutionKernel(1, -1, []));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => new ConvolutionKernel(1, 4, [1, 1, 1, 1]));
        Assert.Throws<ArgumentException>("values", () => new ConvolutionKernel(3, 3, new double[8]));
        Assert.Throws<ArgumentException>("values", () => new ConvolutionKernel(3, 3, new double[10]));
        Assert.Throws<ArgumentException>("values", () => new ConvolutionKernel(1, 1, [double.NaN]));
        Assert.Throws<ArgumentException>("values", () => new ConvolutionKernel(3, 1, [0, double.PositiveInfinity, 0]));
        Assert.Throws<ArgumentException>("values", () => new ConvolutionKernel(1, 3, [0, 0, double.NegativeInfinity]));

        // Weights are stored row by row and copied
        double[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        var kernel = new ConvolutionKernel(5, 3, values);
        values[7] = -1;
        Assert.Equal(5, kernel.Width);
        Assert.Equal(3, kernel.Height);
        Assert.Equal(1, kernel[0, 0]);
        Assert.Equal(5, kernel[4, 0]);
        Assert.Equal(8, kernel[2, 1]);
        Assert.Equal(11, kernel[0, 2]);
        Assert.Equal(15, kernel[4, 2]);
        Assert.Throws<ArgumentOutOfRangeException>("x", () => _ = kernel[5, 0]);
        Assert.Throws<ArgumentOutOfRangeException>("x", () => _ = kernel[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>("y", () => _ = kernel[0, 3]);
        Assert.Throws<ArgumentOutOfRangeException>("y", () => _ = kernel[0, -1]);
    }

    [Fact]
    public void OptionsAreValidated()
    {
        var kernel = Kernel(1, 1, [1]);
        Assert.Throws<ArgumentNullException>("kernel", () => new ConvolutionOptions(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConvolutionOptions(kernel) { EdgeMode = (ConvolutionEdgeMode)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConvolutionOptions(kernel) { EdgeMode = (ConvolutionEdgeMode)(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConvolutionOptions(kernel) { WorkingSpace = (ConvolutionWorkingSpace)2 });
        Assert.Throws<ArgumentException>("value", () => new ConvolutionOptions(kernel) { Bias = double.NaN });
        Assert.Throws<ArgumentException>("value", () => new ConvolutionOptions(kernel) { Bias = double.PositiveInfinity });
        Assert.Throws<ArgumentException>("value", () => new ConvolutionOptions(kernel) { Bias = double.NegativeInfinity });
        Assert.Equal(-3.5, new ConvolutionOptions(kernel) { Bias = -3.5 }.Bias);

        using var image = BuildGray8([[1]]);
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Convolve(new ConvolutionOptions(kernel), Ct));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).Convolve(new ConvolutionOptions(kernel), Ct));
        Assert.Throws<ArgumentNullException>("options", () => image.Convolve(null!, Ct));
        Assert.Throws<ArgumentNullException>("options", () => image.Frames[0].Convolve(null!, Ct));
    }

    public static TheoryData<int, int, ConvolutionEdgeMode, int> IndexCases => new()
    {
        // Inside the image, every mode reads the index itself
        { 0, 5, ConvolutionEdgeMode.Zero, 0 },
        { 4, 5, ConvolutionEdgeMode.Mirror, 4 },

        // Clamp: the nearest edge
        { -1, 5, ConvolutionEdgeMode.Clamp, 0 },
        { -100, 5, ConvolutionEdgeMode.Clamp, 0 },
        { 5, 5, ConvolutionEdgeMode.Clamp, 4 },
        { int.MaxValue, 5, ConvolutionEdgeMode.Clamp, 4 },

        // Mirror, a b c: ... a b c | c b a | a b c | c b a | a b c ...
        { -1, 3, ConvolutionEdgeMode.Mirror, 0 },
        { -3, 3, ConvolutionEdgeMode.Mirror, 2 },
        { -4, 3, ConvolutionEdgeMode.Mirror, 2 },
        { -6, 3, ConvolutionEdgeMode.Mirror, 0 },
        { -7, 3, ConvolutionEdgeMode.Mirror, 0 },
        { 3, 3, ConvolutionEdgeMode.Mirror, 2 },
        { 5, 3, ConvolutionEdgeMode.Mirror, 0 },
        { 6, 3, ConvolutionEdgeMode.Mirror, 0 },
        { 8, 3, ConvolutionEdgeMode.Mirror, 2 },
        { -5, 1, ConvolutionEdgeMode.Mirror, 0 },

        // Wrap, a b c: ... a b c | a b c | a b c ...
        { -1, 3, ConvolutionEdgeMode.Wrap, 2 },
        { -3, 3, ConvolutionEdgeMode.Wrap, 0 },
        { -4, 3, ConvolutionEdgeMode.Wrap, 2 },
        { 3, 3, ConvolutionEdgeMode.Wrap, 0 },
        { 7, 3, ConvolutionEdgeMode.Wrap, 1 },

        // Zero: nothing
        { -1, 5, ConvolutionEdgeMode.Zero, -1 },
        { 5, 5, ConvolutionEdgeMode.Zero, -1 },
    };

    [Theory]
    [MemberData(nameof(IndexCases))]
    public void IndexesOutsideTheImageAreMappedByTheEdgeMode(int index, int length, ConvolutionEdgeMode mode, int expected)
        => Assert.Equal(expected, ConvolutionPlan.MapIndex(index, length, mode));

    // -----------------------------------------------------------------------------------------------------------------
    // Floating-point samples: the working values are the samples, colors are not clamped, alpha is
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<PixelFormat> FloatingPointFormats => [PixelFormat.RgbaFloat, PixelFormat.RgbaHalf];

    [Theory]
    [MemberData(nameof(FloatingPointFormats))]
    public void FloatingPointConvolutionKeepsHighDynamicRangeValuesAndNegativeSums(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointConvolution<RgbaFloat>();
        }
        else
        {
            CheckFloatingPointConvolution<RgbaHalf>();
        }
    }

    [Theory]
    [MemberData(nameof(FloatingPointFormats))]
    public void FloatingPointLinearLightConvolutionRejectsEncodedValuesOutsideTheUnitRange(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointLinearLightConvolution<RgbaFloat>();
        }
        else
        {
            CheckFloatingPointLinearLightConvolution<RgbaHalf>();
        }
    }

    private static void CheckFloatingPointConvolution<TPixel>()
        where TPixel : unmanaged
    {
        // [-1, 3, -1] on (0, 4, 0), edges clamped: -0 + 0 - 4 = -4, -0 + 12 - 0 = 12, -4 + 0 - 0 = -4 (and the opposite for
        // -4, and 1000 for the blue of the middle pixel); alpha is 1 everywhere, so the sums are the colors, unclamped
        using (var image = new Image<TPixel>(3, 1, ProcessingTests.FloatPixel<TPixel>(0, 0, 0)))
        {
            image.Frames[0][1, 0] = ProcessingTests.FloatPixel<TPixel>(4, -4, 1000);
            image.Convolve(Options(3, 1, [-1, 3, -1]), Ct);
            AssertFloatRow(image.Frames[0], new(-4, 4, -1000, 1), new(12, -12, 3000, 1), new(-4, 4, -1000, 1));
        }

        // A weight of 2 doubles the alpha 0.75 to 1.5, clamped to 1, and the colors are unpremultiplied by 1.5: they stay;
        // with PreserveAlpha the colors are doubled as stored and alpha is kept
        using (var image = new Image<TPixel>(1, 1, ProcessingTests.FloatPixel<TPixel>(1.5f, -0.5f, 2, 0.75f)))
        {
            image.Convolve(Options(1, 1, [2]), Ct);
            AssertFloatRow(image.Frames[0], new RgbaFloat(1.5f, -0.5f, 2, 1));
            image.Frames[0][0, 0] = ProcessingTests.FloatPixel<TPixel>(1.5f, -0.5f, 2, 0.75f);
            image.Convolve(new ConvolutionOptions(Kernel(1, 1, [2])) { PreserveAlpha = true }, Ct);
            AssertFloatRow(image.Frames[0], new RgbaFloat(3, -1, 4, 0.75f));
        }

        // A blur of a high dynamic range impulse keeps its light above 1: the weights add up to 1 and the edges are clamped
        // far from it, so the row still adds up to 100 (within the rounding of the samples)
        using (var image = new Image<TPixel>(9, 1, ProcessingTests.FloatPixel<TPixel>(0, 0, 0)))
        {
            image.Frames[0][4, 0] = ProcessingTests.FloatPixel<TPixel>(100, 0, 0);
            image.GaussianBlur(1, cancellationToken: Ct);
            var row = CopyRow(image.Frames[0], 0).Select(ProcessingTests.ToRgbaFloat).ToArray();
            Assert.True(row[4].R is > 1 and < 100, $"The center is {row[4].R}");
            Assert.Equal(100d, row.Sum(pixel => (double)pixel.R), tolerance: 0.1);
            Assert.All(row, pixel => Assert.Equal(1f, pixel.A));
        }

        // A sample that is not a number spreads to the pixels whose kernel reads it, and only to them
        using (var image = new Image<TPixel>(5, 1, ProcessingTests.FloatPixel<TPixel>(1, 1, 1)))
        {
            image.Frames[0][0, 0] = ProcessingTests.FloatPixel<TPixel>(float.NaN, 1, 1);
            image.Convolve(Options(3, 1, [0.25, 0.5, 0.25]), Ct);
            var row = CopyRow(image.Frames[0], 0).Select(ProcessingTests.ToRgbaFloat).ToArray();
            Assert.True(float.IsNaN(row[0].R));
            Assert.True(float.IsNaN(row[1].R));
            Assert.Equal(new RgbaFloat(1, 1, 1), row[2]);
            Assert.Equal(1f, row[1].G);
        }
    }

    private static void CheckFloatingPointLinearLightConvolution<TPixel>()
        where TPixel : unmanaged
    {
        var options = new ConvolutionOptions(Kernel(3, 1, [0.25, 0.5, 0.25])) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb };

        // Labeled as linear light: filtered as stored, high dynamic range values included
        using (var linear = new Image<TPixel>(3, 1, ProcessingTests.FloatPixel<TPixel>(0, 0, 0)))
        {
            linear.Frames[0][1, 0] = ProcessingTests.FloatPixel<TPixel>(8, -4, 2);
            linear.Convolve(options, Ct);
            AssertFloatRow(linear.Frames[0], new(2, -1, 0.5f, 1), new(4, -2, 1, 1), new(2, -1, 0.5f, 1));
            linear.GaussianBlur(1, workingSpace: ConvolutionWorkingSpace.LinearSrgb, cancellationToken: Ct);
        }

        // Labeled sRGB, with an encoded value outside [0, 1] in the second frame: nothing changes, in any frame, although
        // convolution works in place
        foreach (var value in new[] { 1.5f, -0.5f, float.NaN })
        {
            using var image = new Image<TPixel>(3, 1, ProcessingTests.FloatPixel<TPixel>(0.25f, 0.5f, 0.75f));
            image.Metadata.TransferFunction = ColorTransferFunction.Srgb;
            image.AppendFrame()[2, 0] = ProcessingTests.FloatPixel<TPixel>(value, 0.5f, 0.5f);
            foreach (var operation in new Action[] { () => image.Convolve(options, Ct), () => image.GaussianBlur(1, workingSpace: ConvolutionWorkingSpace.LinearSrgb, cancellationToken: Ct), () => image.Frames[1].Convolve(options, Ct) })
            {
                var exception = Assert.Throws<UnsupportedImageFeatureException>(operation);
                Assert.Equal("Linear-light convolution of floating-point samples outside [0, 1]", exception.Feature);
                AssertFloatRow(image.Frames[0], new(0.25f, 0.5f, 0.75f, 1), new(0.25f, 0.5f, 0.75f, 1), new(0.25f, 0.5f, 0.75f, 1));
            }

            // A frame is checked on its own, and the encoded working space reads any value
            image.Frames[0].Convolve(options, Ct);
            image.Convolve(Options(3, 1, [0.25, 0.5, 0.25]), Ct);
        }
    }

    private static void CheckSeparableSkipsZeroWeights<TPixel>()
        where TPixel : unmanaged
    {
        // The four corners of the square, a quarter each, given as factors or weight by weight: both take the two passes.
        // A sample that is not a number in the middle reaches the pixels whose corners read it (the corner pixels, edges
        // clamped), and not the middle one, whose zero weight reads it
        foreach (var kernel in new[] { ConvolutionKernel.CreateSeparable([0.5, 0, 0.5], [0.5, 0, 0.5]), Kernel(3, 3, [0.25, 0, 0.25, 0, 0, 0, 0.25, 0, 0.25]) })
        {
            Assert.True(kernel.IsSeparable);
            using var image = new Image<TPixel>(3, 3, ProcessingTests.FloatPixel<TPixel>(0.5f, 0.5f, 0.5f));
            image.Frames[0][1, 1] = ProcessingTests.FloatPixel<TPixel>(float.NaN, 0.5f, 0.5f);
            image.Convolve(new ConvolutionOptions(kernel), Ct);
            Assert.Equal(new RgbaFloat(0.5f, 0.5f, 0.5f, 1), ProcessingTests.ToRgbaFloat(image.Frames[0][1, 1]));
            Assert.Equal(new RgbaFloat(0.5f, 0.5f, 0.5f, 1), ProcessingTests.ToRgbaFloat(image.Frames[0][1, 0]));
            Assert.True(float.IsNaN(ProcessingTests.ToRgbaFloat(image.Frames[0][0, 0]).R));
            Assert.Equal(0.5f, ProcessingTests.ToRgbaFloat(image.Frames[0][0, 0]).G);
        }
    }

    private static void CheckBoxBlurNonFinite<TPixel>()
        where TPixel : unmanaged
    {
        // One row of ones, radius 1: every pixel is the mean of three columns. Red is not a number at 3, green infinite at
        // 0, blue 4 (above 1) at 6. A running sum would subtract the infinity and leave not-a-number behind it; the block
        // sums only add the samples of each square
        using var image = new Image<TPixel>(7, 1, ProcessingTests.FloatPixel<TPixel>(1, 1, 1));
        image.Frames[0][3, 0] = ProcessingTests.FloatPixel<TPixel>(float.NaN, 1, 1);
        image.Frames[0][0, 0] = ProcessingTests.FloatPixel<TPixel>(1, float.PositiveInfinity, 1);
        image.Frames[0][6, 0] = ProcessingTests.FloatPixel<TPixel>(1, 1, 4);
        image.BoxBlur(1, cancellationToken: Ct);
        var row = CopyRow(image.Frames[0], 0).Select(ProcessingTests.ToRgbaFloat).ToArray();
        Assert.Equal([false, false, true, true, true, false, false], row.Select(pixel => float.IsNaN(pixel.R)));
        Assert.Equal([float.PositiveInfinity, float.PositiveInfinity, 1, 1, 1, 1, 1], row.Select(pixel => pixel.G));
        Assert.Equal([1, 1, 1, 1, 1, 2, 3], row.Select(pixel => pixel.B));
        Assert.All(row, pixel => Assert.Equal(1f, pixel.A));
    }

    private static void CheckFloatingPointMedian<TPixel>()
        where TPixel : unmanaged
    {
        // One row: the middle window reads every pixel three times. Red NaN 1 2 sorts as 1 2 NaN; green -inf 5 -0 as
        // -inf -0 5 (-0 keeps its sign); blue +inf NaN NaN gives a positive NaN; alpha 0.5 0.25 1 gives 0.5
        using var image = new Image<TPixel>(3, 1);
        image.Frames[0][0, 0] = ProcessingTests.FloatPixel<TPixel>(float.NaN, float.NegativeInfinity, float.PositiveInfinity, 0.5f);
        image.Frames[0][1, 0] = ProcessingTests.FloatPixel<TPixel>(1, 5, float.NaN, 0.25f);
        image.Frames[0][2, 0] = ProcessingTests.FloatPixel<TPixel>(2, -0f, float.NaN, 1);
        image.MedianBlur(1, cancellationToken: Ct);
        var middle = ProcessingTests.ToRgbaFloat(image.Frames[0][1, 0]);
        Assert.Equal(2f, middle.R);
        Assert.Equal(0x8000_0000u, BitConverter.SingleToUInt32Bits(middle.G));
        Assert.True(float.IsNaN(middle.B));
        Assert.Equal(0u, BitConverter.SingleToUInt32Bits(middle.B) & 0x8000_0000u);
        Assert.Equal(0.5f, middle.A);
    }

    private static void CheckFloatingPointEdges<TPixel>()
        where TPixel : unmanaged
    {
        // One row, edges clamped: the vertical Sobel gradient is 0 and the horizontal one is 4 (right - left), so the
        // magnitude is 4 |right - left|: 16, 16 and 4000 at the ends, 0 in the middle; nothing is clamped
        using (var image = new Image<TPixel>(3, 1, ProcessingTests.FloatPixel<TPixel>(0, 0, 0)))
        {
            image.Frames[0][1, 0] = ProcessingTests.FloatPixel<TPixel>(4, -4, 1000);
            image.DetectEdges(EdgeDetector.Sobel, grayscale: false, cancellationToken: Ct);
            AssertFloatRow(image.Frames[0], new(16, 16, 4000, 1), new(0, 0, 0, 1), new(16, 16, 4000, 1));
        }

        // A sample that is not a number spreads to the pixels whose matrices read it with a weight: pixel 0 (the vertical
        // matrix reads it above and below) and pixel 1, not pixel 2
        using (var image = new Image<TPixel>(3, 1, ProcessingTests.FloatPixel<TPixel>(1, 1, 1)))
        {
            image.Frames[0][0, 0] = ProcessingTests.FloatPixel<TPixel>(float.NaN, 1, 1);
            image.DetectEdges(EdgeDetector.Sobel, grayscale: false, cancellationToken: Ct);
            var row = CopyRow(image.Frames[0], 0).Select(ProcessingTests.ToRgbaFloat).ToArray();
            Assert.True(float.IsNaN(row[0].R));
            Assert.True(float.IsNaN(row[1].R));
            Assert.Equal(new RgbaFloat(0, 0, 0), row[2]);
            Assert.Equal(0f, row[1].G);
        }
    }

    private static void AssertFloatRow<TPixel>(ImageFrame<TPixel> frame, params RgbaFloat[] expected)
        where TPixel : unmanaged
    {
        Assert.Equal(new Size(expected.Length, 1), frame.Size);
        Assert.Equal(expected, CopyRow(frame, 0).Select(ProcessingTests.ToRgbaFloat).ToArray());
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------------------------

    private static void CheckIdentity<TPixel>()
        where TPixel : unmanaged
    {
        // Every pixel is visible (non-zero alpha): c * a / a is exactly c, whatever the matrix size and the edge mode
        var source = new TPixel[6];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = ProcessingTests.Pixel<TPixel>(i + 1);
        }

        foreach (var mode in EdgeModes)
        {
            foreach (var kernel in new[] { Kernel(1, 1, [1]), Kernel(3, 3, [0, 0, 0, 0, 1, 0, 0, 0, 0]), Kernel(5, 1, [0, 0, 1, 0, 0]), Kernel(1, 7, [0, 0, 0, 1, 0, 0, 0]) })
            {
                foreach (var preserveAlpha in new[] { false, true })
                {
                    using var image = Image.ImportPixelData<TPixel>(source, 3, 2);
                    image.Convolve(new ConvolutionOptions(kernel) { EdgeMode = mode, PreserveAlpha = preserveAlpha }, Ct);
                    Assert.Equal(source, CopyAll(image.Frames[0]));
                }
            }
        }
    }

    private static ConvolutionKernel Kernel(int width, int height, double[] values) => new(width, height, values);

    private static ConvolutionOptions Options(int width, int height, double[] values, ConvolutionEdgeMode mode = ConvolutionEdgeMode.Clamp)
        => new(Kernel(width, height, values)) { EdgeMode = mode };

    private static int[][] Column(int[] values) => [.. values.Select(value => new[] { value })];

    private static Image<Gray8> BuildGray8(int[][] rows)
    {
        var image = new Image<Gray8>(rows[0].Length, rows.Length);
        Fill(image.Frames[0], rows);
        return image;
    }

    private static void Fill(ImageFrame<Gray8> frame, int[][] rows)
    {
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                frame[x, y] = new Gray8((byte)rows[y][x]);
            }
        }
    }

    private static void AssertGray8(ImageFrame<Gray8> frame, int[][] expected)
    {
        Assert.Equal(new Size(expected[0].Length, expected.Length), frame.Size);
        for (var y = 0; y < expected.Length; y++)
        {
            Assert.Equal(expected[y], CopyRow(frame, y).Select(pixel => (int)pixel.Value).ToArray());
        }
    }

    private static Image<Rgba64> BuildGradient(int width, int height, PixelStorageLayoutOptions? layout)
    {
        var image = new Image<Rgba64>(ImageConfiguration.Default, new Size(width, height), scope: null, layout);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new Rgba64((ushort)(x * 4919), (ushort)(y * 5843), (ushort)((x * y * 97) + 3), (ushort)((x + y) % 4 == 0 ? 0 : 20000 + (x * 1000) + y));
            }
        }

        return image;
    }

    /// <summary>A two-frame 4x3 Rgba32 image with a poster and an EXIF thumbnail, charged to a scope that uses <paramref name="pool"/>.</summary>
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
        image.Metadata.ExifProfile = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: false)
            .Ifd0(TiffBuilder.Short(ExifTiff.ImageWidthTag, 4), TiffBuilder.Short(ExifTiff.ImageLengthTag, 3))
            .Thumbnail("THUMB"u8.ToArray())
            .Build()));
        return image;
    }

    private static Rgba32[] CopyAllFrames(Image<Rgba32> image)
        => [.. CopyAll(image.Frames[0]), .. CopyAll(image.Frames[1]), .. CopyAll(image.PosterFrame!)];

    private static void AssertRow<TPixel>(ImageFrame<TPixel> frame, params TPixel[] expected)
        where TPixel : unmanaged
        => Assert.Equal(expected, CopyRow(frame, 0));

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
}
