using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Fills the pixels of a mask from the pixels around it with the fast marching method of Telea ("An Image Inpainting
/// Technique Based on the Fast Marching Method", Journal of Graphics Tools, 2004).
/// </summary>
/// <remarks>
/// <para>
/// The known pixels next to the mask (4-neighbors) form the initial band, at distance <c>T = 0</c>. The pixel of the band
/// with the smallest distance is accepted; each of its 4-neighbors in the mask gets the distance of the eikonal equation
/// <c>|∇T| = 1</c> from its accepted neighbors (the upwind solution: <c>min + 1</c> when the two axes differ by at least 1,
/// <c>(a + b + sqrt(2 - (a - b)²)) / 2</c> otherwise), then, the first time, its color: the weighted mean of the first-order
/// estimates <c>I(q) + ∇I(q) · (p - q)</c> of the pixels <c>q</c> already filled or known within the radius. A weight is the
/// product of the alignment of <c>p - q</c> with the gradient of <c>T</c> at <c>p</c> (at least 10⁻⁶), of
/// <c>1 / |p - q|²</c>, and of <c>1 / (1 + |T(q) - T(p)|)</c>. The front thus moves inward and every pixel is filled from
/// the ones nearer to the boundary.
/// </para>
/// <para>
/// The colors are filled premultiplied by alpha, in double precision, and each channel is clamped to the range of the
/// values it is estimated from, so that the gradient term never overshoots. The band is a binary heap ordered by distance,
/// then by pixel index: the order is total, so the result does not depend on the platform. The working memory is 17 bytes
/// per pixel (flags, distances, the heap and its positions) and a working copy of the frame, charged to the image.
/// </para>
/// </remarks>
internal sealed class Inpainter : IDisposable
{
    private const byte Known = 0;
    private const byte Band = 1;
    private const byte Inside = 2;

    private const double Unknown = 1e9;

    private readonly int _width;
    private readonly int _height;
    private readonly int _radius;
    private readonly PixelMask _mask;
    private PooledBuffer? _flags;
    private PooledBuffer? _distances;
    private PooledBuffer? _heap;
    private PooledBuffer? _positions;
    private PooledBuffer? _pixels;
    private int _heapCount;

    /// <summary>Rents the working memory of the inpainting of frames of a size.</summary>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    public Inpainter(AllocationScope scope, Size size, PixelMask mask, int radius)
    {
        _width = size.Width;
        _height = size.Height;
        _radius = radius;
        _mask = mask;
        var count = (long)_width * _height;
        try
        {
            _flags = ResampleWeights.Rent<byte>(scope, count);
            _distances = ResampleWeights.Rent<double>(scope, count);
            _heap = ResampleWeights.Rent<int>(scope, count);
            _positions = ResampleWeights.Rent<int>(scope, count);
            _pixels = ResampleWeights.Rent<double>(scope, 4 * count);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private int Count => _width * _height;

    private Span<byte> Flags => _flags!.Span[..Count];

    private Span<double> Distances => unsafe(MemoryMarshal.Cast<byte, double>(_distances!.Span))[..Count];

    private Span<int> Heap => unsafe(MemoryMarshal.Cast<byte, int>(_heap!.Span))[..Count];

    private Span<int> Positions => unsafe(MemoryMarshal.Cast<byte, int>(_positions!.Span))[..Count];

    /// <summary>The pixels of the frame, premultiplied by alpha: red, green, blue and alpha.</summary>
    private Span<double> Pixels => unsafe(MemoryMarshal.Cast<byte, double>(_pixels!.Span))[..(4 * Count)];

    /// <summary>Copies a frame to the destination with the pixels of the mask filled.</summary>
    public void Apply(PixelRowSource<RgbaFloat> source, PixelAccessor<RgbaFloat> destination, CancellationToken cancellationToken)
    {
        Load(source);
        Initialize();
        March(cancellationToken);
        Store(source, destination);
    }

    public void Dispose()
    {
        _flags?.Dispose();
        _flags = null;
        _distances?.Dispose();
        _distances = null;
        _heap?.Dispose();
        _heap = null;
        _positions?.Dispose();
        _positions = null;
        _pixels?.Dispose();
        _pixels = null;
    }

    private void Load(PixelRowSource<RgbaFloat> source)
    {
        var pixels = Pixels;
        for (var y = 0; y < _height; y++)
        {
            var row = source.GetRow(y);
            for (var x = 0; x < _width; x++)
            {
                var pixel = row[x];
                var index = 4 * ((y * _width) + x);
                pixels[index] = (double)pixel.R * pixel.A;
                pixels[index + 1] = (double)pixel.G * pixel.A;
                pixels[index + 2] = (double)pixel.B * pixel.A;
                pixels[index + 3] = pixel.A;
            }
        }
    }

    /// <summary>Writes the frame: the pixels outside the mask as they are, the filled ones unpremultiplied.</summary>
    private void Store(PixelRowSource<RgbaFloat> source, PixelAccessor<RgbaFloat> destination)
    {
        var pixels = Pixels;
        for (var y = 0; y < _height; y++)
        {
            var row = destination.GetRowSpan(y);
            source.GetRow(y).CopyTo(row);
            for (var x = 0; x < _width; x++)
            {
                if (!_mask.Get((y * _width) + x))
                    continue;

                var index = 4 * ((y * _width) + x);
                var alpha = pixels[index + 3];
                row[x] = alpha == 0
                    ? new RgbaFloat(0, 0, 0, 0)
                    : new RgbaFloat((float)(pixels[index] / alpha), (float)(pixels[index + 1] / alpha), (float)(pixels[index + 2] / alpha), (float)alpha);
            }
        }
    }

    /// <summary>Flags the pixels of the mask as inside, and puts the known pixels next to them in the band.</summary>
    private void Initialize()
    {
        var flags = Flags;
        var distances = Distances;
        var positions = Positions;
        _heapCount = 0;
        for (var index = 0; index < Count; index++)
        {
            positions[index] = -1;
            if (_mask.Get(index))
            {
                flags[index] = Inside;
                distances[index] = Unknown;
            }
            else
            {
                flags[index] = Known;
                distances[index] = 0;
            }
        }

        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var index = (y * _width) + x;
                if (flags[index] == Known && (IsInside(x - 1, y) || IsInside(x + 1, y) || IsInside(x, y - 1) || IsInside(x, y + 1)))
                {
                    flags[index] = Band;
                    Push(index);
                }
            }
        }
    }

    private bool IsInside(int x, int y) => (uint)x < (uint)_width && (uint)y < (uint)_height && Flags[(y * _width) + x] == Inside;

    private void March(CancellationToken cancellationToken)
    {
        var flags = Flags;
        var distances = Distances;
        var accepted = 0;
        while (_heapCount > 0)
        {
            if (++accepted % 4096 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var index = Pop();
            flags[index] = Known;
            var x = index % _width;
            var y = index / _width;
            for (var n = 0; n < 4; n++)
            {
                var nx = x + (n switch { 0 => -1, 1 => 1, _ => 0 });
                var ny = y + (n switch { 2 => -1, 3 => 1, _ => 0 });
                if ((uint)nx >= (uint)_width || (uint)ny >= (uint)_height)
                    continue;

                var neighbor = (ny * _width) + nx;
                if (flags[neighbor] == Known)
                    continue;

                var distance = Math.Min(Math.Min(Solve(nx - 1, ny, nx, ny - 1), Solve(nx + 1, ny, nx, ny - 1)), Math.Min(Solve(nx - 1, ny, nx, ny + 1), Solve(nx + 1, ny, nx, ny + 1)));
                if (flags[neighbor] == Inside)
                {
                    // The pixel has no color until it is filled: its neighbors do not read it
                    distances[neighbor] = distance;
                    Fill(nx, ny);
                    flags[neighbor] = Band;
                    Push(neighbor);
                }
                else if (distance < distances[neighbor])
                {
                    distances[neighbor] = distance;
                    MoveUp(Positions[neighbor]);
                }
            }
        }
    }

    /// <summary>Solves the eikonal equation at a pixel from a horizontal and a vertical neighbor, from the accepted ones only.</summary>
    private double Solve(int x1, int y1, int x2, int y2)
    {
        var a = GetAcceptedDistance(x1, y1);
        var b = GetAcceptedDistance(x2, y2);
        if (a >= Unknown && b >= Unknown)
            return Unknown;

        if (a >= Unknown || b >= Unknown || Math.Abs(a - b) >= 1)
            return Math.Min(a, b) + 1;

        var difference = a - b;
        return (a + b + Math.Sqrt(2 - (difference * difference))) / 2;
    }

    private double GetAcceptedDistance(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            return Unknown;

        var index = (y * _width) + x;
        return Flags[index] == Known ? Distances[index] : Unknown;
    }

    /// <summary>Gets whether a pixel has a color: known, or already filled (in the band).</summary>
    private bool HasColor(int x, int y) => (uint)x < (uint)_width && (uint)y < (uint)_height && Flags[(y * _width) + x] != Inside;

    /// <summary>Fills a pixel from the pixels with a color within the radius.</summary>
    private void Fill(int x, int y)
    {
        var distances = Distances;
        var pixels = Pixels;
        var index = (y * _width) + x;
        var distance = distances[index];

        // The gradient of the distance at the pixel, from the neighbors that have one
        var gradientX = GetDistanceDerivative(x, y, 1, 0);
        var gradientY = GetDistanceDerivative(x, y, 0, 1);
        Span<double> sums = stackalloc double[4];
        Span<double> minimums = stackalloc double[4];
        Span<double> maximums = stackalloc double[4];
        sums.Clear();
        minimums.Fill(double.PositiveInfinity);
        maximums.Fill(double.NegativeInfinity);
        var totalWeight = 0.0;
        var radiusSquared = _radius * _radius;
        for (var qy = Math.Max(y - _radius, 0); qy <= Math.Min(y + _radius, _height - 1); qy++)
        {
            for (var qx = Math.Max(x - _radius, 0); qx <= Math.Min(x + _radius, _width - 1); qx++)
            {
                var dx = x - qx;
                var dy = y - qy;
                var lengthSquared = (dx * dx) + (dy * dy);
                if (lengthSquared == 0 || lengthSquared > radiusSquared || !HasColor(qx, qy))
                    continue;

                var q = (qy * _width) + qx;
                var direction = Math.Abs((dx * gradientX) + (dy * gradientY)) / Math.Sqrt(lengthSquared);
                var weight = Math.Max(direction, 1e-6) / lengthSquared / (1 + Math.Abs(distances[q] - distance));
                for (var c = 0; c < 4; c++)
                {
                    var value = pixels[(4 * q) + c];
                    var estimate = value + (GetColorDerivative(qx, qy, c, 1, 0) * dx) + (GetColorDerivative(qx, qy, c, 0, 1) * dy);
                    sums[c] += weight * estimate;
                    minimums[c] = Math.Min(minimums[c], value);
                    maximums[c] = Math.Max(maximums[c], value);
                }

                totalWeight += weight;
            }
        }

        for (var c = 0; c < 4; c++)
        {
            pixels[(4 * index) + c] = Math.Clamp(sums[c] / totalWeight, minimums[c], maximums[c]);
        }
    }

    /// <summary>Gets the derivative of the distance along an axis: central when both neighbors have one, one-sided otherwise, 0 without any.</summary>
    private double GetDistanceDerivative(int x, int y, int stepX, int stepY)
    {
        var distances = Distances;
        var center = distances[(y * _width) + x];
        var hasBefore = HasColor(x - stepX, y - stepY);
        var hasAfter = HasColor(x + stepX, y + stepY);
        var before = hasBefore ? distances[((y - stepY) * _width) + x - stepX] : 0;
        var after = hasAfter ? distances[((y + stepY) * _width) + x + stepX] : 0;
        return hasBefore && hasAfter ? (after - before) / 2 : hasAfter ? after - center : hasBefore ? center - before : 0;
    }

    /// <summary>Gets the derivative of a channel along an axis at a pixel with a color, from its neighbors with a color.</summary>
    private double GetColorDerivative(int x, int y, int channel, int stepX, int stepY)
    {
        var pixels = Pixels;
        var center = pixels[(4 * ((y * _width) + x)) + channel];
        var hasBefore = HasColor(x - stepX, y - stepY);
        var hasAfter = HasColor(x + stepX, y + stepY);
        var before = hasBefore ? pixels[(4 * (((y - stepY) * _width) + x - stepX)) + channel] : 0;
        var after = hasAfter ? pixels[(4 * (((y + stepY) * _width) + x + stepX)) + channel] : 0;
        return hasBefore && hasAfter ? (after - before) / 2 : hasAfter ? after - center : hasBefore ? center - before : 0;
    }

    // The band: a binary heap of pixel indices ordered by distance, then by index, with the position of each index in it

    private bool IsBefore(int first, int second)
    {
        var distances = Distances;
        return distances[first] < distances[second] || (distances[first] == distances[second] && first < second);
    }

    private void Push(int index)
    {
        Heap[_heapCount] = index;
        Positions[index] = _heapCount;
        _heapCount++;
        MoveUp(_heapCount - 1);
    }

    private int Pop()
    {
        var heap = Heap;
        var positions = Positions;
        var first = heap[0];
        _heapCount--;
        positions[first] = -1;
        if (_heapCount > 0)
        {
            heap[0] = heap[_heapCount];
            positions[heap[0]] = 0;
            MoveDown(0);
        }

        return first;
    }

    private void MoveUp(int position)
    {
        var heap = Heap;
        var positions = Positions;
        var item = heap[position];
        while (position > 0)
        {
            var parent = (position - 1) / 2;
            if (!IsBefore(item, heap[parent]))
                break;

            heap[position] = heap[parent];
            positions[heap[position]] = position;
            position = parent;
        }

        heap[position] = item;
        positions[item] = position;
    }

    private void MoveDown(int position)
    {
        var heap = Heap;
        var positions = Positions;
        var item = heap[position];
        while (true)
        {
            var child = (2 * position) + 1;
            if (child >= _heapCount)
                break;

            if (child + 1 < _heapCount && IsBefore(heap[child + 1], heap[child]))
            {
                child++;
            }

            if (!IsBefore(heap[child], item))
                break;

            heap[position] = heap[child];
            positions[heap[position]] = position;
            position = child;
        }

        heap[position] = item;
        positions[item] = position;
    }
}
