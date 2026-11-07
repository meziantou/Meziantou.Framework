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

    public ProjectiveView(in ProjectiveTransform transform, double left, double top, double right, double bottom, double reach)
        : this(transform, left, top, right, bottom, reach, reach)
    {
    }

    public ProjectiveView(in ProjectiveTransform transform, double left, double top, double right, double bottom, double reach, double endReach)
    {
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

    /// <summary>Gets the same view of the shape of what is drawn itself, with nothing around it.</summary>
    public ProjectiveView WithoutReach() => new(Transform, Left, Top, Right, Bottom, reach: 0);

    /// <summary>Gets a bound of the factor by which the transformation enlarges a length in the part of a rectangle of the plane that is seen, with <see cref="Reach"/> around it.</summary>
    public double GetScale(double minX, double minY, double maxX, double maxY) => GetScaleWithReach(minX, minY, maxX, maxY, Reach);

    /// <summary>Gets a bound of the factor by which the transformation enlarges a length in the part of a rectangle of the plane that is seen, with <see cref="EndReach"/> around it: a part of a curve at one of its ends.</summary>
    public double GetEndScale(double minX, double minY, double maxX, double maxY) => GetScaleWithReach(minX, minY, maxX, maxY, EndReach);

    private double GetScaleWithReach(double minX, double minY, double maxX, double maxY, double reach)
    {
        // The rectangle, then each half-plane cuts the polygon (at most 8 vertices: one more per cut)
        Span<double> polygon = stackalloc double[16];
        Span<double> cut = stackalloc double[16];
        polygon[0] = minX - reach;
        polygon[1] = minY - reach;
        polygon[2] = maxX + reach;
        polygon[3] = minY - reach;
        polygon[4] = maxX + reach;
        polygon[5] = maxY + reach;
        polygon[6] = minX - reach;
        polygon[7] = maxY + reach;
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
