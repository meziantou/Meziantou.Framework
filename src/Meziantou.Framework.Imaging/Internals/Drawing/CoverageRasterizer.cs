using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes, row by row, the area of each pixel that is inside a set of closed contours.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The coverage of the pixel <c>(X, Y)</c> is the area of the part of the square
/// <c>[X, X + 1] x [Y, Y + 1]</c> whose winding number is not zero (<see cref="FillRule.NonZero"/>) or is odd
/// (<see cref="FillRule.EvenOdd"/>), as a multiple of 1/65536 rounded to nearest with ties upward
/// (<see cref="FullCoverage"/> is 1). It is the area of the union of overlapping contours, not the sum of their areas:
/// contours that overlap or intersect themselves inside a pixel are measured exactly.
/// </para>
/// <para>
/// <b>Method.</b> A horizontal line sweeps each row from top to bottom, carrying the edges it crosses in left-to-right
/// order (the sweep line). Walking that order accumulates the winding number; an edge at which "inside" changes is a
/// boundary of the region, and the area of a pixel is the sum, over the boundaries, of plus or minus the area of the
/// pixel to the right of the boundary. For a piece of boundary of height <c>h</c> inside the column <c>X</c>, at mean
/// offset <c>m</c> from the left side of the column, that area is <c>h (1 - m)</c> in the column and <c>h</c> in every
/// column further right: it is recorded as two entries of a difference row (<c>h (1 - m)</c> at <c>X</c>, <c>h m</c> at
/// <c>X + 1</c>) whose running sum is the area.
/// </para>
/// <para>
/// The sweep line only changes at events: an edge starts, an edge ends, or two neighbors cross. Each edge remembers
/// whether it is a boundary and since which height; that stretch of boundary is recorded when the answer changes, when
/// the edge ends and at the bottom of each row. An event therefore costs a search in the sweep line and the update of the
/// few edges around it, whatever the number of edges on the row: because the contours are closed, the winding numbers
/// to the right of everything that changes at one height stay what they were.
/// </para>
/// <para>
/// <b>Precision.</b> Everything is computed in double precision with additions, multiplications and divisions only. The
/// error on an area is a few units of the last place of the coordinates involved, far below the rounding step for the
/// coordinates the API accepts; <see cref="QuantizationBias"/> makes exact ties round upward in spite of it. Crossing
/// heights only need to be approximately right: a pair of edges swapped slightly too early or too late encloses a
/// negligible area, and a pair found in the wrong order is swapped at once. The order in which the events of a row are
/// processed is fully determined (ties included), so the sums do not depend on the platform.
/// </para>
/// </remarks>
internal sealed class CoverageRasterizer : IDisposable
{
    /// <summary>The coverage of a pixel that is entirely inside.</summary>
    public const uint FullCoverage = 65536;

    /// <summary>
    /// Added before truncating a coverage, in units of 1/65536: an area that is exactly half way between two steps rounds
    /// upward even when its computed value lands just below.
    /// </summary>
    private const double QuantizationBias = 1.0 / 256;

    /// <summary>The number of events of the sweep processed between two cancellation checks.</summary>
    private const int EventsPerCancellationCheck = 4096;

    /// <summary>The winding number of an edge that just entered the sweep line and was not walked yet.</summary>
    private const int UnknownWinding = int.MinValue;

    /// <summary>The number of columns of a row from which the entries of the difference row that are zero are skipped.</summary>
    private const int MinZeroSkipWidth = 32;

    // The scratch of the sweep, for each edge a row can hold at once
    private const int SweepDoublesPerEdge = 4;
    private const int SweepIntegersPerEdge = 9;
    private const int SweepBytesPerEdge = (SweepDoublesPerEdge * sizeof(double)) + (SweepIntegersPerEdge * sizeof(int));

    private readonly AllocationScope _scope;
    private readonly int _width;
    private readonly int _height;
    private readonly PooledBuffer _accumulation;
    private readonly PooledBuffer _coverage;
    private readonly PooledBuffer _rows;
    private PooledBuffer? _order;
    private int _orderCapacity;
    private PooledBuffer? _sweep;
    private int _sweepCapacity;

    private EdgeBuilder? _edges;
    private bool _evenOdd;
    private int _edgeCount;
    private int _firstRow;
    private int _lastRow;
    private int _row;
    private int _nextEdge;
    private int _lineCount;
    private int _heapCount;
    private int _freeCount;
    private int _changedFirst;
    private int _changedLast;
    private int _dirtyMin;
    private int _dirtyMax;
    private int _events;
    private CancellationToken _cancellationToken;

    /// <summary>Rents the scratch that only depends on the size of the frame.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public CoverageRasterizer(AllocationScope scope, int width, int height)
    {
        _scope = scope;
        _width = width;
        _height = height;
        PooledBuffer? accumulation = null;
        PooledBuffer? coverage = null;
        try
        {
            accumulation = ResampleWeights.Rent<double>(scope, (long)width + 2);
            coverage = ResampleWeights.Rent<uint>(scope, width);
            _rows = ResampleWeights.Rent<int>(scope, 2 * ((long)height + 1));
        }
        catch
        {
            accumulation?.Dispose();
            coverage?.Dispose();
            throw;
        }

        _accumulation = accumulation;
        _coverage = coverage;
        _firstRow = height;
        ResetDirtyRange();
    }

    /// <summary>Gets the row of the current run.</summary>
    public int Y { get; private set; }

    /// <summary>Gets the first column of the current run.</summary>
    public int X { get; private set; }

    /// <summary>Gets the number of pixels of the current run. The first and the last ones are covered.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the coverage of the pixels of the current run, from 0 to <see cref="FullCoverage"/>.</summary>
    public ReadOnlySpan<uint> Coverage => CoverageRow.Slice(X, Count);

    private Span<double> Accumulation => unsafe(MemoryMarshal.Cast<byte, double>(_accumulation.Span));

    private Span<uint> CoverageRow => unsafe(MemoryMarshal.Cast<byte, uint>(_coverage.Span));

    /// <summary>The number of edges whose first row is each row, then the edges that become or stop being active at each row.</summary>
    private Span<int> Rows => unsafe(MemoryMarshal.Cast<byte, int>(_rows.Span));

    /// <summary>The edges sorted by first row.</summary>
    private Span<int> Order => unsafe(MemoryMarshal.Cast<byte, int>(_order!.Span));

    private Span<double> SweepDoubles => unsafe(MemoryMarshal.Cast<byte, double>(_sweep!.Span[..(SweepDoublesPerEdge * sizeof(double) * _sweepCapacity)]));

    private Span<int> SweepIntegers => unsafe(MemoryMarshal.Cast<byte, int>(_sweep!.Span[(SweepDoublesPerEdge * sizeof(double) * _sweepCapacity)..]));

    // Each edge of the sweep line has a slot, which it keeps until it ends. By slot:

    /// <summary>The height since which the edge is the boundary it currently is.</summary>
    private Span<double> SinceY => SweepDoubles[.._sweepCapacity];

    /// <summary>The height at which the edge crosses its right neighbor, or infinity.</summary>
    private Span<double> CrossingY => SweepDoubles.Slice(_sweepCapacity, _sweepCapacity);

    /// <summary>The edge of the slot.</summary>
    private Span<int> SlotEdge => SweepIntegers[.._sweepCapacity];

    /// <summary>What the edge is: +1 when the inside starts at it, -1 when the inside ends at it, 0 when it is not a boundary.</summary>
    private Span<int> SlotSign => SweepIntegers.Slice(_sweepCapacity, _sweepCapacity);

    /// <summary>The winding number just left of the edge.</summary>
    private Span<int> WindingBefore => SweepIntegers.Slice(2 * _sweepCapacity, _sweepCapacity);

    /// <summary>The position of the slot in <see cref="Heap"/>, or -1.</summary>
    private Span<int> HeapPosition => SweepIntegers.Slice(3 * _sweepCapacity, _sweepCapacity);

    // And the structures over the slots:

    /// <summary>The slots that cross their right neighbor, the first crossing at the root (a binary heap).</summary>
    private Span<int> Heap => SweepIntegers.Slice(4 * _sweepCapacity, _sweepCapacity);

    /// <summary>The sweep line: the slots from left to right at the current height.</summary>
    private Span<int> Line => SweepIntegers.Slice(5 * _sweepCapacity, _sweepCapacity);

    /// <summary>The slots that are not used.</summary>
    private Span<int> FreeSlots => SweepIntegers.Slice(6 * _sweepCapacity, _sweepCapacity);

    /// <summary>The slots that start in the current row, by height (the heights are in <see cref="StartKeys"/>).</summary>
    private Span<int> StartSlots => SweepIntegers.Slice(7 * _sweepCapacity, _sweepCapacity);

    /// <summary>The slots that end in the current row, by height (the heights are in <see cref="EndKeys"/>).</summary>
    private Span<int> EndSlots => SweepIntegers.Slice(8 * _sweepCapacity, _sweepCapacity);

    private Span<double> StartKeys => SweepDoubles.Slice(2 * _sweepCapacity, _sweepCapacity);

    private Span<double> EndKeys => SweepDoubles.Slice(3 * _sweepCapacity, _sweepCapacity);

    /// <summary>
    /// Prepares the rasterization of a set of edges: sorts them by first row and rents the scratch of the sweep, sized for
    /// the largest number of edges on one row. Nothing else is rented until the next call.
    /// </summary>
    /// <returns><see langword="false"/> if there is nothing to rasterize.</returns>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public bool Begin(EdgeBuilder edges, FillRule fillRule, CancellationToken cancellationToken)
    {
        var rowCounts = Rows[..(_height + 1)];
        var deltas = Rows[(_height + 1)..];
        if (_firstRow < _lastRow)
        {
            // What the previous rasterization left
            rowCounts[_firstRow.._lastRow].Clear();
            deltas[_firstRow..(_lastRow + 1)].Clear();
        }

        if (_dirtyMin <= _dirtyMax)
        {
            // A rasterization that was canceled in the middle of a row
            Accumulation.Clear();
            ResetDirtyRange();
        }

        _edges = edges;
        _evenOdd = fillRule == FillRule.EvenOdd;
        _cancellationToken = cancellationToken;
        _edgeCount = edges.Count;
        _firstRow = _height;
        _lastRow = 0;
        _lineCount = 0;
        _heapCount = 0;
        _nextEdge = 0;
        _events = 0;
        Count = 0;
        var list = edges.Edges;
        foreach (ref readonly var edge in list)
        {
            var first = (int)edge.Y0;
            var end = (int)Math.Ceiling(edge.Y1);
            rowCounts[first]++;
            deltas[first]++;
            deltas[end]--;
            _firstRow = Math.Min(_firstRow, first);
            _lastRow = Math.Max(_lastRow, end);
        }

        _row = _firstRow;
        if (_edgeCount == 0)
            return false;

        var active = 0;
        var maxActive = 0;
        var offset = 0;
        for (var row = _firstRow; row < _lastRow; row++)
        {
            active += deltas[row];
            maxActive = Math.Max(maxActive, active);
            var count = rowCounts[row];
            rowCounts[row] = offset;
            offset += count;
        }

        EnsureCapacity(ref _order, ref _orderCapacity, _edgeCount, sizeof(int));
        EnsureCapacity(ref _sweep, ref _sweepCapacity, maxActive, SweepBytesPerEdge);
        var order = Order;
        for (var i = 0; i < list.Length; i++)
        {
            order[rowCounts[(int)list[i].Y0]++] = i;
        }

        // Every slot is free; the first ones are taken first
        var free = FreeSlots;
        _freeCount = _sweepCapacity;
        for (var i = 0; i < _freeCount; i++)
        {
            free[i] = _freeCount - 1 - i;
        }

        return true;
    }

    /// <summary>Moves to the next run of covered pixels.</summary>
    /// <returns><see langword="false"/> when every row is done.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public bool MoveNext()
    {
        while (_row < _lastRow)
        {
            var row = _row++;
            if (row % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            if (ProcessRow(row))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _accumulation.Dispose();
        _coverage.Dispose();
        _rows.Dispose();
        _order?.Dispose();
        _sweep?.Dispose();
    }

    private void EnsureCapacity(ref PooledBuffer? buffer, ref int capacity, int required, int elementSize)
    {
        if (required <= capacity)
            return;

        // The previous buffer is released first: its content is not needed
        buffer?.Dispose();
        buffer = null;
        capacity = 0;
        var grown = Math.Max(required, 16);
        buffer = ResampleWeights.Rent<byte>(_scope, (long)grown * elementSize);
        capacity = grown;
    }

    /// <summary>Sweeps a row and turns what it accumulated into coverages.</summary>
    private bool ProcessRow(int row)
    {
        var edges = _edges!.Edges;
        double top = row;
        var bottom = top + 1;

        // The events of the row: the edges of the sweep line that end in it, and the edges that start in it
        var line = Line;
        var slotEdge = SlotEdge;
        var startKeys = StartKeys;
        var startSlots = StartSlots;
        var endKeys = EndKeys;
        var endSlots = EndSlots;
        var startCount = 0;
        var endCount = 0;
        for (var i = 0; i < _lineCount; i++)
        {
            var slot = line[i];
            var end = edges[slotEdge[slot]].Y1;
            if (end <= bottom)
            {
                endKeys[endCount] = end;
                endSlots[endCount++] = slot;
            }
        }

        var order = Order;
        while (_nextEdge < _edgeCount && (int)edges[order[_nextEdge]].Y0 == row)
        {
            var index = order[_nextEdge++];
            ref readonly var edge = ref edges[index];
            var slot = FreeSlots[--_freeCount];
            slotEdge[slot] = index;
            SlotSign[slot] = 0;
            WindingBefore[slot] = UnknownWinding;
            SinceY[slot] = edge.Y0;
            CrossingY[slot] = double.PositiveInfinity;
            HeapPosition[slot] = -1;
            startKeys[startCount] = edge.Y0;
            startSlots[startCount++] = slot;
            if (edge.Y1 <= bottom)
            {
                endKeys[endCount] = edge.Y1;
                endSlots[endCount++] = slot;
            }
        }

        if (_lineCount == 0 && startCount == 0)
            return false;

        SortEvents(startKeys[..startCount], startSlots[..startCount]);
        SortEvents(endKeys[..endCount], endSlots[..endCount]);
        var startIndex = 0;
        var endIndex = 0;
        var y = top;
        while (true)
        {
            // Everything that happens at this height: the crossings first (the sweep line is then in order), the edges
            // that end, the edges that start; then the edges around what changed are walked again, which may find
            // neighbors that are already in the wrong order
            while (true)
            {
                while (_heapCount > 0 && CrossingY[Heap[0]] <= y)
                {
                    Swap(Heap[0], y);
                }

                _changedFirst = int.MaxValue;
                _changedLast = -1;
                while (endIndex < endCount && endKeys[endIndex] <= y)
                {
                    Remove(endSlots[endIndex++]);
                }

                while (startIndex < startCount && startKeys[startIndex] <= y)
                {
                    Insert(startSlots[startIndex++], y);
                }

                if (_changedFirst == int.MaxValue)
                    break;

                Walk(y);
            }

            if (y >= bottom)
                break;

            var next = bottom;
            if (startIndex < startCount && startKeys[startIndex] < next)
            {
                next = startKeys[startIndex];
            }

            if (endIndex < endCount && endKeys[endIndex] < next)
            {
                next = endKeys[endIndex];
            }

            if (_heapCount > 0 && CrossingY[Heap[0]] < next)
            {
                next = CrossingY[Heap[0]];
            }

            y = next;
        }

        // The boundaries that go on below the row are recorded up to its bottom
        for (var i = 0; i < _lineCount; i++)
        {
            RecordBoundary(line[i], bottom);
        }

        return Flush(row);
    }

    /// <summary>
    /// Sorts the events of a row by height, then by slot: a total order, so that the result does not depend on the sorting
    /// algorithm. A heap sort: in place, without allocation, and never quadratic.
    /// </summary>
    private static void SortEvents(Span<double> keys, Span<int> slots)
    {
        var count = keys.Length;
        for (var root = (count / 2) - 1; root >= 0; root--)
        {
            SiftEventDown(keys, slots, root, count);
        }

        for (var end = count - 1; end > 0; end--)
        {
            (keys[0], keys[end]) = (keys[end], keys[0]);
            (slots[0], slots[end]) = (slots[end], slots[0]);
            SiftEventDown(keys, slots, 0, end);
        }
    }

    private static void SiftEventDown(Span<double> keys, Span<int> slots, int root, int count)
    {
        var key = keys[root];
        var slot = slots[root];
        while (true)
        {
            var child = (2 * root) + 1;
            if (child >= count)
                break;

            if (child + 1 < count && IsEventBefore(keys[child], slots[child], keys[child + 1], slots[child + 1]))
            {
                child++;
            }

            if (!IsEventBefore(key, slot, keys[child], slots[child]))
                break;

            keys[root] = keys[child];
            slots[root] = slots[child];
            root = child;
        }

        keys[root] = key;
        slots[root] = slot;
    }

    private static bool IsEventBefore(double key, int slot, double otherKey, int otherSlot)
        => key != otherKey ? key < otherKey : slot < otherSlot;

    /// <summary>Puts an edge that starts at the current height in the sweep line.</summary>
    private void Insert(int slot, double y)
    {
        var edges = _edges!.Edges;
        var line = Line;
        var slotEdge = SlotEdge;
        var index = slotEdge[slot];
        ref readonly var edge = ref edges[index];

        // After every edge that is on its left at this height, or at the same place and going more to the left
        var low = 0;
        var high = _lineCount;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            var otherIndex = slotEdge[line[middle]];
            ref readonly var other = ref edges[otherIndex];
            if (IsBefore(GetX(other, y), other.Slope, otherIndex, edge.X0, edge.Slope, index))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        line.Slice(low, _lineCount - low).CopyTo(line[(low + 1)..]);
        line[low] = slot;
        _lineCount++;
        if (_changedLast >= low)
        {
            _changedLast++;
        }

        _changedFirst = Math.Min(_changedFirst, low);
        _changedLast = Math.Max(_changedLast, low);
        CountEvent();
    }

    /// <summary>Takes an edge that ends at the current height out of the sweep line.</summary>
    private void Remove(int slot)
    {
        var end = _edges!.Edges[SlotEdge[slot]].Y1;
        RecordBoundary(slot, end);
        var position = FindPosition(slot, end);
        var line = Line;
        line.Slice(position + 1, _lineCount - position - 1).CopyTo(line[position..]);
        _lineCount--;
        if (HeapPosition[slot] >= 0)
        {
            RemoveFromHeap(slot);
        }

        FreeSlots[_freeCount++] = slot;
        if (_changedLast > position)
        {
            _changedLast--;
        }

        // The edge that takes its place has a new left neighbor
        _changedFirst = Math.Min(_changedFirst, position);
        _changedLast = Math.Max(_changedLast, position);
        CountEvent();
    }

    /// <summary>Exchanges an edge and its right neighbor, which cross at the current height.</summary>
    private void Swap(int slot, double y)
    {
        var line = Line;
        var position = FindPosition(slot, y);
        var other = line[position + 1];
        line[position] = other;
        line[position + 1] = slot;

        // The winding number left of the pair does not change; what each of them separates does
        var winding = WindingBefore[slot];
        winding = Update(other, winding, y);
        Update(slot, winding, y);
        for (var i = Math.Max(position - 1, 0); i <= position + 1; i++)
        {
            ScheduleCrossing(i, y);
        }

        CountEvent();
    }

    /// <summary>
    /// Walks the edges around what changed in the sweep line at the current height, from left to right: each edge gets
    /// its winding number, becomes or stops being a boundary, and learns when it crosses its new neighbor.
    /// </summary>
    private void Walk(double y)
    {
        var line = Line;
        var first = _changedFirst;
        var last = Math.Min(_changedLast, _lineCount - 1);
        var winding = 0;
        if (first > 0)
        {
            var before = line[first - 1];
            winding = WindingBefore[before] + _edges!.Edges[SlotEdge[before]].Direction;
        }

        // The contours are closed: right of the last change, the winding numbers are what they were, and the walk stops
        // at the first edge that confirms it
        var position = first;
        for (; position < _lineCount; position++)
        {
            var slot = line[position];
            if (position > last && WindingBefore[slot] == winding)
                break;

            winding = Update(slot, winding, y);
        }

        for (var i = Math.Max(first - 1, 0); i < position && i < _lineCount; i++)
        {
            ScheduleCrossing(i, y);
        }
    }

    /// <summary>Sets the winding number left of an edge; when the edge becomes another kind of boundary, records the stretch it was until now.</summary>
    /// <returns>The winding number right of the edge.</returns>
    private int Update(int slot, int winding, double y)
    {
        var after = winding + _edges!.Edges[SlotEdge[slot]].Direction;
        var sign = (IsInside(after) ? 1 : 0) - (IsInside(winding) ? 1 : 0);
        if (sign != SlotSign[slot])
        {
            RecordBoundary(slot, y);
            SlotSign[slot] = sign;
        }

        WindingBefore[slot] = winding;
        return after;
    }

    /// <summary>Accumulates the stretch of boundary an edge has been since it last changed, up to a height.</summary>
    private void RecordBoundary(int slot, double y)
    {
        var sinceY = SinceY;
        var since = sinceY[slot];
        var sign = SlotSign[slot];
        if (sign != 0 && y > since)
        {
            ref readonly var edge = ref _edges!.Edges[SlotEdge[slot]];
            var height = y - since;
            AddBoundary(GetX(edge, since), GetX(edge, y), sign > 0 ? height : -height);
        }

        sinceY[slot] = y;
    }

    /// <summary>Finds when the edge at a position of the sweep line crosses its right neighbor, if it does before one of them ends.</summary>
    private void ScheduleCrossing(int position, double y)
    {
        var line = Line;
        var slot = line[position];
        var crossing = double.PositiveInfinity;
        if (position + 1 < _lineCount)
        {
            var edges = _edges!.Edges;
            ref readonly var left = ref edges[SlotEdge[slot]];
            ref readonly var right = ref edges[SlotEdge[line[position + 1]]];
            var end = Math.Min(left.Y1, right.Y1);

            // In the other order where the first of them ends: they cross on the way
            var overlap = GetX(left, end) - GetX(right, end);
            if (overlap > 0)
            {
                // The gap is not negative unless the pair is already in the wrong order, which is fixed at once
                var gap = GetX(right, y) - GetX(left, y);
                crossing = gap > 0 ? Math.Min(y + ((end - y) * (gap / (gap + overlap))), end) : y;
            }
        }

        if (crossing == CrossingY[slot])
            return;

        var heapPosition = HeapPosition[slot];
        CrossingY[slot] = crossing;
        if (double.IsPositiveInfinity(crossing))
        {
            if (heapPosition >= 0)
            {
                RemoveFromHeap(slot);
            }
        }
        else if (heapPosition >= 0)
        {
            SiftDown(SiftUp(heapPosition));
        }
        else
        {
            Heap[_heapCount] = slot;
            HeapPosition[slot] = _heapCount;
            SiftUp(_heapCount++);
        }
    }

    private void RemoveFromHeap(int slot)
    {
        var heap = Heap;
        var position = HeapPosition[slot];
        HeapPosition[slot] = -1;
        CrossingY[slot] = double.PositiveInfinity;
        _heapCount--;
        if (position == _heapCount)
            return;

        var moved = heap[_heapCount];
        heap[position] = moved;
        HeapPosition[moved] = position;
        SiftDown(SiftUp(position));
    }

    private int SiftUp(int position)
    {
        var heap = Heap;
        var slot = heap[position];
        while (position > 0)
        {
            var parent = (position - 1) >> 1;
            if (!IsEarlier(slot, heap[parent]))
                break;

            heap[position] = heap[parent];
            HeapPosition[heap[position]] = position;
            position = parent;
        }

        heap[position] = slot;
        HeapPosition[slot] = position;
        return position;
    }

    private void SiftDown(int position)
    {
        var heap = Heap;
        var slot = heap[position];
        while (true)
        {
            var child = (2 * position) + 1;
            if (child >= _heapCount)
                break;

            if (child + 1 < _heapCount && IsEarlier(heap[child + 1], heap[child]))
            {
                child++;
            }

            if (!IsEarlier(heap[child], slot))
                break;

            heap[position] = heap[child];
            HeapPosition[heap[position]] = position;
            position = child;
        }

        heap[position] = slot;
        HeapPosition[slot] = position;
    }

    /// <summary>The total order of the crossings: height, then slot.</summary>
    private bool IsEarlier(int slot, int other)
    {
        var crossing = CrossingY;
        return crossing[slot] != crossing[other] ? crossing[slot] < crossing[other] : slot < other;
    }

    /// <summary>Finds a slot in the sweep line, which is in left-to-right order at a height (up to rounding).</summary>
    private int FindPosition(int slot, double y)
    {
        var edges = _edges!.Edges;
        var line = Line;
        var slotEdge = SlotEdge;
        var x = GetX(edges[slotEdge[slot]], y);
        var low = 0;
        var high = _lineCount;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (GetX(edges[slotEdge[line[middle]]], y) < x)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        // The slot is there or next to it, among the edges at the same place; further away only if rounding disturbed
        // the order
        for (var distance = 0; ; distance++)
        {
            var right = low + distance;
            var left = low - 1 - distance;
            if (right < _lineCount && line[right] == slot)
                return right;

            if (left >= 0 && line[left] == slot)
                return left;

            if (right >= _lineCount && left < 0)
                throw new InvalidOperationException("The edge is not in the sweep line.");
        }
    }

    private void CountEvent()
    {
        if (++_events % EventsPerCancellationCheck == 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Records a piece of boundary going from <paramref name="x0"/> to <paramref name="x1"/> over a height: every point to
    /// its right gains (or loses, for a negative height) that height of area.
    /// </summary>
    private void AddBoundary(double x0, double x1, double signedHeight)
    {
        if (x0 > x1)
        {
            (x0, x1) = (x1, x0);
        }

        var accumulation = Accumulation;
        if (x1 <= 0)
        {
            // Entirely to the left of the frame: the whole row is on its right
            accumulation[0] += signedHeight;
            Touch(0, 0);
            return;
        }

        if (x0 >= _width)
            return;

        var span = x1 - x0;
        if (!(span > 0))
        {
            var column = (int)x0;
            var offset = x0 - column;
            accumulation[column] += signedHeight * (1 - offset);
            accumulation[column + 1] += signedHeight * offset;
            Touch(column, column + 1);
            return;
        }

        // The height of boundary per unit of width
        var slope = signedHeight / span;
        var remaining = signedHeight;
        if (x0 < 0)
        {
            var outside = -x0 * slope;
            accumulation[0] += outside;
            Touch(0, 0);
            remaining -= outside;
            x0 = 0;
        }

        var clipped = x1 > _width;
        if (clipped)
        {
            x1 = _width;
        }

        var firstColumn = (int)x0;
        var lastColumn = (int)Math.Ceiling(x1) - 1;
        var left = x0;
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var right = Math.Min(column + 1, x1);

            // The last piece takes what is left, so that the pieces add up to the height exactly
            var height = column == lastColumn && !clipped ? remaining : (right - left) * slope;
            var offset = ((left + right) / 2) - column;
            accumulation[column] += height * (1 - offset);
            accumulation[column + 1] += height * offset;
            remaining -= height;
            left = right;
        }

        Touch(firstColumn, lastColumn + 1);
    }

    /// <summary>Turns the difference row into the coverage of the row, and clears it.</summary>
    private bool Flush(int row)
    {
        if (_dirtyMin > _dirtyMax)
            return false;

        var accumulation = Accumulation;
        var coverage = CoverageRow;
        var last = Math.Min(_dirtyMax, _width - 1);
        var firstCovered = -1;
        var lastCovered = -1;
        double area = 0;

        if (last - _dirtyMin < MinZeroSkipWidth)
        {
            for (var column = _dirtyMin; column <= last; column++)
            {
                area += accumulation[column];
                accumulation[column] = 0;
                var value = Quantize(area);
                coverage[column] = value;
                if (value != 0)
                {
                    if (firstCovered < 0)
                    {
                        firstCovered = column;
                    }

                    lastCovered = column;
                }
            }
        }
        else
        {
            area = FlushWide(accumulation, coverage, _dirtyMin, last, ref firstCovered, ref lastCovered);
        }

        // The entry past the last column only receives what leaves the frame
        accumulation[_width] = 0;
        ResetDirtyRange();

        // An edge on the right of the frame was dropped: what is inside after the last boundary stays inside up to the
        // right side of the frame
        var tail = Quantize(area);
        if (tail != 0 && last < _width - 1)
        {
            coverage[(last + 1)..].Fill(tail);
            if (firstCovered < 0)
            {
                firstCovered = last + 1;
            }

            lastCovered = _width - 1;
        }

        if (firstCovered < 0)
            return false;

        Y = row;
        X = firstCovered;
        Count = lastCovered - firstCovered + 1;
        return true;
    }

    /// <summary>
    /// Turns the difference row of a long row into coverages, skipping the entries that are zero: an entry of +0
    /// leaves the area as it is (only a sum of zeros changes its sign, which quantizes the same), so the columns up to
    /// the next other entry have the coverage of the column before them.
    /// </summary>
    /// <returns>The area after the last column.</returns>
    private static double FlushWide(Span<double> accumulation, Span<uint> coverage, int first, int last, ref int firstCovered, ref int lastCovered)
    {
        var bits = unsafe(MemoryMarshal.Cast<double, ulong>(accumulation));
        double area = 0;
        var column = first;
        while (column <= last)
        {
            area += accumulation[column];
            accumulation[column] = 0;
            var value = Quantize(area);
            coverage[column] = value;
            var end = column + 1;
            if (end <= last && bits[end] == 0)
            {
                var next = bits[end..(last + 1)].IndexOfAnyExcept(0UL);
                end = next < 0 ? last + 1 : end + next;
                coverage[(column + 1)..end].Fill(value);
            }

            if (value != 0)
            {
                if (firstCovered < 0)
                {
                    firstCovered = column;
                }

                lastCovered = end - 1;
            }

            column = end;
        }

        return area;
    }

    private void Touch(int first, int last)
    {
        if (first < _dirtyMin)
        {
            _dirtyMin = first;
        }

        if (last > _dirtyMax)
        {
            _dirtyMax = last;
        }
    }

    private void ResetDirtyRange()
    {
        _dirtyMin = int.MaxValue;
        _dirtyMax = -1;
    }

    private bool IsInside(int winding) => _evenOdd ? (winding & 1) != 0 : winding != 0;

    private static uint Quantize(double area)
    {
        var value = (area * FullCoverage) + 0.5 + QuantizationBias;
        if (!(value >= 1))
            return 0;

        return value >= FullCoverage ? FullCoverage : (uint)value;
    }

    /// <summary>Gets the position of an edge at a height of its range; the ends are returned exactly.</summary>
    private static double GetX(in DrawingEdge edge, double y)
    {
        if (y == edge.Y0)
            return edge.X0;

        if (y == edge.Y1)
            return edge.X1;

        return edge.X0 + ((y - edge.Y0) * edge.Slope);
    }

    /// <summary>The total order of the edges at a height: position, then direction of travel (the one going more to the left first), then index.</summary>
    private static bool IsBefore(double x, double slope, int index, double otherX, double otherSlope, int otherIndex)
    {
        if (x != otherX)
            return x < otherX;

        if (slope != otherSlope)
            return slope < otherSlope;

        return index < otherIndex;
    }
}
