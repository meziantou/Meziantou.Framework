using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// SSIM, PSNR, SSIMULACRA 2, Butteraugli, dHash and pHash. The expected scores and hashes come from scikit-image, from the
/// libjxl tools, from an independent Python implementation of the area-weighted thumbnail, from brute-force computations of
/// the SSIM and PSNR definitions written in the test, or from hand-built images whose hashes follow from the definition.
/// </summary>
public sealed class ImageSimilarityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The expected scores come from scikit-image 0.24:
    //   color = structural_similarity(premultiplied_rgb_expected, premultiplied_rgb_actual, data_range=255, channel_axis=-1)
    //   alpha = structural_similarity(alpha_expected, alpha_actual, data_range=255)
    //   score = min(color, alpha)
    // where the RGB channels are premultiplied as (value × alpha + 127) // 255, on the images built by CreateReferenceImages.
    [Theory]
    [InlineData("TexturedWithRedBlock", 0.9605711225224126)]
    [InlineData("TranslucentAlphaShift", 0.5393716414539776)]
    [InlineData("WhiteWithBlackSquare", 0.9891674563063622)]
    [InlineData("ScreenshotWithErrorBanner", 0.9932801453262927)]
    [InlineData("OpaqueVersusTransparentRed", 9.99900009999e-05)]
    public void ComputeSsim_MatchesScikitImage(string name, double expectedScore)
    {
        var (width, height, expected, actual) = CreateReferenceImages(name);

        Assert.Equal(expectedScore, ComputeSsim(expected, actual, width, height), tolerance: 1e-9);
    }

    [Fact]
    public void ComputeSsim_LocalizedDifferenceLowersTheScoreInProportionToItsArea()
    {
        // A single SSIM over the whole image scores this pair 0.087: the black square dominates the global variance even
        // though it covers less than 1% of the image.
        var (width, height, expected, actual) = CreateReferenceImages("WhiteWithBlackSquare");

        Assert.InRange(ComputeSsim(expected, actual, width, height), 0.98, 0.99);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(7, 7)]
    [InlineData(8, 9)]
    [InlineData(13, 4)]
    [InlineData(2, 40)]
    [InlineData(31, 17)]
    public void ComputeSsim_MatchesABruteForceComputation(int width, int height)
    {
        // Covers images smaller than the window in either direction, where the window shrinks to the image
        var (expected, actual) = CreateRandomPixels(width * height);

        Assert.Equal(ComputeReferenceSsim(expected, actual, width, height), ComputeSsim(expected, actual, width, height), tolerance: 1e-12);
    }

    [Fact]
    public void ComputeSsim_IdenticalPixelsScoreExactlyOne()
    {
        var (pixels, _) = CreateRandomPixels(37 * 23);

        Assert.Equal(1d, ComputeSsim(pixels, pixels, 37, 23));
    }

    [Fact]
    public void ComputeSsim_TransparentPixelsScoreTheSameWhateverColorTheyHide()
    {
        var (expected, actual) = CreateRandomPixels(20 * 10);
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = expected[i] with { A = 0 };
            actual[i] = actual[i] with { A = 0 };
        }

        Assert.Equal(1d, ComputeSsim(expected, actual, 20, 10));
    }

    [Fact]
    public void ComputeSsim_AcceptsTheSameFrameTwice()
    {
        var (pixels, _) = CreateRandomPixels(9 * 8);
        using var image = Image.ImportPixelData<Rgba32>(pixels, 9, 8);

        Assert.Equal(1d, ImageSimilarity.ComputeSsim(image.Frames[0], image.Frames[0], Ct));
        Assert.Equal(0, ImageSimilarity.ComputeDifferenceHashDistance(image.Frames[0], image.Frames[0], Ct));
        Assert.Equal(0, ImageSimilarity.ComputePerceptualHashDistance(image.Frames[0], image.Frames[0], Ct));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void SsimAccumulator_RequiresAllTheRows()
    {
        using var image = new Image<Rgba32>(4, 2);
        using var accumulator = new SsimAccumulator(image.Owner.Scope, 4, 2);
        accumulator.AddRow(new Rgba32[4], new Rgba32[4]);

        Assert.Throws<InvalidOperationException>(() => accumulator.ComputeMeanSsim());
        Assert.Throws<ArgumentException>(() => accumulator.AddRow(new Rgba32[3], new Rgba32[4]));
    }

    [Theory]
    [InlineData("TexturedWithRedBlock")]
    [InlineData("TranslucentAlphaShift")]
    [InlineData("ScreenshotWithErrorBanner")]
    [InlineData("OpaqueVersusTransparentRed")]
    [InlineData("PatternWithNoise")]
    public void ComputePsnr_MatchesABruteForceComputation(string name)
    {
        var (width, height, expected, actual) = CreateReferenceImages(name);
        using var expectedImage = Image.ImportPixelData<Rgba32>(expected, width, height);
        using var actualImage = Image.ImportPixelData<Rgba32>(actual, width, height);

        Assert.Equal(ComputeReferencePsnr(expected, actual), ImageSimilarity.ComputePsnr(expectedImage.Frames[0], actualImage.Frames[0], Ct), tolerance: 1e-12);
    }

    [Fact]
    public void ComputePsnr_AOneLevelErrorInEverySampleIs20Log10Of255()
    {
        using var expected = CreateSolidImage(9, 7, Gray(100));
        using var actual = CreateSolidImage(9, 7, Gray(101));

        Assert.Equal(20 * Math.Log10(255), ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], Ct), tolerance: 1e-12);
    }

    [Fact]
    public void PerceptualMetrics_IdenticalPixelsHaveTheBestScore()
    {
        using var image = CreatePatternImage(37, 23, inverted: false);
        using var copy = image.Clone();

        Assert.Equal(double.PositiveInfinity, ImageSimilarity.ComputePsnr(image.Frames[0], copy.Frames[0], Ct));
        Assert.Equal(100d, ImageSimilarity.ComputeSsimulacra2(image.Frames[0], copy.Frames[0], Ct));
        Assert.Equal(new ButteraugliDistance(0, 0), ImageSimilarity.ComputeButteraugliDistance(image.Frames[0], copy.Frames[0], Ct));
    }

    // The expected scores come from the tools of libjxl 0.12.0, built with -DJPEGXL_ENABLE_DEVTOOLS=ON, on the images built
    // by CreateReferenceImages saved as PNG files without color chunks (read as sRGB):
    //   ssimulacra2 expected.png actual.png
    // The port emulates the arithmetic of the tool (its sRGB curve, cube root and blur, with fused multiply-adds), so the
    // scores agree with the 8 decimals it prints.
    [Theory]
    [InlineData("TexturedWithRedBlock", 22.85836756)]
    [InlineData("TranslucentAlphaShift", -40.56160581)]
    [InlineData("WhiteWithBlackSquare", -40.90694011)]
    [InlineData("ScreenshotWithErrorBanner", -22.96581715)]
    [InlineData("OpaqueVersusTransparentRed", 75.02388435)]
    [InlineData("PatternWithNoise", 85.42310758)]
    [InlineData("MinimumSizePatternWithNoise", 95.22227886)]
    [InlineData("NarrowPatternWithNoise", 95.42975291)]
    public void ComputeSsimulacra2_MatchesLibjxl(string name, double expectedScore)
    {
        var (width, height, expected, actual) = CreateReferenceImages(name);
        using var expectedImage = Image.ImportPixelData<Rgba32>(expected, width, height);
        using var actualImage = Image.ImportPixelData<Rgba32>(actual, width, height);

        Assert.Equal(expectedScore, ImageSimilarity.ComputeSsimulacra2(expectedImage.Frames[0], actualImage.Frames[0], Ct), tolerance: 1e-6);
    }

    // The expected distances come from butteraugli_main of libjxl 0.12.0 (same build and files as above), which prints the
    // largest distance and the 3-norm. For SmallPatternWithNoise, which the tool scores 0, the values come from the tool run
    // on the frame extended to 8x8 as ButteraugliDiffmap extends it, with --rawdistmap, cropped back to 5x6. The port
    // computes in the order of the scalar code of libjxl: the tool, with SIMD instructions and a fast logarithm, differs by
    // about 1e-5 of the value.
    [Theory]
    [InlineData("TexturedWithRedBlock", 24.7280178070, 10.609880)]
    [InlineData("TranslucentAlphaShift", 46.2900810242, 25.076416)]
    [InlineData("ScreenshotWithErrorBanner", 78.8184814453, 29.324292)]
    [InlineData("OpaqueVersusTransparentRed", 339.9974975586, 339.995591)]
    [InlineData("PatternWithNoise", 0.5026070476, 0.323986)]
    [InlineData("SmallPatternWithNoise", 0.5853016973, 0.4634496)]
    [InlineData("MinimumSizePatternWithNoise", 0.4033407569, 0.308746)]
    [InlineData("NarrowPatternWithNoise", 0.5056172013, 0.340175)]
    public void ComputeButteraugliDistance_MatchesLibjxl(string name, double expectedMaxDistance, double expectedPNorm3)
    {
        var (width, height, expected, actual) = CreateReferenceImages(name);
        using var expectedImage = Image.ImportPixelData<Rgba32>(expected, width, height);
        using var actualImage = Image.ImportPixelData<Rgba32>(actual, width, height);

        var distance = ImageSimilarity.ComputeButteraugliDistance(expectedImage.Frames[0], actualImage.Frames[0], Ct);

        Assert.Equal(expectedMaxDistance, distance.MaxDistance, tolerance: expectedMaxDistance * 1e-4);
        Assert.Equal(expectedPNorm3, distance.PNorm3, tolerance: expectedPNorm3 * 1e-4);
    }

    [Theory]
    [InlineData(300, 257, false)]
    [InlineData(300, 257, true)]
    [InlineData(257, 300, true)]
    public void Metrics_DoNotDependOnTheWorkerCount(int width, int height, bool opaque)
    {
        // Neither height is a multiple of the 64-row bands of the sums; 16 workers get a band of at least 32 rows each
        Assert.Equal(height / 32, RowBands.GetWorkerCount(new Size(width, height), 16));
        var (expectedPixels, actualPixels) = CreateRandomPixels(width * height);
        if (opaque)
        {
            for (var i = 0; i < expectedPixels.Length; i++)
            {
                expectedPixels[i].A = 255;
                actualPixels[i].A = 255;
            }
        }

        var reference = Compute(workers: 1);
        Assert.InRange(reference.Ssim, 0.1, 0.99);
        foreach (var workers in new[] { 2, 3, 16 })
        {
            Assert.Equal(reference, Compute(workers));
        }

        (double Ssim, double Psnr, double Ssimulacra2, ButteraugliDistance Butteraugli) Compute(int workers)
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var expectedImage = Image.ImportPixelData<Rgba32>(expectedPixels, width, height, configuration: configuration);
            using var actualImage = Image.ImportPixelData<Rgba32>(actualPixels, width, height, configuration: configuration);
            var expected = expectedImage.Frames[0];
            var actual = actualImage.Frames[0];
            return (
                ImageSimilarity.ComputeSsim(expected, actual, Ct),
                ImageSimilarity.ComputePsnr(expected, actual, Ct),
                ImageSimilarity.ComputeSsimulacra2(expected, actual, Ct),
                ImageSimilarity.ComputeButteraugliDistance(expected, actual, Ct));
        }
    }

    [Fact]
    public void Metrics_ReportTheFirstInvalidSampleWhateverTheWorkerCount()
    {
        // Rows 20 and 230 are read by different workers: the error is the one of row 20, as with one worker
        const int Width = 300;
        const int Height = 257;
        foreach (var workers in new[] { 1, 2, 3, 16 })
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var reference = new Image<RgbaFloat>(Width, Height, new RgbaFloat(0.5f, 0.5f, 0.5f), configuration);
            using var image = new Image<RgbaFloat>(Width, Height, new RgbaFloat(0.5f, 0.5f, 0.5f), configuration);
            image.Frames[0][7, 230] = new RgbaFloat(2f, 0.5f, 0.5f);
            image.Frames[0][9, 20] = new RgbaFloat(float.NaN, 0.5f, 0.5f);
            var expected = reference.Frames[0];
            var actual = image.Frames[0];
            Action[] members =
            [
                () => ImageSimilarity.ComputeSsim(expected, actual, Ct),
                () => ImageSimilarity.ComputePsnr(expected, actual, Ct),
                () => ImageSimilarity.ComputeSsimulacra2(expected, actual, Ct),
                () => ImageSimilarity.ComputeButteraugliDistance(expected, actual, Ct),
            ];

            foreach (var member in members)
            {
                var exception = Assert.Throws<UnsupportedImageFeatureException>(member);
                Assert.Contains("RgbaFloat pixel (9, 20)", exception.Message, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ComputeButteraugliDistance_PNorm3IsAtMostTheMaxDistance()
    {
        var (width, height, expected, actual) = CreateReferenceImages("TexturedWithRedBlock");
        using var expectedImage = Image.ImportPixelData<Rgba32>(expected, width, height);
        using var actualImage = Image.ImportPixelData<Rgba32>(actual, width, height);

        var distance = ImageSimilarity.ComputeButteraugliDistance(expectedImage.Frames[0], actualImage.Frames[0], Ct);

        Assert.InRange(distance.PNorm3, 0, distance.MaxDistance);
        Assert.Equal(distance, new ButteraugliDistance(distance.MaxDistance, distance.PNorm3));
        Assert.NotEqual(distance, default);
    }

    [Fact]
    public void ComputeSsimulacra2_RejectsFramesSmallerThan8x8()
    {
        using var narrow = CreatePatternImage(7, 20, inverted: false);
        using var short_ = CreatePatternImage(20, 7, inverted: false);

        Assert.Throws<ArgumentException>("expected", () => ImageSimilarity.ComputeSsimulacra2(narrow.Frames[0], narrow.Frames[0], Ct));
        Assert.Throws<ArgumentException>("expected", () => ImageSimilarity.ComputeSsimulacra2(short_.Frames[0], short_.Frames[0], Ct));
    }

    [Fact]
    public void PerceptualMetrics_TransparentPixelsScoreTheSameWhateverColorTheyHide()
    {
        var (expected, actual) = CreateRandomPixels(20 * 10);
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = expected[i] with { A = 0 };
            actual[i] = actual[i] with { A = 0 };
        }

        using var expectedImage = Image.ImportPixelData<Rgba32>(expected, 20, 10);
        using var actualImage = Image.ImportPixelData<Rgba32>(actual, 20, 10);

        Assert.Equal(double.PositiveInfinity, ImageSimilarity.ComputePsnr(expectedImage.Frames[0], actualImage.Frames[0], Ct));
        Assert.Equal(100d, ImageSimilarity.ComputeSsimulacra2(expectedImage.Frames[0], actualImage.Frames[0], Ct));
        Assert.Equal(new ButteraugliDistance(0, 0), ImageSimilarity.ComputeButteraugliDistance(expectedImage.Frames[0], actualImage.Frames[0], Ct));
    }

    [Fact]
    public void PerceptualMetrics_LargerDifferencesScoreWorse()
    {
        var (width, height, expected, _) = CreateReferenceImages("PatternWithNoise");
        using var reference = Image.ImportPixelData<Rgba32>(expected, width, height);
        using var slightlyDifferent = Image.ImportPixelData<Rgba32>(Array.ConvertAll(expected, p => new Rgba32((byte)(p.R ^ 1), p.G, p.B)), width, height);
        using var veryDifferent = Image.ImportPixelData<Rgba32>(Array.ConvertAll(expected, p => new Rgba32((byte)(255 - p.R), p.G, p.B)), width, height);
        var frame = reference.Frames[0];

        Assert.True(ImageSimilarity.ComputePsnr(frame, slightlyDifferent.Frames[0], Ct) > ImageSimilarity.ComputePsnr(frame, veryDifferent.Frames[0], Ct));
        Assert.True(ImageSimilarity.ComputeSsimulacra2(frame, slightlyDifferent.Frames[0], Ct) > 90);
        Assert.True(ImageSimilarity.ComputeSsimulacra2(frame, veryDifferent.Frames[0], Ct) < 50);
        Assert.True(ImageSimilarity.ComputeButteraugliDistance(frame, slightlyDifferent.Frames[0], Ct).MaxDistance < 1);
        Assert.True(ImageSimilarity.ComputeButteraugliDistance(frame, veryDifferent.Frames[0], Ct).MaxDistance > 5);
    }

    [Fact]
    public void ComputeDifferenceHash_ComputesKnownHashes()
    {
        // Each row brightens (or darkens) by 16 levels from one cell to the next: no bit, or every bit
        var ascending = Enumerable.Range(0, 9 * 8).Select(index => Gray(index % 9 * 16)).ToArray();
        var descending = Enumerable.Range(0, 9 * 8).Select(index => Gray((8 - index % 9) * 16)).ToArray();
        using var ascendingImage = Image.ImportPixelData<Rgba32>(ascending, 9, 8);
        using var descendingImage = Image.ImportPixelData<Rgba32>(descending, 9, 8);

        Assert.Equal(0UL, ImageSimilarity.ComputeDifferenceHash(ascendingImage.Frames[0], Ct));
        Assert.Equal(ulong.MaxValue, ImageSimilarity.ComputeDifferenceHash(descendingImage.Frames[0], Ct));
    }

    [Fact]
    public void ComputePerceptualHash_ComputesAKnownHash()
    {
        using var image = CreatePatternImage(32, 32, inverted: false);

        Assert.Equal(11580269642849678823UL, ImageSimilarity.ComputePerceptualHash(image.Frames[0], Ct));
    }

    // The expected hashes come from an independent Python implementation that computes the area-weighted thumbnail with
    // arbitrary-precision integers. 13×5 is smaller than the pHash thumbnail, so each pixel spans several cells.
    [Theory]
    [InlineData(1000, 700, 1125899973951492UL, 48225311559189755UL)]
    [InlineData(13, 5, 872590157973323776UL, 14782499366000441473UL)]
    public void ComputeHashes_MatchAReferenceImplementation(int width, int height, ulong expectedDHash, ulong expectedPHash)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = new Rgba32((byte)((x * 17 + y * 31 + x * y * 3) % 256), (byte)((x * 7 + y * 13) % 256), (byte)((x * x + y * 5) % 256));
            }
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        Assert.Equal(expectedDHash, ImageSimilarity.ComputeDifferenceHash(image.Frames[0], Ct));
        Assert.Equal(expectedPHash, ImageSimilarity.ComputePerceptualHash(image.Frames[0], Ct));
    }

    [Fact]
    public void ComputeHashes_FlatImagesHaveNoNoise()
    {
        // Only the DC coefficient is above the median; every AC coefficient is zero up to rounding
        using var small = CreateSolidImage(37, 23, new Rgba32(0x81, 0x80, 0x80));
        using var wide = CreateSolidImage(1000, 7, new Rgba32(0x81, 0x80, 0x80));

        Assert.Equal(1UL, ImageSimilarity.ComputePerceptualHash(small.Frames[0], Ct));
        Assert.Equal(0UL, ImageSimilarity.ComputeDifferenceHash(wide.Frames[0], Ct));
    }

    [Fact]
    public void ComputeDifferenceHash_UsesEverySourcePixel()
    {
        // With area averaging, darkening a single pixel darkens its thumbnail cell. Bilinear sampling only read the pixels
        // around the center of each cell, and never this one.
        using var expected = CreateSolidImage(90, 80, Colors.White);
        using var actual = CreateSolidImage(90, 80, Colors.White);
        actual.Frames[0][12, 42] = Colors.Black;

        var expectedHash = ImageSimilarity.ComputeDifferenceHash(expected.Frames[0], Ct);
        var actualHash = ImageSimilarity.ComputeDifferenceHash(actual.Frames[0], Ct);
        Assert.Equal(1, BitOperations.PopCount(expectedHash ^ actualHash));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ComputeHashDistance_IgnoresAOneLevelChangeInASinglePixel(bool useDHash, bool gradient)
    {
        // Such a change is invisible (SSIM 1.000000), but it used to flip about half the pHash bits of flat images
        using var expected = gradient ? CreateGradientImage(400, 300) : CreateSolidImage(400, 300, new Rgba32(0x80, 0x80, 0x80));
        using var actual = expected.Clone();
        var pixel = actual.Frames[0][200, 150];
        actual.Frames[0][200, 150] = pixel with { R = (byte)(pixel.R + 1) };

        Assert.Equal(0, ComputeHashDistance(useDHash, expected, actual));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ComputeHashDistance_IgnoresScatteredOneLevelNoise(bool useDHash, bool gradient)
    {
        // 109 pixels changed by ±1 used to give a dHash distance of 25
        using var expected = gradient ? CreateGradientImage(400, 300) : CreateSolidImage(400, 300, new Rgba32(0x80, 0x80, 0x80));
        using var actual = expected.Clone();
        for (var i = 0; i < 109; i++)
        {
            // Scattered over the whole image, a few pixels per thumbnail cell
            var index = (int)((i * 1_103_515_245L + 12_345) % (400 * 300));
            var (x, y) = (index % 400, index / 400);
            var pixel = actual.Frames[0][x, y];
            var delta = i % 2 is 0 ? 1 : -1;

            // The images are gray, and clamping keeps black and white pixels from wrapping around
            actual.Frames[0][x, y] = Gray(Math.Clamp(pixel.R + delta, 0, 255));
        }

        Assert.InRange(ComputeHashDistance(useDHash, expected, actual), 0, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputeHashDistance_DifferentStructuresAreFarApart(bool useDHash)
    {
        // Same mean luminance, so the distance comes from the hashes only
        using var blackLeftHalf = CreateSolidImage(400, 300, Colors.White);
        FillRectangle(blackLeftHalf, x: 0, y: 0, width: 200, height: 300, Colors.Black);
        using var blackRightHalf = CreateSolidImage(400, 300, Colors.White);
        FillRectangle(blackRightHalf, x: 200, y: 0, width: 200, height: 300, Colors.Black);
        using var pattern = CreatePatternImage(400, 300, inverted: false);
        using var invertedPattern = CreatePatternImage(400, 300, inverted: true);
        using var gradient = CreateGradientImage(400, 300);

        Assert.True(ComputeHashDistance(useDHash, blackLeftHalf, blackRightHalf) >= 4);
        Assert.True(ComputeHashDistance(useDHash, pattern, invertedPattern) >= 16);
        Assert.True(ComputeHashDistance(useDHash, pattern, gradient) >= 16);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputeHashDistance_IncludesTheDifferenceOfMeanLuminance(bool useDHash)
    {
        // Flat images have no dHash bit, and only the DC bit of the pHash when they are not black. Black and white are the
        // farthest apart: 64 units of about 4 levels.
        using var black = CreateSolidImage(16, 16, Colors.Black);
        using var white = CreateSolidImage(16, 16, Colors.White);
        using var gray = CreateSolidImage(16, 16, Gray(128));
        using var lightGray = CreateSolidImage(16, 16, Gray(192));

        Assert.Equal(ImageSimilarity.MaxHashDistance, ComputeHashDistance(useDHash, black, white));

        // 128 / 255 × 64 = 32.1, rounded down, plus the DC bit of the pHash
        Assert.Equal(useDHash ? 32 : 33, ComputeHashDistance(useDHash, black, gray));

        // 64 / 255 × 64 = 16.1, rounded down; both pHashes have the DC bit
        Assert.Equal(16, ComputeHashDistance(useDHash, gray, lightGray));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputeHashDistance_ComparesTransparentImagesOverBlackAndWhite(bool useDHash)
    {
        // Over black, transparent white and opaque black are the same; over white, they are as far apart as possible
        using var transparentWhite = CreateSolidImage(16, 16, new Rgba32(255, 255, 255, 0));
        using var opaqueBlack = CreateSolidImage(16, 16, Colors.Black);

        Assert.Equal(ImageSimilarity.MaxHashDistance, ComputeHashDistance(useDHash, transparentWhite, opaqueBlack));
        Assert.Equal(0UL, ImageSimilarity.ComputeDifferenceHash(transparentWhite.Frames[0], Ct) ^ ImageSimilarity.ComputeDifferenceHash(opaqueBlack.Frames[0], Ct));
    }

    [Fact]
    public void AllMembers_ReadEveryPixelFormatAs8BitRgba()
    {
        // The same pixels in every format: the conversion of CloneAs<Rgba32> gives the same 8-bit values
        // An alpha mask has no color to compare, and Bgr565 keeps 5-bit and 6-bit colors only
        using var reference = CreatePatternImage(23, 19, inverted: false);
        foreach (var format in PixelFormats.All.Where(format => PixelFormats.HasColor(format) && format != PixelFormat.Bgr565))
        {
            using var converted = ConvertTo(reference, format);
            Assert.Equal(1d, ImageSimilarity.ComputeSsim(reference.Frames[0], converted.Frames[0], Ct));
            Assert.Equal(double.PositiveInfinity, ImageSimilarity.ComputePsnr(reference.Frames[0], converted.Frames[0], Ct));
            Assert.Equal(100d, ImageSimilarity.ComputeSsimulacra2(converted.Frames[0], reference.Frames[0], Ct));
            Assert.Equal(new ButteraugliDistance(0, 0), ImageSimilarity.ComputeButteraugliDistance(reference.Frames[0], converted.Frames[0], Ct));
            Assert.Equal(0, ImageSimilarity.ComputeDifferenceHashDistance(reference.Frames[0], converted.Frames[0], Ct));
            Assert.Equal(0, ImageSimilarity.ComputePerceptualHashDistance(converted.Frames[0], reference.Frames[0], Ct));
            Assert.Equal(ImageSimilarity.ComputeDifferenceHash(reference.Frames[0], Ct), ImageSimilarity.ComputeDifferenceHash(converted.Frames[0], Ct));
            Assert.Equal(ImageSimilarity.ComputePerceptualHash(reference.Frames[0], Ct), ImageSimilarity.ComputePerceptualHash(converted.Frames[0], Ct));
        }
    }

    [Fact]
    public void AllMembers_RejectFloatingPointSamplesWithoutEightBitValue()
    {
        // A sample outside [0, 1], alpha included, or that is not a number has no 8-bit value: it is never clipped
        using var reference = new Image<RgbaFloat>(8, 8, new RgbaFloat(0.5f, 0.5f, 0.5f));
        foreach (var (color, alpha) in new[] { (1.5f, 1f), (-0.25f, 1f), (float.NaN, 1f), (0.5f, 2f) })
        {
            using var image = new Image<RgbaHalf>(8, 8, new RgbaHalf((Half)0.5f, (Half)0.5f, (Half)0.5f));
            image.Frames[0][3, 5] = new RgbaHalf((Half)0.5f, (Half)color, (Half)0.5f, (Half)alpha);
            var expected = reference.Frames[0];
            var actual = image.Frames[0];
            Action[] members =
            [
                () => ImageSimilarity.ComputeSsim(expected, actual, Ct),
                () => ImageSimilarity.ComputePsnr(actual, expected, Ct),
                () => ImageSimilarity.ComputeSsimulacra2(expected, actual, Ct),
                () => ImageSimilarity.ComputeButteraugliDistance(expected, actual, Ct),
                () => ImageSimilarity.ComputeDifferenceHash(actual, Ct),
                () => ImageSimilarity.ComputePerceptualHash(actual, Ct),
                () => ImageSimilarity.ComputeDifferenceHashDistance(expected, actual, Ct),
                () => ImageSimilarity.ComputePerceptualHashDistance(actual, expected, Ct),
            ];

            foreach (var member in members)
            {
                var exception = Assert.Throws<UnsupportedImageFeatureException>(member);
                Assert.Equal("Comparison of floating-point samples outside [0, 1]", exception.Feature);
                Assert.Contains("RgbaHalf pixel (3, 5)", exception.Message, StringComparison.Ordinal);
            }
        }

        // In [0, 1], they are rounded to 8 bits like the samples of the other formats: 0.5 is 127.5, which rounds up
        using var rgba32 = new Image<Rgba32>(8, 8, new Rgba32(128, 128, 128));
        Assert.Equal(double.PositiveInfinity, ImageSimilarity.ComputePsnr(rgba32.Frames[0], reference.Frames[0], Ct));
    }

    [Fact]
    public void AllMembers_Round16BitSamplesTo8Bits()
    {
        // 0x12 is 0x1212 on 16 bits, and the midpoint with 0x13 is 0x1292.8: 0x1292 rounds to 0x12, and 0x1293 to 0x13
        // although its high byte is still 0x12
        using var reference = CreateSolidImage(8, 8, Gray(0x12));
        using var roundsDown = new Image<Rgba64>(8, 8, new Rgba64(0x1292, 0x1292, 0x1292, 0xFFFF));
        using var roundsUp = new Image<Rgba64>(8, 8, new Rgba64(0x1293, 0x1293, 0x1293, 0xFFFF));

        Assert.Equal(1d, ImageSimilarity.ComputeSsim(reference.Frames[0], roundsDown.Frames[0], Ct));
        Assert.True(ImageSimilarity.ComputeSsim(reference.Frames[0], roundsUp.Frames[0], Ct) < 1);
        Assert.Equal(double.PositiveInfinity, ImageSimilarity.ComputePsnr(reference.Frames[0], roundsDown.Frames[0], Ct));
        Assert.Equal(20 * Math.Log10(255), ImageSimilarity.ComputePsnr(reference.Frames[0], roundsUp.Frames[0], Ct), tolerance: 1e-12);
        Assert.Equal(100d, ImageSimilarity.ComputeSsimulacra2(reference.Frames[0], roundsDown.Frames[0], Ct));
        Assert.True(ImageSimilarity.ComputeSsimulacra2(reference.Frames[0], roundsUp.Frames[0], Ct) < 100);
        Assert.Equal(0d, ImageSimilarity.ComputeButteraugliDistance(reference.Frames[0], roundsDown.Frames[0], Ct).MaxDistance);
        Assert.True(ImageSimilarity.ComputeButteraugliDistance(reference.Frames[0], roundsUp.Frames[0], Ct).MaxDistance > 0);
    }

    [Fact]
    public void AllMembers_ValidateTheirArguments()
    {
        using var image = new Image<Rgba32>(4, 3);
        using var other = new Image<Rgba32>(3, 4);
        var frame = image.Frames[0];

        Assert.Throws<ArgumentNullException>("expected", () => ImageSimilarity.ComputeSsim(null!, frame, Ct));
        Assert.Throws<ArgumentNullException>("actual", () => ImageSimilarity.ComputeSsim(frame, null!, Ct));
        Assert.Throws<ArgumentNullException>("frame", () => ImageSimilarity.ComputeDifferenceHash(null!, Ct));
        Assert.Throws<ArgumentNullException>("frame", () => ImageSimilarity.ComputePerceptualHash(null!, Ct));
        Assert.Throws<ArgumentNullException>("expected", () => ImageSimilarity.ComputeDifferenceHashDistance(null!, frame, Ct));
        Assert.Throws<ArgumentNullException>("actual", () => ImageSimilarity.ComputePerceptualHashDistance(frame, null!, Ct));

        Assert.Throws<ArgumentNullException>("expected", () => ImageSimilarity.ComputePsnr(null!, frame, Ct));
        Assert.Throws<ArgumentNullException>("actual", () => ImageSimilarity.ComputePsnr(frame, null!, Ct));
        Assert.Throws<ArgumentNullException>("expected", () => ImageSimilarity.ComputeSsimulacra2(null!, frame, Ct));
        Assert.Throws<ArgumentNullException>("actual", () => ImageSimilarity.ComputeSsimulacra2(frame, null!, Ct));
        Assert.Throws<ArgumentNullException>("expected", () => ImageSimilarity.ComputeButteraugliDistance(null!, frame, Ct));
        Assert.Throws<ArgumentNullException>("actual", () => ImageSimilarity.ComputeButteraugliDistance(frame, null!, Ct));

        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputeSsim(frame, other.Frames[0], Ct));
        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputePsnr(frame, other.Frames[0], Ct));
        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputeButteraugliDistance(frame, other.Frames[0], Ct));
        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputeDifferenceHashDistance(frame, other.Frames[0], Ct));
        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputePerceptualHashDistance(frame, other.Frames[0], Ct));
    }

    [Fact]
    public void AllMembers_ObserveCancellation()
    {
        using var image = CreatePatternImage(16, 16, inverted: false);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var frame = image.Frames[0];

        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputeSsim(frame, frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputePsnr(frame, frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputeSsimulacra2(frame, frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputeButteraugliDistance(frame, frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputeDifferenceHash(frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputePerceptualHash(frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputeDifferenceHashDistance(frame, frame, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputePerceptualHashDistance(frame, frame, canceled.Token));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
    }

    [Fact]
    public void AllMembers_RejectFramesThatAreLeasedOrDetached()
    {
        using var image = CreatePatternImage(8, 8, inverted: false);
        using var other = CreatePatternImage(8, 8, inverted: true);
        var frame = image.Frames[0];
        var otherFrame = other.Frames[0];

        frame.ProcessPixelRows(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => ImageSimilarity.ComputeSsim(otherFrame, frame, Ct));
            Assert.Throws<InvalidOperationException>(() => ImageSimilarity.ComputeDifferenceHash(frame, Ct));
            Assert.Throws<InvalidOperationException>(() => ImageSimilarity.ComputePerceptualHashDistance(frame, otherFrame, Ct));
            Assert.Throws<InvalidOperationException>(() => ImageSimilarity.ComputePsnr(frame, otherFrame, Ct));
            Assert.Throws<InvalidOperationException>(() => ImageSimilarity.ComputeSsimulacra2(otherFrame, frame, Ct));
            Assert.Throws<InvalidOperationException>(() => ImageSimilarity.ComputeButteraugliDistance(frame, otherFrame, Ct));
        });

        // The lease of the other frame was released when the first one failed
        Assert.Equal(0, other.Owner.ActiveLeaseCount);

        var disposed = CreatePatternImage(8, 8, inverted: false);
        var disposedFrame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputeSsim(frame, disposedFrame, Ct));
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputePerceptualHash(disposedFrame, Ct));
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputeDifferenceHashDistance(disposedFrame, frame, Ct));
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputePsnr(disposedFrame, frame, Ct));
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputeButteraugliDistance(frame, disposedFrame, Ct));
    }

    [Fact]
    public void ComputeSsim_ChargesItsBuffersToTheScopeAndReleasesThem()
    {
        // The pixels take 64 × 64 × 4 bytes; the row ring and the column statistics of SSIM need more than 2 KB on top
        const int Size = 64;
        long peak;
        using (var probe = CreatePatternImage(Size, Size, inverted: false))
        {
            var before = probe.Owner.Scope.LiveBytes;
            ImageSimilarity.ComputeSsim(probe.Frames[0], probe.Frames[0], Ct);
            peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.True(peak > before + 2048, "The SSIM buffers are charged to the scope of the image");
            Assert.Equal(before, probe.Owner.Scope.LiveBytes);
        }

        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } };
        using (var image = new Image<Rgba32>(Size, Size, Colors.White, tight))
        {
            var exception = Assert.Throws<ImageResourceLimitException>(() => ImageSimilarity.ComputeSsim(image.Frames[0], image.Frames[0], Ct));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
            Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak } };
        using (var image = new Image<Rgba32>(Size, Size, Colors.White, exact))
        {
            Assert.Equal(1d, ImageSimilarity.ComputeSsim(image.Frames[0], image.Frames[0], Ct));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PerceptualMetrics_ChargeTheirPlanesToTheScopeAndReleaseThem(bool butteraugli)
    {
        // The pixels take 64 × 64 × 4 bytes; the float planes of the metrics need several times more
        const int Size = 64;
        long peak;
        using (var probe = CreatePatternImage(Size, Size, inverted: false))
        {
            var before = probe.Owner.Scope.LiveBytes;
            Compute(probe);
            peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.True(peak > before + (Size * Size * 4 * 6), "The planes are charged to the scope of the image");
            Assert.Equal(before, probe.Owner.Scope.LiveBytes);
        }

        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } };
        using (var image = CreatePatternImage(Size, Size, inverted: false, tight))
        {
            var exception = Assert.Throws<ImageResourceLimitException>(() => Compute(image));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
            Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak } };
        using (var image = CreatePatternImage(Size, Size, inverted: false, exact))
        {
            Compute(image);
        }

        void Compute(Image image)
        {
            if (butteraugli)
            {
                ImageSimilarity.ComputeButteraugliDistance(image.Frames[0], image.Frames[0], Ct);
            }
            else
            {
                ImageSimilarity.ComputeSsimulacra2(image.Frames[0], image.Frames[0], Ct);
            }
        }
    }

    [Fact]
    public void ConversionBuffers_AreChargedAndReleased()
    {
        using var image = new Image<Gray8>(64, 64, new Gray8(10));
        var before = image.Owner.Scope.LiveBytes;

        ImageSimilarity.ComputePerceptualHash(image.Frames[0], Ct);
        ImageSimilarity.ComputeDifferenceHashDistance(image.Frames[0], image.Frames[0], Ct);

        Assert.True(image.Owner.Scope.GetDiagnostics().PeakLiveBytes >= before + (64 * 4), "The Rgba32 row is charged to the scope of the image");
        Assert.Equal(before, image.Owner.Scope.LiveBytes);
    }

    internal static (int Width, int Height, Rgba32[] Expected, Rgba32[] Actual) CreateReferenceImages(string name)
    {
        switch (name)
        {
            case "TexturedWithRedBlock":
            {
                var expected = CreatePattern(64, 48, static (_, _) => 255);
                var actual = (Rgba32[])expected.Clone();
                for (var y = 0; y < 48; y++)
                {
                    for (var x = 0; x < 64; x++)
                    {
                        ref var pixel = ref actual[y * 64 + x];
                        if (y is >= 15 and < 21 && x is >= 20 and < 30)
                        {
                            pixel = new Rgba32(255, 0, 0);
                        }

                        pixel.B = (byte)Math.Min(255, pixel.B + x * y % 5);
                    }
                }

                return (64, 48, expected, actual);
            }

            case "TranslucentAlphaShift":
                return (40, 30, CreatePattern(40, 30, static (x, y) => (x * 11 + y * 3) % 256), CreatePattern(40, 30, static (x, y) => (x * 11 + y * 3 + 40) % 256));

            case "WhiteWithBlackSquare":
            {
                var expected = CreateFilled(1024, 1024, Colors.White);
                var actual = (Rgba32[])expected.Clone();
                FillRectangle(actual, 1024, x: 462, y: 462, width: 100, height: 100, Colors.Black);
                return (1024, 1024, expected, actual);
            }

            case "ScreenshotWithErrorBanner":
            {
                var expected = CreateFilled(1280, 720, Colors.White);
                for (var y = 0; y < 720; y++)
                {
                    for (var x = 41; x < 1240; x++)
                    {
                        if (y % 24 < 12 && x % 9 < 5)
                        {
                            expected[y * 1280 + x] = new Rgba32(0x3C, 0x3C, 0x3C);
                        }
                    }
                }

                var actual = (Rgba32[])expected.Clone();
                FillRectangle(actual, 1280, x: 560, y: 340, width: 160, height: 40, new Rgba32(0xDC, 0x1E, 0x1E));
                return (1280, 720, expected, actual);
            }

            case "OpaqueVersusTransparentRed":
                return (16, 16, CreateFilled(16, 16, new Rgba32(255, 0, 0)), CreateFilled(16, 16, new Rgba32(255, 0, 0, 0)));

            // Opaque, with a deterministic noise of ±5 levels: the pairs of the perceptual metrics, of several sizes
            case "PatternWithNoise":
                return CreateNoisyPair(61, 45);

            case "SmallPatternWithNoise":
                return CreateNoisyPair(5, 6);

            case "MinimumSizePatternWithNoise":
                return CreateNoisyPair(8, 8);

            case "NarrowPatternWithNoise":
                return CreateNoisyPair(9, 40);

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, message: null);
        }

        static Rgba32[] CreatePattern(int width, int height, Func<int, int, int> alpha)
        {
            var pixels = new Rgba32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var red = (x * 17 + y * 31 + x * y * 3) % 256;
                    var green = (x * 7 + y * 13) % 256;
                    var blue = (x * x + y * 5) % 256;
                    pixels[y * width + x] = new Rgba32((byte)red, (byte)green, (byte)blue, (byte)alpha(x, y));
                }
            }

            return pixels;
        }

        static (int Width, int Height, Rgba32[] Expected, Rgba32[] Actual) CreateNoisyPair(int width, int height)
        {
            var expected = CreatePattern(width, height, static (_, _) => 255);
            var actual = new Rgba32[expected.Length];
            for (var i = 0; i < expected.Length; i++)
            {
                var pixel = expected[i];
                actual[i] = new Rgba32(AddNoise(pixel.R, i), AddNoise(pixel.G, i * 3 + 1), AddNoise(pixel.B, i * 7 + 2));
            }

            return (width, height, expected, actual);

            static byte AddNoise(byte value, int seed) => (byte)Math.Clamp(value + (seed * 2654435761u >> 28) % 11 - 5, 0, 255);
        }

        static Rgba32[] CreateFilled(int width, int height, Rgba32 color)
        {
            var pixels = new Rgba32[width * height];
            pixels.AsSpan().Fill(color);
            return pixels;
        }

        static void FillRectangle(Rgba32[] pixels, int imageWidth, int x, int y, int width, int height, Rgba32 color)
        {
            for (var row = y; row < y + height; row++)
            {
                pixels.AsSpan(row * imageWidth + x, width).Fill(color);
            }
        }
    }

    // The expected scores come from scikit-image 0.26.0 on the frames of CreateWidePair: with 16-bit samples, the colors
    // premultiplied as (value × alpha + 32767) // 65535 and structural_similarity(..., data_range=65535, channel_axis=-1) and
    // peak_signal_noise_ratio(..., data_range=65535) on uint16 arrays, the lower of the color and alpha scores; with
    // fractions, the colors scaled to 3.5 as float32 and data_range=4 on float64 arrays.
    [Theory]
    [InlineData(SimilarityPrecision.SixteenBit, false, 0.9933877322348765, 40.8766701753226)]
    [InlineData(SimilarityPrecision.SixteenBit, true, 0.8921295597019451, 28.813715882256005)]
    [InlineData(SimilarityPrecision.FloatingPoint, false, 0.9935735810766341, 42.036509012362664)]
    public void WideMetrics_MatchScikitImage(SimilarityPrecision precision, bool translucent, double expectedSsim, double expectedPsnr)
    {
        var (expected, actual) = CreateWidePair(translucent);
        using (expected)
        using (actual)
        {
            Image expectedImage = expected;
            Image actualImage = actual;
            using var expectedFloats = precision == SimilarityPrecision.FloatingPoint ? ScaleToHighDynamicRange(expected) : null;
            using var actualFloats = precision == SimilarityPrecision.FloatingPoint ? ScaleToHighDynamicRange(actual) : null;
            var options = new ImageSimilarityOptions { Precision = precision, DataRange = precision == SimilarityPrecision.FloatingPoint ? 4 : 1 };
            var e = (expectedFloats ?? expectedImage).Frames[0];
            var a = (actualFloats ?? actualImage).Frames[0];
            Assert.Equal(expectedSsim, ImageSimilarity.ComputeSsim(e, a, options, Ct), 1e-12);
            Assert.Equal(expectedPsnr, ImageSimilarity.ComputePsnr(e, a, options, Ct), 1e-10);
        }
    }

    [Fact]
    public void WideMetrics_EightBitPrecisionIsTheDefault()
    {
        using var expected = CreatePatternImage(31, 23, inverted: false);
        using var actual = CreateGradientImage(31, 23);
        Assert.Equal(ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], Ct), ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], new ImageSimilarityOptions(), Ct));
        Assert.Equal(ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], Ct), ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], new ImageSimilarityOptions(), Ct));
        Assert.Equal(SimilarityPrecision.EightBit, new ImageSimilarityOptions().Precision);
        Assert.Equal(1, new ImageSimilarityOptions().DataRange);
    }

    [Theory]
    [InlineData(SimilarityPrecision.SixteenBit)]
    [InlineData(SimilarityPrecision.FloatingPoint)]
    public void WideMetrics_OfOpaque8BitFramesAreThe8BitMetrics(SimilarityPrecision precision)
    {
        // SSIM and PSNR do not depend on the scale of the samples when the range scales with them
        using var expected = CreatePatternImage(31, 23, inverted: false);
        using var actual = CreateGradientImage(31, 23);
        var options = new ImageSimilarityOptions { Precision = precision };
        Assert.Equal(ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], Ct), ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], options, Ct), 1e-12);
        Assert.Equal(ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], Ct), ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], options, Ct), 1e-10);
    }

    [Fact]
    public void WideMetrics_SeeDifferencesBelowOneEightBitLevel()
    {
        // The frames differ by 100 units of 65,535, which round to the same 8-bit samples
        using var expected = new Image<Rgba64>(16, 16, new Rgba64(30000, 30000, 30000, 65535));
        using var actual = new Image<Rgba64>(16, 16, new Rgba64(30100, 30000, 30000, 65535));
        Assert.Equal(double.PositiveInfinity, ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], Ct));
        var psnr = ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit }, Ct);
        Assert.Equal(10 * Math.Log10(65535d * 65535 / (100d * 100 / 3)), psnr, 1e-10);
    }

    [Fact]
    public void WideMetrics_ReadHighDynamicRangeSamplesAsFractionsOnly()
    {
        using var expected = new Image<RgbaFloat>(8, 8, new RgbaFloat(3f, 0.5f, 0.25f));
        using var actual = new Image<RgbaFloat>(8, 8, new RgbaFloat(3f, 0.5f, 0.25f));
        actual.Frames[0][2, 3] = new RgbaFloat(2.5f, 0.5f, 0.25f);
        var fractions = new ImageSimilarityOptions { Precision = SimilarityPrecision.FloatingPoint, DataRange = 4 };

        // One sample of 192 differs by 0.5: MSE = 0.25 / 192, PSNR = 10 log10(16 / MSE)
        Assert.Equal(10 * Math.Log10(16 / (0.25 / 192)), ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], fractions, Ct), 1e-10);
        Assert.InRange(ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], fractions, Ct), 0.5, 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit }, Ct));
        Assert.Throws<UnsupportedImageFeatureException>(() => ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit }, Ct));

        actual.Frames[0][4, 4] = new RgbaFloat(float.PositiveInfinity, 0, 0);
        Assert.Throws<UnsupportedImageFeatureException>(() => ImageSimilarity.ComputePsnr(expected.Frames[0], actual.Frames[0], fractions, Ct));
        actual.Frames[0][4, 4] = new RgbaFloat(float.NaN, 0, 0);
        Assert.Throws<UnsupportedImageFeatureException>(() => ImageSimilarity.ComputeSsim(expected.Frames[0], actual.Frames[0], fractions, Ct));
        Assert.Throws<UnsupportedImageFeatureException>(() => ImageSimilarity.CreateDifferenceMap(expected.Frames[0], actual.Frames[0], cancellationToken: Ct));
    }

    [Theory]
    [InlineData(SimilarityPrecision.SixteenBit)]
    [InlineData(SimilarityPrecision.FloatingPoint)]
    public void WideMetrics_DoNotDependOnTheWorkerCount(SimilarityPrecision precision)
    {
        // 300x260 = 78,000 pixels: enough for several workers, with a partial last band
        var scores = new List<(double Ssim, double Psnr)>();
        foreach (var workers in new[] { 1, 2, 3, 7 })
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var expected = CreatePatternImage(300, 260, inverted: false, configuration);
            using var actual = CreateGradientImage(300, 260);
            using var wideExpected = expected.CloneAs<Rgba64>();
            using var wideActual = actual.CloneAs<Rgba64>();
            wideActual.Frames[0][17, 200] = new Rgba64(1, 2, 3, 40000);
            var options = new ImageSimilarityOptions { Precision = precision };
            scores.Add((ImageSimilarity.ComputeSsim(wideExpected.Frames[0], wideActual.Frames[0], options, Ct), ImageSimilarity.ComputePsnr(wideExpected.Frames[0], wideActual.Frames[0], options, Ct)));
        }

        Assert.All(scores, score => Assert.Equal(scores[0], score));
    }

    [Fact]
    public void WideMetrics_AndDifferenceMap_ValidateTheirArguments()
    {
        using var image = new Image<Rgba64>(8, 8);
        using var other = new Image<Rgba64>(9, 8);
        Assert.Throws<ArgumentNullException>("options", () => ImageSimilarity.ComputeSsim(image.Frames[0], image.Frames[0], null!, Ct));
        Assert.Throws<ArgumentNullException>("options", () => ImageSimilarity.ComputePsnr(image.Frames[0], image.Frames[0], null!, Ct));
        Assert.Throws<ArgumentNullException>("expected", () => ImageSimilarity.ComputeSsim(null!, image.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit }, Ct));
        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputePsnr(image.Frames[0], other.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.FloatingPoint }, Ct));
        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.CreateDifferenceMap(image.Frames[0], other.Frames[0], cancellationToken: Ct));
        Assert.Throws<ArgumentNullException>("actual", () => ImageSimilarity.CreateDifferenceMap(image.Frames[0], null!, cancellationToken: Ct));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ImageSimilarityOptions { Precision = (SimilarityPrecision)3 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ImageSimilarityOptions { DataRange = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ImageSimilarityOptions { DataRange = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DifferenceMapOptions { Kind = (DifferenceMapKind)3 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DifferenceMapOptions { Threshold = -0.1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DifferenceMapOptions { Gain = 0 });
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.ComputeSsim(image.Frames[0], image.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit }, source.Token));
        Assert.Throws<OperationCanceledException>(() => ImageSimilarity.CreateDifferenceMap(image.Frames[0], image.Frames[0], cancellationToken: source.Token));
    }

    // The map of scikit-image 0.26.0's structural_similarity(expected, actual, data_range=255, channel_axis=-1, full=True)
    // on the 8-bit samples (the 16-bit samples >> 8) of the opaque frames of CreateWidePair: round(clip((1 - S.mean(-1)) × 4, 0, 1) × 255)
    private const string SkimageSsimMap = "Eg0HBgcHBgcHBwsKCQgJDAgJBgcFBgcLCgoLCgsKCAcICAcGBAgLDBALBwYHCAcICQgLCgkICQkICQYHBgYHDAsKCgkICAgHCAgHBAQICgsNBwYGBgcHBwgHCgoJCAcIBwgGBgUGBwsKCgoJBwgIBgcHBAMECAoLDwgGBgYHBwcIBwoKCQgGBwcIBgcGBgcKCgoKBwcIBwYHBwMDBAcJCQwIBgYGBgYHCAcJCgkHBggICQcHBwcICwoKCgcHCAgHCAUDBAYGBwgKCAYGBgYGBwkJCgoKBwYICAgHCAgICgoKCQcGBwcHBwcEBAQGBwgJCgkHBwcGBwgLCQkKCAcGCAcHBgcICQsLCgcHBgcHBgcEBAQGBgYHCAoJBwYGBgcICwoKCAgGBggHBwYICQwMDAsHBgYGBgYEBAQFBQYGBwgPDAkHBwYGCQkICQcHBgYHBgYGCAoLCwoIBwYGBgYGBAMEBQUGBgYICwoIBwcGBwkICAcHBwYGBwYGBggJCQkJBgYGBgYGBAQEBgUGBgcHCQsJCAcHBwgJCQkGBwcGBQYGBgcKCgsKBwYGBgUGBgQEBAYFBgYHBwwMCggHBwkJCQkHBgYHBgYGBQYICwoKCAYGBgUFBQQEBAYFBQUFBgcKCggHBgcICQkHBwYGBgYFBQUHCQoJCgcGBgYFBQQEBAYGBgUFBgcJCgoIBwcJCQkJBgYGBgYGBgYGCQkLCQcHBgYGBgUEBAQHBwcHBgcJCQoLCQgICgoKCAcGBgYGBgYGBwkKCwkGBgUFBgYEBAQGBwcHBwcHCQkKDAoJCQkKCQcGBgYGBwcGBwkKCgwIBgYFBQUGBAMEBgcIBwcGCQoLDA0KCQgICQcGBgYGBgYGBgcKCwsLCAcGBgUFBAMEBgYHCAgHBgkKCwwNCwkJCAcHBwYGBgYGBwgKCQsKCggGBgYFBAQEBwYGBwcHBwcICQsNDAoJCAgHBwcGBwYGBgcKCgoLCQkHBgcHBgQEBAYFBgcHCAkHCAkLCw0LCQgHBwcHBgYHBwcICQoKCggIBwYHBwQEBAYGBgYHBwgJBwgICgoNCwkJBwcHBwcHBwcHCQkKCwkICAcGBwcFBAUGBgYGBwcKCQgICQsLDgwJBwYHBwYGBwcHBwgJCQgHCAcGBgcFBQUGBgcGBwcHCQgIBgYHCA0LBwYGBwcGBwcHCAkICQoICAgIBwcFBAQHBgYGBgcICQkICAYGCAkNDAcHBgcHBgYHBwkJCQkICAcHBwYGBQUFCAcGBgYHCgkJCAYGBggKDwkHBwYHBwcGBwcICQkKBgcHCAcGBAUFBgcHBwcICQsKCgkHBwcJChELCQgHBwcHBwcJCQkJBwYHBwcHBgQEBAYHBwcHBwkKCQoGBgcHCQoLCggHBgcGBwYHCAgIBwcGBwcHBwUEBAcHCAcHCAgKCgkHBgYGBwkKCwoJBwYGBgYGCAgICQgHBgYHBwYFBAYHBwkHCAkMCwoJBgYFBwcJCgwKCQgHBgYGBwgJCgoJBwYGBwgGBQUHBwcJCAkKCwoKCQYGBgcICgsNCwoJBwYGBwcICQsLCQcFBgcIBgUHBwgICQgLDA0MCwkHBgUGBggI";

    // ImageMagick 7.1.1-43 `compare -metric AE expected.png actual.png map.png` (RGBA bytes), on the 8-bit expected frame and a
    // copy with 15 pixels changed in red (rows 3 to 5, columns 4 to 8) and one in green (20, 30)
    private const string ImageMagickHighlightMap = "zNzu/87g8f/T4/L/0+X1/9bk8P/U4vL/1+f1/9nn0P/X6NP/2urT/9vs0P/Z6dT/3/DU/+Hr2P/h8df/3/DW/+Hz3f/i8tj/4fPe/+jy3f/n0t//6s7i/+nU3v/r0uT/6dDl/+rT5v/v0+j/7Nrk/+/Z5f/01+r/8t3o//Hd6v/12+7/9d3w/9Lf7f/R4+7/1ODu/9Pl8P/V5vP/2OXz/87f8v/T3u//0t/w/9Li8P/V5PX/0ujz/9Xn9//Y5c7/2+rT/9zq0v/a69H/3ezU/97u1f/d7dX/4fHb/+Hz2f/i8Nz/5vLY/+Ly2//k9+D/59Df/+jS4//o0+L/7NHk/+zV5f/t1OL/7tfn/+7W6P/v2Ob/8t3p//Ld7P/x2ev/8t3s/8/c8P/S4O3/1ODw/9Pg8v/V5fT/0uf2/9fo8//S3vD/zuPu/8/g8P/R4vX/0+bz/9bo+P/Y5tL/2eXR/9ro1f/d7db/3erR/9/s1P/h7tb/4e3U/+Hy2//k8tf/5vPc/+Pz3P/n89v/5Mzd/+jO3v/p0d7/7dPg/+rR4f/v0+f/7Nnn//LZ5v/z1+j/89zm//Ta5//x2+3/89/r//Tb7P/T3vH/0N/w/9Li8f/U5PH/1+fx/9bn8//Y6M3/0d/w/8/g8f/R4vH/1uf0/8oaQ//LHBj/yhkd/84bHP/RIB7/2O3R/97s1f/f6tX/3+/X/+Hw1v/h7tv/5fTZ/+X13//k9Nz/5fPb/+bS3//n097/6dDg/+zR4f/t1eP/6tTm//DU5//u2+j/8tzn//LY6P/x2+v/8t/u//Lh7f/Q3O7/0d/x/9Ph8//Q4fL/0+bw/9Xi+P/T6PT/2uTR/8/h9P/S4fP/0uH1/9Pk9f/JG0P/zh4c/88bHf/OGyH/0B4e/97s1f/f7dP/3O7U/+Ly2v/i89j/4u/Z/+T03P/j9t7/5Pje/+jN4P/l0uD/6NHg/+vS4v/s1uP/7Nfi/+7Z5//u1ej/8Nnl//Pa6P/x2ez/8tzu//Pe7v/M4ez/0+Du/9Le8f/Q4e//1OHw/9Xk8v/Z6PX/2ebQ/9flzv/Q5PX/0+Pz/9Pn9f/V5/X/zRgY/8wbHP/PHRz/ziAg/84iIv/d6tf/4PDX/93x2v/i7tn/3/Pc/+Xy2v/h8tv/5fXc/+bQ3P/m0uD/69Hj/+rU5f/r1uP/7tfl/+/X5v/s1Of/8NXo//PX6P/02O3/897s//Lc7v/43PD/zt/x/9Pf7f/R3+//1eL0/9fn9P/X5ff/2OPO/9Xk0//Z69T/0uH0/9Xj8f/V4/b/1OX2/9vo0v/Z6tH/1+zS/93p0f/c6tP/3/DU/9zx2P/g79b/5PDb/+Dw2P/l8tv/4vXg/+j43//lzuD/5dDh/+bS3v/p1eH/69Lj/+7X5P/t1+T/79jm//HY6P/x3On/9djs//Hf6v/03uv/0uHt/9De7f/P3vH/0+Xy/9Xm9P/S4vT/2ejz/9rk0P/b5tP/2+vR/9Pk8//U5/H/0+Tz/9fkzv/Z58//2unT/97n1f/Z7dT/2urW/+Dv2f/i7dr/3u/Y/+Ty3P/j89v/4/Ha/+Xz4P/k0Nz/5dDf/+fQ4P/p1eH/69Xi/+7S5//r0+X/7Njm/+7X5v/u2+f/89nr//Xa7f/03e3/9eHs/9Ld7//T3u7/0OXv/9Hj8//S4/H/1+bz/9blzv/X6c7/2ubP/9zt1f/W5vP/2eT0/9Xm0v/W5s3/1uvS/9zp1f/d6tH/3+zT/9/v0//h7Nn/3fLa/9/x3P/h8t3/5PTc/+X02//l8t//6dLg/+jS4//mz+H/69Ti/+7X4v/w0+P/7tnk/+za5//z1+b/8dzr//Lc7P/04O7/9d3w/9Di7v/Q3/P/z9/x/9Xk9f/R4vb/0+Py/9Tn+P/a6s//1unT/9fo0P/a6dD/0+b0/9nn8//U5tH/1+bP/9jp1f/d59H/3OnV/9vt2P/h8Nj/4fLa/97w1//f8tj/5fDd/+T33P/o9dz/5s3e/+nQ3//r1OD/6tbi/+zS4v/u1+P/7tTn//Da5//z2OX/8Njo//TZ7P/13er/993q/8/f7f/P3u3/z+Pw/9Dj8f/V4/D/0+b2/9Po8v/Z5s7/1+jS/9vo0v/b59X/3enS/9Xk9//Y5dL/2ebN/9rqz//b7NP/2enR/9vu1P/g69f/3+7a/+Dx2v/k8Nz/4PDa/+Lx3//n89//5dDh/+jS4P/oz+H/6dLj/+3T4f/s1OX/8Njl//DX6P/u1+X/89ro/+/Z6P/13er/8tzv//ff6//Q4ez/zuLy/8/g8//V5fL/1OH1/9To8v/Z59H/2+XQ/9zm0P/b69H/2u3V/9zt1f/U5fj/1+XS/9fp0v/X59L/3ezS/97r1//c79j/4e3V/+Lv2f/i9Nr/4/TY/+H12v/o9t3/4/Pg/+TQ4f/qz+L/6dXh/+vR4v/q0eL/69fm/+zV4//s1eX/89ro//Pb7P/12uv/9t7s//Xe7f/O4ez/zt3t/9Dj8v/S5PH/0uX2/9bj8v/X5Pb/2erS/9jqzv/b6dH/2enV/9zp1f/e8Nf/2eTQ/9vp0v/c68//3OvS/93r1f/c69T/3+7Y/+Lv2P/j8Nf/5PLY/+Tz3v/n9Nn/6Pfd/+bO3f/l0OH/59Hf/+zV4P/o1ub/79jj/+3T4//s2eX/8tjp/+/Z5//w2+3/8tnt//Pb7//03e//zd7w/9Pg8v/Q4fL/0eXv/9Pl8//U4vT/2OfN/9rmzf/W6tL/2OvV/93t0f/a69j/3OvT/9brzv/X6dL/3ejR/9nq0v/f7NX/3e7U/+Dv2f/f79n/3/Pa/+Dv2//m9t7/5fXb/+fz3P/n0OL/6tPf/+jU4f/t1OT/6dbm//DX5//t2On/7dfk//HY6P/v3Oj/8tro//Hc6f/13+7/zt3x/8/d7f/R4e//0eTx/9Tm8v/W5fL/1uX2/9rm0f/Y69P/2+zQ/9jq0v/f7tL/3e/W/9vu1P/W5tP/3OnR/9rq0//c69X/4OrU/9zx2P/e8tj/4fHY/+Hw1//g8Nz/5fLd/+f42//o0OD/6M/j/+jU3v/p0eT/6tPj/+/Y4//t2Oj/79nq/+/V6f/y2Ob/89nt//Lc7P/23uz/0eDu/9Pg7P/S3/P/0uHy/9bm9v/W5Pf/1eb1/9bk0v/Y5s//2ujP/9rq0v/e7NT/3+/X/9/w2f/i7Nr/3OrT/9ro1f/b7NX/3u/X/97w0//c7Nf/4+/Z/+Pv2f/g89z/4fPf/+L23v/n0dv/6s7e/+bP3v/q0eP/7tbk/+/W4//q2Oj/79bn/+3Y5//u2ej/8djr//Ha6f/03+//89zw/9Lc7v/P4/D/0N7w/9Tl9f/V4/L/1+b1/9bk0f/W5NH/1uvT/9nn0P/Y7dP/3OvX/9vw2P/h7Nn/4u3W/9no1P/Y6db/2+rT/97s1f/d69n/4PDY/+Hv2//k79j/4fXd/+X13//k89//6c/g/+rT4v/pz+D/59Lg/+rS4//s1+b/8dTn/+/b5f/z2+n/7tzo//Ha6f/22uz/89/t/9Dd8f/T3+//0eHv/9Tj9P/U4PL/1uby/9Pj+P/Y5dL/1ubS/9zn1f/Y6dD/3OjR/9vt1f/b7tb/3fHb/+Py1//Z7NP/2+rU/9rv0//b7df/4u3a/93u2//g89v/4/Pe/+H12//o9dv/5s/f/+fN3f/m0OP/59Pi/+zT5P/v0uX/69Xm/+zX5//w1ur/89fm//Xd6v/13Ov/9t3t//Pd7//O4O//0N/u/9Lk8//R4PX/1+Xy/9bn9v/Y5c//2+vS/9jp0f/Z6tX/3uzS/93q1P/b8Nb/3e3V/+Lv2//i7tn/2enV/93v1f/g7tn/4u/a/+Pw1//k8Nf/4fLd/+f12v/k9N3/5vXe/+jQ4f/n0eP/6tPf/+rS4//t0ef/69fl//HX5P/x2ej/8dbo//Ta6P/y2+n/8t3r//jd6//O4fH/0N/y/9Ph8//V4vP/1OX2/9Xl9v/Y5Pb/2ObT/9fp1P/b7M//2OzS/9/q0//c7Nn/3+3a/+Hw1v/j79j/3/DZ/93r1//b6tL/4ezU/97x2f/g8tj/4vXa/+b12f/n8dv/6Pfe/+jO3P/l0eP/6dDi/+vV4P/t1uP/6tXo/+/V5v/u2uj/79fo//TY6v/12On/8dzr//fa7v/R3+//0N7w/9Pe7v/R4+//1OP0/9Xk8//Y6Pf/1+bR/9brzv/Z59T/3ejT/9rq0v/b7tL/3/DU/97s1f/e8dj/4vTd/+Hy2f/d69j/3+vY/97x2P/j79j/4fPX/+Hz3f/i8dn/5PPe/+bP4P/l0eH/5s7j/+vR4f/o1OX/7Nfm/+zT4//u2uX/79nl/+/b5v/z2O3/9dnu//ba7f/13+//z97s/9Pi7f/P5fH/0+Hv/9Xj8//U5/T/1+XP/9fmzv/OGx3/2e3V/9vq0P/b6dX/2+3T/97w1v/d8Nf/5PLa/9/z2f/h9dv/3PDT/93w1//e8tr/4/DY/+Dz3P/l9d7/4/bb/+P34P/m0eL/5c7f/+nQ3//t1eX/69bk/+vU6P/t1uT/7trp/+/a6v/01+r/8Njp//Pc7P/44O7/zOHx/9Lj8f/O4fP/1uTv/9Xi8//U5vX/1+b2/9bqz//Z59P/2+zU/97o1P/a6tb/3u3T/9/w0//f7tv/4O3Y/+Hy3P/i9dn/4/Xc/+Dr1//g8Nj/4+3Y/+Tu1//l8dv/4vPd/+P03f/k0Nv/587f/+rR4v/s1OL/7tHi/+3V4v/s1eb/7tro/+/X6v/02uv/9Nzr//Lb6P/23uv/9eHv/87f7//P3fD/z+Tw/9Hj9f/U4fP/2ef3/9bjzv/a6ND/2ujS/93o1P/a7NH/3u7S/+Dt2f/i7dj/4fDY/+Tw2v/g9dz/5vHZ/+j33//f79f/4vDY/+Hw2P/l8t3/4/Lb/+j32v/l893/5s3h/+fT4P/r0eT/7dTi/+7S4f/q1uP/8drn//Da5//v1+n/9Nvp//La6f/03u//9t7u/9Dd7P/Q3e7/1OP0/9Xk9P/R5/L/2OT1/9bj9//X5s7/2+bS/9fs0P/b69X/2e7R/97w1//c7tn/3u3X/9/y1//i8Nj/4vLb/+X13v/o+N//3e/a/+Hu1//l8dz/5fXZ/+T33f/m8uH/6M/f/+jO3//q097/69Hk/+jV5f/s0uX/7tnm//DZ6f/t2uv/9Nzq//Xb6v/23er/9dzv/8zf7P/P3/L/zuDu/9Tk9P/R4vX/1eLz/9ji+P/U6cz/2urP/9vp0f/Y7NT/2urW/9vv1v/e7tT/3O3Z/+Hy1v/j9Nr/4/XZ/+Pz3v/n9+D/49Df/+Dx1v/f9Nz/5PPd/+Ty2//m99z/5s7d/+jS4f/qz9//6NLh/+vR4f/r0uf/7tPl/+/X5f/s1+f/9Nvm//La6P/z2uv/9dzu//Le6//S4e3/z+Lv/9Hf9P/R4vP/0ub1/9To9P/V5s//2ujP/9joz//a6tT/2erR/93v1P/f79b/3O7X/+Lv2f/g8tn/4PHd/+bw3v/n8d7/5vTb/+bP3P/k89j/5fPY/+Tz3v/i9+D/5vPg/+TR3P/l0eD/7M/k/+rS5P/u0+H/6tTm/+3W6P/y1er/7tnl//Lb6P/02Oz/9N7r//Tg7P/R4e//zd7t/87j8f/V5PP/0+bx/9Xl9v/Y5PT/2ubN/9fp0P/X6ND/2+nV/93s0f/e79T/3vDZ/93w2v/e8Nj/4vDc/+Pw3f/i9Nv/4/bb/+nN3P/oz9//4fDZ/+Lv2P/i8d7/5PLc/+nQ2//o0t3/58/h/+zR5P/p1uH/6tbk/+zY6P/v1uX/7tnn//Lb6//12+j/897t//Tf7P/03ez/zeHy/9De7//R4vH/0eHx/9Pm8f/U5Pb/2ejO/9Xo0f/W6tP/3OvV/9zr1v/a6db/3u3Y/97t1f/e7dj/3vDb/+L12f/k89z/6PTe/+bz4f/lzd//6tHj/+Hy3P/l89z/4/La/+P34P/mzt3/6dTe/+jP4f/r1eD/6dPm/+/X6P/s2en/7dnl/+/X6v/12+n/8dns//Xg7P/43e7/0eHs/87h8f/U4u//1ODy/9Lm8//T5ff/1ef2/9Tp0f/c6M7/2+vQ/9rs0//b6tf/4OvX/97s1P/e8tv/5PHY/+Dz3P/m8Nn/5PTd/+j13P/m0eL/6M/i/+vQ3v/j9Nz/5vfb/+P43f/lzOH/59Lg/+jP4P/p0+X/7dXj/+3U5//s1ef/8dvo//Pa6v/x2ej/8djs//Tc6v/z3u3/0d7u/87f7P/O4vH/0uTw/9bh9f/X5PP/0+b1/9bmzP/V69H/2OzQ/9jq0f/d7NT/3OvT/93x1P/f7tf/3u7Y/9/u2//l9dr/4/Hb/+f02//o0OH/5tDe/+vQ4//q0uD/";

    [Fact]
    public void CreateDifferenceMap_SsimMatchesScikitImage()
    {
        var (expected, actual) = CreateWidePair(translucent: false);
        using (expected)
        using (actual)
        {
            using var expected8 = Reduce(expected);
            using var actual8 = Reduce(actual);
            using var map = ImageSimilarity.CreateDifferenceMap(expected8.Frames[0], actual8.Frames[0], new DifferenceMapOptions { Kind = DifferenceMapKind.Ssim, Gain = 4 }, Ct);
            var reference = Convert.FromBase64String(SkimageSsimMap);
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var pixel = map.Frames[0][x, y];
                    Assert.Equal(new Rgba32(reference[(y * map.Width) + x], reference[(y * map.Width) + x], reference[(y * map.Width) + x], 255), pixel);
                }
            }
        }
    }

    [Fact]
    public void CreateDifferenceMap_HighlightMatchesImageMagick()
    {
        var (expected, actual) = CreateWidePair(translucent: false);
        using (expected)
        using (actual)
        {
            using var expected8 = Reduce(expected);
            using var actual8 = expected8.Clone();
            for (var y = 3; y < 6; y++)
            {
                for (var x = 4; x < 9; x++)
                {
                    var pixel = actual8.Frames[0][x, y];
                    actual8.Frames[0][x, y] = new Rgba32((byte)(pixel.R ^ 0x20), pixel.G, pixel.B, pixel.A);
                }
            }

            var changed = actual8.Frames[0][30, 20];
            actual8.Frames[0][30, 20] = new Rgba32(changed.R, (byte)(changed.G ^ 1), changed.B, changed.A);
            using var map = ImageSimilarity.CreateDifferenceMap(expected8.Frames[0], actual8.Frames[0], cancellationToken: Ct);

            // ImageMagick composites in 16 bits: within one unit
            var reference = Convert.FromBase64String(ImageMagickHighlightMap);
            var bytes = new byte[reference.Length];
            map.Frames[0].CopyPixelBytesTo(bytes);
            for (var i = 0; i < bytes.Length; i++)
            {
                Assert.InRange(Math.Abs(bytes[i] - reference[i]), 0, 1);
            }

            Assert.Equal(new Rgba32(0xF1, 0x00, 0x1E, 0xCC), new DifferenceMapOptions().HighlightColor);
            Assert.InRange(map.Frames[0][30, 20].R, 190, 255);
            Assert.InRange(map.Frames[0][0, 0].G, 200, 255);
        }
    }

    [Fact]
    public void CreateDifferenceMap_AbsoluteShowsTheLargestDifference()
    {
        using var expected = new Image<Rgba32>(4, 2, new Rgba32(100, 100, 100, 255));
        using var actual = expected.Clone();
        actual.Frames[0][1, 0] = new Rgba32(110, 95, 100, 255);
        actual.Frames[0][2, 1] = new Rgba32(100, 100, 100, 0);
        expected.Frames[0][3, 1] = new Rgba32(1, 2, 3, 0);
        actual.Frames[0][3, 1] = new Rgba32(250, 0, 9, 0);
        using var map = ImageSimilarity.CreateDifferenceMap(expected.Frames[0], actual.Frames[0], new DifferenceMapOptions { Kind = DifferenceMapKind.Absolute, Gain = 2 }, Ct);

        // 10 levels doubled; a pixel made transparent differs by its alpha (clamped to white); two transparent pixels hiding
        // different colors do not differ
        Assert.Equal(new Rgba32(20, 20, 20, 255), map.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), map.Frames[0][2, 1]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), map.Frames[0][3, 1]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), map.Frames[0][0, 0]);
        Assert.Equal(expected.Configuration, map.Configuration);
    }

    [Fact]
    public void CreateDifferenceMap_HighlightThresholdIgnoresSmallDifferences()
    {
        using var expected = new Image<Rgba64>(3, 1, new Rgba64(0, 0, 0, 65535));
        using var actual = expected.Clone();
        actual.Frames[0][0, 0] = new Rgba64(600, 0, 0, 65535);
        actual.Frames[0][1, 0] = new Rgba64(7000, 0, 0, 65535);
        using var map = ImageSimilarity.CreateDifferenceMap(expected.Frames[0], actual.Frames[0], new DifferenceMapOptions { Threshold = 0.05 }, Ct);

        // White at 80% over black is 204; the highlight red at 80% over black is 0.8 × 241
        Assert.Equal(new Rgba32(204, 204, 204, 255), map.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(193, 0, 24, 255), map.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(204, 204, 204, 255), map.Frames[0][2, 0]);
    }

    /// <summary>
    /// Creates two 40x30 <see cref="Rgba64"/> frames from a linear congruential generator: ramps of 16-bit colors with noise,
    /// the actual frame within about 1,024 units of the expected one; translucent frames have an alpha ramp, lowered by up to
    /// 4,095 units in the actual frame.
    /// </summary>
    private static (Image<Rgba64> Expected, Image<Rgba64> Actual) CreateWidePair(bool translucent)
    {
        const int Width = 40;
        const int Height = 30;
        var expected = new Image<Rgba64>(Width, Height);
        var actual = new Image<Rgba64>(Width, Height);
        long state = 777;
        Span<ushort> e = stackalloc ushort[4];
        Span<ushort> a = stackalloc ushort[4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var value = (((x * 1500) + (y * 900) + (c * 20000)) % 50000) + (int)(Next() & 0x1FFF);
                    e[c] = (ushort)value;
                    a[c] = (ushort)Math.Clamp(value + (int)(Next() & 0x7FF) - 1024, 0, 65535);
                }

                e[3] = (ushort)(translucent ? 65535 - (x * 1000) : 65535);
                a[3] = translucent ? (ushort)(e[3] - (int)(Next() & 0xFFF)) : (ushort)65535;
                expected.Frames[0][x, y] = new Rgba64(e[0], e[1], e[2], e[3]);
                actual.Frames[0][x, y] = new Rgba64(a[0], a[1], a[2], a[3]);
            }
        }

        return (expected, actual);

        long Next()
        {
            state = ((1103515245 * state) + 12345) % (1L << 31);
            return state >> 8;
        }
    }

    /// <summary>Keeps the high byte of every 16-bit sample.</summary>
    private static Image<Rgba32> Reduce(Image<Rgba64> image)
    {
        var result = new Image<Rgba32>(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                result.Frames[0][x, y] = new Rgba32((byte)(pixel.R >> 8), (byte)(pixel.G >> 8), (byte)(pixel.B >> 8), (byte)(pixel.A >> 8));
            }
        }

        return result;
    }

    /// <summary>Scales the colors of a 16-bit frame to [0, 3.5] as single-precision values.</summary>
    private static Image<RgbaFloat> ScaleToHighDynamicRange(Image<Rgba64> image)
    {
        var result = new Image<RgbaFloat>(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                result.Frames[0][x, y] = new RgbaFloat((float)(pixel.R / 65535d * 3.5), (float)(pixel.G / 65535d * 3.5), (float)(pixel.B / 65535d * 3.5));
            }
        }

        return result;
    }

    private static double ComputeSsim(Rgba32[] expected, Rgba32[] actual, int width, int height)
    {
        using var expectedImage = Image.ImportPixelData<Rgba32>(expected, width, height);
        using var actualImage = Image.ImportPixelData<Rgba32>(actual, width, height);
        return ImageSimilarity.ComputeSsim(expectedImage.Frames[0], actualImage.Frames[0], Ct);
    }

    private static int ComputeHashDistance(bool useDHash, Image expected, Image actual)
    {
        return useDHash
            ? ImageSimilarity.ComputeDifferenceHashDistance(expected.Frames[0], actual.Frames[0], Ct)
            : ImageSimilarity.ComputePerceptualHashDistance(expected.Frames[0], actual.Frames[0], Ct);
    }

    private static Rgba32 Gray(int value) => new((byte)value, (byte)value, (byte)value);

    private static Image<Rgba32> CreateSolidImage(int width, int height, Rgba32 color) => new(width, height, color);

    private static void FillRectangle(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var row = y; row < y + height; row++)
            {
                pixels.GetRowSpan(row).Slice(x, width).Fill(color);
            }
        });
    }

    private static Image<Rgba32> CreatePatternImage(int width, int height, bool inverted, ImageConfiguration? configuration = null)
    {
        var pixels = new Rgba32[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (x * 17 + y * 31 + x * y * 3) % 256;
                pixels[y * width + x] = Gray(inverted ? 255 - value : value);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height, configuration: configuration);
    }

    private static Image<Rgba32> CreateGradientImage(int width, int height)
    {
        var pixels = new Rgba32[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = Gray(x * 255 / (width - 1));
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static Image ConvertTo(Image<Rgba32> image, PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 => image.Clone(),
        PixelFormat.Bgra32 => image.CloneAs<Bgra32>(),
        PixelFormat.Rgb24 => image.CloneAs<Rgb24>(),
        PixelFormat.Rgba64 => image.CloneAs<Rgba64>(),
        PixelFormat.Gray8 => image.CloneAs<Gray8>(),
        PixelFormat.Gray16 => image.CloneAs<Gray16>(),
        PixelFormat.La16 => image.CloneAs<La16>(),
        PixelFormat.La32 => image.CloneAs<La32>(),
        PixelFormat.Rgb48 => image.CloneAs<Rgb48>(),
        PixelFormat.A8 => image.CloneAs<A8>(),
        PixelFormat.Argb32 => image.CloneAs<Argb32>(),
        PixelFormat.Abgr32 => image.CloneAs<Abgr32>(),
        PixelFormat.Rgba1010102 => image.CloneAs<Rgba1010102>(),
        PixelFormat.RgbaFloat => image.CloneAs<RgbaFloat>(),
        PixelFormat.RgbaHalf => image.CloneAs<RgbaHalf>(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, message: null),
    };

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test pixels, and the fixed seed keeps the score reproducible.")]
    private static (Rgba32[] Expected, Rgba32[] Actual) CreateRandomPixels(int pixelCount)
    {
        var random = new Random(42);
        var expected = new Rgba32[pixelCount];
        var actual = new Rgba32[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            expected[i] = FromPacked((uint)random.Next() | (random.Next(2) is 0 ? 0xFF000000u : 0));

            // Mostly identical, so the score lands in the range a caller would actually set a threshold in
            actual[i] = random.Next(5) is 0 ? FromPacked((uint)random.Next()) : expected[i];
        }

        return (expected, actual);

        static Rgba32 FromPacked(uint value) => new((byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24));
    }

    /// <summary>Computes the ratio straight from its definition, in floating point: 10 log10(255² / MSE), color and alpha apart.</summary>
    private static double ComputeReferencePsnr(Rgba32[] expected, Rgba32[] actual)
    {
        double colorError = 0, alphaError = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var difference = Premultiplied(expected[i], channel) - Premultiplied(actual[i], channel);
                colorError += difference * difference;
            }

            var alphaDifference = expected[i].A - actual[i].A;
            alphaError += alphaDifference * alphaDifference;
        }

        return Math.Min(ToPsnr(colorError / (3.0 * expected.Length)), ToPsnr(alphaError / expected.Length));

        static double ToPsnr(double meanSquaredError) => meanSquaredError is 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / meanSquaredError);

        static double Premultiplied(Rgba32 pixel, int channel)
        {
            var value = channel switch
            {
                0 => pixel.R,
                1 => pixel.G,
                _ => pixel.B,
            };

            return (value * pixel.A + 127) / 255;
        }
    }

    /// <summary>Computes the score straight from its definition: every window is summed from scratch, in floating point.</summary>
    private static double ComputeReferenceSsim(Rgba32[] expected, Rgba32[] actual, int width, int height)
    {
        const double C1 = 0.01 * 255 * 0.01 * 255;
        const double C2 = 0.03 * 255 * 0.03 * 255;

        var windowWidth = Math.Min(7, width);
        var windowHeight = Math.Min(7, height);
        var count = windowWidth * windowHeight;
        var channelSums = new double[4];
        var windowCount = 0;
        for (var top = 0; top + windowHeight <= height; top++)
        {
            for (var left = 0; left + windowWidth <= width; left++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    double sumExpected = 0, sumActual = 0, sumExpectedSquared = 0, sumActualSquared = 0, sumCross = 0;
                    for (var y = top; y < top + windowHeight; y++)
                    {
                        for (var x = left; x < left + windowWidth; x++)
                        {
                            var e = GetChannel(expected[y * width + x], channel);
                            var a = GetChannel(actual[y * width + x], channel);
                            sumExpected += e;
                            sumActual += a;
                            sumExpectedSquared += e * e;
                            sumActualSquared += a * a;
                            sumCross += e * a;
                        }
                    }

                    var expectedMean = sumExpected / count;
                    var actualMean = sumActual / count;
                    var normalization = count > 1 ? count - 1 : 1;
                    var expectedVariance = (sumExpectedSquared - count * expectedMean * expectedMean) / normalization;
                    var actualVariance = (sumActualSquared - count * actualMean * actualMean) / normalization;
                    var covariance = (sumCross - count * expectedMean * actualMean) / normalization;
                    channelSums[channel] += (2 * expectedMean * actualMean + C1) * (2 * covariance + C2)
                        / ((expectedMean * expectedMean + actualMean * actualMean + C1) * (expectedVariance + actualVariance + C2));
                }

                windowCount++;
            }
        }

        var color = (channelSums[0] + channelSums[1] + channelSums[2]) / (3 * windowCount);
        var alpha = channelSums[3] / windowCount;
        return Math.Min(color, alpha);

        static double GetChannel(Rgba32 pixel, int channel)
        {
            if (channel is 3)
                return pixel.A;

            var value = channel switch
            {
                0 => pixel.R,
                1 => pixel.G,
                _ => pixel.B,
            };

            return (value * pixel.A + 127) / 255;
        }
    }
}
