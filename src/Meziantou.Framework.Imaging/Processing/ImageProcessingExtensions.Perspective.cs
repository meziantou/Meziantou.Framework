namespace Meziantou.Framework.Imaging;

// The four-point perspective correction
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Straightens a quadrilateral of every frame of the image into an upright rectangle that becomes the whole image: a
    /// document, a whiteboard or a painting photographed at an angle, seen from the front. The four corners are given in
    /// the image, in order around the quadrilateral.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The corners are in stored-pixel coordinates, where the image covers <c>[0, width] x [0, height]</c> and the center of
    /// pixel <c>(i, j)</c> is <c>(i + 1/2, j + 1/2)</c>: the corner of a document whose top-left pixel is <c>(10, 20)</c> is
    /// <c>(10, 20)</c>, and its center <c>(10.5, 20.5)</c>. The result maps <paramref name="topLeft"/> to the corner
    /// <c>(0, 0)</c> of a canvas of <paramref name="size"/> and <paramref name="bottomRight"/> to its opposite corner, with
    /// the projective transformation that maps the quadrilateral onto the rectangle
    /// (<see cref="ProjectiveTransform.CreateFromQuadrilateral(RectangleD, PointD, PointD, PointD, PointD)"/>, inverted). The
    /// default size is the one a scanner gives: the longer of the top and bottom edges by the longer of the left and right
    /// edges, rounded to whole pixels.
    /// </para>
    /// <para>
    /// The pixels are sampled as <see cref="Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/>
    /// samples them, with the filter, the background color (for the parts of a quadrilateral that reach beyond the image)
    /// and the working space of <paramref name="options"/>; its canvas mode and canvas size are not used. The operation is
    /// transactional across every frame and the poster.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/GeometryTests.cs" ref:region="CorrectPerspective_Document" ref:dedent="true">
    /// [Fact]
    /// public void CorrectPerspective_Document()
    /// {
    ///     // A 32x24 page with a checkerboard of 4-pixel squares
    ///     using var page = new Image&lt;Rgb24&gt;(32, 24);
    ///     for (var y = 0; y &lt; 24; y++)
    ///     {
    ///         for (var x = 0; x &lt; 32; x++)
    ///         {
    ///             page.Frames[0][x, y] = (x / 4) % 2 == (y / 4) % 2 ? new Rgb24(250, 250, 240) : new Rgb24(20, 30, 90);
    ///         }
    ///     }
    ///
    ///     // Photographed at an angle on a gray desk: the corners of the page land on a quadrilateral of a 64x48 photo
    ///     var topLeft = new PointD(8, 6);
    ///     var topRight = new PointD(56, 10);
    ///     var bottomRight = new PointD(52, 42);
    ///     var bottomLeft = new PointD(12, 40);
    ///     using var photo = page.Clone();
    ///     photo.Transform(
    ///         ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 32, 24), topLeft, topRight, bottomRight, bottomLeft),
    ///         new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(64, 48), BackgroundColor = new Rgba64(30000, 30000, 30000, 65535) });
    ///
    ///     // Seen from the front again: the quadrilateral becomes the whole image
    ///     photo.CorrectPerspective(topLeft, topRight, bottomRight, bottomLeft, new Size(32, 24), new TransformOptions { BackgroundColor = new Rgba64(30000, 30000, 30000, 65535) });
    ///
    ///     Assert.Equal(new Size(32, 24), photo.Size);
    ///
    ///     // The middle of every square has its color back, within the blur of the two resamplings
    ///     var largest = 0;
    ///     for (var y = 0; y &lt; 24; y++)
    ///     {
    ///         for (var x = 0; x &lt; 32; x++)
    ///         {
    ///             if (x % 4 is 1 or 2 &amp;&amp; y % 4 is 1 or 2)
    ///             {
    ///                 largest = Math.Max(largest, Math.Abs(page.Frames[0][x, y].B - photo.Frames[0][x, y].B));
    ///             }
    ///         }
    ///     }
    ///
    ///     Assert.InRange(largest, 0, 16);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="topLeft">The corner of the quadrilateral that becomes the top-left corner of the image.</param>
    /// <param name="topRight">The corner that becomes the top-right corner.</param>
    /// <param name="bottomRight">The corner that becomes the bottom-right corner.</param>
    /// <param name="bottomLeft">The corner that becomes the bottom-left corner.</param>
    /// <param name="size">The size of the result, or <see langword="null"/> for the size of the quadrilateral.</param>
    /// <param name="options">The filter, background color and working space, or <see langword="null"/> for those of <see cref="TransformOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not finite, the points are not the corners of a convex quadrilateral in order around it, or <paramref name="size"/> is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha and <see cref="TransformOptions.BackgroundColor"/> is not opaque; <see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image it cannot decode; or the cursor hotspot of a frame leaves the canvas. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds the size limits, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void CorrectPerspective(this Image image, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, Size? size = null, TransformOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= TransformOptions.Default;
        if (size is { IsEmpty: true })
            throw new ArgumentException("The size must not be empty.", nameof(size));

        var output = size ?? GetQuadrilateralSize(topLeft, topRight, bottomRight, bottomLeft);
        var toQuadrilateral = ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, output.Width, output.Height), topLeft, topRight, bottomRight, bottomLeft);
        if (!toQuadrilateral.TryInvert(out var transform))
            throw new ArgumentException("The quadrilateral is degenerate.", nameof(topLeft));

        var explicitOptions = new TransformOptions
        {
            Filter = options.Filter,
            BackgroundColor = options.BackgroundColor,
            WorkingSpace = options.WorkingSpace,
            CanvasMode = TransformCanvasMode.Explicit,
            CanvasSize = output,
        };

        image.Warp(transform, explicitOptions, centered: false, nameof(topLeft), cancellationToken);
    }

    /// <summary>The size of a quadrilateral: the longer of its top and bottom edges by the longer of its left and right edges, rounded, at least 1.</summary>
    private static Size GetQuadrilateralSize(PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft)
    {
        var width = Math.Max(topLeft.DistanceTo(topRight), bottomLeft.DistanceTo(bottomRight));
        var height = Math.Max(topLeft.DistanceTo(bottomLeft), topRight.DistanceTo(bottomRight));
        if (!double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentException("The coordinates of the corners must be finite.", nameof(topLeft));

        return new Size((int)Math.Clamp(Math.Round(width, MidpointRounding.AwayFromZero), 1, int.MaxValue), (int)Math.Clamp(Math.Round(height, MidpointRounding.AwayFromZero), 1, int.MaxValue));
    }
}
