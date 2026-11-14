using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// The estimation of a homography from point correspondences
public readonly partial struct ProjectiveTransform
{
    /// <summary>
    /// Estimates the homography that maps each source point onto its destination point, by least squares: the projective
    /// transformation of a plane seen from two points of view, such as the corners of a document matched in two photos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is the normalized direct linear transformation (Hartley and Zisserman, algorithm 4.2): the points are
    /// centered and scaled, and the homography minimizes the algebraic error of the equations of all the correspondences.
    /// Four correspondences, no three of their points on a line, give the exact homography; more correspondences are
    /// averaged, and all of them count: when some are wrong, use
    /// <see cref="EstimateHomographyRobust(ReadOnlySpan{PointD}, ReadOnlySpan{PointD}, HomographyRansacOptions?)"/>.
    /// </para>
    /// <para>
    /// The result is scaled so that <c>W</c> is 1 at the centroid of the source points (they are in front of the viewer). It
    /// is computed with additions, multiplications, divisions and square roots only, so it does not depend on the platform.
    /// Pass it to <see cref="ImageProcessingExtensions.Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/>
    /// to warp an image, with <see cref="TransformCanvasMode.Explicit"/> to choose the canvas of the destination points.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/GeometryTests.cs" ref:region="EstimateHomography_FromCorrespondences" ref:dedent="true">
    /// [Fact]
    /// public void EstimateHomography_FromCorrespondences()
    /// {
    ///     // The corners of a page found in a photo, and where they go: an upright 210x297 page
    ///     PointD[] photo = [new(31, 18), new(240, 42), new(225, 330), new(12, 300)];
    ///     PointD[] page = [new(0, 0), new(210, 0), new(210, 297), new(0, 297)];
    ///
    ///     var homography = ProjectiveTransform.EstimateHomography(photo, page);
    ///
    ///     // Four correspondences give the exact homography: each corner lands on its destination
    ///     for (var i = 0; i &lt; 4; i++)
    ///     {
    ///         Assert.True(homography.TryTransform(photo[i], out var point));
    ///         Assert.InRange(point.DistanceTo(page[i]), 0, 1e-9);
    ///     }
    ///
    ///     // Warping the photo onto a 210x297 canvas with it gives the four-point perspective correction
    ///     using var image = new Image&lt;Rgb24&gt;(256, 340);
    ///     for (var y = 0; y &lt; 340; y++)
    ///     {
    ///         for (var x = 0; x &lt; 256; x++)
    ///         {
    ///             image.Frames[0][x, y] = new Rgb24((byte)x, (byte)(y / 2), 100);
    ///         }
    ///     }
    ///
    ///     var background = new Rgba64(65535, 65535, 65535, 65535);
    ///     using var corrected = image.Clone();
    ///     corrected.CorrectPerspective(photo[0], photo[1], photo[2], photo[3], new Size(210, 297), new TransformOptions { BackgroundColor = background });
    ///     image.Transform(homography, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(210, 297), BackgroundColor = background });
    ///
    ///     Assert.Equal(new Size(210, 297), image.Size);
    ///     for (var y = 0; y &lt; 297; y++)
    ///     {
    ///         for (var x = 0; x &lt; 210; x++)
    ///         {
    ///             Assert.InRange(Math.Abs(image.Frames[0][x, y].R - corrected.Frames[0][x, y].R), 0, 1);
    ///             Assert.InRange(Math.Abs(image.Frames[0][x, y].G - corrected.Frames[0][x, y].G), 0, 1);
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <param name="source">The source points.</param>
    /// <param name="destination">The destination points, in the same order.</param>
    /// <returns>The homography.</returns>
    /// <exception cref="ArgumentException">The spans do not have the same length, have fewer than four points, hold a coordinate that is not finite, or the points determine no homography (too many of them on a line).</exception>
    public static ProjectiveTransform EstimateHomography(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination)
    {
        ValidateCorrespondences(source, destination);
        var indices = new int[source.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        if (!HomographyEstimator.TryFit(source, destination, indices, out var transform))
            throw new ArgumentException("The points determine no homography: too many of them are on a line.", nameof(source));

        return transform;
    }

    /// <summary>
    /// Estimates the homography that maps the source points onto their destination points when some of the correspondences
    /// are wrong, such as the corners matched between two photos: RANSAC keeps the homography that the most correspondences
    /// agree on, and refits it on them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is RANSAC (Fischler and Bolles, 1981): <see cref="HomographyRansacOptions.Iterations"/> samples of four
    /// correspondences are drawn with a generator seeded by <see cref="HomographyRansacOptions.Seed"/>, and the exact
    /// homography of each sample is scored by its inliers, the correspondences whose source point lands within
    /// <see cref="HomographyRansacOptions.Threshold"/> of its destination. The homography with the most inliers (then the
    /// smallest sum of their squared errors, then the first one) is refitted by least squares on its inliers
    /// (<see cref="EstimateHomography(ReadOnlySpan{PointD}, ReadOnlySpan{PointD})"/>), and the refit is kept when it has at
    /// least as many inliers. The same points and options give the same result on every platform.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/GeometryTests.cs" ref:region="EstimateHomographyRobust_IgnoreTheOutliers" ref:dedent="true">
    /// [Fact]
    /// public void EstimateHomographyRobust_IgnoreTheOutliers()
    /// {
    ///     // 20 corners matched between two photos of a poster, and 5 wrong matches
    ///     var truth = ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 100, 100), new PointD(10, 12), new PointD(118, 5), new PointD(125, 110), new PointD(4, 98));
    ///     var source = new List&lt;PointD&gt;();
    ///     var destination = new List&lt;PointD&gt;();
    ///     for (var i = 0; i &lt; 20; i++)
    ///     {
    ///         var point = new PointD(5 + (i * 37 % 90), 3 + (i * 53 % 94));
    ///         Assert.True(truth.TryTransform(point, out var image));
    ///         source.Add(point);
    ///         destination.Add(image);
    ///     }
    ///
    ///     for (var i = 0; i &lt; 5; i++)
    ///     {
    ///         source.Add(new PointD(20 + (i * 15), 50));
    ///         destination.Add(new PointD(200 - (i * 31), 10 + (i * 40)));
    ///     }
    ///
    ///     // RANSAC keeps the homography the 20 good matches agree on
    ///     var estimate = ProjectiveTransform.EstimateHomographyRobust([.. source], [.. destination]);
    ///
    ///     Assert.Equal(20, estimate.InlierCount);
    ///     Assert.All(Enumerable.Range(0, 25), i =&gt; Assert.Equal(i &lt; 20, estimate.Inliers[i]));
    ///     Assert.InRange(estimate.RootMeanSquareError, 0, 1e-6);
    /// }
    /// </code>
    /// </example>
    /// <param name="source">The source points.</param>
    /// <param name="destination">The destination points, in the same order.</param>
    /// <param name="options">The threshold, the number of samples and the seed, or <see langword="null"/> for <see cref="HomographyRansacOptions.Default"/>.</param>
    /// <returns>The homography, its inliers and their error.</returns>
    /// <exception cref="ArgumentException">The spans do not have the same length, have fewer than four points, hold a coordinate that is not finite, or no sample determines a homography.</exception>
    public static HomographyEstimate EstimateHomographyRobust(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination, HomographyRansacOptions? options = null)
    {
        ValidateCorrespondences(source, destination);
        options ??= HomographyRansacOptions.Default;
        if (!HomographyEstimator.TryFitRobust(source, destination, options, out var transform, out var inliers))
            throw new ArgumentException("No sample of the points determines a homography: too many of them are on a line.", nameof(source));

        var count = 0;
        var squaredErrors = 0.0;
        for (var i = 0; i < inliers.Length; i++)
        {
            if (inliers[i])
            {
                var error = HomographyEstimator.GetError(transform, source[i], destination[i]);
                count++;
                squaredErrors += error * error;
            }
        }

        return new HomographyEstimate(transform, inliers, count, count == 0 ? 0 : Math.Sqrt(squaredErrors / count));
    }

    private static void ValidateCorrespondences(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination)
    {
        if (source.Length != destination.Length)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"There are {source.Length} source points and {destination.Length} destination points: each source point needs a destination point."), nameof(destination));

        if (source.Length < 4)
            throw new ArgumentException("A homography needs at least four correspondences.", nameof(source));

        for (var i = 0; i < source.Length; i++)
        {
            if (!double.IsFinite(source[i].X) || !double.IsFinite(source[i].Y))
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The source point {i} has a coordinate that is not finite."), nameof(source));

            if (!double.IsFinite(destination[i].X) || !double.IsFinite(destination[i].Y))
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination point {i} has a coordinate that is not finite."), nameof(destination));
        }
    }
}
