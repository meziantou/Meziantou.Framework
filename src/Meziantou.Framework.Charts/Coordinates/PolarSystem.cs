namespace Meziantou.Framework.Charts;

/// <summary>Polar coordinates: the angular scale maps to radians (0 at 12 o'clock, clockwise), the radial scale to pixels from the center.</summary>
internal sealed class PolarSystem(bool transposed, double centerX, double centerY, double innerRadius, double outerRadius, double startAngle, double endAngle) : CoordinateSystem(transposed)
{
    public override bool IsPolar => true;

    public double CenterX { get; } = centerX;

    public double CenterY { get; } = centerY;

    public double InnerRadius { get; } = innerRadius;

    public double OuterRadius { get; } = outerRadius;

    /// <summary>The start of the angular range, in radians.</summary>
    public double StartAngle { get; } = startAngle;

    /// <summary>The end of the angular range, in radians.</summary>
    public double EndAngle { get; } = endAngle;

    public bool IsFullCircle => Math.Abs(EndAngle - StartAngle) >= (2 * Math.PI) - 1e-9;

    public override (double X, double Y) Project(double x, double y)
    {
        var (angle, radius) = Transposed ? (y, x) : (x, y);
        return PolarGeometry.Point(CenterX, CenterY, radius, angle);
    }

    public override SceneShape Rectangle(double x1, double x2, double y1, double y2)
    {
        var (a1, a2, r1, r2) = Transposed ? (y1, y2, x1, x2) : (x1, x2, y1, y2);
        return new ScenePath(PolarGeometry.Sector(CenterX, CenterY, Math.Min(r1, r2), Math.Max(r1, r2), Math.Min(a1, a2), Math.Max(a1, a2)));
    }
}
