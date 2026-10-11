using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoding state of one frame component: its sampling geometry (ITU-T T.81 section A.1.1) and the plane that receives
/// its decoded samples.
/// </summary>
/// <remarks>
/// The plane holds whole bands of <c>8 * V</c> sample rows (one MCU row of an interleaved scan, or one block row of a
/// single-component frame) of <c>McusX * H * 8</c> samples. A streaming plane keeps a ring of at most three bands (the band
/// being written, the band waiting for its lower context row, and the band providing the upper context row); a full plane
/// keeps every band (frames coded in several sequential scans).
/// <para>
/// Progressive frames also keep the quantized coefficients of every block of the padded block grid (<c>McusX * H</c> by
/// <c>McusY * V</c> blocks, 64 16-bit values each in natural order) until the end of the image: a later scan may refine any
/// coefficient of any block, so progressive reconstruction needs this whole-image state.
/// </para>
/// </remarks>
internal sealed class JpegFrameComponent : IDisposable
{
    private PooledBuffer? _plane;
    private int _bandCount;
    private PooledBuffer? _coefficients;
    private int _coefficientBlocksPerLine;

    public JpegFrameComponent(int index, byte id, int horizontalSampling, int verticalSampling, int quantizationTable)
    {
        Index = index;
        Id = id;
        HorizontalSampling = horizontalSampling;
        VerticalSampling = verticalSampling;
        QuantizationTable = quantizationTable;
    }

    public int Index { get; }

    public byte Id { get; }

    /// <summary>Gets the horizontal sampling factor (1 for the only component of a grayscale frame).</summary>
    public int HorizontalSampling { get; }

    /// <summary>Gets the vertical sampling factor (1 for the only component of a grayscale frame).</summary>
    public int VerticalSampling { get; }

    public int QuantizationTable { get; }

    /// <summary>Gets the number of samples per row: <c>ceil(X * H / Hmax)</c>.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the number of sample rows: <c>ceil(Y * V / Vmax)</c>.</summary>
    public int Height { get; private set; }

    /// <summary>Gets <c>Hmax / H</c>: the horizontal upsampling factor.</summary>
    public int HorizontalRatio { get; private set; }

    /// <summary>Gets <c>Vmax / V</c>: the vertical upsampling factor.</summary>
    public int VerticalRatio { get; private set; }

    /// <summary>Gets the number of blocks per row of a non-interleaved scan: <c>ceil(Width / 8)</c>.</summary>
    public int BlocksPerLine => (Width + 7) / 8;

    /// <summary>Gets the number of block rows of a non-interleaved scan: <c>ceil(Height / 8)</c>.</summary>
    public int BlocksPerColumn => (Height + 7) / 8;

    /// <summary>Gets the distance between two sample rows of the plane.</summary>
    public int Stride { get; private set; }

    /// <summary>Gets the number of sample rows of one band: <c>8 * V</c>.</summary>
    public int BandRows => 8 * VerticalSampling;

    /// <summary>
    /// Gets a value indicating whether every sample of the component was decoded (its sequential scan ended), or, for a
    /// progressive frame, whether its first DC scan ended.
    /// </summary>
    public bool IsDecoded { get; set; }

    /// <summary>Gets or sets the quantization table of a progressive frame, latched at the component's first scan.</summary>
    public ushort[]? LatchedQuantization { get; set; }

    /// <summary>Gets the number of blocks per row of the coefficient grid: <c>McusX * H</c>.</summary>
    public int CoefficientBlocksPerLine => _coefficientBlocksPerLine;

    /// <summary>Gets the samples of the plane (bands in ring order).</summary>
    public byte[] Samples => (_plane ?? throw new InvalidOperationException("The JPEG component plane is not allocated.")).RawBuffer;

    public void SetGeometry(int frameWidth, int frameHeight, int maxHorizontal, int maxVertical)
    {
        Width = (int)(((long)frameWidth * HorizontalSampling + maxHorizontal - 1) / maxHorizontal);
        Height = (int)(((long)frameHeight * VerticalSampling + maxVertical - 1) / maxVertical);
        HorizontalRatio = maxHorizontal / HorizontalSampling;
        VerticalRatio = maxVertical / VerticalSampling;
    }

    /// <summary>Allocates the plane.</summary>
    /// <param name="scope">The scope charged with the plane (decoder state).</param>
    /// <param name="mcusX">The number of MCUs per MCU row of the frame.</param>
    /// <param name="bandCount">The number of bands kept (3 for a streaming ring, every band otherwise).</param>
    /// <param name="limits">The limits (for the overflow report).</param>
    public void AllocatePlane(AllocationScope scope, int mcusX, int bandCount, ImageResourceLimits limits)
    {
        var stride = (long)mcusX * HorizontalSampling * 8;
        var length = stride * BandRows * bandCount;
        if (stride > int.MaxValue || length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _plane = scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        Stride = (int)stride;
        _bandCount = bandCount;
    }

    /// <summary>Allocates the coefficients of a progressive frame (zero).</summary>
    /// <param name="scope">The scope charged with the coefficients (decoder state).</param>
    /// <param name="mcusX">The number of MCUs per MCU row of the frame.</param>
    /// <param name="mcusY">The number of MCU rows of the frame.</param>
    /// <param name="limits">The limits (for the overflow report).</param>
    public void AllocateCoefficients(AllocationScope scope, int mcusX, int mcusY, ImageResourceLimits limits)
    {
        var blocksPerLine = (long)mcusX * HorizontalSampling;
        var length = blocksPerLine * mcusY * VerticalSampling * 64 * sizeof(short);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _coefficients = scope.Rent((int)length, AllocationKind.DecoderState, clear: true);
        _coefficientBlocksPerLine = (int)blocksPerLine;
    }

    /// <summary>Gets the 64 coefficients (natural order) of a block of the coefficient grid.</summary>
    /// <param name="blockRow">The block row.</param>
    /// <param name="blockColumn">The block column.</param>
    public Span<short> GetCoefficients(int blockRow, int blockColumn)
    {
        var buffer = (_coefficients ?? throw new InvalidOperationException("The JPEG component coefficients are not allocated.")).RawBuffer;
        var offset = ((blockRow * _coefficientBlocksPerLine) + blockColumn) * 64 * sizeof(short);
        return unsafe(MemoryMarshal.Cast<byte, short>(buffer.AsSpan(offset, 64 * sizeof(short))));
    }

    /// <summary>Releases the coefficients of a progressive frame.</summary>
    public void ReleaseCoefficients()
    {
        _coefficients?.Dispose();
        _coefficients = null;
    }

    /// <summary>Gets the offset of a sample row in <see cref="Samples"/>.</summary>
    /// <param name="row">The row, in component sample coordinates (rows of one band are contiguous).</param>
    public int GetRowOffset(int row)
    {
        var bandRows = BandRows;
        var band = row / bandRows;
        return (((band % _bandCount) * bandRows) + (row - (band * bandRows))) * Stride;
    }

    /// <summary>Gets a sample row, at most <see cref="Width"/> valid samples.</summary>
    public ReadOnlySpan<byte> GetRow(int row) => Samples.AsSpan(GetRowOffset(row), Width);

    public void Dispose()
    {
        _plane?.Dispose();
        _plane = null;
        ReleaseCoefficients();
    }
}
