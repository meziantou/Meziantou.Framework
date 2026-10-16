using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Cuts the contours of a flattened path into the dashes of a pen, and sends each dash to a stroker as an open contour.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The pattern of the pen is a list of lengths, alternately of a dash and of a gap, repeated along each
/// contour; a pattern of an odd number of lengths is used twice, so that it is a whole number of dashes and gaps. A
/// contour starts <see cref="Pen.DashOffset"/> into the pattern, and every contour starts over. Lengths are measured
/// along the chords of the path. A dash is an open contour: it gets the caps of the pen at both ends, and the joins of
/// the pen at the vertices of the path it goes through. A dash without length is drawn by its caps alone, turned to
/// the direction of the path: a dot for round caps, a square for square caps, nothing for butt caps.
/// </para>
/// <para>
/// When a closed contour ends in the middle of a dash, that dash continues with the first dash of the contour: they are
/// one dash, joined at the first point of the contour like any vertex, not two dashes with a cap each. A closed contour
/// that is shorter than its first dash is stroked as a closed contour. The first dash of every contour is therefore
/// kept until the contour ends; that storage grows to the longest first dash and is kept.
/// </para>
/// <para>
/// <b>Cost.</b> The time and the edges of a dashed stroke grow with the number of dashes. When the stroke is drawn on
/// a canvas, only the parts of a segment whose dashes can change a pixel are cut into dashes, and the pattern is
/// advanced by the length of the others: a long path with a short pattern costs what the canvas shows of it. A dash
/// changes a pixel of the canvas through its body (within half the width of the pen of the segment, between its ends),
/// through its caps (half the width further along the segment) or through the join of a vertex it goes through. So a
/// segment is cut where the canvas is, seen along the segment, plus the reach of a cap; and, when one of its ends is
/// close enough to the canvas for a join or a cap there to show, on a short length next to that end, which is all the
/// stroker needs to draw the join or the cap. Once the stroke covers the whole canvas, nothing more is cut. The
/// cancellation token is checked every few hundred dashes.
/// </para>
/// <para>
/// Under a transformation of the canvas, affine or projective, the dashes are cut in the coordinates of the drawing,
/// where the part of the drawing whose image is on the canvas (with a pixel around it) is the intersection of four
/// half-planes. A segment is cut where it is within the reach of a body of every half-plane, plus the reach of a cap
/// along it, and next to an end that is within the reach of a join of every half-plane; the reaches are lengths of the
/// drawing, enlarged by what the pen reaches beyond its width (<see cref="ProjectiveView.Reach"/>).
/// </para>
/// </remarks>
internal sealed class Dasher : PathSink, IDisposable
{
    private const int InitialCapacity = 64;
    private const int ValuesPerVertex = 3;
    private const int DashesPerCancellationCheck = 256;

    /// <summary>The length of a segment that is cut next to one of its ends for the join or the cap of that end.</summary>
    private const double VertexLength = 1;

    private readonly AllocationScope? _scope;
    private Stroker _stroker = null!;
    private double[] _pattern = [];
    private double _patternLength;
    private CancellationToken _cancellationToken;
    private int _dashCount;

    // The canvas, and how far from it a dash can be and still change one of its pixels: across the segment, along it,
    // and from a vertex
    private bool _hasCanvas;
    private double _canvasWidth;
    private double _canvasHeight;

    // Under a transformation of the canvas: the four half-planes of the drawing whose image is on the canvas, as
    // (a, b, c) with a x + b y + c the distance to the border of the half-plane, positive inside
    private bool _hasPlanes;
    private readonly double[] _planes = new double[12];
    private double _acrossReach;
    private double _alongReach;
    private double _vertexReach;

    // Where a contour starts in the pattern
    private int _startIndex;
    private double _startRemaining;

    // Where the current contour is in the pattern
    private int _index;
    private double _remaining;
    private bool _dashOpen;

    // The current contour
    private bool _hasCurrent;
    private bool _hasSegment;
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;

    // The first dash of the current contour: its vertices (x, y, and whether the vertex before is smooth)
    private bool _isBufferingFirst;
    private int _firstCount;
    private PooledBuffer? _pooled;
    private double[]? _managed;
    private int _capacity;

    /// <param name="scope">The scope the storage of the first dash is rented from, or <see langword="null"/> to allocate it: the dashes of a path that is not drawn on an image.</param>
    public Dasher(AllocationScope? scope)
    {
        _scope = scope;
    }

    private bool IsOn => (_index & 1) == 0;

    private Span<double> First => _pooled is not null ? unsafe(MemoryMarshal.Cast<byte, double>(_pooled.Span)) : _managed;

    /// <summary>Sets the pen and the stroker of the next path, drawn on a canvas of a size.</summary>
    public void Configure(Pen pen, Stroker stroker, double width, double height, CancellationToken cancellationToken, double widthFactor = 1)
    {
        Configure(pen, stroker, cancellationToken);

        // The body of a dash is within half the width of the pen of its segment; a cap goes that far beyond its end; a
        // miter goes as far as the miter limit allows from its vertex, and the corner of a square cap or of a square dot
        // the diagonal of half the width. One more pixel than needed never shows.
        var half = pen.StrokeWidth * widthFactor / 2;
        _hasCanvas = true;
        _canvasWidth = width;
        _canvasHeight = height;
        _acrossReach = half + 1;
        _alongReach = (pen.LineCap == LineCap.Butt ? 0 : half) + 1;
        _vertexReach = (half * Math.Max(pen.LineJoin == LineJoin.Miter ? pen.MiterLimit : 1, 2)) + 1;
    }

    /// <summary>
    /// Sets the pen and the stroker of the next path, drawn in the coordinates of a drawing that a transformation places
    /// on a canvas: the part of the drawing that is seen is the intersection of the half-planes of the view, whose
    /// functions are affine in the coordinates of the drawing whatever the transformation.
    /// </summary>
    public void Configure(Pen pen, Stroker stroker, in ProjectiveView view, CancellationToken cancellationToken, double widthFactor = 1)
    {
        Configure(pen, stroker, cancellationToken);
        var half = pen.StrokeWidth * widthFactor / 2;
        _hasPlanes = true;
        _acrossReach = half + view.Reach;
        _alongReach = (pen.LineCap == LineCap.Butt ? 0 : half) + view.Reach;
        _vertexReach = (half * Math.Max(pen.LineJoin == LineJoin.Miter ? pen.MiterLimit : 1, 2)) + view.Reach;
        var transform = view.Transform;
        for (var plane = 0; plane < 4; plane++)
        {
            // X - Left W, Right W - X, Y - Top W, Bottom W - Y, as functions of the point of the drawing
            var (sign, limit, column) = plane switch
            {
                0 => (1d, view.Left, 0),
                1 => (-1d, view.Right, 0),
                2 => (1d, view.Top, 1),
                _ => (-1d, view.Bottom, 1),
            };
            var a = sign * ((column == 0 ? transform.M11 : transform.M12) - (limit * transform.M13));
            var b = sign * ((column == 0 ? transform.M21 : transform.M22) - (limit * transform.M23));
            var c = sign * ((column == 0 ? transform.M31 : transform.M32) - (limit * transform.M33));
            var norm = Math.Sqrt((a * a) + (b * b));
            if (norm > 0)
            {
                a /= norm;
                b /= norm;
                c /= norm;
            }

            _planes[3 * plane] = a;
            _planes[(3 * plane) + 1] = b;
            _planes[(3 * plane) + 2] = c;
        }
    }

    /// <summary>Sets the pen and the stroker of the next path, wherever it is: every dash of the path is cut.</summary>
    public void Configure(Pen pen, Stroker stroker, CancellationToken cancellationToken = default)
    {
        _stroker = stroker;
        _pattern = pen.Dashes;
        _patternLength = pen.DashLength;
        _cancellationToken = cancellationToken;
        _dashCount = 0;
        _hasCanvas = false;
        _hasPlanes = false;
        _hasCurrent = false;
        _index = 0;
        _remaining = _pattern[0];
        if (pen.DashOffset != 0)
        {
            Advance(pen.DashOffset);
        }

        _startIndex = _index;
        _startRemaining = _remaining;
    }

    public override void MoveTo(double x, double y)
    {
        FinishContour();
        BeginContour(x, y);
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_hasCurrent)
        {
            BeginContour(x, y);
            return;
        }

        var dx = x - _x;
        var dy = y - _y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        // A segment without length has no direction: it is skipped, as the stroker does
        if (!(length > 0))
            return;

        var startX = _x;
        var startY = _y;
        _x = x;
        _y = y;
        _hasSegment = true;

        // The stroke already covers the whole canvas: nothing can be added to it
        if (_stroker.CoversFrame)
            return;

        var ux = dx / length;
        var uy = dy / length;

        // The parts of the segment whose dashes can change a pixel, as distances from its start. Between them, the
        // pattern goes on without drawing.
        Span<double> ranges = stackalloc double[6];
        var count = GetVisibleRanges(startX, startY, x, y, ux, uy, length, ranges);
        double position = 0;
        for (var i = 0; i < count; i++)
        {
            var from = ranges[2 * i];
            if (from > position)
            {
                EndDash();
                Advance(from - position);
            }

            position = ranges[(2 * i) + 1];
            CutRange(startX, startY, x, y, ux, uy, length, from, position, smooth);
        }

        if (position < length)
        {
            EndDash();
            Advance(length - position);
        }
    }

    /// <summary>Cuts a part of a segment into dashes.</summary>
    private void CutRange(double startX, double startY, double x, double y, double ux, double uy, double length, double from, double to, bool smooth)
    {
        // A dash that comes from the segment before continues through the vertex between them
        var isContinued = _dashOpen && from == 0;
        var position = from;
        while (true)
        {
            // A dash covered the whole canvas: the stroke is complete
            if (_stroker.CoversFrame)
                return;

            if (!(_remaining > 0))
            {
                // The dash or the gap is over, or has no length
                if (IsOn && !_dashOpen)
                {
                    _stroker.AddDot(startX + (position * ux), startY + (position * uy), ux, uy);
                }

                EndDash();
                NextEntry();
                continue;
            }

            if (position >= to)
                break;

            var step = Math.Min(_remaining, to - position);
            var next = position + step;
            if (IsOn)
            {
                if (!_dashOpen)
                {
                    StartDash(startX + (position * ux), startY + (position * uy));
                    isContinued = false;
                }

                // The end of the segment is its end point, exactly: the dash goes on from there
                if (next >= length)
                {
                    DashLineTo(x, y, isContinued && smooth);
                }
                else
                {
                    DashLineTo(startX + (next * ux), startY + (next * uy), isContinued && smooth);
                }

                isContinued = false;
            }

            _remaining -= step;
            position = next;
        }
    }

    public override void Close(bool smooth)
    {
        if (!_hasCurrent)
            return;

        LineTo(_startX, _startY, smooth);
        if (_firstCount > 0)
        {
            if (_isBufferingFirst)
            {
                // The contour is shorter than its first dash: it is stroked whole, as a closed contour
                EmitFirst();
                _stroker.Close(smooth);
            }
            else if (_dashOpen)
            {
                // The last dash reaches the first point: it continues with the first dash
                var first = First;
                for (var i = 1; i < _firstCount; i++)
                {
                    _stroker.LineTo(first[i * ValuesPerVertex], first[(i * ValuesPerVertex) + 1], i == 1 ? smooth : first[(i * ValuesPerVertex) + 2] != 0);
                }
            }
            else
            {
                EmitFirst();
            }
        }
        else if (!_hasSegment && (_startIndex & 1) == 0)
        {
            // A contour reduced to a point, in a dash: the stroker draws its caps
            _stroker.MoveTo(_startX, _startY);
            _stroker.Close(smooth);
        }

        // A segment that follows starts a new contour at the first point of this one
        BeginContour(_startX, _startY);
        _hasSegment = true;
    }

    public override void End()
    {
        FinishContour();
        _hasCurrent = false;
        _stroker.End();
    }

    public void Dispose()
    {
        _pooled?.Dispose();
        _pooled = null;
        _managed = null;
        _capacity = 0;
        _firstCount = 0;
    }

    private void BeginContour(double x, double y)
    {
        _startX = x;
        _startY = y;
        _x = x;
        _y = y;
        _hasCurrent = true;
        _hasSegment = false;
        _index = _startIndex;
        _remaining = _startRemaining;
        _dashOpen = false;
        _firstCount = 0;

        // The first dash is kept until the end of the contour says whether the last dash continues with it
        _isBufferingFirst = IsOn && _remaining > 0;
    }

    /// <summary>Ends a contour that stays open.</summary>
    private void FinishContour()
    {
        if (!_hasCurrent)
            return;

        if (_firstCount > 0)
        {
            EmitFirst();
        }
        else if (!_hasSegment && (_startIndex & 1) == 0)
        {
            // A contour reduced to a point, in a dash: the stroker draws its caps
            _stroker.MoveTo(_startX, _startY);
        }

        _firstCount = 0;
        _dashOpen = false;
    }

    /// <summary>Sends the first dash of the contour to the stroker, as an open contour.</summary>
    private void EmitFirst()
    {
        var first = First;
        _stroker.MoveTo(first[0], first[1]);
        for (var i = 1; i < _firstCount; i++)
        {
            _stroker.LineTo(first[i * ValuesPerVertex], first[(i * ValuesPerVertex) + 1], first[(i * ValuesPerVertex) + 2] != 0);
        }

        _firstCount = 0;
        _isBufferingFirst = false;
    }

    private void StartDash(double x, double y)
    {
        _dashOpen = true;
        if (_isBufferingFirst)
        {
            AddFirst(x, y, smooth: false);
        }
        else
        {
            _stroker.MoveTo(x, y);
        }
    }

    private void DashLineTo(double x, double y, bool smooth)
    {
        if (_isBufferingFirst)
        {
            AddFirst(x, y, smooth);
        }
        else
        {
            _stroker.LineTo(x, y, smooth);
        }
    }

    /// <summary>Ends the dash being drawn, if any: the contour of the stroker stays open until the next one starts, and gets its caps then.</summary>
    private void EndDash()
    {
        _dashOpen = false;
        _isBufferingFirst = false;
    }

    private void NextEntry()
    {
        _index++;
        if (_index == _pattern.Length)
        {
            _index = 0;
        }

        _remaining = _pattern[_index];
        if (++_dashCount % DashesPerCancellationCheck == 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Moves along the pattern by a distance, without drawing; dashes without length on the way are not drawn.</summary>
    private void Advance(double distance)
    {
        // Whole repetitions of the pattern change nothing
        distance -= Math.Floor(distance / _patternLength) * _patternLength;
        while (distance > 0 && distance >= _remaining)
        {
            distance -= _remaining;
            NextEntry();
        }

        _remaining -= distance;
    }

    /// <summary>
    /// Gets the parts of a segment whose dashes can change a pixel of the canvas, as distances from its start, in
    /// increasing order and apart from each other: at most three.
    /// </summary>
    /// <returns>The number of parts; each one takes two values of <paramref name="ranges"/>.</returns>
    private int GetVisibleRanges(double startX, double startY, double endX, double endY, double ux, double uy, double length, Span<double> ranges)
    {
        if (_hasPlanes)
            return GetVisibleRangesInPlanes(startX, startY, endX, endY, ux, uy, length, ranges);

        if (!_hasCanvas)
        {
            ranges[0] = 0;
            ranges[1] = length;
            return 1;
        }

        // The canvas seen from the segment: how far along it and how far across it the four corners are
        var minAlong = double.PositiveInfinity;
        var maxAlong = double.NegativeInfinity;
        var minAcross = double.PositiveInfinity;
        var maxAcross = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var cx = ((corner & 1) == 0 ? 0 : _canvasWidth) - startX;
            var cy = ((corner & 2) == 0 ? 0 : _canvasHeight) - startY;
            var along = (cx * ux) + (cy * uy);
            var across = (cy * ux) - (cx * uy);
            minAlong = Math.Min(minAlong, along);
            maxAlong = Math.Max(maxAlong, along);
            minAcross = Math.Min(minAcross, across);
            maxAcross = Math.Max(maxAcross, across);
        }

        // Where the bodies and the caps of the dashes can reach the canvas
        double from = 0;
        double to = -1;
        if (minAcross <= _acrossReach && maxAcross >= -_acrossReach)
        {
            from = Math.Max(minAlong - _alongReach, 0);
            to = Math.Min(maxAlong + _alongReach, length);
        }

        // Next to an end whose join or cap can reach the canvas, enough of the segment for the stroker to draw it
        var count = 0;
        if (IsNearCanvas(startX, startY))
        {
            ranges[0] = 0;
            ranges[1] = Math.Min(VertexLength, length);
            count = 1;
        }

        if (from < to)
        {
            count = AddRange(ranges, count, from, to);
        }

        if (IsNearCanvas(endX, endY))
        {
            count = AddRange(ranges, count, Math.Max(length - VertexLength, 0), length);
        }

        return count;
    }

    /// <summary>The same, when the canvas is the intersection of four half-planes of the drawing.</summary>
    private int GetVisibleRangesInPlanes(double startX, double startY, double endX, double endY, double ux, double uy, double length, Span<double> ranges)
    {
        // Where the segment, moved across by the reach of the bodies, is in every half-plane
        double low = 0;
        double high = length;
        for (var plane = 0; plane < 4 && low <= high; plane++)
        {
            var a = _planes[3 * plane];
            var b = _planes[(3 * plane) + 1];
            var start = (a * startX) + (b * startY) + _planes[(3 * plane) + 2] + _acrossReach;
            var slope = (a * ux) + (b * uy);

            // Written so that a value that is not a number leaves the whole segment
            if (slope > 0)
            {
                low = Math.Max(low, -start / slope);
            }
            else if (slope < 0)
            {
                high = Math.Min(high, start / -slope);
            }
            else if (start < 0)
            {
                high = -1;
            }
        }

        double from = 0;
        double to = -1;
        if (low <= high)
        {
            from = Math.Max(low - _alongReach, 0);
            to = Math.Min(high + _alongReach, length);
        }

        var count = 0;
        if (IsNearPlanes(startX, startY))
        {
            ranges[0] = 0;
            ranges[1] = Math.Min(VertexLength, length);
            count = 1;
        }

        if (from < to)
        {
            count = AddRange(ranges, count, from, to);
        }

        if (IsNearPlanes(endX, endY))
        {
            count = AddRange(ranges, count, Math.Max(length - VertexLength, 0), length);
        }

        return count;
    }

    /// <summary>Gets a value indicating whether a join or a cap at a point can change a pixel of the canvas seen through the half-planes.</summary>
    private bool IsNearPlanes(double x, double y)
    {
        for (var plane = 0; plane < 4; plane++)
        {
            // Written so that a value that is not a number is near
            if ((_planes[3 * plane] * x) + (_planes[(3 * plane) + 1] * y) + _planes[(3 * plane) + 2] + _vertexReach < 0)
                return false;
        }

        return true;
    }

    /// <summary>Adds a part that starts at or after the start of the last one, merging them when they touch.</summary>
    private static int AddRange(Span<double> ranges, int count, double from, double to)
    {
        if (count > 0 && from <= ranges[(2 * count) - 1])
        {
            // The parts are added by increasing start, except the part of the canvas, which may start before the end
            // of the part next to the first vertex
            ranges[(2 * count) - 2] = Math.Min(ranges[(2 * count) - 2], from);
            ranges[(2 * count) - 1] = Math.Max(ranges[(2 * count) - 1], to);
            return count;
        }

        ranges[2 * count] = from;
        ranges[(2 * count) + 1] = to;
        return count + 1;
    }

    /// <summary>Gets a value indicating whether a join or a cap at a point can change a pixel of the canvas.</summary>
    private bool IsNearCanvas(double x, double y)
        => x >= -_vertexReach && x <= _canvasWidth + _vertexReach && y >= -_vertexReach && y <= _canvasHeight + _vertexReach;

    private void AddFirst(double x, double y, bool smooth)
    {
        if (_firstCount == _capacity)
        {
            Grow();
        }

        var entry = First.Slice(_firstCount * ValuesPerVertex, ValuesPerVertex);
        entry[0] = x;
        entry[1] = y;
        entry[2] = smooth ? 1 : 0;
        _firstCount++;
    }

    private void Grow()
    {
        var capacity = _capacity == 0 ? InitialCapacity : (long)_capacity * 2;
        if (_scope is null)
        {
            var managed = new double[capacity * ValuesPerVertex];
            _managed?.CopyTo(managed, 0);
            _managed = managed;
        }
        else
        {
            var buffer = ResampleWeights.Rent<double>(_scope, capacity * ValuesPerVertex);
            if (_pooled is not null)
            {
                _pooled.Span.CopyTo(buffer.Span);
                _pooled.Dispose();
            }

            _pooled = buffer;
        }

        _capacity = (int)capacity;
    }
}
