namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The prepared state of a convolution: the kernel, the edge rule, the working-sample layout of
/// the pixel format, the row bands and the scratch of each worker. One plan serves every frame of an image, since they
/// share the canvas size and the pixel format.
/// </summary>
/// <remarks>
/// A worker needs the ring of the rows of its band (as many as the kernel has rows), the rows it reads above and below its
/// band (the halos, loaded before any row is rewritten) and one accumulator row. All of it is rented up front, charged to
/// the scope of the image, before any pixel changes.
/// </remarks>
internal sealed class ConvolutionPlan : IDisposable
{
    private readonly ConvolutionKernel _kernel;
    private ConvolutionScratch?[] _scratch = [];

    public ConvolutionPlan(ConvolutionKernel kernel, ConvolutionEdgeMode edgeMode, bool preserveAlpha, bool linear, PixelFormat format, Size size, int maxDegreeOfParallelism = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        _kernel = kernel;
        EdgeMode = edgeMode;
        IsLinear = linear;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        Alpha = !PixelFormats.HasAlpha(format) ? WorkingAlpha.None : preserveAlpha ? WorkingAlpha.Preserved : WorkingAlpha.Premultiplied;
        MaxValue = PixelFormats.GetBitsPerComponent(format) == 16 ? ushort.MaxValue : byte.MaxValue;
        Workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
    }

    /// <summary>Gets the weights, row by row.</summary>
    public ReadOnlySpan<double> Weights => _kernel.Values;

    public int KernelWidth => _kernel.Width;

    public int KernelHeight => _kernel.Height;

    /// <summary>Gets the column of the weight applied to the pixel itself.</summary>
    public int CenterX => _kernel.Width / 2;

    /// <summary>Gets the row of the weight applied to the pixel itself.</summary>
    public int CenterY => _kernel.Height / 2;

    public ConvolutionEdgeMode EdgeMode { get; }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets a value indicating whether samples are filtered in linear light (sRGB transfer function).</summary>
    public bool IsLinear { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets how alpha takes part in the working samples.</summary>
    public WorkingAlpha Alpha { get; }

    /// <summary>Gets the maximum sample value (255 or 65,535).</summary>
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
        _scratch = new ConvolutionScratch?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _scratch[worker] = ConvolutionScratch.Create(scope, paddedLength, rowLength, KernelHeight, CenterY);
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
