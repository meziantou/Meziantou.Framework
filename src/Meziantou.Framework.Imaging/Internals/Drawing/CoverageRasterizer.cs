using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
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
/// <para>
/// <b>Without antialiasing</b> (<see cref="Begin(EdgeBuilder, FillRule, bool, CancellationToken)"/> with <c>isAliased</c>), a pixel is covered entirely when its center
/// is inside, and not at all otherwise, as browsers draw hard edges: on the row <c>Y</c>, the edges whose range of
/// heights contains <c>Y + 1/2</c> (the top excluded, the bottom included) are crossed at that height, and between two
/// crossings where the region starts at <c>xa</c> and ends at <c>xb</c>, the pixels from <c>floor(xa + 1/2)</c> to
/// <c>floor(xb + 1/2)</c> excluded are covered: a center on the left side of a shape is outside, one on its right side
/// inside, as with the rounding of positions half up. Crossings are insertion-sorted with at most one shift per active
/// edge in total, then heap-sorted if needed: ordered rows take linear time, and sorting never exceeds O(n log n) for
/// n active edges, without additional working storage.
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

    /// <summary>The number of positions on each side of <see cref="SweepSpans.PositionHint"/> where <see cref="FindPosition"/> looks for a slot before it searches the sweep line.</summary>
    private const int PositionHintWindow = 32;

    /// <summary>The number of events of a row up to which they are sorted by insertion.</summary>
    private const int MaxInsertionSortLength = 16;

    /// <summary>The number of packed events up to which <see cref="SortVector512"/> sorts them, in at most 128 vectors (8 KiB of stack).</summary>
    internal const int MaxVectorSortLength = 1024;

    /// <summary>The number of columns of a row from which the entries of the difference row that are zero are skipped.</summary>
    private const int MinZeroSkipWidth = 32;

    /// <summary>
    /// The number of pieces of boundary of a row that <see cref="RecordPieces"/> keeps: a row with more is accumulated by
    /// the sweep, as without recording (see <see cref="HasPieces"/>).
    /// </summary>
    internal const int MaxPiecesPerRow = 1024;

    /// <summary>The number of values of a piece of boundary: the two ends and the signed height.</summary>
    internal const int ValuesPerPiece = 3;

    /// <summary>The number of positions the changes of a height span from which they are recorded, and the walk jumps between them (see <see cref="Walk"/>).</summary>
    private const int MinJumpingWalkSpan = 32;

    // The kinds of the entries of ChangeEvents
    private const int Removal = 0;
    private const int Insertion = 1;
    private const int ChangedRange = 2;

    // The scratch of the sweep, for each edge a row can hold at once: a copy of the edge, then the doubles and integers
    private const int SweepDoublesPerEdge = 4;
    private const int SweepIntegersPerEdge = 14;
    private static readonly int SweepBytesPerEdge = Unsafe.SizeOf<DrawingEdge>() + (SweepDoublesPerEdge * sizeof(double)) + (SweepIntegersPerEdge * sizeof(int));

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
    private PooledBuffer? _pieces;

    // The arrays of the buffers, read without going through PooledBuffer.Span: the sweep reaches them for every event.
    // They are emptied on disposal, so that nothing reads a buffer that went back to the pool.
    private byte[] _accumulationBytes;
    private byte[] _coverageBytes;
    private byte[] _rowsBytes;
    private byte[] _orderBytes = [];
    private byte[] _sweepBytes = [];
    private byte[] _edgeBytes = [];
    private byte[] _pieceBytes = [];

    private bool _evenOdd;
    private bool _isAliased;

    // Whether the pieces of boundary of the rows are recorded (RecordPieces), and whether those of the current row still are
    private bool _recordsPieces;
    private bool _recordsRow;
    private int _pieceCount;
    private double _pieceLeft;
    private double _pieceRight;

    // The edges are a rectangle of whole pixels: every row of the sweep is the same run of full coverage
    private bool _isRectangle;
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

    // The number of entries of ChangeEvents for the current height: none until the changes span many positions
    private int _changeEventCount;

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
        _accumulationBytes = accumulation.RawBuffer;
        _coverageBytes = coverage.RawBuffer;
        _rowsBytes = _rows.RawBuffer;
        _firstRow = height;
        ResetDirtyRange();
    }

    /// <summary>Gets the row of the current run.</summary>
    public int Y { get; private set; }

    /// <summary>Gets the first column of the current run.</summary>
    public int X { get; private set; }

    /// <summary>Gets the number of pixels of the current run. The first and the last ones are covered.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets, after <see cref="Begin(EdgeBuilder, FillRule, bool, CancellationToken)"/>, a bound of the number of pixels the
    /// runs may cover: the rows of the edges, from the leftmost edge to the right side of the frame.
    /// </summary>
    public long EstimatedPixels { get; private set; }

    /// <summary>Gets the coverage of the pixels of the current run, from 0 to <see cref="FullCoverage"/>.</summary>
    public ReadOnlySpan<uint> Coverage => CoverageRow.Slice(X, Count);

    /// <summary>
    /// Gets a value indicating whether the current row is given by its pieces of boundary (<see cref="Pieces"/>) rather than
    /// by its coverages: only after <see cref="RecordPieces"/>. <see cref="X"/>, <see cref="Count"/> and
    /// <see cref="Coverage"/> are then not set.
    /// </summary>
    public bool HasPieces { get; private set; }

    /// <summary>
    /// Gets or sets the number of pieces of a row from which the sweep accumulates the row itself, at most
    /// <see cref="MaxPiecesPerRow"/>: a test hook that makes the rows with few pieces take that path.
    /// </summary>
    internal int PieceLimit
    {
        get;
        set => field = Math.Clamp(value, 0, MaxPiecesPerRow);
    } = MaxPiecesPerRow;

    /// <summary>Gets a value indicating whether <see cref="RecordPieces"/> applies to the shape: antialiased, and not a rectangle of whole pixels.</summary>
    public bool CanRecordPieces => !_isAliased && !_isRectangle;

    /// <summary>
    /// Gets the pieces of boundary of the current row when <see cref="HasPieces"/> is <see langword="true"/>: for each, the
    /// two ends and the signed height, in the order the sweep accumulates them. <see cref="ReplayPieces"/> turns them into
    /// the coverages the sweep would have computed.
    /// </summary>
    public ReadOnlySpan<double> Pieces => unsafe(MemoryMarshal.Cast<byte, double>(_pieceBytes.AsSpan(0, _pieceCount * ValuesPerPiece * sizeof(double))));

    /// <summary>Gets an estimate of the number of columns that the replay of <see cref="Pieces"/> goes through, for the scheduling of the work.</summary>
    public int PieceColumns => (int)Math.Clamp(Math.Ceiling(_pieceRight) - Math.Floor(_pieceLeft) + 1, 1, _width);

    /// <summary>Gets a value indicating whether every coverage of <see cref="Coverage"/> is <see cref="FullCoverage"/>, as in each row of a rectangle of whole pixels.</summary>
    public bool IsFullyCovered => _isRectangle;

    /// <summary>
    /// Gets or sets a value indicating whether the walk after the events of a height updates every edge it passes and
    /// computes again when every pair of neighbors it passes crosses, as it did before it skipped those whose answer
    /// cannot change (see <see cref="Walk"/>): the coverages are the same, bit for bit, which the tests check.
    /// </summary>
    internal bool WalksEveryEdge { get; set; }

    private Span<double> Accumulation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, double>(_accumulationBytes.AsSpan(0, _accumulation.Length)));
    }

    // The edges of the sweep, read without going through PooledBuffer.Span: the sweep reaches them for every event
    private Span<DrawingEdge> EdgeList
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, DrawingEdge>(_edgeBytes.AsSpan(0, _edgeCount * Unsafe.SizeOf<DrawingEdge>())));
    }

    private Span<uint> CoverageRow
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, uint>(_coverageBytes.AsSpan(0, _coverage.Length)));
    }

    /// <summary>The number of edges whose first row is each row, then the edges that become or stop being active at each row.</summary>
    private Span<int> Rows
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, int>(_rowsBytes.AsSpan(0, _rows.Length)));
    }

    /// <summary>The edges sorted by first row.</summary>
    private Span<int> Order
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, int>(_orderBytes.AsSpan(0, _orderCapacity * sizeof(int))));
    }

    private Span<double> SweepDoubles
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, double>(_sweepBytes.AsSpan(Unsafe.SizeOf<DrawingEdge>() * _sweepCapacity, SweepDoublesPerEdge * sizeof(double) * _sweepCapacity)));
    }

    private Span<int> SweepIntegers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, int>(_sweepBytes.AsSpan((Unsafe.SizeOf<DrawingEdge>() + (SweepDoublesPerEdge * sizeof(double))) * _sweepCapacity, SweepIntegersPerEdge * sizeof(int) * _sweepCapacity)));
    }

    // Each edge of the sweep line has a slot, which it keeps until it ends. By slot, then the structures over the slots,
    // each a slice of SweepDoubles or SweepIntegers of one integer or double per slot (two for ChangeEvents):
    private const int SinceYSlice = 0;
    private const int CrossingYSlice = 1;
    private const int StartKeysSlice = 2;
    private const int EndKeysSlice = 3;
    private const int SlotEdgeSlice = 0;
    private const int SlotSignSlice = 1;
    private const int WindingBeforeSlice = 2;
    private const int HeapPositionSlice = 3;
    private const int HeapSlice = 4;
    private const int LineSlice = 5;
    private const int FreeSlotsSlice = 6;
    private const int StartSlotsSlice = 7;
    private const int EndSlotsSlice = 8;
    private const int NeighborChangedSlice = 9;
    private const int ChangedPositionsSlice = 10;
    private const int ChangeEventsSlice = 11;
    private const int PositionHintSlice = 13;

    /// <summary>
    /// A copy of the edge of the slot: the sweep reads the edges of the sweep line at every event, and the copies of the
    /// few hundred edges a row holds stay in the cache, where the edges of the whole shape do not.
    /// </summary>
    private Span<DrawingEdge> SlotEdges
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unsafe(MemoryMarshal.Cast<byte, DrawingEdge>(_sweepBytes.AsSpan(0, Unsafe.SizeOf<DrawingEdge>() * _sweepCapacity)));
    }

    /// <summary>The height since which the edge is the boundary it currently is.</summary>
    private Span<double> SinceY => GetSlice(SweepDoubles, _sweepCapacity, SinceYSlice);

    /// <summary>The edge of the slot.</summary>
    private Span<int> SlotEdge => GetSlice(SweepIntegers, _sweepCapacity, SlotEdgeSlice);

    /// <summary>What the edge is: +1 when the inside starts at it, -1 when the inside ends at it, 0 when it is not a boundary.</summary>
    private Span<int> SlotSign => GetSlice(SweepIntegers, _sweepCapacity, SlotSignSlice);

    // And the structures over the slots:

    /// <summary>The slots that are not used.</summary>
    private Span<int> FreeSlots => GetSlice(SweepIntegers, _sweepCapacity, FreeSlotsSlice);

    /// <summary>
    /// A bit per position of the sweep line, set by <see cref="Walk"/> where an edge was inserted or removed at the current
    /// height and at the left neighbor of that position (which has a new right neighbor), and cleared once it is done. The
    /// integers of the slice hold more bits than the sweep line has positions, plus one.
    /// </summary>
    private Span<ulong> ChangedPositions => unsafe(MemoryMarshal.Cast<int, ulong>(GetSlice(SweepIntegers, _sweepCapacity, ChangedPositionsSlice)));

    /// <summary>
    /// What changed in the sweep line at the current height, in order, once the changes span many positions (see
    /// <see cref="RecordChange"/>): a range of positions marked as a whole (two entries, of kind <see cref="ChangedRange"/>),
    /// then each insertion (<see cref="Insertion"/>) and removal (<see cref="Removal"/>). An entry is the position times 4,
    /// plus its kind. An edge does not start and end at the same height: there are at most two entries more than slots.
    /// </summary>
    private Span<int> ChangeEvents => GetSlice(SweepIntegers, _sweepCapacity, ChangeEventsSlice, 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Span<T> GetSlice<T>(Span<T> storage, int capacity, int index, int count = 1) => storage.Slice(index * capacity, count * capacity);

    /// <summary>The storage of the sweep of a row, taken once for the row: the sweep reaches it at every event.</summary>
    private readonly ref struct SweepSpans
    {
        public readonly Span<DrawingEdge> Edges;
        public readonly Span<DrawingEdge> SlotEdges;
        public readonly Span<double> Accumulation;
        public readonly Span<double> SinceY;

        /// <summary>The height at which the edge crosses its right neighbor, or infinity.</summary>
        public readonly Span<double> CrossingY;

        public readonly Span<int> SlotEdge;
        public readonly Span<int> SlotSign;

        /// <summary>The winding number just left of the edge.</summary>
        public readonly Span<int> WindingBefore;

        /// <summary>The position of the slot in <see cref="Heap"/>, or -1.</summary>
        public readonly Span<int> HeapPosition;

        /// <summary>
        /// Whether the right neighbor of the edge changed since <see cref="CrossingY"/> was last computed (1) or not (0). The
        /// crossing of a pair of neighbors that do not cross stays infinite whatever the height it is computed at: only the
        /// pairs that are new, and those that cross, are computed again.
        /// </summary>
        public readonly Span<int> NeighborChanged;

        /// <summary>
        /// A position the edge of the slot had in the sweep line, recently: where <see cref="FindPosition"/> looks first. The
        /// edges inserted and removed on its left since then moved it by as many positions at most.
        /// </summary>
        public readonly Span<int> PositionHint;

        /// <summary>The slots that cross their right neighbor, the first crossing at the root (a binary heap).</summary>
        public readonly Span<int> Heap;

        /// <summary>The sweep line: the slots from left to right at the current height.</summary>
        public readonly Span<int> Line;

        public readonly Span<int> FreeSlots;

        /// <summary>The slots that start in the current row, by height (the heights are in <see cref="StartKeys"/>).</summary>
        public readonly Span<int> StartSlots;

        /// <summary>The slots that end in the current row, by height (the heights are in <see cref="EndKeys"/>).</summary>
        public readonly Span<int> EndSlots;

        public readonly Span<double> StartKeys;
        public readonly Span<double> EndKeys;

        public SweepSpans(CoverageRasterizer rasterizer)
        {
            // The slices of the scratch, from two spans of it
            var capacity = rasterizer._sweepCapacity;
            var doubles = rasterizer.SweepDoubles;
            var integers = rasterizer.SweepIntegers;
            Edges = rasterizer.EdgeList;
            SlotEdges = rasterizer.SlotEdges;
            Accumulation = rasterizer.Accumulation;
            SinceY = GetSlice(doubles, capacity, SinceYSlice);
            CrossingY = GetSlice(doubles, capacity, CrossingYSlice);
            StartKeys = GetSlice(doubles, capacity, StartKeysSlice);
            EndKeys = GetSlice(doubles, capacity, EndKeysSlice);
            SlotEdge = GetSlice(integers, capacity, SlotEdgeSlice);
            SlotSign = GetSlice(integers, capacity, SlotSignSlice);
            WindingBefore = GetSlice(integers, capacity, WindingBeforeSlice);
            HeapPosition = GetSlice(integers, capacity, HeapPositionSlice);
            Heap = GetSlice(integers, capacity, HeapSlice);
            Line = GetSlice(integers, capacity, LineSlice);
            FreeSlots = GetSlice(integers, capacity, FreeSlotsSlice);
            StartSlots = GetSlice(integers, capacity, StartSlotsSlice);
            EndSlots = GetSlice(integers, capacity, EndSlotsSlice);
            NeighborChanged = GetSlice(integers, capacity, NeighborChangedSlice);
            PositionHint = GetSlice(integers, capacity, PositionHintSlice);
        }
    }


    /// <summary>
    /// Prepares the rasterization of a set of edges: sorts them by first row and rents the scratch of the sweep, sized for
    /// the largest number of edges on one row. Nothing else is rented until the next call.
    /// </summary>
    /// <returns><see langword="false"/> if there is nothing to rasterize.</returns>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public bool Begin(EdgeBuilder edges, FillRule fillRule, CancellationToken cancellationToken) => Begin(edges, fillRule, isAliased: false, cancellationToken);

    /// <summary>
    /// Prepares the rasterization of a set of edges, with antialiasing or with the centers of the pixels only: sorts them
    /// by first row and rents the scratch of the sweep, sized for the largest number of edges on one row.
    /// </summary>
    /// <returns><see langword="false"/> if there is nothing to rasterize.</returns>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public bool Begin(EdgeBuilder edges, FillRule fillRule, bool isAliased, CancellationToken cancellationToken)
    {
        _isAliased = isAliased;
        _recordsPieces = false;
        _recordsRow = false;
        HasPieces = false;
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

        if (!isAliased)
        {
            ClipHeights(edges);
        }

        _edgeBytes = edges.RawBytes;
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
        var left = double.PositiveInfinity;
        foreach (ref readonly var edge in list)
        {
            var first = GetFirstRow(edge);
            var end = (int)Math.Ceiling(Math.Min(edge.Y1, _height));
            rowCounts[first]++;
            deltas[first]++;
            deltas[end]--;
            _firstRow = Math.Min(_firstRow, first);
            _lastRow = Math.Max(_lastRow, end);
            left = Math.Min(left, Math.Min(edge.X0, edge.X1));
        }

        // What is inside after the last edge of a row goes on to the right side of the frame
        EstimatedPixels = _edgeCount == 0 ? 0 : (long)(_lastRow - _firstRow) * (_width - (int)Math.Clamp(Math.Floor(left), 0, _width));

        _row = _firstRow;
        _isRectangle = false;
        if (_edgeCount == 0)
            return false;

        if (TryGetWholePixelRectangle(list, out var rectangleLeft, out var rectangleRight))
        {
            // Its coverages are written once: no sweep writes the row of coverages while the rows are read
            _isRectangle = true;
            X = rectangleLeft;
            Count = rectangleRight - rectangleLeft;
            CoverageRow[rectangleLeft..rectangleRight].Fill(FullCoverage);
            return true;
        }

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

        EnsureCapacity(ref _order, ref _orderBytes, ref _orderCapacity, _edgeCount, sizeof(int));
        EnsureCapacity(ref _sweep, ref _sweepBytes, ref _sweepCapacity, maxActive, SweepBytesPerEdge);
        var order = Order;
        for (var i = 0; i < list.Length; i++)
        {
            order[rowCounts[GetFirstRow(list[i])]++] = i;
        }

        // A rasterization canceled in the middle of a walk left its changes
        ChangedPositions[..((maxActive >> 6) + 1)].Clear();

        // Every slot a row can need is free; the first ones are taken first. The edges of a row take their slots before
        // those that end in it release theirs: no more slots are taken at once than edges cross a row
        var free = FreeSlots;
        _freeCount = maxActive;
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

            if (_isRectangle)
            {
                if (Count == 0)
                    continue;

                Y = row;
                return true;
            }

            if (_isAliased)
            {
                if (ProcessAliasedRow(row))
                    return true;

                continue;
            }

            if (_recordsPieces)
            {
                _recordsRow = true;
                _pieceCount = 0;
                _pieceLeft = double.PositiveInfinity;
                _pieceRight = double.NegativeInfinity;
            }

            if (ProcessRow(row))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Moves past the rows of a rectangle of whole pixels (<see cref="IsFullyCovered"/>) that <see cref="MoveNext"/> has
    /// not returned yet, which all have the run of the current one. The caller checks the cancellation every
    /// <see cref="ProcessingKernels.RowsPerCancellationCheck"/> rows, as <see cref="MoveNext"/> does.
    /// </summary>
    /// <returns>The first of these rows and the row after the last one.</returns>
    public (int Top, int Bottom) TakeRemainingRectangleRows()
    {
        Debug.Assert(_isRectangle && Count != 0);
        var top = _row;
        _row = _lastRow;
        return (top, _lastRow);
    }

    public void Dispose()
    {
        _accumulationBytes = [];
        _coverageBytes = [];
        _rowsBytes = [];
        _orderBytes = [];
        _sweepBytes = [];
        _edgeBytes = [];
        _pieceBytes = [];
        _pieces?.Dispose();
        _pieces = null;
        _accumulation.Dispose();
        _coverage.Dispose();
        _rows.Dispose();
        _order?.Dispose();
        _sweep?.Dispose();
    }

    private void EnsureCapacity(ref PooledBuffer? buffer, ref byte[] bytes, ref int capacity, int required, int elementSize)
    {
        if (required <= capacity)
            return;

        // The previous buffer is released first: its content is not needed
        bytes = [];
        buffer?.Dispose();
        buffer = null;
        capacity = 0;
        var grown = Math.Max(required, 16);
        buffer = ResampleWeights.Rent<byte>(_scope, (long)grown * elementSize);
        bytes = buffer.RawBuffer;
        capacity = grown;
    }

    /// <summary>
    /// Gets the columns of a rectangle of whole pixels: two vertical edges of opposite directions on whole columns, from
    /// the same whole row to the same whole row, or one such edge when the other one was on the right of the frame
    /// (<see cref="EdgeBuilder"/> drops it: what is on the right of the edge is inside up to the right side of the frame).
    /// Each pixel between them is covered entirely and the others not at all, with or without antialiasing and with either
    /// fill rule (the winding number is 1 or -1 between the edges, 0 elsewhere), which is what the sweep computes: the area
    /// of a pixel inside is exactly 1, and no pixel center is on an edge.
    /// </summary>
    /// <returns><see langword="false"/> when the edges are not such a rectangle.</returns>
    private bool TryGetWholePixelRectangle(ReadOnlySpan<DrawingEdge> edges, out int left, out int right)
    {
        left = 0;
        right = 0;
        if (edges.Length == 1)
        {
            ref readonly var edge = ref edges[0];
            if (edge.X0 != edge.X1 || !IsWhole(edge.X0) || !IsWhole(edge.Y0) || !IsWhole(edge.Y1))
                return false;

            left = (int)Math.Clamp(edge.X0, 0, _width);
            right = _width;
            return true;
        }

        if (edges.Length != 2)
            return false;

        ref readonly var first = ref edges[0];
        ref readonly var second = ref edges[1];
        if (first.X0 != first.X1 || second.X0 != second.X1 || first.Y0 != second.Y0 || first.Y1 != second.Y1 || first.Direction != -second.Direction || first.X0 == second.X0)
            return false;

        if (!IsWhole(first.X0) || !IsWhole(second.X0) || !IsWhole(first.Y0) || !IsWhole(first.Y1))
            return false;

        left = (int)Math.Clamp(Math.Min(first.X0, second.X0), 0, _width);
        right = (int)Math.Clamp(Math.Max(first.X0, second.X0), 0, _width);
        return true;

        static bool IsWhole(double value) => Math.Floor(value) == value;
    }

    /// <summary>Gets the first row of the frame an edge is on.</summary>
    private static int GetFirstRow(in DrawingEdge edge) => edge.Y0 > 0 ? (int)edge.Y0 : 0;

    /// <summary>
    /// Cuts the edges at the top and at the bottom of the frame, for the areas: then an edge entirely to the right of the
    /// frame is dropped, and one entirely to the left of it is moved onto its left side, which keeps the winding number of
    /// every point of the frame.
    /// </summary>
    private void ClipHeights(EdgeBuilder edges)
    {
        var list = edges.Edges;
        var kept = 0;
        for (var i = 0; i < list.Length; i++)
        {
            var edge = list[i];
            if (edge.Y0 < 0 || edge.Y1 > _height)
            {
                var top = edge.Y0;
                var left = edge.X0;
                var slope = (edge.X1 - edge.X0) / (edge.Y1 - edge.Y0);
                if (edge.Y0 < 0)
                {
                    edge.X0 = left - (top * slope);
                    edge.Y0 = 0;
                }

                if (edge.Y1 > _height)
                {
                    edge.X1 = left + ((_height - top) * slope);
                    edge.Y1 = _height;
                }

                if (!(edge.Y0 < edge.Y1) || Math.Min(edge.X0, edge.X1) >= _width)
                    continue;

                edge.Slope = (edge.X1 - edge.X0) / (edge.Y1 - edge.Y0);
            }

            if (Math.Max(edge.X0, edge.X1) <= 0)
            {
                edge.X0 = 0;
                edge.X1 = 0;
                edge.Slope = 0;
            }

            list[kept++] = edge;
        }

        edges.Truncate(kept);
    }

    /// <summary>Covers the pixels of a row whose centers are inside: the edges are crossed at the height of the centers.</summary>
    private bool ProcessAliasedRow(int row)
    {
        var edges = EdgeList;
        var center = row + 0.5;

        // The edges that ended leave the active ones (in the slots of the sweep line), then those that start in the row
        // join them: there are never more than the edges of one row
        var active = SlotEdge;
        for (var i = 0; i < _lineCount; i++)
        {
            if (edges[active[i]].Y1 < center)
            {
                active[i--] = active[--_lineCount];
            }
        }

        var order = Order;
        while (_nextEdge < _edgeCount && GetFirstRow(edges[order[_nextEdge]]) <= row)
        {
            active[_lineCount++] = order[_nextEdge++];
        }

        var positions = SinceY;
        var directions = SlotSign;
        var count = 0;
        var shiftsRemaining = _lineCount;
        var needsSort = false;
        for (var i = 0; i < _lineCount; i++)
        {
            ref readonly var edge = ref edges[active[i]];
            if (edge.Y0 < center && center <= edge.Y1)
            {
                // The crossing is one rounded division: when it is exactly the center of a pixel, which decides
                // whether that pixel is inside, it is found exactly.
                var x = edge.X0 + (((center - edge.Y0) * (edge.X1 - edge.X0)) / (edge.Y1 - edge.Y0));
                var position = count++;
                if (!needsSort)
                {
                    while (position > 0 && positions[position - 1] > x)
                    {
                        if (shiftsRemaining == 0)
                        {
                            needsSort = true;
                            break;
                        }

                        positions[position] = positions[position - 1];
                        directions[position] = directions[position - 1];
                        position--;
                        shiftsRemaining--;
                    }
                }

                positions[position] = x;
                directions[position] = edge.Direction;
            }
        }

        // Keep insertion sort linear for already ordered crossings, but bound its total shifts per row.
        // Equal crossings may be ordered by direction: any spans between them have zero width.
        if (needsSort)
        {
            SortEvents(positions[..count], directions[..count]);
        }

        // The spans between the crossings where the region starts and ends
        var coverage = CoverageRow;
        var first = -1;
        var last = -1;
        var winding = 0;
        double start = 0;
        for (var i = 0; i < count; i++)
        {
            var wasInside = IsInside(winding);
            winding += directions[i];
            var isInside = IsInside(winding);
            if (!wasInside && isInside)
            {
                start = positions[i];
            }
            else if (wasInside && !isInside)
            {
                AddSpan(coverage, start, positions[i], ref first, ref last);
            }
        }

        // An edge on the right of the frame was dropped: what is inside after the last crossing is inside up to the right side
        if (IsInside(winding))
        {
            AddSpan(coverage, start, _width, ref first, ref last);
        }

        if (first < 0)
            return false;

        Y = row;
        X = first;
        Count = last - first;
        return true;
    }

    /// <summary>Covers the pixels whose centers are between two crossings, and clears those between the previous span and this one.</summary>
    private void AddSpan(Span<uint> coverage, double start, double end, ref int first, ref int last)
    {
        var from = (int)Math.Clamp(Math.Floor(start + 0.5), 0, _width);
        var to = (int)Math.Clamp(Math.Floor(end + 0.5), 0, _width);
        if (from >= to)
            return;

        if (first < 0)
        {
            first = from;
        }
        else if (from > last)
        {
            coverage[last..from].Clear();
        }

        coverage[from..to].Fill(FullCoverage);
        last = Math.Max(last, to);
    }

    /// <summary>Sweeps a row and turns what it accumulated into coverages.</summary>
    private bool ProcessRow(int row)
    {
        var s = new SweepSpans(this);
        var edges = s.Edges;
        var slotEdges = s.SlotEdges;
        double top = row;
        var bottom = top + 1;

        // The events of the row: the edges of the sweep line that end in it, and the edges that start in it
        var line = s.Line;
        var slotEdge = s.SlotEdge;
        var startKeys = s.StartKeys;
        var startSlots = s.StartSlots;
        var endKeys = s.EndKeys;
        var endSlots = s.EndSlots;
        var startCount = 0;
        var endCount = 0;
        var positionHint = s.PositionHint;
        for (var i = 0; i < _lineCount; i++)
        {
            var slot = line[i];
            var end = slotEdges[slot].Y1;
            if (end <= bottom)
            {
                endKeys[endCount] = end;
                endSlots[endCount++] = slot;
                positionHint[slot] = i;
            }
        }

        var order = Order;
        while (_nextEdge < _edgeCount && (int)edges[order[_nextEdge]].Y0 == row)
        {
            var index = order[_nextEdge++];
            ref readonly var edge = ref edges[index];
            var slot = s.FreeSlots[--_freeCount];
            slotEdge[slot] = index;
            slotEdges[slot] = edge;
            s.SlotSign[slot] = 0;
            s.WindingBefore[slot] = UnknownWinding;
            s.SinceY[slot] = edge.Y0;
            s.CrossingY[slot] = double.PositiveInfinity;
            s.HeapPosition[slot] = -1;
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
                while (_heapCount > 0 && s.CrossingY[s.Heap[0]] <= y)
                {
                    Swap(ref s, s.Heap[0], y);
                }

                _changedFirst = int.MaxValue;
                _changedLast = -1;
                _changeEventCount = 0;
                while (endIndex < endCount && endKeys[endIndex] <= y)
                {
                    Remove(ref s, endSlots[endIndex++]);
                }

                while (startIndex < startCount && startKeys[startIndex] <= y)
                {
                    Insert(ref s, startSlots[startIndex++], y);
                }

                if (_changedFirst == int.MaxValue)
                    break;

                Walk(ref s, y);
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

            if (_heapCount > 0 && s.CrossingY[s.Heap[0]] < next)
            {
                next = s.CrossingY[s.Heap[0]];
            }

            y = next;
        }

        // The boundaries that go on below the row are recorded up to its bottom
        for (var i = 0; i < _lineCount; i++)
        {
            RecordBoundary(ref s, line[i], bottom);
        }

        if (_recordsRow)
        {
            HasPieces = true;
            Y = row;
            return _pieceCount != 0;
        }

        HasPieces = false;
        return Flush(row);
    }

    /// <summary>
    /// Sorts the events of a row by height, then by slot: a total order (the slots of a row are distinct), so that the
    /// result does not depend on the sorting algorithm. The heights of a row are positive and close to each other: the
    /// pair is packed into one integer when it fits (<see cref="TrySortPacked"/>). Otherwise, an introspective sort:
    /// partitions around a median of three, sorts short ranges by insertion, and falls back to a heap sort when the
    /// partitions go too deep. In place, without allocation, and never quadratic.
    /// </summary>
    internal static void SortEvents(Span<double> keys, Span<int> slots)
    {
        if (keys.Length > MaxInsertionSortLength && TrySortPacked(keys, slots))
            return;

        IntroSortEvents(keys, slots, 2 * (BitOperations.Log2((uint)keys.Length) + 1));
    }

    /// <summary>
    /// Sorts events as integers made of the bits of the height (a positive double is ordered as its bits), minus those of
    /// the lowest one, followed by the bits of the slot: their order is the order of the events.
    /// </summary>
    /// <returns><see langword="false"/>, and nothing changed, when a height is negative or not a number, a slot is negative, or the integers do not fit in 64 bits.</returns>
    internal static bool TrySortPacked(Span<double> keys, Span<int> slots)
    {
        var bits = unsafe(MemoryMarshal.Cast<double, long>(keys));
        slots = slots[..bits.Length];
        GetPackingRange(bits, slots, out var lowest, out var highest, out var slotMask);

        // -0 and the negative heights have the sign bit, the NaNs are above the infinity
        if (lowest < 0 || highest > BitConverter.DoubleToInt64Bits(double.PositiveInfinity) || slotMask < 0)
            return false;

        var slotBits = Math.Max(32 - BitOperations.LeadingZeroCount((uint)slotMask), 1);
        if (((ulong)(highest - lowest) >> (64 - slotBits)) != 0)
            return false;

        if (Vector512.IsHardwareAccelerated && bits.Length <= MaxVectorSortLength)
        {
            SortPackedVector512(bits, slots, lowest, slotBits);
            return true;
        }

        var packed = unsafe(MemoryMarshal.Cast<double, ulong>(keys));
        for (var i = 0; i < packed.Length; i++)
        {
            packed[i] = ((ulong)(bits[i] - lowest) << slotBits) | (uint)slots[i];
        }

        packed.Sort();
        var slotMaskBits = (1UL << slotBits) - 1;
        for (var i = 0; i < packed.Length; i++)
        {
            var value = packed[i];
            slots[i] = (int)(value & slotMaskBits);
            bits[i] = lowest + (long)(value >> slotBits);
        }

        return true;
    }

    /// <summary>
    /// Sorts integers in ascending order, as <see cref="MemoryExtensions.Sort{T}(Span{T})"/> does, with a bitonic network
    /// of AVX-512 vectors: the values, followed by as many <see cref="ulong.MaxValue"/> as make a power of two of at least
    /// eight, are sorted in each vector, then the sorted sequences of each size are merged in pairs (a comparison of each
    /// value with its mirror in the other sequence, then half-cleaners at decreasing distances, across vectors and then
    /// in them). The network makes the same comparisons whatever the values: no branch to mispredict, where a comparison
    /// sort of the hundred events of a row of text mispredicts about every other comparison.
    /// </summary>
    /// <param name="values">At most <see cref="MaxVectorSortLength"/> values.</param>
    internal static void SortVector512(Span<ulong> values)
    {
        Debug.Assert(Vector512.IsHardwareAccelerated && values.Length <= MaxVectorSortLength);
        Span<Vector512<ulong>> vectors = stackalloc Vector512<ulong>[GetSortVectorCount(values.Length)];
        var lanes = unsafe(MemoryMarshal.Cast<Vector512<ulong>, ulong>(vectors));
        values.CopyTo(lanes);
        lanes[values.Length..].Fill(ulong.MaxValue);
        SortVectors(vectors);
        lanes[..values.Length].CopyTo(values);
    }

    /// <summary>
    /// Sorts packed events (<see cref="TrySortPacked"/>) with the network of <see cref="SortVector512"/>: they are packed
    /// into its vectors and taken out of them eight at a time.
    /// </summary>
    private static void SortPackedVector512(Span<long> bits, Span<int> slots, long lowest, int slotBits)
    {
        var count = bits.Length;
        Span<Vector512<ulong>> vectors = stackalloc Vector512<ulong>[GetSortVectorCount(count)];
        var lanes = unsafe(MemoryMarshal.Cast<Vector512<ulong>, ulong>(vectors));
        var lowestLanes = Vector512.Create(lowest);
        var i = 0;
        for (; i + 8 <= count; i += 8)
        {
            var slotLanes = Vector512.WidenLower(Vector256.Create(slots.Slice(i, 8)).ToVector512());
            vectors[i >> 3] = ((Vector512.Create(bits.Slice(i, 8)) - lowestLanes).AsUInt64() << slotBits) | slotLanes.AsUInt64();
        }

        for (; i < count; i++)
        {
            lanes[i] = ((ulong)(bits[i] - lowest) << slotBits) | (uint)slots[i];
        }

        lanes[count..].Fill(ulong.MaxValue);
        SortVectors(vectors);
        var slotMaskBits = (1UL << slotBits) - 1;
        var slotMaskLanes = Vector512.Create(slotMaskBits);
        for (i = 0; i + 8 <= count; i += 8)
        {
            var value = vectors[i >> 3];
            (lowestLanes + (value >> slotBits).AsInt64()).CopyTo(bits.Slice(i, 8));
            Vector512.Narrow((value & slotMaskLanes).AsInt64(), Vector512<long>.Zero).GetLower().CopyTo(slots.Slice(i, 8));
        }

        for (; i < count; i++)
        {
            var value = lanes[i];
            slots[i] = (int)(value & slotMaskBits);
            bits[i] = lowest + (long)(value >> slotBits);
        }
    }

    /// <summary>Gets the number of vectors of the network that sorts a number of values: a power of two.</summary>
    private static int GetSortVectorCount(int count) => (int)BitOperations.RoundUpToPowerOf2((uint)(Math.Max(count, 1) + 7) / 8);

    /// <summary>Gets the lowest and the highest bits of the heights of events, and the bits set in their slots.</summary>
    private static void GetPackingRange(ReadOnlySpan<long> bits, ReadOnlySpan<int> slots, out long lowest, out long highest, out int slotMask)
    {
        lowest = long.MaxValue;
        highest = long.MinValue;
        slotMask = 0;
        var i = 0;
        if (Vector512.IsHardwareAccelerated && bits.Length >= 16)
        {
            var lowestLanes = Vector512.Create(long.MaxValue);
            var highestLanes = Vector512.Create(long.MinValue);
            var slotLanes = Vector512<int>.Zero;
            for (; i + 16 <= bits.Length; i += 16)
            {
                var first = Vector512.Create(bits.Slice(i, 8));
                var second = Vector512.Create(bits.Slice(i + 8, 8));
                lowestLanes = Vector512.Min(lowestLanes, Vector512.Min(first, second));
                highestLanes = Vector512.Max(highestLanes, Vector512.Max(first, second));
                slotLanes |= Vector512.Create(slots.Slice(i, 16));
            }

            for (var lane = 0; lane < Vector512<long>.Count; lane++)
            {
                lowest = Math.Min(lowest, lowestLanes.GetElement(lane));
                highest = Math.Max(highest, highestLanes.GetElement(lane));
            }

            for (var lane = 0; lane < Vector512<int>.Count; lane++)
            {
                slotMask |= slotLanes.GetElement(lane);
            }
        }

        for (; i < bits.Length; i++)
        {
            lowest = Math.Min(lowest, bits[i]);
            highest = Math.Max(highest, bits[i]);
            slotMask |= slots[i];
        }
    }

    /// <summary>The network of <see cref="SortVector512"/>, on its vectors.</summary>
    private static void SortVectors(Span<Vector512<ulong>> vectors)
    {
        var vectorCount = vectors.Length;

        // The partner of each lane at a distance of 1, 2 and 4 lanes, and its mirror in pairs, fours and the whole vector;
        // a lane keeps the smaller value when it is the first of the two
        var next1 = Vector512.Create(1UL, 0, 3, 2, 5, 4, 7, 6);
        var next2 = Vector512.Create(2UL, 3, 0, 1, 6, 7, 4, 5);
        var next4 = Vector512.Create(4UL, 5, 6, 7, 0, 1, 2, 3);
        var mirror4 = Vector512.Create(3UL, 2, 1, 0, 7, 6, 5, 4);
        var reverse = Vector512.Create(7UL, 6, 5, 4, 3, 2, 1, 0);
        var first1 = Vector512.Create(ulong.MaxValue, 0, ulong.MaxValue, 0, ulong.MaxValue, 0, ulong.MaxValue, 0);
        var first2 = Vector512.Create(ulong.MaxValue, ulong.MaxValue, 0, 0, ulong.MaxValue, ulong.MaxValue, 0, 0);
        var first4 = Vector512.Create(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0, 0, 0, 0);
        foreach (ref var vector in vectors)
        {
            var value = Exchange(vector, next1, first1);
            value = Exchange(Exchange(value, mirror4, first2), next1, first1);
            vector = Exchange(Exchange(Exchange(value, reverse, first4), next2, first2), next1, first1);
        }

        for (var size = 2; size <= vectorCount; size <<= 1)
        {
            // Each pair of sorted sequences of half that size: every value with its mirror, then the half-cleaners
            for (var block = 0; block < vectorCount; block += size)
            {
                for (var i = 0; i < size / 2; i++)
                {
                    ref var low = ref vectors[block + i];
                    ref var high = ref vectors[block + size - 1 - i];
                    var mirrored = Vector512.Shuffle(high, reverse);
                    var smaller = Vector512.Min(low, mirrored);
                    high = Vector512.Shuffle(Vector512.Max(low, mirrored), reverse);
                    low = smaller;
                }
            }

            for (var distance = size / 4; distance > 0; distance >>= 1)
            {
                for (var block = 0; block < vectorCount; block += 2 * distance)
                {
                    for (var i = block; i < block + distance; i++)
                    {
                        ref var low = ref vectors[i];
                        ref var high = ref vectors[i + distance];
                        var smaller = Vector512.Min(low, high);
                        high = Vector512.Max(low, high);
                        low = smaller;
                    }
                }
            }

            foreach (ref var vector in vectors)
            {
                vector = Exchange(Exchange(Exchange(vector, next4, first4), next2, first2), next1, first1);
            }
        }

        // Each lane gets the smaller of its value and the value of its partner when it is the first of the two, the larger otherwise
        static Vector512<ulong> Exchange(Vector512<ulong> value, Vector512<ulong> partners, Vector512<ulong> isFirst)
        {
            var partner = Vector512.Shuffle(value, partners);
            return Vector512.ConditionalSelect(isFirst, Vector512.Min(value, partner), Vector512.Max(value, partner));
        }
    }

    /// <summary>Sorts events, with a heap sort once <paramref name="depthLimit"/> partitions are nested.</summary>
    internal static void IntroSortEvents(Span<double> keys, Span<int> slots, int depthLimit)
    {
        while (keys.Length > MaxInsertionSortLength)
        {
            if (depthLimit-- == 0)
            {
                HeapSortEvents(keys, slots);
                return;
            }

            var pivot = PartitionEvents(keys, slots);

            // The shorter side is sorted by recursion, the longer one by the loop: the stack stays logarithmic
            if (pivot < keys.Length - pivot - 1)
            {
                IntroSortEvents(keys[..pivot], slots[..pivot], depthLimit);
                keys = keys[(pivot + 1)..];
                slots = slots[(pivot + 1)..];
            }
            else
            {
                IntroSortEvents(keys[(pivot + 1)..], slots[(pivot + 1)..], depthLimit);
                keys = keys[..pivot];
                slots = slots[..pivot];
            }
        }

        for (var i = 1; i < keys.Length; i++)
        {
            var key = keys[i];
            var slot = slots[i];
            var position = i;
            while (position > 0 && IsEventBefore(key, slot, keys[position - 1], slots[position - 1]))
            {
                keys[position] = keys[position - 1];
                slots[position] = slots[position - 1];
                position--;
            }

            keys[position] = key;
            slots[position] = slot;
        }
    }

    /// <summary>Partitions events around the median of the first, middle and last ones.</summary>
    /// <returns>The final position of the pivot: the events before it come before it, those after it after it.</returns>
    private static int PartitionEvents(Span<double> keys, Span<int> slots)
    {
        var last = keys.Length - 1;
        var middle = last >> 1;
        SortPair(keys, slots, 0, middle);
        SortPair(keys, slots, 0, last);
        SortPair(keys, slots, middle, last);

        // The median goes next to the end; the first and last events are already on their sides
        SwapEvents(keys, slots, middle, last - 1);
        var pivotKey = keys[last - 1];
        var pivotSlot = slots[last - 1];
        var left = 0;
        var right = last - 1;
        while (true)
        {
            while (IsEventBefore(keys[++left], slots[left], pivotKey, pivotSlot))
            {
            }

            while (IsEventBefore(pivotKey, pivotSlot, keys[--right], slots[right]))
            {
            }

            if (left >= right)
                break;

            SwapEvents(keys, slots, left, right);
        }

        SwapEvents(keys, slots, left, last - 1);
        return left;

        static void SortPair(Span<double> keys, Span<int> slots, int i, int j)
        {
            if (IsEventBefore(keys[j], slots[j], keys[i], slots[i]))
            {
                SwapEvents(keys, slots, i, j);
            }
        }
    }

    private static void SwapEvents(Span<double> keys, Span<int> slots, int i, int j)
    {
        (keys[i], keys[j]) = (keys[j], keys[i]);
        (slots[i], slots[j]) = (slots[j], slots[i]);
    }

    private static void HeapSortEvents(Span<double> keys, Span<int> slots)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEventBefore(double key, int slot, double otherKey, int otherSlot)
        => key != otherKey ? key < otherKey : slot < otherSlot;

    /// <summary>Puts an edge that starts at the current height in the sweep line.</summary>
    private void Insert(ref SweepSpans s, int slot, double y)
    {
        var slotEdges = s.SlotEdges;
        var line = s.Line;
        var slotEdge = s.SlotEdge;
        ref readonly var edge = ref slotEdges[slot];

        // After every edge that is on its left at this height, or at the same place and going more to the left
        var low = 0;
        var high = _lineCount;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            var otherSlot = line[middle];
            ref readonly var other = ref slotEdges[otherSlot];
            if (IsBefore(GetX(other, y), other.Slope, otherSlot, edge.X0, edge.Slope, slot, slotEdge))
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
        s.PositionHint[slot] = low;

        // Its left neighbor has a new right neighbor, and it has one
        var neighborChanged = s.NeighborChanged;
        neighborChanged[slot] = 1;
        if (low > 0)
        {
            neighborChanged[line[low - 1]] = 1;
        }

        var changedFirst = _changedFirst;
        var changedLast = _changedLast;
        _changedFirst = Math.Min(changedFirst, low);
        _changedLast = Math.Max(changedLast >= low ? changedLast + 1 : changedLast, low);
        if (_changeEventCount != 0 || _changedLast - _changedFirst >= MinJumpingWalkSpan)
        {
            RecordChange((low << 2) | Insertion, changedFirst, changedLast);
        }

        CountEvent();
    }

    /// <summary>Takes an edge that ends at the current height out of the sweep line.</summary>
    private void Remove(ref SweepSpans s, int slot)
    {
        var end = s.SlotEdges[slot].Y1;
        RecordBoundary(ref s, slot, end);
        var position = FindPosition(ref s, slot, end);
        var line = s.Line;
        line.Slice(position + 1, _lineCount - position - 1).CopyTo(line[position..]);
        _lineCount--;
        if (s.HeapPosition[slot] >= 0)
        {
            RemoveFromHeap(ref s, slot);
        }

        s.FreeSlots[_freeCount++] = slot;
        if (position > 0)
        {
            s.NeighborChanged[line[position - 1]] = 1;
        }

        // The edge that takes its place has a new left neighbor
        var changedFirst = _changedFirst;
        var changedLast = _changedLast;
        _changedFirst = Math.Min(changedFirst, position);
        _changedLast = Math.Max(changedLast > position ? changedLast - 1 : changedLast, position);
        if (_changeEventCount != 0 || _changedLast - _changedFirst >= MinJumpingWalkSpan)
        {
            RecordChange((position << 2) | Removal, changedFirst, changedLast);
        }

        CountEvent();
    }

    /// <summary>Exchanges an edge and its right neighbor, which cross at the current height.</summary>
    private void Swap(ref SweepSpans s, int slot, double y)
    {
        var line = s.Line;
        var position = FindPosition(ref s, slot, y);
        var other = line[position + 1];
        line[position] = other;
        line[position + 1] = slot;
        s.PositionHint[other] = position;
        s.PositionHint[slot] = position + 1;

        // The winding number left of the pair does not change; what each of them separates does
        var winding = s.WindingBefore[slot];
        winding = Update(ref s, other, winding, y);
        Update(ref s, slot, winding, y);
        for (var i = Math.Max(position - 1, 0); i <= position + 1; i++)
        {
            ScheduleCrossing(ref s, i, y);
        }

        CountEvent();
    }

    /// <summary>
    /// Records an insertion or a removal in <see cref="ChangeEvents"/>, once the changes of the height span many positions.
    /// The changes before the first one recorded were in a range of fewer positions (<paramref name="changedFirst"/> to
    /// <paramref name="changedLast"/>, before this one): the range is recorded as a whole.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordChange(int change, int changedFirst, int changedLast)
    {
        var events = ChangeEvents;
        var count = _changeEventCount;
        if (count == 0 && changedFirst <= changedLast)
        {
            events[count++] = (Math.Max(changedFirst - 1, 0) << 2) | ChangedRange;
            events[count++] = (changedLast << 2) | ChangedRange;
        }

        events[count++] = change;
        _changeEventCount = count;
    }

    /// <summary>
    /// Marks in <see cref="ChangedPositions"/> the positions where the edges of the current height were inserted and
    /// removed, and their left neighbors, which have new right neighbors: the positions marked by an event move with their
    /// edges as the events that follow it insert and remove edges on their left.
    /// </summary>
    private void MarkChanges(Span<ulong> bits)
    {
        var events = ChangeEvents[.._changeEventCount];

        // The length of the sweep line before the events
        var length = _lineCount;
        foreach (var changeEvent in events)
        {
            switch (changeEvent & 3)
            {
                case Insertion:
                    length--;
                    break;
                case Removal:
                    length++;
                    break;
            }
        }

        if ((events[0] & 3) == ChangedRange)
        {
            for (var position = events[0] >> 2; position <= events[1] >> 2; position++)
            {
                bits[position >> 6] |= 1UL << position;
            }

            events = events[2..];
        }

        foreach (var changeEvent in events)
        {
            var position = changeEvent >> 2;
            var first = position >> 6;
            var kept = (1UL << position) - 1;
            if ((changeEvent & 3) == Insertion)
            {
                // The positions marked go up to the length of the sweep line
                length++;
                for (var i = length >> 6; i > first; i--)
                {
                    bits[i] = (bits[i] << 1) | (bits[i - 1] >> 63);
                }

                var word = bits[first];
                bits[first] = (word & kept) | ((word & ~kept) << 1);
            }
            else
            {
                // The edge on the right of the removed one takes its position
                var last = length >> 6;
                length--;
                var word = bits[first];
                var shifted = (word & kept) | ((word >> 1) & ~kept);
                for (var i = first; i < last; i++)
                {
                    var next = bits[i + 1];
                    bits[i] = shifted | (next << 63);
                    shifted = next >> 1;
                }

                bits[last] = shifted;
            }

            MarkChange(bits, position);
        }
    }

    private static void MarkChange(Span<ulong> bits, int position)
    {
        bits[position >> 6] |= 1UL << position;
        if (position > 0)
        {
            bits[(position - 1) >> 6] |= 1UL << (position - 1);
        }
    }

    /// <summary>Gets the first position noted in <see cref="ChangedPositions"/> from <paramref name="position"/>, or <paramref name="limit"/> when there is none before it.</summary>
    private static int GetNextChange(ReadOnlySpan<ulong> bits, int position, int limit)
    {
        var index = position >> 6;
        var word = bits[index] & (ulong.MaxValue << position);
        while (true)
        {
            if (word != 0)
                return Math.Min((index << 6) + BitOperations.TrailingZeroCount(word), limit);

            if (++index > limit >> 6)
                return limit;

            word = bits[index];
        }
    }

    /// <summary>
    /// Walks the edges around what changed in the sweep line at the current height, from left to right: each edge gets
    /// its winding number, becomes or stops being a boundary, and learns when it crosses its new neighbor.
    /// </summary>
    private void Walk(ref SweepSpans s, double y)
    {
        var walksEveryEdge = WalksEveryEdge;
        if (_changeEventCount != 0 && !walksEveryEdge)
        {
            WalkBetweenChanges(ref s, y);
            return;
        }

        var line = s.Line;
        var first = _changedFirst;
        var last = Math.Min(_changedLast, _lineCount - 1);
        var winding = 0;
        if (first > 0)
        {
            var before = line[first - 1];
            winding = s.WindingBefore[before] + s.SlotEdges[before].Direction;
        }

        // The contours are closed: right of the last change, the winding numbers are what they were, and the walk stops
        // at the first edge that confirms it. The changes of a height can be far apart (the glyphs of a line of text share
        // their baseline and their x-height), and the edges between them are passed quickly: an edge whose winding number
        // did not change stays the boundary it was. When they span many edges, the walk jumps between them instead
        // (WalkBetweenChanges)
        var slotEdges = s.SlotEdges;
        var windingBefore = s.WindingBefore;
        var position = first;
        for (; position < _lineCount; position++)
        {
            var slot = line[position];
            if (windingBefore[slot] == winding)
            {
                if (position > last)
                    break;

                if (!walksEveryEdge)
                {
                    winding += slotEdges[slot].Direction;
                    continue;
                }
            }

            winding = Update(ref s, slot, winding, y);
        }

        // The pairs of neighbors that are new, and those that cross, learn again when they cross. The others did not cross
        // when they were last computed, and still do not: whether they cross depends on where they are at the bottom of
        // the shorter one, not on the height they are computed at
        var neighborChanged = s.NeighborChanged;
        var crossingY = s.CrossingY;
        for (var i = Math.Max(first - 1, 0); i < position && i < _lineCount; i++)
        {
            var slot = line[i];
            if (walksEveryEdge || neighborChanged[slot] != 0 || !double.IsPositiveInfinity(crossingY[slot]))
            {
                ScheduleCrossing(ref s, i, y);
            }
        }
    }

    /// <summary>
    /// Does what <see cref="Walk"/> does, for changes that span many edges: their positions are marked
    /// (<see cref="ChangedPositions"/>), and from an edge whose winding number did not change, the walk jumps to the next
    /// one. The edges in between did not change either: each stays the boundary it was, and the winding number right of
    /// the last of them is what it was. Nothing else differs, and the coverages are the same, bit for bit.
    /// </summary>
    private void WalkBetweenChanges(ref SweepSpans s, double y)
    {
        var changes = ChangedPositions;
        MarkChanges(changes);
        var line = s.Line;
        var first = _changedFirst;
        var last = Math.Min(_changedLast, _lineCount - 1);
        var winding = 0;
        if (first > 0)
        {
            var before = line[first - 1];
            winding = s.WindingBefore[before] + s.SlotEdges[before].Direction;
        }

        var slotEdges = s.SlotEdges;
        var windingBefore = s.WindingBefore;
        var position = first;
        while (position < _lineCount)
        {
            var slot = line[position];
            if (windingBefore[slot] == winding)
            {
                if (position > last)
                    break;

                var next = GetNextChange(changes, position + 1, last + 1);
                if (next >= _lineCount)
                {
                    position = _lineCount;
                    break;
                }

                var previous = line[next - 1];
                winding = windingBefore[previous] + slotEdges[previous].Direction;
                position = next;
                continue;
            }

            winding = Update(ref s, slot, winding, y);
            position++;
        }

        // The new pairs of neighbors are at the positions of the changes, or left of them: when no pair crosses (the heap
        // is empty), they are the only ones to look at
        var neighborChanged = s.NeighborChanged;
        var crossingY = s.CrossingY;
        var from = Math.Max(first - 1, 0);
        var to = Math.Min(position, _lineCount);
        if (_heapCount == 0)
        {
            for (var i = GetNextChange(changes, from, to); i < to; i = GetNextChange(changes, i + 1, to))
            {
                if (neighborChanged[line[i]] != 0)
                {
                    ScheduleCrossing(ref s, i, y);
                }
            }
        }
        else
        {
            for (var i = from; i < to; i++)
            {
                var slot = line[i];
                if (neighborChanged[slot] != 0 || !double.IsPositiveInfinity(crossingY[slot]))
                {
                    ScheduleCrossing(ref s, i, y);
                }
            }
        }

        // Every position marked is between the left neighbor of the first change and the last change
        changes[(from >> 6)..((_changedLast >> 6) + 1)].Clear();
    }

    /// <summary>Sets the winding number left of an edge; when the edge becomes another kind of boundary, records the stretch it was until now.</summary>
    /// <returns>The winding number right of the edge.</returns>
    private int Update(ref SweepSpans s, int slot, int winding, double y)
    {
        var after = winding + s.SlotEdges[slot].Direction;
        var sign = (IsInside(after) ? 1 : 0) - (IsInside(winding) ? 1 : 0);
        if (sign != s.SlotSign[slot])
        {
            RecordBoundary(ref s, slot, y);
            s.SlotSign[slot] = sign;
        }

        s.WindingBefore[slot] = winding;
        return after;
    }

    /// <summary>Accumulates the stretch of boundary an edge has been since it last changed, up to a height.</summary>
    private void RecordBoundary(ref SweepSpans s, int slot, double y)
    {
        var sinceY = s.SinceY;
        var since = sinceY[slot];
        var sign = s.SlotSign[slot];
        if (sign != 0 && y > since)
        {
            ref readonly var edge = ref s.SlotEdges[slot];
            var height = y - since;
            AddBoundary(ref s, GetX(edge, since), GetX(edge, y), sign > 0 ? height : -height);
        }

        sinceY[slot] = y;
    }

    /// <summary>Finds when the edge at a position of the sweep line crosses its right neighbor, if it does before one of them ends.</summary>
    private void ScheduleCrossing(ref SweepSpans s, int position, double y)
    {
        var line = s.Line;
        var slot = line[position];
        s.NeighborChanged[slot] = 0;
        var crossing = double.PositiveInfinity;
        if (position + 1 < _lineCount)
        {
            var slotEdges = s.SlotEdges;
            ref readonly var left = ref slotEdges[slot];
            ref readonly var right = ref slotEdges[line[position + 1]];
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

        if (crossing == s.CrossingY[slot])
            return;

        var heapPosition = s.HeapPosition[slot];
        s.CrossingY[slot] = crossing;
        if (double.IsPositiveInfinity(crossing))
        {
            if (heapPosition >= 0)
            {
                RemoveFromHeap(ref s, slot);
            }
        }
        else if (heapPosition >= 0)
        {
            SiftDown(ref s, SiftUp(ref s, heapPosition));
        }
        else
        {
            s.Heap[_heapCount] = slot;
            s.HeapPosition[slot] = _heapCount;
            SiftUp(ref s, _heapCount++);
        }
    }

    private void RemoveFromHeap(ref SweepSpans s, int slot)
    {
        var heap = s.Heap;
        var position = s.HeapPosition[slot];
        s.HeapPosition[slot] = -1;
        s.CrossingY[slot] = double.PositiveInfinity;
        _heapCount--;
        if (position == _heapCount)
            return;

        var moved = heap[_heapCount];
        heap[position] = moved;
        s.HeapPosition[moved] = position;
        SiftDown(ref s, SiftUp(ref s, position));
    }

    private static int SiftUp(ref SweepSpans s, int position)
    {
        var heap = s.Heap;
        var slot = heap[position];
        while (position > 0)
        {
            var parent = (position - 1) >> 1;
            if (!IsEarlier(ref s, slot, heap[parent]))
                break;

            heap[position] = heap[parent];
            s.HeapPosition[heap[position]] = position;
            position = parent;
        }

        heap[position] = slot;
        s.HeapPosition[slot] = position;
        return position;
    }

    private void SiftDown(ref SweepSpans s, int position)
    {
        var heap = s.Heap;
        var slot = heap[position];
        while (true)
        {
            var child = (2 * position) + 1;
            if (child >= _heapCount)
                break;

            if (child + 1 < _heapCount && IsEarlier(ref s, heap[child + 1], heap[child]))
            {
                child++;
            }

            if (!IsEarlier(ref s, heap[child], slot))
                break;

            heap[position] = heap[child];
            s.HeapPosition[heap[position]] = position;
            position = child;
        }

        heap[position] = slot;
        s.HeapPosition[slot] = position;
    }

    /// <summary>The total order of the crossings: height, then slot.</summary>
    private static bool IsEarlier(ref SweepSpans s, int slot, int other)
    {
        var crossing = s.CrossingY;
        return crossing[slot] != crossing[other] ? crossing[slot] < crossing[other] : slot < other;
    }

    /// <summary>
    /// Finds a slot in the sweep line: around the position it had recently (<see cref="SweepSpans.PositionHint"/>), then
    /// by its place in the sweep line, which is in left-to-right order at a height (up to rounding). A slot is at one
    /// position only.
    /// </summary>
    private int FindPosition(ref SweepSpans s, int slot, double y)
    {
        var line = s.Line;
        var hint = s.PositionHint[slot];
        if ((uint)hint < (uint)_lineCount)
        {
            if (line[hint] == slot)
                return hint;

            var windowStart = Math.Max(hint - PositionHintWindow, 0);
            var found = line[windowStart..Math.Min(hint + PositionHintWindow, _lineCount)].IndexOf(slot);
            if (found >= 0)
                return windowStart + found;
        }

        var slotEdges = s.SlotEdges;
        var x = GetX(slotEdges[slot], y);
        var low = 0;
        var high = _lineCount;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (GetX(slotEdges[line[middle]], y) < x)
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
    /// Makes the rows of an antialiased shape that is not a rectangle of whole pixels come as their pieces of boundary
    /// (<see cref="HasPieces"/>) instead of their coverages, from the next call to <see cref="MoveNext"/> until the next
    /// call to <see cref="Begin(EdgeBuilder, FillRule, bool, CancellationToken)"/>: the sweep, which carries its state
    /// from one row to the next, then leaves to others the accumulation and the coverages, which take time in proportion
    /// to the width of the rows. A row with more than <see cref="MaxPiecesPerRow"/> pieces comes as its coverages.
    /// </summary>
    /// <returns><see langword="false"/> when the rows of the shape come as their coverages: aliased, or a rectangle of whole pixels.</returns>
    /// <exception cref="ImageResourceLimitException">The storage of the pieces exceeds the allocation limit.</exception>
    public bool RecordPieces()
    {
        if (!ReservePieces())
            return false;

        _recordsPieces = true;
        return true;
    }

    /// <summary>Rents the storage of the pieces of boundary of a row, without recording them: a call rents it before its first pixel write.</summary>
    /// <returns><see langword="false"/> when the rows of the shape come as their coverages: aliased, or a rectangle of whole pixels.</returns>
    /// <exception cref="ImageResourceLimitException">The storage of the pieces exceeds the allocation limit.</exception>
    public bool ReservePieces()
    {
        if (!CanRecordPieces)
            return false;

        if (_pieces is null)
        {
            _pieces = ResampleWeights.Rent<double>(_scope, (long)MaxPiecesPerRow * ValuesPerPiece);
            _pieceBytes = _pieces.RawBuffer;
        }

        return true;
    }

    /// <summary>
    /// Records a piece of boundary of the current row, unless the row has too many: then the pieces recorded so far are
    /// accumulated, in their order, and the sweep accumulates the rest of the row itself.
    /// </summary>
    /// <returns><see langword="false"/> when the piece is to be accumulated by the caller.</returns>
    private bool TryRecordPiece(ref SweepSpans s, double x0, double x1, double signedHeight)
    {
        var left = Math.Min(x0, x1);

        // Entirely to the right of the frame: it changes nothing
        if (left >= _width)
            return true;

        if (_pieceCount == PieceLimit)
        {
            var pieces = Pieces;
            for (var i = 0; i < pieces.Length; i += ValuesPerPiece)
            {
                Accumulate(s.Accumulation, _width, pieces[i], pieces[i + 1], pieces[i + 2], ref _dirtyMin, ref _dirtyMax);
            }

            _pieceCount = 0;
            _recordsRow = false;
            return false;
        }

        var values = unsafe(MemoryMarshal.Cast<byte, double>(_pieceBytes.AsSpan()));
        var index = _pieceCount * ValuesPerPiece;
        values[index] = x0;
        values[index + 1] = x1;
        values[index + 2] = signedHeight;
        _pieceCount++;
        _pieceLeft = Math.Min(_pieceLeft, left);
        _pieceRight = Math.Max(_pieceRight, Math.Max(x0, x1));
        return true;
    }

    /// <summary>
    /// Accumulates the pieces of boundary of a row (<see cref="Pieces"/>) in a difference row that is all zeros, and turns
    /// it into the coverages of the row, as the sweep does: the coverages are the same, bit for bit. The difference row is
    /// all zeros again when it returns.
    /// </summary>
    /// <param name="pieces">The pieces of the row.</param>
    /// <param name="accumulation">A difference row of <c>width + 2</c> zeros.</param>
    /// <param name="coverage">The coverages of the row, of <paramref name="width"/> values.</param>
    /// <param name="width">The width of the frame.</param>
    /// <param name="x">The first covered column.</param>
    /// <param name="count">The number of columns up to the last covered one.</param>
    /// <returns><see langword="false"/> when no pixel of the row is covered.</returns>
    internal static bool ReplayPieces(ReadOnlySpan<double> pieces, Span<double> accumulation, Span<uint> coverage, int width, out int x, out int count)
    {
        var dirtyMin = int.MaxValue;
        var dirtyMax = -1;
        for (var i = 0; i + ValuesPerPiece <= pieces.Length; i += ValuesPerPiece)
        {
            Accumulate(accumulation, width, pieces[i], pieces[i + 1], pieces[i + 2], ref dirtyMin, ref dirtyMax);
        }

        return FlushRow(accumulation, coverage, width, dirtyMin, dirtyMax, out x, out count);
    }

    /// <summary>
    /// Records a piece of boundary going from <paramref name="x0"/> to <paramref name="x1"/> over a height: every point to
    /// its right gains (or loses, for a negative height) that height of area.
    /// </summary>
    private void AddBoundary(ref SweepSpans s, double x0, double x1, double signedHeight)
    {
        if (_recordsRow && TryRecordPiece(ref s, x0, x1, signedHeight))
            return;

        Accumulate(s.Accumulation, _width, x0, x1, signedHeight, ref _dirtyMin, ref _dirtyMax);
    }

    /// <summary>
    /// Adds a piece of boundary to a difference row (see <see cref="AddBoundary"/>), and widens the range of its entries
    /// that may not be zero: the sweep and the replay of the pieces of a row (<see cref="ReplayPieces"/>) add the same
    /// pieces in the same order with this method, so their sums are the same, bit for bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Accumulate(Span<double> accumulation, int width, double x0, double x1, double signedHeight, ref int dirtyMin, ref int dirtyMax)
    {
        if (x0 > x1)
        {
            (x0, x1) = (x1, x0);
        }

        if (x1 <= 0)
        {
            // Entirely to the left of the frame: the whole row is on its right
            accumulation[0] += signedHeight;
            Touch(0, 0, ref dirtyMin, ref dirtyMax);
            return;
        }

        if (x0 >= width)
            return;

        var span = x1 - x0;
        if (!(span > 0))
        {
            var column = (int)x0;
            var offset = x0 - column;
            accumulation[column] += signedHeight * (1 - offset);
            accumulation[column + 1] += signedHeight * offset;
            Touch(column, column + 1, ref dirtyMin, ref dirtyMax);
            return;
        }

        // The height of boundary per unit of width
        var slope = signedHeight / span;
        var remaining = signedHeight;
        if (x0 < 0)
        {
            var outside = -x0 * slope;
            accumulation[0] += outside;
            Touch(0, 0, ref dirtyMin, ref dirtyMax);
            remaining -= outside;
            x0 = 0;
        }

        var clipped = x1 > width;
        if (clipped)
        {
            x1 = width;
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

        Touch(firstColumn, lastColumn + 1, ref dirtyMin, ref dirtyMax);
    }

    /// <summary>Turns the difference row into the coverage of the row, and clears it.</summary>
    private bool Flush(int row)
    {
        var hasRun = FlushRow(Accumulation, CoverageRow, _width, _dirtyMin, _dirtyMax, out var x, out var count);
        ResetDirtyRange();
        if (!hasRun)
            return false;

        Y = row;
        X = x;
        Count = count;
        return true;
    }

    /// <summary>
    /// Turns the entries of a difference row from <paramref name="dirtyMin"/> to <paramref name="dirtyMax"/> (the others
    /// are zero) into the coverages of the row, and clears them.
    /// </summary>
    /// <returns><see langword="false"/> when no pixel of the row is covered; otherwise, the first covered column and the number of columns up to the last covered one.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool FlushRow(Span<double> accumulation, Span<uint> coverage, int width, int dirtyMin, int dirtyMax, out int x, out int count)
    {
        x = 0;
        count = 0;
        if (dirtyMin > dirtyMax)
            return false;

        var last = Math.Min(dirtyMax, width - 1);
        var firstCovered = -1;
        var lastCovered = -1;
        double area = 0;

        if (last - dirtyMin < MinZeroSkipWidth)
        {
            for (var column = dirtyMin; column <= last; column++)
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
            area = FlushWide(accumulation, coverage, dirtyMin, last, ref firstCovered, ref lastCovered);
        }

        // The entry past the last column only receives what leaves the frame
        accumulation[width] = 0;

        // An edge on the right of the frame was dropped: what is inside after the last boundary stays inside up to the
        // right side of the frame
        var tail = Quantize(area);
        if (tail != 0 && last < width - 1)
        {
            coverage[(last + 1)..width].Fill(tail);
            if (firstCovered < 0)
            {
                firstCovered = last + 1;
            }

            lastCovered = width - 1;
        }

        if (firstCovered < 0)
            return false;

        x = firstCovered;
        count = lastCovered - firstCovered + 1;
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
                var next = IndexOfNonZero(bits[end..(last + 1)]);
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

    /// <summary>
    /// Gets the index of the first value that is not zero, or -1: <see cref="MemoryExtensions.IndexOfAnyExcept{T}(ReadOnlySpan{T}, T)"/>,
    /// with 64 bytes per test. The zeros between two boundaries far apart (the inside of a large stroked shape) are
    /// most of what a row reads.
    /// </summary>
    internal static int IndexOfNonZero(ReadOnlySpan<ulong> values)
    {
        var index = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var first = ref unsafe(MemoryMarshal.GetReference(values));
            for (; index + 8 <= values.Length; index += 8)
            {
                var any = unsafe(Vector128.LoadUnsafe(ref first, (nuint)index) | Vector128.LoadUnsafe(ref first, (nuint)index + 2) | Vector128.LoadUnsafe(ref first, (nuint)index + 4) | Vector128.LoadUnsafe(ref first, (nuint)index + 6));
                if (any != Vector128<ulong>.Zero)
                    break;
            }
        }

        for (; index < values.Length; index++)
        {
            if (values[index] != 0)
                return index;
        }

        return -1;
    }

    private static void Touch(int first, int last, ref int dirtyMin, ref int dirtyMax)
    {
        if (first < dirtyMin)
        {
            dirtyMin = first;
        }

        if (last > dirtyMax)
        {
            dirtyMax = last;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetX(in DrawingEdge edge, double y)
    {
        if (y == edge.Y0)
            return edge.X0;

        if (y == edge.Y1)
            return edge.X1;

        return edge.X0 + ((y - edge.Y0) * edge.Slope);
    }

    /// <summary>The total order of the edges of two slots at a height: position, then direction of travel (the one going more to the left first), then index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBefore(double x, double slope, int slot, double otherX, double otherSlope, int otherSlot, Span<int> slotEdge)
    {
        if (x != otherX)
            return x < otherX;

        if (slope != otherSlope)
            return slope < otherSlope;

        return slotEdge[slot] < slotEdge[otherSlot];
    }
}
