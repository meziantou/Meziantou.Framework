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
    private readonly AllocationScope? _scope;
    private List<AllocationCharge>? _charges;
    private long _chargedBytes;

    /// <summary>Initializes a new instance of the <see cref="GlyphCache"/> class.</summary>
    /// <param name="maxBytes">The most bytes the entries may hold.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is negative.</exception>
    public GlyphCache(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxBytes = maxBytes;
    }

    /// <summary>Initializes a cache whose entries are charged to an allocation scope: an entry that does not fit in its limit is not kept.</summary>
    /// <param name="maxBytes">The most bytes the entries may hold.</param>
    /// <param name="scope">The scope the bytes of the entries are charged to, until the cache is cleared.</param>
    internal GlyphCache(long maxBytes, AllocationScope scope)
        : this(maxBytes)
    {
        _scope = scope;
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

    /// <summary>Gets whether the bytes of the entries are charged to an allocation scope.</summary>
    internal bool IsCharged => _scope is not null;

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
            if (_charges is not null)
            {
                foreach (var charge in _charges)
                {
                    charge.Dispose();
                }

                _charges.Clear();
                _chargedBytes = 0;
            }
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
            if (bytes > MaxBytes || _entries.ContainsKey(key) || !TryCharge(Math.Min(Bytes + bytes, MaxBytes)))
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

    /// <summary>Charges the scope of the cache, if any, so that it covers the bytes the entries will hold.</summary>
    /// <returns><see langword="false"/> when the charge would exceed the limit of the scope: nothing is charged.</returns>
    private bool TryCharge(long bytes)
    {
        if (_scope is null || bytes <= _chargedBytes)
            return true;

        // Charged by blocks of at least 64 KiB, released together when the cache is cleared
        var needed = bytes - _chargedBytes;
        var block = Math.Min(Math.Max(needed, 64 * 1024), MaxBytes - _chargedBytes);
        if (_scope.LiveBytes + block > _scope.Limit)
        {
            block = needed;
            if (_scope.LiveBytes + block > _scope.Limit)
                return false;
        }

        try
        {
            (_charges ??= []).Add(_scope.Charge(block, AllocationKind.Temporary));
        }
        catch (ImageResourceLimitException)
        {
            return false;
        }

        _chargedBytes += block;
        return true;
    }
}
