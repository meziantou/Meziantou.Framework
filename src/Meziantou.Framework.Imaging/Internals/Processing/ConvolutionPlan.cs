namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The prepared state of a convolution: the kernel, the edge rule, the working-sample layout of
/// the pixel format, the row bands and the scratch of each worker. One plan serves every frame of an image, since they
/// share the canvas size and the pixel format.
/// </summary>
/// <remarks>
/// <para>
/// A worker needs the ring of the rows of its band (as many as the kernel has rows), the rows it reads above and below its
/// band (the halos, loaded before any row is rewritten) and one accumulator row. All of it is rented up front, charged to
/// the scope of the image, before any pixel changes.
/// </para>
/// <para>
/// A separable plan (<see cref="RowWeights"/>, the Gaussian blur) has a kernel of one column: every row it loads is first
/// filtered by the row weights, from one padded working row, so the ring and the halos hold horizontally filtered rows
/// and the column of weights combines them. Nothing is rounded between the two directions.
/// </para>
/// </remarks>
internal sealed class ConvolutionPlan : IDisposable
{
    private readonly ConvolutionKernel _kernel;
    private readonly double[]? _rowWeights;
    private ConvolutionScratch?[] _scratch = [];

    /// <summary>Initializes a new instance of the <see cref="ConvolutionPlan"/> class.</summary>
    /// <param name="kernel">The kernel; a single column when <paramref name="rowWeights"/> is set.</param>
    /// <param name="edgeMode">The pixels read outside the image.</param>
    /// <param name="preserveAlpha">Whether colors are filtered straight and alpha kept.</param>
    /// <param name="linear">Whether samples are filtered in linear light.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="size">The size of the frames.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers.</param>
    /// <param name="transfer">The transfer characteristics of linear-light filtering.</param>
    /// <param name="rowWeights">The weights every row is filtered by before the kernel combines the rows (an odd number), or <see langword="null"/>.</param>
    public ConvolutionPlan(ConvolutionKernel kernel, ConvolutionEdgeMode edgeMode, bool preserveAlpha, bool linear, PixelFormat format, Size size, int maxDegreeOfParallelism = 1, byte transfer = CicpTransfer.Srgb, double[]? rowWeights = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        if (rowWeights is not null && (kernel.Width != 1 || rowWeights.Length % 2 != 1))
            throw new ArgumentException("A separable plan has a kernel of one column and an odd number of row weights.", nameof(rowWeights));

        _kernel = kernel;
        _rowWeights = rowWeights;
        EdgeMode = edgeMode;
        IsLinear = linear;
        Transfer = linear ? transfer : (byte)0;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        Alpha = !PixelFormats.HasAlpha(format) ? WorkingAlpha.None : preserveAlpha ? WorkingAlpha.Preserved : WorkingAlpha.Premultiplied;
        MaxValue = WorkingSamples.GetMaxValue(format);
        Workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
    }

    /// <summary>Gets the weights, row by row.</summary>
    public ReadOnlySpan<double> Weights => _kernel.Values;

    /// <summary>Gets a value indicating whether every row is filtered by <see cref="RowWeights"/> before the kernel combines the rows.</summary>
    public bool IsSeparable => _rowWeights is not null;

    /// <summary>Gets the weights every row is filtered by, from left to right (separable plans), or an empty span.</summary>
    public ReadOnlySpan<double> RowWeights => _rowWeights;

    /// <summary>Gets the column of the row weight applied to the pixel itself (separable plans).</summary>
    public int RowCenterX => (_rowWeights?.Length ?? 0) / 2;

    public int KernelWidth => _kernel.Width;

    public int KernelHeight => _kernel.Height;

    /// <summary>Gets the column of the weight applied to the pixel itself.</summary>
    public int CenterX => _kernel.Width / 2;

    /// <summary>Gets the row of the weight applied to the pixel itself.</summary>
    public int CenterY => _kernel.Height / 2;

    public ConvolutionEdgeMode EdgeMode { get; }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets a value indicating whether samples are filtered in linear light.</summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Gets the transfer characteristics (<see cref="CicpTransfer"/>) whose function decodes the samples to the linear
    /// light they are filtered in, or 0 when they are filtered as they are stored.
    /// </summary>
    public byte Transfer { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets how alpha takes part in the working samples.</summary>
    public WorkingAlpha Alpha { get; }

    /// <summary>Gets the maximum sample value: 255 or 65,535, or 1 for floating-point samples (<see cref="WorkingSamples.GetMaxValue"/>).</summary>
    public int MaxValue { get; }

    /// <summary>Gets the number of workers (and row bands) of each frame (<see cref="RowBands.GetWorkerCount"/>).</summary>
    public int Workers { get; }

    /// <summary>Gets the number of bytes of scratch storage rented by <see cref="Prepare"/> (pool capacity, as charged).</summary>
    public long ScratchBytes { get; private set; }

    /// <summary>Rents the scratch of every worker. Called once per operation, before any pixel changes.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit; the plan must still be disposed.</exception>
    public void Prepare(AllocationScope scope)
    {
        var before = scope.LiveBytes;
        var rowLength = (long)Size.Width * Channels;
        var paddedLength = ((long)Size.Width + KernelWidth - 1) * Channels;

        // A separable plan loads each row padded for its row weights, then filters it into a ring or halo row
        var inputLength = _rowWeights is null ? 0 : ((long)Size.Width + _rowWeights.Length - 1) * Channels;
        _scratch = new ConvolutionScratch?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _scratch[worker] = ConvolutionScratch.Create(scope, paddedLength, rowLength, KernelHeight, CenterY, inputLength);
        }

        ScratchBytes = scope.LiveBytes - before;
    }

    /// <summary>Gets the row buffers of a worker, between 0 and <see cref="Workers"/> - 1.</summary>
    public ConvolutionScratch GetScratch(int worker) => _scratch[worker]!;

    /// <summary>Gets the first row of a band (the bands split the rows into <see cref="Workers"/> contiguous ranges).</summary>
    public int GetBandStart(int band) => RowBands.GetBandStart(Size.Height, band, Workers);

    /// <summary>Gets the image row read for row <paramref name="y"/>, which may be outside the image, or -1 when it contributes nothing.</summary>
    public int MapRow(int y) => MapIndex(y, Size.Height, EdgeMode);

    /// <summary>Gets the image column read for column <paramref name="x"/>, which may be outside the image, or -1 when it contributes nothing.</summary>
    public int MapColumn(int x) => MapIndex(x, Size.Width, EdgeMode);

    public void Dispose()
    {
        foreach (var scratch in _scratch)
        {
            scratch?.Dispose();
        }

        _scratch = [];
    }

    /// <summary>
    /// Maps an index of an axis of <paramref name="length"/> pixels to the index read for it: itself inside the image; else
    /// the nearest edge (<see cref="ConvolutionEdgeMode.Clamp"/>), the reflection about the border with a period of twice the
    /// length (<see cref="ConvolutionEdgeMode.Mirror"/>), the index modulo the length (<see cref="ConvolutionEdgeMode.Wrap"/>),
    /// or -1 (<see cref="ConvolutionEdgeMode.Zero"/>).
    /// </summary>
    internal static int MapIndex(int index, int length, ConvolutionEdgeMode mode)
    {
        if ((uint)index < (uint)length)
            return index;

        switch (mode)
        {
            case ConvolutionEdgeMode.Clamp:
                return index < 0 ? 0 : length - 1;

            case ConvolutionEdgeMode.Mirror:
            {
                var period = 2L * length;
                var position = index % period;
                if (position < 0)
                {
                    position += period;
                }

                return (int)(position < length ? position : period - 1 - position);
            }

            case ConvolutionEdgeMode.Wrap:
            {
                var position = index % length;
                return position < 0 ? position + length : position;
            }

            default:
                return -1;
        }
    }
}
