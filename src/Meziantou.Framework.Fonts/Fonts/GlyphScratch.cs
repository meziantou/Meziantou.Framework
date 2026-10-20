using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The working storage of the decoding of a glyph, charged to an allocator. It grows to the largest glyph and is
/// kept by its owner, so that decoding the same glyphs again allocates nothing.
/// </summary>
/// <remarks>A font has no cache and no state of its own: several threads can decode its glyphs, each with its own scratch.</remarks>
public sealed class GlyphScratch : IDisposable
{
    private const int InitialCapacity = 128;

    private readonly FontAllocator _scope;
    private FontBuffer? _buffer;
    private int _capacity;
    private FontBuffer? _work;
    private int _workCapacity;
    private int _workCount;

    /// <summary>Initializes a new instance of the <see cref="GlyphScratch"/> class.</summary>
    /// <param name="scope">The allocator the storage is rented from.</param>
    public GlyphScratch(FontAllocator scope)
    {
        _scope = scope;
    }

    /// <summary>Gets the number of points.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the points.</summary>
    internal Span<GlyphPoint> Points => _buffer is null ? default : unsafe(MemoryMarshal.Cast<byte, GlyphPoint>(_buffer.Span))[..Count];

    /// <summary>Removes every point and keeps the storage.</summary>
    public void Reset()
    {
        Count = 0;
        _workCount = 0;
    }

    /// <summary>Adds points, whose content is unspecified, and returns the index of the first one.</summary>
    /// <exception cref="FontResourceLimitException">The points exceed the allocation limit.</exception>
    public int Add(int count)
    {
        var start = Count;
        var required = (long)Count + count;
        if (required > _capacity)
        {
            Grow(required);
        }

        Count = (int)required;
        return start;
    }

    /// <summary>Gets the working numbers that are rented.</summary>
    public Span<double> Work => _work is null ? default : unsafe(MemoryMarshal.Cast<byte, double>(_work.Span))[.._workCount];

    /// <summary>
    /// Rents working numbers, set to zero, after the ones already rented. The storage can move: a span of
    /// <see cref="Work"/> must not be kept across another rental.
    /// </summary>
    /// <returns>The index of the first number in <see cref="Work"/>.</returns>
    public int RentWork(long count)
    {
        var start = _workCount;
        var required = start + count;
        if (required > _workCapacity)
        {
            var capacity = Math.Max(required, _workCapacity == 0 ? InitialCapacity : (long)_workCapacity * 2);
            var buffer = _scope.Rent<double>(capacity);
            if (_work is not null)
            {
                _work.Span.CopyTo(buffer.Span);
                _work.Dispose();
            }

            _work = buffer;
            _workCapacity = (int)Math.Min(capacity, int.MaxValue);
        }

        _workCount = (int)required;
        Work[start..].Clear();
        return start;
    }

    /// <summary>Returns the working numbers rented last.</summary>
    public void ReturnWork(int start) => _workCount = start;

    /// <summary>Returns the storage to the allocator.</summary>
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _capacity = 0;
        Count = 0;
        _work?.Dispose();
        _work = null;
        _workCapacity = 0;
        _workCount = 0;
    }

    private void Grow(long required)
    {
        var capacity = Math.Max(required, _capacity == 0 ? InitialCapacity : (long)_capacity * 2);
        var buffer = _scope.Rent<GlyphPoint>(capacity);
        if (_buffer is not null)
        {
            _buffer.Span.CopyTo(buffer.Span);
            _buffer.Dispose();
        }

        _buffer = buffer;
        _capacity = (int)Math.Min(capacity, int.MaxValue);
    }
}
