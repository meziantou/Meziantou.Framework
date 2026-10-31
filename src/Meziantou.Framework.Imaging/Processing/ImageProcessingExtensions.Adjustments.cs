using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// The tone adjustments: tone curves (gamma, levels, curves, one-dimensional tables) and color lookup tables
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Gives a new value to the red, green and blue samples of every frame of the image (including the poster frame) with
    /// a tone curve, in place: a gamma correction, a levels adjustment, a curve through control points or a lookup table
    /// (see <see cref="ToneCurve"/>). Alpha and the pixel format are kept. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each sample <c>v</c> becomes <c>curve.Evaluate(v / max) * max</c>, where <c>max</c> is the largest sample value: the
    /// curve maps the stored (encoded) values, as photo editors do, and no color profile is applied. Integer samples are
    /// mapped at the storage precision, through a table of every level (256 for 8-bit formats, 65,536 for 16-bit ones),
    /// rounded to the nearest level (ties upward) and clamped to the sample range. The colors are straight (not
    /// premultiplied), so the hidden color of a transparent pixel is mapped too. Gray formats map their gray level, and an
    /// alpha mask (<see cref="A8"/>) is left unchanged.
    /// </para>
    /// <para>
    /// Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) are mapped as stored and stored without
    /// clamping: the curve decides what happens outside [0, 1] (a gamma keeps the highlights above 1, the levels and the
    /// curves through points clip them, see <see cref="ToneCurve"/>), and a sample that is not a number stays one.
    /// </para>
    /// <para>
    /// The working memory (a row, and the tables of the levels, at most 384 KiB) is charged to the image and rented before
    /// a pixel changes.
    /// <see cref="ToneCurve.Identity"/> changes nothing. On cancellation, some frames or rows may already be changed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ToneAdjustmentTests.cs" ref:region="ToneCurve_Levels" ref:dedent="true">
    /// [Fact]
    /// public void ToneCurve_Levels()
    /// {
    ///     using var image = new Image&lt;Rgb24&gt;(2, 1);
    ///     image.Frames[0][0, 0] = new Rgb24(64, 128, 192);
    ///     image.Frames[0][1, 0] = new Rgb24(10, 240, 255);
    ///
    ///     // Clip the darkest and the brightest 10% of the levels and brighten the midtones, then add contrast with an
    ///     // S curve through control points
    ///     var levels = ToneCurve.CreateLevels(inputBlack: 0.1, inputWhite: 0.9, gamma: 1.2);
    ///     var contrast = ToneCurve.CreateFromPoints(new PointD(0, 0), new PointD(0.25, 0.2), new PointD(0.75, 0.8), new PointD(1, 1));
    ///     image.ApplyToneCurve(levels.Then(contrast));
    ///
    ///     Assert.Equal(new Rgb24(51, 149, 225), image.Frames[0][0, 0]);
    ///     Assert.Equal(new Rgb24(0, 255, 255), image.Frames[0][1, 0]);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="curve">The curve of the three color channels.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="curve"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyToneCurve(this Image image, ToneCurve curve, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(curve);
        image.ApplyToneCurves(curve, curve, curve, cancellationToken);
    }

    /// <summary>
    /// Gives a new value to the red, green and blue samples of one frame with a tone curve, in place. The EXIF thumbnail of
    /// the image, now stale, is removed. See <see cref="ApplyToneCurve(Image, ToneCurve, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="curve">The curve of the three color channels.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="curve"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyToneCurve(this ImageFrame frame, ToneCurve curve, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(curve);
        frame.ApplyToneCurves(curve, curve, curve, cancellationToken);
    }

    /// <summary>
    /// Gives a new value to the red, green and blue samples of every frame of the image (including the poster frame) with a
    /// tone curve per channel, in place: the per-channel curves of photo editors, which also correct or create a color
    /// cast. Alpha and the pixel format are kept. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every color sample is mapped by the curve of its channel, as
    /// <see cref="ApplyToneCurve(Image, ToneCurve, CancellationToken)"/> describes. A gray pixel is mapped as the color of
    /// three equal samples, and stores the Rec. 709 luma of the result, as <see cref="Grayscale(Image, CancellationToken)"/>
    /// computes it: equal curves give the gray level of the curve.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="red">The curve of the red channel.</param>
    /// <param name="green">The curve of the green channel.</param>
    /// <param name="blue">The curve of the blue channel.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/>, <paramref name="red"/>, <paramref name="green"/> or <paramref name="blue"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyToneCurves(this Image image, ToneCurve red, ToneCurve green, ToneCurve blue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateToneCurves(red, green, blue);
        if (red.IsIdentity && green.IsIdentity && blue.IsIdentity)
        {
            image.Owner.EnsureCanModify("apply a tone curve");
            return;
        }

        image.ApplyColorLookupFrames(frames: null, new ToneCurveLookup(red, green, blue), "apply a tone curve", cancellationToken);
    }

    /// <summary>
    /// Gives a new value to the red, green and blue samples of one frame with a tone curve per channel, in place. The EXIF
    /// thumbnail of the image, now stale, is removed. See <see cref="ApplyToneCurves(Image, ToneCurve, ToneCurve, ToneCurve, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="red">The curve of the red channel.</param>
    /// <param name="green">The curve of the green channel.</param>
    /// <param name="blue">The curve of the blue channel.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/>, <paramref name="red"/>, <paramref name="green"/> or <paramref name="blue"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyToneCurves(this ImageFrame frame, ToneCurve red, ToneCurve green, ToneCurve blue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateToneCurves(red, green, blue);
        _ = frame.GetStorage();
        if (red.IsIdentity && green.IsIdentity && blue.IsIdentity)
            return;

        frame.OwnerImage.ApplyColorLookupFrames([frame], new ToneCurveLookup(red, green, blue), "apply a tone curve", cancellationToken);
    }

    /// <summary>
    /// Gives a new color to every pixel of every frame of the image (including the poster frame) from a color lookup table
    /// read from a <c>.cube</c> file, in place: a color grade or a film look exported by a video or photo tool. Alpha and the
    /// pixel format are kept. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table maps the stored (encoded) colors as fractions of the largest sample value, which is what such tables
    /// expect from encoded images (an sRGB photo, or video in the color space the table was made for): no color profile is
    /// applied. A three-dimensional table gives each color a new one, interpolated between the points of its lattice as
    /// <paramref name="interpolation"/> says; a one-dimensional table maps each channel on its own, interpolated linearly
    /// (<paramref name="interpolation"/> does not apply). An input value is clamped to the domain of the table first. With
    /// <see cref="LutInterpolation.Tetrahedral"/> or <see cref="LutInterpolation.Trilinear"/>, the result is the one of
    /// FFmpeg's <c>lut3d</c> filter with the same interpolation, except that the table is read in single precision and
    /// interpolated in double precision.
    /// </para>
    /// <para>
    /// Integer samples are rounded to the nearest level (ties upward) and clamped to the sample range. A gray pixel is
    /// mapped as the color of three equal samples and stores the Rec. 709 luma of the result; an alpha mask (<see cref="A8"/>)
    /// is left unchanged. Floating-point samples are mapped as stored, clamped to the domain on input, and the output of the
    /// table is stored without clamping; a pixel with a color sample that is not a number gets three of them.
    /// </para>
    /// <para>
    /// A three-dimensional table is evaluated for every pixel, and a one-dimensional one, like the table of a gray format,
    /// once per level. The working memory (a row, and the tables of the levels, at most 384 KiB) is charged to the image and
    /// rented before a pixel changes. On cancellation, some frames or rows may already be changed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ToneAdjustmentTests.cs" ref:region="CubeLut_Apply" ref:dedent="true">
    /// [Fact]
    /// public void CubeLut_Apply()
    /// {
    ///     // A three-dimensional table of 2 x 2 x 2 points that inverts the colors, as a .cube file holds it
    ///     var lut = CubeLut.Parse("""
    ///         TITLE "Negative"
    ///         LUT_3D_SIZE 2
    ///         1 1 1
    ///         0 1 1
    ///         1 0 1
    ///         0 0 1
    ///         1 1 0
    ///         0 1 0
    ///         1 0 0
    ///         0 0 0
    ///         """);
    ///
    ///     using var image = new Image&lt;Rgba32&gt;(1, 1, new Rgba32(255, 64, 0, 128));
    ///     image.ApplyLut(lut);
    ///
    ///     Assert.Equal("Negative", lut.Title);
    ///     Assert.Equal(new Rgba32(0, 191, 255, 128), image.Frames[0][0, 0]);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="lut">The table.</param>
    /// <param name="interpolation">The interpolation of a three-dimensional table.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="lut"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interpolation"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyLut(this Image image, CubeLut lut, LutInterpolation interpolation = LutInterpolation.Tetrahedral, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateLut(lut, interpolation);
        image.ApplyColorLookupFrames(frames: null, new CubeLutLookup(lut, interpolation), "apply a lookup table", cancellationToken);
    }

    /// <summary>
    /// Gives a new color to every pixel of one frame from a color lookup table, in place. The EXIF thumbnail of the image,
    /// now stale, is removed. See <see cref="ApplyLut(Image, CubeLut, LutInterpolation, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="lut">The table.</param>
    /// <param name="interpolation">The interpolation of a three-dimensional table.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="lut"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interpolation"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyLut(this ImageFrame frame, CubeLut lut, LutInterpolation interpolation = LutInterpolation.Tetrahedral, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateLut(lut, interpolation);
        _ = frame.GetStorage();
        frame.OwnerImage.ApplyColorLookupFrames([frame], new CubeLutLookup(lut, interpolation), "apply a lookup table", cancellationToken);
    }

    private static void ValidateToneCurves(ToneCurve red, ToneCurve green, ToneCurve blue)
    {
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(green);
        ArgumentNullException.ThrowIfNull(blue);
    }

    private static void ValidateLut(CubeLut lut, LutInterpolation interpolation)
    {
        ArgumentNullException.ThrowIfNull(lut);
        if (!Enum.IsDefined(interpolation))
            throw new ArgumentOutOfRangeException(nameof(interpolation), interpolation, "The interpolation is not valid.");
    }
}
