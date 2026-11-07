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
/// A part of a round join or cap whose triangle (its ends and the meeting point of their tangents) has a box outside
/// the frame is replaced by its chord, whatever its size: what is between them is not seen. A cap of a very wide pen
/// far from the frame is a few chords instead of thousands.
/// </para>
/// <para>
/// A vertex flagged as smooth was created by flattening a curve: it is joined with a round join whatever the pen says,
/// because the stroke of a smooth curve has no corner there. A chord next to such a vertex is not a rectangle: its
/// body is what the segment of the width of the pen perpendicular to the curve sweeps, from the normal at its start to
/// the one at its end (between the normals of the chords at a smooth vertex), with the half of its rectangle on the
/// outer side of the turns. A pen wider than the curve sweeps beyond its center: the two perpendiculars cross within the
/// pen, and the body is two triangles that meet there, as the canvas of HTML defines a stroke. At the end of a curve,
/// the direction of the stroke (its end, its cap, the join with what follows) is the direction of the curve as its
/// chords give it: the end chord turned away from the next one by half the angle between them, not the chord itself.
/// The body of a chord is made when the vertex at its end is known, and the first chord of a contour when the contour
/// ends, which says whether its start is a corner. The stroke that moves an outline (<see cref="PathOffsetter"/>) and
/// thickens a glyph keeps the rectangles and the directions of the chords: what it needs is what is within a distance
/// of the outline, not what a pen sweeps.
/// </para>
/// <para>
/// The pieces go to the edges of a draw call, or to a path as closed contours (<see cref="VectorPath.Widen"/>): the same
/// points in the same order, so that filling that path is stroking. They can also go to a sink as closed contours
/// (<see cref="VectorPath.StrokeContains"/>): a <see cref="WindingSink"/> counts how they wind around a point without
/// keeping them. Only the chords near that point are stroked (<see cref="NearChordFilter"/>), and an arc of a join or a
/// cap whose triangle has a box that does not hold that point is replaced by its chord, which winds around it as the arc
/// does.
/// </para>
/// <para>
/// Under a transformation of the canvas, the path is stroked as it is given and the vertices of the pieces are
/// transformed when they are emitted: the pen is transformed with the path, whatever the transformation. The round
/// joins and caps are cut finer by the factor of the transformation (<see cref="PathSink.Scale"/>). The pieces stay
/// convex once they are transformed, and are tested against the frame on the canvas when the pen is as wide there as
/// the smallest side of the frame; a transformation that mirrors reverses their orientation, which the test follows. The
/// image of a layer of a rectangle is drawn through a translation: a wide dashed pen there is as cheap as on the canvas.
/// A pen that the transformation does not apply to (<see cref="Pen.ScalesWithTransform"/>)
/// is given the path as it is on the canvas instead, by a <see cref="TransformSink"/> before it: it strokes as it
/// does without transformation. Under a projective transformation, each piece is cut where it has no image before its
/// vertices are transformed (<see cref="ProjectiveSink"/>), and the arcs of a join or a cap are cut finer by the
/// factor of the transformation on each of their parts (<see cref="ProjectiveView"/>). The pieces stay convex once they
/// are cut and transformed, so they are tested against the frame whatever the width of the pen, which is not a width
/// of the canvas.
/// </para>
/// </remarks>
internal sealed class Stroker : PathSink
{
    private readonly EdgeBuilder? _edges;
    private readonly VectorPathBuilder? _outline;
    private readonly PathSink? _pieces;

    // The point whose position the pieces are tested against, when they go to a sink, and what passes on the chords near it
    private readonly double _testX;
    private readonly double _testY;
    private readonly NearChordFilter? _nearChords;
    private double _halfWidth;
    private LineJoin _join;
    private LineCap _cap;
    private double _miterLimitSquared;

    // Whether the chords of a curve are swept by the perpendicular to the curve (the stroke of a pen), or are rectangles
    // (what is within a distance of an outline)
    private bool _sweepsCurves;
    private bool _hasOutputTransform;
    private Affine2D _outputTransform;
    private double _originX;
    private double _originY;
    private bool _isOutputChecked = true;
    private ProjectiveSink? _projectiveOutput;
    private PieceSink? _pieceSink;
    private bool _isProjectiveOutput;

    // Whether the pieces of the stroke that leave the range of the coordinates are cut by it (a path placed before it
    // is stroked that leaves it), and the piece being cut
    private bool _clipsToRange;
    private RangeClipSink? _rangeClipper;
    private bool _isPieceClipped;

    // The view of the pieces themselves: their arcs are what is drawn, nothing reaches beyond them
    private ProjectiveView _pieceView;

    // The contour being stroked
    private bool _hasStart;
    private bool _hasSegment;
    private bool _implicitStart;
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;

    // The chords whose pieces wait for the vertex at their end: the last one, and the first one of the contour (whose
    // start is only known when the contour ends: a closed curve has no corner there)
    private Chord _last;
    private Chord _first;
    private bool _isLastFirst;
    private bool _firstEndSmooth;
    private double _firstNextX;
    private double _firstNextY;

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
    private double _frameOrientation = 1;
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

    /// <summary>
    /// Creates a stroker that gives the pieces of the stroke to a sink, each one as a closed contour, to find whether
    /// they contain a point: the arcs of joins and caps are only cut finely around that point.
    /// </summary>
    /// <param name="pieces">The sink.</param>
    /// <param name="x">The horizontal coordinate of the point.</param>
    /// <param name="y">The vertical coordinate of the point.</param>
    public Stroker(PathSink pieces, double x, double y)
    {
        _pieces = pieces;
        _testX = x;
        _testY = y;
        _nearChords = new NearChordFilter(new ContourSink(this), x, y);
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
    /// <param name="originX">Where the frame of the pieces is on the canvas the largest coordinate is checked on, along x: not zero for the image of a layer of a rectangle.</param>
    /// <param name="originY">Where the frame of the pieces is on that canvas, along y.</param>
    /// <param name="isChecked"><see langword="false"/> when the pieces are those of the canvas, only moved to the image of a layer of a rectangle: the canvas does not check them.</param>
    public void SetOutput(bool hasTransform, in Affine2D transform, double scale, double originX = 0, double originY = 0, bool isChecked = true)
    {
        _originX = originX;
        _originY = originY;
        _isOutputChecked = isChecked;
        _hasOutputTransform = hasTransform;
        _outputTransform = transform;
        _isProjectiveOutput = false;
        Projection = null;
        Scale = hasTransform ? scale : 1;

        // A transformation that mirrors gives the pieces the opposite orientation: the inner side of their edges is then
        // where the cross product is negative
        _frameOrientation = hasTransform && (transform.A * transform.D) - (transform.B * transform.C) < 0 ? -1 : 1;
    }

    /// <summary>
    /// Sets the projective transformation the pieces of the next strokes go through before they become edges: each
    /// piece is cut where it has no image (<see cref="ProjectiveSink"/>), then its vertices are transformed. Set before
    /// the pen.
    /// </summary>
    public void SetOutput(in ProjectiveView view)
    {
        _hasOutputTransform = false;
        _isProjectiveOutput = true;
        Projection = view;
        _pieceView = view.WithoutReach();
        Scale = 1;
        _frameOrientation = 1;
        (_projectiveOutput ??= new ProjectiveSink()).Configure(_pieceSink ??= new PieceSink(this), view, isPolyline: false);
    }

    /// <summary>Sets the pen of the next path.</summary>
    /// <param name="pen">The pen.</param>
    /// <param name="widthFactor">What the width of the pen is multiplied by: 2 for a stroke of which a clip keeps one side.</param>
    public void Configure(Pen pen, double widthFactor = 1)
    {
        var width = pen.StrokeWidth * widthFactor;
        CoversFrame = false;
        _clipsToRange = false;
        _testFrame = CanCoverFrame(width);
        _halfWidth = width / 2;
        _join = pen.LineJoin;
        _cap = pen.LineCap;
        _miterLimitSquared = pen.MiterLimit * pen.MiterLimit;
        _sweepsCurves = true;
        _hasStart = false;

        // The pieces are within half the width of their chord, the miters (up to the limit) and the corners of the
        // square caps of its ends excepted
        _nearChords?.Configure(_halfWidth * Math.Max(_join == LineJoin.Miter ? pen.MiterLimit : 1, _cap == LineCap.Square ? Math.Sqrt(2) : 1));
    }

    /// <summary>
    /// Gets a value indicating whether a piece of a stroke of a width can contain the whole frame: it is then tested
    /// against it. A piece is at most as wide as the pen on the canvas, which the transformation enlarges by its factor at
    /// most; a piece cut and transformed by a projective transformation is always tested.
    /// </summary>
    private bool CanCoverFrame(double width) => _edges is not null && (_isProjectiveOutput || width * Scale >= Math.Min(_edges.Width, _edges.Height));

    /// <summary>Sets the width and the joins of the stroke of the next closed contours: the stroke a glyph is thickened with.</summary>
    public void Configure(double width, LineJoin join, double miterLimit)
    {
        CoversFrame = false;
        _clipsToRange = false;
        _testFrame = CanCoverFrame(width);
        _halfWidth = width / 2;
        _join = join;
        _cap = LineCap.Butt;
        _miterLimitSquared = miterLimit * miterLimit;
        _sweepsCurves = false;
        _hasStart = false;
    }

    public override void MoveTo(double x, double y)
    {
        if (_nearChords is not null)
        {
            _nearChords.MoveTo(x, y);
            return;
        }

        MoveToCore(x, y);
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (_nearChords is not null)
        {
            _nearChords.LineTo(x, y, smooth);
            return;
        }

        LineToCore(x, y, smooth);
    }

    public override void Close(bool smooth)
    {
        if (_nearChords is not null)
        {
            _nearChords.Close(smooth);
            return;
        }

        CloseCore(smooth);
    }

    public override void End()
    {
        if (_nearChords is not null)
        {
            _nearChords.End();
            return;
        }

        EndCore();
    }

    private void MoveToCore(double x, double y)
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

    private void LineToCore(double x, double y, bool smooth)
    {
        if (!_hasStart)
        {
            MoveToCore(x, y);
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
        var chord = new Chord { StartX = _x, StartY = _y, EndX = x, EndY = y, DirectionX = directionX, DirectionY = directionY };
        if (_hasSegment)
        {
            // The vertex between the last chord and this one: the pieces of the last chord can be made
            chord.StartSmooth = smooth;
            chord.PreviousX = _last.DirectionX;
            chord.PreviousY = _last.DirectionY;
            double endX, endY;
            if (_isLastFirst)
            {
                _first = _last;
                _firstEndSmooth = smooth;
                _firstNextX = directionX;
                _firstNextY = directionY;
                GetEndDirection(in _last, smooth, out endX, out endY);
            }
            else
            {
                AddChord(in _last, smooth, directionX, directionY, out _, out _, out endX, out endY);
            }

            if (smooth)
            {
                AddJoin(_x, _y, _last.DirectionX, _last.DirectionY, directionX, directionY, smooth: true);
            }
            else
            {
                // The join needs the direction the stroke starts this chord with, known at its end
                chord.HasJoin = true;
                chord.JoinX = endX;
                chord.JoinY = endY;
            }
        }

        _isLastFirst = !_hasSegment;
        _last = chord;
        _x = x;
        _y = y;
        _hasSegment = true;
        _implicitStart = false;
    }

    private void CloseCore(bool smooth)
    {
        if (!_hasStart)
            return;

        if (_x != _startX || _y != _startY)
        {
            LineToCore(_startX, _startY, smooth);
        }

        if (_hasSegment)
        {
            if (_isLastFirst)
            {
                // One chord, which ends where it starts: it cannot be, a chord without length is skipped
                AddChord(in _last, endSmooth: false, 0, 0, out _, out _, out _, out _);
            }
            else
            {
                // The last chord ends where the first one starts
                AddChord(in _last, smooth, _first.DirectionX, _first.DirectionY, out _, out _, out var endX, out var endY);
                _first.StartSmooth = smooth;
                _first.PreviousX = _last.DirectionX;
                _first.PreviousY = _last.DirectionY;
                if (smooth)
                {
                    AddJoin(_startX, _startY, _last.DirectionX, _last.DirectionY, _first.DirectionX, _first.DirectionY, smooth: true);
                }
                else
                {
                    _first.HasJoin = true;
                    _first.JoinX = endX;
                    _first.JoinY = endY;
                }

                AddChord(in _first, _firstEndSmooth, _firstNextX, _firstNextY, out _, out _, out _, out _);
            }
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

    private void EndCore()
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
            double startX, startY, endX, endY;
            if (_isLastFirst)
            {
                AddChord(in _last, endSmooth: false, 0, 0, out startX, out startY, out endX, out endY);
            }
            else
            {
                AddChord(in _last, endSmooth: false, 0, 0, out _, out _, out endX, out endY);
                AddChord(in _first, _firstEndSmooth, _firstNextX, _firstNextY, out startX, out startY, out _, out _);
            }

            AddCap(_startX, _startY, -startX, -startY);
            AddCap(_x, _y, endX, endY);
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

    /// <summary>
    /// Adds the pieces of a chord, once the vertex at its end is known: the join that waits at its start, then its
    /// body. Gives the directions the stroke has at both ends of the chord, for its joins and its caps.
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <param name="endSmooth">Whether the vertex at its end was created by flattening a curve.</param>
    /// <param name="nextX">The direction of the chord after it, when there is one.</param>
    /// <param name="nextY">The direction of the chord after it, along y.</param>
    /// <param name="startX">The direction of the stroke at the start of the chord.</param>
    /// <param name="startY">The direction of the stroke at the start of the chord, along y.</param>
    /// <param name="endX">The direction of the stroke at the end of the chord.</param>
    /// <param name="endY">The direction of the stroke at the end of the chord, along y.</param>
    private void AddChord(in Chord chord, bool endSmooth, double nextX, double nextY, out double startX, out double startY, out double endX, out double endY)
    {
        startX = chord.DirectionX;
        startY = chord.DirectionY;
        var startCorrected = false;
        if (_sweepsCurves && !chord.StartSmooth && endSmooth && TryGetCurveEnd(chord.DirectionX, chord.DirectionY, nextX, nextY, out var curveX, out var curveY))
        {
            (startX, startY) = (curveX, curveY);
            startCorrected = true;
        }

        var endCorrected = GetEndDirection(in chord, endSmooth, out endX, out endY);
        if (chord.HasJoin)
        {
            AddJoin(chord.StartX, chord.StartY, chord.JoinX, chord.JoinY, startX, startY, smooth: false);
        }

        var dx = chord.DirectionX;
        var dy = chord.DirectionY;
        if (!_sweepsCurves || (!chord.StartSmooth && !endSmooth))
        {
            AddBody(chord.StartX, chord.StartY, chord.EndX, chord.EndY, dx, dy);
            return;
        }

        // The normals at both ends: between those of the chords at a vertex of a curve, of the direction of the curve
        // at its end
        var (startNormalX, startNormalY) = chord.StartSmooth ? GetBisector(-chord.PreviousY, chord.PreviousX, -dy, dx) : (-startY, startX);
        var (endNormalX, endNormalY) = endSmooth ? GetBisector(-dy, dx, -nextY, nextX) : (-endY, endX);

        // How the curve turns at each end: toward +n when the cross product is positive
        var startTurn = chord.StartSmooth ? Cross(chord.PreviousX, chord.PreviousY, dx, dy) : startCorrected ? Cross(dx, dy, nextX, nextY) : 0;
        var endTurn = endSmooth ? Cross(dx, dy, nextX, nextY) : endCorrected ? Cross(chord.PreviousX, chord.PreviousY, dx, dy) : 0;
        AddSweptBody(chord.StartX, chord.StartY, chord.EndX, chord.EndY, dx, dy, startNormalX, startNormalY, endNormalX, endNormalY, startTurn, endTurn);
    }

    /// <summary>Gets the direction of the stroke at the end of a chord: the direction of the curve there when the chord ends a curve, the one of the chord otherwise.</summary>
    /// <returns><see langword="true"/> when the direction of the curve is used.</returns>
    private bool GetEndDirection(in Chord chord, bool endSmooth, out double endX, out double endY)
    {
        if (_sweepsCurves && chord.StartSmooth && !endSmooth && TryGetCurveEnd(chord.DirectionX, chord.DirectionY, chord.PreviousX, chord.PreviousY, out endX, out endY))
            return true;

        endX = chord.DirectionX;
        endY = chord.DirectionY;
        return false;
    }

    /// <summary>
    /// Gets the direction of a curve at the end of its chord <c>d</c>, from the chord <c>e</c> next to it: <c>d</c> turned
    /// away from <c>e</c> by half the turn between them, the reflection of their bisector across <c>d</c>. The end of the
    /// stroke of a curve is perpendicular to the curve, not to its last chord.
    /// </summary>
    private static bool TryGetCurveEnd(double dx, double dy, double ex, double ey, out double x, out double y)
    {
        var mx = dx + ex;
        var my = dy + ey;
        var length = Math.Sqrt((mx * mx) + (my * my));
        if (length > 0)
        {
            mx /= length;
            my /= length;
            var dot = 2 * ((dx * mx) + (dy * my));
            x = (dot * dx) - mx;
            y = (dot * dy) - my;
            return true;
        }

        x = dx;
        y = dy;
        return false;
    }

    /// <summary>
    /// Adds the body of a chord of a curve: the area swept by the segment of the width of the pen perpendicular to the
    /// curve, from the normal at its start to the one at its end, with the half of the rectangle of the chord on the
    /// outer side of its turns, which the round joins continue. When the two normals cross within the pen (on the other
    /// side of the center of a curve narrower than the pen), the area swept is two triangles that meet there.
    /// </summary>
    private void AddSweptBody(double ax, double ay, double bx, double by, double dx, double dy, double nax, double nay, double nbx, double nby, double startTurn, double endTurn)
    {
        var h = _halfWidth;
        var nx = -dy * h;
        var ny = dx * h;
        var outerAX = ax + (h * nax);
        var outerAY = ay + (h * nay);
        var innerAX = ax - (h * nax);
        var innerAY = ay - (h * nay);
        var outerBX = bx + (h * nbx);
        var outerBY = by + (h * nby);
        var innerBX = bx - (h * nbx);
        var innerBY = by - (h * nby);

        // The side away from the center of the turns: +n when the curve turns toward -n. A chord between two turns of
        // opposite directions has both.
        var outerPlus = startTurn < 0 || endTurn < 0;
        var outerMinus = startTurn > 0 || endTurn > 0;
        Span<double> points = stackalloc double[12];
        if (outerPlus && outerMinus)
        {
            AddBody(ax, ay, bx, by, dx, dy);
            outerPlus = false;
            outerMinus = false;
        }

        // Where the two normals meet: A + s nA = B + t nB
        var denominator = Cross(nax, nay, nbx, nby);
        if (denominator != 0)
        {
            var s = Cross(bx - ax, by - ay, nbx, nby) / denominator;
            var t = Cross(bx - ax, by - ay, nax, nay) / denominator;
            if (Math.Abs(s) < h && Math.Abs(t) < h)
            {
                var crossX = ax + (s * nax);
                var crossY = ay + (s * nay);
                if (outerPlus)
                {
                    SetPoints(points[..10], outerAX, outerAY, crossX, crossY, outerBX, outerBY, bx + nx, by + ny, ax + nx, ay + ny);
                    AddOrientedPiece(points[..10]);
                }
                else
                {
                    SetPoints(points[..6], outerAX, outerAY, crossX, crossY, outerBX, outerBY);
                    AddOrientedPiece(points[..6]);
                }

                if (outerMinus)
                {
                    SetPoints(points[..10], crossX, crossY, innerBX, innerBY, bx - nx, by - ny, ax - nx, ay - ny, innerAX, innerAY);
                    AddOrientedPiece(points[..10]);
                }
                else
                {
                    SetPoints(points[..6], crossX, crossY, innerBX, innerBY, innerAX, innerAY);
                    AddOrientedPiece(points[..6]);
                }

                return;
            }
        }

        if (outerPlus)
        {
            SetPoints(points, outerAX, outerAY, innerAX, innerAY, innerBX, innerBY, outerBX, outerBY, bx + nx, by + ny, ax + nx, ay + ny);
            AddOrientedPiece(points);
        }
        else if (outerMinus)
        {
            SetPoints(points, outerAX, outerAY, innerAX, innerAY, ax - nx, ay - ny, bx - nx, by - ny, innerBX, innerBY, outerBX, outerBY);
            AddOrientedPiece(points);
        }
        else
        {
            SetPoints(points[..8], outerAX, outerAY, innerAX, innerAY, innerBX, innerBY, outerBX, outerBY);
            AddOrientedPiece(points[..8]);
        }
    }

    /// <summary>Adds a polygon as a piece with the orientation of the pieces (a positive shoelace sum), reversed when it turns the other way; nothing when it has no area.</summary>
    private void AddOrientedPiece(ReadOnlySpan<double> points)
    {
        var count = points.Length / 2;
        var area = 0d;
        for (var i = 0; i < count; i++)
        {
            var j = i + 1 == count ? 0 : i + 1;
            area += (points[2 * i] * points[(2 * j) + 1]) - (points[2 * j] * points[(2 * i) + 1]);
        }

        // Written so that an area that is not a number is skipped too
        if (!(area > 0 || area < 0))
            return;

        if (area > 0)
        {
            BeginPiece(points[0], points[1]);
            for (var i = 1; i < count; i++)
            {
                PieceLineTo(points[2 * i], points[(2 * i) + 1]);
            }
        }
        else
        {
            BeginPiece(points[0], points[1]);
            for (var i = count - 1; i > 0; i--)
            {
                PieceLineTo(points[2 * i], points[(2 * i) + 1]);
            }
        }

        ClosePiece();
    }

    private static void SetPoints(Span<double> points, params ReadOnlySpan<double> values) => values.CopyTo(points);

    private static double Cross(double ax, double ay, double bx, double by) => (ax * by) - (ay * bx);

    /// <summary>Gets the unit vector between two unit normals, or the second one when they are opposite.</summary>
    private static (double X, double Y) GetBisector(double ax, double ay, double bx, double by)
    {
        var x = ax + bx;
        var y = ay + by;
        var length = Math.Sqrt((x * x) + (y * y));
        return length > 0 ? (x / length, y / length) : (bx, by);
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
        var scale = Scale;
        if (_isProjectiveOutput)
        {
            // The factor of a projective transformation on the triangle of the ends of the arc and of the meeting
            // point of their tangents, which contains the arc
            var x0 = centerX + (_halfWidth * ux);
            var y0 = centerY + (_halfWidth * uy);
            var x1 = centerX + (_halfWidth * vx);
            var y1 = centerY + (_halfWidth * vy);
            var toTangents = 2 * _halfWidth / (length * length);
            var tx = centerX + (mx * toTangents);
            var ty = centerY + (my * toTangents);
            scale = _pieceView.GetScale(Math.Min(x0, Math.Min(x1, tx)), Math.Min(y0, Math.Min(y1, ty)), Math.Max(x0, Math.Max(x1, tx)), Math.Max(y0, Math.Max(y1, ty)));
        }

        if (depth >= PathFlattener.MaxArcDepth || !(_halfWidth * scale * (1 - (length / 2)) > Tolerance) || IsArcOutsideFrame(centerX, centerY, ux, uy, vx, vy, mx, my, length))
        {
            PieceLineTo(centerX + (_halfWidth * vx), centerY + (_halfWidth * vy));
            return;
        }

        mx /= length;
        my /= length;
        AddArc(centerX, centerY, ux, uy, mx, my, depth + 1);
        AddArc(centerX, centerY, mx, my, vx, vy, depth + 1);
    }

    /// <summary>
    /// Gets a value indicating whether an arc of a join or a cap is outside the frame: the triangle of its ends and of
    /// the meeting point of their tangents, which contains the arc and its chord, has a box on the canvas that misses the
    /// frame. Its chord then gives the same pixels: what is between them is not seen. A cap of a very wide pen far from
    /// the frame is not cut finely.
    /// </summary>
    private bool IsArcOutsideFrame(double centerX, double centerY, double ux, double uy, double vx, double vy, double mx, double my, double length)
    {
        if ((_edges is null && _pieces is null) || _isProjectiveOutput)
            return false;

        var toTangents = 2 * _halfWidth / (length * length);
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 3; corner++)
        {
            var (x, y) = corner switch
            {
                0 => (centerX + (_halfWidth * ux), centerY + (_halfWidth * uy)),
                1 => (centerX + (_halfWidth * vx), centerY + (_halfWidth * vy)),
                _ => (centerX + (mx * toTangents), centerY + (my * toTangents)),
            };

            if (_hasOutputTransform)
            {
                (x, y) = (_outputTransform.TransformX(x, y), _outputTransform.TransformY(x, y));
            }

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (_edges is null)
        {
            // The point is outside the triangle: the arc and its chord wind around it the same way
            return !(_testX >= minX && _testX <= maxX && _testY >= minY && _testY <= maxY);
        }

        return maxX < 0 || maxY < 0 || minX > _edges.Width || minY > _edges.Height;
    }

    /// <summary>
    /// Cuts the pieces of the rest of the stroke whose vertices leave the range of the coordinates: the path, placed on
    /// the canvas before it is stroked, has left it. The pieces that stay in the range are emitted as they are.
    /// </summary>
    public void ClipToRange() => _clipsToRange = true;

    private void BeginPiece(double x, double y)
    {
        if (_isProjectiveOutput)
        {
            _projectiveOutput!.MoveTo(x, y);
            return;
        }

        _isPieceClipped = false;
        if (_hasOutputTransform)
        {
            (x, y) = TransformOutput(x, y);
        }

        if (IsOutOfRange(x, y))
        {
            // A piece that leaves the range of the coordinates is cut by it
            StartPieceClipping(resume: false).MoveTo(x, y);
            return;
        }

        BeginOutputPiece(x, y);
    }

    /// <summary>Gets a value indicating whether a vertex of a piece on the canvas is beyond the range of the coordinates, which the canvas checks.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate of the vertex is not a finite number.</exception>
    private bool IsOutOfRange(double x, double y) => (_hasOutputTransform || _clipsToRange) && _isOutputChecked && TransformSink.IsOutOfRange(x, y, _originX, _originY);

    /// <summary>Starts cutting the piece being emitted by the range of the coordinates.</summary>
    /// <param name="resume"><see langword="true"/> when the piece has vertices on the canvas already, all in the range.</param>
    private RangeClipSink StartPieceClipping(bool resume)
    {
        var clipper = _rangeClipper ??= new RangeClipSink();
        clipper.Configure(_pieceSink ??= new PieceSink(this), -DrawingGuard.MaxCoordinate - _originX, -DrawingGuard.MaxCoordinate - _originY, DrawingGuard.MaxCoordinate - _originX, DrawingGuard.MaxCoordinate - _originY);
        if (resume)
        {
            clipper.Resume(_pieceStartX, _pieceStartY, _pieceX, _pieceY);
        }

        _isPieceClipped = true;
        return clipper;
    }

    /// <summary>Starts a piece at a point of the canvas.</summary>
    private void BeginOutputPiece(double x, double y)
    {
        _pieceStartX = x;
        _pieceStartY = y;
        _pieceX = x;
        _pieceY = y;
        _pieceMaxX = x;
        if (_edges is null)
        {
            if (_outline is not null)
            {
                _outline.AppendMoveTo(x, y);
            }
            else
            {
                _pieces!.MoveTo(x, y);
            }

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
        if (_isProjectiveOutput)
        {
            _projectiveOutput!.LineTo(x, y, smooth: false);
            return;
        }

        if (_hasOutputTransform)
        {
            (x, y) = TransformOutput(x, y);
        }

        if (_isPieceClipped)
        {
            _rangeClipper!.LineTo(x, y, smooth: false);
            return;
        }

        if (IsOutOfRange(x, y))
        {
            StartPieceClipping(resume: true).LineTo(x, y, smooth: false);
            return;
        }

        OutputLineTo(x, y);
    }

    /// <summary>Adds a side to the piece, to a point of the canvas.</summary>
    private void OutputLineTo(double x, double y)
    {
        AddPieceEdge(x, y);
        _pieceX = x;
        _pieceY = y;
        _pieceMaxX = Math.Max(_pieceMaxX, x);
    }

    private void ClosePiece()
    {
        if (_isProjectiveOutput)
        {
            _projectiveOutput!.Close(smooth: false);
            return;
        }

        if (_isPieceClipped)
        {
            // What remains of the piece, if anything, is closed by the clipper
            _isPieceClipped = false;
            _rangeClipper!.Close(smooth: false);
            _rangeClipper.End();
            return;
        }

        CloseOutputPiece();
    }

    /// <summary>Ends the piece on the canvas.</summary>
    private void CloseOutputPiece()
    {
        if (_edges is null)
        {
            if (_outline is not null)
            {
                _outline.Close();
            }
            else
            {
                _pieces!.Close(smooth: false);
            }

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
    private (double X, double Y) TransformOutput(double x, double y) => (_outputTransform.TransformX(x, y), _outputTransform.TransformY(x, y));

    private void AddPieceEdge(double x, double y)
    {
        if (_edges is null)
        {
            if (_outline is not null)
            {
                _outline.AppendLineTo(x, y);
            }
            else
            {
                _pieces!.LineTo(x, y, smooth: false);
            }

            return;
        }

        if (CoversFrame)
            return;

        _edges.Add(_pieceX, _pieceY, x, y);

        // A side without length has no inner side: the closing side of a disk, whose last arc ends on its first point
        if (_testFrame && (x != _pieceX || y != _pieceY))
        {
            // A point is strictly inside a convex piece when it is strictly on the inner side of every edge; with the
            // orientation of the pieces, that side is where the cross product is positive
            var dx = x - _pieceX;
            var dy = y - _pieceY;
            var left = -_pieceX;
            var top = -_pieceY;
            var right = _edges.Width - _pieceX;
            var bottom = _edges.Height - _pieceY;
            var orientation = _frameOrientation;
            _topLeftInside &= orientation * ((dx * top) - (dy * left)) > 0;
            _topRightInside &= orientation * ((dx * top) - (dy * right)) > 0;
            _bottomLeftInside &= orientation * ((dx * bottom) - (dy * left)) > 0;
            _bottomRightInside &= orientation * ((dx * bottom) - (dy * right)) > 0;
        }
    }

    /// <summary>A chord of a contour, with what its pieces need to know about the vertex at its start.</summary>
    private struct Chord
    {
        public double StartX;
        public double StartY;
        public double EndX;
        public double EndY;
        public double DirectionX;
        public double DirectionY;

        // Whether the vertex at its start was created by flattening a curve, and the chord before it
        public bool StartSmooth;
        public double PreviousX;
        public double PreviousY;

        // A join at its start, a corner, waits for the direction the stroke starts the chord with; it comes from the
        // direction the stroke ends the chord before it with
        public bool HasJoin;
        public double JoinX;
        public double JoinY;
    }

    /// <summary>Receives the contours the stroke is made of, when they do not come to the stroker directly: the chords near the point of a test.</summary>
    private sealed class ContourSink(Stroker stroker) : PathSink
    {
        public override void MoveTo(double x, double y) => stroker.MoveToCore(x, y);

        public override void LineTo(double x, double y, bool smooth) => stroker.LineToCore(x, y, smooth);

        public override void Close(bool smooth) => stroker.CloseCore(smooth);

        public override void End() => stroker.EndCore();
    }

    /// <summary>Receives the pieces of the stroke once they are cut and transformed by a projective transformation.</summary>
    private sealed class PieceSink(Stroker stroker) : PathSink
    {
        public override void MoveTo(double x, double y) => stroker.BeginOutputPiece(x, y);

        public override void LineTo(double x, double y, bool smooth) => stroker.OutputLineTo(x, y);

        public override void Close(bool smooth) => stroker.CloseOutputPiece();

        public override void End()
        {
        }
    }
}
