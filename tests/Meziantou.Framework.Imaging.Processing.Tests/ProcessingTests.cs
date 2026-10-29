using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Color;
using Meziantou.Framework.Imaging.TestHarness.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

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
            case PixelFormat.La16: CheckRotationsAndFlips<La16>(); break;
            case PixelFormat.La32: CheckRotationsAndFlips<La32>(); break;
            case PixelFormat.Rgb48: CheckRotationsAndFlips<Rgb48>(); break;
            case PixelFormat.A8: CheckRotationsAndFlips<A8>(); break;
            case PixelFormat.RgbaFloat: CheckRotationsAndFlips<RgbaFloat>(); break;
            case PixelFormat.RgbaHalf: CheckRotationsAndFlips<RgbaHalf>(); break;
            case PixelFormat.Argb32: CheckRotationsAndFlips<Argb32>(); break;
            case PixelFormat.Abgr32: CheckRotationsAndFlips<Abgr32>(); break;
            case PixelFormat.Bgr565: CheckRotationsAndFlips<Bgr565>(); break;
            case PixelFormat.Rgba1010102: CheckRotationsAndFlips<Rgba1010102>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void GeometryMovesFloatingPointSamplesBitForBit(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointGeometry<RgbaFloat>(
            [
                new(BitConverter.Int32BitsToSingle(0x7FC0_1234), -0f, float.PositiveInfinity, 0.5f),
                new(BitConverter.Int32BitsToSingle(unchecked((int)0xFFC0_0042)), float.NegativeInfinity, 131008f, 1f),
                new(-1e-30f, float.MaxValue, -0.5f, BitConverter.Int32BitsToSingle(0x7FC0_0777)),
                new(1.5f, 2f, -2f, 2f),
                new(0.25f, 0.5f, 0.75f, -0.25f),
                new(float.Epsilon, -float.Epsilon, 100f, 0.75f),
            ]);
        }
        else
        {
            CheckFloatingPointGeometry<RgbaHalf>(
            [
                new(BitConverter.Int16BitsToHalf(0x7E12), Half.NegativeZero, Half.PositiveInfinity, (Half)0.5f),
                new(BitConverter.Int16BitsToHalf(unchecked((short)0xFE34)), Half.NegativeInfinity, Half.MaxValue, Half.One),
                new(Half.Epsilon, Half.MinValue, (Half)(-0.5f), BitConverter.Int16BitsToHalf(0x7E77)),
                new((Half)1.5f, (Half)2f, (Half)(-2f), (Half)2f),
                new((Half)0.25f, (Half)0.5f, (Half)0.75f, (Half)(-0.25f)),
                new(-Half.Epsilon, (Half)1000f, (Half)100f, (Half)0.75f),
            ]);
        }
    }

    [Fact]
    public void ReplaceColorChangesThePixelsWithinTheToleranceOfAColor()
    {
        // Exactly the color, alpha included: the pixel 0 only
        Rgba32[] pixels = [new(10, 20, 30), new(11, 19, 30), new(10, 20, 30, 128), new(12, 20, 30), new(200, 20, 30)];
        using (var image = new Image<Rgba32>(5, 1))
        {
            Write(image, pixels);
            image.ReplaceColor(new Rgba32(10, 20, 30), default, cancellationToken: Ct);
            Assert.Equal([default, pixels[1], pixels[2], pixels[3], pixels[4]], Read(image));
        }

        // 1/255 is 257 in 16-bit steps: one 8-bit level on each side, in each sample
        using (var image = new Image<Rgba32>(5, 1))
        {
            Write(image, pixels);
            image.ReplaceColor(new Rgba32(10, 20, 30), new Rgba32(0, 0, 255), 1.0 / 255, Ct);
            Assert.Equal([new Rgba32(0, 0, 255), new Rgba32(0, 0, 255), pixels[2], pixels[3], pixels[4]], Read(image));
        }

        // A 16-bit image compares its own samples: 1/65535 is one step
        using (var wide = new Image<Rgba64>(3, 1))
        {
            wide.Frames[0][0, 0] = new Rgba64(1000, 2000, 3000, 65535);
            wide.Frames[0][1, 0] = new Rgba64(1001, 1999, 3000, 65535);
            wide.Frames[0][2, 0] = new Rgba64(1002, 2000, 3000, 65535);
            wide.Frames[0].ReplaceColor(new Rgba64(1000, 2000, 3000, 65535), new Rgba64(1, 2, 3, 4), 1.0 / 65535, Ct);
            Assert.Equal(new Rgba64(1, 2, 3, 4), wide.Frames[0][0, 0]);
            Assert.Equal(new Rgba64(1, 2, 3, 4), wide.Frames[0][1, 0]);
            Assert.Equal(new Rgba64(1002, 2000, 3000, 65535), wide.Frames[0][2, 0]);
        }

        // A gray level is its three colors, and a format without alpha is opaque
        using (var gray = new Image<Gray8>(2, 1))
        {
            gray.Frames[0][0, 0] = new Gray8(10);
            gray.Frames[0][1, 0] = new Gray8(20);
            gray.ReplaceColor(new Rgba32(10, 10, 10), new Rgba32(255, 255, 255), cancellationToken: Ct);
            Assert.Equal(new Gray8(255), gray.Frames[0][0, 0]);
            Assert.Equal(new Gray8(20), gray.Frames[0][1, 0]);
        }

        using (var opaque = new Image<Rgb24>(1, 1, new Rgb24(10, 20, 30)))
        {
            opaque.ReplaceColor(new Rgba32(10, 20, 30, 0), new Rgba32(0, 0, 0), cancellationToken: Ct);
            Assert.Equal(new Rgb24(10, 20, 30), opaque.Frames[0][0, 0]);
            opaque.ReplaceColor(new Rgba32(10, 20, 30), new Rgba32(1, 2, 3), cancellationToken: Ct);
            Assert.Equal(new Rgb24(1, 2, 3), opaque.Frames[0][0, 0]);
        }

        // Floating-point pixels are compared through their 16-bit colors, which must exist
        using (var floats = new Image<RgbaFloat>(1, 1, new RgbaFloat(1, 0, 0, 1)))
        {
            floats.ReplaceColor(new Rgba64(65535, 0, 0, 65535), new Rgba64(0, 65535, 0, 65535), cancellationToken: Ct);
            Assert.Equal(new RgbaFloat(0, 1, 0, 1), floats.Frames[0][0, 0]);
            floats.Frames[0][0, 0] = new RgbaFloat(2, 0, 0, 1);
            Assert.Throws<UnsupportedImageFeatureException>(() => floats.ReplaceColor(new Rgba64(65535, 0, 0, 65535), default, cancellationToken: Ct));
        }

        using var invalid = new Image<Rgba32>(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => invalid.ReplaceColor(default, default, -0.5, Ct));
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => invalid.ReplaceColor(default, default, 1.5, Ct));
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => invalid.Frames[0].ReplaceColor(default, default, double.NaN, Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).ReplaceColor(default, default, cancellationToken: Ct));

        static void Write(Image<Rgba32> image, Rgba32[] values)
        {
            for (var x = 0; x < values.Length; x++)
            {
                image.Frames[0][x, 0] = values[x];
            }
        }

        static Rgba32[] Read(Image<Rgba32> image) => [.. Enumerable.Range(0, image.Width).Select(x => image.Frames[0][x, 0])];
    }

    [Fact]
    public void GrayscaleOfFloatingPointPixelsIsTheLumaOfTheStoredValues()
    {
        // 0.2126 * 2 + 0.7152 * -1 + 0.0722 * 0.5 = 0.4252 - 0.7152 + 0.0361 = -0.2539: not clamped, alpha kept;
        // equal colors keep their value (the coefficients add up to 1), and a sample that is not a number spreads
        using (var image = new Image<RgbaFloat>(3, 1))
        {
            image.Frames[0][0, 0] = new RgbaFloat(2, -1, 0.5f, 0.25f);
            image.Frames[0][1, 0] = new RgbaFloat(10, 10, 10, 1);
            image.Frames[0][2, 0] = new RgbaFloat(float.NaN, 0, 0, 0.5f);
            image.Grayscale(Ct);
            Assert.Equal(new RgbaFloat(-0.2539f, -0.2539f, -0.2539f, 0.25f), image.Frames[0][0, 0]);
            Assert.Equal(new RgbaFloat(10, 10, 10, 1), image.Frames[0][1, 0]);
            Assert.True(float.IsNaN(image.Frames[0][2, 0].R));
            Assert.True(float.IsNaN(image.Frames[0][2, 0].B));
            Assert.Equal(0.5f, image.Frames[0][2, 0].A);
        }

        // Halves get the nearest half of the same luma, computed from their exact values
        using var half = new Image<RgbaHalf>(1, 1, new RgbaHalf((Half)2, (Half)(-1), (Half)0.5f, (Half)0.25f));
        half.Frames[0].Grayscale(Ct);
        Assert.Equal(new RgbaHalf((Half)(-0.2539), (Half)(-0.2539), (Half)(-0.2539), (Half)0.25f), half.Frames[0][0, 0]);
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void ConvertColorProfileConvertsFloatingPointSamplesWithoutQuantization(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointColorConversion<RgbaFloat>(tolerance: 0);
        }
        else
        {
            // The nearest half of a value in [0, 1] is at most 2^-12 away
            CheckFloatingPointColorConversion<RgbaHalf>(tolerance: 1.0 / 4096);
        }
    }

    [Fact]
    public void ConvertColorProfileRejectsFloatingPointColorsOutsideTheUnitRangeBeforeAnythingChanges()
    {
        foreach (var value in new[] { 1.5f, -0.25f, float.NaN, float.PositiveInfinity })
        {
            // The sample is in the second frame: the first one is not converted either
            using var image = new Image<RgbaFloat>(2, 1, new RgbaFloat(0.5f, 0.25f, 0.75f));
            image.AppendFrame()[1, 0] = new RgbaFloat(0.5f, value, 0.5f);
            var storage = image.Frames[0].Storage;
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct));
            Assert.Equal("Color conversion of floating-point samples outside [0, 1]", exception.Feature);
            Assert.Contains("(1, 0) of frame 1", exception.Message, StringComparison.Ordinal);
            Assert.Same(storage, image.Frames[0].Storage);
            Assert.Equal(new RgbaFloat(0.5f, 0.25f, 0.75f), image.Frames[0][0, 0]);
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        }

        // Alpha is copied, whatever its value
        using var translucent = new Image<RgbaHalf>(1, 1, new RgbaHalf((Half)0.5f, (Half)0.5f, (Half)0.5f, (Half)3));
        translucent.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
        Assert.Equal((Half)3, translucent.Frames[0][0, 0].A);
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
    public void OperationsThatSwapTheAxesSwapThePixelAspectRatio()
    {
        // A pixel twice as wide as it is high is twice as high as it is wide once the image is turned by a quarter
        using var image = Build<Rgba32>(Source3X2);
        image.Metadata.PixelAspectRatio = new PixelAspectRatio(2, 1);
        image.Rotate(RotateMode.Rotate90, Ct);
        Assert.Equal(new PixelAspectRatio(1, 2), image.Metadata.PixelAspectRatio);
        image.Rotate(RotateMode.Rotate270, Ct);
        Assert.Equal(new PixelAspectRatio(2, 1), image.Metadata.PixelAspectRatio);

        // Half turns, flips and crops keep the axes
        image.Rotate(RotateMode.Rotate180, Ct);
        image.Flip(FlipMode.Horizontal, Ct);
        image.Crop(new Rectangle(0, 0, 2, 2), Ct);
        Assert.Equal(new PixelAspectRatio(2, 1), image.Metadata.PixelAspectRatio);

        // The orientations that transpose do it too; a failed operation changes nothing
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.AutoOrient(Ct);
        Assert.Equal(new PixelAspectRatio(1, 2), image.Metadata.PixelAspectRatio);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.Rotate(RotateMode.Rotate90, canceled.Token));
        Assert.Equal(new PixelAspectRatio(1, 2), image.Metadata.PixelAspectRatio);

        using var square = Build<Rgba32>(Source3X2);
        square.Rotate(RotateMode.Rotate90, Ct);
        Assert.Null(square.Metadata.PixelAspectRatio);
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
    // Rotations by an angle, skews and transformations (their numerics are checked in TransformGoldenTests)
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void QuarterTurnsByAnAngleAreThePermutationsOfRotateMode(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.Rgba32: CheckQuarterTurns<Rgba32>(); break;
            case PixelFormat.Bgra32: CheckQuarterTurns<Bgra32>(); break;
            case PixelFormat.Rgb24: CheckQuarterTurns<Rgb24>(); break;
            case PixelFormat.Rgba64: CheckQuarterTurns<Rgba64>(); break;
            case PixelFormat.Gray8: CheckQuarterTurns<Gray8>(); break;
            case PixelFormat.Gray16: CheckQuarterTurns<Gray16>(); break;
            case PixelFormat.La16: CheckQuarterTurns<La16>(); break;
            case PixelFormat.La32: CheckQuarterTurns<La32>(); break;
            case PixelFormat.Rgb48: CheckQuarterTurns<Rgb48>(); break;
            case PixelFormat.A8: CheckQuarterTurns<A8>(); break;
            case PixelFormat.RgbaFloat: CheckQuarterTurns<RgbaFloat>(); break;
            case PixelFormat.RgbaHalf: CheckQuarterTurns<RgbaHalf>(); break;
            case PixelFormat.Argb32: CheckQuarterTurns<Argb32>(); break;
            case PixelFormat.Abgr32: CheckQuarterTurns<Abgr32>(); break;
            case PixelFormat.Bgr565: CheckQuarterTurns<Bgr565>(); break;
            case PixelFormat.Rgba1010102: CheckQuarterTurns<Rgba1010102>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Fact]
    public void ARotationByAWholeTurnLeavesTheImageUnchanged()
    {
        foreach (var degrees in new[] { 0, 360, -720 })
        {
            using var image = Build<Rgb24>(Source3X2);
            var storage = image.Frames[0].Storage;
            image.Rotate(degrees, new TransformOptions { Filter = ResamplingFilter.Lanczos3 }, Ct);
            Assert.Same(storage, image.Frames[0].Storage);
            AssertFrame(image.Frames[0], Source3X2);
        }
    }

    [Theory]
    [InlineData(ResamplingFilter.Bilinear, 30, 0.975)]
    [InlineData(ResamplingFilter.Bicubic, -17.5, 0.985)]
    [InlineData(ResamplingFilter.Lanczos3, 63, 0.985)]
    public void RotatingByAnAngleThenBackIsCloseToTheOriginal(ResamplingFilter filter, double degrees, double minimumSsim)
    {
        // A smooth opaque pattern with some detail; the second rotation puts the center back at the center of a canvas of
        // the original size. The edges blend into the transparent background twice: the comparison is on the image
        // without a margin of 4 pixels, the reach of the kernels of both rotations. The measured SSIM is 0.982 with the
        // bilinear filter, which blurs the most, and 0.990 with the others; an image moved by one pixel is far below
        using var original = new Image<Rgba32>(64, 48);
        for (var y = 0; y < original.Height; y++)
        {
            for (var x = 0; x < original.Width; x++)
            {
                original.Frames[0][x, y] = new Rgba32(
                    (byte)(128 + (100 * Math.Sin(x / 5.0))),
                    (byte)(128 + (90 * Math.Cos((x + y) / 7.0))),
                    (byte)(((x + y) / 8 % 2) * 200),
                    255);
            }
        }

        using var image = original.Clone();
        image.Rotate(degrees, new TransformOptions { Filter = filter }, Ct);
        Assert.NotEqual(original.Size, image.Size);
        image.Rotate(-degrees, new TransformOptions { Filter = filter, CanvasMode = TransformCanvasMode.Explicit, CanvasSize = original.Size }, Ct);
        Assert.Equal(original.Size, image.Size);
        var interior = new Rectangle(4, 4, original.Width - 8, original.Height - 8);
        original.Crop(interior, Ct);
        image.Crop(interior, Ct);
        var ssim = ImageSimilarity.ComputeSsim(original.Frames[0], image.Frames[0], Ct);
        Assert.True(ssim > minimumSsim, string.Create(CultureInfo.InvariantCulture, $"SSIM {ssim}"));

        using var moved = original.Clone();
        moved.Transform(AffineTransform.CreateTranslation(1, 0), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct);
        var movedSsim = ImageSimilarity.ComputeSsim(original.Frames[0], moved.Frames[0], Ct);
        Assert.True(movedSsim < 0.95, string.Create(CultureInfo.InvariantCulture, $"SSIM of the moved image {movedSsim}"));
    }

    [Theory]
    [InlineData(ResamplingFilter.Bilinear)]
    [InlineData(ResamplingFilter.Bicubic)]
    public void ARotationReproducesALinearGradientInsideTheImage(ResamplingFilter filter)
    {
        // Bilinear and Catmull-Rom reproduce linear functions: away from the edges, every output pixel is the gradient at the
        // point its center comes from, computed here from the definition of the rotation
        const int Width = 40;
        const int Height = 30;
        using var image = new Image<Gray16>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                image.Frames[0][x, y] = new Gray16((ushort)(2000 + (700 * x) + (900 * y)));
            }
        }

        image.Rotate(30, new TransformOptions { Filter = filter, BackgroundColor = new Rgba64(0, 0, 0, 65535) }, Ct);

        // 40 cos 30 + 30 sin 30 = 49.64 and 40 sin 30 + 30 cos 30 = 45.98, rounded up
        Assert.Equal(new Size(50, 46), image.Size);
        var cos = Math.Sqrt(3) / 2;
        var sin = 0.5;
        var radius = filter == ResamplingFilter.Bilinear ? 1 : 2;
        var checkedPixels = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                // The canvas center is the image center; the inverse of a clockwise turn turns counterclockwise
                var dx = x + 0.5 - 25;
                var dy = y + 0.5 - 23;
                var u = (cos * dx) + (sin * dy) + (Width / 2.0) - 0.5;
                var v = (-sin * dx) + (cos * dy) + (Height / 2.0) - 0.5;
                if (u < radius - 1 || v < radius - 1 || u > Width - radius || v > Height - radius)
                    continue;

                var expected = 2000 + (700 * u) + (900 * v);
                Assert.True(Math.Abs(image.Frames[0][x, y].Value - expected) <= 0.5 + 1e-6, string.Create(CultureInfo.InvariantCulture, $"({x}, {y}): {image.Frames[0][x, y].Value}, expected {expected}"));
                checkedPixels++;
            }
        }

        Assert.True(checkedPixels > 500, string.Create(CultureInfo.InvariantCulture, $"{checkedPixels} pixels checked"));
    }

    [Fact]
    public void AnExpandedRotationCentersTheImageAndFillsTheCornersWithTheBackground()
    {
        // 20 x 20 turned by 45 degrees: 20 sqrt(2) = 28.28 is rounded up; the corners are far from the turned square
        using var image = new Image<Rgb24>(20, 20, new Rgb24(10, 20, 30));
        image.Rotate(45, new TransformOptions { BackgroundColor = new Rgba64(65535, 0, 0, 65535) }, Ct);
        Assert.Equal(new Size(29, 29), image.Size);
        Assert.Equal(new Rgb24(255, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(255, 0, 0), image.Frames[0][28, 28]);
        Assert.Equal(new Rgb24(10, 20, 30), image.Frames[0][14, 14]);
        Assert.Equal(new Rgb24(10, 20, 30), image.Frames[0][14, 2]);

        // A translucent background is filtered premultiplied: transparent where nothing of the image is read
        using var transparent = new Image<Rgba32>(20, 20, new Rgba32(10, 20, 30, 255));
        transparent.Rotate(45, cancellationToken: Ct);
        Assert.Equal(default, transparent.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(10, 20, 30, 255), transparent.Frames[0][14, 14]);
        var edge = transparent.Frames[0][14, 0];
        Assert.InRange(edge.A, 1, 254);
        Assert.Equal(new Rgba32(10, 20, 30, edge.A), edge);
    }

    [Fact]
    public void ASkewKeepsTheCenterAndLeansTheEdges()
    {
        // tan(45) = 1: the 4 x 2 image becomes a parallelogram 6 pixels wide, X = x + y on the canvas. With the bilinear
        // filter, the center of output pixel (x, 0) comes from source (x, 0.5) and reads pixels x - 1 and x of row 0, and
        // that of (x, 1) from (x - 1, 1.5): a pixel that reads one pixel of the image and one of the background is half
        // transparent, with the color of the image
        var content = new Rgba32(200, 100, 50, 255);
        using var image = new Image<Rgba32>(4, 2, content);
        image.Skew(45, 0, new TransformOptions { Filter = ResamplingFilter.Bilinear }, Ct);
        Assert.Equal(new Size(6, 2), image.Size);
        string[] expected = ["half content content content half transparent", "transparent half content content content half"];
        for (var y = 0; y < 2; y++)
        {
            Assert.Equal(expected[y], string.Join(' ', Row(image, y).Select(pixel => pixel switch
            {
                { A: 255 } when pixel == content => "content",
                { A: 0 } when pixel == default => "transparent",
                { A: > 100 and < 155 } when (pixel.R, pixel.G, pixel.B) == (200, 100, 50) => "half",
                _ => pixel.ToString(),
            })));
        }

        // Two angles whose tangents multiply to 1 flatten the image
        Assert.Throws<ArgumentException>(() => image.Skew(45, 45, cancellationToken: Ct));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Skew(90, 0, cancellationToken: Ct));
        Assert.Equal(new Size(6, 2), image.Size);
    }

    [Fact]
    public void AKeptCanvasAppliesTheTransformationAsGiven()
    {
        // An integer translation on a kept canvas: the pixels move, and the background enters where they leave
        using var image = Build<Rgba32>(Source3X2);
        image.Transform(AffineTransform.CreateTranslation(1, 0), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct);
        Assert.Equal(new Size(3, 2), image.Size);
        Assert.Equal(default, image.Frames[0][0, 0]);
        Assert.Equal(Pixel<Rgba32>(1), image.Frames[0][1, 0]);
        Assert.Equal(Pixel<Rgba32>(5), image.Frames[0][2, 1]);

        // An explicit canvas has the origin of the image at its top-left corner
        using var larger = Build<Rgba32>(Source3X2);
        larger.Transform(AffineTransform.Identity, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(4, 3) }, Ct);
        Assert.Equal(new Size(4, 3), larger.Size);
        Assert.Equal(Pixel<Rgba32>(1), larger.Frames[0][0, 0]);
        Assert.Equal(Pixel<Rgba32>(6), larger.Frames[0][2, 1]);
        Assert.Equal(default, larger.Frames[0][3, 0]);
        Assert.Equal(default, larger.Frames[0][0, 2]);

        // An explicit canvas needs a size
        Assert.Throws<ArgumentException>(() => larger.Rotate(10, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit }, Ct));
    }

    [Fact]
    public void AFormatWithoutAlphaNeedsAnOpaqueBackgroundWhenTheBackgroundShows()
    {
        using var image = Build<Rgb24>(Source3X2);
        var storage = image.Frames[0].Storage;
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Rotate(30, cancellationToken: Ct));
        Assert.Equal("Translucent background without alpha", exception.Feature);
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Rotate(30, new TransformOptions { BackgroundColor = new Rgba64(0, 0, 0, 65534) }, Ct));
        Assert.Same(storage, image.Frames[0].Storage);
        AssertFrame(image.Frames[0], Source3X2);

        // A quarter turn, a mirror, and an enlargement of the center on a kept canvas show no background
        image.Rotate(90.0, cancellationToken: Ct);
        AssertFrame(image.Frames[0], Rotated90);
        image.Transform(new AffineTransform(-1, 0, 0, 1, 0, 0), cancellationToken: Ct);
        AssertFrame(image.Frames[0], [[1, 4], [2, 5], [3, 6]]);
        using var gray = new Image<Gray8>(8, 8, new Gray8(77));
        gray.Transform(AffineTransform.CreateScale(2, 2, new PointD(4, 4)), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct);
        Assert.Equal(new Gray8(77), gray.Frames[0][0, 0]);

        // The same enlargement with Lanczos3 reads 3 pixels around each point, beyond the edges of the image
        Assert.Throws<UnsupportedImageFeatureException>(() => gray.Transform(AffineTransform.CreateScale(2, 2, new PointD(4, 4)), new TransformOptions { CanvasMode = TransformCanvasMode.Keep, Filter = ResamplingFilter.Lanczos3 }, Ct));

        // A canvas of the image without its border, with the same pixel centers: an interpolating filter reads one pixel per
        // output pixel and shows no background, a smoothing cubic also reads the neighbors, beyond the edges
        using var smaller = new Image<Gray8>(8, 8, new Gray8(77));
        var cropped = new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(6, 6) };
        Assert.Throws<UnsupportedImageFeatureException>(() => smaller.Transform(AffineTransform.Identity, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(6, 6), Filter = ResamplingFilter.MitchellNetravali }, Ct));
        smaller.Transform(AffineTransform.Identity, cropped, Ct);
        Assert.Equal(new Size(6, 6), smaller.Size);
        Assert.Equal(new Gray8(77), smaller.Frames[0][0, 0]);

        // A projective transformation may always show it
        Assert.Throws<UnsupportedImageFeatureException>(() => gray.Transform(new ProjectiveTransform(1, 0, 0.001, 0, 1, 0, 0, 0, 1), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct));

        image.Rotate(30, new TransformOptions { BackgroundColor = new Rgba64(0, 65535, 0, 65535) }, Ct);
        Assert.Equal(new Size(4, 4), image.Size);
    }

    [Fact]
    public void TheCanvasOfATransformationRespectsTheLimits()
    {
        // 100 x 60 turned by 45 degrees: 160 / sqrt(2) = 113.14 in both directions, rounded up to 114
        foreach (var (limits, kind) in new (ImageResourceLimits Limits, ImageResourceLimitKind? Kind)[]
        {
            (new ImageResourceLimits { MaxWidth = 114, MaxHeight = 114, MaxFramePixels = 114 * 114 }, null),
            (new ImageResourceLimits { MaxWidth = 113 }, ImageResourceLimitKind.Width),
            (new ImageResourceLimits { MaxHeight = 113 }, ImageResourceLimitKind.Height),
            (new ImageResourceLimits { MaxFramePixels = (114 * 114) - 1 }, ImageResourceLimitKind.FramePixels),
        })
        {
            using var image = new Image<Rgba32>(100, 60, new ImageConfiguration { Limits = limits });
            var storage = image.Frames[0].Storage;
            if (kind is null)
            {
                image.Rotate(45, cancellationToken: Ct);
                Assert.Equal(new Size(114, 114), image.Size);
                continue;
            }

            var exception = Assert.Throws<ImageResourceLimitException>(() => image.Rotate(45, cancellationToken: Ct));
            Assert.Equal(kind, exception.Kind);
            Assert.Same(storage, image.Frames[0].Storage);
            Assert.Equal(new Size(100, 60), image.Size);
        }

        // A canvas larger than any canvas can be is reported with the size it would have, and an explicit canvas is checked too
        using var small = new Image<Rgba32>(4, 4);
        var huge = Assert.Throws<ImageResourceLimitException>(() => small.Transform(AffineTransform.CreateScale(1e12, 1), cancellationToken: Ct));
        Assert.Equal(ImageResourceLimitKind.Width, huge.Kind);
        var limited = new ImageConfiguration { Limits = new ImageResourceLimits { MaxHeight = 10 } };
        using var explicitCanvas = new Image<Rgba32>(4, 4, limited);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => explicitCanvas.Rotate(10, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(10, 11) }, Ct)).Kind);
        explicitCanvas.Rotate(10, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(10, 10) }, Ct);
        Assert.Equal(new Size(10, 10), explicitCanvas.Size);
    }

    [Fact]
    public void ATransformationIsTransactional()
    {
        // The limit is exact at the measured peak: one byte less fails before anything changes, frames and poster included
        var options = new TransformOptions { Filter = ResamplingFilter.Lanczos3 };
        long peak;
        using (var probe = BuildAnimated(ImageConfiguration.Default))
        {
            probe.Rotate(20, options, Ct);
            peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
        }

        using (var image = BuildAnimated(new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } }))
        {
            var snapshot = ImageState.Capture(image);
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => image.Rotate(20, options, Ct)).Kind);
            snapshot.AssertUnchanged(image);

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => image.Rotate(20, options, canceled.Token));
            snapshot.AssertUnchanged(image);
        }

        using (var image = BuildAnimated(new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak } }))
        {
            var frames = ((IEnumerable<ImageFrame<Rgba32>>)image.Frames).ToArray();
            var liveStorages = image.Owner.LiveStorageCount;
            image.Rotate(20, options, Ct);
            Assert.Equal(frames, ((IEnumerable<ImageFrame<Rgba32>>)image.Frames).ToArray());
            Assert.Equal(image.Size, image.PosterFrame!.Size);

            // The scratch and the original storages are released
            Assert.Equal(liveStorages, image.Owner.LiveStorageCount);
            Assert.Equal(image.Owner.Scope.GetDiagnostics().LiveAllocations, liveStorages);
        }

        static Image<Rgba32> BuildAnimated(ImageConfiguration configuration)
        {
            var image = Build<Rgba32>(configuration, scope: null, layout: null, Source4X3, Offset(Source4X3, 20));
            image.SetPosterFrame(image.Frames[1]);
            return image;
        }
    }

    [Fact]
    public void ATransformationRejectsWhatItCannotDo()
    {
        using var image = Build<Rgba32>(Source3X2);
        Assert.Throws<ArgumentException>(() => image.Rotate(double.NaN, cancellationToken: Ct));
        Assert.Throws<ArgumentException>(() => image.Transform(new AffineTransform(1, 2, 2, 4, 0, 0), cancellationToken: Ct));
        Assert.Throws<ArgumentException>(() => image.Transform(new ProjectiveTransform(1, 0, 0, 0, 0, 0, 0, 0, 1), cancellationToken: Ct));

        // W = x - 1: the points left of x = 1 are behind the horizon. An expanded canvas cannot hold them; on a kept canvas,
        // the center of pixel (0, 0) comes from x = -1, which has no image, and that of (2, 0) from x = 5/3, in pixel 1
        var horizon = new ProjectiveTransform(1, 0, 1, 0, 1, 0, 0, 0, -1);
        Assert.Throws<ArgumentException>(() => image.Transform(horizon, cancellationToken: Ct));
        Assert.Equal(new Size(3, 2), image.Size);
        image.Transform(horizon, new TransformOptions { CanvasMode = TransformCanvasMode.Keep, Filter = ResamplingFilter.NearestNeighbor }, Ct);
        Assert.Equal(default, image.Frames[0][0, 0]);
        Assert.Equal(Pixel<Rgba32>(2), image.Frames[0][2, 0]);

        Assert.Throws<ArgumentNullException>(() => ImageProcessingExtensions.Rotate(null!, 10.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransformOptions { Filter = (ResamplingFilter)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransformOptions { CanvasMode = (TransformCanvasMode)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransformOptions { WorkingSpace = (ResizeWorkingSpace)2 });
    }

    [Fact]
    public void ATransformationKeepsThePixelAspectRatioAndReconcilesTheExifDimensions()
    {
        using var image = Build<Rgba32>(Source4X3);
        image.Metadata.PixelAspectRatio = new PixelAspectRatio(1, 2);
        image.Metadata.ExifProfile = CreateExif(orientation: 1, width: 4, height: 3);
        image.Rotate(30, cancellationToken: Ct);
        Assert.Equal(new PixelAspectRatio(1, 2), image.Metadata.PixelAspectRatio);
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.Equal(((uint)image.Width, (uint)image.Height), ReadIfd0Dimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));

        // A quarter turn swaps the axes of the pixels, as RotateMode does
        image.Rotate(-90, cancellationToken: Ct);
        Assert.Equal(new PixelAspectRatio(2, 1), image.Metadata.PixelAspectRatio);
    }

    [Fact]
    public void FloatingPointSamplesAreTransformedWithoutClamping()
    {
        // Highlights above 1 and negative values survive the filter; a sample that is not a number spreads to the pixels
        // whose kernels read it only
        using var image = new Image<RgbaFloat>(16, 16, new RgbaFloat(4, -0.5f, 0.25f, 1));
        image.Frames[0][0, 0] = new RgbaFloat(float.NaN, 0, 0, 1);
        image.Rotate(10, new TransformOptions { Filter = ResamplingFilter.Bilinear }, Ct);
        var center = image.Frames[0][image.Width / 2, image.Height / 2];
        Assert.Equal(new RgbaFloat(4, -0.5f, 0.25f, 1), center);
        var nan = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (float.IsNaN(image.Frames[0][x, y].R))
                {
                    nan++;
                }
            }
        }

        Assert.InRange(nan, 1, 4);
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

    // -----------------------------------------------------------------------------------------------------------------
    // Cursor hotspots
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, int, int> HotspotPermutations => new()
    {
        // The hotspot designates C, the top-right pixel of [[A, B, C], [D, E, F]]: where C is in each result
        { "Rotate90", 1, 2 },
        { "Rotate180", 0, 1 },
        { "Rotate270", 0, 0 },
        { "FlipHorizontal", 0, 0 },
        { "FlipVertical", 2, 1 },
        { "AutoOrient1", 2, 0 },
        { "AutoOrient2", 0, 0 },
        { "AutoOrient3", 0, 1 },
        { "AutoOrient4", 2, 1 },
        { "AutoOrient5", 0, 2 },
        { "AutoOrient6", 1, 2 },
        { "AutoOrient7", 1, 0 },
        { "AutoOrient8", 0, 0 },
    };

    [Theory]
    [MemberData(nameof(HotspotPermutations))]
    public void AHotspotFollowsItsPixelThroughRotationsMirrorsAndAutoOrient(string operation, int expectedX, int expectedY)
    {
        using var image = Build<Rgba32>(Source3X2);
        image.Frames[0].Metadata.Hotspot = new Point(2, 0);
        switch (operation)
        {
            case "Rotate90":
                image.Rotate(RotateMode.Rotate90, Ct);
                break;
            case "Rotate180":
                image.Rotate(RotateMode.Rotate180, Ct);
                break;
            case "Rotate270":
                image.Rotate(RotateMode.Rotate270, Ct);
                break;
            case "FlipHorizontal":
                image.Flip(FlipMode.Horizontal, Ct);
                break;
            case "FlipVertical":
                image.Flip(FlipMode.Vertical, Ct);
                break;
            default:
                image.Metadata.Orientation = (ExifOrientation)(operation[^1] - '0');
                image.AutoOrient(Ct);
                break;
        }

        Assert.Equal(new Point(expectedX, expectedY), image.Frames[0].Metadata.Hotspot);
        Assert.Equal(Pixel<Rgba32>(3), image.Frames[0][expectedX, expectedY]); // it still designates C
    }

    [Fact]
    public void ATransformationMovesTheHotspotWithItsPixelAndNeverRemovesItSilently()
    {
        // 20 x 10 turned by 90.5 degrees: the canvas is 11 x 21 (10.17 x 20.09 rounded up). The center of pixel (2, 4) is
        // (-7.5, -0.5) from the center of the image: it turns to (0.565, -7.495), at (6.065, 3.005) on the canvas
        using (var image = new Image<Rgba32>(20, 10))
        {
            image.Frames[0].Metadata.Hotspot = new Point(2, 4);
            image.Rotate(90.5, cancellationToken: Ct);
            Assert.Equal(new Size(11, 21), image.Size);
            Assert.Equal(new Point(6, 3), image.Frames[0].Metadata.Hotspot);
        }

        // A translation on a kept canvas that moves the pixel off the canvas fails, and changes nothing
        using var removed = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        removed.Frames[1].Metadata.Hotspot = new Point(2, 0);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => removed.Transform(AffineTransform.CreateTranslation(1, 0), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct));
        Assert.Equal("Cursor hotspot outside the kept region", exception.Feature);
        Assert.Contains("frame 1", exception.Message, StringComparison.Ordinal);
        AssertFrame(removed.Frames[1], Source3X2, idOffset: 10);
        removed.Transform(AffineTransform.CreateTranslation(-1, 0), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct);
        Assert.Equal(new Point(1, 0), removed.Frames[1].Metadata.Hotspot);
    }

    [Fact]
    public void ACropTranslatesTheHotspotAndNeverRemovesItSilently()
    {
        using (var image = Build<Rgba32>(Source3X2))
        {
            image.Frames[0].Metadata.Hotspot = new Point(2, 1);
            image.Crop(new Rectangle(1, 1, 2, 1), Ct);
            Assert.Equal(new Point(1, 0), image.Frames[0].Metadata.Hotspot); // the last kept column, the only kept row
            AssertFrame(image.Frames[0], [[5, 6]]);
        }

        using var removed = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        removed.Frames[1].Metadata.Hotspot = new Point(2, 0);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => removed.Crop(new Rectangle(0, 0, 2, 2), Ct));
        Assert.Equal("Cursor hotspot outside the kept region", exception.Feature);
        Assert.Contains("frame 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new Size(3, 2), removed.Size);
        Assert.Equal(new Point(2, 0), removed.Frames[1].Metadata.Hotspot);
        AssertFrame(removed.Frames[1], Source3X2, idOffset: 10);

        // Removing the pixel a hotspot designates is the caller's explicit choice
        removed.Frames[1].Metadata.Hotspot = null;
        removed.Crop(new Rectangle(0, 0, 2, 2), Ct);
        Assert.Equal(new Size(2, 2), removed.Size);
    }

    [Fact]
    public void EveryFrameAndThePosterKeepTheirOwnHotspot()
    {
        using var image = Build<Rgba32>(Source3X2, Offset(Source3X2, 10), Offset(Source3X2, 20));
        image.SetPosterFrame(image.Frames[0]);
        image.Frames[0].Metadata.Hotspot = new Point(0, 0);
        image.Frames[2].Metadata.Hotspot = new Point(2, 1);
        image.PosterFrame!.Metadata.Hotspot = new Point(1, 0);

        image.Rotate(RotateMode.Rotate90, Ct);
        Assert.Equal(new Point(1, 0), image.Frames[0].Metadata.Hotspot); // A is now the top-right pixel
        Assert.Null(image.Frames[1].Metadata.Hotspot);
        Assert.Equal(new Point(0, 2), image.Frames[2].Metadata.Hotspot); // F is now the bottom-left pixel
        Assert.Equal(new Point(1, 1), image.PosterFrame.Metadata.Hotspot); // B

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Crop(new Rectangle(0, 0, 2, 1), Ct));
        Assert.Contains("frame 2", exception.Message, StringComparison.Ordinal);
        image.Frames[2].Metadata.Hotspot = null;
        Assert.Contains("poster", Assert.Throws<UnsupportedImageFeatureException>(() => image.Crop(new Rectangle(0, 0, 2, 1), Ct)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlippingOneFrameMovesOnlyItsHotspotAndPixelOperationsKeepIt()
    {
        using var image = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        image.Frames[0].Metadata.Hotspot = new Point(2, 0);
        image.Frames[1].Metadata.Hotspot = new Point(2, 0);
        image.Frames[1].Flip(FlipMode.Horizontal, Ct);
        Assert.Equal(new Point(2, 0), image.Frames[0].Metadata.Hotspot);
        Assert.Equal(new Point(0, 0), image.Frames[1].Metadata.Hotspot);

        image.Grayscale(Ct);
        image.Convolve(Sharpen, Ct);
        image.Frames[0].Grayscale(Ct);
        Assert.Equal(new Point(2, 0), image.Frames[0].Metadata.Hotspot);
        Assert.Equal(new Point(0, 0), image.Frames[1].Metadata.Hotspot);

        // The identity operations do nothing to it either
        image.Rotate(RotateMode.None, Ct);
        image.AutoOrient(Ct);
        image.Crop(new Rectangle(0, 0, 3, 2), Ct);
        Assert.Equal(new Point(2, 0), image.Frames[0].Metadata.Hotspot);
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
        Assert.ThrowsAny<OperationCanceledException>(() => image.AutoCrop(cancellationToken: canceled));
        Assert.ThrowsAny<OperationCanceledException>(() => image.AnalyzeAutoCrop(cancellationToken: canceled));
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
            Assert.Throws<InvalidOperationException>(() => image.AutoCrop(cancellationToken: Ct));
            Assert.Throws<InvalidOperationException>(() => image.AnalyzeAutoCrop(cancellationToken: Ct));
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
        var analysis = image.AnalyzeAutoCrop(cancellationToken: Ct);
        var removed = image.Frames[1];
        image.RemoveFrame(1);
        Assert.Throws<ObjectDisposedException>(() => removed.Flip(FlipMode.Horizontal, Ct));
        Assert.Throws<ObjectDisposedException>(() => removed.Grayscale(Ct));
        Assert.Throws<ObjectDisposedException>(() => removed.Convolve(Sharpen, Ct));

        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => image.Crop(new Rectangle(0, 0, 1, 1), Ct));
        Assert.Throws<ObjectDisposedException>(() => image.AutoCrop(cancellationToken: Ct));
        Assert.Throws<ObjectDisposedException>(() => image.AutoCrop(analysis, cancellationToken: Ct));
        Assert.Throws<ObjectDisposedException>(() => image.AnalyzeAutoCrop(cancellationToken: Ct));
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
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).AutoCrop(cancellationToken: Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).AutoCrop(image.AnalyzeAutoCrop(cancellationToken: Ct), cancellationToken: Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).AnalyzeAutoCrop(cancellationToken: Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image<Rgba32>)null!).AnalyzeAutoCrop(cancellationToken: Ct));
        Assert.Throws<ArgumentNullException>("analysis", () => image.AutoCrop((AutoCropAnalysis)null!, cancellationToken: Ct));
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
        Assert.Throws<UnsupportedImageFeatureException>(() => image.DetectEdges(EdgeDetector.Sobel, cancellationToken: Ct));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].DetectEdges(EdgeDetector.Sobel, cancellationToken: Ct));
        Assert.Equal(new Rgba32(255, 0, 0, 255), image.Frames[0][0, 0]);

        // Without grayscale, edge detection keeps the colors, so the profile does not matter
        image.DetectEdges(EdgeDetector.Sobel, grayscale: false, cancellationToken: Ct);
        Assert.Equal(new Rgba32(0, 0, 0, 255), image.Frames[0][0, 0]);
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

    /// <summary>
    /// An image without ICC profile whose CICP color description is not the one of sRGB is converted from the color
    /// space of that description: the function of its transfer characteristics, then its primaries, adapted to the
    /// white of the connection space. The reference is the <see cref="decimal"/> conversion of the harness
    /// (<c>ReferenceCicpColorSpace</c>: its own table of chromaticities, its own functions), followed by its reading of
    /// the destination profile.
    /// </summary>
    [Theory]
    [InlineData(1, 1)] // Rec. ITU-R BT.709: the primaries of sRGB with another function
    [InlineData(9, 14)] // Rec. ITU-R BT.2020
    [InlineData(12, 13)] // Display P3
    [InlineData(1, 4)] // gamma 2.2
    [InlineData(5, 5)] // Rec. ITU-R BT.470 System B, G: gamma 2.8
    [InlineData(6, 6)] // Rec. ITU-R BT.601
    [InlineData(4, 7)] // System M primaries (white C), SMPTE 240M function
    [InlineData(8, 9)] // film, logarithmic function
    [InlineData(22, 10)] // EBU Tech. 3213-E, logarithmic function
    [InlineData(11, 17)] // DCI P3, SMPTE ST 428-1
    [InlineData(10, 17)] // CIE 1931 XYZ
    [InlineData(9, 8)] // linear BT.2020
    [InlineData(1, 8)] // linear BT.709, without the linear label
    public void ConvertColorProfileConvertsFromTheCicpColorDescription(byte primaries, byte transfer)
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var grayGamma = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
        foreach (var (intent, blackPointCompensation) in new[] { (IccRenderingIntent.RelativeColorimetric, true), (IccRenderingIntent.AbsoluteColorimetric, false), (IccRenderingIntent.Perceptual, false) })
        {
            var options = new IccColorTransformOptions { Intent = intent, BlackPointCompensation = blackPointCompensation };
            Check<Rgb24>(IccProfile.Srgb, options);
            Check<Rgba32>(displayP3, options);
            Check<Rgba64>(IccProfile.Srgb, options);
            Check<Gray8>(IccProfile.SrgbGray, options);
            Check<Gray16>(grayGamma, options);
        }

        void Check<TPixel>(IccProfile destination, IccColorTransformOptions options)
            where TPixel : unmanaged
        {
            // The pixels cover the range of each channel; the matrix coefficients and the range of the description
            // describe how a file stored the samples, not the pixels
            using var image = Build<TPixel>(Source4X3, Offset(Source4X3, 20));
            image.SetPosterFrame(image.Frames[1]);
            image.Metadata.Cicp = new CicpColorDescription(primaries, transfer, 6, fullRange: false);
            image.Metadata.ContentLightLevel = new ContentLightLevel(400, 100);
            var before = image.Frames.Append(image.PosterFrame!).Select(frame => ImageSnapshots.CaptureFrameRows(frame)).ToArray();
            image.ConvertColorProfile(destination, options, Ct);

            // The pixels are described by the profile, and by nothing that described them before
            Assert.Same(destination, image.Metadata.IccProfile);
            Assert.Null(image.Metadata.Cicp);
            Assert.Null(image.Metadata.ContentLightLevel);
            Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);

            var isGray = PixelFormats.IsGrayscale(image.PixelFormat);

            // Gray samples have no primaries: with the sRGB function they are the samples of the built-in sGray profile
            var reference = isGray && transfer is 13
                ? ReferenceIccTransform.Create(IccProfile.SrgbGray.Data.Span, destination.Data.Span, (int)options.Intent, options.BlackPointCompensation)
                : ReferenceIccTransform.CreateFromCicp(primaries, transfer, isGray, destination.Data.Span, (int)options.Intent, options.BlackPointCompensation);
            var after = image.Frames.Append(image.PosterFrame!).Select(frame => ImageSnapshots.CaptureFrameRows(frame)).ToArray();
            Assert.HasCount(3, after);
            for (var i = 0; i < after.Length; i++)
            {
                var source = ColorSamples(before[i]);
                var actual = ColorSamples(after[i]);
                Assert.Empty(PixelFormats.GetBitsPerComponent(image.PixelFormat) == 16
                    ? reference.Compare(source.Select(value => (ushort)value).ToArray(), actual.Select(value => (ushort)value).ToArray())
                    : reference.Compare(source.Select(value => (byte)value).ToArray(), actual.Select(value => (byte)value).ToArray()));
                if (before[i].Layout.HasAlpha)
                {
                    for (var pixel = 0; pixel < before[i].Width * before[i].Height; pixel++)
                    {
                        Assert.Equal(before[i].GetSample(pixel % before[i].Width, pixel / before[i].Width, before[i].Layout.AlphaChannel), after[i].GetSample(pixel % before[i].Width, pixel / before[i].Width, before[i].Layout.AlphaChannel));
                    }
                }
            }
        }

        static int[] ColorSamples(RawPixelBuffer buffer)
        {
            var samples = new List<int>();
            for (var pixel = 0; pixel < buffer.Width * buffer.Height; pixel++)
            {
                for (var channel = 0; channel < buffer.Layout.ChannelCount; channel++)
                {
                    if (!buffer.Layout.HasAlpha || channel != buffer.Layout.AlphaChannel)
                        samples.Add(buffer.GetSample(pixel % buffer.Width, pixel / buffer.Width, channel));
                }
            }

            return [.. samples];
        }
    }

    /// <summary>
    /// The primaries of a CICP description are those of an ICC profile with the same colorants: Display P3 pixels convert
    /// to sRGB the same way whether they are described by the code points 12/13 or by a profile, up to the rounding of
    /// the colorants of the test profile, which are written with four decimals: less than one 8-bit step.
    /// </summary>
    [Fact]
    public void ConvertColorProfileFromACicpDescriptionAgreesWithTheEquivalentProfile()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        using var described = Build<Rgba64>(Source4X3);
        using var profiled = Build<Rgba64>(Source4X3);
        described.Metadata.Cicp = new CicpColorDescription(12, 13, 0, fullRange: true);
        profiled.Metadata.IccProfile = displayP3;
        described.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
        profiled.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct);
        var worst = 0;
        for (var y = 0; y < described.Height; y++)
        {
            for (var x = 0; x < described.Width; x++)
            {
                var (a, b) = (described.Frames[0][x, y], profiled.Frames[0][x, y]);
                worst = Math.Max(worst, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
            }
        }

        Assert.True(worst <= 257, $"The two descriptions of Display P3 differ by {worst} units of 65535.");
    }

    [Fact]
    public void ConvertColorProfileKeepsTheBuiltInProfilesForSrgbDescriptions()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        using var untagged = Build<Rgba64>(Source4X3);
        untagged.ConvertColorProfile(displayP3, cancellationToken: Ct);

        // BT.709 or unspecified primaries with the sRGB or an unspecified function: the pixels of an untagged image, bit
        // for bit. The description is kept: it is the one of a file, whose matrix coefficients an encoder may reuse
        foreach (var cicp in new[] { new CicpColorDescription(1, 13, 6, fullRange: true), new CicpColorDescription(2, 2, 1, fullRange: false), new CicpColorDescription(1, 2, 0, fullRange: true), new CicpColorDescription(2, 13, 5, fullRange: true) })
        {
            using var image = Build<Rgba64>(Source4X3);
            image.Metadata.Cicp = cicp;
            image.ConvertColorProfile(displayP3, cancellationToken: Ct);
            AssertSamePixels(untagged, image);
            Assert.Equal(cicp, image.Metadata.Cicp);
        }

        // Samples labeled as linear light with BT.709 primaries are those of the built-in linear profile
        using var linear = Build<Rgba64>(Source4X3);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        linear.ConvertColorProfile(displayP3, cancellationToken: Ct);
        using var linearDescribed = Build<Rgba64>(Source4X3);
        linearDescribed.Metadata.TransferFunction = ColorTransferFunction.Linear;
        linearDescribed.Metadata.Cicp = new CicpColorDescription(1, 8, 0, fullRange: true);
        linearDescribed.ConvertColorProfile(displayP3, cancellationToken: Ct);
        AssertSamePixels(linear, linearDescribed);

        // An ICC profile describes the colors in the place of the description
        using var profiled = Build<Rgba64>(Source4X3);
        profiled.Metadata.IccProfile = IccProfile.Srgb;
        profiled.Metadata.Cicp = new CicpColorDescription(9, 16, 9, fullRange: true);
        profiled.ConvertColorProfile(displayP3, cancellationToken: Ct);
        AssertSamePixels(untagged, profiled);
        Assert.Null(profiled.Metadata.Cicp);

        static void AssertSamePixels(Image<Rgba64> expected, Image<Rgba64> actual)
        {
            for (var y = 0; y < expected.Height; y++)
            {
                for (var x = 0; x < expected.Width; x++)
                {
                    Assert.Equal(expected.Frames[0][x, y], actual.Frames[0][x, y]);
                }
            }
        }
    }

    [Theory]
    [InlineData(9, 16, "Color conversion of high dynamic range samples without tone mapping")]
    [InlineData(9, 18, "Color conversion of high dynamic range samples without tone mapping")]
    [InlineData(9, 3, "Color conversion from reserved transfer characteristics")]
    [InlineData(1, 0, "Color conversion from reserved transfer characteristics")]
    [InlineData(3, 13, "Color conversion from reserved color primaries")]
    [InlineData(13, 1, "Color conversion from reserved color primaries")]
    public void ConvertColorProfileRejectsCicpDescriptionsItCannotConvert(byte primaries, byte transfer, string feature)
    {
        using var image = Build<Rgba64>(Source4X3);
        var cicp = new CicpColorDescription(primaries, transfer, 0, fullRange: true);
        image.Metadata.Cicp = cicp;
        image.Metadata.ContentLightLevel = new ContentLightLevel(1000, 200);
        var storage = image.Frames[0].Storage;
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.ConvertColorProfile(IccProfile.Srgb, cancellationToken: Ct));
        Assert.Equal(feature, exception.Feature);

        // Nothing is changed
        Assert.Same(storage, image.Frames[0].Storage);
        Assert.Equal(cicp, image.Metadata.Cicp);
        Assert.Null(image.Metadata.IccProfile);
        Assert.NotNull(image.Metadata.ContentLightLevel);
        AssertFrame(image.Frames[0], Source4X3);
    }

    /// <summary>
    /// PQ and HLG samples are converted when the options say how their highlights are compressed: every operator, for
    /// both functions, with a peak that is given, derived from the light levels of the image or left to the nominal peak
    /// of the function, against the <see cref="decimal"/> reference of the harness (<c>ReferenceHdrToneMap</c>).
    /// </summary>
    [Theory]
    [MemberData(nameof(ToneMappingCases))]
    public void ConvertColorProfileToneMapsHighDynamicRangeSamples(byte transfer, HdrToneMappingOperator toneMappingOperator)
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        Check<Rgba64>(9, IccProfile.Srgb, peak: 1000, white: 203, lightLevel: null, display: null, expectedPeak: 1000);
        Check<Rgb24>(12, displayP3, peak: null, white: 203, lightLevel: null, display: null, expectedPeak: transfer is 16 ? 10000 : 1000);
        Check<Gray16>(9, IccProfile.SrgbGray, peak: 600, white: 100, lightLevel: null, display: null, expectedPeak: 600);

        // The light levels of the image: the content light level describes PQ samples, the mastering display both
        var mastering = new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 40_000_000, 50);
        Check<Rgba64>(1, displayP3, peak: null, white: 203, new ContentLightLevel(1500, 300), mastering, expectedPeak: transfer is 16 ? 1500 : 4000);
        Check<Rgba32>(9, IccProfile.Srgb, peak: null, white: 250, new ContentLightLevel(0, 0), mastering, expectedPeak: 4000);
        Check<Rgba64>(10, IccProfile.Srgb, peak: 150, white: 203, lightLevel: null, display: null, expectedPeak: 150);

        void Check<TPixel>(byte primaries, IccProfile destination, double? peak, double white, ContentLightLevel? lightLevel, MasteringDisplayColorVolume? display, double expectedPeak)
            where TPixel : unmanaged
        {
            using var image = Build<TPixel>(Source4X3, Offset(Source4X3, 20));
            image.Metadata.Cicp = new CicpColorDescription(primaries, transfer, 9, fullRange: true);
            image.Metadata.ContentLightLevel = lightLevel;
            image.Metadata.MasteringDisplay = display;
            var before = Enumerable.Range(0, image.Frames.Count).Select(i => ImageSnapshots.CaptureFrameRows(image.Frames[i])).ToArray();
            var options = new IccColorTransformOptions
            {
                BlackPointCompensation = false,
                ToneMapping = new HdrToneMappingOptions { Operator = toneMappingOperator, SourcePeakLuminance = peak, TargetWhiteLuminance = white },
            };
            image.ConvertColorProfile(destination, options, Ct);
            Assert.Same(destination, image.Metadata.IccProfile);
            Assert.Null(image.Metadata.Cicp);
            Assert.Null(image.Metadata.ContentLightLevel);
            Assert.Null(image.Metadata.MasteringDisplay);

            var isGray = PixelFormats.IsGrayscale(image.PixelFormat);
            var reference = ReferenceIccTransform.CreateFromCicp(primaries, transfer, isGray, destination.Data.Span, toneMap: new ReferenceHdrToneMap((int)toneMappingOperator, (decimal)expectedPeak, (decimal)white));
            for (var i = 0; i < before.Length; i++)
            {
                var source = ColorSamplesOf(before[i]);
                var actual = ColorSamplesOf(ImageSnapshots.CaptureFrameRows(image.Frames[i]));
                Assert.Empty(PixelFormats.GetBitsPerComponent(image.PixelFormat) == 16
                    ? reference.Compare(source.Select(value => (ushort)value).ToArray(), actual.Select(value => (ushort)value).ToArray())
                    : reference.Compare(source.Select(value => (byte)value).ToArray(), actual.Select(value => (byte)value).ToArray()));
            }
        }
    }

    [Fact]
    public void ConvertColorProfileUsesTheCicpTagOfAHighDynamicRangeProfile()
    {
        // A profile of PQ or HLG samples only approximates their function: the samples are converted from the color space
        // of its cicp tag, as when the image has that CICP description and no profile. The tag of other samples is not used.
        var options = new IccColorTransformOptions { BlackPointCompensation = false, ToneMapping = new HdrToneMappingOptions { SourcePeakLuminance = 1000 } };
        var approximation = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.Gamma(512));
        foreach (var transfer in new byte[] { 16, 18 })
        {
            using var described = Build<Rgba64>(Source4X3);
            described.Metadata.Cicp = new CicpColorDescription(9, transfer, 0, fullRange: true);
            described.ConvertColorProfile(IccProfile.Srgb, options, Ct);

            using var tagged = Build<Rgba64>(Source4X3);
            tagged.Metadata.IccProfile = IccTestProfiles.WithTag(approximation, "cicp", IccTestProfiles.CicpTag(9, transfer, 0, 1));
            tagged.ConvertColorProfile(IccProfile.Srgb, options, Ct);
            Assert.Equal(Pixels(described), Pixels(tagged));
            Assert.Same(IccProfile.Srgb, tagged.Metadata.IccProfile);

            // Without tone mapping, the samples are refused like the ones of a CICP description
            using var refused = Build<Rgba64>(Source4X3);
            refused.Metadata.IccProfile = IccTestProfiles.WithTag(approximation, "cicp", IccTestProfiles.CicpTag(9, transfer, 0, 1));
            Assert.Throws<UnsupportedImageFeatureException>(() => refused.ConvertColorProfile(IccProfile.Srgb, new IccColorTransformOptions(), Ct));
        }

        using var standard = Build<Rgba64>(Source4X3);
        standard.Metadata.IccProfile = approximation;
        standard.ConvertColorProfile(IccProfile.Srgb, options, Ct);
        using var standardTagged = Build<Rgba64>(Source4X3);
        standardTagged.Metadata.IccProfile = IccTestProfiles.WithTag(approximation, "cicp", IccTestProfiles.CicpTag(12, 1, 0, 1));
        standardTagged.ConvertColorProfile(IccProfile.Srgb, options, Ct);
        Assert.Equal(Pixels(standard), Pixels(standardTagged));

        static Rgba64[] Pixels(Image<Rgba64> image)
        {
            var pixels = new Rgba64[image.Width * image.Height];
            image.Frames[0].CopyPixelDataTo(pixels);
            return pixels;
        }
    }

    public static TheoryData<byte, HdrToneMappingOperator> ToneMappingCases
    {
        get
        {
            var data = new TheoryData<byte, HdrToneMappingOperator>();
            foreach (var transfer in new byte[] { 16, 18 })
            {
                foreach (var toneMappingOperator in Enum.GetValues<HdrToneMappingOperator>())
                {
                    data.Add(transfer, toneMappingOperator);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// The light that a PQ sample encodes and what each curve makes of it, for a peak of 1,000 cd/m2 and a white of 203
    /// cd/m2: the samples are <c>65535 PQ(L / 10000)</c> for L = 10, 50, 100, 203, 400, 1000 and 4000 cd/m2, and the
    /// expected linear samples <c>65535 f(L' / 203)</c>, where L' is the light of the rounded sample, were computed with
    /// the <c>decimal</c> module of Python at 50 digits from SMPTE ST 2084 and from the formulas that
    /// <see cref="HdrToneMappingOperator"/> documents. Below the white, the clip keeps the light: 100 cd/m2 is 100/203
    /// of the white. The conversion to the linear profile adds less than two units.
    /// </summary>
    [Theory]
    [InlineData(HdrToneMappingOperator.Bt2390, new[] { 3228, 16142, 32077, 51342, 62504, 65535, 65535 })]
    [InlineData(HdrToneMappingOperator.Clip, new[] { 3228, 16142, 32284, 65531, 65535, 65535, 65535 })]
    [InlineData(HdrToneMappingOperator.Reinhard, new[] { 3083, 13083, 22068, 34117, 47003, 65535, 65535 })]
    [InlineData(HdrToneMappingOperator.Hable, new[] { 1585, 7607, 14288, 25613, 41110, 65535, 65535 })]
    [InlineData(HdrToneMappingOperator.Mobius, new[] { 3228, 16142, 29894, 44474, 55847, 65535, 65535 })]
    public void ToneMappingCurvesGiveTheLightOfTheirDefinition(HdrToneMappingOperator toneMappingOperator, int[] expected)
    {
        ushort[] samples = [19641, 28854, 33297, 38055, 42767, 49271, 59150];
        using var image = new Image<Rgba64>(samples.Length, 1);
        for (var i = 0; i < samples.Length; i++)
        {
            image.Frames[0][i, 0] = new Rgba64(samples[i], samples[i], samples[i], 65535);
        }

        image.Metadata.Cicp = new CicpColorDescription(1, 16, 0, fullRange: true);
        image.ConvertColorProfile(BuiltInIccProfiles.LinearSrgb, new IccColorTransformOptions { BlackPointCompensation = false, ToneMapping = new HdrToneMappingOptions { Operator = toneMappingOperator, SourcePeakLuminance = 1000 } }, Ct);
        for (var i = 0; i < samples.Length; i++)
        {
            var pixel = image.Frames[0][i, 0];
            Assert.True(Math.Abs(pixel.R - expected[i]) <= 2 && Math.Abs(pixel.G - expected[i]) <= 2 && Math.Abs(pixel.B - expected[i]) <= 2, $"{toneMappingOperator}, sample {samples[i]}: expected {expected[i]}, actual {pixel}.");
        }
    }

    [Fact]
    public void ToneMappingKeepsTheHueOfAColorAndNeverBrightens()
    {
        // The three components are scaled together: a saturated highlight keeps the ratio of its components (in linear
        // light), instead of clipping one of them
        using var image = new Image<Rgba64>(1, 1);
        image.Frames[0][0, 0] = new Rgba64(59150, 49271, 38055, 65535); // 4000, 1000 and 203 cd/m2
        image.Metadata.Cicp = new CicpColorDescription(1, 16, 0, fullRange: true);
        image.ConvertColorProfile(BuiltInIccProfiles.LinearSrgb, new IccColorTransformOptions { BlackPointCompensation = false, ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Clip, SourcePeakLuminance = 4000 } }, Ct);
        var pixel = image.Frames[0][0, 0];
        Assert.True(Math.Abs(pixel.R - 65535) <= 2 && Math.Abs(pixel.G - (65535 / 4)) <= 40 && Math.Abs(pixel.B - (65535 * 203 / 4000)) <= 40, $"Actual {pixel}.");

        // A source whose peak is below the white is not compressed, and not brightened: 100 cd/m2 stays 100/203 of the white
        foreach (var toneMappingOperator in Enum.GetValues<HdrToneMappingOperator>())
        {
            using var dim = new Image<Rgba64>(1, 1);
            dim.Frames[0][0, 0] = new Rgba64(33297, 33297, 33297, 65535);
            dim.Metadata.Cicp = new CicpColorDescription(1, 16, 0, fullRange: true);
            dim.ConvertColorProfile(BuiltInIccProfiles.LinearSrgb, new IccColorTransformOptions { BlackPointCompensation = false, ToneMapping = new HdrToneMappingOptions { Operator = toneMappingOperator, SourcePeakLuminance = 150 } }, Ct);
            Assert.True(toneMappingOperator is HdrToneMappingOperator.Hable or HdrToneMappingOperator.Reinhard || Math.Abs(dim.Frames[0][0, 0].R - 32284) <= 2, $"{toneMappingOperator}: {dim.Frames[0][0, 0]}.");
        }
    }

    [Fact]
    public void ToneMappingOptionsValidateTheirValues()
    {
        var options = HdrToneMappingOptions.Default;
        Assert.Equal((HdrToneMappingOperator.Bt2390, null, 203d), (options.Operator, options.SourcePeakLuminance, options.TargetWhiteLuminance));
        Assert.Null(IccColorTransformOptions.Default.ToneMapping);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new HdrToneMappingOptions { Operator = (HdrToneMappingOperator)5 });
        foreach (var value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, 10000.5 })
        {
            Assert.Throws<ArgumentOutOfRangeException>("value", () => new HdrToneMappingOptions { SourcePeakLuminance = value });
            Assert.Throws<ArgumentOutOfRangeException>("value", () => new HdrToneMappingOptions { TargetWhiteLuminance = value });
        }

        // Tone mapping is for high dynamic range samples only: it changes nothing for the other sources
        using var expected = Build<Rgba64>(Source4X3);
        using var actual = Build<Rgba64>(Source4X3);
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        expected.ConvertColorProfile(displayP3, cancellationToken: Ct);
        actual.ConvertColorProfile(displayP3, new IccColorTransformOptions { ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Hable } }, Ct);
        for (var x = 0; x < expected.Width; x++)
        {
            Assert.Equal(expected.Frames[0][x, 0], actual.Frames[0][x, 0]);
        }
    }

    private static int[] ColorSamplesOf(RawPixelBuffer buffer)
    {
        var samples = new List<int>();
        for (var pixel = 0; pixel < buffer.Width * buffer.Height; pixel++)
        {
            for (var channel = 0; channel < buffer.Layout.ChannelCount; channel++)
            {
                if (!buffer.Layout.HasAlpha || channel != buffer.Layout.AlphaChannel)
                    samples.Add(buffer.GetSample(pixel % buffer.Width, pixel / buffer.Width, channel));
            }
        }

        return [.. samples];
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
    public void ConvertColorProfileKeepsCursorHotspots()
    {
        // Colors change, pixels do not move: the hotspot of every frame and of the poster stays where it is
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        using var image = Build<Rgba32>(Source3X2, Offset(Source3X2, 10));
        image.SetPosterFrame(image.Frames[1]);
        image.Frames[0].Metadata.Hotspot = new Point(2, 1);
        image.Frames[1].Metadata.Hotspot = new Point(0, 0);
        image.PosterFrame!.Metadata.Hotspot = new Point(1, 1);
        image.ConvertColorProfile(displayP3, cancellationToken: Ct);
        Assert.Same(displayP3, image.Metadata.IccProfile);
        Assert.Equal(new Point(2, 1), image.Frames[0].Metadata.Hotspot);
        Assert.Equal(new Point(0, 0), image.Frames[1].Metadata.Hotspot);
        Assert.Equal(new Point(1, 1), image.PosterFrame.Metadata.Hotspot);
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

    [Theory]
    [InlineData(0, 3, 3)] // a plain crop to the 3x3 content
    [InlineData(2, 7, 7)] // the padding reaches one pixel outside the 5x5 canvas on every side
    public void AutoCropUpdatesDimensionTags(int padding, ushort expectedWidth, ushort expectedHeight)
    {
        // Ids 1 and 60 differ by 59 on every color channel: 59 * 10000 is above the default tolerance of 35 * 10000
        using var image = Build<Rgba32>([[1, 1, 1, 1, 1], [1, 60, 60, 60, 1], [1, 60, 60, 60, 1], [1, 60, 60, 60, 1], [1, 1, 1, 1, 1]]);
        image.Metadata.ExifProfile = CreateExif(orientation: 6, width: 5, height: 5);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = padding, PaddingY = padding }, Ct));
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.Equal(((uint)expectedWidth, (uint)expectedHeight), ReadIfd0Dimensions(exif));
        Assert.Equal(((uint?)expectedWidth, (uint?)expectedHeight), ExifTiff.ReadPixelDimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
    }

    [Fact]
    public void AnalyzeAutoCropKeepsTheThumbnail()
    {
        using var image = Build<Rgba32>(Source4X3);
        var profile = CreateExif(orientation: 1, width: 4, height: 3);
        image.Metadata.ExifProfile = profile;
        _ = image.AnalyzeAutoCrop(cancellationToken: Ct);
        Assert.Same(profile, image.Metadata.ExifProfile);
        Assert.True(ExifTiff.HasThumbnail(image.Metadata.ExifProfile!.Data.Span));
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

    // -----------------------------------------------------------------------------------------------------------------
    // Effects: pixelation, oil painting, vignette, glow and flattening. The references below are written from the
    // definitions of the operations, one pixel at a time, in decimal for the integer formats.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PixelateGivesEveryBlockTheMeanOfItsPixels()
    {
        // A 3x2 image in blocks of 2: the block of the first two columns averages premultiplied colors, so the hidden white
        // of the transparent pixel takes no part. Alpha: (255 + 255 + 0 + 128) / 4 = 159.5 -> 160; red: 100 * 255 / 638 = 39.97;
        // blue: 200 * 128 / 638 = 40.13. The last column is a block of two pixels
        using var image = new Image<Rgba32>(3, 2);
        image.Frames[0][0, 0] = new Rgba32(100, 0, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(0, 100, 0, 255);
        image.Frames[0][0, 1] = new Rgba32(255, 255, 255, 0);
        image.Frames[0][1, 1] = new Rgba32(0, 0, 200, 128);
        image.Frames[0][2, 0] = new Rgba32(10, 20, 30, 255);
        image.Frames[0][2, 1] = new Rgba32(30, 40, 50, 255);
        image.Pixelate(2, Ct);
        var block = new Rgba32(40, 40, 40, 160);
        Assert.Equal([block, block, new Rgba32(20, 30, 40, 255), block, block, new Rgba32(20, 30, 40, 255)], ReadRgba32(image.Frames[0]));

        // A block larger than the image is the whole image
        using var gray = BuildGray8([0, 10, 20], [30, 40, 51]);
        gray.Pixelate(100, Ct);
        Assert.Equal([25, 25, 25, 25, 25, 25], DrawingTestSupport.ReadSamples(gray.Frames[0]));
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void PixelateMatchesTheReference(PixelFormat format)
    {
        var random = new Random(31);
        foreach (var size in new[] { 2, 3, 5 })
        {
            using var image = DrawingTestSupport.CreateRandom(format, 11, 7, random.Next);
            var source = ReadValues(image.Frames[0]);
            image.Pixelate(size, Ct);
            var expected = new double[source.Length];
            var channels = PixelFormats.GetComponentCount(format);
            for (var top = 0; top < 7; top += size)
            {
                for (var left = 0; left < 11; left += size)
                {
                    var pixels = new List<int>();
                    for (var y = top; y < Math.Min(7, top + size); y++)
                    {
                        for (var x = left; x < Math.Min(11, left + size); x++)
                        {
                            pixels.Add(((y * 11) + x) * channels);
                        }
                    }

                    var mean = ReferenceMean(format, source, pixels);
                    foreach (var pixel in pixels)
                    {
                        mean.CopyTo(expected, pixel);
                    }
                }
            }

            AssertValues(format, expected, ReadValues(image.Frames[0]), string.Create(CultureInfo.InvariantCulture, $"size {size}"));
        }
    }

    [Fact]
    public void OilPaintGivesEveryPixelTheMeanOfTheMostFrequentLevelOfItsNeighborhood()
    {
        // Two levels: 0 and 10 are in the first one, 200, 210 and 220 in the second (200 / 255 * 2 = 1.57). The windows of three
        // pixels stop at the edges: { 0, 10 } -> 5, { 0, 10, 200 } -> 5, { 10, 200, 210 } -> 205, { 200, 210, 220 } -> 210,
        // { 210, 220 } -> 215
        using var image = BuildGray8([0, 10, 200, 210, 220]);
        image.OilPaint(levels: 2, radius: 1, Ct);
        Assert.Equal([5, 5, 205, 210, 215], DrawingTestSupport.ReadSamples(image.Frames[0]));

        // The intensity is the luma of the color over black: a transparent pixel is in the first level whatever its hidden
        // color, and a tie goes to the lowest level. { transparent white, opaque white }: one pixel in each level -> the
        // transparent one, so transparent black
        using var alpha = new Image<Rgba32>(2, 1);
        alpha.Frames[0][0, 0] = new Rgba32(255, 255, 255, 0);
        alpha.Frames[0][1, 0] = new Rgba32(255, 255, 255, 255);
        alpha.OilPaint(levels: 2, radius: 1, Ct);
        Assert.Equal([default, default], ReadRgba32(alpha.Frames[0]));
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void OilPaintMatchesTheReference(PixelFormat format)
    {
        var random = new Random(47);
        foreach (var (levels, radius) in new[] { (6, 2), (1, 1), (256, 1), (3, 4) })
        {
            using var image = DrawingTestSupport.CreateRandom(format, 13, 9, random.Next);
            var source = ReadValues(image.Frames[0]);
            image.OilPaint(levels, radius, Ct);
            var channels = PixelFormats.GetComponentCount(format);
            var (colors, alpha) = GetSampleLayout(format);
            var max = MaxValue(format);
            var (red, blue) = format == PixelFormat.Bgra32 ? (2, 0) : (0, 2);
            var pixelLevels = new int[13 * 9];
            for (var i = 0; i < pixelLevels.Length; i++)
            {
                var pixel = i * channels;
                var a = alpha >= 0 ? source[pixel + alpha] / max : 1;

                // A gray over black is the product of the gray and alpha (black for an alpha mask)
                var intensity = colors switch
                {
                    0 => 0,
                    1 => alpha >= 0 ? source[pixel] * source[pixel + alpha] / ((double)max * max) : source[pixel] / max,
                    _ => ((0.2126 * source[pixel + red]) + (0.7152 * source[pixel + 1]) + (0.0722 * source[pixel + blue])) * a / max,
                };
                pixelLevels[i] = Math.Clamp((int)Math.Floor(intensity * levels), 0, levels - 1);
            }

            var expected = new double[source.Length];
            for (var y = 0; y < 9; y++)
            {
                for (var x = 0; x < 13; x++)
                {
                    var window = new List<int>();
                    for (var v = Math.Max(0, y - radius); v <= Math.Min(8, y + radius); v++)
                    {
                        for (var u = Math.Max(0, x - radius); u <= Math.Min(12, x + radius); u++)
                        {
                            window.Add((v * 13) + u);
                        }
                    }

                    var best = window.GroupBy(i => pixelLevels[i]).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
                    ReferenceMean(format, source, [.. window.Where(i => pixelLevels[i] == best).Select(i => i * channels)]).CopyTo(expected, ((y * 13) + x) * channels);
                }
            }

            AssertValues(format, expected, ReadValues(image.Frames[0]), string.Create(CultureInfo.InvariantCulture, $"{levels} levels, radius {radius}"));
        }
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void VignetteAndGlowMatchTheReference(PixelFormat format)
    {
        // At the center of a pixel whose distance to the center of the rectangle is the fraction t of the ellipse, a
        // vignette composites its color with the alpha t A (1 at the ellipse and beyond), a glow with (1 - t) A (nothing
        // beyond), on the color of the pixel, whose alpha is kept. Outside the rectangle nothing changes
        var gray = PixelFormats.IsGrayscale(format);
        Rgba64 color = gray ? new Rgba32(40, 40, 40, 230) : new Rgba32(20, 60, 200, 230);
        var random = new Random(53);
        foreach (var (vignette, rectangle, radiusX, radiusY) in new[] { (true, (Rectangle?)null, 9.0, 6.0), (false, null, 12.0, 5.5), (true, new Rectangle(3, 2, 10, 6), 4.0, 2.5), (false, new Rectangle(-4, 1, 12, 9), 7.0, 7.0) })
        {
            using var image = DrawingTestSupport.CreateRandom(format, 17, 11, random.Next);
            var source = ReadValues(image.Frames[0]);
            if (vignette)
            {
                image.Vignette(color, radiusX, radiusY, rectangle, Ct);
            }
            else
            {
                image.Glow(color, radiusX, radiusY, rectangle, Ct);
            }

            var actual = ReadValues(image.Frames[0]);
            var area = rectangle ?? new Rectangle(0, 0, 17, 11);
            var channels = PixelFormats.GetComponentCount(format);
            var (colors, alpha) = GetSampleLayout(format);
            var max = MaxValue(format);
            var tolerance = PixelFormats.IsFloatingPoint(format) ? (format == PixelFormat.RgbaFloat ? 1e-4 : 2e-3) : max == 255 ? 1 : 2;
            var paint = format == PixelFormat.Bgra32 ? new[] { color.B, color.G, color.R } : [color.R, color.G, color.B];
            for (var y = 0; y < 11; y++)
            {
                for (var x = 0; x < 17; x++)
                {
                    var pixel = ((y * 17) + x) * channels;
                    var context = string.Create(CultureInfo.InvariantCulture, $"{(vignette ? "vignette" : "glow")} {area}, pixel ({x}, {y})");
                    if (x < area.X || x >= area.Right || y < area.Y || y >= area.Bottom)
                    {
                        Assert.True(source.AsSpan(pixel, channels).SequenceEqual(actual.AsSpan(pixel, channels)), context);
                        continue;
                    }

                    var dx = (x + 0.5 - (area.X + (area.Width / 2.0))) / radiusX;
                    var dy = (y + 0.5 - (area.Y + (area.Height / 2.0))) / radiusY;
                    var t = Math.Min(1, Math.Sqrt((dx * dx) + (dy * dy)));
                    var coverage = (vignette ? t : 1 - t) * color.A / 65535;
                    if (alpha >= 0)
                    {
                        Assert.Equal(source[pixel + alpha], actual[pixel + alpha]);
                        if (source[pixel + alpha] == 0)
                            continue;
                    }

                    for (var c = 0; c < colors; c++)
                    {
                        var expected = (paint[c] / 65535.0 * max * coverage) + (source[pixel + c] * (1 - coverage));
                        Assert.True(Math.Abs(expected - actual[pixel + c]) <= tolerance, string.Create(CultureInfo.InvariantCulture, $"{context}, sample {c}: expected {expected}, actual {actual[pixel + c]}"));
                    }
                }
            }
        }
    }

    [Fact]
    public void FlattenCompositesOverTheBackgroundAndKeepsThePixelFormat()
    {
        // 8-bit: the pixels of the conversion to Rgb24 with the same background, bit for bit, with an opaque alpha
        var background = new Rgba32(250, 128, 3);
        var random = new Random(59);
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Bgra32 })
        {
            using var image = DrawingTestSupport.CreateRandom(format, 23, 9, random.Next);
            using var converted = image.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = background });
            image.Flatten(background, Ct);
            Assert.Equal(format, image.PixelFormat);
            using var flattened = image.CloneAs<Rgb24>();
            Assert.Equal(DrawingTestSupport.ReadSamples(converted.Frames[0]), DrawingTestSupport.ReadSamples(flattened.Frames[0]));
        }

        // 16-bit: (s a + bg (65535 - a) + 32767) / 65535: (1000 * 30000 + 64250 * 35535 + 32767) / 65535 = 35296.5 -> 35296
        using (var wide = new Image<Rgba64>(2, 1, new Rgba64(1000, 2000, 3000, 30000)))
        {
            wide.Frames[0][1, 0] = new Rgba64(1, 2, 3, 65535);
            wide.Flatten(new Rgba64(64250, 0, 65535), Ct);
            Assert.Equal(new Rgba64(35296, 916, 36908, 65535), wide.Frames[0][0, 0]);
            Assert.Equal(new Rgba64(1, 2, 3, 65535), wide.Frames[0][1, 0]);
        }

        // Floating point: c a + bg (1 - a), colors not clamped, alpha kept between 0 and 1
        using (var hdr = new Image<RgbaFloat>(3, 1, new RgbaFloat(4, 0.5f, -0.25f, 0.25f)))
        {
            hdr.Frames[0][1, 0] = new RgbaFloat(0.5f, 0.5f, 0.5f, 1.5f);
            hdr.Frames[0][2, 0] = new RgbaFloat(2, 2, 2, 1);
            hdr.Flatten(new Rgba64(0, 65535, 0), Ct);
            Assert.Equal(new RgbaFloat(1, 0.875f, -0.0625f, 1), hdr.Frames[0][0, 0]);
            Assert.Equal(new RgbaFloat(0.5f, 0.5f, 0.5f, 1), hdr.Frames[0][1, 0]);
            Assert.Equal(new RgbaFloat(2, 2, 2, 1), hdr.Frames[0][2, 0]);
        }

        using (var half = new Image<RgbaHalf>(1, 1, new RgbaHalf((Half)1, (Half)0, (Half)0, (Half)0.5)))
        {
            half.Flatten(new Rgba64(0, 0, 65535), Ct);
            Assert.Equal(new RgbaHalf((Half)0.5, (Half)0, (Half)0.5, Half.One), half.Frames[0][0, 0]);
        }

        // Gray with alpha: the pixels of the conversion to the gray format without alpha, the luma of the flattened colors
        using (var image = DrawingTestSupport.CreateRandom(PixelFormat.La16, 23, 9, random.Next))
        {
            CheckGrayFlatten<Gray8>(image, background);
        }

        using (var image = DrawingTestSupport.CreateRandom(PixelFormat.La32, 23, 9, random.Next))
        {
            CheckGrayFlatten<Gray16>(image, background);
        }

        // A format without alpha is already opaque
        using (var opaque = new Image<Rgb24>(1, 1, new Rgb24(1, 2, 3)))
        {
            opaque.Flatten(new Rgba32(9, 9, 9), Ct);
            Assert.Equal(new Rgb24(1, 2, 3), opaque.Frames[0][0, 0]);
        }

        using (var opaque = new Image<Rgb48>(1, 1, new Rgb48(1, 2, 3)))
        {
            opaque.Flatten(new Rgba32(9, 9, 9), Ct);
            Assert.Equal(new Rgb48(1, 2, 3), opaque.Frames[0][0, 0]);
        }

        // An alpha mask has no color to hold the background
        using (var mask = new Image<A8>(1, 1, new A8(4)))
        {
            Assert.Throws<NotSupportedException>(() => mask.Flatten(new Rgba32(9, 9, 9), Ct));
            Assert.Equal(new A8(4), mask.Frames[0][0, 0]);
        }

        using var rejected = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 4));
        Assert.Throws<ArgumentException>(() => rejected.Flatten(new Rgba32(9, 9, 9, 254)));
        Assert.Throws<ArgumentException>(() => rejected.Frames[0].Flatten(new Rgba64(9, 9, 9, 0)));
        Assert.Equal(new Rgba32(1, 2, 3, 4), rejected.Frames[0][0, 0]);

        static void CheckGrayFlatten<TGray>(Image image, Rgba64 background)
            where TGray : unmanaged
        {
            var format = image.PixelFormat;
            using var converted = image.CloneAs<TGray>(new PixelConversionOptions { BackgroundColor = background });
            image.Flatten(background, Ct);
            Assert.Equal(format, image.PixelFormat);
            using var flattened = image.CloneAs<TGray>();
            Assert.Equal(DrawingTestSupport.ReadSamples(converted.Frames[0]), DrawingTestSupport.ReadSamples(flattened.Frames[0]));
        }
    }

    [Fact]
    public void TheEffectsApplyToEveryFrameOrToOneAndValidateTheirArguments()
    {
        using var image = Build<Rgba32>([[1, 2, 3], [4, 5, 6]], [[7, 8, 9], [10, 11, 12]]);
        image.SetPosterFrame(image.Frames[0]);
        var effects = new (string Name, Action<Image> OnImage, Action<ImageFrame> OnFrame)[]
        {
            ("pixelate", image => image.Pixelate(2, Ct), frame => frame.Pixelate(2, Ct)),
            ("oil paint", image => image.OilPaint(4, 1, Ct), frame => frame.OilPaint(4, 1, Ct)),
            ("vignette", image => image.Vignette(new Rgba32(0, 0, 0), 1, 1, cancellationToken: Ct), frame => frame.Vignette(new Rgba32(0, 0, 0), 1, 1, cancellationToken: Ct)),
            ("glow", image => image.Glow(new Rgba32(255, 255, 255), 2, 1, cancellationToken: Ct), frame => frame.Glow(new Rgba32(255, 255, 255), 2, 1, cancellationToken: Ct)),
            ("flatten", image => image.Flatten(new Rgba32(0, 0, 0), Ct), frame => frame.Flatten(new Rgba32(0, 0, 0), Ct)),
        };

        foreach (var (name, onImage, onFrame) in effects)
        {
            WriteTranslucent(image);
            var before = image.Frames.Append(image.PosterFrame!).Select(DrawingTestSupport.ReadSamples).ToArray();
            onImage(image);
            var after = image.Frames.Append(image.PosterFrame!).Select(DrawingTestSupport.ReadSamples).ToArray();
            for (var i = 0; i < before.Length; i++)
            {
                Assert.False(before[i].SequenceEqual(after[i]), $"{name}: frame {i} is unchanged.");
            }

            WriteTranslucent(image);
            onFrame(image.Frames[1]);
            Assert.Equal(before[0], DrawingTestSupport.ReadSamples(image.Frames[0]));
            Assert.False(before[1].SequenceEqual(DrawingTestSupport.ReadSamples(image.Frames[1])), $"{name}: the frame is unchanged.");
            Assert.Equal(0, image.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
            Assert.Equal(0, image.Owner.ActiveLeaseCount);

            image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => onImage(image)));
            Assert.Throws<OperationCanceledException>(() => EffectWithCanceledToken(name, image));
        }

        // A block of one pixel, a radius of 0: nothing changes
        WriteTranslucent(image);
        var unchanged = DrawingTestSupport.ReadSamples(image.Frames[0]);
        image.Pixelate(1, Ct);
        image.OilPaint(10, 0, Ct);
        image.Frames[0].Pixelate(1, Ct);
        image.Frames[0].OilPaint(10, 0, Ct);
        Assert.Equal(unchanged, DrawingTestSupport.ReadSamples(image.Frames[0]));

        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Pixelate(2));
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).OilPaint(2, 1));
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Vignette(default, 1, 1));
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).Glow(default, 1, 1));
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Flatten(new Rgba32(0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Pixelate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].Pixelate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.OilPaint(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.OilPaint(257, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.OilPaint(2, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].OilPaint(2, 501));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Vignette(default, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Vignette(default, 1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Glow(default, double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Frames[0].Glow(default, 1, 16777217));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Vignette(default, 1, 1, new Rectangle(0, 0, 0, 1)));
        Assert.Equal(unchanged, DrawingTestSupport.ReadSamples(image.Frames[0]));

        var disposed = new Image<Rgba32>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.Pixelate(2));
        Assert.Throws<ObjectDisposedException>(() => disposed.OilPaint(2, 1));
        Assert.Throws<ObjectDisposedException>(() => disposed.Vignette(default, 1, 1));
        Assert.Throws<ObjectDisposedException>(() => disposed.Flatten(new Rgba32(0, 0, 0)));
        Assert.Throws<ObjectDisposedException>(() => frame.Pixelate(2));
        Assert.Throws<ObjectDisposedException>(() => frame.OilPaint(2, 1));
        Assert.Throws<ObjectDisposedException>(() => frame.Glow(default, 1, 1));
        Assert.Throws<ObjectDisposedException>(() => frame.Flatten(new Rgba32(0, 0, 0)));

        static void WriteTranslucent(Image<Rgba32> image)
        {
            var frames = image.Frames.Append(image.PosterFrame!).Distinct().ToArray();
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = (ImageFrame<Rgba32>)frames[i];
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 3; x++)
                    {
                        frame[x, y] = new Rgba32((byte)(40 * x), (byte)(100 + (50 * y)), (byte)(30 * i), (byte)(60 + (60 * x) + (20 * y)));
                    }
                }
            }
        }

        static void EffectWithCanceledToken(string name, Image image)
        {
            var canceled = new CancellationToken(canceled: true);
            switch (name)
            {
                case "pixelate":
                    image.Pixelate(2, canceled);
                    break;
                case "oil paint":
                    image.OilPaint(4, 1, canceled);
                    break;
                case "vignette":
                    image.Vignette(new Rgba32(0, 0, 0), 1, 1, cancellationToken: canceled);
                    break;
                case "glow":
                    image.Glow(new Rgba32(0, 0, 0), 1, 1, cancellationToken: canceled);
                    break;
                default:
                    image.Flatten(new Rgba32(0, 0, 0), canceled);
                    break;
            }
        }
    }

    /// <summary>The samples of a frame as numbers: integer samples, or the values of floating-point ones.</summary>
    // -----------------------------------------------------------------------------------------------------------------
    // Interoperability formats: every operation gives the result of the working format, stored back in the format
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<PixelFormat, string> InteropOperations
    {
        get
        {
            var data = new TheoryData<PixelFormat, string>();
            foreach (var format in PixelFormats.All.Where(DrawingTestSupport.IsInteropFormat))
            {
                foreach (var operation in InteropOperationsByName.Keys)
                {
                    data.Add(format, operation);
                }
            }

            return data;
        }
    }

    private static Dictionary<string, Action<Image>> InteropOperationsByName { get; } = new(StringComparer.Ordinal)
    {
        ["Resize"] = image => image.Resize(new ResizeOptions(7, 5), Ct),
        ["Rotate"] = image => image.Rotate(17, new TransformOptions { BackgroundColor = new Rgba64(0, 0, 65535) }, Ct),
        ["GaussianBlur"] = image => image.GaussianBlur(1.5, cancellationToken: Ct),
        ["BoxBlur"] = image => image.BoxBlur(2, cancellationToken: Ct),
        ["MedianBlur"] = image => image.MedianBlur(1, cancellationToken: Ct),
        ["Sharpen"] = image => image.Sharpen(1, 1.5, cancellationToken: Ct),
        ["DetectEdges"] = image => image.DetectEdges(EdgeDetector.Sobel, grayscale: false, cancellationToken: Ct),
        ["Grayscale"] = image => image.Grayscale(Ct),
        ["Sepia"] = image => image.ApplyColorMatrix(ColorMatrix.CreateSepia(), Ct),
        ["Opacity"] = image => image.ApplyColorMatrix(ColorMatrix.CreateOpacity(0.5), Ct),
        ["AutoLevels"] = image => image.AutoLevels(cancellationToken: Ct),
        ["Equalize"] = image => image.Equalize(cancellationToken: Ct),
        ["Threshold"] = image => image.Threshold(cancellationToken: Ct),
        ["Pixelate"] = image => image.Pixelate(3, Ct),
        ["OilPaint"] = image => image.OilPaint(5, 2, Ct),
        ["Flatten"] = image => image.Flatten(new Rgba64(65535, 32768, 0), Ct),
        ["Vignette"] = image => image.Vignette(new Rgba32(20, 60, 200, 230), 6, 4, cancellationToken: Ct),
        ["ConvertColorProfile"] = image => image.ConvertColorProfile(IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve()), cancellationToken: Ct),
        ["AutoCrop"] = image => image.AutoCrop(cancellationToken: Ct),
        ["Fill"] = image => image.Draw(canvas => canvas.FillRectangle(new SolidBrush(new Rgba32(200, 30, 90, 160)), new RectangleD(1.5, 2.25, 8, 5)), Ct),
        ["Layer"] = image => image.Draw(canvas => canvas.Layer(0.5, layer => layer.FillRectangle(new SolidBrush(new Rgba32(10, 220, 90)), new RectangleD(0.5, 0.5, 6, 4))), Ct),
    };

    [Theory]
    [MemberData(nameof(InteropOperations))]
    public void InteropFormatsProcessLikeTheirWorkingFormat(PixelFormat format, string operation)
    {
        var random = new Random(61);
        using var image = DrawingTestSupport.CreateRandom(format, 13, 9, random.Next);
        using var working = CloneAs(image, DrawingTestSupport.GetWorkingFormat(format));
        InteropOperationsByName[operation](image);
        InteropOperationsByName[operation](working);
        using var expected = CloneAs(working, format);
        Assert.Equal(format, image.PixelFormat);
        Assert.Equal(expected.Size, image.Size);
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.InteropFormats), MemberType = typeof(DrawingTestSupport))]
    public void InteropImagesAreDrawnLikeTheColorsTheyStore(PixelFormat format)
    {
        // A source is read as 16-bit colors: the byte orders as Rgba32, a packed component as its nearest 16-bit sample
        var random = new Random(67);
        using var source = DrawingTestSupport.CreateRandom(format, 6, 5, random.Next);
        using var workingSource = CloneAs(source, format is PixelFormat.Bgr565 or PixelFormat.Rgba1010102 ? PixelFormat.Rgba64 : DrawingTestSupport.GetWorkingFormat(format));
        using var canvas = DrawingTestSupport.CreateRandom(PixelFormat.Rgba64, 9, 8, random.Next);
        using var expected = canvas.CloneAs<Rgba64>();
        canvas.Draw(drawing => drawing.DrawImage(source.Frames[0], new RectangleD(1.25, 0.5, 7, 6), opacity: 0.75), Ct);
        expected.Draw(drawing => drawing.DrawImage(workingSource.Frames[0], new RectangleD(1.25, 0.5, 7, 6), opacity: 0.75), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(canvas.Frames[0]));
    }

    private static Image CloneAs(Image image, PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 => image.CloneAs<Rgba32>(),
        PixelFormat.Rgb24 => image.CloneAs<Rgb24>(),
        PixelFormat.Rgba64 => image.CloneAs<Rgba64>(),
        PixelFormat.Argb32 => image.CloneAs<Argb32>(),
        PixelFormat.Abgr32 => image.CloneAs<Abgr32>(),
        PixelFormat.Bgr565 => image.CloneAs<Bgr565>(),
        PixelFormat.Rgba1010102 => image.CloneAs<Rgba1010102>(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an interoperability format or the working format of one."),
    };

    private static double[] ReadValues(ImageFrame frame)
    {
        var samples = DrawingTestSupport.ReadSamples(frame);
        return frame.PixelFormat switch
        {
            PixelFormat.RgbaFloat => [.. samples.Select(bits => (double)BitConverter.Int32BitsToSingle(bits))],
            PixelFormat.RgbaHalf => [.. samples.Select(bits => (double)BitConverter.UInt16BitsToHalf((ushort)bits))],
            _ => [.. samples.Select(sample => (double)sample)],
        };
    }

    /// <summary>The number of color samples of a pixel and the index of its alpha sample (-1 without alpha).</summary>
    private static (int Colors, int Alpha) GetSampleLayout(PixelFormat format)
    {
        if (PixelFormats.IsFloatingPoint(format))
            return (3, 3);

        var layout = ReferencePixelLayout.FromPixelFormat(format);
        return (layout.ColorChannels, layout.HasAlpha ? layout.ColorChannels : -1);
    }

    private static int MaxValue(PixelFormat format) => PixelFormats.IsFloatingPoint(format) ? 1 : PixelFormats.GetBitsPerComponent(format) == 16 ? 65535 : 255;

    /// <summary>
    /// The alpha-aware mean of pixels (the indexes of their first samples): the colors premultiplied by alpha, then divided by
    /// the mean alpha, rounded half up (exactly, in decimal) for the integer formats; transparent black when the alpha rounds to 0.
    /// </summary>
    private static double[] ReferenceMean(PixelFormat format, double[] samples, List<int> pixels)
    {
        var channels = PixelFormats.GetComponentCount(format);
        var (colors, alphaIndex) = GetSampleLayout(format);
        var result = new double[channels];
        if (PixelFormats.IsFloatingPoint(format))
        {
            double alphaSum = 0;
            var colorSums = new double[3];
            foreach (var pixel in pixels)
            {
                var a = samples[pixel + 3];
                alphaSum += a;
                for (var c = 0; c < 3; c++)
                {
                    colorSums[c] += samples[pixel + c] * a;
                }
            }

            var alpha = Math.Clamp(alphaSum / pixels.Count, 0, 1);
            if (format == PixelFormat.RgbaHalf ? (Half)alpha == Half.Zero : (float)alpha == 0)
                return result;

            for (var c = 0; c < 3; c++)
            {
                result[c] = colorSums[c] / alphaSum;
            }

            result[3] = alpha;
            return result;
        }

        var max = MaxValue(format);
        if (alphaIndex < 0)
        {
            for (var c = 0; c < channels; c++)
            {
                result[c] = RoundHalfUp(pixels.Sum(pixel => (decimal)samples[pixel + c]) / pixels.Count, max);
            }

            return result;
        }

        var sumOfAlpha = pixels.Sum(pixel => (decimal)samples[pixel + alphaIndex]);
        var roundedAlpha = RoundHalfUp(sumOfAlpha / pixels.Count, max);
        if (roundedAlpha == 0)
            return result;

        for (var c = 0; c < colors; c++)
        {
            result[c] = RoundHalfUp(pixels.Sum(pixel => (decimal)samples[pixel + c] * (decimal)samples[pixel + alphaIndex]) / sumOfAlpha, max);
        }

        result[alphaIndex] = roundedAlpha;
        return result;

        static int RoundHalfUp(decimal value, int max) => (int)Math.Clamp(Math.Floor(value + 0.5m), 0, max);
    }

    private static void AssertValues(PixelFormat format, double[] expected, double[] actual, string context)
    {
        Assert.HasCount(expected.Length, actual);
        var tolerance = format switch
        {
            PixelFormat.RgbaFloat => 1e-6,
            PixelFormat.RgbaHalf => 1e-3,
            _ => 0,
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, string.Create(CultureInfo.InvariantCulture, $"{format}, {context}: sample {i} is {actual[i]}, expected {expected[i]}."));
        }
    }

    private static Image<Gray8> BuildGray8(params int[][] rows)
    {
        var image = new Image<Gray8>(rows[0].Length, rows.Length);
        DrawingTestSupport.WriteSamples(image.Frames[0], [.. rows.SelectMany(row => row)]);
        return image;
    }

    private static Rgba32[] ReadRgba32(ImageFrame<Rgba32> frame)
    {
        var pixels = new Rgba32[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

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

    /// <summary>
    /// Every geometry operation on a 3x2 image of special floating-point pixels (not-a-number payloads of both signs, signed
    /// zeros, infinities, subnormals, the largest values, alpha outside [0, 1] or not a number): each output pixel has the bits
    /// of the source pixel the permutation designates.
    /// </summary>
    private static void CheckQuarterTurns<TPixel>()
        where TPixel : unmanaged
    {
        (double Degrees, int[][] Expected)[] cases = [(90, Rotated90), (-270, Rotated90), (450, Rotated90), (180, Rotated180), (-180, Rotated180), (270, Rotated270), (-90, Rotated270)];
        foreach (var (degrees, expected) in cases)
        {
            foreach (var filter in Enum.GetValues<ResamplingFilter>())
            {
                // Two frames and a poster, and the same result as RotateMode, hotspot included
                using var image = Build<TPixel>(Source3X2, Offset(Source3X2, 10));
                image.SetPosterFrame(image.Frames[1]);
                image.Frames[0].Metadata.Hotspot = new Point(2, 0);
                using var reference = Build<TPixel>(Source3X2);
                reference.Frames[0].Metadata.Hotspot = new Point(2, 0);
                reference.Rotate(ReferenceRotateMode(degrees), Ct);

                image.Rotate(degrees, new TransformOptions { Filter = filter }, Ct);
                AssertFrame(image.Frames[0], expected);
                AssertFrame(image.Frames[1], expected, idOffset: 10);
                AssertFrame(image.PosterFrame!, expected, idOffset: 10);
                Assert.Equal(reference.Frames[0].Metadata.Hotspot, image.Frames[0].Metadata.Hotspot);
            }
        }

        static RotateMode ReferenceRotateMode(double degrees) => ((((int)degrees % 360) + 360) % 360) switch
        {
            90 => RotateMode.Rotate90,
            180 => RotateMode.Rotate180,
            _ => RotateMode.Rotate270,
        };
    }

    private static void CheckFloatingPointGeometry<TPixel>(TPixel[] pixels)
        where TPixel : unmanaged
    {
        (Action<Image> Operation, int[][] Expected)[] cases =
        [
            (static image => image.Rotate(RotateMode.Rotate90, Ct), Rotated90),
            (static image => image.Rotate(RotateMode.Rotate180, Ct), Rotated180),
            (static image => image.Rotate(RotateMode.Rotate270, Ct), Rotated270),
            (static image => image.Rotate(90.0, cancellationToken: Ct), Rotated90),
            (static image => image.Transform(new AffineTransform(1, 0, 0, -1, 0, 0), cancellationToken: Ct), FlippedVertically),
            (static image => image.Flip(FlipMode.Horizontal, Ct), FlippedHorizontally),
            (static image => image.Flip(FlipMode.Vertical, Ct), FlippedVertically),
            (static image => image.Crop(new Rectangle(1, 0, 2, 2), Ct), [[2, 3], [5, 6]]),
            (static image =>
            {
                image.Metadata.Orientation = ExifOrientation.LeftTop;
                image.AutoOrient(Ct);
            }, Transposed),

            // Nearest neighbor copies the pixels too (none has a zero alpha, which would become transparent black)
            (static image => image.Resize(new ResizeOptions(6, 2) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.NearestNeighbor }, Ct), [[1, 1, 2, 2, 3, 3], [4, 4, 5, 5, 6, 6]]),
        ];

        foreach (var (operation, expected) in cases)
        {
            using var image = Image.ImportPixelData<TPixel>(pixels, 3, 2);
            operation(image);
            var frame = image.Frames[0];
            Assert.Equal(new Size(expected[0].Length, expected.Length), frame.Size);
            for (var y = 0; y < expected.Length; y++)
            {
                for (var x = 0; x < expected[y].Length; x++)
                {
                    Assert.Equal(Bits(pixels[expected[y][x] - 1]), Bits(frame[x, y]));
                }
            }
        }
    }

    /// <summary>
    /// Converts sRGB samples to Display P3 and linear-light samples to sRGB: the results are the decimal reference of the
    /// harness within the precision of the sample type (single precision: its tolerance for floats), not values of 16 bits,
    /// and alpha (beyond 1 here) is copied.
    /// </summary>
    private static void CheckFloatingPointColorConversion<TPixel>(double tolerance)
        where TPixel : unmanaged
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        (ColorTransferFunction Label, IccProfile Source, IccProfile Destination)[] cases =
        [
            (ColorTransferFunction.Srgb, IccProfile.Srgb, displayP3),
            (ColorTransferFunction.Linear, BuiltInIccProfiles.LinearSrgb, IccProfile.Srgb),
        ];

        foreach (var (label, source, destination) in cases)
        {
            RgbaFloat[] colors = [new(12345 / 65535f, 23456 / 65535f, 34567 / 65535f, 1.75f), new(0.2f, 0.5f, 0.9f, 0.25f), new(0, 1, 0.5f, 0)];
            using var image = new Image<TPixel>(colors.Length, 1);
            for (var x = 0; x < colors.Length; x++)
            {
                image.Frames[0][x, 0] = FloatPixel<TPixel>(colors[x].R, colors[x].G, colors[x].B, colors[x].A);
            }

            image.Metadata.TransferFunction = label;
            var before = Enumerable.Range(0, colors.Length).Select(x => ToRgbaFloat(image.Frames[0][x, 0])).ToArray();
            image.ConvertColorProfile(destination, cancellationToken: Ct);
            Assert.Same(destination, image.Metadata.IccProfile);
            Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);

            var after = Enumerable.Range(0, colors.Length).Select(x => ToRgbaFloat(image.Frames[0][x, 0])).ToArray();
            var reference = ReferenceIccTransform.Create(source.Data.Span, destination.Data.Span);
            if (tolerance == 0)
            {
                Assert.Empty(reference.Compare(before.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B }).ToArray(), after.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B }).ToArray()));
            }

            for (var x = 0; x < colors.Length; x++)
            {
                var expected = reference.Convert([(decimal)before[x].R, (decimal)before[x].G, (decimal)before[x].B]);
                Assert.Equal((double)expected[0], after[x].R, tolerance: Math.Max(tolerance, 1e-6));
                Assert.Equal((double)expected[1], after[x].G, tolerance: Math.Max(tolerance, 1e-6));
                Assert.Equal((double)expected[2], after[x].B, tolerance: Math.Max(tolerance, 1e-6));
                Assert.Equal(before[x].A, after[x].A);
            }

            // The first color is the 16-bit literal of ConvertColorProfileConvertsUntaggedPixelsFromSrgbAndLabelsTheImage,
            // (15033, 23185, 33666), within half a 16-bit step, but it is not rounded to 16 bits
            if (label == ColorTransferFunction.Srgb && typeof(TPixel) == typeof(RgbaFloat))
            {
                Assert.Equal(15033, after[0].R * 65535d, tolerance: 0.5);
                Assert.Equal(23185, after[0].G * 65535d, tolerance: 0.5);
                Assert.Equal(33666, after[0].B * 65535d, tolerance: 0.5);
                Assert.NotEqual(15033 / 65535f, after[0].R);
            }
        }
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
            var t when t == typeof(La16) => new La16((byte)(id * 3), (byte)(255 - id)),
            var t when t == typeof(La32) => new La32((ushort)((id * 1000) + 1), (ushort)(0x8000 + id)),
            var t when t == typeof(Rgb48) => new Rgb48((ushort)((0x0101 * id) + 1), (ushort)(0x1000 + (3 * id)), (ushort)(0xFFFF - (7 * id))),
            var t when t == typeof(A8) => new A8((byte)(255 - (id * 3))),
            var t when t == typeof(Argb32) => new Argb32((byte)id, (byte)(id + 100), (byte)(200 - id), (byte)(255 - id)),
            var t when t == typeof(Abgr32) => new Abgr32((byte)id, (byte)(id + 100), (byte)(200 - id), (byte)(255 - id)),
            var t when t == typeof(Bgr565) => new Bgr565((byte)(id & 31), (byte)((id + 20) & 63), (byte)((31 - id) & 31)),
            var t when t == typeof(Rgba1010102) => new Rgba1010102((ushort)(((37 * id) + 1) & 1023), (ushort)((0x100 + (3 * id)) & 1023), (ushort)((1023 - (7 * id)) & 1023), (byte)(1 + (id % 3))),

            // A fraction, a negative (out-of-gamut) color, a high dynamic range one and a translucent alpha, all exact as halves
            var t when t == typeof(RgbaFloat) => new RgbaFloat(id + 0.25f, -id / 8f, 1000f + id, 1f - (id / 64f)),
            var t when t == typeof(RgbaHalf) => new RgbaHalf((Half)(id + 0.25f), (Half)(-id / 8f), (Half)(1000f + id), (Half)(1f - (id / 64f))),
            _ => throw new NotSupportedException(),
        };

        return (TPixel)pixel;
    }

    /// <summary>A pixel of a floating-point type (<see cref="RgbaFloat"/>, or the nearest <see cref="RgbaHalf"/>).</summary>
    internal static TPixel FloatPixel<TPixel>(float r, float g, float b, float a = 1f)
        where TPixel : unmanaged
    {
        object pixel = typeof(TPixel) == typeof(RgbaFloat) ? new RgbaFloat(r, g, b, a) : new RgbaHalf((Half)r, (Half)g, (Half)b, (Half)a);
        return (TPixel)pixel;
    }

    /// <summary>Widens a floating-point pixel to <see cref="RgbaFloat"/>, exactly.</summary>
    internal static RgbaFloat ToRgbaFloat<TPixel>(TPixel pixel)
        where TPixel : unmanaged
        => pixel switch
        {
            RgbaFloat value => value,
            RgbaHalf value => new RgbaFloat((float)value.R, (float)value.G, (float)value.B, (float)value.A),
            _ => throw new NotSupportedException(),
        };

    /// <summary>The bits of a pixel, to compare not-a-number payloads and signed zeros.</summary>
    internal static (int R, int G, int B, int A) Bits<TPixel>(TPixel pixel)
        where TPixel : unmanaged
        => pixel switch
        {
            RgbaFloat value => (BitConverter.SingleToInt32Bits(value.R), BitConverter.SingleToInt32Bits(value.G), BitConverter.SingleToInt32Bits(value.B), BitConverter.SingleToInt32Bits(value.A)),
            RgbaHalf value => (BitConverter.HalfToInt16Bits(value.R), BitConverter.HalfToInt16Bits(value.G), BitConverter.HalfToInt16Bits(value.B), BitConverter.HalfToInt16Bits(value.A)),
            _ => throw new NotSupportedException(),
        };

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
        image.Frames[0].Metadata.Hotspot = new Point(2, 1);
        image.Frames[1].Metadata.Hotspot = new Point(1, 0);
        image.PosterFrame!.Metadata.Hotspot = new Point(1, 1);
        image.Metadata.ExifProfile = CreateExif(orientation: 7, width: 3, height: 2);
        image.Metadata.Orientation = ExifOrientation.RightBottom;
        return image;
    }

    private static Rgba32[] Row(Image<Rgba32> image, int y) => [.. Enumerable.Range(0, image.Width).Select(x => image.Frames[0][x, y])];

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
        private Point?[] _hotspots = [];
        private Point? _posterHotspot;
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
                _hotspots = [.. frames.Select(frame => frame.Metadata.Hotspot)],
                _posterHotspot = image.PosterFrame?.Metadata.Hotspot,
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
                Assert.Equal(_hotspots[i], image.Frames[i].Metadata.Hotspot);
            }

            Assert.Same(_poster, image.PosterFrame);
            if (_poster is not null)
            {
                Assert.Same(_posterStorage, image.PosterFrame!.Storage);
                Assert.Equal(_posterPixels, CopyPixels(image.PosterFrame));
                Assert.Equal(_posterHotspot, image.PosterFrame.Metadata.Hotspot);
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
