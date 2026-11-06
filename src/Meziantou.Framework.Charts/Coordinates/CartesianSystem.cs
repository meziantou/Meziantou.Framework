namespace Meziantou.Framework.Charts;

/// <summary>Cartesian coordinates: the scales map to pixels directly.</summary>
internal sealed class CartesianSystem(bool transposed) : CoordinateSystem(transposed)
{
    public override bool IsPolar => false;

    public override (double X, double Y) Project(double x, double y) => Transposed ? (y, x) : (x, y);

    public override SceneShape Rectangle(double x1, double x2, double y1, double y2)
    {
        var (ax, ay) = Project(x1, y1);
        var (bx, by) = Project(x2, y2);
        return new SceneRectangle(Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax), Math.Abs(by - ay));
    }
}
