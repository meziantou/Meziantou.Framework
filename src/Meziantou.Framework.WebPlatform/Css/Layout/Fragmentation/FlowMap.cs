using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The breaks of a flow of a fragmentation context (the content of the fragmentation context root, or a parallel flow,
/// <see cref="ParallelItem"/>) chosen by <see cref="ColumnBreaker"/>: the fragmentainer of each position of the strip, and its
/// offset in the fragmentainer. The flow is a sequence of pieces: a piece starts at a break (the positions after it are in the
/// next fragmentainer), or after a track of parallel flows that grew (the positions after it are shifted in the same
/// fragmentainer).
/// </summary>
internal sealed class FlowMap
{
    private const double Epsilon = 0.01;

    private readonly List<Piece> _pieces = [];

    /// <summary>A range of positions of the strip that are in one fragmentainer, at the same distance of their offsets in it.</summary>
    /// <param name="From">The first position of the piece.</param>
    /// <param name="Index">The index of the fragmentainer.</param>
    /// <param name="Origin">The position of the strip at the block-start edge of the fragmentainer (a position of the piece is at the offset <c>position - Origin</c>).</param>
    /// <param name="IsBreak">Whether the piece starts at a break: the content that ends at <paramref name="From"/> is in the previous piece.</param>
    /// <param name="Sequence">The order of the break in the content (<see cref="BreakCandidate.Sequence"/>), or 0.</param>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Piece(double From, int Index, double Origin, bool IsBreak, int Sequence = 0);

    /// <summary>The ranges of positions that are truncated at a break (a gap or a margin that disappears): the content that ends in them ends at an offset of the fragmentainer.</summary>
    private List<(double From, double To, int Index, double Offset)>? _truncations;

    /// <summary>The shifts of the content of a box at the start of a fragmentainer (see <see cref="AddStartShift"/>).</summary>
    private List<(int Index, double Shift, BoxFragment Box)>? _startShifts;

    public int Count => _pieces.Count;

    /// <summary>The block size that the flow uses in the fragmentainers where it is broken (see <see cref="GetBreakEnd"/>).</summary>
    private List<(int Index, double Offset)>? _breakEnds;

    /// <summary>
    /// Records the offset in a fragmentainer where the flow is broken: the block size of the fragmentainer, or the end of the
    /// unbreakable content of the flow that overflows it.
    /// </summary>
    public void SetBreakEnd(int index, double offset) => (_breakEnds ??= []).Add((index, offset));

    /// <summary>
    /// Returns the offset in a fragmentainer where the boxes of the flow that are broken at its end end (see
    /// <see cref="SetBreakEnd"/>), or null when the flow is not broken in it: a parallel flow is not broken where the
    /// unbreakable content of another flow that overflows the fragmentainer ends.
    /// </summary>
    public double? GetBreakEnd(int index)
    {
        if (_breakEnds is not null)
        {
            for (var i = _breakEnds.Count - 1; i >= 0; i--)
            {
                if (_breakEnds[i].Index == index)
                    return _breakEnds[i].Offset;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the sum of the parts of the unbreakable content of the flow that overflow the fragmentainers where it is broken
    /// (see <see cref="SetBreakEnd"/>), from a fragmentainer: the content after them starts the next fragmentainer, they take no
    /// block size there.
    /// </summary>
    public double GetUnbreakableOverflow(int from, List<double> blockSizes)
    {
        var result = 0d;
        if (_breakEnds is not null)
        {
            for (var i = 0; i < _breakEnds.Count; i++)
            {
                var (index, offset) = _breakEnds[i];
                if (index >= from && index < blockSizes.Count && IsLastBreakEnd(i))
                {
                    result += Math.Max(0, offset - blockSizes[index]);
                }
            }
        }

        return result;

        // (the last offset recorded in a fragmentainer is the one of its break, see GetBreakEnd)
        bool IsLastBreakEnd(int i)
        {
            for (var j = i + 1; j < _breakEnds.Count; j++)
            {
                if (_breakEnds[j].Index == _breakEnds[i].Index)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Records that the part of a box that continues in a fragmentainer starts below the floats that were pushed to it (see
    /// <c>FragmentationFlow.GetFloatAvoider</c>): the content of the flow is shifted from the start of the fragmentainer.
    /// </summary>
    public void AddStartShift(int index, double shift, BoxFragment box) => (_startShifts ??= []).Add((index, shift, box));

    /// <summary>Returns the shift of the part of a box that continues in a fragmentainer (see <see cref="AddStartShift"/>), or 0.</summary>
    public double GetStartShift(int index, BoxFragment box)
    {
        if (_startShifts is not null)
        {
            foreach (var (shiftIndex, shift, shiftedBox) in _startShifts)
            {
                if (shiftIndex == index && ReferenceEquals(shiftedBox, box))
                    return shift;
            }
        }

        return 0;
    }

    /// <summary>Returns a copy of the map.</summary>
    public FlowMap Clone()
    {
        var result = new FlowMap { _truncations = _truncations is null ? null : [.. _truncations], _startShifts = _startShifts is null ? null : [.. _startShifts], _breakEnds = _breakEnds is null ? null : [.. _breakEnds] };
        result._pieces.AddRange(_pieces);
        return result;
    }

    /// <summary>
    /// Records that the positions of a range are truncated at the break that ends a fragmentainer (the block-end margin of the last
    /// child of a box, https://drafts.csswg.org/css-break-3/#break-margins): a box that ends in the range ends at an offset of the fragmentainer.
    /// </summary>
    public void AddTruncation(double from, double to, int index, double offset) => (_truncations ??= []).Add((from, to, index, offset));

    public void Add(double from, int index, double origin, bool isBreak, int sequence = 0) => _pieces.Add(new Piece(from, index, origin, isBreak, sequence));

    /// <summary>
    /// Returns the fragmentainer and the offset of a box without block size: it is before the breaks at its position that
    /// follow it in the content (<paramref name="ordinal"/>, see <see cref="FragmentationFlow.GetEmptyOrdinal"/>).
    /// </summary>
    public (int Index, double Offset) MapEmpty(double position, int ordinal)
    {
        var index = Find(position);
        while (index > 0 && _pieces[index].IsBreak && _pieces[index].From >= position - Epsilon && ordinal < _pieces[index].Sequence)
        {
            index--;
        }

        var piece = _pieces[index];
        return (piece.Index, position - piece.Origin);
    }

    /// <summary>Returns the fragmentainer and the offset of a position where content starts.</summary>
    public (int Index, double Offset) Map(double position)
    {
        var piece = _pieces[Find(position)];
        return (piece.Index, position - piece.Origin);
    }

    /// <summary>
    /// Returns the fragmentainer and the offset of a position where content ends: the content that ends where a fragmentainer
    /// starts is not in it.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="start">The position where the content starts: the content is not before it.</param>
    /// <param name="growsWithTrack">Whether the content that ends where a track of parallel flows ends grows with the track.</param>
    public (int Index, double Offset) MapEnd(double position, double start, bool growsWithTrack = true)
    {
        var index = Find(position);
        var first = Find(start);
        while (index > first && _pieces[index].From >= position - (2 * Epsilon) && (_pieces[index].IsBreak || !growsWithTrack))
        {
            index--;
        }

        var piece = _pieces[index];
        var offset = position - piece.Origin;
        if (_truncations is not null)
        {
            foreach (var truncation in _truncations)
            {
                if (truncation.Index == piece.Index && position > truncation.From + Epsilon && position <= truncation.To + Epsilon)
                {
                    offset = Math.Min(offset, truncation.Offset);
                }
            }
        }

        return (piece.Index, offset);
    }

    /// <summary>Returns whether a position is in a range that is truncated at the break that ends a fragmentainer (see <see cref="AddTruncation"/>).</summary>
    public bool IsTruncated(int index, double position)
    {
        if (_truncations is not null)
        {
            foreach (var truncation in _truncations)
            {
                if (truncation.Index == index && position > truncation.From + Epsilon && position <= truncation.To + Epsilon)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Returns the offset of a position in a fragmentainer (which may not be the one of the position).</summary>
    public double GetOffsetIn(int index, double position)
    {
        var found = Find(position);
        if (_pieces[found].Index > index)
        {
            // The last piece of the fragmentainer
            while (found > 0 && _pieces[found].Index > index)
            {
                found--;
            }
        }
        else
        {
            // The first piece of the fragmentainer
            while (found + 1 < _pieces.Count && _pieces[found].Index < index)
            {
                found++;
            }
        }

        return position - _pieces[found].Origin;
    }

    /// <summary>Returns the range of positions of the strip that are in a fragmentainer (the first one starts at -∞, the last one ends at +∞), or null when the flow has no content in it.</summary>
    public (double Start, double End)? GetRange(int index)
    {
        double? start = null;
        for (var i = 0; i < _pieces.Count; i++)
        {
            if (_pieces[i].Index == index)
            {
                start ??= _pieces[i].From;
            }
            else if (_pieces[i].Index > index)
            {
                return start is { } value ? (value, _pieces[i].From) : null;
            }
        }

        return start is { } last ? (last, double.PositiveInfinity) : null;
    }

    /// <summary>Returns whether a fragmentainer is the first one of the flow.</summary>
    public bool IsFirst(int index) => _pieces.Count > 0 && _pieces[0].Index == index;

    /// <summary>
    /// Returns a map where each fragmentainer contains the positions of the strip up to its block-end edge, when the flow breaks
    /// before it (the fragments of the boxes that are broken extend to the end of the fragmentainer).
    /// </summary>
    public FlowMap Extend(List<Fragmentainer> fragmentainers)
    {
        var result = new FlowMap { _truncations = _truncations, _startShifts = _startShifts, _breakEnds = _breakEnds };
        result._pieces.AddRange(_pieces);
        var next = double.PositiveInfinity;
        for (var i = _pieces.Count - 1; i > 0; i--)
        {
            var piece = _pieces[i];
            var previous = _pieces[i - 1];
            if (piece.IsBreak && previous.Index < piece.Index && previous.Index < fragmentainers.Count)
            {
                var from = Math.Min(Math.Max(piece.From, previous.Origin + fragmentainers[previous.Index].BlockSize), next);
                result._pieces[i] = piece with { From = from };
            }

            next = result._pieces[i].From;
        }

        return result;
    }

    /// <summary>Returns the last piece that starts at or before a position.</summary>
    private int Find(double position)
    {
        var low = 0;
        var high = _pieces.Count - 1;
        var result = 0;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (_pieces[middle].From <= position + Epsilon)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }
}
