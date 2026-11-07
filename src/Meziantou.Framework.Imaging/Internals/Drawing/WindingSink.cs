namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Counts how many times the contours of a flattened path wind around a point. An open contour is closed by a straight
/// segment, as it is when it is filled.
/// </summary>
internal sealed class WindingSink : PathSink
{
    private readonly double _pointX;
    private readonly double _pointY;
    private bool _open;
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;

    public WindingSink(double x, double y)
    {
        _pointX = x;
        _pointY = y;
    }

    /// <summary>Gets the winding number of the point.</summary>
    public int Winding { get; private set; }

    public override void MoveTo(double x, double y)
    {
        End();
        _startX = x;
        _startY = y;
        _x = x;
        _y = y;
        _open = true;
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_open)
        {
            MoveTo(x, y);
            return;
        }

        Add(_x, _y, x, y);
        _x = x;
        _y = y;
    }

    public override void Close(bool smooth)
    {
        if (!_open)
            return;

        // A segment that follows starts a new contour at the first point of this one
        Add(_x, _y, _startX, _startY);
        _x = _startX;
        _y = _startY;
    }

    public override void End()
    {
        if (!_open)
            return;

        Add(_x, _y, _startX, _startY);
        _open = false;
    }

    /// <summary>Counts a segment when the horizontal line through the point crosses it on the right of the point.</summary>
    private void Add(double x0, double y0, double x1, double y1)
    {
        // A segment counts for the heights from its upper end (included) to its lower end (excluded): a vertex the line
        // goes through is counted once
        var side = ((x1 - x0) * (_pointY - y0)) - ((_pointX - x0) * (y1 - y0));
        if (y0 <= _pointY)
        {
            if (y1 > _pointY && side > 0)
            {
                Winding++;
            }
        }
        else if (y1 <= _pointY && side < 0)
        {
            Winding--;
        }
    }
}
