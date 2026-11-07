using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The row buffers of one convolution worker, in a single rental (see <see cref="ResizeScratch"/>): the ring of the padded
/// working rows of its band, the halo rows it reads above and below its band, the accumulator row (one per kernel of the
/// plan) and, for a separable plan, the padded working row that is filtered into a ring or halo row.
/// </summary>
/// <remarks>
/// The halo has one row per image row that the band reads outside its range (<see cref="ConvolutionHalo"/>): the positions
/// above and below the band that the edge mode maps to the same image row share it, so a kernel taller than the image
/// does not reserve a row per position.
/// </remarks>
internal sealed class ConvolutionScratch : IDisposable
{
    private readonly int _paddedLength;
    private readonly int _rowLength;
    private readonly int _ringSize;
    private readonly ConvolutionHalo _halo;
    private readonly int _inputLength;
    private readonly int _accumulatorCount;
    private PooledBuffer? _buffer;

    private ConvolutionScratch(int paddedLength, int rowLength, int ringSize, ConvolutionHalo halo, int inputLength, int accumulatorCount)
    {
        _accumulatorCount = accumulatorCount;
        _paddedLength = paddedLength;
        _rowLength = rowLength;
        _ringSize = ringSize;
        _halo = halo;
        _inputLength = inputLength;
    }

    /// <summary>Gets the accumulator of one output row (not padded), the one of the first kernel.</summary>
    public Span<double> Accumulator => Buffer.Slice(AccumulatorOffset, _rowLength);

    /// <summary>Gets the position of <see cref="Accumulator"/> in <see cref="Buffer"/>.</summary>
    public int AccumulatorOffset => (_ringSize + _halo.RowCount) * _paddedLength;

    /// <summary>Gets the position of <see cref="InputRow"/> in <see cref="Buffer"/>, after the accumulators.</summary>
    public int InputOffset => AccumulatorOffset + (_accumulatorCount * _rowLength);

    /// <summary>Gets the padded working row that a separable plan filters into a ring or halo row (empty otherwise).</summary>
    public Span<double> InputRow => Buffer.Slice(InputOffset, _inputLength);

    /// <summary>Gets the rows and the accumulator, at the positions that the <c>Get...Offset</c> methods give.</summary>
    public Span<double> Buffer => unsafe(MemoryMarshal.Cast<byte, double>((_buffer ?? throw new ObjectDisposedException(nameof(ConvolutionScratch))).Span));

    /// <summary>Gets the number of ring rows.</summary>
    public int RingSize => _ringSize;

    /// <summary>Gets the rows read outside the band.</summary>
    public ConvolutionHalo Halo => _halo;

    /// <summary>
    /// Gets the number of doubles of the buffers of a worker, or -1 when it overflows.
    /// </summary>
    /// <param name="paddedLength">The number of doubles of a padded working row.</param>
    /// <param name="rowLength">The number of doubles of an output row.</param>
    /// <param name="ringSize">The number of ring rows.</param>
    /// <param name="haloRows">The number of halo rows (<see cref="ConvolutionHalo.RowCount"/>).</param>
    /// <param name="inputLength">The number of doubles of the padded working row of a separable plan, or 0.</param>
    /// <param name="accumulatorCount">The number of accumulator rows (the number of kernels of the plan).</param>
    public static long GetLength(long paddedLength, long rowLength, int ringSize, int haloRows, long inputLength, int accumulatorCount)
    {
        if (!CheckedSizes.TryMultiply(paddedLength, (long)ringSize + haloRows, out var rows) || !CheckedSizes.TryMultiply(rowLength, accumulatorCount, out var accumulators) || !CheckedSizes.TryAdd(rows, accumulators, out var count) || !CheckedSizes.TryAdd(count, inputLength, out var total))
            return -1;

        return total;
    }

    /// <summary>Rents the buffers of a worker; nothing stays charged on failure.</summary>
    /// <param name="scope">The image scope.</param>
    /// <param name="paddedLength">The number of doubles of a padded working row.</param>
    /// <param name="rowLength">The number of doubles of an output row.</param>
    /// <param name="ringSize">The number of ring rows: the kernel height, or the height of the band when it is smaller.</param>
    /// <param name="halo">The rows read outside the band.</param>
    /// <param name="inputLength">The number of doubles of the padded working row of a separable plan, or 0.</param>
    /// <param name="accumulatorCount">The number of accumulator rows (the number of kernels of the plan).</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public static ConvolutionScratch Create(AllocationScope scope, long paddedLength, long rowLength, int ringSize, ConvolutionHalo halo, long inputLength = 0, int accumulatorCount = 1)
    {
        var total = GetLength(paddedLength, rowLength, ringSize, halo.RowCount, inputLength, accumulatorCount);
        if (total < 0)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var buffer = ResampleWeights.Rent<double>(scope, total);
        return new ConvolutionScratch((int)paddedLength, (int)rowLength, ringSize, halo, (int)inputLength, accumulatorCount) { _buffer = buffer };
    }

    /// <summary>Gets the position in <see cref="Buffer"/> of the accumulator of kernel <paramref name="index"/> (0 is <see cref="Accumulator"/>).</summary>
    public int GetAccumulatorOffset(int index) => AccumulatorOffset + (index * _rowLength);

    /// <summary>Gets the position in <see cref="Buffer"/> of the working row of image row <paramref name="y"/>, which is inside the band.</summary>
    public int GetRingRowOffset(int y) => y % _ringSize * _paddedLength;

    /// <summary>Gets the position in <see cref="Buffer"/> of halo row <paramref name="index"/> (<see cref="ConvolutionHalo.GetImageRow"/>).</summary>
    public int GetHaloRowOffset(int index) => (_ringSize + index) * _paddedLength;

    /// <summary>
    /// Gets the position in <see cref="Buffer"/> of a working row read above the band (0 is the farthest one, the halo size
    /// above the first row of the band), or -1 when it contributes nothing.
    /// </summary>
    public int GetTopHaloOffset(int index) => _halo.GetTopRow(index) is var row and >= 0 ? GetHaloRowOffset(row) : -1;

    /// <summary>
    /// Gets the position in <see cref="Buffer"/> of a working row read below the band (0 is the nearest one, right after the
    /// last row of the band), or -1 when it contributes nothing.
    /// </summary>
    public int GetBottomHaloOffset(int index) => _halo.GetBottomRow(index) is var row and >= 0 ? GetHaloRowOffset(row) : -1;

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
