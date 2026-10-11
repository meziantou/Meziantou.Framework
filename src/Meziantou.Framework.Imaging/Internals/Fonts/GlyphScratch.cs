using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The working storage of the decoding of a glyph, charged to an allocation scope. It grows to the largest glyph and is
/// kept by its owner, so that decoding the same glyphs again allocates nothing.
/// </summary>
/// <remarks>A font has no cache and no state of its own: several threads can decode its glyphs, each with its own scratch.</remarks>
internal sealed class GlyphScratch : IDisposable
{
    private const int InitialCapacity = 128;

    private readonly AllocationScope _scope;
    private PooledBuffer? _buffer;
    private int _capacity;

    public GlyphScratch(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets the number of points.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the points.</summary>
    public Span<GlyphPoint> Points => _buffer is null ? default : unsafe(MemoryMarshal.Cast<byte, GlyphPoint>(_buffer.Span))[..Count];

    /// <summary>Removes every point and keeps the storage.</summary>
    public void Reset() => Count = 0;

    /// <summary>Adds points, whose content is unspecified, and returns the index of the first one.</summary>
    /// <exception cref="ImageResourceLimitException">The points exceed the allocation limit.</exception>
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

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _capacity = 0;
        Count = 0;
    }

    private void Grow(long required)
    {
        var capacity = Math.Max(required, _capacity == 0 ? InitialCapacity : (long)_capacity * 2);
        var buffer = ResampleWeights.Rent<GlyphPoint>(_scope, capacity);
        if (_buffer is not null)
        {
            _buffer.Span.CopyTo(buffer.Span);
            _buffer.Dispose();
        }

        _buffer = buffer;
        _capacity = (int)Math.Min(capacity, int.MaxValue);
    }
}
