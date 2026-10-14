using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Adds the markers of a pen to the ends of the open contours of a flattened path, and shortens those contours by the insets of the markers.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> A contour is kept until it ends. A closed contour, and a contour without length, goes on as it is.
/// An open contour of length <c>L</c> has, with <c>w</c> the width of the pen, the marker of its start at its first
/// point, turned to the opposite of the direction of its first segment, and the marker of its end at its last point,
/// turned to the direction of its last segment (the segments without length have no direction and are left out). A
/// point <c>(x, y)</c> of the shape of a marker is at <c>P + w (x d + y n)</c>, where <c>P</c> is the end of the
/// contour, <c>d</c> the unit vector of the direction and <c>n = (-d.y, d.x)</c>. The contour then goes on from the
/// distance <c>w i0</c> to the distance <c>L - w i1</c> along itself, <c>i0</c> and <c>i1</c> being the insets of the
/// markers; nothing of it goes on when that is not a positive length.
/// </para>
/// <para>
/// Each contour of the shape of a marker is flattened at the size it has on the canvas, turned the way the pieces of
/// a stroke turn, and added to the stroke as a piece: the stroke and its markers are filled together.
/// </para>
/// </remarks>
internal sealed class MarkerSink : PathSink, IDisposable
{
    private const int ValuesPerVertex = 3;

    private readonly GrowableBuffer _vertices;
    private readonly ShapeSink _shape;
    private PathSink _target = null!;
    private Stroker _stroker = null!;
    private LineMarker? _start;
    private LineMarker? _end;
    private double _width;
    private bool _hasContour;
    private bool _isImplicit;

    /// <param name="scope">The scope the storage is rented from, or <see langword="null"/> to allocate it: the markers of a path that is not drawn on an image.</param>
    public MarkerSink(AllocationScope? scope)
    {
        _vertices = new GrowableBuffer(scope);
        _shape = new ShapeSink(scope);
    }

    /// <summary>Sets the pen of the next path, the stroker its markers are added to, and the sink its contours go on to (the stroker, or what cuts them into dashes).</summary>
    public void Configure(Pen pen, Stroker stroker, PathSink target)
    {
        _target = target;
        _stroker = stroker;
        _start = pen.StartMarker;
        _end = pen.EndMarker;
        _width = pen.Width;
        _hasContour = false;
        _vertices.Count = 0;
    }

    public override void MoveTo(double x, double y)
    {
        FinishOpenContour();
        Add(x, y, smooth: false);
        _hasContour = true;
        _isImplicit = false;
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_hasContour)
        {
            MoveTo(x, y);
            return;
        }

        Add(x, y, smooth);
        _isImplicit = false;
    }

    public override void Close(bool smooth)
    {
        if (!_hasContour)
            return;

        var vertices = _vertices.Values;
        var startX = vertices[0];
        var startY = vertices[1];
        if (!_isImplicit)
        {
            // A closed contour has no end: it goes on as it is
            _target.MoveTo(startX, startY);
            for (var i = ValuesPerVertex; i < vertices.Length; i += ValuesPerVertex)
            {
                _target.LineTo(vertices[i], vertices[i + 1], vertices[i + 2] != 0);
            }

            _target.Close(smooth);
        }

        // A segment that follows starts a new contour at the first point of this one
        _vertices.Count = 0;
        Add(startX, startY, smooth: false);
        _isImplicit = true;
    }

    public override void End()
    {
        FinishOpenContour();
        _hasContour = false;
        _target.End();
    }

    public void Dispose()
    {
        _vertices.Dispose();
        _shape.Dispose();
    }

    private void Add(double x, double y, bool smooth)
    {
        var entry = _vertices.Append(ValuesPerVertex);
        entry[0] = x;
        entry[1] = y;
        entry[2] = smooth ? 1 : 0;
    }

    private void FinishOpenContour()
    {
        if (!_hasContour)
            return;

        var vertices = _vertices.Values;
        _vertices.Count = 0;
        _hasContour = false;
        if (_isImplicit)
            return;

        // The length of the contour, and the directions of its first and of its last segment
        double length = 0, firstX = 0, firstY = 0, lastX = 0, lastY = 0;
        for (var i = ValuesPerVertex; i < vertices.Length; i += ValuesPerVertex)
        {
            var dx = vertices[i] - vertices[i - ValuesPerVertex];
            var dy = vertices[i + 1] - vertices[i + 1 - ValuesPerVertex];
            var segment = Math.Sqrt((dx * dx) + (dy * dy));
            if (!(segment > 0))
                continue;

            if (length == 0)
            {
                firstX = dx / segment;
                firstY = dy / segment;
            }

            lastX = dx / segment;
            lastY = dy / segment;
            length += segment;
        }

        if (!(length > 0))
        {
            // A contour without length has no direction: it goes on as it is, and is drawn by the caps of the pen
            _target.MoveTo(vertices[0], vertices[1]);
            return;
        }

        double from = 0;
        var to = length;
        if (_start is { } start)
        {
            AddMarker(start, vertices[0], vertices[1], -firstX, -firstY);
            from = start.Inset * _width;
        }

        if (_end is { } end)
        {
            AddMarker(end, vertices[^ValuesPerVertex], vertices[^(ValuesPerVertex - 1)], lastX, lastY);
            to = length - (end.Inset * _width);
        }

        // Nothing is left of the contour: it is drawn by its markers alone. Written so that distances that are not
        // numbers leave the contour out.
        if (!(from < to))
            return;

        var isStarted = from <= 0;
        if (isStarted)
        {
            _target.MoveTo(vertices[0], vertices[1]);
        }
        double position = 0;
        for (var i = ValuesPerVertex; i < vertices.Length; i += ValuesPerVertex)
        {
            var ax = vertices[i - ValuesPerVertex];
            var ay = vertices[i + 1 - ValuesPerVertex];
            var bx = vertices[i];
            var by = vertices[i + 1];
            var dx = bx - ax;
            var dy = by - ay;
            var segment = Math.Sqrt((dx * dx) + (dy * dy));
            if (!(segment > 0))
                continue;

            var segmentStart = position;
            position += segment;
            if (!isStarted)
            {
                if (position <= from)
                    continue;

                var t = (from - segmentStart) / segment;
                _target.MoveTo(ax + (t * dx), ay + (t * dy));
                isStarted = true;
            }

            if (position > to)
            {
                var t = (to - segmentStart) / segment;
                _target.LineTo(ax + (t * dx), ay + (t * dy), vertices[i + 2] != 0);
                break;
            }

            _target.LineTo(bx, by, vertices[i + 2] != 0);
        }
    }

    /// <summary>Adds a marker at a point, turned to a direction.</summary>
    private void AddMarker(LineMarker marker, double x, double y, double directionX, double directionY)
    {
        if (marker.Shape.IsEmpty)
            return;

        var scale = _stroker.Scale;
        if (_stroker.Projection is not null)
        {
            // Under a projective transformation, the factor around the marker: it is within that distance of its point
            var bounds = marker.Shape.Bounds;
            var reach = _width * (Math.Max(Math.Abs(bounds.Left), Math.Abs(bounds.Right)) + Math.Max(Math.Abs(bounds.Top), Math.Abs(bounds.Bottom)));
            scale = _stroker.GetScale(x - reach, y - reach, x + reach, y + reach);
        }

        _shape.Begin(_stroker, new Affine2D(_width * directionX, _width * directionY, -_width * directionY, _width * directionX, x, y), _width * scale);
        PathFlattener.Flatten(marker.Shape, _shape);
    }

    /// <summary>Places the contours of the shape of a marker, and adds each of them to a stroke as a piece.</summary>
    private sealed class ShapeSink : PathSink, IDisposable
    {
        private readonly GrowableBuffer _points;
        private Stroker _stroker = null!;
        private Affine2D _placement;

        public ShapeSink(AllocationScope? scope)
        {
            _points = new GrowableBuffer(scope);
        }

        public void Begin(Stroker stroker, in Affine2D placement, double scale)
        {
            _stroker = stroker;
            _placement = placement;
            Scale = scale;
            _points.Count = 0;
        }

        public override void MoveTo(double x, double y)
        {
            Flush();
            Add(x, y);
        }

        public override void LineTo(double x, double y, bool smooth) => Add(x, y);

        public override void Close(bool smooth)
        {
            if (_points.Count == 0)
                return;

            // A segment that follows starts a new contour at the first point of this one
            var points = _points.Values;
            var startX = points[0];
            var startY = points[1];
            Flush();
            var entry = _points.Append(2);
            entry[0] = startX;
            entry[1] = startY;
        }

        public override void End() => Flush();

        public void Dispose() => _points.Dispose();

        private void Add(double x, double y)
        {
            var entry = _points.Append(2);
            entry[0] = _placement.TransformX(x, y);
            entry[1] = _placement.TransformY(x, y);
        }

        private void Flush()
        {
            var points = _points.Values;
            _points.Count = 0;
            if (points.Length < 6)
                return;

            // Twice the area of the contour, positive when it turns the way the pieces of a stroke do
            double area = 0;
            var previousX = points[^2];
            var previousY = points[^1];
            for (var i = 0; i < points.Length; i += 2)
            {
                area += (previousX * points[i + 1]) - (points[i] * previousY);
                previousX = points[i];
                previousY = points[i + 1];
            }

            if (area == 0)
                return;

            if (area < 0)
            {
                for (int i = 0, j = points.Length - 2; i < j; i += 2, j -= 2)
                {
                    (points[i], points[j]) = (points[j], points[i]);
                    (points[i + 1], points[j + 1]) = (points[j + 1], points[i + 1]);
                }
            }

            _stroker.AddPolygon(points);
        }
    }
}
