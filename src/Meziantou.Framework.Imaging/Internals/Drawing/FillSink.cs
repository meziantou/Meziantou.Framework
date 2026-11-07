namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Turns the contours of a path into the edges of its interior: an open contour is closed by a straight segment.</summary>
internal sealed class FillSink : PathSink
{
    private readonly EdgeBuilder _edges;
    private bool _open;
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;
    private double _maxX;
    private int _firstEdge;

    public FillSink(EdgeBuilder edges)
    {
        _edges = edges;
    }

    public override void MoveTo(double x, double y)
    {
        End();
        _startX = x;
        _startY = y;
        _x = x;
        _y = y;
        _maxX = x;
        _firstEdge = _edges.Count;
        _open = true;
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_open)
        {
            MoveTo(x, y);
            return;
        }

        _edges.Add(_x, _y, x, y);
        _x = x;
        _y = y;
        _maxX = Math.Max(_maxX, x);
    }

    public override void Close(bool smooth)
    {
        if (!_open)
            return;

        EndContour();

        // A segment that follows starts a new contour at the first point of this one
        _x = _startX;
        _y = _startY;
        _maxX = _startX;
        _firstEdge = _edges.Count;
    }

    public override void End()
    {
        if (!_open)
            return;

        EndContour();
        _open = false;
    }

    private void EndContour()
    {
        _edges.Add(_x, _y, _startX, _startY);

        // Nothing of the frame is inside a contour that stays on its left
        if (_maxX <= 0)
        {
            _edges.Truncate(_firstEdge);
        }
    }
}
