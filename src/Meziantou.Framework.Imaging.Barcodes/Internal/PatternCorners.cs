namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Finds the corners of a square ring of a pattern of concentric squares (the finder pattern of a QR code, the bullseye
/// of an Aztec symbol) in a photograph, where the perspective makes it any convex quadrilateral: rays from the center
/// find the edge of the ring, and a line fitted to the edge points of each side gives the corners.
/// </summary>
internal static class PatternCorners
{
    private const int RayCount = 72;

    /// <summary>Gets the corners of the edge that rays from the center cross after a number of changes of color, clockwise in the image.</summary>
    /// <param name="image">The image.</param>
    /// <param name="center">The center of the pattern, which is in its center module.</param>
    /// <param name="moduleSize">The estimated size of a module, in pixels.</param>
    /// <param name="transitions">The number of changes of color from the center to the edge.</param>
    /// <returns>The 4 corners, or <see langword="null"/> when the edge does not look like a quadrilateral.</returns>
    public static PointD[]? Find(BinaryImage image, PointD center, double moduleSize, int transitions)
    {
        var centerIsDark = image.IsDark(center);
        var step = Math.Clamp(moduleSize / 8, 0.25, 1);

        // The edge is `transitions` - 0.5 modules from the center along the axes, √2 more at the corners; the perspective
        // can stretch it
        var maximum = moduleSize * (transitions + 1) * 2.5;
        var points = new List<(double Angle, PointD Point)>(RayCount);
        for (var k = 0; k < RayCount; k++)
        {
            var angle = k * 2 * Math.PI / RayCount;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);
            var dark = centerIsDark;
            var count = 0;
            for (var distance = step; distance < maximum; distance += step)
            {
                var isDark = image.IsDark(center.X + (dx * distance), center.Y + (dy * distance));
                if (isDark == dark)
                    continue;

                dark = isDark;
                if (++count == transitions)
                {
                    var edge = distance - (step / 2);
                    points.Add((angle, new PointD(center.X + (dx * edge), center.Y + (dy * edge))));
                    break;
                }
            }
        }

        if (points.Count < RayCount * 3 / 4)
            return null;

        return FitQuadrilateral(points, center);
    }

    private static PointD[]? FitQuadrilateral(List<(double Angle, PointD Point)> points, PointD center)
    {
        // Rough corners: the farthest point from the center, the farthest from it, then the farthest on each side of the
        // diagonal between them
        var c0 = points.MaxBy(point => point.Point.DistanceTo(center)).Point;
        var c2 = points.MaxBy(point => point.Point.DistanceTo(c0)).Point;
        var dx = c2.X - c0.X;
        var dy = c2.Y - c0.Y;
        double Side(PointD point) => (dx * (point.Y - c0.Y)) - (dy * (point.X - c0.X));
        var positive = points.Where(point => Side(point.Point) > 0).ToList();
        var negative = points.Where(point => Side(point.Point) < 0).ToList();
        if (positive.Count is 0 || negative.Count is 0)
            return null;

        var c1 = positive.MaxBy(point => Side(point.Point)).Point;
        var c3 = negative.MinBy(point => Side(point.Point)).Point;
        PointD[] corners = [c0, c1, c2, c3];

        // Clockwise in the image (y down): the angles from the center increase
        var ordered = corners.OrderBy(corner => Math.Atan2(corner.Y - center.Y, corner.X - center.X)).ToArray();

        // Each side: a line through the edge points between its two corners, away from the corners where the edge is
        // rounded by the blur
        var lines = new (PointD Point, PointD Direction)?[4];
        for (var side = 0; side < 4; side++)
        {
            var from = ordered[side];
            var to = ordered[(side + 1) % 4];
            var length = from.DistanceTo(to);
            if (length < 2)
                return null;

            var sidePoints = new List<PointD>();
            foreach (var (_, point) in points)
            {
                // The projection on the side, and the distance to it
                var t = (((point.X - from.X) * (to.X - from.X)) + ((point.Y - from.Y) * (to.Y - from.Y))) / (length * length);
                var distance = Math.Abs(((to.X - from.X) * (point.Y - from.Y)) - ((to.Y - from.Y) * (point.X - from.X))) / length;
                if (t is > 0.15 and < 0.85 && distance < length * 0.15)
                {
                    sidePoints.Add(point);
                }
            }

            lines[side] = sidePoints.Count >= 3 ? Geometry.FitLine(sidePoints) : null;
        }

        var result = new PointD[4];
        for (var corner = 0; corner < 4; corner++)
        {
            // The corner between the side before it and the side after it
            var before = lines[(corner + 3) % 4];
            var after = lines[corner];
            result[corner] = before is { } a && after is { } b && Geometry.Intersect(a, b) is { } intersection && intersection.DistanceTo(ordered[corner]) < ordered[corner].DistanceTo(center) * 0.5
                ? intersection
                : ordered[corner];
        }

        return IsConvex(result) ? result : null;
    }

    private static bool IsConvex(PointD[] corners)
    {
        var sign = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var c = corners[(i + 2) % 4];
            var cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
            var current = Math.Sign(cross);
            if (current is 0)
                return false;

            if (sign is 0)
            {
                sign = current;
            }
            else if (sign != current)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Matches the corners of a pattern with the corners of a square in module coordinates: the corner whose offset from
    /// the center is (-, -) along the axes of the symbol is the top-left one, and so on.
    /// </summary>
    /// <param name="corners">The corners of the pattern in the image.</param>
    /// <param name="center">The center of the pattern in the image.</param>
    /// <param name="u">The direction of the columns of the symbol in the image.</param>
    /// <param name="v">The direction of the rows of the symbol in the image.</param>
    /// <returns>The corners in the order (-, -), (+, -), (+, +), (-, +), or <see langword="null"/> when two corners are on the same side.</returns>
    public static PointD[]? MatchToAxes(PointD[] corners, PointD center, PointD u, PointD v)
    {
        var determinant = (u.X * v.Y) - (u.Y * v.X);
        if (Math.Abs(determinant) < 1e-9)
            return null;

        var result = new PointD?[4];
        foreach (var corner in corners)
        {
            // Solve corner - center = a u + b v
            var x = corner.X - center.X;
            var y = corner.Y - center.Y;
            var a = ((x * v.Y) - (y * v.X)) / determinant;
            var b = ((u.X * y) - (u.Y * x)) / determinant;
            var index = (a, b) switch
            {
                ( < 0, < 0) => 0,
                ( >= 0, < 0) => 1,
                ( >= 0, >= 0) => 2,
                _ => 3,
            };

            if (result[index] is not null)
                return null;

            result[index] = corner;
        }

        return [result[0]!.Value, result[1]!.Value, result[2]!.Value, result[3]!.Value];
    }
}
