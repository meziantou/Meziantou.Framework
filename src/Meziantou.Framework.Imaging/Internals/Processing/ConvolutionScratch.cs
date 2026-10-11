using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The row buffers of one convolution worker, in a single rental (see <see cref="ResizeScratch"/>): the ring of the padded
/// working rows of its band, the halo rows it reads above and below its band, and the accumulator row.
/// </summary>
internal sealed class ConvolutionScratch : IDisposable
{
    private readonly int _paddedLength;
    private readonly int _rowLength;
    private readonly int _ringSize;
    private readonly int _haloSize;
    private PooledBuffer? _buffer;

    private ConvolutionScratch(int paddedLength, int rowLength, int ringSize, int haloSize)
    {
        _paddedLength = paddedLength;
        _rowLength = rowLength;
        _ringSize = ringSize;
        _haloSize = haloSize;
    }

    /// <summary>Gets the accumulator of one output row (not padded).</summary>
    public Span<double> Accumulator => Buffer.Slice((_ringSize + (2 * _haloSize)) * _paddedLength, _rowLength);

    private Span<double> Buffer => unsafe(MemoryMarshal.Cast<byte, double>((_buffer ?? throw new ObjectDisposedException(nameof(ConvolutionScratch))).Span));

    /// <summary>Rents the buffers of a worker; nothing stays charged on failure.</summary>
    /// <param name="scope">The image scope.</param>
    /// <param name="paddedLength">The number of doubles of a padded working row.</param>
    /// <param name="rowLength">The number of doubles of an output row.</param>
    /// <param name="ringSize">The number of ring rows (the kernel height).</param>
    /// <param name="haloSize">The number of halo rows on each side of the band (the kernel center row).</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public static ConvolutionScratch Create(AllocationScope scope, long paddedLength, long rowLength, int ringSize, int haloSize)
    {
        if (!CheckedSizes.TryMultiply(paddedLength, ringSize + (2L * haloSize), out var rows) || !CheckedSizes.TryAdd(rows, rowLength, out var count))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var buffer = ResampleWeights.Rent<double>(scope, count);
        return new ConvolutionScratch((int)paddedLength, (int)rowLength, ringSize, haloSize) { _buffer = buffer };
    }

    /// <summary>Gets the padded working row of image row <paramref name="y"/>, which is inside the band.</summary>
    public Span<double> GetRingRow(int y) => Buffer.Slice(y % _ringSize * _paddedLength, _paddedLength);

    /// <summary>Gets a padded working row read above the band: 0 is the farthest one, the halo size above the first row of the band.</summary>
    public Span<double> GetTopHalo(int index) => Buffer.Slice((_ringSize + index) * _paddedLength, _paddedLength);

    /// <summary>Gets a padded working row read below the band: 0 is the nearest one, right after the last row of the band.</summary>
    public Span<double> GetBottomHalo(int index) => Buffer.Slice((_ringSize + _haloSize + index) * _paddedLength, _paddedLength);

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
