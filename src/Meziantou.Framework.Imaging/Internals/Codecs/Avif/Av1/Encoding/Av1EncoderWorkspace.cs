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
/// overwrite, and a copy of the reconstructed samples of their best candidate.
/// </para>
/// </remarks>
internal sealed class Av1EncoderWorkspace : IDisposable
{
    /// <summary>The number of sizes of a square of the partition tree.</summary>
    public const int Levels = 6;

    /// <summary>The number of bytes of a context snapshot: for each plane, 4 lines of 33 contexts and 33 x 33 block flags.</summary>
    public const int ContextSnapshotLength = 3 * ((4 * 33) + (33 * 33));

    private const int InitialRecordCapacity = 1 << 16;

    private readonly AllocationScope _scope;
    private readonly Av1Plane?[] _source = new Av1Plane?[3];
    private readonly Av1PooledArray<byte> _contexts;
    private readonly Av1PooledArray<ushort> _pixels;
    private readonly Av1PooledArray<int> _coefficients;
    private Av1PooledArray<int>? _records;

    /// <summary>Rents the buffers for frames of <paramref name="width"/> x <paramref name="height"/> samples.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public Av1EncoderWorkspace(AllocationScope scope, Av1SequenceHeader sequence, int width, int height)
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
            }

            // The superblock entry snapshot, then a partition and a block snapshot per level
            _contexts = Av1PooledArray<byte>.Rent(scope, (1 + (2 * Levels)) * ContextSnapshotLength, clear: false, AllocationKind.Temporary);
            _pixels = Av1PooledArray<ushort>.Rent(scope, 2 * GetPixelOffset(Levels), clear: false, AllocationKind.Temporary);
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

    /// <summary>Gets the context snapshot of the search of a partition node (<paramref name="block"/> false) or of a block of a level.</summary>
    public Span<byte> GetContextSnapshot(int level, bool block) => _contexts.Span.Slice((1 + (2 * level) + (block ? 1 : 0)) * ContextSnapshotLength, ContextSnapshotLength);

    /// <summary>
    /// Gets the samples saved by the search of a partition node (<paramref name="block"/> false) or of a block of a level:
    /// 3 planes of the size of the level.
    /// </summary>
    public Span<ushort> GetPixelSnapshot(int level, bool block)
    {
        var offset = GetPixelOffset(level);
        var length = GetPixelOffset(level + 1) - offset;
        return _pixels.Span.Slice((2 * offset) + (block ? length : 0), length);
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
            _source[i]?.Dispose();
            _source[i] = null;
        }

        _contexts?.Dispose();
        _pixels?.Dispose();
        _coefficients?.Dispose();
        _records?.Dispose();
        _records = null;
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
