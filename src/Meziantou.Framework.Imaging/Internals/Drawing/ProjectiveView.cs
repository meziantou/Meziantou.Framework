using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A projective transformation of the canvas and the rectangle of the canvas it is seen through: what a sink needs to
/// know how much finer to cut a curve, at the place it is in the plane.
/// </summary>
/// <remarks>
/// <para>
/// The factor by which the transformation enlarges a length at a point <c>p</c> of the plane is the largest singular
/// value of its derivative, <c>(A - f(p) c^T) / W</c>, with <c>A</c> the matrix of <c>M11</c>, <c>M12</c>, <c>M21</c>
/// and <c>M22</c>, <c>c = (M13, M23)</c> and <c>f(p)</c> the image of the point. Where the image is on the rectangle
/// that is seen, <c>|f(p)|</c> is at most the largest distance <c>D</c> from the origin to a corner of the rectangle, so
/// the factor is at most <c>(|A| + D |c|) / W</c>; and <c>W</c> is affine, so on a convex polygon of points that are
/// seen it is smallest at a vertex.
/// </para>
/// <para>
/// Only what can be seen on the canvas matters: the polygon is the rectangle of a curve (its control points, enlarged
/// by <see cref="Reach"/> for what a pen draws around it) cut by the four half-planes whose image is inside
/// <see cref="Left"/>, <see cref="Top"/>, <see cref="Right"/> and <see cref="Bottom"/> (in the plane, <c>X &gt;= Left W</c>,
/// and so on, which also make <c>W</c> positive). A curve whose rectangle has no part there is not seen: its chords stay
/// in the convex hull of its control points, where nothing is seen either, and the winding number of a point of the
/// canvas does not depend on them. Such a curve gets the smallest factor, and so do the parts of the plane that the
/// transformation reduces a lot (beyond the horizon of what is seen, curves are cut no finer than a factor of
/// <see cref="MinScale"/>); the factor is at most <see cref="MaxScale"/>.
/// </para>
/// <para>
/// What a pen draws around the inside of a curve is within half its width (or the corners of its square caps, at the
/// ends of dashes): the vertices of its chords are smooth, and joined with round joins. Only at the ends of a curve,
/// where it can meet another segment at a corner, does a miter reach further: <see cref="EndReach"/>. A curve is cut
/// for <see cref="Reach"/> where it is far from its ends (<see cref="GetScale"/>), and for <see cref="EndReach"/> next to
/// them (<see cref="GetEndScale"/>): the miters of a pen do not make the whole curve reach the horizon of the plane,
/// where it would be cut 65,536 times finer.
/// </para>
/// <para>
/// A curve is cut finer so that the outline of its stroke stays within the tolerance, and the outline of the inside of a
/// curve is not everywhere within the reach: it is the curve moved by half the width of the pen along its normals, on
/// each side, and the caps of the dashes that end there (<see cref="WithOutline"/>). The tangents of a part of a curve
/// are in the cone of the sides of its control polygon (the control points of its derivative); when that cone is
/// narrow, its normals are close to one direction <c>n</c>, and the outline is in two small rectangles: the rectangle
/// of the part moved by <c>h n</c> and by <c>-h n</c>, enlarged by what the spread of the normals and the caps add
/// (<see cref="GetCurveScale"/>). The pen of a wide stroke near a horizon reaches the horizon from every part of a
/// curve, while the outline of most parts lands far from it, or beyond it, where nothing is seen: only the parts whose
/// outline is near the horizon are cut finely (fuzz regression: a pen 84 pixels wide on a circle of 14 pixels was
/// 195,000 edges, all crossing each other at the vanishing point). The chords and the round joins of their vertices are
/// in these rectangles too: the direction of a chord is the one of a tangent of its arc. The sides of the pieces between
/// the two sides of the outline are straight, and stay straight in perspective.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ProjectiveView
{
    /// <summary>The largest factor a curve is cut finer by.</summary>
    public const double MaxScale = 65536;

    /// <summary>The smallest factor a curve is cut finer by.</summary>
    public const double MinScale = 1.0 / 1024;

    // |A| + D |c|: the numerator of the bound of the factor on what is seen
    private readonly double _numerator;

    // The distance of the outline from a curve along its normals, and along its tangents (the square caps of dashes);
    // negative without an outline: what is drawn around the inside of a curve is anywhere within the reach
    private readonly double _outlineAcross;
    private readonly double _outlineAlong;

    public ProjectiveView(in ProjectiveTransform transform, double left, double top, double right, double bottom, double reach)
        : this(transform, left, top, right, bottom, reach, reach)
    {
    }

    public ProjectiveView(in ProjectiveTransform transform, double left, double top, double right, double bottom, double reach, double endReach)
        : this(transform, left, top, right, bottom, reach, endReach, outlineAcross: -1, outlineAlong: 0)
    {
    }

    private ProjectiveView(in ProjectiveTransform transform, double left, double top, double right, double bottom, double reach, double endReach, double outlineAcross, double outlineAlong)
    {
        _outlineAcross = outlineAcross;
        _outlineAlong = outlineAlong;
        Transform = transform;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        Reach = reach;
        EndReach = endReach;
        IsFlat = transform.IsFlat;
        var distance = Math.Sqrt(Math.Max(Math.Max((left * left) + (top * top), (right * right) + (top * top)), Math.Max((left * left) + (bottom * bottom), (right * right) + (bottom * bottom))));
        _numerator = PathFlattener.GetLargestRadius(transform.M11, transform.M12, transform.M21, transform.M22) + (distance * Math.Sqrt((transform.M13 * transform.M13) + (transform.M23 * transform.M23)));
    }

    public ProjectiveTransform Transform { get; }

    /// <summary>Gets the left of the rectangle of the canvas that is seen, in pixels.</summary>
    public double Left { get; }

    public double Top { get; }

    public double Right { get; }

    public double Bottom { get; }

    /// <summary>Gets the distance, in the plane, around a curve that what is drawn from it can reach: the half-width of a pen.</summary>
    public double Reach { get; }

    /// <summary>Gets the distance, in the plane, around the ends of a curve that what is drawn from it can reach: <see cref="Reach"/>, or the miters of a pen.</summary>
    public double EndReach { get; }

    /// <summary>Gets a value indicating whether the transformation flattens the plane, up to rounding: nothing is seen of it.</summary>
    public bool IsFlat { get; }

    /// <summary>Gets the same view of what is drawn around a curve: the stroke of a pen.</summary>
    /// <param name="margin">The distance on the canvas around the rectangle that is seen.</param>
    /// <param name="reach">The distance in the plane around a curve.</param>
    public ProjectiveView Inflate(double margin, double reach) => Inflate(margin, reach, reach);

    /// <summary>Gets the same view of what is drawn around a curve, further around its ends: the stroke of a pen that miters its corners.</summary>
    /// <param name="margin">The distance on the canvas around the rectangle that is seen.</param>
    /// <param name="reach">The distance in the plane around a curve.</param>
    /// <param name="endReach">The distance in the plane around the ends of a curve.</param>
    public ProjectiveView Inflate(double margin, double reach, double endReach) => new(Transform, Left - margin, Top - margin, Right + margin, Bottom + margin, Reach + reach, EndReach + endReach);

    /// <summary>
    /// Gets the same view of the stroke of a pen whose outline, around the inside of a curve, is at a distance from the
    /// curve along its normals, and at most at another distance along its tangents: what <see cref="GetCurveScale"/>
    /// measures instead of <see cref="Reach"/> when the tangents of a part of a curve are close to one direction.
    /// </summary>
    /// <param name="across">The distance along the normals of the curve: half the width of the pen.</param>
    /// <param name="along">The distance along the tangents: the square caps of the dashes that end inside the curve, 0 otherwise.</param>
    public ProjectiveView WithOutline(double across, double along) => new(Transform, Left, Top, Right, Bottom, Reach, EndReach, across, along);

    /// <summary>Gets the same view of the shape of what is drawn itself, with nothing around it.</summary>
    public ProjectiveView WithoutReach() => new(Transform, Left, Top, Right, Bottom, reach: 0);

    /// <summary>Gets a bound of the factor by which the transformation enlarges a length in the part of a rectangle of the plane that is seen, with <see cref="Reach"/> around it.</summary>
    public double GetScale(double minX, double minY, double maxX, double maxY) => GetScaleWithReach(minX, minY, maxX, maxY, Reach);

    /// <summary>Gets a bound of the factor by which the transformation enlarges a length in the part of a rectangle of the plane that is seen, with <see cref="EndReach"/> around it: a part of a curve at one of its ends.</summary>
    public double GetEndScale(double minX, double minY, double maxX, double maxY) => GetScaleWithReach(minX, minY, maxX, maxY, EndReach);

    /// <summary>
    /// Gets a bound of the factor by which the transformation enlarges a length where the stroke of the inside of a part
    /// of a curve is seen: the part is in a rectangle of the plane, and its tangents in the cone of three directions (the
    /// sides of its control polygon; a zero direction adds nothing). Without an outline (<see cref="WithOutline"/>), or
    /// when the tangents spread too much, it is <see cref="GetScale"/>.
    /// </summary>
    public double GetCurveScale(double minX, double minY, double maxX, double maxY, double ax, double ay, double bx, double by, double cx, double cy)
    {
        var scale = GetScaleWithReach(minX, minY, maxX, maxY, Reach);
        if (!(_outlineAcross >= 0) || scale <= MinScale || !TryGetTangentCone(ax, ay, bx, by, cx, cy, out var directionX, out var directionY, out var spread))
            return scale;

        // A unit tangent t of the part is within the spread of the direction d, and its normal n within the spread of the
        // normal of d: the outline, at h n + s t from a point of the part (|s| at most the reach along the tangents), is
        // within (h + |s|) spread of h n(d) + s d
        var across = _outlineAcross;
        var along = _outlineAlong;
        var normalX = -directionY * across;
        var normalY = directionX * across;
        var extentX = (along * Math.Abs(directionX)) + ((across + along) * spread);
        var extentY = (along * Math.Abs(directionY)) + ((across + along) * spread);
        var first = GetScaleOfRectangle(minX + normalX - extentX, minY + normalY - extentY, maxX + normalX + extentX, maxY + normalY + extentY);
        if (!(first < scale))
            return scale;

        var second = GetScaleOfRectangle(minX - normalX - extentX, minY - normalY - extentY, maxX - normalX + extentX, maxY - normalY + extentY);
        return Math.Min(Math.Max(first, second), scale);
    }

    /// <summary>
    /// Gets a unit direction <c>d</c> and a spread <c>e</c> such that every unit vector of the cone of three directions
    /// is within <c>e</c> of <c>d</c>, when the cone is less than a third of a turn wide.
    /// </summary>
    private static bool TryGetTangentCone(double ax, double ay, double bx, double by, double cx, double cy, out double directionX, out double directionY, out double spread)
    {
        Normalize(ref ax, ref ay);
        Normalize(ref bx, ref by);
        Normalize(ref cx, ref cy);
        directionX = ax + bx + cx;
        directionY = ay + by + cy;
        var length = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        spread = 0;
        if (!(length > 0 && double.IsFinite(length)))
            return false;

        directionX /= length;
        directionY /= length;

        // Every direction is within an angle of d whose cosine is the smallest dot product: below a quarter of a turn,
        // the vectors within that angle of d are a convex cone, which holds the cone of the directions. A unit vector at
        // that angle is at sqrt(2 - 2 cos) from d.
        var cosine = 1.0;
        cosine = SmallestCosine(cosine, ax, ay, directionX, directionY);
        cosine = SmallestCosine(cosine, bx, by, directionX, directionY);
        cosine = SmallestCosine(cosine, cx, cy, directionX, directionY);
        if (!(cosine >= 0.5))
            return false;

        spread = Math.Sqrt(2 - (2 * cosine));
        return true;

        static double SmallestCosine(double cosine, double x, double y, double directionX, double directionY)
            => x == 0 && y == 0 ? cosine : Math.Min(cosine, (x * directionX) + (y * directionY));

        static void Normalize(ref double x, ref double y)
        {
            // A zero direction adds nothing to the cone. One too small for its length to be computed gives infinities,
            // or not a number, and the cone is rejected.
            if (x == 0 && y == 0)
                return;

            var length = Math.Sqrt((x * x) + (y * y));
            x /= length;
            y /= length;
        }
    }

    private double GetScaleWithReach(double minX, double minY, double maxX, double maxY, double reach)
        => GetScaleOfRectangle(minX - reach, minY - reach, maxX + reach, maxY + reach);

    private double GetScaleOfRectangle(double minX, double minY, double maxX, double maxY)
    {
        // The rectangle, then each half-plane cuts the polygon (at most 8 vertices: one more per cut)
        Span<double> polygon = stackalloc double[16];
        Span<double> cut = stackalloc double[16];
        polygon[0] = minX;
        polygon[1] = minY;
        polygon[2] = maxX;
        polygon[3] = minY;
        polygon[4] = maxX;
        polygon[5] = maxY;
        polygon[6] = minX;
        polygon[7] = maxY;
        var count = 4;
        for (var plane = 0; plane < 4 && count > 0; plane++)
        {
            var kept = 0;
            for (var i = 0; i < count; i++)
            {
                var ax = polygon[2 * i];
                var ay = polygon[(2 * i) + 1];
                var j = i + 1 == count ? 0 : i + 1;
                var bx = polygon[2 * j];
                var by = polygon[(2 * j) + 1];
                var a = Evaluate(plane, ax, ay);
                var b = Evaluate(plane, bx, by);
                if (a >= 0)
                {
                    cut[2 * kept] = ax;
                    cut[(2 * kept) + 1] = ay;
                    kept++;
                }

                if ((a >= 0) != (b >= 0) && kept < 8)
                {
                    var t = a / (a - b);
                    cut[2 * kept] = ax + (t * (bx - ax));
                    cut[(2 * kept) + 1] = ay + (t * (by - ay));
                    kept++;
                }
            }

            cut[..(2 * kept)].CopyTo(polygon);
            count = kept;
        }

        if (count == 0)
            return MinScale;

        var smallestW = double.PositiveInfinity;
        for (var i = 0; i < count; i++)
        {
            smallestW = Math.Min(smallestW, Transform.TransformW(polygon[2 * i], polygon[(2 * i) + 1]));
        }

        // Written so that a value that is not a number gives the largest factor
        var bound = _numerator / smallestW;
        return smallestW > 0 && bound < MaxScale ? Math.Max(bound, MinScale) : MaxScale;
    }

    /// <summary>Gets the value of the function of a half-plane at a point: positive or zero where its image is on the side of the rectangle.</summary>
    private double Evaluate(int plane, double x, double y)
    {
        var w = Transform.TransformW(x, y);
        return plane switch
        {
            0 => Transform.TransformX(x, y) - (Left * w),
            1 => (Right * w) - Transform.TransformX(x, y),
            2 => Transform.TransformY(x, y) - (Top * w),
            _ => (Bottom * w) - Transform.TransformY(x, y),
        };
    }
}
