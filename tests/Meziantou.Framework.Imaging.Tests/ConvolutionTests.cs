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
