using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Keeps the part of a flattened path between two distances from its start, as straight segments. The path is
/// measured as <see cref="DistanceSink"/> measures it: its contours follow each other, without the gap between them,
/// and a closed contour includes its closing segment.
/// </summary>
/// <remarks>
/// A chord that is kept entirely keeps its two ends, exactly; a chord that is cut gets a point on it. A closed contour
/// that is kept entirely stays closed.
/// </remarks>
internal sealed class TrimSink : PathSink
{
    private readonly VectorPathBuilder _builder;
    private readonly double _from;
    private readonly double _to;
    private bool _hasCurrent;
    private bool _isDrawing;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private double _contourStart;
    private double _length;

    public TrimSink(VectorPathBuilder builder, double from, double to)
    {
        _builder = builder;
        _from = from;
        _to = to;
    }

    public override void MoveTo(double x, double y)
    {
        _x = _startX = x;
        _y = _startY = y;
        _contourStart = _length;
        _hasCurrent = true;
        _isDrawing = false;
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_hasCurrent)
        {
            MoveTo(x, y);
            return;
        }

        AddChord(x, y, closes: false);
    }

    public override void Close(bool smooth)
    {
        if (!_hasCurrent)
            return;

        AddChord(_startX, _startY, closes: true);

        // A segment that follows starts a new contour at the first point of this one
        _contourStart = _length;
        _isDrawing = false;
    }

    public override void End()
    {
    }

    private void AddChord(double x, double y, bool closes)
    {
        var dx = x - _x;
        var dy = y - _y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var start = _length;
        var end = start + length;

        // A contour that is kept from its first point to its last one is closed as it was
        var closesContour = closes && _isDrawing && _contourStart >= _from && end <= _to;
        if (length > 0)
        {
            var from = Math.Max(_from, start);
            var to = Math.Min(_to, end);
            if (from < to)
            {
                if (!_isDrawing)
                {
                    if (from > start)
                    {
                        var along = (from - start) / length;
                        _builder.AppendMoveTo(_x + (along * dx), _y + (along * dy));
                    }
                    else
                    {
                        _builder.AppendMoveTo(_x, _y);
                    }

                    _isDrawing = true;
                }

                if (to < end)
                {
                    var along = (to - start) / length;
                    _builder.AppendLineTo(_x + (along * dx), _y + (along * dy));
                    _isDrawing = false;
                }
                else if (!closesContour)
                {
                    _builder.AppendLineTo(x, y);
                }
            }
            else
            {
                _isDrawing = false;
            }

            _length = end;
        }

        if (closesContour)
        {
            _builder.Close();
        }

        _x = x;
        _y = y;
    }
}
