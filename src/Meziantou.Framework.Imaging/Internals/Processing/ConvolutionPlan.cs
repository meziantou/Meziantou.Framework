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
/// A separable plan (<see cref="RowWeights"/>: the Gaussian blur, the unsharp mask, and the matrices that
/// <see cref="ConvolutionKernel.IsSeparable"/>) has a kernel of one column: every row it loads is first filtered by the
/// row weights, from one padded working row, so the ring and the halos hold horizontally filtered rows and the column of
/// weights combines them. Nothing is rounded between the two directions. A separable matrix takes this path when it saves
/// taps: both of its factors have at least two weights that are not zero (or it has no weight-by-weight form).
/// </para>
/// <para>
/// An unsharp mask (<see cref="SharpenAmount"/>) is a blur plan whose blurred row is combined with the original row
/// before it is stored: <c>o + amount (o - b)</c> for every working sample, or <c>o</c> when <c>|o - b|</c> is below the
/// threshold.
/// </para>
/// </remarks>
internal sealed class ConvolutionPlan : IDisposable
{
    private readonly double[] _weights;
    private readonly int _kernelWidth;
    private readonly int _kernelHeight;
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
    /// <param name="rowWeights">
    /// The weights every row is filtered by before the kernel combines the rows (an odd number), or <see langword="null"/>.
    /// They make a blur, whose weights add up to 1 (<see cref="IsBlur"/>). Without them, a separable kernel provides its own.
    /// </param>
    /// <param name="bias">The value added to every color sum, a fraction of the range of a color (<see cref="ConvolutionOptions.Bias"/>).</param>
    public ConvolutionPlan(ConvolutionKernel kernel, ConvolutionEdgeMode edgeMode, bool preserveAlpha, bool linear, PixelFormat format, Size size, int maxDegreeOfParallelism = 1, byte transfer = CicpTransfer.Srgb, double[]? rowWeights = null, double bias = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        if (rowWeights is not null && (kernel.Width != 1 || rowWeights.Length % 2 != 1))
            throw new ArgumentException("A separable plan has a kernel of one column and an odd number of row weights.", nameof(rowWeights));

        if (rowWeights is not null)
        {
            _weights = kernel.GetValues();
            _kernelWidth = kernel.Width;
            _kernelHeight = kernel.Height;
            _rowWeights = rowWeights;
            IsBlur = true;
        }
        else if (kernel.IsSeparable && (!kernel.HasValues || (CountNonZero(kernel.RowWeights) >= 2 && CountNonZero(kernel.ColumnWeights) >= 2)))
        {
            _weights = kernel.ColumnWeights.ToArray();
            _kernelWidth = 1;
            _kernelHeight = kernel.Height;
            _rowWeights = kernel.RowWeights.ToArray();
        }
        else
        {
            _weights = kernel.GetValues();
            _kernelWidth = kernel.Width;
            _kernelHeight = kernel.Height;
        }

        EdgeMode = edgeMode;
        IsLinear = linear;
        Transfer = linear ? transfer : (byte)0;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        Alpha = !PixelFormats.HasAlpha(format) ? WorkingAlpha.None : preserveAlpha ? WorkingAlpha.Preserved : WorkingAlpha.Premultiplied;
        MaxValue = WorkingSamples.GetMaxValue(format);
        Workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
        KernelCount = 1;
        Bias = bias * (linear ? 1 : MaxValue);
    }

    private ConvolutionPlan(ConvolutionKernel[] kernels, KernelCombination combination, ConvolutionEdgeMode edgeMode, PixelFormat format, Size size, int maxDegreeOfParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        _kernelWidth = kernels[0].Width;
        _kernelHeight = kernels[0].Height;
        var area = _kernelWidth * _kernelHeight;
        _weights = new double[kernels.Length * area];
        for (var k = 0; k < kernels.Length; k++)
        {
            if (kernels[k].Width != _kernelWidth || kernels[k].Height != _kernelHeight)
                throw new ArgumentException("The kernels of a plan have the same dimensions.", nameof(kernels));

            kernels[k].GetValues().CopyTo(_weights, k * area);
        }

        EdgeMode = edgeMode;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        Alpha = PixelFormats.HasAlpha(format) ? WorkingAlpha.Preserved : WorkingAlpha.None;
        MaxValue = WorkingSamples.GetMaxValue(format);
        Workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
        KernelCount = kernels.Length;
        Combination = combination;
    }

    /// <summary>
    /// Gets the number of kernels whose sums <see cref="Combination"/> combines into every output sample (1 for a plain
    /// convolution). They have the same dimensions, and <see cref="GetKernelWeights"/> gives their weights.
    /// </summary>
    public int KernelCount { get; }

    /// <summary>Gets how the sums of the kernels make an output sample.</summary>
    public KernelCombination Combination { get; }

    /// <summary>
    /// Gets the value added to every color working sample before it is stored (premultiplied by the filtered alpha when
    /// the colors are), in working units: the bias of the options times the range of a color.
    /// </summary>
    public double Bias { get; }

    /// <summary>
    /// Creates the plan of an edge detector: colors filtered as stored, alpha kept, and the sums of the kernels of the
    /// operator combined into a magnitude (two gradients), a maximum (a compass) or an absolute value (a Laplacian).
    /// </summary>
    public static ConvolutionPlan CreateEdgeDetection(EdgeDetector detector, ConvolutionEdgeMode edgeMode, PixelFormat format, Size size, int maxDegreeOfParallelism = 1)
    {
        (ConvolutionKernel[] Kernels, KernelCombination Combination) definition = detector switch
        {
            EdgeDetector.Sobel => ([ConvolutionKernel.SobelX, ConvolutionKernel.SobelY], KernelCombination.Magnitude),
            EdgeDetector.Scharr => ([ConvolutionKernel.ScharrX, ConvolutionKernel.ScharrY], KernelCombination.Magnitude),
            EdgeDetector.Prewitt => ([ConvolutionKernel.PrewittX, ConvolutionKernel.PrewittY], KernelCombination.Magnitude),
            EdgeDetector.Kirsch => (CreateCompass([5, 5, 5, -3, -3, -3, -3, -3], 8), KernelCombination.Maximum),
            EdgeDetector.Robinson => (CreateCompass([-1, 0, 1, 2, 1, 0, -1, -2], 4), KernelCombination.MaximumAbsolute),
            EdgeDetector.Laplacian3x3 => ([ConvolutionKernel.Laplacian3x3], KernelCombination.MaximumAbsolute),
            EdgeDetector.Laplacian5x5 => ([ConvolutionKernel.Laplacian5x5], KernelCombination.MaximumAbsolute),
            EdgeDetector.LaplacianOfGaussian => ([ConvolutionKernel.LaplacianOfGaussian], KernelCombination.MaximumAbsolute),
            _ => throw new ArgumentOutOfRangeException(nameof(detector), detector, "The edge detector is not valid."),
        };

        return new ConvolutionPlan(definition.Kernels, definition.Combination, edgeMode, format, size, maxDegreeOfParallelism);
    }

    /// <summary>
    /// Creates the first <paramref name="count"/> rotations by 45 degrees of a 3x3 compass matrix whose middle weight is 0,
    /// given by its eight outer weights clockwise from the top-left one: each rotation moves them one place clockwise.
    /// </summary>
    internal static ConvolutionKernel[] CreateCompass(ReadOnlySpan<double> ring, int count)
    {
        // The positions in a 3x3 matrix of the outer weights, clockwise from the top-left one
        ReadOnlySpan<int> positions = [0, 1, 2, 5, 8, 7, 6, 3];
        var kernels = new ConvolutionKernel[count];
        Span<double> values = stackalloc double[9];
        for (var rotation = 0; rotation < count; rotation++)
        {
            values.Clear();
            for (var i = 0; i < positions.Length; i++)
            {
                values[positions[(i + rotation) % positions.Length]] = ring[i];
            }

            kernels[rotation] = new ConvolutionKernel(3, 3, values);
        }

        return kernels;
    }

    /// <summary>Gets the weights of kernel <paramref name="index"/>, row by row (the column of a separable plan).</summary>
    public ReadOnlySpan<double> GetKernelWeights(int index) => _weights.AsSpan(index * _kernelWidth * _kernelHeight, _kernelWidth * _kernelHeight);

    /// <summary>Gets a value indicating whether every row is filtered by <see cref="RowWeights"/> before the kernel combines the rows.</summary>
    public bool IsSeparable => _rowWeights is not null;

    /// <summary>
    /// Gets a value indicating whether the plan is a blur: separable, with weights that add up to 1, so that an opaque frame
    /// can be filtered as its colors alone (<see cref="Convolver.GetWorkingAlpha"/>).
    /// </summary>
    public bool IsBlur { get; }

    /// <summary>
    /// Gets the amount of an unsharp mask: when positive, every blurred working sample <c>b</c> becomes
    /// <c>o + amount (o - b)</c>, <c>o</c> being the original working sample. Requires a blur plan.
    /// </summary>
    public double SharpenAmount
    {
        get;
        init
        {
            if (!IsBlur)
                throw new InvalidOperationException("Only a blur plan can sharpen.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the threshold of an unsharp mask, a fraction of the range of a working sample: a sample whose difference from
    /// the blurred one is smaller keeps its original value.
    /// </summary>
    public double SharpenThreshold { get; init; }

    /// <summary>Gets a value indicating whether the plan is an unsharp mask (<see cref="SharpenAmount"/>).</summary>
    public bool IsSharpen => SharpenAmount > 0;

    /// <summary>Gets the weights every row is filtered by, from left to right (separable plans), or an empty span.</summary>
    public ReadOnlySpan<double> RowWeights => _rowWeights;

    /// <summary>Gets the column of the row weight applied to the pixel itself (separable plans).</summary>
    public int RowCenterX => (_rowWeights?.Length ?? 0) / 2;

    public int KernelWidth => _kernelWidth;

    public int KernelHeight => _kernelHeight;

    /// <summary>Gets the column of the weight applied to the pixel itself.</summary>
    public int CenterX => _kernelWidth / 2;

    /// <summary>Gets the row of the weight applied to the pixel itself.</summary>
    public int CenterY => _kernelHeight / 2;

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
            _scratch[worker] = ConvolutionScratch.Create(scope, paddedLength, rowLength, KernelHeight, CenterY, inputLength, KernelCount);
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

    private static int CountNonZero(ReadOnlySpan<double> values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (value != 0)
            {
                count++;
            }
        }

        return count;
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
