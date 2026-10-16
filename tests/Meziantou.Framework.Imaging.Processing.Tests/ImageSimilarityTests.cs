using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// SSIM, dHash and pHash. The expected scores and hashes come from scikit-image, from an independent Python
/// implementation of the area-weighted thumbnail, from a brute-force computation of the SSIM definition written in the
/// test, or from hand-built images whose hashes follow from the definition.
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
        using var reference = CreatePatternImage(23, 19, inverted: false);
        foreach (var format in PixelFormats.All)
        {
            using var converted = ConvertTo(reference, format);
            Assert.Equal(1d, ImageSimilarity.ComputeSsim(reference.Frames[0], converted.Frames[0], Ct));
            Assert.Equal(0, ImageSimilarity.ComputeDifferenceHashDistance(reference.Frames[0], converted.Frames[0], Ct));
            Assert.Equal(0, ImageSimilarity.ComputePerceptualHashDistance(converted.Frames[0], reference.Frames[0], Ct));
            Assert.Equal(ImageSimilarity.ComputeDifferenceHash(reference.Frames[0], Ct), ImageSimilarity.ComputeDifferenceHash(converted.Frames[0], Ct));
            Assert.Equal(ImageSimilarity.ComputePerceptualHash(reference.Frames[0], Ct), ImageSimilarity.ComputePerceptualHash(converted.Frames[0], Ct));
        }
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

        Assert.Throws<ArgumentException>("actual", () => ImageSimilarity.ComputeSsim(frame, other.Frames[0], Ct));
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
        });

        // The lease of the other frame was released when the first one failed
        Assert.Equal(0, other.Owner.ActiveLeaseCount);

        var disposed = CreatePatternImage(8, 8, inverted: false);
        var disposedFrame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputeSsim(frame, disposedFrame, Ct));
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputePerceptualHash(disposedFrame, Ct));
        Assert.Throws<ObjectDisposedException>(() => ImageSimilarity.ComputeDifferenceHashDistance(disposedFrame, frame, Ct));
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

    private static Image<Rgba32> CreatePatternImage(int width, int height, bool inverted)
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

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
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
