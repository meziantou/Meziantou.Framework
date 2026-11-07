namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Walks the chords of a flattened path and keeps the point of them that is the nearest to a position, with its
/// distance from the start of the path. The contours are walked as <see cref="DistanceSink"/> walks them: one after
/// the other, without the gap between them, and a closed contour includes its closing segment.
/// </summary>
/// <remarks>
/// The nearest point of a chord <c>A B</c> to <c>P</c> is <c>A + t (B - A)</c> with <c>t = (P - A).(B - A) / |B - A|^2</c>
/// kept between 0 and 1. Among the points that are as near, the first one along the path is kept.
/// </remarks>
internal sealed class ClosestPointSink : PathSink
{
    private readonly double _targetX;
    private readonly double _targetY;
    private bool _hasCurrent;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private double _length;
    private double _bestSquaredDistance = double.PositiveInfinity;

    public ClosestPointSink(double x, double y)
    {
        _targetX = x;
        _targetY = y;
    }

    /// <summary>Gets a value indicating whether the path has a chord: a point was found.</summary>
    public bool IsFound { get; private set; }

    public double PointX { get; private set; }

    public double PointY { get; private set; }

    /// <summary>Gets the distance from the start of the path to the point, along the path.</summary>
    public double Distance { get; private set; }

    public override void MoveTo(double x, double y)
    {
        _x = _startX = x;
        _y = _startY = y;
        _hasCurrent = true;
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_hasCurrent)
        {
            MoveTo(x, y);
            return;
        }

        var dx = x - _x;
        var dy = y - _y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var length = Math.Sqrt(lengthSquared);

        // A chord without length has no direction: it is left out, as it is when the path is measured
        if (length > 0)
        {
            var t = (((_targetX - _x) * dx) + ((_targetY - _y) * dy)) / lengthSquared;
            t = t > 0 ? (t < 1 ? t : 1) : 0;
            var px = _x + (t * dx);
            var py = _y + (t * dy);
            var squaredDistance = ((_targetX - px) * (_targetX - px)) + ((_targetY - py) * (_targetY - py));
            if (squaredDistance < _bestSquaredDistance)
            {
                _bestSquaredDistance = squaredDistance;
                PointX = px;
                PointY = py;
                Distance = _length + (t * length);
                IsFound = true;
            }

            _length += length;
        }

        _x = x;
        _y = y;
    }

    public override void Close(bool smooth)
    {
        if (_hasCurrent)
        {
            LineTo(_startX, _startY, smooth);
        }
    }

    public override void End()
    {
    }
}
