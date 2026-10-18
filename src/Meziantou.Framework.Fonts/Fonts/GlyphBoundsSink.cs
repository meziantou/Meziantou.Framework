namespace Meziantou.Framework.Fonts;

/// <summary>Measures an outline: the smallest rectangle that contains its points and the control points of its curves.</summary>
/// <remarks>
/// A curve stays inside the polygon of its control points, so the rectangle contains the outline. It is the exact
/// bounding box of an outline that has a point at each of its extrema, which is how fonts are drawn.
/// </remarks>
internal sealed class GlyphBoundsSink : OutlineSink
{
    public double MinX { get; private set; }

    public double MinY { get; private set; }

    public double MaxX { get; private set; }

    public double MaxY { get; private set; }

    /// <summary>Gets a value indicating whether no point was received since <see cref="Reset"/>.</summary>
    public bool IsEmpty { get; private set; } = true;

    public void Reset() => IsEmpty = true;

    public override void MoveTo(double x, double y) => Include(x, y);

    public override void LineTo(double x, double y) => Include(x, y);

    public override void QuadraticTo(double controlX, double controlY, double x, double y)
    {
        Include(controlX, controlY);
        Include(x, y);
    }

    public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        Include(control1X, control1Y);
        Include(control2X, control2Y);
        Include(x, y);
    }

    public override void Close()
    {
    }

    private void Include(double x, double y)
    {
        if (IsEmpty)
        {
            MinX = MaxX = x;
            MinY = MaxY = y;
            IsEmpty = false;
            return;
        }

        MinX = Math.Min(MinX, x);
        MinY = Math.Min(MinY, y);
        MaxX = Math.Max(MaxX, x);
        MaxY = Math.Max(MaxY, y);
    }
}
