using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The row buffers of one convolution worker, in a single rental (see <see cref="ResizeScratch"/>): the ring of the padded
/// working rows of its band, the halo rows it reads above and below its band, the accumulator row and, for a separable
/// plan, the padded working row that is filtered into a ring or halo row.
/// </summary>
internal sealed class ConvolutionScratch : IDisposable
{
    private readonly int _paddedLength;
    private readonly int _rowLength;
    private readonly int _ringSize;
    private readonly int _haloSize;
    private readonly int _inputLength;
    private PooledBuffer? _buffer;

    private ConvolutionScratch(int paddedLength, int rowLength, int ringSize, int haloSize, int inputLength)
    {
        _paddedLength = paddedLength;
        _rowLength = rowLength;
        _ringSize = ringSize;
        _haloSize = haloSize;
        _inputLength = inputLength;
    }

    /// <summary>Gets the accumulator of one output row (not padded).</summary>
    public Span<double> Accumulator => Buffer.Slice(AccumulatorOffset, _rowLength);

    /// <summary>Gets the position of <see cref="Accumulator"/> in <see cref="Buffer"/>.</summary>
    public int AccumulatorOffset => (_ringSize + (2 * _haloSize)) * _paddedLength;

    /// <summary>Gets the position of <see cref="InputRow"/> in <see cref="Buffer"/>, after the accumulator.</summary>
    public int InputOffset => AccumulatorOffset + _rowLength;

    /// <summary>Gets the padded working row that a separable plan filters into a ring or halo row (empty otherwise).</summary>
    public Span<double> InputRow => Buffer.Slice(InputOffset, _inputLength);

    /// <summary>Gets the rows and the accumulator, at the positions that the <c>Get...Offset</c> methods give.</summary>
    public Span<double> Buffer => unsafe(MemoryMarshal.Cast<byte, double>((_buffer ?? throw new ObjectDisposedException(nameof(ConvolutionScratch))).Span));

    /// <summary>Rents the buffers of a worker; nothing stays charged on failure.</summary>
    /// <param name="scope">The image scope.</param>
    /// <param name="paddedLength">The number of doubles of a padded working row.</param>
    /// <param name="rowLength">The number of doubles of an output row.</param>
    /// <param name="ringSize">The number of ring rows (the kernel height).</param>
    /// <param name="haloSize">The number of halo rows on each side of the band (the kernel center row).</param>
    /// <param name="inputLength">The number of doubles of the padded working row of a separable plan, or 0.</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public static ConvolutionScratch Create(AllocationScope scope, long paddedLength, long rowLength, int ringSize, int haloSize, long inputLength = 0)
    {
        if (!CheckedSizes.TryMultiply(paddedLength, ringSize + (2L * haloSize), out var rows) || !CheckedSizes.TryAdd(rows, rowLength, out var count) || !CheckedSizes.TryAdd(count, inputLength, out var total))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var buffer = ResampleWeights.Rent<double>(scope, total);
        return new ConvolutionScratch((int)paddedLength, (int)rowLength, ringSize, haloSize, (int)inputLength) { _buffer = buffer };
    }

    /// <summary>Gets the position in <see cref="Buffer"/> of the working row of image row <paramref name="y"/>, which is inside the band.</summary>
    public int GetRingRowOffset(int y) => y % _ringSize * _paddedLength;

    /// <summary>Gets the position in <see cref="Buffer"/> of a working row read above the band: 0 is the farthest one, the halo size above the first row of the band.</summary>
    public int GetTopHaloOffset(int index) => (_ringSize + index) * _paddedLength;

    /// <summary>Gets the position in <see cref="Buffer"/> of a working row read below the band: 0 is the nearest one, right after the last row of the band.</summary>
    public int GetBottomHaloOffset(int index) => (_ringSize + _haloSize + index) * _paddedLength;

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
