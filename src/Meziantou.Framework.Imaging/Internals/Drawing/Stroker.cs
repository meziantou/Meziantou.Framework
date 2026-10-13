using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Turns the contours of a path into the edges of its stroke.</summary>
/// <remarks>
/// <para>
/// The stroke is emitted as a union of convex pieces, to be filled with <see cref="FillRule.NonZero"/>: one rectangle per
/// segment (the points within half the width of it, between its ends), one piece per vertex on the outer side of the
/// turn (triangle for a bevel, kite for a miter, circular sector for a round join), and the caps of open contours. The
/// pieces overlap freely: the rasterizer measures the area of their union, so an overlap never shows.
/// </para>
/// <para>
/// Every piece is emitted with the same orientation (positive shoelace sum), so that the winding number of a point
/// covered by several pieces never cancels out.
/// </para>
/// <para>
/// When a single piece contains the whole frame, so does the stroke: <see cref="CoversFrame"/> is set and nothing more is
/// emitted. A stroke much wider than the frame is made of such pieces, all crossing each other; measuring their union
/// would cost a lot to find the whole frame. Only pens at least as wide as the smallest side of the frame can have such
/// a piece, so the others are not tested.
/// </para>
/// <para>
/// A vertex flagged as smooth was created by flattening a curve: it is joined with a round join whatever the pen says,
/// because the stroke of a smooth curve has no corner there.
/// </para>
/// <para>
/// The pieces go to the edges of a draw call, or to a path as closed contours (<see cref="VectorPath.Widen"/>): the same
/// points in the same order, so that filling that path is stroking.
/// </para>
/// <para>
/// Under a transformation of the canvas, the path is stroked as it is given and the vertices of the pieces are
/// transformed when they are emitted: the pen is transformed with the path, whatever the transformation. The round
/// joins and caps are cut finer by the factor of the transformation (<see cref="PathSink.Scale"/>), and no piece is
/// tested against the frame.
/// </para>
/// </remarks>
internal sealed class Stroker : PathSink
{
    private readonly EdgeBuilder? _edges;
    private readonly VectorPathBuilder? _outline;
    private double _halfWidth;
    private LineJoin _join;
    private LineCap _cap;
    private double _miterLimitSquared;
    private bool _hasOutputTransform;
    private Affine2D _outputTransform;

    // The contour being stroked
    private bool _hasStart;
    private bool _hasSegment;
    private bool _implicitStart;
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;
    private double _firstDirectionX;
    private double _firstDirectionY;
    private double _lastDirectionX;
    private double _lastDirectionY;

    // The piece being emitted
    private double _pieceStartX;
    private double _pieceStartY;
    private double _pieceX;
    private double _pieceY;
    private double _pieceMaxX;
    private int _pieceFirstEdge;

    // Whether the corners of the frame are strictly inside the piece being emitted (top-left, top-right, bottom-left,
    // bottom-right)
    private bool _testFrame;
    private bool _topLeftInside;
    private bool _topRightInside;
    private bool _bottomLeftInside;
    private bool _bottomRightInside;

    public Stroker(EdgeBuilder edges)
    {
        _edges = edges;
    }

    /// <summary>Creates a stroker that adds the pieces of the stroke to a path, each one as a closed contour.</summary>
    public Stroker(VectorPathBuilder outline)
    {
        _outline = outline;
    }

    /// <summary>Gets a value indicating whether the stroke covers the whole frame; the edges are then meaningless.</summary>
    public bool CoversFrame { get; private set; }

    /// <summary>
    /// Sets the transformation the pieces of the next strokes go through before they become edges, or none: the stroke
    /// is then the one of the path as it is given, pen included, placed on the canvas afterward. Set before the pen.
    /// </summary>
    /// <param name="hasTransform">Whether there is a transformation.</param>
    /// <param name="transform">The transformation.</param>
    /// <param name="scale">The largest factor by which the transformation enlarges a length.</param>
    public void SetOutput(bool hasTransform, in Affine2D transform, double scale)
    {
        _hasOutputTransform = hasTransform;
        _outputTransform = transform;
        Scale = hasTransform ? scale : 1;
    }

    /// <summary>Sets the pen of the next path.</summary>
    /// <param name="pen">The pen.</param>
    /// <param name="widthFactor">What the width of the pen is multiplied by: 2 for a stroke of which a clip keeps one side.</param>
    public void Configure(Pen pen, double widthFactor = 1)
    {
        var width = pen.Width * widthFactor;
        CoversFrame = false;
        _testFrame = _edges is not null && !_hasOutputTransform && width >= Math.Min(_edges.Width, _edges.Height);
        _halfWidth = width / 2;
        _join = pen.LineJoin;
        _cap = pen.LineCap;
        _miterLimitSquared = pen.MiterLimit * pen.MiterLimit;
        _hasStart = false;
    }

    /// <summary>Sets the width and the joins of the stroke of the next closed contours: the stroke a glyph is thickened with.</summary>
    public void Configure(double width, LineJoin join, double miterLimit)
    {
        CoversFrame = false;
        _testFrame = _edges is not null && !_hasOutputTransform && width >= Math.Min(_edges.Width, _edges.Height);
        _halfWidth = width / 2;
        _join = join;
        _cap = LineCap.Butt;
        _miterLimitSquared = miterLimit * miterLimit;
        _hasStart = false;
    }

    public override void MoveTo(double x, double y)
    {
        FinishOpenContour();
        _startX = x;
        _startY = y;
        _x = x;
        _y = y;
        _hasStart = true;
        _hasSegment = false;
        _implicitStart = false;
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_hasStart)
        {
            MoveTo(x, y);
            return;
        }

        var directionX = x - _x;
        var directionY = y - _y;
        var lengthSquared = (directionX * directionX) + (directionY * directionY);

        // A segment without length has no direction: it is skipped, and its ends are one vertex
        if (!(lengthSquared > 0))
            return;

        var length = Math.Sqrt(lengthSquared);
        directionX /= length;
        directionY /= length;
        AddBody(_x, _y, x, y, directionX, directionY);
        if (_hasSegment)
        {
            AddJoin(_x, _y, _lastDirectionX, _lastDirectionY, directionX, directionY, smooth);
        }
        else
        {
            _firstDirectionX = directionX;
            _firstDirectionY = directionY;
        }

        _lastDirectionX = directionX;
        _lastDirectionY = directionY;
        _x = x;
        _y = y;
        _hasSegment = true;
        _implicitStart = false;
    }

    public override void Close(bool smooth)
    {
        if (!_hasStart)
            return;

        if (_x != _startX || _y != _startY)
        {
            LineTo(_startX, _startY, smooth);
        }

        if (_hasSegment)
        {
            AddJoin(_startX, _startY, _lastDirectionX, _lastDirectionY, _firstDirectionX, _firstDirectionY, smooth);
        }
        else if (!_implicitStart)
        {
            AddDot(_startX, _startY);
        }

        // A segment that follows starts a new contour at the first point of this one
        _x = _startX;
        _y = _startY;
        _hasSegment = false;
        _implicitStart = true;
    }

    public override void End()
    {
        FinishOpenContour();
        _hasStart = false;
    }

    private void FinishOpenContour()
    {
        if (!_hasStart)
            return;

        if (_hasSegment)
        {
            AddCap(_startX, _startY, -_firstDirectionX, -_firstDirectionY);
            AddCap(_x, _y, _lastDirectionX, _lastDirectionY);
        }
        else if (!_implicitStart)
        {
            AddDot(_startX, _startY);
        }
    }

    /// <summary>
    /// Adds a closed polygon to the stroke, as a piece: the shape of a marker. Its points are given as the points of a
    /// path are, with the orientation of the pieces (a positive shoelace sum); it does not have to be convex.
    /// </summary>
    /// <param name="points">The coordinates of the vertices: x then y for each one, three vertices at least.</param>
    public void AddPolygon(ReadOnlySpan<double> points)
    {
        // The test of the frame is the one of a convex piece
        var testFrame = _testFrame;
        _testFrame = false;
        BeginPiece(points[0], points[1]);
        for (var i = 2; i < points.Length; i += 2)
        {
            PieceLineTo(points[i], points[i + 1]);
        }

        ClosePiece();
        _testFrame = testFrame;
    }

    /// <summary>Adds the rectangle of a segment: with the normal <c>n</c> of length half the width, <c>A + n, A - n, B - n, B + n</c>.</summary>
    private void AddBody(double ax, double ay, double bx, double by, double directionX, double directionY)
    {
        var nx = -directionY * _halfWidth;
        var ny = directionX * _halfWidth;
        BeginPiece(ax + nx, ay + ny);
        PieceLineTo(ax - nx, ay - ny);
        PieceLineTo(bx - nx, by - ny);
        PieceLineTo(bx + nx, by + ny);
        ClosePiece();
    }

    /// <summary>Adds the piece that fills the outer side of the turn from direction <c>d0</c> to direction <c>d1</c> at a vertex.</summary>
    private void AddJoin(double x, double y, double d0x, double d0y, double d1x, double d1y, bool smooth)
    {
        var cross = (d0x * d1y) - (d0y * d1x);
        var dot = (d0x * d1x) + (d0y * d1y);
        var join = smooth ? LineJoin.Round : _join;
        if (cross == 0)
        {
            // Same direction: the rectangles already meet. Opposite directions: the path turns back, and only a round
            // join adds something (the half disk beyond the vertex)
            if (dot < 0 && join == LineJoin.Round)
            {
                AddHalfDisk(x, y, d0x, d0y);
            }

            return;
        }

        // The unit normals of both segments on the outer side of the turn, ordered so that the turn from a to b is
        // positive (cross(a, b) > 0). With n = (-dy, dx), cross(d0, d1) > 0 means the path turns toward +n: the outer
        // side is -n.
        double ax, ay, bx, by;
        if (cross > 0)
        {
            (ax, ay, bx, by) = (d0y, -d0x, d1y, -d1x);
        }
        else
        {
            (ax, ay, bx, by) = (-d1y, d1x, -d0y, d0x);
        }

        var h = _halfWidth;
        BeginPiece(x, y);
        PieceLineTo(x + (h * ax), y + (h * ay));
        switch (join)
        {
            case LineJoin.Round:
                if (dot >= 0)
                {
                    AddArc(x, y, ax, ay, bx, by, depth: 0);
                }
                else
                {
                    // Beyond a quarter turn, the sum of the normals gets too short to give the middle of the arc; the
                    // difference of the directions points to that middle and is at least sqrt(2) long
                    var mx = d0x - d1x;
                    var my = d0y - d1y;
                    var length = Math.Sqrt((mx * mx) + (my * my));
                    mx /= length;
                    my /= length;
                    AddArc(x, y, ax, ay, mx, my, depth: 0);
                    AddArc(x, y, mx, my, bx, by, depth: 0);
                }

                break;

            case LineJoin.Miter when 2 <= _miterLimitSquared * (1 + dot):
                // The outer edges meet at V + h (a + b) / (1 + a.b), at h / cos(turn / 2) from the vertex; the ratio of
                // the miter length to the width is 1 / cos(turn / 2), and cos^2(turn / 2) = (1 + d0.d1) / 2
                var scale = h / (1 + dot);
                PieceLineTo(x + (scale * (ax + bx)), y + (scale * (ay + by)));
                PieceLineTo(x + (h * bx), y + (h * by));
                break;

            default:
                PieceLineTo(x + (h * bx), y + (h * by));
                break;
        }

        ClosePiece();
    }

    private void AddCap(double x, double y, double directionX, double directionY)
    {
        switch (_cap)
        {
            case LineCap.Round:
                AddHalfDisk(x, y, directionX, directionY);
                break;

            case LineCap.Square:
                AddBody(x, y, x + (_halfWidth * directionX), y + (_halfWidth * directionY), directionX, directionY);
                break;
        }
    }

    /// <summary>
    /// Adds what a dash without length draws at a point of a path: its two caps back to back. Nothing for butt caps, a
    /// disk for round caps, a square turned to the direction of the path for square caps.
    /// </summary>
    public void AddDot(double x, double y, double directionX, double directionY)
    {
        switch (_cap)
        {
            case LineCap.Round:
                AddDot(x, y);
                break;

            case LineCap.Square:
                AddBody(x - (_halfWidth * directionX), y - (_halfWidth * directionY), x + (_halfWidth * directionX), y + (_halfWidth * directionY), directionX, directionY);
                break;
        }
    }

    /// <summary>Adds what a contour reduced to a single point draws: nothing for butt caps, a disk or an axis-aligned square otherwise.</summary>
    private void AddDot(double x, double y)
    {
        var h = _halfWidth;
        switch (_cap)
        {
            case LineCap.Round:
                BeginPiece(x + h, y);
                AddArc(x, y, 1, 0, 0, 1, depth: 0);
                AddArc(x, y, 0, 1, -1, 0, depth: 0);
                AddArc(x, y, -1, 0, 0, -1, depth: 0);
                AddArc(x, y, 0, -1, 1, 0, depth: 0);
                ClosePiece();
                break;

            case LineCap.Square:
                BeginPiece(x - h, y - h);
                PieceLineTo(x + h, y - h);
                PieceLineTo(x + h, y + h);
                PieceLineTo(x - h, y + h);
                ClosePiece();
                break;
        }
    }

    /// <summary>Adds the half disk centered on a point, on the side a direction points to.</summary>
    private void AddHalfDisk(double x, double y, double directionX, double directionY)
    {
        // From -n to +n through the direction: both quarter turns are positive
        var nx = -directionY;
        var ny = directionX;
        BeginPiece(x - (_halfWidth * nx), y - (_halfWidth * ny));
        AddArc(x, y, -nx, -ny, directionX, directionY, depth: 0);
        AddArc(x, y, directionX, directionY, nx, ny, depth: 0);
        ClosePiece();
    }

    /// <summary>
    /// Adds the chords of the arc of radius half the width from the unit vector <c>u</c> (the current point of the piece)
    /// to the unit vector <c>v</c>, at most a quarter turn apart. The arc is halved until its chord is within the
    /// flattening tolerance: the chord of two unit vectors is at <c>|u + v| / 2</c> from the center.
    /// </summary>
    private void AddArc(double centerX, double centerY, double ux, double uy, double vx, double vy, int depth)
    {
        var mx = ux + vx;
        var my = uy + vy;
        var length = Math.Sqrt((mx * mx) + (my * my));
        if (depth >= PathFlattener.MaxArcDepth || !(_halfWidth * Scale * (1 - (length / 2)) > PathFlattener.Tolerance))
        {
            PieceLineTo(centerX + (_halfWidth * vx), centerY + (_halfWidth * vy));
            return;
        }

        mx /= length;
        my /= length;
        AddArc(centerX, centerY, ux, uy, mx, my, depth + 1);
        AddArc(centerX, centerY, mx, my, vx, vy, depth + 1);
    }

    private void BeginPiece(double x, double y)
    {
        if (_hasOutputTransform)
        {
            (x, y) = TransformOutput(x, y);
        }

        _pieceStartX = x;
        _pieceStartY = y;
        _pieceX = x;
        _pieceY = y;
        _pieceMaxX = x;
        if (_edges is null)
        {
            _outline!.AppendMoveTo(x, y);
            return;
        }

        _pieceFirstEdge = _edges.Count;
        if (_testFrame)
        {
            _topLeftInside = true;
            _topRightInside = true;
            _bottomLeftInside = true;
            _bottomRightInside = true;
        }
    }

    private void PieceLineTo(double x, double y)
    {
        if (_hasOutputTransform)
        {
            (x, y) = TransformOutput(x, y);
        }

        AddPieceEdge(x, y);
        _pieceX = x;
        _pieceY = y;
        _pieceMaxX = Math.Max(_pieceMaxX, x);
    }

    private void ClosePiece()
    {
        if (_edges is null)
        {
            _outline!.Close();
            return;
        }

        AddPieceEdge(_pieceStartX, _pieceStartY);
        if (_testFrame && _topLeftInside && _topRightInside && _bottomLeftInside && _bottomRightInside)
        {
            CoversFrame = true;
        }

        // Nothing of the frame is inside a piece that stays on its left: a long stroke that leaves the frame does not
        // keep an edge for each of its pieces
        if (_pieceMaxX <= 0)
        {
            _edges.Truncate(_pieceFirstEdge);
        }
    }

    /// <summary>Places a vertex of a piece on the canvas.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The vertex is beyond the largest coordinate.</exception>
    private (double X, double Y) TransformOutput(double x, double y)
    {
        var px = _outputTransform.TransformX(x, y);
        var py = _outputTransform.TransformY(x, y);

        // Written so that a value that is not a number is rejected too
        if (!(Math.Abs(px) <= DrawingGuard.MaxCoordinate && Math.Abs(py) <= DrawingGuard.MaxCoordinate))
            TransformSink.ThrowOutOfRange(Math.Abs(px) <= DrawingGuard.MaxCoordinate ? py : px);

        return (px, py);
    }

    private void AddPieceEdge(double x, double y)
    {
        if (_edges is null)
        {
            _outline!.AppendLineTo(x, y);
            return;
        }

        if (CoversFrame)
            return;

        _edges.Add(_pieceX, _pieceY, x, y);
        if (_testFrame)
        {
            // A point is strictly inside a convex piece when it is strictly on the inner side of every edge; with the
            // orientation of the pieces, that side is where the cross product is positive
            var dx = x - _pieceX;
            var dy = y - _pieceY;
            var left = -_pieceX;
            var top = -_pieceY;
            var right = _edges.Width - _pieceX;
            var bottom = _edges.Height - _pieceY;
            _topLeftInside &= (dx * top) - (dy * left) > 0;
            _topRightInside &= (dx * top) - (dy * right) > 0;
            _bottomLeftInside &= (dx * bottom) - (dy * left) > 0;
            _bottomRightInside &= (dx * bottom) - (dy * right) > 0;
        }
    }
}
