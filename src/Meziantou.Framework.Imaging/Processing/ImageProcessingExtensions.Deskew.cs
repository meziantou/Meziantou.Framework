namespace Meziantou.Framework.Imaging;

// Skew estimation and correction of scanned documents
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Estimates the angle by which the lines of text of a scanned or photographed document are rotated, in degrees: positive
    /// when they go down to the right (the page is turned clockwise). Nothing is changed; pass the opposite of the angle to
    /// <see cref="Rotate(Image, double, TransformOptions?, CancellationToken)"/>, or call
    /// <see cref="Deskew(Image, SkewEstimationOptions?, TransformOptions?, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is the projection profile (Postl, 1986; Baird, 1987). The frame is read as its Rec. 709 luma composited over
    /// white, and split into paper and ink by the threshold of Otsu (1979), the ink being the smaller class, so that dark text
    /// on light paper and light text on dark paper both work. The ink pixels are counted along the lines of each candidate
    /// angle, and the angle whose counts are the most uneven (the largest sum of their squares: the lines of text fall in few
    /// rows, the gaps between them in empty ones) wins: the multiples of 0.5 degree up to
    /// <see cref="SkewEstimationOptions.MaxAngle"/>, then the multiples of <see cref="SkewEstimationOptions.Precision"/> near
    /// the best one.
    /// </para>
    /// <para>
    /// It needs lines of text, or other parallel rows of ink; a frame without contrast gives 0. The scores are integers and
    /// the sines and cosines do not depend on the platform, so neither does the result: among equal scores, the angle nearest
    /// to 0 wins. The time is the number of ink pixels times the number of angles; the levels (1 byte per pixel) and the ink
    /// positions (8 bytes per ink pixel) are charged to the image.
    /// </para>
    /// </remarks>
    /// <param name="frame">The frame.</param>
    /// <param name="options">The largest angle and the precision, or <see langword="null"/> for <see cref="SkewEstimationOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token checked between rows and between angles.</param>
    /// <returns>The skew angle, in degrees, from <c>-MaxAngle</c> to <c>MaxAngle</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are CMYK inks.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static double EstimateSkew(this ImageFrame frame, SkewEstimationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfCmyk(frame.PixelFormat, nameof(EstimateSkew));
        _ = frame.GetStorage();
        return frame.OwnerImage.EstimateSkewCore(frame, options ?? SkewEstimationOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Straightens a scanned or photographed document: estimates the skew of its first frame
    /// (<see cref="EstimateSkew(ImageFrame, SkewEstimationOptions?, CancellationToken)"/>) and rotates every frame by the
    /// opposite angle, around its center.
    /// </summary>
    /// <remarks>
    /// The rotation is <see cref="Rotate(Image, double, TransformOptions?, CancellationToken)"/> with
    /// <paramref name="transformOptions"/>; by default, the canvas keeps the size of the image
    /// (<see cref="TransformCanvasMode.Keep"/>), the corners the rotation uncovers are opaque white, like paper, and the filter
    /// is the default one of <see cref="TransformOptions"/>. It is transactional across every frame and the poster. A skew of
    /// 0 leaves the image unchanged.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/GeometryTests.cs" ref:region="Deskew_StraightenAScan" ref:dedent="true">
    /// [Fact]
    /// public void Deskew_StraightenAScan()
    /// {
    ///     // A page of 8 lines of text (words of dark pixels), scanned 3 degrees clockwise
    ///     using var scan = new Image&lt;Gray8&gt;(240, 180, new Gray8(250));
    ///     for (var line = 0; line &lt; 8; line++)
    ///     {
    ///         for (var y = 20 + (line * 18); y &lt; 26 + (line * 18); y++)
    ///         {
    ///             for (var x = 20; x &lt; 220; x++)
    ///             {
    ///                 if (x % 23 &lt; 18)
    ///                 {
    ///                     scan.Frames[0][x, y] = new Gray8(30);
    ///                 }
    ///             }
    ///         }
    ///     }
    ///
    ///     scan.Rotate(3, new TransformOptions { CanvasMode = TransformCanvasMode.Keep, BackgroundColor = new Rgba64(64250, 64250, 64250, 65535) });
    ///
    ///     // The skew is found, then corrected: the lines of text are horizontal again
    ///     Assert.InRange(scan.Frames[0].EstimateSkew(), 2.9, 3.1);
    ///     var corrected = scan.Deskew();
    ///     Assert.InRange(corrected, 2.9, 3.1);
    ///     Assert.InRange(scan.Frames[0].EstimateSkew(), -0.1, 0.1);
    ///     Assert.Equal(new Size(240, 180), scan.Size);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The largest angle and the precision, or <see langword="null"/> for <see cref="SkewEstimationOptions.Default"/>.</param>
    /// <param name="transformOptions">The options of the rotation, or <see langword="null"/> for a canvas of the size of the image and an opaque white background.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The skew that was corrected, in degrees: the image was rotated by its opposite.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="TransformOptions.CanvasMode"/> is <see cref="TransformCanvasMode.Explicit"/> and <see cref="TransformOptions.CanvasSize"/> is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are CMYK inks; the pixel format has no alpha and the background is not opaque; or the cursor hotspot of a frame leaves the canvas. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory or the replacement buffers exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static double Deskew(this Image image, SkewEstimationOptions? options = null, TransformOptions? transformOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfCmyk(image.PixelFormat, nameof(Deskew));
        transformOptions = ValidateTransformOptions(transformOptions ?? new TransformOptions { CanvasMode = TransformCanvasMode.Keep, BackgroundColor = new Rgba64(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue) });
        var skew = image.Frames[0].EstimateSkew(options, cancellationToken);
        if (skew != 0)
        {
            image.Rotate(-skew, transformOptions, cancellationToken);
        }

        return skew;
    }
}
