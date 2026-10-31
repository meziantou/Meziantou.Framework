using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// The morphological filters (erosion, dilation, opening, closing, top-hat transforms) and the percentile filter
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Replaces every sample of every displayed frame and the poster frame by the minimum of the same sample of the pixels
    /// of a structuring element around its pixel, in place: a grayscale erosion, which grows the dark areas and removes
    /// bright details smaller than the element. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The element is flat (see <see cref="StructuringElement"/>), so the result is one of the samples: nothing is
    /// computed or rounded, 16-bit samples keep their precision, and there is no working space, since a transfer function
    /// keeps the order of the samples. Every sample of a pixel is filtered on its own, alpha included, as stored: an
    /// erosion also shrinks the opaque areas of a mask. The result is the one of OpenCV's <c>cv2.erode</c> with
    /// <c>borderType=cv2.BORDER_REPLICATE</c> and of SciPy's <c>ndimage.grey_erosion(footprint=...)</c>, whose modes
    /// <c>nearest</c>, <c>reflect</c>, <c>wrap</c> and <c>constant</c> (0) are the edge modes
    /// <see cref="ConvolutionEdgeMode.Clamp"/>, <see cref="ConvolutionEdgeMode.Mirror"/>, <see cref="ConvolutionEdgeMode.Wrap"/>
    /// and <see cref="ConvolutionEdgeMode.Zero"/>. <see cref="ConvolutionEdgeMode.Clamp"/>, the default, gives the result of
    /// ignoring the pixels outside the frame, the default of OpenCV. Floating-point samples are ordered as
    /// numbers, -0 below +0, and every sample that is not a number above the positive infinity (it comes back with a
    /// positive sign), as <see cref="MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/> orders them.
    /// </para>
    /// <para>
    /// A rectangle takes a constant time per pixel, whatever its size (the algorithm of van Herk and of Gil and Werman, in
    /// two passes); an ellipse takes a time per pixel that grows with its height. The scratch storage (about three blocks
    /// of <c>2 RadiusY + 1</c> rows of keys of 1, 2 or 4 bytes per sample) is charged to the image and rented before any
    /// pixel changes. The frames are filtered on the calling thread. An element of radius 0 changes nothing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/MorphologyTests.cs" ref:region="Morphology_RemoveSpecks" ref:dedent="true">
    /// [Fact]
    /// public void Morphology_RemoveSpecks()
    /// {
    ///     // A dark page with a bright one-pixel speck and a bright 5x5 square
    ///     using var image = new Image&lt;Gray8&gt;(16, 16, new Gray8(20));
    ///     image.Frames[0][2, 2] = new Gray8(250);
    ///     for (var y = 8; y &lt; 13; y++)
    ///     {
    ///         for (var x = 8; x &lt; 13; x++)
    ///         {
    ///             image.Frames[0][x, y] = new Gray8(200);
    ///         }
    ///     }
    ///
    ///     // An opening by a 3x3 square removes what is smaller than the square, and keeps the larger square intact
    ///     image.MorphologicalOpen(StructuringElement.CreateRectangle(1, 1));
    ///
    ///     Assert.Equal(20, image.Frames[0][2, 2].Value);
    ///     Assert.Equal(200, image.Frames[0][8, 8].Value);
    ///     Assert.Equal(200, image.Frames[0][12, 12].Value);
    ///     Assert.Equal(20, image.Frames[0][13, 13].Value);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Erode(this Image image, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyImage(image, element, edgeMode, MorphologyOperation.Erode, cancellationToken);

    /// <summary>Erodes one frame in place. The EXIF thumbnail of the image, now stale, is removed. See <see cref="Erode(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Erode(this ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyFrame(frame, element, edgeMode, MorphologyOperation.Erode, cancellationToken);

    /// <summary>
    /// Replaces every sample of every displayed frame and the poster frame by the maximum of the same sample of the pixels
    /// of a structuring element around its pixel, in place: a grayscale dilation, which grows the bright areas and fills
    /// dark details smaller than the element. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// The dilation is the counterpart of <see cref="Erode(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>,
    /// which describes the samples, the edges and the costs: it gives the result of OpenCV's <c>cv2.dilate</c> and of
    /// SciPy's <c>ndimage.grey_dilation</c>. Alpha is filtered too: a dilation grows the opaque areas of a mask.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Dilate(this Image image, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyImage(image, element, edgeMode, MorphologyOperation.Dilate, cancellationToken);

    /// <summary>Dilates one frame in place. The EXIF thumbnail of the image, now stale, is removed. See <see cref="Dilate(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Dilate(this ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyFrame(frame, element, edgeMode, MorphologyOperation.Dilate, cancellationToken);

    /// <summary>
    /// Applies a morphological opening to every displayed frame and the poster frame, in place: an erosion followed by a
    /// dilation with the same element, which removes the bright details smaller than the element (specks, thin lines) and
    /// keeps the shape and brightness of larger areas. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// See <see cref="Erode(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/> for the samples, the
    /// edges and the costs; the dilation reads the eroded frame with the same edge mode, as OpenCV's
    /// <c>cv2.morphologyEx(MORPH_OPEN)</c> does.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void MorphologicalOpen(this Image image, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyImage(image, element, edgeMode, MorphologyOperation.Open, cancellationToken);

    /// <summary>Applies a morphological opening to one frame in place. The EXIF thumbnail of the image, now stale, is removed. See <see cref="MorphologicalOpen(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void MorphologicalOpen(this ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyFrame(frame, element, edgeMode, MorphologyOperation.Open, cancellationToken);

    /// <summary>
    /// Applies a morphological closing to every displayed frame and the poster frame, in place: a dilation followed by an
    /// erosion with the same element, which fills the dark details smaller than the element (holes, gaps, thin dark lines)
    /// and keeps the shape and brightness of larger areas. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// See <see cref="Erode(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/> for the samples, the
    /// edges and the costs; the erosion reads the dilated frame with the same edge mode, as OpenCV's
    /// <c>cv2.morphologyEx(MORPH_CLOSE)</c> does.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void MorphologicalClose(this Image image, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyImage(image, element, edgeMode, MorphologyOperation.Close, cancellationToken);

    /// <summary>Applies a morphological closing to one frame in place. The EXIF thumbnail of the image, now stale, is removed. See <see cref="MorphologicalClose(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="element">The neighborhood of each pixel.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void MorphologicalClose(this ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyFrame(frame, element, edgeMode, MorphologyOperation.Close, cancellationToken);

    /// <summary>
    /// Replaces the colors of every displayed frame and the poster frame by their white top-hat transform, in place: the
    /// difference between the frame and its opening, which keeps only the bright details smaller than the element, on
    /// black. It extracts small bright features from an uneven background (text on a page lit unevenly, stars, cells).
    /// Alpha is kept. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every color sample becomes <c>sample - opening</c>, where the opening is the one of
    /// <see cref="MorphologicalOpen(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>: the result of
    /// OpenCV's <c>cv2.morphologyEx(MORPH_TOPHAT)</c> for the colors. Integer differences are never negative (they are
    /// clamped to 0), and floating-point differences are rounded to the sample type. Alpha is not transformed (the
    /// transform of an opaque alpha would be fully transparent), so an alpha mask (<see cref="A8"/>), which has no color, is
    /// rejected.
    /// </para>
    /// <para>
    /// The scratch storage of the opening and a copy of the frame are charged to the image and rented before any pixel
    /// changes.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="element">The neighborhood of each pixel: larger than the details to keep.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">The pixel format is <see cref="A8"/>, which has no color. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void TopHat(this Image image, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyImage(image, element, edgeMode, MorphologyOperation.TopHat, cancellationToken);

    /// <summary>Replaces the colors of one frame by their white top-hat transform, in place. The EXIF thumbnail of the image, now stale, is removed. See <see cref="TopHat(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="element">The neighborhood of each pixel: larger than the details to keep.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">The pixel format is <see cref="A8"/>, which has no color. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void TopHat(this ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyFrame(frame, element, edgeMode, MorphologyOperation.TopHat, cancellationToken);

    /// <summary>
    /// Replaces the colors of every displayed frame and the poster frame by their black top-hat transform, in place: the
    /// difference between the closing of the frame and the frame, which shows the dark details smaller than the element as
    /// bright ones, on black (dark text on a page lit unevenly, scratches). Alpha is kept. The EXIF thumbnail, now stale,
    /// is removed.
    /// </summary>
    /// <remarks>
    /// Every color sample becomes <c>closing - sample</c>, where the closing is the one of
    /// <see cref="MorphologicalClose(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>: the result of
    /// OpenCV's <c>cv2.morphologyEx(MORPH_BLACKHAT)</c> for the colors. See
    /// <see cref="TopHat(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/> for the differences, alpha and
    /// the scratch storage.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="element">The neighborhood of each pixel: larger than the details to keep.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">The pixel format is <see cref="A8"/>, which has no color. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void BlackHat(this Image image, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyImage(image, element, edgeMode, MorphologyOperation.BlackHat, cancellationToken);

    /// <summary>Replaces the colors of one frame by their black top-hat transform, in place. The EXIF thumbnail of the image, now stale, is removed. See <see cref="BlackHat(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="element">The neighborhood of each pixel: larger than the details to keep.</param>
    /// <param name="edgeMode">The pixels the filters read beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">The pixel format is <see cref="A8"/>, which has no color. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void BlackHat(this ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
        => MorphologyFrame(frame, element, edgeMode, MorphologyOperation.BlackHat, cancellationToken);

    /// <summary>
    /// Replaces every sample of every displayed frame and the poster frame by a percentile of the same sample of the square
    /// around its pixel, in place: a rank filter, which generalizes the median (50), the minimum (0, an erosion by a
    /// square) and the maximum (100, a dilation by a square). The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>n = (2 radius + 1)^2</c> samples of the square are sorted, and the sample of rank <c>floor(n * percentile / 100)</c>
    /// (from 0, and <c>n - 1</c> for 100) is kept: the rank of SciPy's <c>ndimage.percentile_filter(size=2 radius + 1)</c>,
    /// whose result it gives with the same edges. A low percentile, such as 10, darkens like an erosion but ignores a few
    /// dark outliers; a high one brightens like a dilation.
    /// </para>
    /// <para>
    /// Everything else is <see cref="MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/>: the same
    /// histograms, costs, samples (alpha included), edges and workers.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="radius">The number of pixels on each side of a pixel. Must be between 0 and 1000.</param>
    /// <param name="percentile">The percentile, from 0 (the minimum) to 100 (the maximum).</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 1000, <paramref name="percentile"/> is not between 0 and 100, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void PercentileFilter(this Image image, int radius, double percentile, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var rank = ValidatePercentileFilter(radius, percentile, edgeMode);
        if (radius == 0)
        {
            image.Owner.EnsureCanModify("filter the image");
            return;
        }

        image.MedianAllFrames(radius, edgeMode, rank, cancellationToken);
    }

    /// <summary>
    /// Replaces every sample of one frame by a percentile of its neighborhood, in place. The EXIF thumbnail of the image,
    /// now stale, is removed. See <see cref="PercentileFilter(Image, int, double, ConvolutionEdgeMode, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="radius">The number of pixels on each side of a pixel. Must be between 0 and 1000.</param>
    /// <param name="percentile">The percentile, from 0 (the minimum) to 100 (the maximum).</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 1000, <paramref name="percentile"/> is not between 0 and 100, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void PercentileFilter(this ImageFrame frame, int radius, double percentile, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var rank = ValidatePercentileFilter(radius, percentile, edgeMode);
        var storage = frame.GetStorage();
        if (radius == 0)
            return;

        frame.OwnerImage.MedianFrame(frame, storage, radius, edgeMode, rank, cancellationToken);
    }

    private static void MorphologyImage(Image image, StructuringElement element, ConvolutionEdgeMode edgeMode, MorphologyOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateMorphology(element, edgeMode);
        if (element.RadiusX == 0 && element.RadiusY == 0 && operation is not (MorphologyOperation.TopHat or MorphologyOperation.BlackHat))
        {
            image.Owner.EnsureCanModify("filter the image");
            return;
        }

        image.MorphologyFrames(frames: null, element, edgeMode, operation, cancellationToken);
    }

    private static void MorphologyFrame(ImageFrame frame, StructuringElement element, ConvolutionEdgeMode edgeMode, MorphologyOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateMorphology(element, edgeMode);
        _ = frame.GetStorage();
        if (element.RadiusX == 0 && element.RadiusY == 0 && operation is not (MorphologyOperation.TopHat or MorphologyOperation.BlackHat))
            return;

        frame.OwnerImage.MorphologyFrames([frame], element, edgeMode, operation, cancellationToken);
    }

    private static void ValidateMorphology(StructuringElement element, ConvolutionEdgeMode edgeMode)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!Enum.IsDefined(edgeMode))
            throw new ArgumentOutOfRangeException(nameof(edgeMode), edgeMode, "The edge mode is not valid.");
    }

    /// <summary>Validates the arguments of a percentile filter and returns the rank of the percentile in a window of the radius (SciPy's rank).</summary>
    private static int ValidatePercentileFilter(int radius, double percentile, ConvolutionEdgeMode edgeMode)
    {
        ValidateRadius(radius, MaxMedianRadius);
        if (!(percentile is >= 0 and <= 100))
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "The percentile must be between 0 and 100.");

        ValidateBlur(edgeMode, ConvolutionWorkingSpace.Encoded);
        var count = ((2L * radius) + 1) * ((2L * radius) + 1);
        return percentile == 100 ? (int)(count - 1) : (int)Math.Floor(count * percentile / 100);
    }
}
