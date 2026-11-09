using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Accumulates the rectangle that covers points and rectangles.</summary>
internal sealed class BoundsBuilder
{
    private double _minX = double.PositiveInfinity;
    private double _minY = double.PositiveInfinity;
    private double _maxX = double.NegativeInfinity;
    private double _maxY = double.NegativeInfinity;

    public void Add(LayoutPoint point)
    {
        if (!Geometry.IsFinite(point))
            return;

        _minX = Math.Min(_minX, point.X);
        _maxX = Math.Max(_maxX, point.X);
        _minY = Math.Min(_minY, point.Y);
        _maxY = Math.Max(_maxY, point.Y);
    }

    public void Add(SceneBounds bounds)
    {
        Add(new LayoutPoint(bounds.X, bounds.Y));
        Add(new LayoutPoint(bounds.Right, bounds.Bottom));
    }

    public SceneBounds ToBounds() => double.IsInfinity(_minX) ? default : new SceneBounds(_minX, _minY, _maxX - _minX, _maxY - _minY);
}
