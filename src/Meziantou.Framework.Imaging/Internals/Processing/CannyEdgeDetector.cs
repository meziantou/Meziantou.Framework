using System.Runtime.InteropServices;

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
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var source = values.Slice(y * _width, _width);
            var destination = temporary.Slice(y * _width, _width);
            for (var x = 0; x < _width; x++)
            {
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
                for (var x = 0; x < _width; x++)
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
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var above = values.Slice(Math.Max(y - 1, 0) * _width, _width);
            var row = values.Slice(y * _width, _width);
            var below = values.Slice(Math.Min(y + 1, _height - 1) * _width, _width);
            for (var x = 0; x < _width; x++)
            {
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
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = 0; x < _width; x++)
            {
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
