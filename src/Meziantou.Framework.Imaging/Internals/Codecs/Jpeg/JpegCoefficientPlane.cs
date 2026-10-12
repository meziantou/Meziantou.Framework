using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The quantized coefficients of one component of a progressive JPEG being written: every scan codes a part of them, so
/// they are kept for the whole image (16 bits each, in zig-zag order, charged to the scope of the encoder).
/// </summary>
/// <remarks>
/// The grid covers whole MCUs, which is what an interleaved scan codes. A scan of the component alone codes its own block
/// grid instead (ITU-T T.81 section A.2.2): the blocks that hold samples of the image, <see cref="ScanBlocksPerLine"/> by
/// <see cref="ScanBlockRows"/>, the first ones of each line of the grid.
/// </remarks>
internal sealed class JpegCoefficientPlane : IDisposable
{
    private PooledBuffer? _buffer;

    /// <param name="scope">The scope charged with the coefficients.</param>
    /// <param name="blocksPerLine">The blocks per line of the grid (whole MCUs).</param>
    /// <param name="blockRows">The block rows of the grid (whole MCUs).</param>
    /// <param name="scanBlocksPerLine">The blocks per line of a scan of the component alone.</param>
    /// <param name="scanBlockRows">The block rows of a scan of the component alone.</param>
    public JpegCoefficientPlane(AllocationScope scope, int blocksPerLine, int blockRows, int scanBlocksPerLine, int scanBlockRows)
    {
        var length = (long)blocksPerLine * blockRows * 64 * sizeof(short);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        _buffer = scope.Rent((int)length, AllocationKind.Temporary, clear: false);
        BlocksPerLine = blocksPerLine;
        BlockRows = blockRows;
        ScanBlocksPerLine = scanBlocksPerLine;
        ScanBlockRows = scanBlockRows;
    }

    public int BlocksPerLine { get; }

    public int BlockRows { get; }

    public int ScanBlocksPerLine { get; }

    public int ScanBlockRows { get; }

    /// <summary>Gets the 64 coefficients (zig-zag order) of a block of the grid.</summary>
    public Span<short> GetBlock(int blockRow, int blockColumn)
    {
        var buffer = (_buffer ?? throw new ObjectDisposedException(nameof(JpegCoefficientPlane))).RawBuffer;
        var offset = ((blockRow * BlocksPerLine) + blockColumn) * 64 * sizeof(short);
        return unsafe(MemoryMarshal.Cast<byte, short>(buffer.AsSpan(offset, 64 * sizeof(short))));
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
