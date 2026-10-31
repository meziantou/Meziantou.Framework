using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoding state of one frame component: its sampling geometry (ITU-T T.81 section A.1.1) and the plane that receives
/// its decoded samples.
/// </summary>
/// <remarks>
/// The plane holds whole bands of <c>8 * V</c> sample rows (one MCU row of an interleaved scan, or one block row of a
/// single-component frame) of <c>McusX * H * 8</c> samples. The data unit of a lossless frame is one sample instead of a
/// block of 8 by 8 (T.81 H.1.1): its bands have <c>V</c> rows of <c>McusX * H</c> samples. A frame decoded at a reduced
/// scale transforms each block into <see cref="BlockWidth"/> by <see cref="BlockHeight"/> samples (8, 4, 2 or 1): its bands
/// have <c>BlockHeight * V</c> rows of <c>McusX * H * BlockWidth</c> samples, and <see cref="Width"/> and
/// <see cref="Height"/> are the reduced dimensions of the component. A streaming plane keeps a ring of at most three bands (the band
/// being written, the band waiting for its lower context row, and the band providing the upper context row); a full plane
/// keeps every band (frames coded in several sequential scans). A sample is a byte, or 16 bits in native byte order when
/// the precision of the frame is not 8 bits.
/// <para>
/// Progressive frames also keep the quantized coefficients of every block of the padded block grid (<c>McusX * H</c> by
/// <c>McusY * V</c> blocks, 64 16-bit values each in natural order) until the end of the image: a later scan may refine any
/// coefficient of any block, so progressive reconstruction needs this whole-image state.
/// </para>
/// <para>
/// Lossless frames keep the lines of one MCU row as they are decoded (<see cref="Differences"/>): the differences, then
/// the samples they reconstruct before the point transform is undone, and the last line of the previous MCU row, which
/// predicts the first one.
/// </para>
/// </remarks>
internal sealed class JpegFrameComponent : IDisposable
{
    private PooledBuffer? _plane;
    private int _bandCount;
    private PooledBuffer? _coefficients;
    private int _coefficientBlocksPerLine;
    private PooledBuffer? _differences;

    /// <param name="index">The index of the component in the frame header.</param>
    /// <param name="id">The component identifier.</param>
    /// <param name="horizontalSampling">The horizontal sampling factor.</param>
    /// <param name="verticalSampling">The vertical sampling factor.</param>
    /// <param name="quantizationTable">The quantization table selector.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    /// <param name="dataUnitSize">The number of samples of a side of a data unit: 8 (a block), or 1 for a lossless frame (a sample).</param>
    public JpegFrameComponent(int index, byte id, int horizontalSampling, int verticalSampling, int quantizationTable, int precision, int dataUnitSize = 8)
    {
        BlockWidth = dataUnitSize;
        BlockHeight = dataUnitSize;
        Index = index;
        Id = id;
        Precision = precision;
        DataUnitSize = dataUnitSize;
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

    /// <summary>Gets the number of bits of a sample (8 or 12, or 2 to 16 for a lossless frame).</summary>
    public int Precision { get; }

    /// <summary>Gets a value indicating whether a sample of the plane is 16 bits wide (<see cref="DeepSamples"/>) rather than a byte.</summary>
    public bool HasDeepSamples => Precision != 8;

    /// <summary>Gets the number of samples of a side of a data unit: 8 (a block), or 1 for a lossless frame (a sample).</summary>
    public int DataUnitSize { get; }

    /// <summary>Gets the number of samples of a row of a decoded data unit in the plane: <see cref="DataUnitSize"/>, or fewer when the frame is scaled.</summary>
    public int BlockWidth { get; private set; }

    /// <summary>Gets the number of rows of a decoded data unit in the plane: <see cref="DataUnitSize"/>, or fewer when the frame is scaled.</summary>
    public int BlockHeight { get; private set; }

    /// <summary>Gets a value indicating whether the blocks are transformed into fewer samples than they code (scaled decoding).</summary>
    public bool IsReduced => BlockWidth != DataUnitSize || BlockHeight != DataUnitSize;

    /// <summary>Gets the number of samples per row of the plane: <c>ceil(X * H / Hmax)</c>, divided by the horizontal reduction when the frame is scaled.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the number of sample rows of the plane: <c>ceil(Y * V / Vmax)</c>, divided by the vertical reduction when the frame is scaled.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the number of coded samples per row: <c>ceil(X * H / Hmax)</c> (T.81 A.1.1).</summary>
    public int CodedWidth { get; private set; }

    /// <summary>Gets the number of coded sample rows: <c>ceil(Y * V / Vmax)</c>.</summary>
    public int CodedHeight { get; private set; }

    /// <summary>Gets the horizontal upsampling factor from the plane to the image: <c>Hmax / H</c>, divided by what the scale of the frame takes of it.</summary>
    public int HorizontalRatio { get; private set; }

    /// <summary>Gets the vertical upsampling factor from the plane to the image: <c>Vmax / V</c>, divided by what the scale of the frame takes of it.</summary>
    public int VerticalRatio { get; private set; }

    /// <summary>Gets the number of data units per row of a non-interleaved scan: <c>ceil(Width / 8)</c> blocks, or <c>Width</c> samples for a lossless frame.</summary>
    public int BlocksPerLine => (CodedWidth + DataUnitSize - 1) / DataUnitSize;

    /// <summary>Gets the number of rows of data units of a non-interleaved scan: <c>ceil(Height / 8)</c> block rows, or <c>Height</c> lines for a lossless frame.</summary>
    public int BlocksPerColumn => (CodedHeight + DataUnitSize - 1) / DataUnitSize;

    /// <summary>Gets the distance between two sample rows of the plane, in samples.</summary>
    public int Stride { get; private set; }

    /// <summary>Gets the number of sample rows of one band: <c>BlockHeight * V</c> (<c>8 * V</c> at full scale), or <c>V</c> for a lossless frame.</summary>
    public int BandRows => BlockHeight * VerticalSampling;

    /// <summary>
    /// Gets a value indicating whether every sample of the component was decoded (its sequential scan ended), or, for a
    /// progressive frame, whether its first DC scan ended.
    /// </summary>
    public bool IsDecoded { get; set; }

    /// <summary>Gets or sets the quantization table of a progressive frame, latched at the component's first scan.</summary>
    public ushort[]? LatchedQuantization { get; set; }

    /// <summary>Gets the number of blocks per row of the coefficient grid: <c>McusX * H</c>.</summary>
    public int CoefficientBlocksPerLine => _coefficientBlocksPerLine;

    /// <summary>Gets the samples of the plane (bands in ring order), for a precision of 8 bits.</summary>
    public byte[] Samples => (_plane ?? throw new InvalidOperationException("The JPEG component plane is not allocated.")).RawBuffer;

    /// <summary>Gets the samples of the plane (bands in ring order), for a precision other than 8 bits.</summary>
    public Span<ushort> DeepSamples => unsafe(MemoryMarshal.Cast<byte, ushort>((_plane ?? throw new InvalidOperationException("The JPEG component plane is not allocated.")).RawBuffer.AsSpan()));

    /// <summary>Sets the sampling geometry of the component.</summary>
    /// <param name="frameWidth">The width of the frame.</param>
    /// <param name="frameHeight">The height of the frame.</param>
    /// <param name="maxHorizontal">The largest horizontal sampling factor of the frame.</param>
    /// <param name="maxVertical">The largest vertical sampling factor of the frame.</param>
    /// <param name="scaleDenominator">
    /// The factor by which the image is reduced (scaled decoding): 1, or 2, 4 or 8 for a frame of blocks. A subsampled
    /// component is reduced less, by <c>s / gcd(s, Hmax / H)</c>, so that its upsampling factor becomes
    /// <c>(Hmax / H) / gcd(s, Hmax / H)</c>: the chrominance of a 4:2:0 frame decoded at 1/2 keeps its 8x8 blocks and needs no
    /// upsampling.
    /// </param>
    public void SetGeometry(int frameWidth, int frameHeight, int maxHorizontal, int maxVertical, int scaleDenominator = 1)
    {
        CodedWidth = (int)(((long)frameWidth * HorizontalSampling + maxHorizontal - 1) / maxHorizontal);
        CodedHeight = (int)(((long)frameHeight * VerticalSampling + maxVertical - 1) / maxVertical);
        var horizontalRatio = maxHorizontal / HorizontalSampling;
        var verticalRatio = maxVertical / VerticalSampling;
        // The scale is a power of two: its greatest common divisor with a ratio is the lowest bit of the ratio, at most the scale
        var horizontalShared = Math.Min(scaleDenominator, horizontalRatio & -horizontalRatio);
        var verticalShared = Math.Min(scaleDenominator, verticalRatio & -verticalRatio);
        var horizontalReduction = scaleDenominator / horizontalShared;
        var verticalReduction = scaleDenominator / verticalShared;
        BlockWidth = DataUnitSize / horizontalReduction;
        BlockHeight = DataUnitSize / verticalReduction;

        // The plane of the reduced blocks: ceil(ceil(X * H / Hmax) / d) samples, which the remaining factor upsamples to at
        // least ceil(X / s) samples
        Width = (CodedWidth + horizontalReduction - 1) / horizontalReduction;
        Height = (CodedHeight + verticalReduction - 1) / verticalReduction;
        HorizontalRatio = horizontalRatio / horizontalShared;
        VerticalRatio = verticalRatio / verticalShared;
    }

    /// <summary>Allocates the plane.</summary>
    /// <param name="scope">The scope charged with the plane (decoder state).</param>
    /// <param name="mcusX">The number of MCUs per MCU row of the frame.</param>
    /// <param name="bandCount">The number of bands kept (3 for a streaming ring, every band otherwise).</param>
    /// <param name="limits">The limits (for the overflow report).</param>
    public void AllocatePlane(AllocationScope scope, int mcusX, int bandCount, ImageResourceLimits limits)
    {
        var stride = (long)mcusX * HorizontalSampling * BlockWidth;
        var length = stride * BandRows * bandCount * (HasDeepSamples ? sizeof(ushort) : 1);
        if (stride > int.MaxValue || length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _plane = scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        Stride = (int)stride;
        _bandCount = bandCount;
    }

    /// <summary>Gets the number of block rows of the coefficient grid: every block row of a progressive frame, or the rows of a ring.</summary>
    public int CoefficientBlockRows { get; private set; }

    /// <summary>
    /// Allocates the coefficients of a progressive frame (zero), or a ring of the coefficients of <paramref name="mcusY"/>
    /// MCU rows of a sequential frame whose blocks are transformed after their MCU rows are decoded (block row <c>r</c> is in
    /// row <c>r % CoefficientBlockRows</c> of the ring).
    /// </summary>
    /// <param name="scope">The scope charged with the coefficients (decoder state).</param>
    /// <param name="mcusX">The number of MCUs per MCU row of the frame.</param>
    /// <param name="mcusY">The number of MCU rows of the frame, or of the ring.</param>
    /// <param name="limits">The limits (for the overflow report).</param>
    /// <param name="clear">Whether the coefficients start at zero: a ring is cleared block by block as it is decoded.</param>
    public void AllocateCoefficients(AllocationScope scope, int mcusX, int mcusY, ImageResourceLimits limits, bool clear = true)
    {
        var blocksPerLine = (long)mcusX * HorizontalSampling;
        var length = blocksPerLine * mcusY * VerticalSampling * 64 * sizeof(short);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _coefficients = scope.Rent((int)length, AllocationKind.DecoderState, clear);
        _coefficientBlocksPerLine = (int)blocksPerLine;
        CoefficientBlockRows = mcusY * VerticalSampling;
    }

    /// <summary>
    /// Allocates the lines of one MCU row of a lossless frame, after the plane: <c>V + 1</c> lines of <see cref="Stride"/>
    /// 16-bit values.
    /// </summary>
    /// <param name="scope">The scope charged with the lines (decoder state).</param>
    /// <param name="limits">The limits (for the overflow report).</param>
    public void AllocateDifferences(AllocationScope scope, ImageResourceLimits limits)
    {
        var length = (long)Stride * (VerticalSampling + 1) * sizeof(ushort);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _differences = scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
    }

    /// <summary>
    /// Gets the lines of the MCU row of a lossless frame that is being decoded, <see cref="Stride"/> values each: the last
    /// line of the previous MCU row first, then the <c>V</c> lines of this one.
    /// </summary>
    public Span<ushort> Differences => unsafe(MemoryMarshal.Cast<byte, ushort>((_differences ?? throw new InvalidOperationException("The JPEG component differences are not allocated.")).RawBuffer.AsSpan()));

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

    /// <summary>Gets the offset of the first sample of a block in <see cref="Samples"/> or <see cref="DeepSamples"/>.</summary>
    /// <param name="blockRow">The block row in the component's block grid.</param>
    /// <param name="blockColumn">The block column in the component's block grid.</param>
    public int GetBlockOffset(int blockRow, int blockColumn) => GetRowOffset(blockRow * BlockHeight) + (blockColumn * BlockWidth);

    /// <summary>Gets the offset of a sample row in <see cref="Samples"/> or <see cref="DeepSamples"/>.</summary>
    /// <param name="row">The row, in component sample coordinates (rows of one band are contiguous).</param>
    public int GetRowOffset(int row)
    {
        var bandRows = BandRows;
        var band = row / bandRows;
        return (((band % _bandCount) * bandRows) + (row - (band * bandRows))) * Stride;
    }

    /// <summary>Gets a sample row, at most <see cref="Width"/> valid samples.</summary>
    public ReadOnlySpan<byte> GetRow(int row) => Samples.AsSpan(GetRowOffset(row), Width);

    /// <summary>Gets a sample row of a precision other than 8 bits, at most <see cref="Width"/> valid samples.</summary>
    public ReadOnlySpan<ushort> GetDeepRow(int row) => DeepSamples.Slice(GetRowOffset(row), Width);

    /// <summary>Releases the plane, keeping the coefficients of a progressive frame.</summary>
    public void ReleasePlane()
    {
        _plane?.Dispose();
        _plane = null;
    }

    public void Dispose()
    {
        _plane?.Dispose();
        _plane = null;
        _differences?.Dispose();
        _differences = null;
        ReleaseCoefficients();
    }
}
