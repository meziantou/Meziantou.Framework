namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Passes on the chords of a path that are near a point, with what the pieces of their stroke near that point need: the
/// test of whether a stroke contains a point (<see cref="Stroker"/>) does not compute the pieces that cannot contain it.
/// </summary>
/// <remarks>
/// <para>
/// A chord is far when the point is farther from it than the reach of the stroke (half the width of the pen, its miters,
/// the corners of its square caps): its body, and the joins and the caps at its ends, cannot contain the point, since
/// each is within that reach of the chord or of one of its ends. The body of a near chord depends on the chords next to
/// it: their directions and the smooth flags of the vertices between them decide how it is swept. A join at one of its
/// ends depends on the chords beyond too (the direction a curve ends with comes from its last two chords), but a join
/// that can contain the point is at a vertex near it, whose two chords are near. A run of near chords is therefore
/// passed on as an open contour of its own, from the chord before it to the chord after it: the joins and the caps the
/// stroker makes differently at the ends of such a contour are at the ends of a far chord, which cannot contain the point.
/// </para>
/// <para>
/// The ends of a contour are where a cap or the join of a closed contour is: the chords from its start to the first far
/// one are kept until the contour ends, then passed on from the start of the contour (an open contour, whose start has
/// a cap) or after the chord that closes it (a closed contour, whose start is a join). A contour without a far chord is
/// passed on as it came.
/// </para>
/// </remarks>
internal sealed class NearChordFilter : PathSink
{
    private readonly PathSink _target;
    private readonly double _pointX;
    private readonly double _pointY;
    private double _reachSquared;

    // The contour being received
    private bool _hasStart;
    private bool _implicitStart;
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;

    // The chords from the start of the contour, kept until it ends, and whether a far chord ended them
    private Chord[] _first = new Chord[8];
    private int _firstCount;
    private bool _isSplit;

    // The last chord, which starts the contour passed on when a near chord follows it, and whether a run of near chords
    // is being passed on
    private Chord _last;
    private bool _isPassing;

    public NearChordFilter(PathSink target, double x, double y)
    {
        _target = target;
        _pointX = x;
        _pointY = y;
    }

    /// <summary>Sets the reach of the stroke: how far its pieces are from their chord, at most.</summary>
    public void Configure(double reach)
    {
        // A margin far larger than the rounding of the distances, which coordinates of 16777216 at most bound
        var limit = (reach * (1 + (1.0 / (1L << 30)))) + (1.0 / (1L << 20));
        _reachSquared = limit * limit;
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
        _implicitStart = false;
        StartContour();
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (!_hasStart)
        {
            MoveTo(x, y);
            return;
        }

        // A chord without length is skipped by the stroker, smooth flag included
        var dx = x - _x;
        var dy = y - _y;
        if (!((dx * dx) + (dy * dy) > 0))
            return;

        var chord = new Chord(_x, _y, x, y, smooth, IsFar(_x, _y, dx, dy));
        _x = x;
        _y = y;
        Add(in chord);
    }

    public override void Close(bool smooth)
    {
        if (!_hasStart)
            return;

        if (!_isSplit)
        {
            PassFirst(implicitStart: _implicitStart);
            if (_firstCount != 0 || !_implicitStart)
            {
                _target.Close(smooth);
            }
        }
        else
        {
            // The chord that closes the contour, then its first chords, whose start is a vertex with the flag of the
            // closing chord: the stroker joins them as it joins the vertex where a closed contour starts
            var dx = _startX - _x;
            var dy = _startY - _y;
            if ((dx * dx) + (dy * dy) > 0)
            {
                Process(new Chord(_x, _y, _startX, _startY, smooth, IsFar(_x, _y, dx, dy)));
            }

            for (var i = 0; i < _firstCount; i++)
            {
                var chord = _first[i];
                Process(i == 0 ? chord with { Smooth = smooth } : chord);
            }

            EndPassing();
        }

        // A segment that follows starts a new contour at the first point of this one
        _x = _startX;
        _y = _startY;
        _implicitStart = true;
        StartContour();
    }

    public override void End()
    {
        FinishOpenContour();
        _hasStart = false;
        _target.End();
    }

    private void FinishOpenContour()
    {
        if (!_hasStart)
            return;

        if (!_isSplit)
        {
            PassFirst(_implicitStart);
            _target.End();
            return;
        }

        // The last chords end the contour: the stroker caps them. The first chords are passed on from the start of the
        // contour, which they cap, unless none of them is near.
        EndPassing();
        var isNear = false;
        for (var i = 0; i < _firstCount; i++)
        {
            isNear |= !_first[i].IsFar;
        }

        if (isNear)
        {
            _target.MoveTo(_startX, _startY);
            for (var i = 0; i < _firstCount; i++)
            {
                _target.LineTo(_first[i].EndX, _first[i].EndY, _first[i].Smooth);
            }

            _target.End();
        }
    }

    private void StartContour()
    {
        _firstCount = 0;
        _isSplit = false;
        _isPassing = false;
    }

    /// <summary>Passes on the contour as it came, when it was not split: a contour after a closed one that has no chord has nothing to stroke.</summary>
    private void PassFirst(bool implicitStart)
    {
        if (implicitStart && _firstCount == 0)
            return;

        _target.MoveTo(_startX, _startY);
        for (var i = 0; i < _firstCount; i++)
        {
            _target.LineTo(_first[i].EndX, _first[i].EndY, _first[i].Smooth);
        }
    }

    private void Add(in Chord chord)
    {
        if (_isSplit)
        {
            Process(chord);
            return;
        }

        if (_firstCount == _first.Length)
        {
            Array.Resize(ref _first, _first.Length * 2);
        }

        _first[_firstCount++] = chord;

        // A far chord ends the chords kept for the end of the contour: what follows is passed on by runs
        if (chord.IsFar)
        {
            _isSplit = true;
            _last = chord;
        }
    }

    /// <summary>Passes on a chord that follows another one: from that one when it is near, and with the far chord after the last near one.</summary>
    private void Process(in Chord chord)
    {
        if (chord.IsFar)
        {
            if (_isPassing)
            {
                _target.LineTo(chord.EndX, chord.EndY, chord.Smooth);
                EndPassing();
            }
        }
        else
        {
            if (!_isPassing)
            {
                _target.MoveTo(_last.StartX, _last.StartY);
                _target.LineTo(_last.EndX, _last.EndY, _last.Smooth);
                _isPassing = true;
            }

            _target.LineTo(chord.EndX, chord.EndY, chord.Smooth);
        }

        _last = chord;
    }

    private void EndPassing()
    {
        if (_isPassing)
        {
            _target.End();
            _isPassing = false;
        }
    }

    /// <summary>Gets a value indicating whether the point is farther from a chord than the reach of the stroke.</summary>
    private bool IsFar(double x, double y, double dx, double dy)
    {
        var t = (((_pointX - x) * dx) + ((_pointY - y) * dy)) / ((dx * dx) + (dy * dy));
        t = t > 0 ? (t < 1 ? t : 1) : 0;
        var ex = _pointX - (x + (t * dx));
        var ey = _pointY - (y + (t * dy));
        return (ex * ex) + (ey * ey) > _reachSquared;
    }

    private readonly record struct Chord(double StartX, double StartY, double EndX, double EndY, bool Smooth, bool IsFar);
}
