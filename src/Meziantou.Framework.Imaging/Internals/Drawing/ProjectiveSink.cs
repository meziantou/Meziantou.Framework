namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Places the contours of a flattened path on the canvas through a projective transformation: what has no image, or an
/// image beyond the largest coordinate, is cut off before the vertices are divided by <c>W</c>.
/// </summary>
/// <remarks>
/// <para>
/// A projective transformation keeps straight lines straight, so a chord of the path is the chord of its image, and the
/// part of the plane whose image is inside the square of the largest coordinate, <c>|X| &lt;= K W</c> and
/// <c>|Y| &lt;= K W</c> (which implies <c>W &gt;= 0</c>), is the intersection of four half-planes of the drawing. Each
/// contour is cut by them in the coordinates of the drawing, where they are straight lines, then its vertices are
/// transformed: a shape that crosses the horizon of the plane is drawn up to it, and what is on the other side, which
/// would be behind the viewer, is not drawn.
/// </para>
/// <para>
/// A contour is cut as a polygon (the shape of a fill, or a piece of a stroke: what is cut off is replaced by the
/// border of the square, far from the canvas) or as a line (the path a pen strokes on the canvas: what is cut off is
/// left out, and the contour is cut into the parts that remain).
/// </para>
/// <para>
/// A transformation that flattens the plane up to the rounding of its coefficients (<see cref="ProjectiveTransform.IsFlat"/>)
/// is a plane seen from its edge: nothing is emitted. Its images would all be on one line, where the rounding would
/// leave edges that cross each other everywhere.
/// </para>
/// </remarks>
internal sealed class ProjectiveSink : PathSink
{
    private const int PlaneCount = 4;
    private const double Limit = DrawingGuard.MaxCoordinate;

    private readonly double[] _firstX = new double[PlaneCount];
    private readonly double[] _firstY = new double[PlaneCount];
    private readonly double[] _previousX = new double[PlaneCount];
    private readonly double[] _previousY = new double[PlaneCount];
    private readonly bool[] _hasFirst = new bool[PlaneCount];
    private PathSink? _target;
    private ProjectiveTransform _transform;
    private bool _isPolyline;
    private bool _isFlat;

    // The contour being received
    private bool _hasContour;
    private bool _isClosedStart;
    private double _startX;
    private double _startY;
    private bool _hasEmitted;

    // A contour cut as a line: its last point, and whether the part of it that is emitted goes on
    private double _x;
    private double _y;
    private bool _isRunOpen;
    private bool _isWhole;

    /// <summary>Sets the sink the transformed vertices go to, and the transformation.</summary>
    /// <param name="target">The sink, in the pixels of the canvas.</param>
    /// <param name="view">The transformation, and the part of the canvas that is seen.</param>
    /// <param name="isPolyline"><see langword="true"/> to cut the contours as lines, <see langword="false"/> to cut them as polygons.</param>
    public void Configure(PathSink target, in ProjectiveView view, bool isPolyline)
    {
        _target = target;
        _transform = view.Transform;
        _isPolyline = isPolyline;
        _isFlat = view.IsFlat;
        Projection = view;
        Scale = 1;
        _hasContour = false;
        _isClosedStart = false;
    }

    public override void MoveTo(double x, double y)
    {
        // A plane seen from its edge shows nothing
        if (_isFlat)
            return;

        FinishContour(close: false);
        BeginContour(x, y);
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (_isFlat)
            return;

        if (!_hasContour)
        {
            // A segment that follows a closed contour starts at its first point
            BeginContour(_isClosedStart ? _startX : x, _isClosedStart ? _startY : y);
            if (!_isClosedStart)
                return;
        }

        if (_isPolyline)
        {
            AddSegment(x, y, smooth);
        }
        else
        {
            Feed(0, x, y);
        }
    }

    public override void Close(bool smooth)
    {
        if (_isFlat || !_hasContour)
            return;

        var startX = _startX;
        var startY = _startY;
        if (_isPolyline)
        {
            if (_x != startX || _y != startY)
            {
                var isWhole = _isWhole;
                AddSegment(startX, startY, smooth);
                if (isWhole && _isWhole)
                {
                    // Nothing was cut off: the contour stays closed, and the pen joins its ends
                    _target!.Close(smooth);
                }
            }
            else if (_isWhole && _hasEmitted)
            {
                _target!.Close(smooth);
            }

            _hasContour = false;
        }
        else
        {
            FinishContour(close: true);
        }

        _isClosedStart = true;
        _startX = startX;
        _startY = startY;
    }

    public override void End()
    {
        FinishContour(close: false);
        _isClosedStart = false;
        _target!.End();
    }

    private void BeginContour(double x, double y)
    {
        _hasContour = true;
        _hasEmitted = false;
        _startX = x;
        _startY = y;
        if (_isPolyline)
        {
            _x = x;
            _y = y;
            _isWhole = IsInside(x, y);
            _isRunOpen = _isWhole;
            if (_isWhole)
            {
                Emit(x, y, isFirst: true, smooth: false);
            }
        }
        else
        {
            Array.Clear(_hasFirst);
            Feed(0, x, y);
        }
    }

    private void FinishContour(bool close)
    {
        if (!_hasContour)
            return;

        _hasContour = false;
        if (_isPolyline)
            return;

        ClosePlane(0);
        if (_hasEmitted && close)
        {
            _target!.Close(smooth: false);
        }
    }

    /// <summary>Gets the value of the function of a plane at a point: positive or zero on the side that is kept.</summary>
    private double Evaluate(int plane, double x, double y)
    {
        var w = Limit * _transform.TransformW(x, y);
        return plane switch
        {
            0 => w - _transform.TransformX(x, y),
            1 => w + _transform.TransformX(x, y),
            2 => w - _transform.TransformY(x, y),
            _ => w + _transform.TransformY(x, y),
        };
    }

    private bool IsInside(double x, double y)
    {
        for (var plane = 0; plane < PlaneCount; plane++)
        {
            // Written so that a value that is not a number is outside
            if (!(Evaluate(plane, x, y) >= 0))
                return false;
        }

        return true;
    }

    /// <summary>Gives a vertex of a polygon to the cut by a plane (Sutherland and Hodgman), whose output is given to the next plane.</summary>
    private void Feed(int plane, double x, double y)
    {
        if (plane == PlaneCount)
        {
            Emit(x, y, isFirst: !_hasEmitted, smooth: false);
            return;
        }

        var current = Evaluate(plane, x, y);
        if (!_hasFirst[plane])
        {
            _hasFirst[plane] = true;
            _firstX[plane] = _previousX[plane] = x;
            _firstY[plane] = _previousY[plane] = y;
            if (current >= 0)
            {
                Feed(plane + 1, x, y);
            }

            return;
        }

        var previousX = _previousX[plane];
        var previousY = _previousY[plane];
        var previous = Evaluate(plane, previousX, previousY);
        if ((previous >= 0) != (current >= 0))
        {
            var t = previous / (previous - current);
            Feed(plane + 1, previousX + (t * (x - previousX)), previousY + (t * (y - previousY)));
        }

        if (current >= 0)
        {
            Feed(plane + 1, x, y);
        }

        _previousX[plane] = x;
        _previousY[plane] = y;
    }

    /// <summary>Ends the polygon a plane cuts: its last side, back to its first vertex, then the planes after it.</summary>
    private void ClosePlane(int plane)
    {
        if (plane == PlaneCount)
            return;

        if (_hasFirst[plane])
        {
            _hasFirst[plane] = false;
            var previousX = _previousX[plane];
            var previousY = _previousY[plane];
            var firstX = _firstX[plane];
            var firstY = _firstY[plane];
            var previous = Evaluate(plane, previousX, previousY);
            var first = Evaluate(plane, firstX, firstY);
            if ((previous >= 0) != (first >= 0))
            {
                var t = previous / (previous - first);
                Feed(plane + 1, previousX + (t * (firstX - previousX)), previousY + (t * (firstY - previousY)));
            }
        }

        ClosePlane(plane + 1);
    }

    /// <summary>Adds a segment of a contour cut as a line: the part of it that is in all the half-planes, if any.</summary>
    private void AddSegment(double x, double y, bool smooth)
    {
        var ax = _x;
        var ay = _y;
        _x = x;
        _y = y;
        double start = 0;
        double end = 1;
        for (var plane = 0; plane < PlaneCount && start <= end; plane++)
        {
            var a = Evaluate(plane, ax, ay);
            var b = Evaluate(plane, x, y);

            // Written so that a value that is not a number leaves nothing
            if (!(a >= 0 || b >= 0))
            {
                start = 1;
                end = 0;
                break;
            }

            if (a < 0)
            {
                start = Math.Max(start, a / (a - b));
            }
            else if (b < 0)
            {
                end = Math.Min(end, a / (a - b));
            }
        }

        if (start > end)
        {
            _isRunOpen = false;
            _isWhole = false;
            return;
        }

        if (start > 0 || end < 1)
        {
            _isWhole = false;
        }

        if (start > 0 || !_isRunOpen)
        {
            Emit(ax + (start * (x - ax)), ay + (start * (y - ay)), isFirst: true, smooth: false);
        }

        if (end < 1)
        {
            Emit(ax + (end * (x - ax)), ay + (end * (y - ay)), isFirst: false, smooth: false);
            _isRunOpen = false;
        }
        else
        {
            Emit(x, y, isFirst: false, smooth);
            _isRunOpen = true;
        }
    }

    private void Emit(double x, double y, bool isFirst, bool smooth)
    {
        var w = _transform.TransformW(x, y);
        var px = Clamp(_transform.TransformX(x, y) / w);
        var py = Clamp(_transform.TransformY(x, y) / w);
        if (isFirst)
        {
            _target!.MoveTo(px, py);
        }
        else
        {
            _target!.LineTo(px, py, smooth);
        }

        _hasEmitted = true;

        // A vertex on a border is there up to a rounding: it is brought onto it. Written so that a value that is not a
        // number (a vertex at the point the whole line of the horizon goes to) is on the border.
        static double Clamp(double value) => value >= -Limit ? (value <= Limit ? value : Limit) : -Limit;
    }
}
