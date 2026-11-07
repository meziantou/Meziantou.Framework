namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Cuts closed contours, on the canvas, by a rectangle: the range of the coordinates the rasterizer takes. Every
/// contour is a polygon, open ones being closed by a straight segment as a fill closes them.
/// </summary>
/// <remarks>
/// <para>
/// The polygons are cut one side of the rectangle after the other (Sutherland and Hodgman), as the vertices come: a
/// side keeps the vertices inside it, and replaces the part of a contour outside it by the points where the contour
/// crosses it, joined along it. The winding number of every point inside the rectangle is kept, and so is a contour
/// that stays inside it: it gets the same calls, with the same vertices, bit for bit. A point where a contour crosses
/// a side is on that side exactly, and on the segment it cuts up to the rounding of one product and one sum.
/// </para>
/// <para>
/// <see cref="Resume"/> continues a contour that was given to the target directly while its vertices were all inside:
/// the sink then cuts what follows as if it had seen the whole contour.
/// </para>
/// </remarks>
internal sealed class RangeClipSink : PathSink
{
    private readonly SideSink _left = new();
    private readonly SideSink _top = new();
    private readonly SideSink _right = new();
    private readonly SideSink _bottom = new();

    /// <summary>Sets the rectangle and the sink of the contours that are cut.</summary>
    public void Configure(PathSink target, double minX, double minY, double maxX, double maxY)
    {
        _left.Configure(_top, isY: false, isMax: false, minX);
        _top.Configure(_right, isY: true, isMax: false, minY);
        _right.Configure(_bottom, isY: false, isMax: true, maxX);
        _bottom.Configure(target, isY: true, isMax: true, maxY);
    }

    /// <summary>
    /// Continues the contour whose vertices, all inside the rectangle, went to the target directly: it started at
    /// <c>(firstX, firstY)</c> and its current point is <c>(lastX, lastY)</c>.
    /// </summary>
    public void Resume(double firstX, double firstY, double lastX, double lastY)
    {
        _left.Resume(firstX, firstY, lastX, lastY);
        _top.Resume(firstX, firstY, lastX, lastY);
        _right.Resume(firstX, firstY, lastX, lastY);
        _bottom.Resume(firstX, firstY, lastX, lastY);
    }

    public override void MoveTo(double x, double y) => _left.MoveTo(x, y);

    public override void LineTo(double x, double y, bool smooth) => _left.LineTo(x, y, smooth);

    public override void Close(bool smooth) => _left.Close(smooth);

    public override void End() => _left.End();

    /// <summary>Cuts the contours by one side of the rectangle: the half-plane where a coordinate is at least, or at most, a value.</summary>
    private sealed class SideSink : PathSink
    {
        private PathSink? _target;
        private bool _isY;
        private bool _isMax;
        private double _bound;
        private bool _hasContour;
        private bool _isStarted;
        private double _firstX;
        private double _firstY;
        private bool _isFirstInside;
        private double _x;
        private double _y;
        private bool _isInside;

        public void Configure(PathSink target, bool isY, bool isMax, double bound)
        {
            _target = target;
            _isY = isY;
            _isMax = isMax;
            _bound = bound;
            _hasContour = false;
        }

        public void Resume(double firstX, double firstY, double lastX, double lastY)
        {
            _hasContour = true;
            _isStarted = true;
            _firstX = firstX;
            _firstY = firstY;
            _isFirstInside = true;
            _x = lastX;
            _y = lastY;
            _isInside = true;
        }

        public override void MoveTo(double x, double y)
        {
            EndContour();
            _hasContour = true;
            _firstX = x;
            _firstY = y;
            _isFirstInside = IsInside(x, y);
            _x = x;
            _y = y;
            _isInside = _isFirstInside;
            _isStarted = _isFirstInside;
            if (_isFirstInside)
            {
                _target!.MoveTo(x, y);
            }
        }

        public override void LineTo(double x, double y, bool smooth)
        {
            // A segment without a contour starts one, as a fill does
            if (!_hasContour)
            {
                MoveTo(x, y);
                return;
            }

            Segment(x, y, smooth);
        }

        public override void Close(bool smooth)
        {
            if (!_hasContour)
                return;

            if (_isInside && _isFirstInside)
            {
                _target!.Close(smooth);
            }
            else
            {
                Segment(_firstX, _firstY, smooth: false);
                if (_isStarted)
                {
                    _target!.Close(smooth: false);
                }
            }

            // A segment that follows starts a new contour at the first point of this one, where the target is too
            // when that point is inside
            _x = _firstX;
            _y = _firstY;
            _isInside = _isFirstInside;
            _isStarted = _isFirstInside;
        }

        public override void End()
        {
            EndContour();
            _target!.End();
        }

        /// <summary>Ends an open contour: the segment that closes it is cut too, and the target closes what it receives.</summary>
        private void EndContour()
        {
            if (_hasContour && !(_isInside && _isFirstInside))
            {
                Segment(_firstX, _firstY, smooth: false);
            }

            _hasContour = false;
        }

        /// <summary>Cuts the segment from the current point to a point.</summary>
        private void Segment(double x, double y, bool smooth)
        {
            var isInside = IsInside(x, y);
            if (_isInside && isInside)
            {
                _target!.LineTo(x, y, smooth);
            }
            else if (_isInside || isInside)
            {
                // The point where the segment crosses the side: on it exactly
                var (crossX, crossY) = Cross(_x, _y, x, y);
                Emit(crossX, crossY);
                if (isInside)
                {
                    _target!.LineTo(x, y, smooth);
                }
            }

            _x = x;
            _y = y;
            _isInside = isInside;
        }

        private void Emit(double x, double y)
        {
            if (_isStarted)
            {
                _target!.LineTo(x, y, smooth: false);
                return;
            }

            _target!.MoveTo(x, y);
            _isStarted = true;
        }

        private bool IsInside(double x, double y)
        {
            var value = _isY ? y : x;
            return _isMax ? value <= _bound : value >= _bound;
        }

        /// <summary>Gets the point of the side on the segment between two points on both sides of it, from the point inside.</summary>
        private (double X, double Y) Cross(double x0, double y0, double x1, double y1)
        {
            // From the point inside, so that a segment gets the same point in both directions
            if (!IsInside(x0, y0))
            {
                (x0, y0, x1, y1) = (x1, y1, x0, y0);
            }

            if (_isY)
            {
                var t = (_bound - y0) / (y1 - y0);
                return (x0 + (t * (x1 - x0)), _bound);
            }

            var s = (_bound - x0) / (x1 - x0);
            return (_bound, y0 + (s * (y1 - y0)));
        }
    }
}
