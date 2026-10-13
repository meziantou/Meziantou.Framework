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
/// a canvas, what is far outside the canvas is not cut into dashes: the pattern is advanced by the length that is
/// skipped, so a long path with a short pattern costs what the canvas shows of it. The cancellation token is checked
/// every few hundred dashes.
/// </para>
/// </remarks>
internal sealed class Dasher : PathSink, IDisposable
{
    private const int InitialCapacity = 64;
    private const int ValuesPerVertex = 3;
    private const int DashesPerCancellationCheck = 256;

    private readonly AllocationScope? _scope;
    private Stroker _stroker = null!;
    private double[] _pattern = [];
    private double _patternLength;
    private CancellationToken _cancellationToken;
    private int _dashCount;

    // The part of the plane where a dash can change a pixel
    private bool _hasBox;
    private double _boxLeft;
    private double _boxTop;
    private double _boxRight;
    private double _boxBottom;

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
    public void Configure(Pen pen, Stroker stroker, double width, double height, CancellationToken cancellationToken)
    {
        Configure(pen, stroker);
        _cancellationToken = cancellationToken;

        // A dash reaches half the width of the pen from its path, times the miter limit at a corner, and a square cap
        // reaches the diagonal of half the width
        var margin = (pen.Width / 2 * Math.Max(pen.LineJoin == LineJoin.Miter ? pen.MiterLimit : 1, 2)) + 1;
        _hasBox = true;
        _boxLeft = -margin;
        _boxTop = -margin;
        _boxRight = width + margin;
        _boxBottom = height + margin;
    }

    /// <summary>Sets the pen and the stroker of the next path, wherever it is.</summary>
    public void Configure(Pen pen, Stroker stroker)
    {
        _stroker = stroker;
        _pattern = pen.Dashes;
        _patternLength = pen.DashLength;
        _cancellationToken = default;
        _dashCount = 0;
        _hasBox = false;
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
        double from = 0;
        var to = length;
        if (_hasBox && !TryClip(startX, startY, dx, dy, length, out from, out to))
        {
            // Nothing of the segment can be seen: the pattern goes on without it
            EndDash();
            Advance(length);
            return;
        }

        var ux = dx / length;
        var uy = dy / length;

        // A dash that comes from the segment before continues through the vertex between them
        var isContinued = _dashOpen && from == 0;
        if (from > 0)
        {
            EndDash();
            Advance(from);
        }

        var position = from;
        while (true)
        {
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

        if (to < length)
        {
            EndDash();
            Advance(length - to);
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

    /// <summary>Gets the part of a segment that is inside the box where a dash can change a pixel, as distances from its start.</summary>
    /// <returns><see langword="false"/> when the segment is entirely outside.</returns>
    private bool TryClip(double x, double y, double dx, double dy, double length, out double from, out double to)
    {
        double t0 = 0;
        double t1 = 1;
        from = 0;
        to = length;
        if (!ClipAxis(x, dx, _boxLeft, _boxRight, ref t0, ref t1) || !ClipAxis(y, dy, _boxTop, _boxBottom, ref t0, ref t1))
            return false;

        if (t0 > 0)
        {
            from = Math.Min(t0 * length, length);
        }

        if (t1 < 1)
        {
            to = Math.Min(t1 * length, length);
        }

        return from < to;
    }

    /// <summary>Narrows the range of the parameters of a segment to those whose coordinate is between two bounds.</summary>
    private static bool ClipAxis(double start, double delta, double low, double high, ref double t0, ref double t1)
    {
        if (delta == 0)
            return start >= low && start <= high;

        var a = (low - start) / delta;
        var b = (high - start) / delta;
        if (a > b)
        {
            (a, b) = (b, a);
        }

        t0 = Math.Max(t0, a);
        t1 = Math.Min(t1, b);
        return t0 <= t1;
    }

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
