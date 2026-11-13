using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Canny edge detector: the luminance of a frame blurred, its Sobel gradient, the gradients that are not a maximum
/// across their edge removed (non-maximum suppression), and the remaining ones kept when they exceed the upper threshold
/// or are connected to such a gradient by gradients that exceed the lower one (hysteresis).
/// </summary>
/// <remarks>
/// <para>
/// The steps after the blur are those of OpenCV's <c>cv2.Canny</c> with <c>L2gradient</c>: the Sobel derivatives read the
/// pixels outside the frame as the nearest edge pixel, the magnitude is <c>gx² + gy²</c> compared with the squares of the
/// thresholds, the direction is one of four sectors chosen with OpenCV's fixed-point tangent of 22.5 degrees
/// (<c>13573 / 2^15</c>), a gradient is a maximum when it is greater than its neighbor before it along the direction and
/// not lower than the one after it (greater than both on the diagonals), the gradients outside the frame are 0, and the
/// hysteresis follows the 8 neighbors of a pixel. Without blur, the edges of 8-bit images are those of OpenCV.
/// </para>
/// <para>
/// The working memory is 18 bytes per pixel: the luminance and the magnitudes as doubles, the sectors and the states as
/// bytes. It is charged to the scope of the image, before any pixel changes.
/// </para>
/// <para>
/// The blur, the gradients and the suppression compute several pixels of a row at once, one per lane of a vector of
/// doubles, with the operations of the scalar loops in the same order; the pixels whose neighbors are beyond the edges
/// keep the scalar loops.
/// </para>
/// </remarks>
internal sealed class CannyEdgeDetector : IDisposable
{
    /// <summary>The tangent of 22.5 degrees of OpenCV, in 1/2^15.</summary>
    private const double Tangent22 = 13573;

    private const byte SectorHorizontal = 0;
    private const byte SectorVertical = 1;
    private const byte SectorDiagonalDown = 2;
    private const byte SectorDiagonalUp = 3;

    private const byte StateNone = 0;
    private const byte StateCandidate = 1;
    private const byte StateEdge = 2;

    private readonly PixelFormat _format;
    private readonly int _width;
    private readonly int _height;
    private readonly int _max;
    private readonly double[] _weights;
    private readonly double _low;
    private readonly double _high;
    private PooledBuffer? _values;
    private PooledBuffer? _magnitudes;
    private PooledBuffer? _sectors;
    private PooledBuffer? _states;
    private PooledBuffer? _row;

    /// <summary>Rents the working memory of the detector.</summary>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    public CannyEdgeDetector(AllocationScope scope, PixelFormat format, Size size, CannyEdgeOptions options)
    {
        _format = format;
        _width = size.Width;
        _height = size.Height;
        _max = ToneSamples.GetLevelCount(format) - 1;
        _weights = options.Sigma > 0 ? GaussianKernel.Create(options.Sigma) : [];

        // The thresholds on the scale of the samples, squared as the magnitudes are
        var low = ToLevel(options.LowThreshold * _max);
        var high = ToLevel(options.HighThreshold * _max);
        _low = low * low;
        _high = high * high;
        var count = (long)_width * _height;
        try
        {
            _values = ResampleWeights.Rent<double>(scope, count);
            _magnitudes = ResampleWeights.Rent<double>(scope, count);
            _sectors = ResampleWeights.Rent<byte>(scope, count);
            _states = ResampleWeights.Rent<byte>(scope, count);
            _row = ToneSamples.RentRow(scope, _width);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Span<double> Values => unsafe(MemoryMarshal.Cast<byte, double>(_values!.Span))[..(_width * _height)];

    private Span<double> Magnitudes => unsafe(MemoryMarshal.Cast<byte, double>(_magnitudes!.Span))[..(_width * _height)];

    private Span<byte> Sectors => _sectors!.Span[..(_width * _height)];

    private Span<byte> States => _states!.Span[..(_width * _height)];

    private static bool IsVectorized => Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar;

    /// <summary>Replaces the pixels of a leased frame by its edges: the maximum sample for the colors of an edge, 0 elsewhere; alpha is kept.</summary>
    public void Apply(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        ReadLuminance(lease, cancellationToken);
        if (_weights.Length > 1)
        {
            Blur(cancellationToken);
        }

        ComputeGradients(cancellationToken);
        SuppressNonMaxima(cancellationToken);
        TraceEdges(cancellationToken);
        Write(lease, cancellationToken);
    }

    public void Dispose()
    {
        _values?.Dispose();
        _magnitudes?.Dispose();
        _sectors?.Dispose();
        _states?.Dispose();
        _row?.Dispose();
        _values = null;
        _magnitudes = null;
        _sectors = null;
        _states = null;
        _row = null;
    }

    /// <summary>Reads the Rec. 709 luma of every pixel, at the storage precision, as <see cref="ImageHistogram"/> counts it.</summary>
    private void ReadLuminance(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        var values = Values;
        var samples = ToneSamples.GetSamples(_row!, _width);
        var luma = ToneSamples.GetLuma(_row!, _width);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(_format);
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ToneSamples.ReadLuma(_format, lease.GetRowBytes(y)[..(_width * bytesPerPixel)], samples, luma);
            var row = values.Slice(y * _width, _width);
            for (var x = 0; x < _width; x++)
            {
                row[x] = luma[x];
            }
        }
    }

    /// <summary>Blurs the luminance with the Gaussian weights, rows then columns, the pixels outside read as the nearest edge pixel.</summary>
    private void Blur(CancellationToken cancellationToken)
    {
        var values = Values;
        var temporary = Magnitudes;
        var weights = _weights;
        var radius = weights.Length / 2;
        var vectorized = IsVectorized;
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var source = values.Slice(y * _width, _width);
            var destination = temporary.Slice(y * _width, _width);

            // The pixels whose taps are all inside the row need no clamping
            var interiorStart = vectorized && _width > 2 * radius ? radius : _width;
            var interiorEnd = interiorStart < _width ? interiorStart + BlurRowVector(source, destination, weights, radius, interiorStart, _width - radius) : _width;
            for (var x = 0; x < _width; x++)
            {
                if (x == interiorStart)
                {
                    x = interiorEnd;
                    if (x >= _width)
                        break;
                }

                var sum = 0.0;
                for (var k = 0; k < weights.Length; k++)
                {
                    sum += weights[k] * source[Math.Clamp(x + k - radius, 0, _width - 1)];
                }

                destination[x] = sum;
            }
        }

        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var destination = values.Slice(y * _width, _width);
            destination.Clear();
            for (var k = 0; k < weights.Length; k++)
            {
                var weight = weights[k];
                var source = temporary.Slice(Math.Clamp(y + k - radius, 0, _height - 1) * _width, _width);
                var x = vectorized ? AddProductsVector(source, weight, destination) : 0;
                for (; x < _width; x++)
                {
                    destination[x] += weight * source[x];
                }
            }
        }
    }

    /// <summary>Computes the squared magnitude of the Sobel gradient of every pixel and the sector of its direction.</summary>
    private void ComputeGradients(CancellationToken cancellationToken)
    {
        var values = Values;
        var magnitudes = Magnitudes;
        var sectors = Sectors;
        var vectorized = IsVectorized;
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var above = values.Slice(Math.Max(y - 1, 0) * _width, _width);
            var row = values.Slice(y * _width, _width);
            var below = values.Slice(Math.Min(y + 1, _height - 1) * _width, _width);
            var interiorEnd = vectorized && _width > 2 ? 1 + ComputeGradientsVector(above, row, below, magnitudes.Slice(y * _width, _width), sectors.Slice(y * _width, _width), 1, _width - 1) : 1;
            for (var x = 0; x < _width; x++)
            {
                if (x == 1)
                {
                    x = interiorEnd;
                    if (x >= _width)
                        break;
                }

                var left = Math.Max(x - 1, 0);
                var right = Math.Min(x + 1, _width - 1);
                var gx = (above[right] - above[left]) + (2 * (row[right] - row[left])) + (below[right] - below[left]);
                var gy = (below[left] - above[left]) + (2 * (below[x] - above[x])) + (below[right] - above[right]);
                var index = (y * _width) + x;
                magnitudes[index] = (gx * gx) + (gy * gy);

                var ax = Math.Abs(gx);
                var ay = Math.Abs(gy) * 32768;
                var tangent22 = ax * Tangent22;
                sectors[index] = ay < tangent22 ? SectorHorizontal
                    : ay > tangent22 + (ax * 65536) ? SectorVertical
                    : (gx < 0) != (gy < 0) ? SectorDiagonalUp : SectorDiagonalDown;
            }
        }
    }

    /// <summary>Marks the gradients that exceed the lower threshold and are a maximum along their direction as candidates, and those that exceed the upper one as edges.</summary>
    private void SuppressNonMaxima(CancellationToken cancellationToken)
    {
        var magnitudes = Magnitudes;
        var sectors = Sectors;
        var states = States;
        var vectorized = IsVectorized;
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The pixels whose 8 neighbors are inside the frame
            var interiorEnd = vectorized && y > 0 && y < _height - 1 && _width > 2 ? 1 + SuppressNonMaximaVector(magnitudes, sectors, states, y, 1, _width - 1) : 1;
            for (var x = 0; x < _width; x++)
            {
                if (x == 1)
                {
                    x = interiorEnd;
                    if (x >= _width)
                        break;
                }

                var index = (y * _width) + x;
                var magnitude = magnitudes[index];
                var state = StateNone;
                if (magnitude > _low)
                {
                    var isMaximum = sectors[index] switch
                    {
                        SectorHorizontal => magnitude > GetMagnitude(magnitudes, x - 1, y) && magnitude >= GetMagnitude(magnitudes, x + 1, y),
                        SectorVertical => magnitude > GetMagnitude(magnitudes, x, y - 1) && magnitude >= GetMagnitude(magnitudes, x, y + 1),
                        SectorDiagonalDown => magnitude > GetMagnitude(magnitudes, x - 1, y - 1) && magnitude > GetMagnitude(magnitudes, x + 1, y + 1),
                        _ => magnitude > GetMagnitude(magnitudes, x + 1, y - 1) && magnitude > GetMagnitude(magnitudes, x - 1, y + 1),
                    };

                    if (isMaximum)
                    {
                        state = magnitude > _high ? StateEdge : StateCandidate;
                    }
                }

                states[index] = state;
            }
        }
    }

    /// <summary>Turns into edges the candidates connected to an edge by candidates, through the 8 neighbors of each pixel.</summary>
    private void TraceEdges(CancellationToken cancellationToken)
    {
        var states = States;

        // The pixels to visit, an index each: the luminance is no longer needed, so its memory holds them
        var stack = unsafe(MemoryMarshal.Cast<byte, int>(_values!.Span))[..(_width * _height)];
        var count = 0;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] == StateEdge)
            {
                stack[count++] = index;
            }
        }

        var visited = 0;
        while (count > 0)
        {
            if (++visited % 65536 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var index = stack[--count];
            var x = index % _width;
            var y = index / _width;
            for (var dy = -1; dy <= 1; dy++)
            {
                var ny = y + dy;
                if ((uint)ny >= (uint)_height)
                    continue;

                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    if ((uint)nx >= (uint)_width)
                        continue;

                    var neighbor = (ny * _width) + nx;
                    if (states[neighbor] == StateCandidate)
                    {
                        // Each pixel is pushed once: it becomes an edge first
                        states[neighbor] = StateEdge;
                        stack[count++] = neighbor;
                    }
                }
            }
        }
    }

    /// <summary>Writes the colors of the edges (the maximum sample) and of the other pixels (0), keeping alpha.</summary>
    private void Write(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        var states = States;
        var samples = ToneSamples.GetSamples(_row!, _width);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(_format);
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var pixels = lease.GetRowBytes(y)[..(_width * bytesPerPixel)];
            var row = states.Slice(y * _width, _width);
            switch (_format)
            {
                // A floating-point sample is written as exactly 0 or 1 (ToneSamples keeps a value whose level does not change)
                case PixelFormat.RgbaFloat:
                    var floats = unsafe(MemoryMarshal.Cast<byte, float>(pixels));
                    for (var x = 0; x < _width; x++)
                    {
                        floats.Slice(4 * x, 3).Fill(row[x] == StateEdge ? 1f : 0f);
                    }

                    break;

                case PixelFormat.RgbaHalf:
                    var halves = unsafe(MemoryMarshal.Cast<byte, Half>(pixels));
                    for (var x = 0; x < _width; x++)
                    {
                        halves.Slice(4 * x, 3).Fill(row[x] == StateEdge ? Half.One : Half.Zero);
                    }

                    break;

                default:
                    for (var x = 0; x < _width; x++)
                    {
                        samples.Slice(4 * x, 3).Fill(row[x] == StateEdge ? (ushort)_max : (ushort)0);
                    }

                    ToneSamples.WriteColors(_format, samples, pixels);
                    break;
            }
        }
    }

    /// <summary>Sets the blurred values of the pixels from <paramref name="start"/> to <paramref name="end"/>, whose taps are inside the row, a vector at a time.</summary>
    /// <returns>The number of pixels computed.</returns>
    private static int BlurRowVector(ReadOnlySpan<double> source, Span<double> destination, double[] weights, int radius, int start, int end)
    {
        if (Vector256.IsHardwareAccelerated)
            return BlurRowVector256(source, destination, weights, radius, start, end);

        return BlurRowVector128(source, destination, weights, radius, start, end);
    }

    /// <summary>Adds the products of <paramref name="source"/> by a weight to <paramref name="destination"/>, a vector at a time.</summary>
    /// <returns>The number of values computed.</returns>
    private static int AddProductsVector(ReadOnlySpan<double> source, double weight, Span<double> destination)
    {
        if (Vector256.IsHardwareAccelerated)
            return AddProductsVector256(source, weight, destination);

        return AddProductsVector128(source, weight, destination);
    }

    /// <summary>Computes the gradients of the pixels from <paramref name="start"/> to <paramref name="end"/>, whose neighbors are inside the row, a vector at a time.</summary>
    /// <returns>The number of pixels computed.</returns>
    private static int ComputeGradientsVector(ReadOnlySpan<double> above, ReadOnlySpan<double> row, ReadOnlySpan<double> below, Span<double> magnitudes, Span<byte> sectors, int start, int end)
    {
        if (Vector256.IsHardwareAccelerated)
            return ComputeGradientsVector256(above, row, below, magnitudes, sectors, start, end);

        return ComputeGradientsVector128(above, row, below, magnitudes, sectors, start, end);
    }

    /// <summary>Sets the states of the pixels of row <paramref name="y"/> from <paramref name="start"/> to <paramref name="end"/>, whose 8 neighbors are inside the frame, a vector at a time.</summary>
    /// <returns>The number of pixels computed.</returns>
    private int SuppressNonMaximaVector(ReadOnlySpan<double> magnitudes, ReadOnlySpan<byte> sectors, Span<byte> states, int y, int start, int end)
    {
        if (Vector256.IsHardwareAccelerated)
            return SuppressNonMaximaVector256(magnitudes, sectors, states, y, start, end);

        return SuppressNonMaximaVector128(magnitudes, sectors, states, y, start, end);
    }

    private static int BlurRowVector256(ReadOnlySpan<double> source, Span<double> destination, double[] weights, int radius, int start, int end)
    {
        _ = source[end - 1 + radius];
        _ = destination[end - 1];
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var x = start;
        for (; x + Vector256<double>.Count <= end; x += Vector256<double>.Count)
        {
            var sum = Vector256<double>.Zero;
            for (var k = 0; k < weights.Length; k++)
            {
                sum += Vector256.Create(weights[k]) * unsafe(Vector256.LoadUnsafe(ref sourceStart, (nuint)(x + k - radius)));
            }

            unsafe { sum.StoreUnsafe(ref destinationStart, (nuint)x); }
        }

        return x - start;
    }

    private static int AddProductsVector256(ReadOnlySpan<double> source, double weight, Span<double> destination)
    {
        var length = Math.Min(source.Length, destination.Length);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var factor = Vector256.Create(weight);
        var x = 0;
        for (; x + Vector256<double>.Count <= length; x += Vector256<double>.Count)
        {
            unsafe { (Vector256.LoadUnsafe(ref destinationStart, (nuint)x) + (factor * Vector256.LoadUnsafe(ref sourceStart, (nuint)x))).StoreUnsafe(ref destinationStart, (nuint)x); }
        }

        return x;
    }

    private static int ComputeGradientsVector256(ReadOnlySpan<double> above, ReadOnlySpan<double> row, ReadOnlySpan<double> below, Span<double> magnitudes, Span<byte> sectors, int start, int end)
    {
        _ = above[end];
        _ = row[end];
        _ = below[end];
        _ = magnitudes[end - 1];
        _ = sectors[end - 1];
        ref var aboveStart = ref unsafe(MemoryMarshal.GetReference(above));
        ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row));
        ref var belowStart = ref unsafe(MemoryMarshal.GetReference(below));
        ref var magnitudeStart = ref unsafe(MemoryMarshal.GetReference(magnitudes));
        var two = Vector256.Create(2.0);
        var tangent = Vector256.Create(Tangent22);
        var x = start;
        for (; x + Vector256<double>.Count <= end; x += Vector256<double>.Count)
        {
            Vector256<double> aboveLeft, aboveMiddle, aboveRight, left, right, belowLeft, belowMiddle, belowRight;
            unsafe
            {
                aboveLeft = Vector256.LoadUnsafe(ref aboveStart, (nuint)(x - 1));
                aboveMiddle = Vector256.LoadUnsafe(ref aboveStart, (nuint)x);
                aboveRight = Vector256.LoadUnsafe(ref aboveStart, (nuint)(x + 1));
                left = Vector256.LoadUnsafe(ref rowStart, (nuint)(x - 1));
                right = Vector256.LoadUnsafe(ref rowStart, (nuint)(x + 1));
                belowLeft = Vector256.LoadUnsafe(ref belowStart, (nuint)(x - 1));
                belowMiddle = Vector256.LoadUnsafe(ref belowStart, (nuint)x);
                belowRight = Vector256.LoadUnsafe(ref belowStart, (nuint)(x + 1));
            }

            var gx = (aboveRight - aboveLeft) + (two * (right - left)) + (belowRight - belowLeft);
            var gy = (belowLeft - aboveLeft) + (two * (belowMiddle - aboveMiddle)) + (belowRight - aboveRight);
            unsafe { ((gx * gx) + (gy * gy)).StoreUnsafe(ref magnitudeStart, (nuint)x); }

            var ax = Vector256.Abs(gx);
            var ay = Vector256.Abs(gy) * Vector256.Create(32768.0);
            var tangent22 = ax * tangent;
            var horizontal = Vector256.LessThan(ay, tangent22);
            var vertical = Vector256.GreaterThan(ay, tangent22 + (ax * Vector256.Create(65536.0)));
            var up = Vector256.LessThan(gx, Vector256<double>.Zero) ^ Vector256.LessThan(gy, Vector256<double>.Zero);
            var sector = Vector256.ConditionalSelect(
                horizontal.AsInt64(),
                Vector256.Create((long)SectorHorizontal),
                Vector256.ConditionalSelect(vertical.AsInt64(), Vector256.Create((long)SectorVertical), Vector256.ConditionalSelect(up.AsInt64(), Vector256.Create((long)SectorDiagonalUp), Vector256.Create((long)SectorDiagonalDown))));
            for (var i = 0; i < Vector256<long>.Count; i++)
            {
                sectors[x + i] = (byte)sector.GetElement(i);
            }
        }

        return x - start;
    }

    private int SuppressNonMaximaVector256(ReadOnlySpan<double> magnitudes, ReadOnlySpan<byte> sectors, Span<byte> states, int y, int start, int end)
    {
        var rowStart = y * _width;
        _ = magnitudes[rowStart + _width + end];
        _ = magnitudes[rowStart - _width + start - 1];
        ref var middleStart = ref unsafe(MemoryMarshal.GetReference(magnitudes.Slice(rowStart)));
        ref var aboveStart = ref unsafe(MemoryMarshal.GetReference(magnitudes.Slice(rowStart - _width)));
        ref var belowStart = ref unsafe(MemoryMarshal.GetReference(magnitudes.Slice(rowStart + _width)));
        var low = Vector256.Create(_low);
        var high = Vector256.Create(_high);
        var x = start;
        for (; x + Vector256<double>.Count <= end; x += Vector256<double>.Count)
        {
            Vector256<double> magnitude, left, right, above, below, aboveLeft, aboveRight, belowLeft, belowRight;
            unsafe
            {
                magnitude = Vector256.LoadUnsafe(ref middleStart, (nuint)x);
                left = Vector256.LoadUnsafe(ref middleStart, (nuint)(x - 1));
                right = Vector256.LoadUnsafe(ref middleStart, (nuint)(x + 1));
                above = Vector256.LoadUnsafe(ref aboveStart, (nuint)x);
                aboveLeft = Vector256.LoadUnsafe(ref aboveStart, (nuint)(x - 1));
                aboveRight = Vector256.LoadUnsafe(ref aboveStart, (nuint)(x + 1));
                below = Vector256.LoadUnsafe(ref belowStart, (nuint)x);
                belowLeft = Vector256.LoadUnsafe(ref belowStart, (nuint)(x - 1));
                belowRight = Vector256.LoadUnsafe(ref belowStart, (nuint)(x + 1));
            }

            // The comparisons of every sector, the one of each pixel kept
            var horizontal = Vector256.GreaterThan(magnitude, left) & Vector256.GreaterThanOrEqual(magnitude, right);
            var vertical = Vector256.GreaterThan(magnitude, above) & Vector256.GreaterThanOrEqual(magnitude, below);
            var down = Vector256.GreaterThan(magnitude, aboveLeft) & Vector256.GreaterThan(magnitude, belowRight);
            var up = Vector256.GreaterThan(magnitude, aboveRight) & Vector256.GreaterThan(magnitude, belowLeft);
            var sector = LoadSectors256(sectors, rowStart + x);
            var isMaximum = (Vector256.Equals(sector, Vector256.Create((long)SectorHorizontal)) & horizontal.AsInt64())
                | (Vector256.Equals(sector, Vector256.Create((long)SectorVertical)) & vertical.AsInt64())
                | (Vector256.Equals(sector, Vector256.Create((long)SectorDiagonalDown)) & down.AsInt64())
                | (Vector256.GreaterThan(sector, Vector256.Create((long)SectorDiagonalDown)) & up.AsInt64());
            var state = (isMaximum & Vector256.GreaterThan(magnitude, low).AsInt64()) & Vector256.ConditionalSelect(Vector256.GreaterThan(magnitude, high).AsInt64(), Vector256.Create((long)StateEdge), Vector256.Create((long)StateCandidate));
            for (var i = 0; i < Vector256<long>.Count; i++)
            {
                states[rowStart + x + i] = (byte)state.GetElement(i);
            }
        }

        return x - start;
    }

    private static int BlurRowVector128(ReadOnlySpan<double> source, Span<double> destination, double[] weights, int radius, int start, int end)
    {
        _ = source[end - 1 + radius];
        _ = destination[end - 1];
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var x = start;
        for (; x + Vector128<double>.Count <= end; x += Vector128<double>.Count)
        {
            var sum = Vector128<double>.Zero;
            for (var k = 0; k < weights.Length; k++)
            {
                sum += Vector128.Create(weights[k]) * unsafe(Vector128.LoadUnsafe(ref sourceStart, (nuint)(x + k - radius)));
            }

            unsafe { sum.StoreUnsafe(ref destinationStart, (nuint)x); }
        }

        return x - start;
    }

    private static int AddProductsVector128(ReadOnlySpan<double> source, double weight, Span<double> destination)
    {
        var length = Math.Min(source.Length, destination.Length);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var factor = Vector128.Create(weight);
        var x = 0;
        for (; x + Vector128<double>.Count <= length; x += Vector128<double>.Count)
        {
            unsafe { (Vector128.LoadUnsafe(ref destinationStart, (nuint)x) + (factor * Vector128.LoadUnsafe(ref sourceStart, (nuint)x))).StoreUnsafe(ref destinationStart, (nuint)x); }
        }

        return x;
    }

    private static int ComputeGradientsVector128(ReadOnlySpan<double> above, ReadOnlySpan<double> row, ReadOnlySpan<double> below, Span<double> magnitudes, Span<byte> sectors, int start, int end)
    {
        _ = above[end];
        _ = row[end];
        _ = below[end];
        _ = magnitudes[end - 1];
        _ = sectors[end - 1];
        ref var aboveStart = ref unsafe(MemoryMarshal.GetReference(above));
        ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row));
        ref var belowStart = ref unsafe(MemoryMarshal.GetReference(below));
        ref var magnitudeStart = ref unsafe(MemoryMarshal.GetReference(magnitudes));
        var two = Vector128.Create(2.0);
        var tangent = Vector128.Create(Tangent22);
        var x = start;
        for (; x + Vector128<double>.Count <= end; x += Vector128<double>.Count)
        {
            Vector128<double> aboveLeft, aboveMiddle, aboveRight, left, right, belowLeft, belowMiddle, belowRight;
            unsafe
            {
                aboveLeft = Vector128.LoadUnsafe(ref aboveStart, (nuint)(x - 1));
                aboveMiddle = Vector128.LoadUnsafe(ref aboveStart, (nuint)x);
                aboveRight = Vector128.LoadUnsafe(ref aboveStart, (nuint)(x + 1));
                left = Vector128.LoadUnsafe(ref rowStart, (nuint)(x - 1));
                right = Vector128.LoadUnsafe(ref rowStart, (nuint)(x + 1));
                belowLeft = Vector128.LoadUnsafe(ref belowStart, (nuint)(x - 1));
                belowMiddle = Vector128.LoadUnsafe(ref belowStart, (nuint)x);
                belowRight = Vector128.LoadUnsafe(ref belowStart, (nuint)(x + 1));
            }

            var gx = (aboveRight - aboveLeft) + (two * (right - left)) + (belowRight - belowLeft);
            var gy = (belowLeft - aboveLeft) + (two * (belowMiddle - aboveMiddle)) + (belowRight - aboveRight);
            unsafe { ((gx * gx) + (gy * gy)).StoreUnsafe(ref magnitudeStart, (nuint)x); }

            var ax = Vector128.Abs(gx);
            var ay = Vector128.Abs(gy) * Vector128.Create(32768.0);
            var tangent22 = ax * tangent;
            var horizontal = Vector128.LessThan(ay, tangent22);
            var vertical = Vector128.GreaterThan(ay, tangent22 + (ax * Vector128.Create(65536.0)));
            var up = Vector128.LessThan(gx, Vector128<double>.Zero) ^ Vector128.LessThan(gy, Vector128<double>.Zero);
            var sector = Vector128.ConditionalSelect(
                horizontal.AsInt64(),
                Vector128.Create((long)SectorHorizontal),
                Vector128.ConditionalSelect(vertical.AsInt64(), Vector128.Create((long)SectorVertical), Vector128.ConditionalSelect(up.AsInt64(), Vector128.Create((long)SectorDiagonalUp), Vector128.Create((long)SectorDiagonalDown))));
            for (var i = 0; i < Vector128<long>.Count; i++)
            {
                sectors[x + i] = (byte)sector.GetElement(i);
            }
        }

        return x - start;
    }

    private int SuppressNonMaximaVector128(ReadOnlySpan<double> magnitudes, ReadOnlySpan<byte> sectors, Span<byte> states, int y, int start, int end)
    {
        var rowStart = y * _width;
        _ = magnitudes[rowStart + _width + end];
        _ = magnitudes[rowStart - _width + start - 1];
        ref var middleStart = ref unsafe(MemoryMarshal.GetReference(magnitudes.Slice(rowStart)));
        ref var aboveStart = ref unsafe(MemoryMarshal.GetReference(magnitudes.Slice(rowStart - _width)));
        ref var belowStart = ref unsafe(MemoryMarshal.GetReference(magnitudes.Slice(rowStart + _width)));
        var low = Vector128.Create(_low);
        var high = Vector128.Create(_high);
        var x = start;
        for (; x + Vector128<double>.Count <= end; x += Vector128<double>.Count)
        {
            Vector128<double> magnitude, left, right, above, below, aboveLeft, aboveRight, belowLeft, belowRight;
            unsafe
            {
                magnitude = Vector128.LoadUnsafe(ref middleStart, (nuint)x);
                left = Vector128.LoadUnsafe(ref middleStart, (nuint)(x - 1));
                right = Vector128.LoadUnsafe(ref middleStart, (nuint)(x + 1));
                above = Vector128.LoadUnsafe(ref aboveStart, (nuint)x);
                aboveLeft = Vector128.LoadUnsafe(ref aboveStart, (nuint)(x - 1));
                aboveRight = Vector128.LoadUnsafe(ref aboveStart, (nuint)(x + 1));
                below = Vector128.LoadUnsafe(ref belowStart, (nuint)x);
                belowLeft = Vector128.LoadUnsafe(ref belowStart, (nuint)(x - 1));
                belowRight = Vector128.LoadUnsafe(ref belowStart, (nuint)(x + 1));
            }

            // The comparisons of every sector, the one of each pixel kept
            var horizontal = Vector128.GreaterThan(magnitude, left) & Vector128.GreaterThanOrEqual(magnitude, right);
            var vertical = Vector128.GreaterThan(magnitude, above) & Vector128.GreaterThanOrEqual(magnitude, below);
            var down = Vector128.GreaterThan(magnitude, aboveLeft) & Vector128.GreaterThan(magnitude, belowRight);
            var up = Vector128.GreaterThan(magnitude, aboveRight) & Vector128.GreaterThan(magnitude, belowLeft);
            var sector = LoadSectors128(sectors, rowStart + x);
            var isMaximum = (Vector128.Equals(sector, Vector128.Create((long)SectorHorizontal)) & horizontal.AsInt64())
                | (Vector128.Equals(sector, Vector128.Create((long)SectorVertical)) & vertical.AsInt64())
                | (Vector128.Equals(sector, Vector128.Create((long)SectorDiagonalDown)) & down.AsInt64())
                | (Vector128.GreaterThan(sector, Vector128.Create((long)SectorDiagonalDown)) & up.AsInt64());
            var state = (isMaximum & Vector128.GreaterThan(magnitude, low).AsInt64()) & Vector128.ConditionalSelect(Vector128.GreaterThan(magnitude, high).AsInt64(), Vector128.Create((long)StateEdge), Vector128.Create((long)StateCandidate));
            for (var i = 0; i < Vector128<long>.Count; i++)
            {
                states[rowStart + x + i] = (byte)state.GetElement(i);
            }
        }

        return x - start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<long> LoadSectors256(ReadOnlySpan<byte> sectors, int index) => Vector256.Create(sectors[index], sectors[index + 1], sectors[index + 2], (long)sectors[index + 3]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<long> LoadSectors128(ReadOnlySpan<byte> sectors, int index) => Vector128.Create(sectors[index], (long)sectors[index + 1]);

    /// <summary>
    /// Gets a threshold on the scale of the samples: a product within 1e-9 of an integer is that integer, so that a
    /// fraction such as <c>50 / 255</c> is exactly the level 50 of 8-bit samples, whose squared magnitudes are integers.
    /// </summary>
    private static double ToLevel(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.ToEven);
        return Math.Abs(value - rounded) <= 1e-9 * Math.Max(1, rounded) ? rounded : value;
    }

    /// <summary>Gets the squared magnitude of a pixel, 0 outside the frame.</summary>
    private double GetMagnitude(ReadOnlySpan<double> magnitudes, int x, int y) => (uint)x < (uint)_width && (uint)y < (uint)_height ? magnitudes[(y * _width) + x] : 0;
}
