namespace Meziantou.Framework.Charts;

/// <summary>A circle the packing algorithms move.</summary>
internal sealed class PackCircle(double x, double y, double radius)
{
    public double X { get; set; } = x;

    public double Y { get; set; } = y;

    public double Radius { get; set; } = radius;
}
