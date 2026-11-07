namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The smooth curve through a list of points: one cubic Bézier curve between each point and the next one.</summary>
/// <remarks>
/// <b>Contract.</b> The curve from the point <c>P[i]</c> to the point <c>P[i + 1]</c> has the control points
/// <c>P[i] + k (P[i + 1] - P[i - 1])</c> and <c>P[i + 1] - k (P[i + 2] - P[i])</c>, with <c>k</c> a third of the
/// tension: at each point, the curve has the direction of the line between the two points around it. With a tension
/// of one half, it is the Catmull-Rom spline; without tension, the control points are the points and the curve is the
/// straight segments between them. An open curve takes its first point for the point before it and its last point for
/// the point after it; a closed curve goes around.
/// </remarks>
internal static class CardinalSpline
{
    /// <summary>Validates the points and the tension of a curve, and the control points they give.</summary>
    /// <exception cref="ArgumentException">There is no point, or a coordinate or the tension is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds the largest one, the tension is not between 0 and 1, or a control point of the curve is beyond the largest coordinate.</exception>
    public static void Validate(ReadOnlySpan<PointD> points, double tension, bool isClosed, string pointsName, string tensionName)
    {
        if (points.IsEmpty)
            throw new ArgumentException("At least one point is required.", pointsName);

        foreach (var point in points)
        {
            DrawingGuard.Point(point, pointsName);
        }

        if (!double.IsFinite(tension))
            throw new ArgumentException("The tension must be a finite number.", tensionName);

        if (tension < 0 || tension > 1)
            throw new ArgumentOutOfRangeException(tensionName, tension, "The tension must be between 0 and 1.");

        var count = GetSegmentCount(points.Length, isClosed);
        for (var i = 0; i < count; i++)
        {
            GetSegment(points, i, isClosed, tension, out var control1, out var control2, out _);
            if (Math.Abs(control1.X) > DrawingGuard.MaxCoordinate || Math.Abs(control1.Y) > DrawingGuard.MaxCoordinate || Math.Abs(control2.X) > DrawingGuard.MaxCoordinate || Math.Abs(control2.Y) > DrawingGuard.MaxCoordinate)
                throw new ArgumentOutOfRangeException(pointsName, "The curve goes beyond the largest coordinate (16777216).");
        }
    }

    /// <summary>Gets the number of curves between the points: one less than the points when the curve is open, and none for a single point.</summary>
    public static int GetSegmentCount(int pointCount, bool isClosed) => pointCount < 2 ? 0 : isClosed ? pointCount : pointCount - 1;

    /// <summary>Gets the curve that starts at a point.</summary>
    /// <param name="points">The points of the curve.</param>
    /// <param name="index">The index of the point the curve starts at.</param>
    /// <param name="isClosed">Whether the last point is followed by the first one.</param>
    /// <param name="tension">The tension, from 0 to 1.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The point the curve ends at.</param>
    public static void GetSegment(ReadOnlySpan<PointD> points, int index, bool isClosed, double tension, out PointD control1, out PointD control2, out PointD end)
    {
        var count = points.Length;
        var start = points[index];
        PointD before, after;
        if (isClosed)
        {
            before = points[index == 0 ? count - 1 : index - 1];
            end = points[index + 1 == count ? 0 : index + 1];
            after = points[(index + 2) % count];
        }
        else
        {
            before = points[index == 0 ? 0 : index - 1];
            end = points[index + 1];
            after = points[index + 2 < count ? index + 2 : count - 1];
        }

        var factor = tension / 3;
        control1 = new PointD(start.X + (factor * (end.X - before.X)), start.Y + (factor * (end.Y - before.Y)));
        control2 = new PointD(end.X - (factor * (after.X - start.X)), end.Y - (factor * (after.Y - start.Y)));
    }
}
