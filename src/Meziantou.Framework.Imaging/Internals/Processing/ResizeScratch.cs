using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The row buffers of one resize worker: the working source row, the extra row and the ring or accumulator rows. The rows
/// share one rental (two in total with the working row): with several workers, one rental per row exceeded the per-size-class
/// retention of the pool, so every parallel resize reallocated its scratch.
/// </summary>
internal sealed class ResizeScratch : IDisposable
{
    private readonly int _rowLength;
    private PooledBuffer? _working;
    private PooledBuffer? _rows;

    private ResizeScratch(int rowLength, int rowCount)
    {
        _rowLength = rowLength;
        RowCount = rowCount;
    }

    /// <summary>Gets the working copy of the used part of one source row, plus one element for 3-sample pixels.</summary>
    public Span<double> SourceRow => unsafe(MemoryMarshal.Cast<byte, double>((_working ?? throw new ObjectDisposedException(nameof(ResizeScratch))).Span));

    /// <summary>Gets the accumulator row (gather) or the horizontally resampled row (scatter).</summary>
    public Span<double> ExtraRow => GetRow(0);

    /// <summary>Gets the number of ring rows (gather) or accumulator rows (scatter).</summary>
    public int RowCount { get; }

    /// <summary>Rents the buffers of a worker; nothing stays charged on failure.</summary>
    /// <param name="scope">The image scope.</param>
    /// <param name="workingLength">The number of doubles of the working row.</param>
    /// <param name="rowLength">The number of doubles of an output row.</param>
    /// <param name="rowCount">The number of ring or accumulator rows.</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public static ResizeScratch Create(AllocationScope scope, long workingLength, long rowLength, int rowCount)
    {
        var scratch = new ResizeScratch((int)rowLength, rowCount);
        try
        {
            scratch._working = ResampleWeights.Rent<double>(scope, workingLength);
            scratch._rows = ResampleWeights.Rent<double>(scope, rowLength * (rowCount + 1));
            return scratch;
        }
        catch
        {
            scratch.Dispose();
            throw;
        }
    }

    /// <summary>Gets ring (gather) or accumulator (scatter) row <paramref name="slot"/>, between 0 and <see cref="RowCount"/> - 1.</summary>
    public Span<double> GetSlot(int slot) => GetRow(slot + 1);

    public void Dispose()
    {
        _working?.Dispose();
        _working = null;
        _rows?.Dispose();
        _rows = null;
    }

    private Span<double> GetRow(int index)
        => unsafe(MemoryMarshal.Cast<byte, double>((_rows ?? throw new ObjectDisposedException(nameof(ResizeScratch))).Span)).Slice(index * _rowLength, _rowLength);
}
