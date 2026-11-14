namespace Meziantou.Framework.Imaging;

// Line and corner detection
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Finds the straight lines of a frame with the Hough transform: the edges of the frame (the Canny detector) vote for
    /// the lines through them, and the lines with the most votes are returned, the strongest first. Nothing is changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edges are those of <see cref="DetectCannyEdges(ImageFrame, CannyEdgeOptions?, CancellationToken)"/> with
    /// <see cref="HoughLineOptions.EdgeOptions"/>. Every edge pixel adds a vote to the lines through it,
    /// <c>ρ = x cos θ + y sin θ</c> (Duda and Hart, 1972), for the angles <c>θ</c> from 0 to 180 degrees by
    /// <see cref="HoughLineOptions.AngleStep"/> that are within 30 degrees of the direction of its gradient (one of the
    /// four sectors of the Canny detector, O'Gorman and Clowes, 1976), the distance rounded to
    /// <see cref="HoughLineOptions.DistanceStep"/>. A line
    /// is a local maximum of the votes with at least <see cref="HoughLineOptions.MinVotes"/> (among equal votes, the line
    /// whose pixels are nearest to the middle of their distance step wins), and the lines are sorted by votes; of two lines
    /// closer than both
    /// <see cref="HoughLineOptions.MinAngleSeparation"/> and <see cref="HoughLineOptions.MinDistanceSeparation"/>, the one with
    /// the most votes is kept. The lines are infinite: a line of the result tells the direction and the position, not where a
    /// segment starts and ends.
    /// </para>
    /// <para>
    /// The votes are integers and the sines and cosines do not depend on the platform, so neither does the result: equal votes
    /// are sorted by how well their pixels line up, then angle, then distance. The pixels vote at their index, and the distance of a line is then given for
    /// the centers of the pixels (<see cref="HoughLine"/>). The time is the number of edge pixels times a third of the number of angles. The edges (18
    /// bytes per pixel) and the accumulator (12 bytes per cell) are charged to the image.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ConvolutionTests.cs" ref:region="DetectLines_FindTheEdgesOfAPage" ref:dedent="true">
    /// [Fact]
    /// public void DetectLines_FindTheEdgesOfAPage()
    /// {
    ///     // A light page on a dark desk: its edges are the lines x = 12, x = 52, y = 10 and y = 40
    ///     using var photo = new Image&lt;Rgb24&gt;(64, 48, new Rgb24(40, 40, 40));
    ///     for (var y = 10; y &lt; 40; y++)
    ///     {
    ///         for (var x = 12; x &lt; 52; x++)
    ///         {
    ///             photo.Frames[0][x, y] = new Rgb24(240, 240, 230);
    ///         }
    ///     }
    ///
    ///     // The four strongest lines, within the degree or two by which the Canny edges round the corners of the page.
    ///     // Vertical lines have an angle near 0 (x = distance), horizontal ones near 90 (y = distance)
    ///     var lines = photo.Frames[0].DetectLines(new HoughLineOptions { MaxLines = 4 });
    ///
    ///     Assert.Equal(4, lines.Count);
    ///     var vertical = lines.Where(line =&gt; Math.Abs(line.Slope) &gt;= 88).Select(line =&gt; Math.Abs(line.Distance)).Order().ToArray();
    ///     var horizontal = lines.Where(line =&gt; Math.Abs(line.Slope) &lt;= 2).Select(line =&gt; line.Distance).Order().ToArray();
    ///     Assert.HasCount(2, vertical);
    ///     Assert.HasCount(2, horizontal);
    ///     Assert.InRange(vertical[0], 10, 14);
    ///     Assert.InRange(vertical[1], 50, 54);
    ///     Assert.InRange(horizontal[0], 8, 12);
    ///     Assert.InRange(horizontal[1], 38, 42);
    /// }
    /// </code>
    /// </example>
    /// <param name="frame">The frame.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HoughLineOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The lines, the one with the most votes first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The thresholds of <see cref="HoughLineOptions.EdgeOptions"/> are in the wrong order.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are CMYK inks.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static IReadOnlyList<HoughLine> DetectLines(this ImageFrame frame, HoughLineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfCmyk(frame.PixelFormat, nameof(DetectLines));
        options ??= HoughLineOptions.Default;
        if (options.EdgeOptions.LowThreshold > options.EdgeOptions.HighThreshold)
            throw new ArgumentException("The lower threshold of the edges must not exceed the upper threshold.", nameof(options));

        _ = frame.GetStorage();
        return frame.OwnerImage.DetectLinesCore(frame, options, cancellationToken);
    }

    /// <summary>
    /// Finds the corners of a frame: the points where the brightness changes in every direction, such as the corners of
    /// objects and the crossings of lines, to track, to match between two images, or to fit a homography. Nothing is changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The frame is read as its Rec. 709 luma premultiplied by alpha, as a fraction of the largest sample. The detector is
    /// Harris and Stephens (1988), Shi and Tomasi (1994) or FAST-9 (Rosten and Drummond, 2006), see
    /// <see cref="CornerDetectionMethod"/>. A corner is a local maximum of the response among its 8 neighbors, above
    /// <see cref="CornerDetectionOptions.ResponseThreshold"/> times the largest response (Harris and Shi-Tomasi) or with a
    /// segment of 9 pixels beyond <see cref="CornerDetectionOptions.FastThreshold"/> (FAST); the strongest are kept, at least
    /// <see cref="CornerDetectionOptions.MinDistance"/> apart, up to <see cref="CornerDetectionOptions.MaxCorners"/>.
    /// </para>
    /// <para>
    /// The responses are computed in double precision without functions of the platform, and equal responses are sorted by
    /// row then column, so the result does not depend on the platform. The working memory (16 bytes per pixel, 32 more for
    /// Harris and Shi-Tomasi) is charged to the image.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ConvolutionTests.cs" ref:region="DetectCorners_FindTheCornersOfARectangle" ref:dedent="true">
    /// [Fact]
    /// public void DetectCorners_FindTheCornersOfARectangle()
    /// {
    ///     // A white square on black: its corners are the pixels (10, 10), (29, 10), (10, 29) and (29, 29)
    ///     using var image = new Image&lt;Gray8&gt;(40, 40);
    ///     for (var y = 10; y &lt; 30; y++)
    ///     {
    ///         for (var x = 10; x &lt; 30; x++)
    ///         {
    ///             image.Frames[0][x, y] = new Gray8(255);
    ///         }
    ///     }
    ///
    ///     var corners = image.Frames[0].DetectCorners();
    ///
    ///     Assert.Equal(4, corners.Count);
    ///     Point[] expected = [new(10, 10), new(29, 10), new(10, 29), new(29, 29)];
    ///     Assert.All(expected, point =&gt; Assert.Contains(corners, corner =&gt; Math.Abs(corner.X - point.X) &lt;= 2 &amp;&amp; Math.Abs(corner.Y - point.Y) &lt;= 2));
    /// }
    /// </code>
    /// </example>
    /// <param name="frame">The frame.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="CornerDetectionOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The corners, the strongest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are CMYK inks.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static IReadOnlyList<ImageCorner> DetectCorners(this ImageFrame frame, CornerDetectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfCmyk(frame.PixelFormat, nameof(DetectCorners));
        _ = frame.GetStorage();
        return frame.OwnerImage.DetectCornersCore(frame, options ?? CornerDetectionOptions.Default, cancellationToken);
    }
}
