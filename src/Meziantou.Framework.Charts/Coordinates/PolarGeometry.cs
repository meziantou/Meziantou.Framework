namespace Meziantou.Framework.Charts;

/// <summary>Points, arcs and sectors around a center, with angles in radians, 0 at 12 o'clock, clockwise.</summary>
internal static class PolarGeometry
{
    private const double FullCircle = 2 * Math.PI;
    private const double Epsilon = 1e-9;

    public static (double X, double Y) Point(double centerX, double centerY, double radius, double angle)
        => (centerX + (radius * Math.Sin(angle)), centerY - (radius * Math.Cos(angle)));

    /// <summary>An annular sector between two radii and two angles. A sweep of a full circle is a ring; an inner radius of 0 is a pie slice.</summary>
    public static PathData Sector(double centerX, double centerY, double innerRadius, double outerRadius, double startAngle, double endAngle)
    {
        var builder = new PathBuilder();
        var sweep = endAngle - startAngle;
        innerRadius = Math.Max(0, innerRadius);
        outerRadius = Math.Max(0, outerRadius);
        if (outerRadius <= Epsilon || sweep <= Epsilon)
            return builder.Build();

        if (sweep >= FullCircle - Epsilon)
        {
            builder.AddCircle(centerX, centerY, outerRadius);
            if (innerRadius > Epsilon)
            {
                // The inner circle runs counterclockwise, so the nonzero rule leaves a hole
                builder.MoveTo(centerX, centerY - innerRadius);
                builder.ArcTo(innerRadius, innerRadius, 0, largeArc: false, sweep: false, centerX, centerY + innerRadius);
                builder.ArcTo(innerRadius, innerRadius, 0, largeArc: false, sweep: false, centerX, centerY - innerRadius);
                builder.Close();
            }

            return builder.Build();
        }

        var large = sweep > Math.PI;
        var (x0, y0) = Point(centerX, centerY, outerRadius, startAngle);
        var (x1, y1) = Point(centerX, centerY, outerRadius, endAngle);
        builder.MoveTo(x0, y0);
        builder.ArcTo(outerRadius, outerRadius, 0, large, sweep: true, x1, y1);
        if (innerRadius > Epsilon)
        {
            var (x2, y2) = Point(centerX, centerY, innerRadius, endAngle);
            var (x3, y3) = Point(centerX, centerY, innerRadius, startAngle);
            builder.LineTo(x2, y2);
            builder.ArcTo(innerRadius, innerRadius, 0, large, sweep: false, x3, y3);
        }
        else
        {
            builder.LineTo(centerX, centerY);
        }

        return builder.Close().Build();
    }

    /// <summary>An arc of a circle between two angles, clockwise. A full circle is two half arcs.</summary>
    public static PathData Arc(double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        var builder = new PathBuilder();
        var sweep = endAngle - startAngle;
        if (radius <= Epsilon || sweep <= Epsilon)
            return builder.Build();

        if (sweep >= FullCircle - Epsilon)
            return builder.AddCircle(centerX, centerY, radius).Build();

        var (x0, y0) = Point(centerX, centerY, radius, startAngle);
        var (x1, y1) = Point(centerX, centerY, radius, endAngle);
        return builder.MoveTo(x0, y0).ArcTo(radius, radius, 0, sweep > Math.PI, sweep: true, x1, y1).Build();
    }

    /// <summary>The bounding box of a sector of the unit circle, centered at the origin, with y pointing down.</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) GetUnitBounds(double innerRadius, double startAngle, double endAngle)
    {
        if (endAngle - startAngle >= FullCircle - Epsilon)
            return (-1, -1, 1, 1);

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        Include(1, startAngle);
        Include(1, endAngle);
        Include(innerRadius, startAngle);
        Include(innerRadius, endAngle);

        // The extreme points of the circle the arc passes through: 12, 3, 6 and 9 o'clock
        var first = Math.Ceiling(startAngle / (Math.PI / 2));
        for (var k = first; k * (Math.PI / 2) <= endAngle + Epsilon; k++)
        {
            Include(1, k * (Math.PI / 2));
        }

        return (minX, minY, maxX, maxY);

        void Include(double radius, double angle)
        {
            var (x, y) = Point(0, 0, radius, angle);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
    }
}
