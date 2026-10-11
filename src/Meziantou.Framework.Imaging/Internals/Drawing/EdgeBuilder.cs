using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Collects the edges of one draw call, clipped to the frame, in scratch storage charged to the scope of the image. The
/// storage grows to the largest draw call of the session and is released with it.
/// </summary>
/// <remarks>
/// Clipping keeps the winding number of every point of the frame: an edge is cut at the top and at the bottom of the
/// frame, dropped when it lies entirely to the right of the frame (it has no point of the frame on its right), and moved
/// onto the left side of the frame when it lies entirely to the left of it (every point of the frame is on its right
/// either way).
/// </remarks>
internal sealed class EdgeBuilder : IDisposable
{
    private const int InitialCapacity = 64;

    private readonly AllocationScope _scope;
    private readonly double _width;
    private readonly double _height;
    private PooledBuffer? _buffer;
    private int _capacity;

    public EdgeBuilder(AllocationScope scope, int width, int height)
    {
        _scope = scope;
        _width = width;
        _height = height;
    }

    /// <summary>Gets the width of the frame.</summary>
    public double Width => _width;

    /// <summary>Gets the height of the frame.</summary>
    public double Height => _height;

    /// <summary>Gets the number of edges.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the edges.</summary>
    public Span<DrawingEdge> Edges => _buffer is null ? default : unsafe(MemoryMarshal.Cast<byte, DrawingEdge>(_buffer.Span))[..Count];

    /// <summary>Removes every edge and keeps the storage.</summary>
    public void Reset() => Count = 0;

    /// <summary>
    /// Removes the edges added since the builder had <paramref name="count"/> edges. A closed contour that lies entirely
    /// outside the frame winds around no point of the frame: its edges can be removed as a whole, while the ones on
    /// the left of the frame could not be removed one by one.
    /// </summary>
    public void Truncate(int count) => Count = count;

    /// <summary>Adds the segment of a closed contour, from its first point to its second point.</summary>
    /// <exception cref="ImageResourceLimitException">The edges exceed the allocation limit.</exception>
    public void Add(double x0, double y0, double x1, double y1)
    {
        // A horizontal segment has no point on either side within its own (empty) range of rows
        if (y0 == y1)
            return;

        var direction = 1;
        if (y0 > y1)
        {
            (x0, y0, x1, y1) = (x1, y1, x0, y0);
            direction = -1;
        }

        if (y1 <= 0 || y0 >= _height)
            return;

        var top = y0;
        var left = x0;
        var slope = (x1 - x0) / (y1 - y0);
        if (y0 < 0)
        {
            x0 = left - (top * slope);
            y0 = 0;
        }

        if (y1 > _height)
        {
            x1 = left + ((_height - top) * slope);
            y1 = _height;
        }

        if (!(y0 < y1) || Math.Min(x0, x1) >= _width)
            return;

        if (Math.Max(x0, x1) <= 0)
        {
            x0 = 0;
            x1 = 0;
        }

        if (Count == _capacity)
        {
            Grow();
        }

        ref var edge = ref unsafe(MemoryMarshal.Cast<byte, DrawingEdge>(_buffer!.Span))[Count++];
        edge.X0 = x0;
        edge.Y0 = y0;
        edge.X1 = x1;
        edge.Y1 = y1;
        edge.Slope = (x1 - x0) / (y1 - y0);
        edge.Direction = direction;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _capacity = 0;
        Count = 0;
    }

    private void Grow()
    {
        var capacity = _capacity == 0 ? InitialCapacity : (long)_capacity * 2;
        var buffer = ResampleWeights.Rent<DrawingEdge>(_scope, capacity);
        if (_buffer is not null)
        {
            _buffer.Span.CopyTo(buffer.Span);
            _buffer.Dispose();
        }

        _buffer = buffer;
        _capacity = (int)capacity;
    }
}
