namespace Meziantou.Framework.Charts;

/// <summary>Converts the positions of the x and y scales to points of the chart, once the layout is known.</summary>
internal abstract class CoordinateSystem
{
    protected CoordinateSystem(bool transposed)
    {
        Transposed = transposed;
    }

    public bool Transposed { get; }

    public abstract bool IsPolar { get; }

    /// <summary>Converts a position on the x scale and a position on the y scale to a point.</summary>
    public abstract (double X, double Y) Project(double x, double y);

    /// <summary>The shape between two positions on each scale: a rectangle, or an annular sector in polar coordinates.</summary>
    public abstract SceneShape Rectangle(double x1, double x2, double y1, double y2);
}
