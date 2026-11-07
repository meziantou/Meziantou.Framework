namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The buffers of the block encoding process that the decoding process does not have: the planes of the frame to encode,
/// the decisions of the superblock being searched, and what the search saves to compare candidates. Rented once by the
/// encoder and reused by every tile and every frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Records.</b> The search writes its decisions to <see cref="Records"/> in coding order: one value per node of the
/// partition tree (the partition), and one record per block (its mode info and the quantized coefficients of its
/// transform blocks). Replaying the records of a region writes its syntax elements and leaves the block-level state as a
/// decoder has it after the region, so a candidate that the search does not keep costs a rollback of its records only.
/// </para>
/// <para>
/// <b>Saved state.</b> A level is the size of a square of the partition tree, from 4x4 (0) to 128x128 (5). For each level,
/// the search of a partition node and the search of a block each have a snapshot of the contexts that candidates
/// overwrite, and a copy of the reconstructed samples of their best candidate. The search of a block of an inter frame has
/// one more copy of samples: the best block so far, while the search of the intra modes uses the other one.
/// </para>
/// </remarks>
internal sealed class Av1EncoderWorkspace : IDisposable
{
    /// <summary>The number of sizes of a square of the partition tree.</summary>
    public const int Levels = 6;

    /// <summary>
    /// The number of bytes of a context snapshot: for each plane, 4 lines of 33 contexts and 33 x 33 block flags; then the
    /// palettes (8 colors of 2 bytes) of the luma plane and of the U plane above and to the left of 32 blocks.
    /// </summary>
    public const int ContextSnapshotLength = (3 * ((4 * 33) + (33 * 33))) + (2 * 2 * 32 * Av1Constants.PaletteColors * sizeof(ushort));

    private const int InitialRecordCapacity = 1 << 16;
    private const int PixelSnapshotKinds = 3;

    private readonly AllocationScope _scope;
    private readonly Av1Plane?[] _source = new Av1Plane?[3];
    private readonly Av1Plane?[] _saved = new Av1Plane?[3];
    private readonly Av1PooledArray<byte> _contexts;
    private readonly Av1PooledArray<ushort> _pixels;
    private readonly Av1PooledArray<int> _coefficients;
    private Av1PooledArray<int>? _records;
    private Av1PooledArray<int>? _tape;
    private int _tapeLength;
    private Av1PooledArray<ushort>? _restored;
    private Av1PooledArray<long>? _statistics;
    private Av1PooledArray<int>? _filtered;
    private Av1PooledArray<int>? _copyHeads;
    private Av1PooledArray<int>? _copyNext;
    private readonly byte[] _copyCells = new byte[16 * 16];
    private readonly bool _ownsSource = true;

    /// <summary>
    /// Rents the buffers of the search of a worker that encodes tiles of the frames of <paramref name="shared"/> at the same
    /// time as it: the source planes are the ones of <paramref name="shared"/>, which the workers only read; the search
    /// state, the records and the tape are its own.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public Av1EncoderWorkspace(AllocationScope scope, Av1EncoderWorkspace shared)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(shared);
        _scope = scope;
        _ownsSource = false;
        shared._source.CopyTo(_source, 0);
        try
        {
            _contexts = Av1PooledArray<byte>.Rent(scope, (1 + (2 * Levels)) * ContextSnapshotLength, clear: false, AllocationKind.Temporary);
            _pixels = Av1PooledArray<ushort>.Rent(scope, PixelSnapshotKinds * GetPixelOffset(Levels), clear: false, AllocationKind.Temporary);
            _coefficients = Av1PooledArray<int>.Rent(scope, 2 * 64 * 64, clear: false, AllocationKind.Temporary);
            _records = Av1PooledArray<int>.Rent(scope, InitialRecordCapacity, clear: false, AllocationKind.Temporary);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Rents the buffers for frames of <paramref name="width"/> x <paramref name="height"/> samples.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="sequence">The sequence header of the frames.</param>
    /// <param name="width">The width of the frames.</param>
    /// <param name="height">The height of the frames.</param>
    /// <param name="searchesFilters">Whether the parameters of the in-loop filters are searched, which needs a copy of the frame.</param>
    public Av1EncoderWorkspace(AllocationScope scope, Av1SequenceHeader sequence, int width, int height, bool searchesFilters)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        try
        {
            // The same geometry as the planes of the frame being reconstructed
            var alignment = sequence.Use128x128Superblock ? 128 : 64;
            for (var plane = 0; plane < sequence.PlaneCount; plane++)
            {
                var subX = plane > 0 ? sequence.SubsamplingX : 0;
                var subY = plane > 0 ? sequence.SubsamplingY : 0;
                _source[plane] = Av1Plane.Create(scope, (width + subX) >> subX, (height + subY) >> subY, alignment, AllocationKind.Temporary);
                if (searchesFilters)
                    _saved[plane] = Av1Plane.Create(scope, (width + subX) >> subX, (height + subY) >> subY, alignment, AllocationKind.Temporary);
            }

            // The superblock entry snapshot, then a partition and a block snapshot per level
            _contexts = Av1PooledArray<byte>.Rent(scope, (1 + (2 * Levels)) * ContextSnapshotLength, clear: false, AllocationKind.Temporary);
            _pixels = Av1PooledArray<ushort>.Rent(scope, PixelSnapshotKinds * GetPixelOffset(Levels), clear: false, AllocationKind.Temporary);
            _coefficients = Av1PooledArray<int>.Rent(scope, 2 * 64 * 64, clear: false, AllocationKind.Temporary);
            _records = Av1PooledArray<int>.Rent(scope, InitialRecordCapacity, clear: false, AllocationKind.Temporary);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the quantizer index of the frame after the block-level deltas of the tile being encoded (<c>CurrentQIndex</c>).</summary>
    public int CurrentQIndex { get; set; }

    /// <summary>
    /// Gets the number of values of the tape: the syntax elements of the frame being encoded, in coding order, when they
    /// are written after the frame is filtered (a frame with loop restoration codes the parameters of its units with its
    /// superblocks, and chooses them last).
    /// </summary>
    public int TapeLength => _tapeLength;

    /// <summary>Gets the values of the tape.</summary>
    public ReadOnlySpan<int> Tape => _tape is null ? default : _tape.Span[.._tapeLength];

    /// <summary>Empties the tape.</summary>
    public void ResetTape() => _tapeLength = 0;

    /// <summary>Appends a value to the tape.</summary>
    /// <exception cref="ImageResourceLimitException">The tape exceeds the live-allocation limit.</exception>
    public void AppendTape(int value)
    {
        if (_tape is null || _tapeLength == _tape.Length)
        {
            var larger = Av1PooledArray<int>.Rent(_scope, Math.Max(InitialRecordCapacity, 2L * _tapeLength), clear: false, AllocationKind.Temporary);
            if (_tape is not null)
            {
                _tape.Span[.._tapeLength].CopyTo(larger.Span);
                _tape.Dispose();
            }

            _tape = larger;
        }

        _tape.Span[_tapeLength++] = value;
    }

    /// <summary>Gets the samples of a block that a candidate filter restores: 64 rows of 64 samples at most.</summary>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the live-allocation limit.</exception>
    public Span<ushort> GetRestoredBlock()
    {
        _restored ??= Av1PooledArray<ushort>.Rent(_scope, Av1LoopRestoration.MaxBlockWidth * Av1LoopRestoration.MaxBlockHeight, clear: false, AllocationKind.Temporary);
        return _restored.Span;
    }

    /// <summary>Gets <paramref name="count"/> sums of the search of the loop restoration filters.</summary>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the live-allocation limit.</exception>
    public Span<long> GetRestorationStatistics(int count)
    {
        if (_statistics is null || _statistics.Length < count)
        {
            _statistics?.Dispose();
            _statistics = null;
            _statistics = Av1PooledArray<long>.Rent(_scope, count, clear: false, AllocationKind.Temporary);
        }

        return _statistics.Span[..count];
    }

    /// <summary>Gets the rows of a stripe that the search of the loop restoration filters filters across: <paramref name="count"/> values.</summary>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the live-allocation limit.</exception>
    public Span<int> GetFilteredRows(int count)
    {
        if (_filtered is null || _filtered.Length < count)
        {
            _filtered?.Dispose();
            _filtered = null;
            _filtered = Av1PooledArray<int>.Rent(_scope, count, clear: false, AllocationKind.Temporary);
        }

        return _filtered.Span[..count];
    }

    /// <summary>The number of lists of the table of the blocks that intra block copy may copy.</summary>
    public const int BlockCopyBuckets = 1 << 16;

    /// <summary>
    /// Gets the number of superblocks of the tile being encoded, in raster order, whose blocks are in the table of the
    /// blocks that intra block copy may copy.
    /// </summary>
    public int BlockCopySuperblocks { get; set; }

    /// <summary>
    /// Gets the heads of the lists of the table of the blocks that intra block copy may copy: for each value of the hash
    /// of a block of 8x8 luma samples, the position of the last block that was added, or -1.
    /// </summary>
    public Span<int> BlockCopyHeads => _copyHeads!.Span[..BlockCopyBuckets];

    /// <summary>Gets, for the position of each block of the table, the position of the block that was added before it with the same hash, or -1.</summary>
    public Span<int> BlockCopyNext => _copyNext!.Span;

    /// <summary>
    /// Gets what the search knows of each cell of 8x8 luma samples of the superblock being searched, 16 cells per row: 0
    /// for a flat cell or a cell that is not whole in the frame, 1 for a cell that no part of the frame repeats, 2 for a
    /// cell that is the copy of a part of the frame.
    /// </summary>
    public Span<byte> BlockCopyCells => _copyCells;

    /// <summary>
    /// Rents the table of the blocks that intra block copy may copy, for a luma plane of <paramref name="width"/> x
    /// <paramref name="height"/> samples, and empties it: a tile only copies its own blocks.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The table exceeds the live-allocation limit.</exception>
    public void ResetBlockCopy(int width, int height)
    {
        _copyHeads ??= Av1PooledArray<int>.Rent(_scope, BlockCopyBuckets, clear: false, AllocationKind.Temporary);
        _copyNext ??= Av1PooledArray<int>.Rent(_scope, (long)width * height, clear: false, AllocationKind.Temporary);
        BlockCopyHeads.Fill(-1);
        BlockCopySuperblocks = 0;
    }

    /// <summary>Gets the decisions of the superblock being searched.</summary>
    public Span<int> Records => _records!.Span;

    /// <summary>Gets the residual of the transform block being encoded: 64 rows of 64 samples at most.</summary>
    public Span<int> Residual => _coefficients.Span[..(64 * 64)];

    /// <summary>Gets the coefficients of the transform block being encoded, in the layout of <c>Quant</c>.</summary>
    public Span<int> Coefficients => _coefficients.Span.Slice(64 * 64, 64 * 64);

    /// <summary>Gets the snapshot of the contexts at the start of the superblock being searched.</summary>
    public Span<byte> SuperblockContexts => _contexts.Span[..ContextSnapshotLength];

    /// <summary>Gets the plane <paramref name="index"/> of the frame to encode: the caller fills it up to its allocated size.</summary>
    public Av1Plane GetSource(int index) => _source[index] ?? throw new ObjectDisposedException(nameof(Av1EncoderWorkspace));

    /// <summary>
    /// Gets the copy of the plane <paramref name="index"/> of the reconstructed frame that the search of the filter
    /// parameters restores between two candidates.
    /// </summary>
    public Av1Plane GetSavedPlane(int index) => _saved[index] ?? throw new ObjectDisposedException(nameof(Av1EncoderWorkspace));

    /// <summary>Gets the context snapshot of the search of a partition node (<paramref name="block"/> false) or of a block of a level.</summary>
    public Span<byte> GetContextSnapshot(int level, bool block) => _contexts.Span.Slice((1 + (2 * level) + (block ? 1 : 0)) * ContextSnapshotLength, ContextSnapshotLength);

    /// <summary>
    /// Gets the samples saved by the search of a partition node (<paramref name="block"/> false) or of a block of a level:
    /// 3 planes of the size of the level.
    /// </summary>
    public Span<ushort> GetPixelSnapshot(int level, bool block) => GetPixelSnapshot(level, block ? 1 : 0);

    /// <summary>
    /// Gets the samples of the best candidate of the search of a block of a level of an inter frame: 3 planes of the size
    /// of the level.
    /// </summary>
    public Span<ushort> GetCandidatePixels(int level) => GetPixelSnapshot(level, kind: 2);

    private Span<ushort> GetPixelSnapshot(int level, int kind)
    {
        var offset = GetPixelOffset(level);
        var length = GetPixelOffset(level + 1) - offset;
        return _pixels.Span.Slice((PixelSnapshotKinds * offset) + (kind * length), length);
    }

    /// <summary>Makes room for <paramref name="count"/> values from <paramref name="position"/> in <see cref="Records"/>.</summary>
    /// <exception cref="ImageResourceLimitException">The records exceed the live-allocation limit.</exception>
    public void EnsureRecords(int position, int count)
    {
        var required = (long)position + count;
        var records = _records!;
        if (required <= records.Length)
            return;

        var larger = Av1PooledArray<int>.Rent(_scope, Math.Max(required, 2L * records.Length), clear: false, AllocationKind.Temporary);
        records.Span[..position].CopyTo(larger.Span);
        records.Dispose();
        _records = larger;
    }

    public void Dispose()
    {
        for (var i = 0; i < _source.Length; i++)
        {
            if (_ownsSource)
                _source[i]?.Dispose();

            _source[i] = null;
            _saved[i]?.Dispose();
            _saved[i] = null;
        }

        _contexts?.Dispose();
        _pixels?.Dispose();
        _coefficients?.Dispose();
        _records?.Dispose();
        _records = null;
        _tape?.Dispose();
        _tape = null;
        _restored?.Dispose();
        _restored = null;
        _statistics?.Dispose();
        _statistics = null;
        _filtered?.Dispose();
        _filtered = null;
        _copyHeads?.Dispose();
        _copyHeads = null;
        _copyNext?.Dispose();
        _copyNext = null;
    }

    // The samples of the 3 planes of the levels below: a level is a square of 4 << level luma samples
    private static int GetPixelOffset(int level)
    {
        var offset = 0;
        for (var i = 0; i < level; i++)
        {
            offset += 3 * (4 << i) * (4 << i);
        }

        return offset;
    }
}
