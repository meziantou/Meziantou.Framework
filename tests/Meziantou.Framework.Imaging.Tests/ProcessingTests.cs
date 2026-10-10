using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Color;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Crop, quarter-turn rotations, auto-orient, flip and grayscale, and the state
/// checks shared with convolve (its numerics are in <see cref="ConvolutionTests"/>).
/// Every expectation is a hand-written literal: pixels are identified by small integers ("ids") laid out as the visual
/// grid they form, and each expected grid was derived by hand from the definition of the operation (and, for
/// auto-orient, from the EXIF definitions of the 0th row and 0th column of each orientation). Nothing is computed by the
/// code under test.
/// </summary>
public sealed class ProcessingTests
{
    // Source used by every orientation test (W = 3, H = 2):
    //   A B C      1 2 3
    //   D E F      4 5 6
    private static readonly int[][] Source3X2 = [[1, 2, 3], [4, 5, 6]];

    // Rotations are clockwise
    private static readonly int[][] Rotated90 = [[4, 1], [5, 2], [6, 3]];
    private static readonly int[][] Rotated180 = [[6, 5, 4], [3, 2, 1]];
    private static readonly int[][] Rotated270 = [[3, 6], [2, 5], [1, 4]];
    private static readonly int[][] FlippedHorizontally = [[3, 2, 1], [6, 5, 4]];
    private static readonly int[][] FlippedVertically = [[4, 5, 6], [1, 2, 3]];

    // EXIF 5 (0th row = visual left, 0th column = visual top): A at the top-left, stored row 0 becomes the left column
    private static readonly int[][] Transposed = [[1, 4], [2, 5], [3, 6]];

    // EXIF 7 (0th row = visual right, 0th column = visual bottom): A at the bottom-right, stored row 0 becomes the right column read upward
    private static readonly int[][] Transversed = [[6, 3], [5, 2], [4, 1]];

    private static readonly ConvolutionOptions Sharpen = new(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<PixelFormat> AllFormats => [.. PixelFormats.All];

    public static TheoryData<int> AllOrientations => [1, 2, 3, 4, 5, 6, 7, 8];

    // -----------------------------------------------------------------------------------------------------------------
    // Rotations and flips: exact permutations for every pixel type (16-bit low bits and alpha included)
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RotationsAndFlipsArePermutationsForEveryPixelType(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.Rgba32: CheckRotationsAndFlips<Rgba32>(); break;
            case PixelFormat.Bgra32: CheckRotationsAndFlips<Bgra32>(); break;
            case PixelFormat.Rgb24: CheckRotationsAndFlips<Rgb24>(); break;
            case PixelFormat.Rgba64: CheckRotationsAndFlips<Rgba64>(); break;
            case PixelFormat.Gray8: CheckRotationsAndFlips<Gray8>(); break;
            case PixelFormat.Gray16: CheckRotationsAndFlips<Gray16>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Theory]
    [MemberData(nameof(AllOrientations))]
    public void AutoOrientHandlesEveryExifOrientation(int orientation)
    {
        int[][] expected = orientation switch
        {
            1 => Source3X2,
            2 => FlippedHorizontally, // 0th row top, 0th column right
            3 => Rotated180,          // 0th row bottom, 0th column right
            4 => FlippedVertically,   // 0th row bottom, 0th column left
            5 => Transposed,
            6 => Rotated90,           // 0th row right, 0th column top
            7 => Transversed,
            8 => Rotated270,          // 0th row left, 0th column bottom
            _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
        };

        using var image = Build<Rgba64>(Source3X2);
        image.Metadata.Orientation = (ExifOrientation)orientation;
        var frame = image.Frames[0];
        image.AutoOrient(Ct);

        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        Assert.Same(frame, image.Frames[0]);
        AssertFrame(image.Frames[0], expected);
        Assert.Equal(new Size(expected[0].Length, expected.Length), image.Size);
    }

    [Fact]
    public void AutoOrientMatchesTheEquivalentExplicitOperations()
    {
        // Auto-orient of orientation 7 equals a transpose then a 180 degree rotation; orientation 5 a 90 degree rotation then a horizontal flip
        using var auto7 = Build<Rgba32>(Source3X2);
        auto7.Metadata.Orientation = ExifOrientation.RightBottom;
        auto7.AutoOrient(Ct);
        using var auto5 = Build<Rgba32>(Source3X2);
        auto5.Metadata.Orientation = ExifOrientation.LeftTop;
        auto5.AutoOrient(Ct);
        using var explicit5 = Build<Rgba32>(Source3X2);
        explicit5.Rotate(RotateMode.Rotate90, Ct);
        explicit5.Flip(FlipMode.Horizontal, Ct);
        using var explicit7 = Build<Rgba32>(Transposed);
        explicit7.Rotate(RotateMode.Rotate180, Ct);

        AssertFrame(explicit5.Frames[0], Transposed);
        AssertFrame(auto5.Frames[0], Transposed);
        AssertFrame(explicit7.Frames[0], Transversed);
        AssertFrame(auto7.Frames[0], Transversed);
    }

    [Fact]
    public void AutoOrientOfAnUprightImageDoesNothing()
    {
        using var image = Build<Rgba32>(Source3X2);
        var exif = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(ExifTiff.OrientationTag, 1)).Thumbnail("THUMB"u8.ToArray()).Build()));
        image.Metadata.ExifProfile = exif;
        var storage = image.Frames[0].Storage;
        image.AutoOrient(Ct);
        Assert.Same(storage, image.Frames[0].Storage);
        Assert.Same(exif, image.Metadata.ExifProfile);
        AssertFrame(image.Frames[0], Source3X2);
    }

    [Fact]
    public void RotateNoneDoesNothing()
    {
        using var image = Build<Gray8>(Source3X2);
        var storage = image.Frames[0].Storage;
        image.Rotate(RotateMode.None, Ct);
        Assert.Same(storage, image.Frames[0].Storage);
        AssertFrame(image.Frames[0], Source3X2);
    }

    [Fact]
    public void TallAndWideImagesRotateAcrossTileBoundaries()
    {
        // 37 x 70 crosses the 32-pixel tiles of the transposing kernel in both directions; segmented, padded storage
        const int Width = 37;
        const int Height = 70;
        var layout = new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 3 * 160 };
        using var image = new Image<Gray16>(ImageConfiguration.Default, new Size(Width, Height), scope: null, layout);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                image.Frames[0][x, y] = new Gray16((ushort)((y * 1000) + x));
            }
        }

        Assert.True(image.Frames[0].Storage.SlabCount > 1);
        image.Rotate(RotateMode.Rotate90, Ct);
        Assert.Equal(new Size(Height, Width), image.Size);
        Assert.True(image.Frames[0].Storage.SlabCount > 1, "The replacement keeps the storage layout of the image");
        for (var y = 0; y < Width; y++)
        {
            for (var x = 0; x < Height; x++)
            {
                // Rotated 90 degrees clockwise: the left column, read bottom-up, becomes the top row
                Assert.Equal((ushort)(((Height - 1 - x) * 1000) + y), image.Frames[0][x, y].Value);
            }
        }

        image.Rotate(RotateMode.Rotate270, Ct);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                Assert.Equal((ushort)((y * 1000) + x), image.Frames[0][x, y].Value);
            }
        }
    }

    [Fact]
    public void RotationRespectsCanvasLimits()
    {
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 4, MaxHeight = 8 } };
        using var image = new Image<Rgba32>(4, 6, configuration);
        var exception = Assert.Throws<ImageResourceLimitException>(() => image.Rotate(RotateMode.Rotate90, Ct));
        Assert.Equal(ImageResourceLimitKind.Width, exception.Kind);
        Assert.Equal(new Size(4, 6), image.Size);
        image.Rotate(RotateMode.Rotate180, Ct);
        Assert.Equal(new Size(4, 6), image.Size);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Crop
    // -----------------------------------------------------------------------------------------------------------------

    // 4 x 3 source with corner markers:
    //    1  2  3  4
    //    5  6  7  8
    //    9 10 11 12
    private static readonly int[][] Source4X3 = [[1, 2, 3, 4], [5, 6, 7, 8], [9, 10, 11, 12]];

    public static TheoryData<int, int, int, int, int[][]> CropCases => new()
    {
        { 0, 0, 1, 1, [[1]] },                       // top-left corner pixel
        { 3, 0, 1, 1, [[4]] },                       // top-right corner pixel
        { 0, 2, 1, 1, [[9]] },                       // bottom-left corner pixel
        { 3, 2, 1, 1, [[12]] },                      // bottom-right corner pixel
        { 1, 1, 3, 2, [[6, 7, 8], [10, 11, 12]] },   // touches the right and bottom edges
        { 0, 0, 4, 1, [[1, 2, 3, 4]] },              // full first row
        { 2, 0, 1, 3, [[3], [7], [11]] },            // full third column
        { 1, 0, 2, 3, [[2, 3], [6, 7], [10, 11]] },  // full height
    };

    [Theory]
    [MemberData(nameof(CropCases))]
    public void CropKeepsExactlyTheRectangle(int x, int y, int width, int height, int[][] expected)
    {
        using var image = Build<Rgba64>(Source4X3, Source4X3.Select(row => row.Select(id => id + 20).ToArray()).ToArray());
        image.SetPosterFrame(image.Frames[1]);
        image.Crop(new Rectangle(x, y, width, height), Ct);
        Assert.Equal(new Size(width, height), image.Size);
        AssertFrame(image.Frames[0], expected);
        AssertFrame(image.Frames[1], expected, idOffset: 20);
        AssertFrame(image.PosterFrame!, expected, idOffset: 20);
    }

    [Theory]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, -1, 2, 2)]
    [InlineData(3, 0, 2, 1)]  // one column past the right edge
    [InlineData(0, 2, 1, 2)]  // one row past the bottom edge
    [InlineData(0, 0, 5, 3)]
    [InlineData(0, 0, 0, 2)]  // empty
    [InlineData(1, 1, 2, 0)]  // empty
    [InlineData(4, 3, 1, 1)]  // outside
    public void CropRejectsRectanglesOutsideTheCanvasWithoutClamping(int x, int y, int width, int height)
    {
        using var image = Build<Rgba32>(Source4X3);
        var storage = image.Frames[0].Storage;
        Assert.Throws<ArgumentOutOfRangeException>("rectangle", () => image.Crop(new Rectangle(x, y, width, height), Ct));
        Assert.Equal(new Size(4, 3), image.Size);
        Assert.Same(storage, image.Frames[0].Storage);
        AssertFrame(image.Frames[0], Source4X3);
    }

    [Fact]
    public void CropToTheWholeCanvasKeepsTheStorage()
    {
        using var image = Build<Rgb24>(Source4X3);
        var storage = image.Frames[0].Storage;
        image.Crop(new Rectangle(0, 0, 4, 3), Ct);
        Assert.Same(storage, image.Frames[0].Storage);
        AssertFrame(image.Frames[0], Source4X3);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Multi-frame images and posters
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WholeImageGeometryTransformsEveryFrameAndThePosterAndKeepsFrameIdentity()
    {
        using var image = Build<Rgba64>(Source3X2, Offset(Source3X2, 10), Offset(Source3X2, 20));
        image.SetPosterFrame(image.Frames[2]);
        image.PosterFrame![0, 0] = Pixel<Rgba64>(30 + 1);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 7);
        image.Animation = new AnimationMetadata { TotalPlays = 3 };
        var frames = new[] { image.Frames[0], image.Frames[1], image.Frames[2] };
        var poster = image.PosterFrame;

        image.Rotate(RotateMode.Rotate90, Ct);

        Assert.Equal(new Size(2, 3), image.Size);
        Assert.Equal(3, image.Frames.Count);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frames[i], image.Frames[i]);
            Assert.Equal(new Size(2, 3), frames[i].Size);
            AssertFrame(frames[i], Rotated90, idOffset: 10 * i);
        }

        Assert.Same(poster, image.PosterFrame);
        Assert.Equal(Pixel<Rgba64>(31), poster[1, 0]); // the marked top-left pixel A is now at the top-right
        Assert.Equal(new FrameDuration(1, 7), frames[1].Metadata.Duration);
        Assert.Equal(3, image.Animation!.TotalPlays);

        // Borrowed references keep working on the new storage, and structural edits still apply to the same frames
        image.MoveFrame(0, 2);
        Assert.Same(frames[0], image.Frames[2]);
        image.Crop(new Rectangle(0, 1, 2, 1), Ct);
        AssertFrame(frames[0], [[5, 2]]);
        AssertFrame(frames[1], [[15, 12]]);
        AssertFrame(image.Frames[0], [[15, 12]]);
    }

    [Fact]
    public void GeometryReleasesTheOriginalStorageAndChargesOldAndNewBuffersTogether()
    {
        using var image = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        var scope = image.Owner.Scope;
        var before = scope.GetDiagnostics();
        Assert.Equal(2, before.LiveAllocations);

        image.Rotate(RotateMode.Rotate90, Ct);

        var after = scope.GetDiagnostics();
        Assert.Equal(2, after.LiveAllocations);
        Assert.Equal(2, image.Owner.LiveStorageCount);
        Assert.Equal(0, after.ReservedBytes);
        Assert.Equal(before.LiveBytes, after.LiveBytes); // 3x2 and 2x3 have the same capacity
        Assert.True(after.PeakLiveBytes >= 2 * before.LiveBytes, "The replacements are budgeted while the originals are live");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Failure injection: nothing is changed before the commit
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)] // the poster replacement
    public void AllocationFailureBeforeCommitLeavesTheImageUnchanged(int failingRental)
    {
        var pool = new SlabPool();
        using var image = BuildWithPool(pool, ImageConfiguration.Default);
        var snapshot = ImageState.Capture(image);
        var rentals = 0;
        pool.RentFailureInjector = _ => ++rentals == failingRental ? new InjectedAllocationFailureException() : null;

        Assert.Throws<InjectedAllocationFailureException>(() => image.Rotate(RotateMode.Rotate90, Ct));

        pool.RentFailureInjector = null;
        snapshot.AssertUnchanged(image);
        image.Rotate(RotateMode.Rotate90, Ct); // still usable
        AssertFrame(image.Frames[0], Rotated90);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CancellationBeforeCommitLeavesTheImageUnchanged(int cancelAtRental)
    {
        var pool = new SlabPool();
        using var image = BuildWithPool(pool, ImageConfiguration.Default);
        var snapshot = ImageState.Capture(image);
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

        Assert.ThrowsAny<OperationCanceledException>(() => image.AutoOrient(cancellation.Token));

        pool.RentFailureInjector = null;
        snapshot.AssertUnchanged(image);
    }

    [Fact]
    public void AlreadyCanceledTokenLeavesTheImageUnchanged()
    {
        using var image = BuildWithPool(new SlabPool(), ImageConfiguration.Default);
        var snapshot = ImageState.Capture(image);
        var canceled = new CancellationToken(canceled: true);
        Assert.ThrowsAny<OperationCanceledException>(() => image.Crop(new Rectangle(1, 0, 2, 2), canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Rotate(RotateMode.Rotate270, canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.AutoOrient(canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Flip(FlipMode.Horizontal, canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Grayscale(canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Frames[0].Flip(FlipMode.Vertical, canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Frames[0].Grayscale(canceled));
        snapshot.AssertUnchanged(image);
    }

    [Fact]
    public void InsufficientBudgetForOldAndNewBuffersFailsBeforeCommit()
    {
        // Measure the live bytes of the image, then allow exactly twice that amount (old + new), or one byte less
        long live;
        using (var probe = BuildWithPool(new SlabPool(), ImageConfiguration.Default))
        {
            live = probe.Owner.Scope.LiveBytes;
        }

        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = (2 * live) - 1 } };
        using (var image = BuildWithPool(new SlabPool(), tight))
        {
            var snapshot = ImageState.Capture(image);
            var exception = Assert.Throws<ImageResourceLimitException>(() => image.Rotate(RotateMode.Rotate180, Ct));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            snapshot.AssertUnchanged(image);
            Assert.Equal(live, image.Owner.Scope.LiveBytes);
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 2 * live } };
        using (var image = BuildWithPool(new SlabPool(), exact))
        {
            image.Rotate(RotateMode.Rotate180, Ct);
            AssertFrame(image.Frames[0], Rotated180);
            Assert.Equal(live, image.Owner.Scope.LiveBytes);
        }
    }

    [Fact]
    public void ActiveLeaseRejectsEveryOperation()
    {
        using var image = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        var snapshot = ImageState.Capture(image);
        image.Frames[1].ProcessPixelRows(image, static (_, image) =>
        {
            Assert.Throws<InvalidOperationException>(() => image.Crop(new Rectangle(0, 0, 1, 1), Ct));
            Assert.Throws<InvalidOperationException>(() => image.Rotate(RotateMode.Rotate90, Ct));
            Assert.Throws<InvalidOperationException>(() => image.AutoOrient(Ct));
            Assert.Throws<InvalidOperationException>(() => image.Flip(FlipMode.Vertical, Ct));
            Assert.Throws<InvalidOperationException>(() => image.Grayscale(Ct));
            Assert.Throws<InvalidOperationException>(() => image.Frames[1].Flip(FlipMode.Horizontal, Ct));
            Assert.Throws<InvalidOperationException>(() => image.Frames[1].Grayscale(Ct));
            Assert.Throws<InvalidOperationException>(() => image.Convolve(Sharpen, Ct));
            Assert.Throws<InvalidOperationException>(() => image.Frames[1].Convolve(Sharpen, Ct));
        });

        snapshot.AssertUnchanged(image);

        // A lease on another frame does not block a frame-level operation
        image.Frames[1].ProcessPixelRows(image, static (_, image) => image.Frames[0].Flip(FlipMode.Horizontal, Ct));
        AssertFrame(image.Frames[0], FlippedHorizontally);
    }

    [Fact]
    public void DisposedImagesAndDetachedFramesAreRejected()
    {
        var image = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        var removed = image.Frames[1];
        image.RemoveFrame(1);
        Assert.Throws<ObjectDisposedException>(() => removed.Flip(FlipMode.Horizontal, Ct));
        Assert.Throws<ObjectDisposedException>(() => removed.Grayscale(Ct));
        Assert.Throws<ObjectDisposedException>(() => removed.Convolve(Sharpen, Ct));

        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => image.Crop(new Rectangle(0, 0, 1, 1), Ct));
        Assert.Throws<ObjectDisposedException>(() => image.Rotate(RotateMode.Rotate90, Ct));
        Assert.Throws<ObjectDisposedException>(() => image.AutoOrient(Ct));
        Assert.Throws<ObjectDisposedException>(() => image.Flip(FlipMode.Horizontal, Ct));
        Assert.Throws<ObjectDisposedException>(() => image.Grayscale(Ct));
        Assert.Throws<ObjectDisposedException>(() => image.Convolve(Sharpen, Ct));
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        using var image = Build<Rgba32>(Source3X2);
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Crop(new Rectangle(0, 0, 1, 1), Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Rotate(RotateMode.Rotate90, Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).AutoOrient(Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Flip(FlipMode.Vertical, Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Grayscale(Ct));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).Flip(FlipMode.Vertical, Ct));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).Grayscale(Ct));
        Assert.Throws<ArgumentOutOfRangeException>("mode", () => image.Rotate((RotateMode)45, Ct));
        Assert.Throws<ArgumentOutOfRangeException>("mode", () => image.Flip((FlipMode)2, Ct));
        Assert.Throws<ArgumentOutOfRangeException>("mode", () => image.Frames[0].Flip((FlipMode)(-1), Ct));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Pixel-only operations
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ImageFlipAppliesToEveryFrameAndThePosterInPlace()
    {
        using var image = Build<Rgba64>(Source3X2, Offset(Source3X2, 10));
        image.SetPosterFrame(image.Frames[1]);
        var storages = new[] { image.Frames[0].Storage, image.Frames[1].Storage, image.PosterFrame!.Storage };

        image.Flip(FlipMode.Vertical, Ct);
        AssertFrame(image.Frames[0], FlippedVertically);
        AssertFrame(image.Frames[1], FlippedVertically, idOffset: 10);
        AssertFrame(image.PosterFrame, FlippedVertically, idOffset: 10);
        Assert.Same(storages[0], image.Frames[0].Storage);
        Assert.Same(storages[2], image.PosterFrame.Storage);

        image.Frames[1].Flip(FlipMode.Horizontal, Ct);
        AssertFrame(image.Frames[0], FlippedVertically);
        AssertFrame(image.Frames[1], Rotated180, idOffset: 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void VerticalFlipHandlesOddAndEvenHeights(int height)
    {
        var rows = Enumerable.Range(0, height).Select(y => new[] { (y * 2) + 1, (y * 2) + 2 }).ToArray();
        using var image = Build<Gray8>(rows);
        image.Flip(FlipMode.Vertical, Ct);
        AssertFrame(image.Frames[0], [.. rows.Reverse()]);
    }

    [Fact]
    public void VerticalFlipSwapsRowsLongerThanTheSwapBuffer()
    {
        // 50 Rgba64 pixels = 400 bytes per row, more than the 256-byte swap chunk
        using var image = new Image<Rgba64>(50, 3);
        for (var x = 0; x < 50; x++)
        {
            image.Frames[0][x, 0] = new Rgba64((ushort)x, 1, 2, 3);
            image.Frames[0][x, 2] = new Rgba64((ushort)(1000 + x), 4, 5, 6);
        }

        image.Flip(FlipMode.Vertical, Ct);
        for (var x = 0; x < 50; x++)
        {
            Assert.Equal(new Rgba64((ushort)(1000 + x), 4, 5, 6), image.Frames[0][x, 0]);
            Assert.Equal(default, image.Frames[0][x, 1]);
            Assert.Equal(new Rgba64((ushort)x, 1, 2, 3), image.Frames[0][x, 2]);
        }
    }

    [Fact]
    public void Grayscale8BitUsesRec709WithTiesUpwardAndKeepsAlpha()
    {
        // Hand-computed from Y = 0.2126 R + 0.7152 G + 0.0722 B, rounded to nearest, ties upward
        (Rgba32 Source, byte Expected)[] cases =
        [
            (new Rgba32(255, 0, 0, 10), 54),        // 54.213
            (new Rgba32(0, 255, 0, 20), 182),       // 182.376
            (new Rgba32(0, 0, 255, 30), 18),        // 18.411
            (new Rgba32(255, 255, 255, 255), 255),  // 255 exactly (the coefficients sum to 1)
            (new Rgba32(0, 0, 0, 0), 0),
            (new Rgba32(3, 234, 7, 40), 169),       // 0.6378 + 167.3568 + 0.5054 = 168.5 exactly: tie, rounded up
            (new Rgba32(3, 234, 6, 50), 168),       // 168.4278
            (new Rgba32(4, 234, 7, 60), 169),       // 168.7126
            (new Rgba32(100, 150, 200, 128), 143),  // 21.26 + 107.28 + 14.44 = 142.98
        ];

        using var image = new Image<Rgba32>(cases.Length, 1);
        for (var i = 0; i < cases.Length; i++)
        {
            image.Frames[0][i, 0] = cases[i].Source;
        }

        image.Grayscale(Ct);
        for (var i = 0; i < cases.Length; i++)
        {
            var expected = cases[i].Expected;
            Assert.Equal(new Rgba32(expected, expected, expected, cases[i].Source.A), image.Frames[0][i, 0]);
        }
    }

    [Fact]
    public void GrayscaleKeepsTheStorageTypeForEveryColorFormat()
    {
        using (var bgra = new Image<Bgra32>(1, 1, new Bgra32(3, 234, 7, 99)))
        {
            bgra.Grayscale(Ct);
            Assert.Equal(new Bgra32(169, 169, 169, 99), bgra.Frames[0][0, 0]);
            Assert.Equal(PixelFormat.Bgra32, bgra.PixelFormat);
        }

        using (var rgb = new Image<Rgb24>(2, 1))
        {
            rgb.Frames[0][0, 0] = new Rgb24(255, 0, 0);
            rgb.Frames[0][1, 0] = new Rgb24(3, 234, 7);
            rgb.Frames[0].Grayscale(Ct);
            Assert.Equal(new Rgb24(54, 54, 54), rgb.Frames[0][0, 0]);
            Assert.Equal(new Rgb24(169, 169, 169), rgb.Frames[0][1, 0]);
        }

        using (var gray = new Image<Gray8>(1, 1, new Gray8(77)))
        {
            gray.Grayscale(Ct);
            Assert.Equal(new Gray8(77), gray.Frames[0][0, 0]);
        }

        using (var gray16 = new Image<Gray16>(1, 1, new Gray16(0x1235)))
        {
            gray16.Grayscale(Ct);
            Assert.Equal(new Gray16(0x1235), gray16.Frames[0][0, 0]);
        }
    }

    [Fact]
    public void Grayscale16BitKeepsFullPrecisionAndAlpha()
    {
        (Rgba64 Source, ushort Expected)[] cases =
        [
            (new Rgba64(65535, 0, 0, 1), 13933),           // 13932.741
            (new Rgba64(65535, 65535, 0, 0x8001), 60803),  // 0.9278 * 65535 = 60803.373
            (new Rgba64(1000, 50000, 30000, 65534), 38139), // 212.6 + 35760 + 2166 = 38138.6
            (new Rgba64(771, 60138, 1799, 0x0102), 43305), // 163.9146 + 43010.6976 + 129.8878 = 43304.5 exactly: tie, rounded up
            (new Rgba64(65535, 65535, 65535, 65535), 65535),
            (new Rgba64(1, 1, 1, 7), 1),                   // a value an 8-bit route would lose
        ];

        using var image = new Image<Rgba64>(1, cases.Length);
        for (var i = 0; i < cases.Length; i++)
        {
            image.Frames[0][0, i] = cases[i].Source;
        }

        image.Grayscale(Ct);
        for (var i = 0; i < cases.Length; i++)
        {
            var expected = cases[i].Expected;
            Assert.Equal(new Rgba64(expected, expected, expected, cases[i].Source.A), image.Frames[0][0, i]);
        }
    }

    [Fact]
    public void GrayscaleAppliesToEveryFrameAndThePoster()
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 1));
        image.AppendFrame().ProcessPixelRows(static pixels => pixels.GetRowSpan(0)[0] = new Rgba32(0, 255, 0, 2));
        image.SetPosterFrame(image.Frames[0]).ProcessPixelRows(static pixels => pixels.GetRowSpan(0)[0] = new Rgba32(0, 0, 255, 3));
        image.Grayscale(Ct);
        Assert.Equal(new Rgba32(54, 54, 54, 1), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(182, 182, 182, 2), image.Frames[1][0, 0]);
        Assert.Equal(new Rgba32(18, 18, 18, 3), image.PosterFrame![0, 0]);
    }

    [Fact]
    public void GrayscaleKeepsACompatibleProfileAndRejectsAnIncompatibleOne()
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        var rgb = new IccProfile(MetadataBlob.FromOwnedArray(CreateIccHeader("RGB ")));
        image.Metadata.IccProfile = rgb;
        image.Grayscale(Ct);
        Assert.Same(rgb, image.Metadata.IccProfile);
        Assert.Equal(new Rgba32(54, 54, 54, 255), image.Frames[0][0, 0]);

        image.Frames[0][0, 0] = new Rgba32(255, 0, 0, 255);
        image.Metadata.IccProfile = new IccProfile(MetadataBlob.FromOwnedArray(CreateIccHeader("GRAY")));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Grayscale(Ct));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].Grayscale(Ct));
        Assert.Equal(new Rgba32(255, 0, 0, 255), image.Frames[0][0, 0]);
    }

    [Fact]
    public void CanceledPixelOperationsLeaveAStructurallyValidImage()
    {
        // 200 rows: the kernels observe cancellation between row bands, so a later cancellation may leave a partial result
        using var image = new Image<Rgba32>(2, 200, new Rgba32(255, 0, 0, 255));
        image.AppendFrame(image.Frames[0]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => image.Flip(FlipMode.Vertical, cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => image.Grayscale(cancellation.Token));
        Assert.Equal(new Size(2, 200), image.Size);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        image.Grayscale(Ct);
        Assert.Equal(new Rgba32(54, 54, 54, 255), image.Frames[1][1, 199]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Color profile conversion. Expected samples are literals computed outside the library (see IccColorTransformTests)
    // or come from the independent reference of the test harness.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertColorProfileConvertsUntaggedPixelsFromSrgbAndLabelsTheImage()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());

        // sRGB red is (234, 51, 35) in Display P3; alpha is kept, the hidden color of a transparent pixel is converted too
        using var rgba = new Image<Rgba32>(2, 1, new Rgba32(255, 0, 0, 128));
        rgba.Frames[0][1, 0] = new Rgba32(200, 100, 50, 0);
        var frame = rgba.Frames[0];
        rgba.ConvertColorProfile(displayP3, cancellationToken: Ct);
        Assert.Same(frame, rgba.Frames[0]);
        Assert.Equal(new Rgba32(234, 51, 35, 128), rgba.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(187, 105, 62, 0), rgba.Frames[0][1, 0]);
        Assert.Same(displayP3, rgba.Metadata.IccProfile);
        Assert.Equal(ColorTransferFunction.Srgb, rgba.Metadata.TransferFunction);

        // The same colors in blue-green-red order, and without alpha
        using var bgra = new Image<Bgra32>(1, 1, new Bgra32(255, 0, 0, 7));
        bgra.ConvertColorProfile(displayP3, cancellationToken: Ct);
        Assert.Equal(new Bgra32(234, 51, 35, 7), bgra.Frames[0][0, 0]);

        using var rgb = new Image<Rgb24>(1, 1, new Rgb24(0, 255, 0));
        rgb.ConvertColorProfile(displayP3, cancellationToken: Ct);
        Assert.Equal(new Rgb24(117, 251, 76), rgb.Frames[0][0, 0]);

        // 16-bit samples are converted with 16-bit precision
        using var rgba64 = new Image<Rgba64>(1, 1, new Rgba64(12345, 23456, 34567, 4660));
        rgba64.ConvertColorProfile(displayP3, cancellationToken: Ct);
        Assert.Equal(new Rgba64(15033, 23185, 33666, 4660), rgba64.Frames[0][0, 0]);

        // Untagged grayscale pixels are sGray
        var grayGamma = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
        using var gray8 = new Image<Gray8>(1, 1, new Gray8(64));
        gray8.ConvertColorProfile(grayGamma, cancellationToken: Ct);
        Assert.Equal(new Gray8(66), gray8.Frames[0][0, 0]);
        Assert.Same(grayGamma, gray8.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertColorProfileUsesTheAttachedProfileForEveryPixelFormat()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var grayGamma = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
        CheckColor<Rgba32>(4, static (Rgba32 pixel) => [pixel.R, pixel.G, pixel.B, pixel.A]);
        CheckColor<Bgra32>(4, static (Bgra32 pixel) => [pixel.R, pixel.G, pixel.B, pixel.A]);
        CheckColor<Rgb24>(3, static (Rgb24 pixel) => [pixel.R, pixel.G, pixel.B]);
        CheckColor<Rgba64>(4, static (Rgba64 pixel) => [pixel.R, pixel.G, pixel.B, pixel.A]);
        CheckGray<Gray8>(static (Gray8 pixel) => pixel.Value);
        CheckGray<Gray16>(static (Gray16 pixel) => pixel.Value);

        void CheckColor<TPixel>(int channels, Func<TPixel, int[]> read)
            where TPixel : unmanaged
        {
            using var image = Build<TPixel>(Source4X3, Offset(Source4X3, 20));
            image.SetPosterFrame(image.Frames[1]);
            image.Metadata.IccProfile = displayP3;
            var before = AllFrames(image).Select(frame => ReadPixels(frame, read)).ToArray();
            image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
            Assert.Same(IccProfile.Srgb, image.Metadata.IccProfile);

            var reference = ReferenceIccTransform.Create(displayP3.Data.Span, IccProfile.Srgb.Data.Span);
            var after = AllFrames(image).Select(frame => ReadPixels(frame, read)).ToArray();
            Assert.HasCount(3, after);
            for (var i = 0; i < after.Length; i++)
            {
                var source = before[i].SelectMany(pixel => pixel.Take(3)).ToArray();
                var actual = after[i].SelectMany(pixel => pixel.Take(3)).ToArray();
                Assert.Empty(typeof(TPixel) == typeof(Rgba64)
                    ? reference.Compare(source.Select(value => (ushort)value).ToArray(), actual.Select(value => (ushort)value).ToArray())
                    : reference.Compare(source.Select(value => (byte)value).ToArray(), actual.Select(value => (byte)value).ToArray()));
                Assert.NotEqual(source, actual);
                if (channels == 4)
                {
                    Assert.Equal(before[i].Select(pixel => pixel[3]).ToArray(), after[i].Select(pixel => pixel[3]).ToArray());
                }
            }
        }

        void CheckGray<TPixel>(Func<TPixel, int> read)
            where TPixel : unmanaged
        {
            using var image = Build<TPixel>(Source4X3, Offset(Source4X3, 20));
            image.Metadata.IccProfile = grayGamma;
            var before = AllFrames(image).Select(frame => ReadPixels(frame, pixel => new[] { read(pixel) })).ToArray();
            image.ConvertColorProfile(IccProfile.SrgbGray, cancellationToken: Ct);
            Assert.Same(IccProfile.SrgbGray, image.Metadata.IccProfile);

            var reference = ReferenceIccTransform.Create(grayGamma.Data.Span, IccProfile.SrgbGray.Data.Span);
            var after = AllFrames(image).Select(frame => ReadPixels(frame, pixel => new[] { read(pixel) })).ToArray();
            for (var i = 0; i < after.Length; i++)
            {
                var source = before[i].Select(pixel => pixel[0]).ToArray();
                var actual = after[i].Select(pixel => pixel[0]).ToArray();
                Assert.Empty(typeof(TPixel) == typeof(Gray16)
                    ? reference.Compare(source.Select(value => (ushort)value).ToArray(), actual.Select(value => (ushort)value).ToArray())
                    : reference.Compare(source.Select(value => (byte)value).ToArray(), actual.Select(value => (byte)value).ToArray()));
                Assert.NotEqual(source, actual);
            }
        }

        static IEnumerable<ImageFrame<TPixel>> AllFrames<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged
            => image.PosterFrame is null ? image.Frames : image.Frames.Append(image.PosterFrame);

        static int[][] ReadPixels<TPixel>(ImageFrame<TPixel> frame, Func<TPixel, int[]> read)
            where TPixel : unmanaged
        {
            var pixels = new List<int[]>();
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    pixels.Add(read(frame[x, y]));
                }
            }

            return [.. pixels];
        }
    }

    [Fact]
    public void ConvertColorProfileBetweenIdenticalProfilesOnlyLabelsTheImage()
    {
        // An untagged image is already sRGB: converting it to sRGB keeps the buffers and every sample
        using var image = Build<Rgba32>(Source3X2);
        image.Metadata.ExifProfile = CreateExif(orientation: 1, width: 3, height: 2);
        var storage = image.Frames[0].Storage;
        image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
        Assert.Same(storage, image.Frames[0].Storage);
        AssertFrame(image.Frames[0], Source3X2);
        Assert.Same(IccProfile.Srgb, image.Metadata.IccProfile);
        Assert.True(ExifTiff.HasThumbnail(image.Metadata.ExifProfile!.Data.Span));

        // Same bytes in another instance: still nothing to convert, and the new instance is the label
        var copy = new IccProfile(new MetadataBlob(IccProfile.Srgb.Data.Span));
        image.ConvertColorProfile(copy, cancellationToken: Ct);
        Assert.Same(storage, image.Frames[0].Storage);
        Assert.Same(copy, image.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertColorProfileConvertsLinearLightSamplesAndResetsTheLabel()
    {
        // Linear light with the sRGB primaries: only the sRGB encoding is applied (32768 / 65535 encodes to 48192 / 65535)
        using var image = new Image<Rgba64>(1, 1, new Rgba64(65535, 32768, 0, 999));
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
        Assert.Equal(new Rgba64(65535, 48192, 0, 999), image.Frames[0][0, 0]);
        Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
        Assert.Same(IccProfile.Srgb, image.Metadata.IccProfile);

        using var gray = new Image<Gray8>(3, 1, new Gray8(128));
        gray.Frames[0][1, 0] = new Gray8(1);
        gray.Frames[0][2, 0] = new Gray8(64);
        gray.Metadata.TransferFunction = ColorTransferFunction.Linear;
        gray.ConvertColorProfile(IccProfile.SrgbGray, cancellationToken: Ct);
        Assert.Equal([new Gray8(188), new Gray8(13), new Gray8(137)], new[] { gray.Frames[0][0, 0], gray.Frames[0][1, 0], gray.Frames[0][2, 0] });
        Assert.Equal(ColorTransferFunction.Srgb, gray.Metadata.TransferFunction);

        // Linear sRGB (255, 128, 0) is (245, 191, 65) in Display P3
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        using var rgb = new Image<Rgb24>(1, 1, new Rgb24(255, 128, 0));
        rgb.Metadata.TransferFunction = ColorTransferFunction.Linear;
        rgb.ConvertColorProfile(displayP3, cancellationToken: Ct);
        Assert.Equal(new Rgb24(245, 191, 65), rgb.Frames[0][0, 0]);

        // A profile together with the linear label is ambiguous: nothing is guessed
        using var ambiguous = new Image<Rgb24>(1, 1, new Rgb24(1, 2, 3));
        ambiguous.Metadata.IccProfile = displayP3;
        ambiguous.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => ambiguous.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct));
        Assert.Equal("ICC profile on linear-light samples", exception.Feature);
        Assert.Equal(new Rgb24(1, 2, 3), ambiguous.Frames[0][0, 0]);
        Assert.Same(displayP3, ambiguous.Metadata.IccProfile);
        Assert.Equal(ColorTransferFunction.Linear, ambiguous.Metadata.TransferFunction);
    }

    [Fact]
    public void ConvertColorProfileRejectsProfilesThatCannotLabelThePixelsAndInvalidProfiles()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var cmyk = new IccProfile(MetadataBlob.FromOwnedArray(CreateIccHeader("CMYK")));
        var malformed = new IccProfile(MetadataBlob.FromOwnedArray([1, 2, 3]));
        var unsupported = new IccProfile(new MetadataBlob(IccTestProfiles.BuildBytes("RGB ", "XYZ ", [], static data => "link"u8.CopyTo(data.AsSpan(12)))));

        using var image = BuildWithPool(new SlabPool(), ImageConfiguration.Default);
        image.Metadata.IccProfile = displayP3;
        var snapshot = ImageState.Capture(image);

        // Destination: a grayscale or CMYK profile on color pixels, a profile too short to declare a color space, an
        // unsupported profile class, a profile without the tags of its model
        Assert.Equal("Incompatible color profile", Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(IccProfile.SrgbGray, cancellationToken: Ct)).Feature);
        Assert.Equal("Incompatible color profile", Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(cmyk, cancellationToken: Ct)).Feature);
        Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(malformed, cancellationToken: Ct));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(unsupported, cancellationToken: Ct));
        Assert.Throws<InvalidImageContentException>(() => image.ConvertColorProfile(IccTestProfiles.Build("RGB ", "XYZ "), cancellationToken: Ct));
        snapshot.AssertUnchanged(image);
        Assert.Same(displayP3, image.Metadata.IccProfile);

        // Source: the attached profile must label the pixels and be usable
        image.Metadata.IccProfile = IccProfile.SrgbGray;
        Assert.Equal("Incompatible color profile", Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct)).Feature);
        var withoutTags = IccTestProfiles.Build("RGB ", "XYZ ");
        image.Metadata.IccProfile = withoutTags;
        Assert.Throws<InvalidImageContentException>(() => image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct));
        snapshot.AssertUnchanged(image);
        Assert.Same(withoutTags, image.Metadata.IccProfile);

        using var gray = new Image<Gray8>(1, 1, new Gray8(5));
        Assert.Equal("Incompatible color profile", Assert.Throws<UnsupportedImageFeatureException>(() => gray.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct)).Feature);
        Assert.Null(gray.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertColorProfileIsTransactional()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());

        // Cancellation before or during the conversion: pixels, buffers and label are unchanged
        var pool = new SlabPool();
        using (var image = BuildWithPool(pool, ImageConfiguration.Default))
        {
            var snapshot = ImageState.Capture(image);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => image.ConvertColorProfile(displayP3, cancellationToken: cancellation.Token));
            snapshot.AssertUnchanged(image);
            Assert.Null(image.Metadata.IccProfile);

            // An allocation failure in the middle of the frames
            var rents = 0;
            pool.RentFailureInjector = _ => ++rents == 2 ? new InjectedAllocationFailureException() : null;
            Assert.Throws<InjectedAllocationFailureException>(() => image.ConvertColorProfile(displayP3, cancellationToken: Ct));
            pool.RentFailureInjector = null;
            snapshot.AssertUnchanged(image);
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);

            // An active lease
            image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.ConvertColorProfile(displayP3)));
            snapshot.AssertUnchanged(image);
        }

        // The old and new buffers are budgeted together
        long live;
        using (var probe = BuildWithPool(new SlabPool(), ImageConfiguration.Default))
        {
            live = probe.Owner.Scope.LiveBytes;
        }

        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = (2 * live) - 1 } };
        using (var image = BuildWithPool(new SlabPool(), tight))
        {
            var snapshot = ImageState.Capture(image);
            var exception = Assert.Throws<ImageResourceLimitException>(() => image.ConvertColorProfile(displayP3, cancellationToken: Ct));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            snapshot.AssertUnchanged(image);
            Assert.Null(image.Metadata.IccProfile);
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 2 * live } };
        using (var image = BuildWithPool(new SlabPool(), exact))
        {
            var frames = ((IEnumerable<ImageFrame<Rgba32>>)image.Frames).ToArray();
            var poster = image.PosterFrame;
            image.ConvertColorProfile(displayP3, cancellationToken: Ct);
            Assert.Same(displayP3, image.Metadata.IccProfile);
            Assert.Equal(frames, ((IEnumerable<ImageFrame<Rgba32>>)image.Frames).ToArray());
            Assert.Same(poster, image.PosterFrame);
            Assert.Equal(live, image.Owner.Scope.LiveBytes);
            Assert.Equal(new FrameDuration(1, 3), image.Frames[0].Metadata.Duration);

            // The pixels changed: the EXIF thumbnail is removed, dimensions and orientation are kept
            var exif = image.Metadata.ExifProfile!.Data.Span;
            Assert.False(ExifTiff.HasThumbnail(exif));
            Assert.Equal((3u, 2u), ReadIfd0Dimensions(exif));
            Assert.Equal(ExifOrientation.RightBottom, image.Metadata.Orientation);
        }
    }

    [Fact]
    public void ConvertColorProfileValidatesItsArgumentsAndState()
    {
        Assert.Throws<ArgumentNullException>("image", () => ImageProcessingExtensions.ConvertColorProfile(null!, IccProfile.Srgb));
        using var image = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 4));
        Assert.Throws<ArgumentNullException>("destinationProfile", () => image.ConvertColorProfile(null!));

        // Options select the conversion: the absolute colorimetric intent is accepted, an undefined one cannot be built
        image.ConvertColorProfile(IccProfile.Srgb, new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual, BlackPointCompensation = false }, Ct);
        Assert.Equal(new Rgba32(1, 2, 3, 4), image.Frames[0][0, 0]);

        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => image.ConvertColorProfile(IccProfile.Srgb));
    }

    [Fact]
    public void ConvertColorProfileToSrgbEnablesLinearLightProcessing()
    {
        // Display P3 pixels are rejected by the linear-light working space until they are converted to sRGB
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var linear = new ResizeOptions(1, 1) { Mode = ResizeMode.Stretch, WorkingSpace = ResizeWorkingSpace.LinearSrgb };
        using var image = new Image<Rgba64>(2, 2, new Rgba64(40000, 40000, 40000, 65535));
        image.Metadata.IccProfile = displayP3;
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Resize(linear, Ct));
        image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
        image.Resize(linear, Ct);
        Assert.Equal(new Rgba64(40000, 40000, 40000, 65535), image.Frames[0][0, 0]);
        Assert.Same(IccProfile.Srgb, image.Metadata.IccProfile);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Metadata reconciliation
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void RotationKeepsTheTypedOrientationAndReconcilesDimensionsAndThumbnail()
    {
        using var image = Build<Rgba32>(Source3X2);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.ExifProfile = CreateExif(orientation: 6, width: 3, height: 2);
        image.Rotate(RotateMode.Rotate90, Ct);

        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(exif));
        Assert.Equal((2u, 3u), ReadIfd0Dimensions(exif));
        Assert.Equal(((uint?)2, (uint?)3), ExifTiff.ReadPixelDimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));
    }

    [Fact]
    public void AutoOrientNormalizesTheOrientationTag()
    {
        using var image = Build<Rgba32>(Source3X2);
        image.Metadata.Orientation = ExifOrientation.LeftBottom;
        image.Metadata.ExifProfile = CreateExif(orientation: 8, width: 3, height: 2);
        image.AutoOrient(Ct);

        AssertFrame(image.Frames[0], Rotated270);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.Equal(ExifOrientation.TopLeft, ExifTiff.ReadOrientation(exif));
        Assert.Equal((2u, 3u), ReadIfd0Dimensions(exif));
        Assert.Equal(((uint?)2, (uint?)3), ExifTiff.ReadPixelDimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));
    }

    [Fact]
    public void AutoOrientDoesNotInventAnOrientationTag()
    {
        using var image = Build<Rgba32>(Source3X2);
        image.Metadata.Orientation = ExifOrientation.BottomRight;
        image.Metadata.ExifProfile = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: true).Ifd0(TiffBuilder.Ascii(0x0131, "software")).Build()));
        image.AutoOrient(Ct);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        Assert.False(ExifTiff.HasOrientationTag(image.Metadata.ExifProfile!.Data.Span));
    }

    [Fact]
    public void CropUpdatesDimensionTags()
    {
        using var image = Build<Rgba32>(Source4X3);
        image.Metadata.ExifProfile = CreateExif(orientation: 1, width: 4, height: 3);
        image.Crop(new Rectangle(1, 1, 3, 1), Ct);
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.Equal((3u, 1u), ReadIfd0Dimensions(exif));
        Assert.Equal(((uint?)3, (uint?)1), ExifTiff.ReadPixelDimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
    }

    [Fact]
    public void PixelOperationsRemoveTheStaleThumbnailButKeepDimensionsAndOrientation()
    {
        foreach (var operation in new Action<Image>[]
        {
            static image => image.Flip(FlipMode.Horizontal, Ct),
            static image => image.Grayscale(Ct),
            static image => image.Frames[0].Flip(FlipMode.Vertical, Ct),
            static image => image.Frames[0].Grayscale(Ct),
            static image => image.Convolve(Sharpen, Ct),
            static image => image.Frames[0].Convolve(Sharpen, Ct),
        })
        {
            using var image = Build<Rgba32>(Source3X2);
            image.Metadata.Orientation = ExifOrientation.BottomLeft;
            image.Metadata.ExifProfile = CreateExif(orientation: 4, width: 3, height: 2);
            operation(image);

            var exif = image.Metadata.ExifProfile!.Data.Span;
            Assert.False(ExifTiff.HasThumbnail(exif));
            Assert.Equal(ExifOrientation.BottomLeft, ExifTiff.ReadOrientation(exif));
            Assert.Equal((3u, 2u), ReadIfd0Dimensions(exif));
            Assert.Equal(ExifOrientation.BottomLeft, image.Metadata.Orientation);
        }
    }

    [Fact]
    public void MalformedExifIsLeftAsIs()
    {
        using var image = Build<Rgba32>(Source3X2);
        var malformed = new ExifProfile(MetadataBlob.FromOwnedArray([1, 2, 3, 4]));
        image.Metadata.ExifProfile = malformed;
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.AutoOrient(Ct);
        Assert.Same(malformed, image.Metadata.ExifProfile);
        image.Flip(FlipMode.Horizontal, Ct);
        Assert.Same(malformed, image.Metadata.ExifProfile);
    }

    [Fact]
    public void FailedGeometryKeepsTheMetadata()
    {
        var pool = new SlabPool();
        using var image = BuildWithPool(pool, ImageConfiguration.Default);
        var exif = CreateExif(orientation: 6, width: 3, height: 2);
        image.Metadata.ExifProfile = exif;
        image.Metadata.Orientation = ExifOrientation.RightTop;
        pool.RentFailureInjector = _ => new InjectedAllocationFailureException();
        Assert.Throws<InjectedAllocationFailureException>(() => image.AutoOrient(Ct));
        pool.RentFailureInjector = null;
        Assert.Same(exif, image.Metadata.ExifProfile);
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        Assert.True(ExifTiff.HasThumbnail(image.Metadata.ExifProfile.Data.Span));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------------------------

    private static void CheckRotationsAndFlips<TPixel>()
        where TPixel : unmanaged
    {
        (Action<Image> Operation, int[][] Expected)[] cases =
        [
            (static image => image.Rotate(RotateMode.Rotate90, Ct), Rotated90),
            (static image => image.Rotate(RotateMode.Rotate180, Ct), Rotated180),
            (static image => image.Rotate(RotateMode.Rotate270, Ct), Rotated270),
            (static image => image.Flip(FlipMode.Horizontal, Ct), FlippedHorizontally),
            (static image => image.Flip(FlipMode.Vertical, Ct), FlippedVertically),
            (static image => image.Frames[0].Flip(FlipMode.Horizontal, Ct), FlippedHorizontally),
            (static image => image.Frames[0].Flip(FlipMode.Vertical, Ct), FlippedVertically),
        ];

        foreach (var (operation, expected) in cases)
        {
            foreach (var segmented in new[] { false, true })
            {
                var layout = segmented ? new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 16 } : null;
                using var image = Build<TPixel>(layout, Source3X2);
                operation(image);
                AssertFrame(image.Frames[0], expected);
            }
        }

        // Four quarter turns and two flips are identities
        using var roundTrip = Build<TPixel>(Source3X2);
        for (var i = 0; i < 4; i++)
        {
            roundTrip.Rotate(RotateMode.Rotate90, Ct);
        }

        roundTrip.Flip(FlipMode.Horizontal, Ct);
        roundTrip.Flip(FlipMode.Horizontal, Ct);
        AssertFrame(roundTrip.Frames[0], Source3X2);
    }

    /// <summary>A distinct pixel for each id, exercising every channel (16-bit low bits and non-trivial alpha included).</summary>
    internal static TPixel Pixel<TPixel>(int id)
        where TPixel : unmanaged
    {
        object pixel = typeof(TPixel) switch
        {
            var t when t == typeof(Rgba32) => new Rgba32((byte)id, (byte)(id + 100), (byte)(200 - id), (byte)(255 - id)),
            var t when t == typeof(Bgra32) => new Bgra32((byte)id, (byte)(id + 100), (byte)(200 - id), (byte)(255 - id)),
            var t when t == typeof(Rgb24) => new Rgb24((byte)id, (byte)(id + 100), (byte)(200 - id)),
            var t when t == typeof(Rgba64) => new Rgba64((ushort)((0x0101 * id) + 1), (ushort)(0x1000 + (3 * id)), (ushort)(0xFFFF - (7 * id)), (ushort)(0x8000 + id)),
            var t when t == typeof(Gray8) => new Gray8((byte)(id * 3)),
            var t when t == typeof(Gray16) => new Gray16((ushort)((id * 1000) + 1)),
            _ => throw new NotSupportedException(),
        };

        return (TPixel)pixel;
    }

    private static Image<TPixel> Build<TPixel>(params int[][][] frames)
        where TPixel : unmanaged
        => Build<TPixel>(layout: null, frames);

    private static Image<TPixel> Build<TPixel>(PixelStorageLayoutOptions? layout, params int[][][] frames)
        where TPixel : unmanaged
        => Build<TPixel>(ImageConfiguration.Default, scope: null, layout, frames);

    private static Image<TPixel> Build<TPixel>(ImageConfiguration configuration, AllocationScope? scope, PixelStorageLayoutOptions? layout, params int[][][] frames)
        where TPixel : unmanaged
    {
        var height = frames[0].Length;
        var width = frames[0][0].Length;
        var image = new Image<TPixel>(configuration, new Size(width, height), scope, layout);
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    frame[x, y] = Pixel<TPixel>(frames[i][y][x]);
                }
            }
        }

        return image;
    }

    /// <summary>A two-frame 3x2 Rgba32 image with a poster, charged to a scope that uses <paramref name="pool"/>.</summary>
    private static Image<Rgba32> BuildWithPool(SlabPool pool, ImageConfiguration configuration)
    {
        var image = Build<Rgba32>(configuration, new AllocationScope(configuration.Limits, pool), layout: null, Source3X2, Offset(Source3X2, 10));
        image.SetPosterFrame(image.Frames[1]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 3);
        image.Metadata.ExifProfile = CreateExif(orientation: 7, width: 3, height: 2);
        image.Metadata.Orientation = ExifOrientation.RightBottom;
        return image;
    }

    private static int[][] Offset(int[][] ids, int offset) => [.. ids.Select(row => row.Select(id => id + offset).ToArray())];

    private static void AssertFrame<TPixel>(ImageFrame<TPixel> frame, int[][] expected, int idOffset = 0)
        where TPixel : unmanaged
    {
        Assert.Equal(new Size(expected[0].Length, expected.Length), frame.Size);
        var actual = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(actual);
        for (var y = 0; y < expected.Length; y++)
        {
            for (var x = 0; x < expected[y].Length; x++)
            {
                var pixel = Pixel<TPixel>(expected[y][x] + idOffset);
                if (!EqualityComparer<TPixel>.Default.Equals(pixel, actual[(y * frame.Width) + x]))
                    Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Pixel ({x},{y}): expected id {expected[y][x] + idOffset} ({pixel}), got {actual[(y * frame.Width) + x]}."));
            }
        }
    }

    private static ExifProfile CreateExif(ushort orientation, ushort width, ushort height)
        => new(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: false)
            .Ifd0(TiffBuilder.Short(ExifTiff.OrientationTag, orientation), TiffBuilder.Short(ExifTiff.ImageWidthTag, width), TiffBuilder.Short(ExifTiff.ImageLengthTag, height))
            .ExifIfd(TiffBuilder.Short(ExifTiff.PixelXDimensionTag, width), TiffBuilder.Long(ExifTiff.PixelYDimensionTag, height))
            .Thumbnail("STALE-THUMBNAIL"u8.ToArray())
            .Build()));

    private static (uint Width, uint Height) ReadIfd0Dimensions(ReadOnlySpan<byte> exif)
    {
        Assert.True(ExifTiff.ExifStructure.TryParse(exif, out var structure, out var error), error);
        var width = ExifTiff.ExifStructure.Find(structure.Ifd0, ExifTiff.ImageWidthTag)!.Value;
        var height = ExifTiff.ExifStructure.Find(structure.Ifd0, ExifTiff.ImageLengthTag)!.Value;
        Assert.True(structure.TryReadUnsigned(exif, width, out var w));
        Assert.True(structure.TryReadUnsigned(exif, height, out var h));
        return (w, h);
    }

    private static byte[] CreateIccHeader(string colorSpace)
    {
        var data = new byte[132];
        data[3] = 132;
        System.Text.Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
        System.Text.Encoding.ASCII.GetBytes("acsp").CopyTo(data, 36);
        return data;
    }

    /// <summary>Everything a failed transactional operation must preserve.</summary>
    private sealed class ImageState
    {
        private Size _size;
        private ImageFrame[] _frames = [];
        private PixelStorage[] _storages = [];
        private Rgba32[][] _pixels = [];
        private FrameDuration[] _durations = [];
        private ImageFrame? _poster;
        private PixelStorage? _posterStorage;
        private Rgba32[] _posterPixels = [];
        private ExifProfile? _exif;
        private ExifOrientation _orientation;
        private long _liveBytes;
        private int _liveStorages;

        public static ImageState Capture(Image<Rgba32> image)
        {
            var frames = ((IEnumerable<ImageFrame<Rgba32>>)image.Frames).ToArray();
            return new ImageState
            {
                _size = image.Size,
                _frames = frames,
                _storages = [.. frames.Select(frame => frame.Storage)],
                _pixels = [.. frames.Select(CopyPixels)],
                _durations = [.. frames.Select(frame => frame.Metadata.Duration)],
                _poster = image.PosterFrame,
                _posterStorage = image.PosterFrame?.Storage,
                _posterPixels = image.PosterFrame is null ? [] : CopyPixels(image.PosterFrame),
                _exif = image.Metadata.ExifProfile,
                _orientation = image.Metadata.Orientation,
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
                Assert.Equal(_pixels[i], CopyPixels(image.Frames[i]));
                Assert.Equal(_durations[i], image.Frames[i].Metadata.Duration);
            }

            Assert.Same(_poster, image.PosterFrame);
            if (_poster is not null)
            {
                Assert.Same(_posterStorage, image.PosterFrame!.Storage);
                Assert.Equal(_posterPixels, CopyPixels(image.PosterFrame));
            }

            Assert.Same(_exif, image.Metadata.ExifProfile);
            Assert.Equal(_orientation, image.Metadata.Orientation);
            Assert.Equal(_liveBytes, image.Owner.Scope.LiveBytes);
            Assert.Equal(_liveStorages, image.Owner.LiveStorageCount);
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
