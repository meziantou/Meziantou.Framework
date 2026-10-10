using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Auto-crop: background detection, content box, padding and canvas extension. The state checks shared with the other
/// operations are in <see cref="ProcessingTests"/>.
/// Every expectation is a hand-written literal derived from the documented rules (border tally capped at the color
/// threshold, Rec. 709 integer difference <c>2126 |dR| + 7152 |dG| + 722 |dB| &lt;= threshold * 10000</c>, alpha difference
/// below the threshold, luma buckets <c>min(10, Y * 11 / 255)</c>, weights as exact fractions); the arithmetic is
/// spelled out next to each case. Nothing is computed by the code under test.
/// </summary>
public sealed class AutoCropTests
{
    private static readonly Rgba64 White64 = new(65535, 65535, 65535);

    // 8 x 7: a 4 x 4 frame of 'X' around near-background pixels, surrounded by background
    private static readonly string[] Framed =
    [
        "........",
        "..XXXX..",
        "..X,,X..",
        "..X,,X..",
        "..XXXX..",
        "........",
        "........",
    ];

    // 6 x 5: a 3 x 3 content box one pixel away from the top-left corner, with near-background markers in two corners
    private static readonly string[] NearCorner =
    [
        ",.....",
        ".XXX..",
        ".X,X..",
        ".XXX..",
        ".....,",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<PixelFormat> AllFormats => [.. PixelFormats.All];

    // -----------------------------------------------------------------------------------------------------------------
    // Content box
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void ContentBoxIsDetectedAndCroppedForEveryPixelType(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.Rgba32: CheckContentBox<Rgba32>(); break;
            case PixelFormat.Bgra32: CheckContentBox<Bgra32>(); break;
            case PixelFormat.Rgb24: CheckContentBox<Rgb24>(); break;
            case PixelFormat.Rgba64: CheckContentBox<Rgba64>(); break;
            case PixelFormat.Gray8: CheckContentBox<Gray8>(); break;
            case PixelFormat.Gray16: CheckContentBox<Gray16>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void CheckContentBox<TPixel>()
        where TPixel : unmanaged
    {
        using var image = Draw<TPixel>(Framed);
        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Size(8, 7), analysis.CanvasSize);
        Assert.Equal(new Rectangle(2, 1, 4, 4), analysis.Bounds);
        Assert.Equal(Ink<TPixel>('.'), analysis.BackgroundColor);
        Assert.Equal(ExpectedBackground<TPixel>(), Widened(analysis));

        // Through the untyped image, the same typed analysis is returned
        var untyped = ((Image)image).AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Equal(Ink<TPixel>('.'), Assert.IsType<AutoCropAnalysis<TPixel>>(untyped).BackgroundColor);
        Assert.Equal(ExpectedBackground<TPixel>(), untyped.BackgroundColor);
        Assert.Equal(analysis.Bounds, untyped.Bounds);
        Assert.Equal(0d, analysis.WeightX);
        Assert.Equal(0d, analysis.WeightY);

        // The analysis changes nothing
        AssertRows(image.Frames[0], Framed);

        Assert.True(image.AutoCrop(cancellationToken: Ct));
        Assert.Equal(new Size(4, 4), image.Size);
        AssertRows(image.Frames[0], "XXXX", "X,,X", "X,,X", "XXXX");

        // The content now touches every edge: nothing more to crop
        var storage = image.Frames[0].Storage;
        Assert.False(image.AutoCrop(cancellationToken: Ct));
        Assert.Same(storage, image.Frames[0].Storage);
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    [InlineData(2, 3, false)] // too narrow
    [InlineData(3, 2, false)] // too low
    [InlineData(1, 1, false)]
    public void ContentSmallerThanThreeByThreeIsNotCropped(int contentWidth, int contentHeight, bool expected)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 255, 255));
        Fill(image.Frames[0], new Rectangle(2, 2, contentWidth, contentHeight), new Rgba32(0, 0, 0));
        var storage = image.Frames[0].Storage;

        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Equal(expected, analysis.Success);
        Assert.Equal(expected ? new Rectangle(2, 2, contentWidth, contentHeight) : new Rectangle(0, 0, 8, 8), analysis.Bounds);
        Assert.Equal(new Rgba32(255, 255, 255), analysis.BackgroundColor);
        Assert.Equal(White64, Widened(analysis));

        Assert.Equal(expected, image.AutoCrop(cancellationToken: Ct));
        if (!expected)
        {
            Assert.Equal(new Size(8, 8), image.Size);
            Assert.Same(storage, image.Frames[0].Storage);
        }
    }

    [Fact]
    public void UniformImageIsNotCropped()
    {
        using var image = new Image<Rgba32>(5, 4, new Rgba32(10, 20, 30));
        var storage = image.Frames[0].Storage;
        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.False(analysis.Success);
        Assert.Equal(new Rectangle(0, 0, 5, 4), analysis.Bounds);
        Assert.Equal(new Rgba32(10, 20, 30), analysis.BackgroundColor);
        Assert.Equal(new Rgba64(10 * 257, 20 * 257, 30 * 257), Widened(analysis));
        Assert.False(image.AutoCrop(cancellationToken: Ct));
        Assert.Same(storage, image.Frames[0].Storage);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tolerance: the limit is 35 * 10000 = 350,000 for colors (inclusive) and 35 for alpha (exclusive)
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(220, 220, 220, 255, false)] // 35 * (2126 + 7152 + 722) = 350,000
    [InlineData(219, 219, 219, 255, true)]  // 36 * 10000 = 360,000
    [InlineData(91, 255, 255, 255, false)]  // 164 * 2126 = 348,664
    [InlineData(90, 255, 255, 255, true)]   // 165 * 2126 = 350,790
    [InlineData(255, 207, 255, 255, false)] // 48 * 7152 = 343,296
    [InlineData(255, 206, 255, 255, true)]  // 49 * 7152 = 350,448
    [InlineData(255, 255, 0, 255, false)]   // 255 * 722 = 184,110: blue alone never exceeds the default threshold
    [InlineData(255, 255, 255, 221, false)] // alpha difference 34
    [InlineData(255, 255, 255, 220, true)]  // alpha difference 35 is not below 35
    [InlineData(255, 255, 255, 0, true)]    // fully transparent: read as transparent black
    public void DefaultToleranceBoundaries(byte r, byte g, byte b, byte a, bool isContent)
    {
        using var image = new Image<Rgba32>(7, 7, new Rgba32(255, 255, 255));
        Fill(image.Frames[0], new Rectangle(2, 2, 3, 3), new Rgba32(r, g, b, a));
        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Equal(isContent, analysis.Success);
        Assert.Equal(isContent ? new Rectangle(2, 2, 3, 3) : new Rectangle(0, 0, 7, 7), analysis.Bounds);
    }

    [Theory]
    [InlineData(56540, 65535, false)] // 65535 - 56540 = 8995 = 35 * 257
    [InlineData(56539, 65535, true)]
    [InlineData(65535, 56541, false)] // alpha difference 8994
    [InlineData(65535, 56540, true)]  // alpha difference 8995 is not below 35 * 257
    public void ToleranceIsScaledForSixteenBitSamples(ushort gray, ushort alpha, bool isContent)
    {
        using var image = new Image<Rgba64>(7, 7, new Rgba64(65535, 65535, 65535));
        Fill(image.Frames[0], new Rectangle(2, 2, 3, 3), new Rgba64(gray, gray, gray, alpha));
        Assert.Equal(isContent, image.AnalyzeAutoCrop(cancellationToken: Ct).Success);

        if (alpha == ushort.MaxValue)
        {
            using var grayImage = new Image<Gray16>(7, 7, new Gray16(65535));
            Fill(grayImage.Frames[0], new Rectangle(2, 2, 3, 3), new Gray16(gray));
            Assert.Equal(isContent, grayImage.AnalyzeAutoCrop(cancellationToken: Ct).Success);
        }
    }

    [Theory]
    [InlineData(253, false)] // difference 2 * 10000 = threshold * 10000
    [InlineData(252, true)]
    public void ColorThresholdSetsTheTolerance(byte gray, bool isContent)
    {
        using var image = new Image<Gray8>(7, 7, new Gray8(255));
        Fill(image.Frames[0], new Rectangle(2, 2, 3, 3), new Gray8(gray));
        Assert.Equal(isContent, image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 2 }, Ct).Success);
    }

    [Fact]
    public void FullyTransparentPixelsAreBackgroundWhateverTheirHiddenColor()
    {
        // 44 border pixels with 44 hidden colors: without the transparent-black rule the border would not be uniform
        using var image = new Image<Rgba32>(12, 12);
        var frame = image.Frames[0];
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                frame[x, y] = new Rgba32((byte)(x * 20), (byte)(y * 20), 7, 0);
            }
        }

        var content = new Rgba32(100, 150, 200);
        Fill(frame, new Rectangle(4, 5, 3, 4), content);

        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Rectangle(4, 5, 3, 4), analysis.Bounds);
        Assert.Equal(new Rgba32(0, 0, 0, 0), analysis.BackgroundColor);
        Assert.Equal(new Rgba64(0, 0, 0, 0), Widened(analysis));

        // Kept rectangle: x = 4 - 5 = -1, y = 5 - 1 = 4, 13 x 6. Column 0 is new; the hidden colors inside the canvas are copied
        Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 5, PaddingY = 1 }, Ct));
        Assert.Equal(new Size(13, 6), image.Size);
        frame = image.Frames[0];
        for (var y = 0; y < 6; y++)
        {
            Assert.Equal(new Rgba32(0, 0, 0, 0), frame[0, y]);
        }

        Assert.Equal(new Rgba32(0, 80, 7, 0), frame[1, 0]);     // source (0, 4)
        Assert.Equal(new Rgba32(220, 180, 7, 0), frame[12, 5]); // source (11, 9)
        Assert.Equal(content, frame[5, 1]);                      // source (4, 5)
        Assert.Equal(content, frame[7, 4]);                      // source (6, 8)
        Assert.Equal(new Rgba32(60, 100, 7, 0), frame[4, 1]);   // source (3, 5)
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Border detection
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void BorderIsFoundBelowTheColorThreshold()
    {
        // Three border colors (200, 201, 202) with a threshold of 4; 201 and 202 are within the tolerance of 200
        using var image = Gray(GrayCanvas(10, 10, 200, (1, 0, 201), (2, 0, 202)));
        Fill(image.Frames[0], new Rectangle(3, 3, 3, 3), new Gray8(0));
        var analysis = image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 4 }, Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Rectangle(3, 3, 3, 3), analysis.Bounds);
        Assert.Equal(new Gray8(200), analysis.BackgroundColor);
        Assert.Equal(new Rgba64(51400, 51400, 51400), Widened(analysis)); // 200 * 257
    }

    [Fact]
    public void BorderIsNotFoundAtTheColorThreshold()
    {
        // Four border colors with a threshold of 4. The retry (threshold 2, same rectangle since 10 / 20 = 0) tracks 200 and
        // 201 only, which is not fewer than 2 colors either. The reported background is the one of the first pass.
        using var image = Gray(GrayCanvas(10, 10, 200, (1, 0, 201), (2, 0, 202), (3, 0, 203)));
        Fill(image.Frames[0], new Rectangle(3, 3, 3, 3), new Gray8(0));
        var analysis = image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 4 }, Ct);
        Assert.False(analysis.Success);
        Assert.Equal(new Rectangle(0, 0, 10, 10), analysis.Bounds);
        Assert.Equal(new Gray8(200), analysis.BackgroundColor);
        Assert.Equal(new Rgba64(51400, 51400, 51400), Widened(analysis));
        Assert.False(image.AutoCrop(new AutoCropOptions { ColorThreshold = 4 }, Ct));
        Assert.Equal(new Size(10, 10), image.Size);
    }

    [Fact]
    public void ThresholdOfOneNeedsTheBucketThreshold()
    {
        // (5, 5) differs by 1 (background); the border pixel (6, 6) differs by 2 (content)
        using var image = Gray(GrayCanvas(7, 7, 255, (5, 5, 254), (6, 6, 253)));
        Fill(image.Frames[0], new Rectangle(2, 2, 3, 3), new Gray8(0));

        // One tracked color is not fewer than 1
        Assert.False(image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 1 }, Ct).Success);

        // All 24 border pixels are in the last luma bucket
        var analysis = image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 1, BucketThreshold = 1 }, Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Rectangle(2, 2, 5, 5), analysis.Bounds);
    }

    [Fact]
    public void RetryUsesHalfTheThresholdOnTheInsetRectangle()
    {
        // 40 x 40 with a noisy 2-pixel frame: the top row alone has 40 colors, so the first pass (threshold 35) fails.
        // The retry works on (2, 2, 36, 36), whose border is white, with a threshold of 18.
        using var image = new Image<Rgba32>(40, 40, new Rgba32(255, 255, 255));
        var frame = image.Frames[0];
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                if (x < 2 || x >= 38 || y < 2 || y >= 38)
                {
                    frame[x, y] = new Rgba32((byte)(x * 5), (byte)(y * 5), 77);
                }
            }
        }

        Fill(frame, new Rectangle(10, 12, 5, 4), new Rgba32(0, 0, 0));
        frame[20, 20] = new Rgba32(236, 236, 236); // difference 19: content for 18, background for 35
        frame[25, 25] = new Rgba32(237, 237, 237); // difference 18: background

        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Rectangle(10, 12, 11, 9), analysis.Bounds);
        Assert.Equal(new Rgba32(255, 255, 255), analysis.BackgroundColor);
        Assert.Equal(White64, Widened(analysis));

        Assert.True(image.AutoCrop(cancellationToken: Ct));
        Assert.Equal(new Size(11, 9), image.Size);
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(236, 236, 236), image.Frames[0][10, 8]);
    }

    [Fact]
    public void BucketThresholdAcceptsANoisyBorder()
    {
        // 20 border pixels: 11 x 200, one each of 201, 202, 203 and 204, and 5 x 100. With a threshold of 4 the table is
        // full after 200, 201, 202 and 203 (row 0), so the color test fails; the background is 200 (11 pixels).
        // Luma buckets: 200 to 204 give 8 (2200 / 255 to 2244 / 255), 100 gives 4. Bucket 8 holds 15 / 20 = 0.75.
        int[][] pixels =
        [
            [200, 201, 202, 203, 200, 200],
            [200, 200, 200, 200, 200, 200],
            [100, 200, 200, 200, 200, 200],
            [100, 200, 200, 0, 200, 200],
            [100, 200, 200, 200, 200, 204],
            [100, 100, 200, 200, 200, 200],
        ];

        using var image = Gray(pixels);
        var accepted = image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 4, BucketThreshold = 0.75 }, Ct);
        Assert.True(accepted.Success);

        // The 100s and the 0 are content; 201 to 204 are within the tolerance of 4
        Assert.Equal(new Rectangle(0, 2, 4, 4), accepted.Bounds);
        Assert.Equal(new Gray8(200), accepted.BackgroundColor);
        Assert.Equal(new Rgba64(51400, 51400, 51400), Widened(accepted));

        // 13 / 16 is above 0.75; the retry has no bucket test and tracks two colors with a threshold of 2
        var rejected = image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 4, BucketThreshold = 0.8125 }, Ct);
        Assert.False(rejected.Success);
        Assert.Equal(new Rectangle(0, 0, 6, 6), rejected.Bounds);
        Assert.Equal(new Gray8(200), rejected.BackgroundColor);
        Assert.Equal(new Rgba64(51400, 51400, 51400), Widened(rejected));

        Assert.False(image.AnalyzeAutoCrop(new AutoCropOptions { ColorThreshold = 4 }, Ct).Success);
    }

    [Theory]
    [InlineData(0, 0, 0, 255, 0)]
    [InlineData(23, 23, 23, 255, 0)]      // 253 / 255
    [InlineData(24, 24, 24, 255, 1)]      // 264 / 255
    [InlineData(231, 231, 231, 255, 9)]   // 2541 / 255
    [InlineData(232, 232, 232, 255, 10)]  // 2552 / 255
    [InlineData(255, 255, 255, 255, 10)]  // 11, capped
    [InlineData(255, 0, 0, 255, 2)]       // luma (2126 * 255 + 5000) / 10000 = 54; 594 / 255
    [InlineData(0, 0, 0, 0, 10)]          // transparent: white
    [InlineData(0, 0, 0, 128, 5)]         // (255 * 127 + 127) / 255 = 127; 1397 / 255
    public void LumaBuckets(byte r, byte g, byte b, byte a, int expected)
        => Assert.Equal(expected, AutoCropAnalyzer.GetBucket(r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24)));

    [Theory]
    [InlineData(210, 200)]
    [InlineData(200, 210)]
    public void MostFrequentBorderColorWinsAndTheFirstEncounteredBreaksTies(int first, int second)
    {
        // 16 border pixels, 8 of each color; the top-left pixel is the first encountered
        int[][] pixels =
        [
            [first, first, first, first, first],
            [second, 0, 0, 0, second],
            [second, 0, 0, 0, second],
            [second, 0, 0, 0, first],
            [first, second, second, second, first],
        ];

        using var image = Gray(pixels);
        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Rectangle(1, 1, 3, 3), analysis.Bounds);
        Assert.Equal(new Gray8((byte)first), analysis.BackgroundColor);
        Assert.Equal(new Rgba64((ushort)(first * 257), (ushort)(first * 257), (ushort)(first * 257)), Widened(analysis));

        // One more pixel of the second color makes it the most frequent (9 against 7)
        image.Frames[0][0, 0] = new Gray8((byte)second);
        analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Equal(new Gray8((byte)second), analysis.BackgroundColor);
        Assert.Equal(new Rgba64((ushort)(second * 257), (ushort)(second * 257), (ushort)(second * 257)), Widened(analysis));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Frames and poster
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ContentBoxIsTheUnionOverEveryFrameAndThePoster()
    {
        // Frame 0: (1, 1, 3, 3). Frame 1: (4, 2, 3, 3). Poster: frame 1 plus the pixel (2, 6). Union: (1, 1, 6, 6)
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 255, 255));
        var black = new Rgba32(0, 0, 0);
        Fill(image.Frames[0], new Rectangle(1, 1, 3, 3), black);
        var second = image.AppendFrame(image.Frames[0]);
        Fill(second, new Rectangle(0, 0, 8, 8), new Rgba32(255, 255, 255));
        Fill(second, new Rectangle(4, 2, 3, 3), black);
        var poster = image.SetPosterFrame(second);
        poster[2, 6] = black;
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 3);
        image.Frames[1].Metadata.Duration = new FrameDuration(2, 5);
        var first = image.Frames[0];

        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.True(analysis.Success);
        Assert.Equal(new Rectangle(1, 1, 6, 6), analysis.Bounds);

        Assert.True(image.AutoCrop(cancellationToken: Ct));
        Assert.Equal(new Size(6, 6), image.Size);
        Assert.Same(first, image.Frames[0]);
        Assert.Same(second, image.Frames[1]);
        Assert.Same(poster, image.PosterFrame);
        Assert.Equal(new FrameDuration(1, 3), image.Frames[0].Metadata.Duration);
        Assert.Equal(new FrameDuration(2, 5), image.Frames[1].Metadata.Duration);
        AssertRows(image.Frames[0], "###...", "###...", "###...", "......", "......", "......");
        AssertRows(image.Frames[1], "......", "...###", "...###", "...###", "......", "......");
        AssertRows(image.PosterFrame!, "......", "...###", "...###", "...###", "......", ".#....");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Padding
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PaddingInsideTheCanvasKeepsTheOriginalPixels()
    {
        using var image = Draw<Rgba32>(
            ".........",
            "..,,,,,..",
            "..,...,..",
            "..,XXX,..",
            "..,X.X,..",
            "..,XXX,..",
            "..,...,..",
            "..,,,,,..",
            ".........");

        // Content (3, 3, 3, 3); kept rectangle (2, 1, 5, 7)
        Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 1, PaddingY = 2 }, Ct));
        AssertRows(image.Frames[0], ",,,,,", ",...,", ",XXX,", ",X.X,", ",XXX,", ",...,", ",,,,,");
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void ExpandFillsOutsideTheCanvasWithTheBackgroundForEveryPixelType(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.Rgba32: CheckExpand<Rgba32>(); break;
            case PixelFormat.Bgra32: CheckExpand<Bgra32>(); break;
            case PixelFormat.Rgb24: CheckExpand<Rgb24>(); break;
            case PixelFormat.Rgba64: CheckExpand<Rgba64>(); break;
            case PixelFormat.Gray8: CheckExpand<Gray8>(); break;
            case PixelFormat.Gray16: CheckExpand<Gray16>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void CheckExpand<TPixel>()
        where TPixel : unmanaged
    {
        foreach (var layout in new PixelStorageLayoutOptions?[] { null, new() { RowAlignment = 16, TargetSlabBytes = 16 } })
        {
            // Content (1, 1, 3, 3); kept rectangle (-1, -1, 7, 7): row 0, column 0 and row 6 are outside the 6 x 5 canvas
            using var image = Draw<TPixel>(layout, NearCorner);
            Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 2, PaddingY = 2 }, Ct));
            AssertRows(
                image.Frames[0],
                ".......",
                ".,.....",
                "..XXX..",
                "..X,X..",
                "..XXX..",
                "......,",
                ".......");

            // Contain: the clamped rectangle is the whole canvas
            using var contained = Draw<TPixel>(layout, NearCorner);
            var storage = contained.Frames[0].Storage;
            Assert.False(contained.AutoCrop(new AutoCropOptions { PaddingX = 2, PaddingY = 2, PaddingMode = AutoCropPaddingMode.Contain }, Ct));
            Assert.Same(storage, contained.Frames[0].Storage);
            AssertRows(contained.Frames[0], NearCorner);
        }
    }

    [Fact]
    public void ContainClampsThePaddedRectangleToTheCanvas()
    {
        using var image = Draw<Rgba32>(
            "......,,..",
            "..........",
            "..........",
            ".XXX......",
            ".XXX......",
            ".XXX......",
            "..........",
            "..........");

        // Content (1, 3, 3, 3) padded by 3: (-2, 0, 9, 9), clamped to (0, 0, 7, 8)
        Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 3, PaddingY = 3, PaddingMode = AutoCropPaddingMode.Contain }, Ct));
        AssertRows(
            image.Frames[0],
            "......,",
            ".......",
            ".......",
            ".XXX...",
            ".XXX...",
            ".XXX...",
            ".......",
            ".......");
    }

    [Fact]
    public void HugePaddingIsALimitErrorOrAClamp()
    {
        using var image = Draw<Rgba32>(Framed);
        var storage = image.Frames[0].Storage;

        var width = Assert.Throws<ImageResourceLimitException>(() => image.AutoCrop(new AutoCropOptions { PaddingX = int.MaxValue }, Ct));
        Assert.Equal(ImageResourceLimitKind.Width, width.Kind);
        Assert.Equal(4L + (2L * int.MaxValue), width.Requested);

        var height = Assert.Throws<ImageResourceLimitException>(() => image.AutoCrop(new AutoCropOptions { PaddingY = int.MaxValue }, Ct));
        Assert.Equal(ImageResourceLimitKind.Height, height.Kind);

        Assert.False(image.AutoCrop(new AutoCropOptions { PaddingX = int.MaxValue, PaddingY = int.MaxValue, PaddingMode = AutoCropPaddingMode.Contain }, Ct));
        Assert.Same(storage, image.Frames[0].Storage);
        AssertRows(image.Frames[0], Framed);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Weights
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WeightsAreTheMeanSignedPositionOfTheDifferenceFromTheBackground()
    {
        // 8 x 8, black (difference 1) at x = 4..6, y = 2..4 on white.
        // X: 2x + 1 - 8 = 1, 3, 5 for each of 3 rows: 27. Y: 2y + 1 - 8 = -3, -1, 1 for each of 3 columns: -9.
        // Divided by 8 * 64 pixels: 27 / 512 and -9 / 512 (exact in binary).
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 255, 255));
        Fill(image.Frames[0], new Rectangle(4, 2, 3, 3), new Rgba32(0, 0, 0));

        var analysis = image.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true }, Ct);
        Assert.Equal(new Rectangle(4, 2, 3, 3), analysis.Bounds);
        Assert.Equal(0.052734375, analysis.WeightX);
        Assert.Equal(-0.017578125, analysis.WeightY);

        var unweighted = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Equal(0d, unweighted.WeightX);
        Assert.Equal(0d, unweighted.WeightY);

        using var gray = new Image<Gray16>(8, 8, new Gray16(65535));
        Fill(gray.Frames[0], new Rectangle(4, 2, 3, 3), new Gray16(0));
        var grayAnalysis = gray.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true }, Ct);
        Assert.Equal(0.052734375, grayAnalysis.WeightX);
        Assert.Equal(-0.017578125, grayAnalysis.WeightY);

        // A second, blank frame halves the mean: 27 / 1024 and -9 / 1024
        var blank = image.AppendFrame();
        Fill(blank, new Rectangle(0, 0, 8, 8), new Rgba32(255, 255, 255));
        analysis = image.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true }, Ct);
        Assert.Equal(0.0263671875, analysis.WeightX);
        Assert.Equal(-0.0087890625, analysis.WeightY);
    }

    [Fact]
    public void WeightsShiftThePaddedRectangle()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 255, 255));
        var black = new Rgba32(0, 0, 0);
        var white = new Rgba32(255, 255, 255);
        Fill(image.Frames[0], new Rectangle(4, 2, 3, 3), black);
        using var unweighted = image.Clone();

        // Shift: trunc(40 * 27 / 512) = trunc(2.109375) = 2 and trunc(60 * -9 / 512) = trunc(-1.0546875) = -1.
        // Kept rectangle: (4 - 40 + 2, 2 - 60 - 1, 3 + 80, 3 + 120) = (-34, -59, 83, 123); the content is at (38, 61).
        Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 40, PaddingY = 60, AnalyzeWeights = true }, Ct));
        Assert.Equal(new Size(83, 123), image.Size);
        var frame = image.Frames[0];
        Assert.Equal(black, frame[38, 61]);
        Assert.Equal(black, frame[40, 63]);
        Assert.Equal(white, frame[37, 61]);
        Assert.Equal(white, frame[41, 61]);
        Assert.Equal(white, frame[38, 60]);
        Assert.Equal(white, frame[38, 64]);
        Assert.Equal(white, frame[0, 0]);
        Assert.Equal(white, frame[82, 122]);

        // Without weights: (-36, -58, 83, 123); the content is at (40, 60)
        Assert.True(unweighted.AutoCrop(new AutoCropOptions { PaddingX = 40, PaddingY = 60 }, Ct));
        Assert.Equal(new Size(83, 123), unweighted.Size);
        Assert.Equal(black, unweighted.Frames[0][40, 60]);
        Assert.Equal(black, unweighted.Frames[0][42, 62]);
        Assert.Equal(white, unweighted.Frames[0][39, 60]);
        Assert.Equal(white, unweighted.Frames[0][40, 63]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Applying an existing analysis
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void KnownAnalysisIsAppliedWithoutAnalyzingAgain()
    {
        using var source = Draw<Rgba32>(Framed);
        var analysis = source.AnalyzeAutoCrop(cancellationToken: Ct);

        using var clone = source.Clone();
        Assert.True(clone.AutoCrop(analysis, new AutoCropOptions { PaddingX = 1 }, Ct));
        AssertRows(clone.Frames[0], ".XXXX.", ".X,,X.", ".X,,X.", ".XXXX.");

        // A blank image of the same size has no content of its own: the box of the analysis is used as is
        using var blank = new Image<Rgba32>(8, 7, new Rgba32(1, 2, 3));
        Assert.True(blank.AutoCrop(analysis, cancellationToken: Ct));
        Assert.Equal(new Size(4, 4), blank.Size);
        Assert.Equal(new Rgba32(1, 2, 3), blank.Frames[0][3, 3]);

        // The source is untouched
        AssertRows(source.Frames[0], Framed);
    }

    [Fact]
    public void KnownAnalysisMustMatchTheCanvasSize()
    {
        using var source = Draw<Rgba32>(Framed);
        var analysis = source.AnalyzeAutoCrop(cancellationToken: Ct);
        using var other = new Image<Rgba32>(8, 8, new Rgba32(255, 255, 255));
        Assert.Throws<ArgumentException>("analysis", () => other.AutoCrop(analysis, cancellationToken: Ct));
        Assert.Equal(new Size(8, 8), other.Size);
    }

    [Fact]
    public void FailedAnalysisLeavesTheImageUnchanged()
    {
        using var uniform = new Image<Rgba32>(8, 7, new Rgba32(255, 255, 255));
        var analysis = uniform.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.False(analysis.Success);

        using var image = Draw<Rgba32>(Framed);
        var storage = image.Frames[0].Storage;
        Assert.False(image.AutoCrop(analysis, new AutoCropOptions { PaddingX = 5 }, Ct));
        Assert.Same(storage, image.Frames[0].Storage);
        AssertRows(image.Frames[0], Framed);
    }

    [Fact]
    public void BackgroundThatThePixelFormatCannotRepresentIsRejectedBeforeEnlarging()
    {
        // A transparent background, applied to pixels without alpha
        using var transparent = new Image<Rgba32>(8, 7);
        Fill(transparent.Frames[0], new Rectangle(2, 1, 4, 4), new Rgba32(10, 20, 30));
        var analysis = transparent.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Equal(new Rectangle(2, 1, 4, 4), analysis.Bounds);
        Assert.Equal(new Rgba32(0, 0, 0, 0), analysis.BackgroundColor);
        Assert.Equal(new Rgba64(0, 0, 0, 0), Widened(analysis));

        using var opaque = Draw<Rgb24>(Framed);
        var storage = opaque.Frames[0].Storage;
        Assert.Throws<UnsupportedImageFeatureException>(() => opaque.AutoCrop(analysis, new AutoCropOptions { PaddingX = 3 }, Ct));
        Assert.Same(storage, opaque.Frames[0].Storage);
        AssertRows(opaque.Frames[0], Framed);

        // No fill is needed inside the canvas: (-1, 1, 10, 4) clamped to (0, 1, 8, 4)
        Assert.True(opaque.AutoCrop(analysis, new AutoCropOptions { PaddingX = 3, PaddingMode = AutoCropPaddingMode.Contain }, Ct));
        AssertRows(opaque.Frames[0], "..XXXX..", "..X,,X..", "..X,,X..", "..XXXX..");
    }

    [Theory]
    [InlineData(65535, 65535, 65535, 65535, true, true, true, true, true)]
    [InlineData(514, 514, 514, 65535, true, true, true, true, true)]
    [InlineData(0, 0, 0, 0, true, false, true, false, false)]             // not opaque
    [InlineData(257, 514, 771, 65535, true, true, true, false, false)]    // not gray
    [InlineData(258, 258, 258, 65535, false, false, true, false, true)]   // not an 8-bit value
    [InlineData(257, 257, 257, 32768, false, false, true, false, false)]  // alpha is not an 8-bit value
    public void ExactlyRepresentableColors(ushort r, ushort g, ushort b, ushort a, bool rgba32, bool rgb24, bool rgba64, bool gray8, bool gray16)
    {
        var color = new Rgba64(r, g, b, a);
        Assert.Equal(rgba32, PixelConverter.IsExactlyRepresentable(color, PixelFormat.Rgba32));
        Assert.Equal(rgba32, PixelConverter.IsExactlyRepresentable(color, PixelFormat.Bgra32));
        Assert.Equal(rgb24, PixelConverter.IsExactlyRepresentable(color, PixelFormat.Rgb24));
        Assert.Equal(rgba64, PixelConverter.IsExactlyRepresentable(color, PixelFormat.Rgba64));
        Assert.Equal(gray8, PixelConverter.IsExactlyRepresentable(color, PixelFormat.Gray8));
        Assert.Equal(gray16, PixelConverter.IsExactlyRepresentable(color, PixelFormat.Gray16));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // State: the analysis is read-only, the crop is transactional
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AnalysisRentsNothingAndChangesNothing()
    {
        var pool = new SlabPool();
        using var image = DrawWithPool(pool, NearCorner);
        var snapshot = Snapshot.Capture(image);
        var rentals = 0;
        pool.RentFailureInjector = _ =>
        {
            rentals++;
            return null;
        };

        var analysis = image.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true, BucketThreshold = 0.5 }, Ct);

        pool.RentFailureInjector = null;
        Assert.True(analysis.Success);
        Assert.Equal(0, rentals);
        snapshot.AssertUnchanged(image);
    }

    [Fact]
    public void AnalysisIsRejectedWhileAFrameIsLeased()
    {
        using var image = Draw<Rgba32>(Framed);
        image.Frames[0].ProcessPixelRows(image, static (_, image) => Assert.Throws<InvalidOperationException>(() => image.AnalyzeAutoCrop(cancellationToken: Ct)));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.True(image.AnalyzeAutoCrop(cancellationToken: Ct).Success);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)] // the poster replacement
    public void AllocationFailureWhileEnlargingLeavesTheImageUnchanged(int failingRental)
    {
        var pool = new SlabPool();
        using var image = DrawWithPool(pool, NearCorner);
        var snapshot = Snapshot.Capture(image);
        var rentals = 0;
        pool.RentFailureInjector = _ => ++rentals == failingRental ? new InjectedAllocationFailureException() : null;

        Assert.Throws<InjectedAllocationFailureException>(() => image.AutoCrop(new AutoCropOptions { PaddingX = 2, PaddingY = 2 }, Ct));

        pool.RentFailureInjector = null;
        snapshot.AssertUnchanged(image);
        Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 2, PaddingY = 2 }, Ct)); // still usable
        Assert.Equal(new Size(7, 7), image.Size);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CancellationWhileEnlargingLeavesTheImageUnchanged(int cancelAtRental)
    {
        var pool = new SlabPool();
        using var image = DrawWithPool(pool, NearCorner);
        var snapshot = Snapshot.Capture(image);
        using var cancellation = new CancellationTokenSource();
        var rentals = 0;
        pool.RentFailureInjector = _ =>
        {
            if (++rentals == cancelAtRental)
            {
                cancellation.Cancel();
            }

            return null;
        };

        Assert.ThrowsAny<OperationCanceledException>(() => image.AutoCrop(new AutoCropOptions { PaddingX = 2, PaddingY = 2 }, cancellation.Token));

        pool.RentFailureInjector = null;
        snapshot.AssertUnchanged(image);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The pixel of an ink: '.' is the background, ',' a color within the default tolerance of the background, 'X' the
    /// content ('#' is opaque black, for images built on white). The 16-bit inks have distinct low bytes.
    /// </summary>
    private static TPixel Ink<TPixel>(char ink)
        where TPixel : unmanaged
    {
        if (ink == '#')
            return Ink<TPixel>(0, 0, 0, 0, 0, 0);

        return ink switch
        {
            '.' => Ink<TPixel>(255, 255, 255, 0xFFFE, 0xFFFD, 0xFFFC),
            ',' => Ink<TPixel>(250, 250, 250, 0xFEFE, 0xFDFD, 0xFCFC),
            'X' => Ink<TPixel>(10, 20, 30, 0x0102, 0x0304, 0x0506),
            _ => throw new ArgumentOutOfRangeException(nameof(ink)),
        };
    }

    private static TPixel Ink<TPixel>(byte r, byte g, byte b, ushort r16, ushort g16, ushort b16)
        where TPixel : unmanaged
    {
        object pixel = typeof(TPixel) switch
        {
            var t when t == typeof(Rgba32) => new Rgba32(r, g, b),
            var t when t == typeof(Bgra32) => new Bgra32(r, g, b),
            var t when t == typeof(Rgb24) => new Rgb24(r, g, b),
            var t when t == typeof(Rgba64) => new Rgba64(r16, g16, b16),
            var t when t == typeof(Gray8) => new Gray8(r),
            var t when t == typeof(Gray16) => new Gray16(r16),
            _ => throw new NotSupportedException(),
        };

        return (TPixel)pixel;
    }

    /// <summary>The background of an analysis as the untyped API gives it: widened to 16 bits.</summary>
    private static Rgba64 Widened(AutoCropAnalysis analysis) => analysis.BackgroundColor;

    /// <summary>The '.' ink widened to 16 bits by hand.</summary>
    private static Rgba64 ExpectedBackground<TPixel>()
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(Rgba64))
            return new Rgba64(0xFFFE, 0xFFFD, 0xFFFC);

        if (typeof(TPixel) == typeof(Gray16))
            return new Rgba64(0xFFFE, 0xFFFE, 0xFFFE);

        return White64;
    }

    private static Image<TPixel> Draw<TPixel>(params string[] rows)
        where TPixel : unmanaged
        => Draw<TPixel>(layout: null, rows);

    private static Image<TPixel> Draw<TPixel>(PixelStorageLayoutOptions? layout, params string[] rows)
        where TPixel : unmanaged
    {
        var image = new Image<TPixel>(ImageConfiguration.Default, new Size(rows[0].Length, rows.Length), scope: null, layout);
        DrawFrame(image.Frames[0], rows);
        return image;
    }

    /// <summary>A two-frame Rgba32 image with a poster, charged to a scope that uses <paramref name="pool"/>.</summary>
    private static Image<Rgba32> DrawWithPool(SlabPool pool, string[] rows)
    {
        var configuration = ImageConfiguration.Default;
        var image = new Image<Rgba32>(configuration, new Size(rows[0].Length, rows.Length), new AllocationScope(configuration.Limits, pool), layoutOptions: null);
        DrawFrame(image.Frames[0], rows);
        image.AppendFrame(image.Frames[0]);
        image.SetPosterFrame(image.Frames[0]);
        return image;
    }

    private static void DrawFrame<TPixel>(ImageFrame<TPixel> frame, string[] rows)
        where TPixel : unmanaged
    {
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                frame[x, y] = Ink<TPixel>(rows[y][x]);
            }
        }
    }

    private static void AssertRows<TPixel>(ImageFrame<TPixel> frame, params string[] rows)
        where TPixel : unmanaged
    {
        Assert.Equal(new Size(rows[0].Length, rows.Length), frame.Size);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                var expected = Ink<TPixel>(rows[y][x]);
                var actual = frame[x, y];
                if (!EqualityComparer<TPixel>.Default.Equals(expected, actual))
                    Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Pixel ({x},{y}): expected '{rows[y][x]}' ({expected}), got {actual}."));
            }
        }
    }

    private static void Fill<TPixel>(ImageFrame<TPixel> frame, Rectangle rectangle, TPixel pixel)
        where TPixel : unmanaged
    {
        for (var y = rectangle.Top; y < rectangle.Bottom; y++)
        {
            for (var x = rectangle.Left; x < rectangle.Right; x++)
            {
                frame[x, y] = pixel;
            }
        }
    }

    /// <summary>A gray canvas of one value with a few pixels set.</summary>
    private static int[][] GrayCanvas(int width, int height, int value, params (int X, int Y, int Value)[] pixels)
    {
        var rows = new int[height][];
        for (var y = 0; y < height; y++)
        {
            rows[y] = new int[width];
            rows[y].AsSpan().Fill(value);
        }

        foreach (var (x, y, pixel) in pixels)
        {
            rows[y][x] = pixel;
        }

        return rows;
    }

    private static Image<Gray8> Gray(int[][] rows)
    {
        var image = new Image<Gray8>(rows[0].Length, rows.Length);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                image.Frames[0][x, y] = new Gray8((byte)rows[y][x]);
            }
        }

        return image;
    }

    /// <summary>Everything a read-only analysis or a failed auto-crop must preserve.</summary>
    private sealed class Snapshot
    {
        private Size _size;
        private ImageFrame<Rgba32>[] _frames = [];
        private PixelStorage[] _storages = [];
        private Rgba32[][] _pixels = [];
        private long _liveBytes;

        public static Snapshot Capture(Image<Rgba32> image)
        {
            ImageFrame<Rgba32>[] frames = [.. (IEnumerable<ImageFrame<Rgba32>>)image.Frames, image.PosterFrame!];
            return new Snapshot
            {
                _size = image.Size,
                _frames = frames,
                _storages = [.. frames.Select(frame => frame.Storage)],
                _pixels = [.. frames.Select(CopyPixels)],
                _liveBytes = image.Owner.Scope.LiveBytes,
            };
        }

        public void AssertUnchanged(Image<Rgba32> image)
        {
            ImageFrame<Rgba32>[] frames = [.. (IEnumerable<ImageFrame<Rgba32>>)image.Frames, image.PosterFrame!];
            Assert.Equal(_size, image.Size);
            Assert.HasCount(_frames.Length, frames);
            for (var i = 0; i < frames.Length; i++)
            {
                Assert.Same(_frames[i], frames[i]);
                Assert.Same(_storages[i], frames[i].Storage);
                Assert.Equal(_pixels[i], CopyPixels(frames[i]));
            }

            Assert.Equal(_liveBytes, image.Owner.Scope.LiveBytes);
            Assert.Equal(0, image.Owner.Scope.GetDiagnostics().ReservedBytes);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }

        private static Rgba32[] CopyPixels(ImageFrame<Rgba32> frame)
        {
            var pixels = new Rgba32[frame.Width * frame.Height];
            frame.CopyPixelDataTo(pixels);
            return pixels;
        }
    }
}
