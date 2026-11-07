using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// Keeps what drawing a glyph computes from one draw call to the next, up to a number of bytes: the coverage of a glyph
/// at a position in a pixel, and the display list of a glyph a font draws with its own colors.
/// </summary>
/// <remarks>
/// <para>
/// A cache is given to <see cref="Drawing.DrawingCanvas.FillGlyphs"/> by <see cref="GlyphRunOptions.Cache"/>. A glyph
/// is found again for the same font object (a <see cref="Font"/> and the fonts <see cref="Font.WithVariations(ReadOnlySpan{FontVariationSetting})"/>
/// makes are different fonts), glyph, size, transformation of the canvas, position in its pixel (a quarter of a pixel
/// along the line, a whole pixel across it), antialiasing and synthetic bold. The pixels are the same with or without a
/// cache: only the time and the allocations of a draw call change.
/// </para>
/// <para>
/// When an entry does not fit, the entries used least recently are removed. The bytes of an entry are an estimate of
/// what it holds. A cache is thread-safe; it holds the fonts of its entries until they are removed.
/// </para>
/// </remarks>
public sealed class GlyphCache
{
    private readonly Lock _lock = new();
    private readonly Dictionary<GlyphCacheKey, LinkedListNode<GlyphCacheEntry>> _entries = [];
    private readonly LinkedList<GlyphCacheEntry> _order = [];

    /// <summary>Initializes a new instance of the <see cref="GlyphCache"/> class.</summary>
    /// <param name="maxBytes">The most bytes the entries may hold.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is negative.</exception>
    public GlyphCache(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxBytes = maxBytes;
    }

    /// <summary>Gets the most bytes the entries may hold.</summary>
    public long MaxBytes { get; }

    /// <summary>Gets the bytes the entries hold.</summary>
    public long Bytes
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>Gets the number of entries.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _order.Clear();
            Bytes = 0;
        }
    }

    /// <summary>Finds an entry, and makes it the one used most recently.</summary>
    internal bool TryGet(in GlyphCacheKey key, out object? value)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Adds an entry, after the ones used least recently that it needs room for.</summary>
    internal void Add(in GlyphCacheKey key, object? value, long bytes)
    {
        lock (_lock)
        {
            if (bytes > MaxBytes || _entries.ContainsKey(key))
                return;

            while (Bytes + bytes > MaxBytes && _order.Last is { } last)
            {
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
                Bytes -= last.Value.Bytes;
            }

            _entries.Add(key, _order.AddFirst(new GlyphCacheEntry(key, value, bytes)));
            Bytes += bytes;
        }
    }
}
