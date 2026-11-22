using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Wiener deconvolution of
/// <see cref="ImageProcessingExtensions.WienerDeconvolve(Image, WienerDeconvolutionOptions, CancellationToken)"/>: the
/// frame is extended to a padded plane, transformed by a two-dimensional discrete Fourier transform, multiplied by
/// <c>G = conj(H) / (|H|² + K)</c>, where <c>H</c> is the transfer function of the convolution
/// <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/> applies with the kernel,
/// and transformed back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The plane.</b> A circular convolution on a plane of <c>N × M</c> complex numbers models the blur. With
/// <see cref="ConvolutionEdgeMode.Wrap"/>, the plane is the frame itself (<c>N = W</c> when the kernel reaches across
/// columns, <c>M = H</c> across rows): the convolution with wrapped edges is exactly circular, and the result is its exact
/// Wiener inverse. With the other modes, the frame is followed, on each axis, by the <c>r</c> pixels of the edge mode
/// beyond its end (<c>r</c> the radius of the kernel on that axis), a linear ramp, and the <c>r</c> pixels of the edge mode
/// before its start, which the circular plane wraps to the left of the frame: the ramp joins the two ends without a step,
/// whose ringing would otherwise spread from the edges. The ramp is at least <c>max(2 r, 8)</c> long, and the plane is
/// rounded up to a length whose prime factors are 2, 3 and 5 (<see cref="FourierTransform"/>). An axis the kernel does not
/// reach across (<c>r = 0</c>) gets no margin, only that rounding: its pixels are not mixed.
/// </para>
/// <para>
/// <b>The filter.</b> The weight of the kernel at the offset <c>d</c> from its middle multiplies the pixel <c>p + d</c>,
/// so <c>H(u) = Σ_d w_d exp(2πi u·d / N)</c>: the transform of the weights placed at <c>-d</c>, modulo the plane (the
/// weights of a kernel wider than a wrapped frame add up). The transfer of a separable kernel is the product of the
/// transfers of its row and its column, computed from the two one-dimensional transforms for every frequency; that of
/// any other kernel is kept for every frequency in single precision (8 bytes per frequency, transposed so that a column
/// of the plane is a row of the filter). <c>G</c> keeps the symmetry <c>G(-u) = conj(G(u))</c> of a real kernel, so the
/// inverse of a real sample plane is real: two working samples of a pixel are transformed together, as the real and the
/// imaginary parts of the plane.
/// </para>
/// <para>
/// <b>The samples.</b> The working samples of <see cref="WorkingFrameCopy"/> (premultiplied colors and alpha, or the colors
/// of an opaque frame) are deconvolved in double precision and stored back in single precision, clamped to its finite
/// range. A sample that is not finite reads as 0 and keeps its value. The inverse transform is the conjugate of the
/// transform of the conjugate, scaled by <c>1 / (N M)</c>, which the filter includes.
/// </para>
/// </remarks>
internal sealed class WienerDeconvolver : IDisposable
{
    private const int GapColumn = -2;
    private const int ZeroColumn = -1;
    private const int MaxBlockColumns = 16;
    private const int BlockDoubles = 1 << 20;

    private readonly int _width;
    private readonly int _height;
    private readonly int _paddedWidth;
    private readonly int _paddedHeight;
    private readonly int _radiusX;
    private readonly int _radiusY;
    private readonly int _blockColumns;
    private readonly double _noise;
    private readonly double _scale;
    private FourierTransform? _rowTransform;
    private FourierTransform? _columnTransform;
    private PixelStorage? _plane;
    private PixelStorage? _filter;
    private PooledBuffer? _spectra;
    private PooledBuffer? _scratch;
    private PooledBuffer? _maps;

    private WienerDeconvolver(Size size, int paddedWidth, int paddedHeight, int radiusX, int radiusY, double noise)
    {
        _width = size.Width;
        _height = size.Height;
        _paddedWidth = paddedWidth;
        _paddedHeight = paddedHeight;
        _radiusX = radiusX;
        _radiusY = radiusY;
        _noise = noise;
        _scale = 1d / ((double)paddedWidth * paddedHeight);
        _blockColumns = Math.Clamp(BlockDoubles / (2 * paddedHeight), 1, MaxBlockColumns);
    }

    /// <summary>Gets the length of the plane along an axis of <paramref name="length"/> pixels that the kernel reaches across by <paramref name="radius"/> pixels on each side.</summary>
    internal static long GetPaddedLength(int length, int radius, ConvolutionEdgeMode edgeMode)
    {
        if (edgeMode == ConvolutionEdgeMode.Wrap)
            return radius == 0 ? FourierTransform.GetSmoothLength(length) : length;

        var margin = radius == 0 ? 0 : (2L * radius) + Math.Max(2L * radius, 8);
        return FourierTransform.GetSmoothLength(length + margin);
    }

    /// <summary>
    /// Creates the plane, the filter and the scratch of the frames of an operation, charged to the scope of
    /// <paramref name="owner"/>, before any pixel changes.
    /// </summary>
    /// <param name="owner">The owner the plane and the filter are allocated from.</param>
    /// <param name="size">The size of the frames.</param>
    /// <param name="kernel">The kernel of the blur, as given (its middle weight is the pixel itself).</param>
    /// <param name="noise">The noise-to-signal ratio <c>K</c>, positive.</param>
    /// <param name="edgeMode">The pixels the blur read beyond the edges.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <exception cref="ImageResourceLimitException">The plane, the filter or the scratch exceed a limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public static WienerDeconvolver Create(StorageOwner owner, Size size, ConvolutionKernel kernel, double noise, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        var centered = kernel.GetCentered(kernel.Width / 2, kernel.Height / 2, divisor: 1);
        var radiusX = centered.Width / 2;
        var radiusY = centered.Height / 2;
        var paddedWidth = GetPaddedLength(size.Width, radiusX, edgeMode);
        var paddedHeight = GetPaddedLength(size.Height, radiusY, edgeMode);
        // A row of the plane (16 bytes per complex number) and the scratch of a row and a column fit in a buffer
        const long MaxLength = int.MaxValue / (4 * sizeof(double));
        if (paddedWidth > MaxLength || paddedHeight > MaxLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, owner.Scope.Limit, CheckedSizes.TryMultiply(paddedWidth * 2 * sizeof(double), paddedHeight, out var bytes) ? bytes : long.MaxValue);

        var result = new WienerDeconvolver(size, (int)paddedWidth, (int)paddedHeight, radiusX, radiusY, noise);
        try
        {
            result.Prepare(owner, centered, edgeMode, cancellationToken);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Deconvolves a leased frame of the format of the copy in place.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public void Apply(PixelStorage storage, WorkingFrameCopy copy, CancellationToken cancellationToken)
    {
        copy.Load(storage, cancellationToken);
        var channels = copy.WorkingChannels;
        for (var channel = 0; channel < channels; channel += 2)
        {
            var second = channel + 1 < channels ? channel + 1 : -1;
            Fill(copy, channel, second);
            TransformRows(_paddedHeight, cancellationToken);
            FilterColumns(cancellationToken);
            TransformRows(_height, cancellationToken);
            WriteBack(copy, channel, second);
        }

        var values = copy.GetRowBuffer(0)[..(_width * channels)];
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = copy.GetRow(y);
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = row[i];
            }

            copy.Store(storage, y, values);
        }
    }

    public void Dispose()
    {
        _rowTransform?.Dispose();
        _rowTransform = null;
        _columnTransform?.Dispose();
        _columnTransform = null;
        _plane?.Dispose();
        _plane = null;
        _filter?.Dispose();
        _filter = null;
        _spectra?.Dispose();
        _spectra = null;
        _scratch?.Dispose();
        _scratch = null;
        _maps?.Dispose();
        _maps = null;
    }

    private Span<double> RowInput => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_scratch).Span))[..(2 * _paddedWidth)];

    private Span<double> ColumnWork => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_scratch).Span)).Slice(2 * _paddedWidth, 2 * _paddedHeight);

    private Span<double> Block => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_scratch).Span)).Slice((2 * _paddedWidth) + (2 * _paddedHeight), _blockColumns * 2 * _paddedHeight);

    private Span<double> RowSpectrum => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_spectra).Span))[..(2 * _paddedWidth)];

    private Span<double> ColumnSpectrum => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_spectra).Span)).Slice(2 * _paddedWidth, 2 * _paddedHeight);

    private Span<int> ColumnMap => unsafe(MemoryMarshal.Cast<byte, int>(GetBuffer(_maps).Span))[.._paddedWidth];

    private Span<int> RowMap => unsafe(MemoryMarshal.Cast<byte, int>(GetBuffer(_maps).Span)).Slice(_paddedWidth, _paddedHeight);

    private static PooledBuffer GetBuffer(PooledBuffer? buffer) => buffer ?? throw new ObjectDisposedException(nameof(WienerDeconvolver));

    private static int Modulo(long value, int length)
    {
        var result = value % length;
        return (int)(result < 0 ? result + length : result);
    }

    private Span<double> GetPlaneRow(int y)
        => unsafe(MemoryMarshal.Cast<byte, double>((_plane ?? throw new ObjectDisposedException(nameof(WienerDeconvolver))).GetRowSpanCore(y)))[..(2 * _paddedWidth)];

    private Span<float> GetFilterRow(int x)
        => unsafe(MemoryMarshal.Cast<byte, float>((_filter ?? throw new ObjectDisposedException(nameof(WienerDeconvolver))).GetRowSpanCore(x)))[..(2 * _paddedHeight)];

    private void Prepare(StorageOwner owner, ConvolutionKernel kernel, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        var scope = owner.Scope;
        _rowTransform = new FourierTransform(scope, _paddedWidth);
        _columnTransform = new FourierTransform(scope, _paddedHeight);
        _scratch = ResampleWeights.Rent<double>(scope, (2L * _paddedWidth) + (2L * _paddedHeight) + (2L * _blockColumns * _paddedHeight));
        _maps = ResampleWeights.Rent<int>(scope, (long)_paddedWidth + _paddedHeight);
        _plane = owner.Allocate(_paddedWidth, _paddedHeight, 2 * sizeof(double), options: null, AllocationKind.Temporary);
        FillMap(ColumnMap, _width, _radiusX, edgeMode);
        FillMap(RowMap, _height, _radiusY, edgeMode);
        if (kernel.IsSeparable)
        {
            _spectra = ResampleWeights.Rent<double>(scope, (2L * _paddedWidth) + (2L * _paddedHeight));
            PlaceWeights(kernel.RowWeights, RowInput, _paddedWidth);
            _rowTransform.Forward(RowInput, RowSpectrum);
            PlaceWeights(kernel.ColumnWeights, ColumnWork, _paddedHeight);
            _columnTransform.Forward(ColumnWork, ColumnSpectrum);
            return;
        }

        // The transfer of the whole matrix: the weights placed in the plane, transformed by rows then by columns, each
        // column giving a row of the filter
        _filter = owner.Allocate(_paddedHeight, _paddedWidth, 2 * sizeof(float), options: null, AllocationKind.Temporary);
        var values = kernel.GetValues();
        for (var y = 0; y < kernel.Height; y++)
        {
            var row = GetPlaneRow(Modulo(-(y - _radiusY), _paddedHeight));
            for (var x = 0; x < kernel.Width; x++)
            {
                row[2 * Modulo(-(x - _radiusX), _paddedWidth)] += values[(y * kernel.Width) + x];
            }
        }

        TransformRows(_paddedHeight, cancellationToken);
        var block = Block;
        var work = ColumnWork;
        var columnLength = 2 * _paddedHeight;
        for (var first = 0; first < _paddedWidth; first += _blockColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(_blockColumns, _paddedWidth - first);
            Gather(block, first, count);
            for (var t = 0; t < count; t++)
            {
                _columnTransform.Forward(block.Slice(t * columnLength, columnLength), work);
                var filter = GetFilterRow(first + t);
                for (var v = 0; v < _paddedHeight; v++)
                {
                    GetGain(work[2 * v], work[(2 * v) + 1], out var re, out var im);
                    filter[2 * v] = (float)re;
                    filter[(2 * v) + 1] = (float)im;
                }
            }
        }
    }

    /// <summary>Places the weights of an axis of the kernel at the opposite of their offsets, modulo the length of the plane, in a cleared span of complex numbers.</summary>
    private static void PlaceWeights(ReadOnlySpan<double> weights, Span<double> destination, int length)
    {
        destination.Clear();
        var radius = weights.Length / 2;
        for (var i = 0; i < weights.Length; i++)
        {
            destination[2 * Modulo(-(i - radius), length)] += weights[i];
        }
    }

    /// <summary>
    /// Sets the source of every position of an axis of the plane: a pixel of the frame (the frame, then the edge mode on
    /// both sides), transparent black (<see cref="ZeroColumn"/>), or the ramp between the two ends (<see cref="GapColumn"/>).
    /// </summary>
    private static void FillMap(Span<int> map, int length, int radius, ConvolutionEdgeMode edgeMode)
    {
        var padded = map.Length;
        for (var i = 0; i < padded; i++)
        {
            var source = i < length + radius ? i : i >= padded - radius ? i - padded : int.MinValue;
            map[i] = source == int.MinValue ? GapColumn : ConvolutionPlan.MapIndex(source, length, edgeMode) is var mapped and >= 0 ? mapped : ZeroColumn;
        }
    }

    /// <summary>Sets the plane to two working samples of the frame (the real and the imaginary parts), extended to the plane.</summary>
    private void Fill(WorkingFrameCopy copy, int first, int second)
    {
        var rows = RowMap;
        var columns = ColumnMap;
        var channels = copy.WorkingChannels;
        for (var y = 0; y < _paddedHeight; y++)
        {
            if (rows[y] == GapColumn)
                continue;

            var row = GetPlaneRow(y);
            if (rows[y] == ZeroColumn)
            {
                row.Clear();
                continue;
            }

            var source = copy.GetRow(rows[y]);
            for (var x = 0; x < _paddedWidth; x++)
            {
                var column = columns[x];
                if (column >= 0)
                {
                    row[2 * x] = ReadSample(source, (column * channels) + first);
                    row[(2 * x) + 1] = second < 0 ? 0 : ReadSample(source, (column * channels) + second);
                }
                else if (column == ZeroColumn)
                {
                    row[2 * x] = 0;
                    row[(2 * x) + 1] = 0;
                }
            }

            FillRamp(row, _width, _radiusX, _paddedWidth, columns);
        }

        // The rows of the ramp, between the last row of the edge mode below the frame and the first one above it
        var gap = _paddedHeight - _height - (2 * _radiusY);
        if (gap <= 0)
            return;

        var top = GetPlaneRow(_height + _radiusY - 1);
        var bottom = GetPlaneRow((_paddedHeight - _radiusY) % _paddedHeight);
        for (var y = _height + _radiusY; y < _paddedHeight - _radiusY; y++)
        {
            var weight = (double)(y - (_height + _radiusY - 1)) / (gap + 1);
            var row = GetPlaneRow(y);
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = top[i] + (weight * (bottom[i] - top[i]));
            }
        }
    }

    /// <summary>Sets the ramp of a row of the plane, between the last column of the edge mode right of the frame and the first one left of it.</summary>
    private static void FillRamp(Span<double> row, int width, int radius, int padded, ReadOnlySpan<int> columns)
    {
        var gap = padded - width - (2 * radius);
        if (gap <= 0)
            return;

        var left = width + radius - 1;
        var right = (padded - radius) % padded;
        for (var x = width + radius; x < padded - radius; x++)
        {
            if (columns[x] != GapColumn)
                continue;

            var weight = (double)(x - left) / (gap + 1);
            row[2 * x] = row[2 * left] + (weight * (row[2 * right] - row[2 * left]));
            row[(2 * x) + 1] = row[(2 * left) + 1] + (weight * (row[(2 * right) + 1] - row[(2 * left) + 1]));
        }
    }

    private static double ReadSample(ReadOnlySpan<float> row, int index)
    {
        var value = row[index];
        return float.IsFinite(value) ? value : 0;
    }

    /// <summary>Transforms the first <paramref name="count"/> rows of the plane in place.</summary>
    private void TransformRows(int count, CancellationToken cancellationToken)
    {
        var transform = _rowTransform ?? throw new ObjectDisposedException(nameof(WienerDeconvolver));
        var input = RowInput;
        for (var y = 0; y < count; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = GetPlaneRow(y);
            row.CopyTo(input);
            transform.Forward(input, row);
        }
    }

    /// <summary>
    /// Transforms every column of the plane, multiplies it by the filter and conjugates it, then transforms it again: the
    /// columns of the conjugate of the inverse transform, whose rows are left to transform.
    /// </summary>
    private void FilterColumns(CancellationToken cancellationToken)
    {
        var transform = _columnTransform ?? throw new ObjectDisposedException(nameof(WienerDeconvolver));
        var block = Block;
        var work = ColumnWork;
        var columnLength = 2 * _paddedHeight;
        var separable = _filter is null;
        for (var first = 0; first < _paddedWidth; first += _blockColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(_blockColumns, _paddedWidth - first);
            Gather(block, first, count);
            for (var t = 0; t < count; t++)
            {
                var column = block.Slice(t * columnLength, columnLength);
                transform.Forward(column, work);
                var u = first + t;
                if (separable)
                {
                    var rowSpectrum = RowSpectrum;
                    var columnSpectrum = ColumnSpectrum;
                    for (var v = 0; v < _paddedHeight; v++)
                    {
                        FourierTransform.Multiply(rowSpectrum[2 * u], rowSpectrum[(2 * u) + 1], columnSpectrum[2 * v], columnSpectrum[(2 * v) + 1], out var transferRe, out var transferIm);
                        GetGain(transferRe, transferIm, out var gainRe, out var gainIm);
                        ApplyGain(work, v, gainRe, gainIm);
                    }
                }
                else
                {
                    var filter = GetFilterRow(u);
                    for (var v = 0; v < _paddedHeight; v++)
                    {
                        ApplyGain(work, v, filter[2 * v], filter[(2 * v) + 1]);
                    }
                }

                transform.Forward(work, column);
            }

            Scatter(block, first, count);
        }
    }

    /// <summary>Gets the gain of a frequency, <c>conj(H) / (|H|² + K)</c>, scaled by the inverse transform.</summary>
    private void GetGain(double transferRe, double transferIm, out double re, out double im)
    {
        var factor = _scale / ((transferRe * transferRe) + (transferIm * transferIm) + _noise);
        re = transferRe * factor;
        im = -transferIm * factor;
    }

    /// <summary>Multiplies a frequency of a column by its gain, and conjugates it.</summary>
    private static void ApplyGain(Span<double> column, int index, double gainRe, double gainIm)
    {
        FourierTransform.Multiply(column[2 * index], column[(2 * index) + 1], gainRe, gainIm, out var re, out var im);
        column[2 * index] = re;
        column[(2 * index) + 1] = -im;
    }

    /// <summary>Copies <paramref name="count"/> columns of the plane from <paramref name="first"/> to the block, a column after the other.</summary>
    private void Gather(Span<double> block, int first, int count)
    {
        var columnLength = 2 * _paddedHeight;
        for (var y = 0; y < _paddedHeight; y++)
        {
            var row = GetPlaneRow(y);
            for (var t = 0; t < count; t++)
            {
                block[(t * columnLength) + (2 * y)] = row[2 * (first + t)];
                block[(t * columnLength) + (2 * y) + 1] = row[(2 * (first + t)) + 1];
            }
        }
    }

    /// <summary>Copies the columns of the block back to the plane (<see cref="Gather"/>).</summary>
    private void Scatter(ReadOnlySpan<double> block, int first, int count)
    {
        var columnLength = 2 * _paddedHeight;
        for (var y = 0; y < _paddedHeight; y++)
        {
            var row = GetPlaneRow(y);
            for (var t = 0; t < count; t++)
            {
                row[2 * (first + t)] = block[(t * columnLength) + (2 * y)];
                row[(2 * (first + t)) + 1] = block[(t * columnLength) + (2 * y) + 1];
            }
        }
    }

    /// <summary>
    /// Stores the deconvolved samples of the frame, the real parts and the opposites of the imaginary parts of the first
    /// rows of the plane, to the working copy, except where the copy holds a sample that is not finite.
    /// </summary>
    private void WriteBack(WorkingFrameCopy copy, int first, int second)
    {
        var channels = copy.WorkingChannels;
        for (var y = 0; y < _height; y++)
        {
            var row = GetPlaneRow(y);
            var destination = copy.GetWritableRow(y);
            for (var x = 0; x < _width; x++)
            {
                Write(destination, (x * channels) + first, row[2 * x]);
                if (second >= 0)
                {
                    Write(destination, (x * channels) + second, -row[(2 * x) + 1]);
                }
            }
        }

        static void Write(Span<float> destination, int index, double value)
        {
            if (float.IsFinite(destination[index]))
            {
                destination[index] = (float)Math.Clamp(value, float.MinValue, float.MaxValue);
            }
        }
    }
}
