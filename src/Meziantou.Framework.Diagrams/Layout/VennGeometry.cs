namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// The areas of the intersections of circles, ported from <c>circleintersection.js</c> of venn.js
/// (https://github.com/upsetjs/venn.js, MIT, see THIRD-PARTY-NOTICES.md).
/// </summary>
internal static class VennGeometry
{
    private const double Small = 1e-10;

    /// <summary>Returns the area of the intersection of circles.</summary>
    public static double IntersectionArea(IReadOnlyList<VennCircle> circles) => IntersectionArea(circles, out _);

    /// <summary>Returns the area of the intersection of circles, and the arcs that make its outline.</summary>
    public static double IntersectionArea(IReadOnlyList<VennCircle> circles, out List<VennArc> arcs)
    {
        arcs = [];
        if (circles.Count is 0)
            return 0;

        // All the intersection points of the circles, then the ones inside every circle
        var intersectionPoints = GetIntersectionPoints(circles);
        var innerPoints = new List<VennIntersectionPoint>();
        foreach (var point in intersectionPoints)
        {
            if (ContainedInCircles(point.X, point.Y, circles))
            {
                innerPoints.Add(point);
            }
        }

        var arcArea = 0.0;
        var polygonArea = 0.0;
        if (innerPoints.Count > 1)
        {
            // Sort the points by angle from the center of the polygon, so the edges are consecutive points
            var centerX = 0.0;
            var centerY = 0.0;
            foreach (var point in innerPoints)
            {
                centerX += point.X;
                centerY += point.Y;
            }

            centerX /= innerPoints.Count;
            centerY /= innerPoints.Count;
            foreach (var point in innerPoints)
            {
                point.Angle = VennMath.Atan2(point.X - centerX, point.Y - centerY);
            }

            VennSort.StableSort(innerPoints, (a, b) => b.Angle - a.Angle);

            var p2 = innerPoints[^1];
            foreach (var p1 in innerPoints)
            {
                polygonArea += (p2.X + p1.X) * (p1.Y - p2.Y);

                // The arc between the two points, on the circle whose arc is the narrowest
                var midX = (p1.X + p2.X) / 2;
                var midY = (p1.Y + p2.Y) / 2;
                VennArc? arc = null;
                foreach (var parent in (ReadOnlySpan<int>)[p1.ParentIndex1, p1.ParentIndex2])
                {
                    if (parent != p2.ParentIndex1 && parent != p2.ParentIndex2)
                        continue;

                    var circle = circles[parent];
                    var a1 = VennMath.Atan2(p1.X - circle.X, p1.Y - circle.Y);
                    var a2 = VennMath.Atan2(p2.X - circle.X, p2.Y - circle.Y);
                    var angleDiff = a2 - a1;
                    if (angleDiff < 0)
                    {
                        angleDiff += 2 * Math.PI;
                    }

                    var a = a2 - (angleDiff / 2);
                    var width = Distance(midX, midY, circle.X + (circle.Radius * VennMath.Sin(a)), circle.Y + (circle.Radius * VennMath.Cos(a)));

                    // The width can overflow a little because of rounding errors
                    if (width > circle.Radius * 2)
                    {
                        width = circle.Radius * 2;
                    }

                    if (arc is null || arc.Width > width)
                    {
                        arc = new VennArc(circle, width, p1.X, p1.Y, p2.X, p2.Y, Large: width > circle.Radius);
                    }
                }

                if (arc is not null)
                {
                    arcs.Add(arc);
                    arcArea += CircleArea(arc.Circle.Radius, arc.Width);
                    p2 = p1;
                }
            }
        }
        else
        {
            // No intersection point: the circles are disjoint, or the smallest is inside all the others
            var smallest = circles[0];
            for (var i = 1; i < circles.Count; i++)
            {
                if (circles[i].Radius < smallest.Radius)
                {
                    smallest = circles[i];
                }
            }

            var disjoint = false;
            foreach (var circle in circles)
            {
                if (Distance(circle, smallest) > Math.Abs(smallest.Radius - circle.Radius))
                {
                    disjoint = true;
                    break;
                }
            }

            if (disjoint)
            {
                arcArea = polygonArea = 0;
            }
            else
            {
                arcArea = smallest.Radius * smallest.Radius * Math.PI;
                arcs.Add(new VennArc(smallest, smallest.Radius * 2, smallest.X, smallest.Y + smallest.Radius, smallest.X - Small, smallest.Y + smallest.Radius, Large: true));
            }
        }

        polygonArea /= 2;
        return arcArea + polygonArea;
    }

    /// <summary>Indicates whether a point is inside all the circles.</summary>
    public static bool ContainedInCircles(double x, double y, IReadOnlyList<VennCircle> circles)
    {
        foreach (var circle in circles)
        {
            if (!(Distance(x, y, circle.X, circle.Y) < circle.Radius + Small))
                return false;
        }

        return true;
    }

    /// <summary>Returns the area of a circular segment of a given width.</summary>
    public static double CircleArea(double radius, double width) => (radius * radius * VennMath.Acos(1 - (width / radius))) - ((radius - width) * Math.Sqrt(width * ((2 * radius) - width)));

    public static double Distance(VennCircle a, VennCircle b) => Distance(a.X, a.Y, b.X, b.Y);

    public static double Distance(double x1, double y1, double x2, double y2) => Math.Sqrt(((x1 - x2) * (x1 - x2)) + ((y1 - y2) * (y1 - y2)));

    /// <summary>Returns the area of the overlap of two circles whose centers are at a distance.</summary>
    public static double CircleOverlap(double r1, double r2, double d)
    {
        if (d >= r1 + r2)
            return 0;

        if (d <= Math.Abs(r1 - r2))
            return Math.PI * Math.Min(r1, r2) * Math.Min(r1, r2);

        var w1 = r1 - (((d * d) - (r2 * r2) + (r1 * r1)) / (2 * d));
        var w2 = r2 - (((d * d) - (r1 * r1) + (r2 * r2)) / (2 * d));
        return CircleArea(r1, w1) + CircleArea(r2, w2);
    }

    /// <summary>Returns the two points where two circles cross, or none when they do not cross.</summary>
    public static List<(double X, double Y)> CircleCircleIntersection(double x1, double y1, double r1, double x2, double y2, double r2)
    {
        var d = Distance(x1, y1, x2, y2);
        if (d >= r1 + r2 || d <= Math.Abs(r1 - r2))
            return [];

        var a = ((r1 * r1) - (r2 * r2) + (d * d)) / (2 * d);
        var h = Math.Sqrt((r1 * r1) - (a * a));
        var x0 = x1 + (a * (x2 - x1) / d);
        var y0 = y1 + (a * (y2 - y1) / d);
        var rx = -(y2 - y1) * (h / d);
        var ry = -(x2 - x1) * (h / d);
        return [(x0 + rx, y0 - ry), (x0 - rx, y0 + ry)];
    }

    private static List<VennIntersectionPoint> GetIntersectionPoints(IReadOnlyList<VennCircle> circles)
    {
        var result = new List<VennIntersectionPoint>();
        for (var i = 0; i < circles.Count; i++)
        {
            for (var j = i + 1; j < circles.Count; j++)
            {
                foreach (var (x, y) in CircleCircleIntersection(circles[i].X, circles[i].Y, circles[i].Radius, circles[j].X, circles[j].Y, circles[j].Radius))
                {
                    result.Add(new VennIntersectionPoint(x, y, i, j));
                }
            }
        }

        return result;
    }

    private sealed class VennIntersectionPoint(double x, double y, int parentIndex1, int parentIndex2)
    {
        public double X { get; } = x;

        public double Y { get; } = y;

        public int ParentIndex1 { get; } = parentIndex1;

        public int ParentIndex2 { get; } = parentIndex2;

        public double Angle { get; set; }
    }
}
