namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Walks the chords of a flattened path: adds up their lengths, and finds the point at a distance from the start. The
/// contours are walked one after the other, without the gap between them, and a closed contour includes its closing
/// segment, as in <see cref="PathMeasure"/>.
/// </summary>
internal sealed class DistanceSink : PathSink
{
    private readonly double _target;
    private bool _hasCurrent;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private double _lastX;
    private double _lastY;

    /// <param name="target">The distance of the point to find; a distance that is not a number finds nothing.</param>
    public DistanceSink(double target)
    {
        _target = target;
    }

    /// <summary>Gets the length walked so far.</summary>
    public double Length { get; private set; }

    /// <summary>Gets a value indicating whether the path has a chord.</summary>
    public bool HasChord { get; private set; }

    /// <summary>Gets a value indicating whether the distance was reached.</summary>
    public bool IsFound { get; private set; }

    public double PointX { get; private set; }

    public double PointY { get; private set; }

    /// <summary>Gets the horizontal component of the unit vector of the chord the point is on.</summary>
    public double DirectionX { get; private set; }

    public double DirectionY { get; private set; }

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
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        // A chord without length has no direction: it is left out
        if (length > 0)
        {
            if (!IsFound)
            {
                // The chord that contains a distance is the last one that starts at or before it. Its end is the sum
                // the length of the path will be: the distance that is the length of the path is beyond every chord,
                // and gives the last point itself.
                var along = _target - Length;
                DirectionX = dx / length;
                DirectionY = dy / length;
                if (_target < Length + length)
                {
                    along = along > 0 ? along : 0;
                    PointX = _x + (along * DirectionX);
                    PointY = _y + (along * DirectionY);
                    IsFound = true;
                }
            }

            _lastX = x;
            _lastY = y;
            HasChord = true;
            Length += length;
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
        // Beyond the end, the point is the end of the last chord
        if (HasChord && !IsFound && _target >= Length)
        {
            PointX = _lastX;
            PointY = _lastY;
            IsFound = true;
        }
    }
}
