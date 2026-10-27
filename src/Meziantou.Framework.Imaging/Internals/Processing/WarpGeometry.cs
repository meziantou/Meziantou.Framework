using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Where a transformed image lands on its canvas (<see cref="ImageProcessingExtensions.Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/>):
/// the size of the canvas, the transformation from the source to the canvas and its inverse, and the widening of the
/// kernels where the transformation shrinks the image.
/// </summary>
/// <remarks>
/// <para>
/// The source covers <c>[0, width] x [0, height]</c>: pixel <c>(i, j)</c> is the unit square centered at
/// <c>(i + 1/2, j + 1/2)</c>, in the source and on the canvas alike. Output pixel <c>(x, y)</c> takes the value of the
/// source at the point its center comes from, <c>Inverse(x + 1/2, y + 1/2)</c>.
/// </para>
/// <para>
/// The kernel of an axis of the source is widened by <c>f = max(1, 1 / |A e|)</c>, where <c>A</c> is the derivative of
/// the transformation at that point and <c>e</c> the unit vector of the axis: the factor by which a source pixel is
/// shrunk along that axis. A rotation or a skew shrinks nothing and widens nothing; a reduction to half the size widens
/// both kernels by 2, as a resize does. An affine transformation has the same factors everywhere. A projective one has
/// factors of its own at every point, limited to <see cref="MaxProjectiveFilterScale"/>, since they grow without bound
/// near the horizon.
/// </para>
/// </remarks>
internal sealed class WarpGeometry
{
    /// <summary>The largest widening of the kernels of a projective transformation.</summary>
    internal const double MaxProjectiveFilterScale = 16;

    /// <summary>
    /// The distance to an integer under which the size of an expanded canvas is that integer: the bounds of a skew, or of a
    /// scale by 0.1 then by 10, are computed with rounding errors that must not add a column of background.
    /// </summary>
    internal const double SizeTolerance = 1e-6;

    // Beyond this magnitude a coordinate is far outside any canvas: kept away from the overflow of the integer conversions
    private const double MaxCoordinate = 1e12;

    private readonly AffineTransform _affineForward;
    private readonly AffineTransform _affineInverse;

    private WarpGeometry(Size sourceSize, Size outputSize, ProjectiveTransform forward, ProjectiveTransform inverse)
    {
        SourceSize = sourceSize;
        OutputSize = outputSize;
        Forward = forward;
        Inverse = inverse;
        IsAffine = forward.TryGetExactAffine(out _affineForward) && inverse.TryGetExactAffine(out _affineInverse);
        if (IsAffine)
        {
            GetAffineFilterScales(_affineInverse, out var scaleX, out var scaleY);
            ScaleX = scaleX;
            ScaleY = scaleY;
            Permutation = FindPermutation();
        }
    }

    /// <summary>Gets the size of the source.</summary>
    public Size SourceSize { get; }

    /// <summary>Gets the size of the canvas.</summary>
    public Size OutputSize { get; }

    /// <summary>Gets the transformation from the source to the canvas.</summary>
    public ProjectiveTransform Forward { get; }

    /// <summary>Gets the transformation from the canvas to the source.</summary>
    public ProjectiveTransform Inverse { get; }

    /// <summary>Gets a value indicating whether both transformations are affine (<c>W</c> is 1 everywhere).</summary>
    public bool IsAffine { get; }

    /// <summary>Gets the horizontal widening of the kernels of an affine transformation.</summary>
    public double ScaleX { get; } = 1;

    /// <summary>Gets the vertical widening of the kernels of an affine transformation.</summary>
    public double ScaleY { get; } = 1;

    /// <summary>
    /// Gets the permutation of the pixels that the transformation is, when it moves every pixel onto a pixel of the canvas
    /// and fills the whole canvas (a quarter turn, a mirror, an integer translation of a canvas of the same size), or
    /// <see langword="null"/>.
    /// </summary>
    public OrientationTransform? Permutation { get; }

    /// <summary>Gets a value indicating whether the transformation leaves every pixel where it is.</summary>
    public bool IsIdentity => Permutation is { IsIdentity: true };

    /// <summary>Computes the canvas and the placement of a transformation.</summary>
    /// <param name="source">The size of the image.</param>
    /// <param name="transform">The transformation, from the source to the plane of the result.</param>
    /// <param name="options">The options.</param>
    /// <param name="centered">
    /// <see langword="true"/> when <paramref name="transform"/> moves the center of the image to the origin
    /// (<see cref="ImageProcessingExtensions.Rotate(Image, double, TransformOptions?, CancellationToken)"/>, <see cref="ImageProcessingExtensions.Skew"/>):
    /// the origin is then placed at the center of the canvas, whatever its mode.
    /// </param>
    /// <param name="paramName">The parameter that describes the transformation, for the exceptions.</param>
    /// <param name="limits">The limits of the canvas.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="ArgumentException">
    /// The transformation flattens the image, cannot be inverted, or puts part of the image on or behind the horizon while
    /// the canvas must hold all of it.
    /// </exception>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds <paramref name="limits"/>.</exception>
    public static WarpGeometry Compute(Size source, ProjectiveTransform transform, TransformOptions options, bool centered, string paramName, ImageResourceLimits limits)
    {
        transform = transform.Normalize();
        if (transform.IsFlat)
            throw new ArgumentException("The transformation flattens the image to a line or a point.", paramName);

        Size outputSize;
        ProjectiveTransform forward;
        if (options.CanvasMode == TransformCanvasMode.Expand)
        {
            var bounds = GetBounds(source, transform, paramName);
            var width = GetExpandedSize(bounds.Width);
            var height = GetExpandedSize(bounds.Height);

            // Checked before the size is narrowed: the limits report the size the canvas would have
            limits.EnsureCanvasWithinLimits(width, height);
            outputSize = new Size((int)width, (int)height);

            // The center of the bounds goes to the center of the canvas, whatever the translation of the transformation
            var dx = ((outputSize.Width - bounds.Width) / 2) - bounds.X;
            var dy = ((outputSize.Height - bounds.Height) / 2) - bounds.Y;
            forward = transform.Then(AffineTransform.CreateTranslation(dx, dy));
        }
        else
        {
            outputSize = options.CanvasMode == TransformCanvasMode.Keep ? source : options.CanvasSize;
            limits.EnsureCanvasWithinLimits(outputSize.Width, outputSize.Height);
            forward = centered ? transform.Then(AffineTransform.CreateTranslation(outputSize.Width / 2.0, outputSize.Height / 2.0)) : transform;
        }

        forward = forward.Normalize();
        if (!forward.TryInvert(out var inverse))
            throw new ArgumentException("The transformation cannot be inverted.", paramName);

        return new WarpGeometry(source, outputSize, forward, inverse.Normalize());
    }

    /// <summary>
    /// Gets the size of an expanded canvas from the size of the bounds of the transformed image: that size when it is within
    /// <see cref="SizeTolerance"/> of an integer, otherwise the next integer, and at least one pixel.
    /// </summary>
    internal static long GetExpandedSize(double extent)
    {
        if (!(extent < MaxCoordinate))
            return long.MaxValue;

        var rounded = Math.Round(extent, MidpointRounding.AwayFromZero);
        var size = Math.Abs(extent - rounded) <= SizeTolerance ? rounded : Math.Ceiling(extent);
        return Math.Max(1, (long)size);
    }

    /// <summary>Gets the point of the source that the center of output pixel <c>(x, y)</c> comes from.</summary>
    /// <returns><see langword="false"/> when the point is not the image of a point of the source (on or beyond the horizon).</returns>
    public bool TryMapToSource(int x, int y, out double u, out double v)
    {
        var px = x + 0.5;
        var py = y + 0.5;
        if (IsAffine)
        {
            u = _affineInverse.TransformX(px, py);
            v = _affineInverse.TransformY(px, py);
            return true;
        }

        var w = Inverse.TransformW(px, py);
        if (w > 0)
        {
            u = Inverse.TransformX(px, py) / w;
            v = Inverse.TransformY(px, py) / w;
            if (double.IsFinite(u) && double.IsFinite(v))
                return true;
        }

        u = 0;
        v = 0;
        return false;
    }

    /// <summary>Gets the widening of the kernels at the point of the source <c>(u, v)</c> that output pixel <c>(x, y)</c> comes from.</summary>
    public void GetFilterScales(int x, int y, double u, double v, out double scaleX, out double scaleY)
    {
        if (IsAffine)
        {
            scaleX = ScaleX;
            scaleY = ScaleY;
            return;
        }

        // The derivatives of u = X / W and v = Y / W, with X, Y and W those of the inverse
        var px = x + 0.5;
        var py = y + 0.5;
        var w = Inverse.TransformW(px, py);
        var dudx = (Inverse.M11 - (u * Inverse.M13)) / w;
        var dudy = (Inverse.M21 - (u * Inverse.M23)) / w;
        var dvdx = (Inverse.M12 - (v * Inverse.M13)) / w;
        var dvdy = (Inverse.M22 - (v * Inverse.M23)) / w;
        GetFilterScales(dudx, dudy, dvdx, dvdy, out scaleX, out scaleY);
        scaleX = Math.Min(scaleX, MaxProjectiveFilterScale);
        scaleY = Math.Min(scaleY, MaxProjectiveFilterScale);
    }

    /// <summary>Gets the largest widening of the horizontal and vertical kernels over the whole canvas.</summary>
    public void GetMaxFilterScales(out double scaleX, out double scaleY)
    {
        if (IsAffine)
        {
            scaleX = ScaleX;
            scaleY = ScaleY;
            return;
        }

        scaleX = MaxProjectiveFilterScale;
        scaleY = MaxProjectiveFilterScale;
    }

    /// <summary>
    /// Gets where a pixel of the source lands on the canvas: the pixel that contains the image of its center (the new
    /// position of a cursor hotspot).
    /// </summary>
    /// <returns><see langword="false"/> when that point is not on the canvas, or has no image.</returns>
    public bool TryMapPoint(Point source, out Point destination)
    {
        if (Forward.TryTransform(new PointD(source.X + 0.5, source.Y + 0.5), out var point))
        {
            var x = Math.Floor(point.X);
            var y = Math.Floor(point.Y);
            if (x >= 0 && y >= 0 && x < OutputSize.Width && y < OutputSize.Height)
            {
                destination = new Point((int)x, (int)y);
                return true;
            }
        }

        destination = default;
        return false;
    }

    /// <summary>
    /// Gets a value indicating whether an output pixel may read the source beyond its edges, and therefore show the
    /// background. A projective transformation is always assumed to.
    /// </summary>
    public bool ReadsBeyondTheSource(ResamplingFilter filter)
    {
        if (!IsAffine)
            return true;

        var width = OutputSize.Width - 1;
        var height = OutputSize.Height - 1;

        // When every output pixel center comes from a source pixel center (a quarter turn or an integer translation), an
        // interpolating filter reads that pixel alone: its other weights are those of the kernel at non-zero integers, which
        // are zero
        var integral = filter != ResamplingFilter.NearestNeighbor && ResamplingKernels.IsInterpolating(filter) && IsIntegral(_affineInverse.M11) && IsIntegral(_affineInverse.M12) && IsIntegral(_affineInverse.M21) && IsIntegral(_affineInverse.M22)
            && ScaleX == 1 && ScaleY == 1
            && TryMapToSource(0, 0, out var u0, out var v0) && IsIntegral(u0 - 0.5) && IsIntegral(v0 - 0.5);

        // The points the centers come from form a parallelogram whose corners are those of the canvas, and the reach of
        // the kernels is the same everywhere: the four corner pixels read the farthest
        ReadOnlySpan<(int X, int Y)> corners = [(0, 0), (width, 0), (0, height), (width, height)];
        foreach (var (x, y) in corners)
        {
            _ = TryMapToSource(x, y, out var u, out var v);
            if (filter == ResamplingFilter.NearestNeighbor || integral)
            {
                // The pixel that contains the point (a center, for an integral mapping)
                if (!(Math.Floor(u) >= 0 && Math.Floor(u) < SourceSize.Width && Math.Floor(v) >= 0 && Math.Floor(v) < SourceSize.Height))
                    return true;

                continue;
            }

            var radius = ResamplingKernels.GetRadius(filter);
            if (!IsWithin(filter, u - 0.5, radius * ScaleX, SourceSize.Width) || !IsWithin(filter, v - 0.5, radius * ScaleY, SourceSize.Height))
                return true;
        }

        return false;

        static bool IsWithin(ResamplingFilter filter, double center, double reach, int length)
        {
            GetWindow(filter, center, reach, out var first, out var last);
            return first >= 0 && last < length;
        }
    }

    /// <summary>
    /// Gets the source pixels whose center is closer to <paramref name="center"/> than <paramref name="reach"/>: the
    /// contributors of a kernel of that radius, every one of them inside the support. The box filter, whose kernel is not
    /// zero at its radius, takes the half-open interval <c>(center - reach, center + reach]</c> of the box resize
    /// (<see cref="ResampleAxis.GetBoxContributors"/>): a point on the edge of two pixels selects the higher one.
    /// </summary>
    /// <returns><see langword="false"/> when the center is too far from any canvas to be computed (it reads nothing).</returns>
    internal static bool GetWindow(ResamplingFilter filter, double center, double reach, out long first, out long last)
    {
        if (!(Math.Abs(center) < MaxCoordinate && reach < MaxCoordinate))
        {
            first = 0;
            last = -1;
            return false;
        }

        first = (long)Math.Floor(center - reach) + 1;
        last = filter is ResamplingFilter.Box ? (long)Math.Floor(center + reach) : (long)Math.Ceiling(center + reach) - 1;
        return true;
    }

    private static bool IsIntegral(double value) => value == Math.Floor(value);

    /// <summary>The widening of the kernels of the derivatives of the inverse: <c>|det| / |row of the other axis|</c>, at least 1.</summary>
    private static void GetFilterScales(double dudx, double dudy, double dvdx, double dvdy, out double scaleX, out double scaleY)
    {
        // The image of the unit vector of the u axis of the source is the first column of the forward derivative, the
        // inverse of the derivative of the inverse: (dvdy, -dvdx) / det, of length |(dvdx, dvdy)| / |det|
        var determinant = Math.Abs((dudx * dvdy) - (dudy * dvdx));
        var scaleU = determinant / Math.Sqrt((dvdx * dvdx) + (dvdy * dvdy));
        var scaleV = determinant / Math.Sqrt((dudx * dudx) + (dudy * dudy));

        // Written so that a value that is not a number widens nothing
        scaleX = scaleU > 1 ? scaleU : 1;
        scaleY = scaleV > 1 ? scaleV : 1;
    }

    private static void GetAffineFilterScales(AffineTransform inverse, out double scaleX, out double scaleY)
        => GetFilterScales(inverse.M11, inverse.M21, inverse.M12, inverse.M22, out scaleX, out scaleY);

    /// <summary>Gets the bounds of the transformed image; every corner must be in front of the viewer.</summary>
    private static RectangleD GetBounds(Size source, ProjectiveTransform transform, string paramName)
    {
        ReadOnlySpan<PointD> corners = [new(0, 0), new(source.Width, 0), new(0, source.Height), new(source.Width, source.Height)];
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var corner in corners)
        {
            if (!transform.TryTransform(corner, out var point))
                throw new ArgumentException("The transformation puts part of the image on or behind its horizon, where it has no image: the canvas cannot hold all of it. Use TransformCanvasMode.Keep or TransformCanvasMode.Explicit.", paramName);

            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new RectangleD(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Finds the permutation of the pixels the affine transformation is: a quarter turn or a mirror whose image of the
    /// source is exactly the canvas, every pixel center going to a pixel center.
    /// </summary>
    private OrientationTransform? FindPermutation()
    {
        var forward = _affineForward;
        if (!IsUnit(forward.M11) || !IsUnit(forward.M12) || !IsUnit(forward.M21) || !IsUnit(forward.M22) || !IsIntegral(forward.OffsetX) || !IsIntegral(forward.OffsetY))
            return null;

        // One coefficient of each row: a signed permutation of the axes (the transformation is not flat)
        if (forward.M11 * forward.M12 != 0 || forward.M21 * forward.M22 != 0)
            return null;

        var bounds = forward.TransformBounds(new RectangleD(0, 0, SourceSize.Width, SourceSize.Height));
        if (bounds != new RectangleD(0, 0, OutputSize.Width, OutputSize.Height))
            return null;

        // The matrix is one of the eight symmetries of the square: the one of the EXIF orientations that moves three
        // corner pixels in the same way is that one
        var size = SourceSize;
        ReadOnlySpan<Point> points = [new(0, 0), new(size.Width - 1, 0), new(0, size.Height - 1)];
        for (var orientation = ExifOrientation.TopLeft; orientation <= ExifOrientation.LeftBottom; orientation++)
        {
            var candidate = OrientationTransform.ForOrientation(orientation);
            if (candidate.GetOutputSize(size) != OutputSize)
                continue;

            var matches = true;
            foreach (var point in points)
            {
                var target = new Point((int)Math.Floor(forward.TransformX(point.X + 0.5, point.Y + 0.5)), (int)Math.Floor(forward.TransformY(point.X + 0.5, point.Y + 0.5)));
                if (candidate.MapPoint(size, point) != target)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return candidate;
        }

        return null;

        static bool IsUnit(double value) => value is 0 or 1 or -1;
    }
}
