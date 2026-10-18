namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The text measurement cache of a <see cref="FontFallbackList"/>: the shaped texts and the widths by text and options. It is
/// discarded with its list when the fonts of the document change (a font face is loaded, added or removed, a style sheet or
/// the environment changes: see <see cref="FontSelector"/>), so it needs no other invalidation.
/// </summary>
/// <remarks>
/// The lookups take a <see cref="ReadOnlySpan{T}"/> of the text (no allocation on a hit). The cache is bounded: it is cleared
/// when it holds too many entries or too many characters.
/// </remarks>
internal sealed class TextShapeCache
{
    /// <summary>The maximum number of entries (shaped texts and widths).</summary>
    internal const int MaxEntries = 16384;

    /// <summary>The maximum number of UTF-16 code units of the cached texts.</summary>
    internal const int MaxCharacters = 1 << 20;

    /// <summary>The largest buffer of the shaping kept for the next texts (a larger one is returned to the array pool).</summary>
    private const int MaxKeptBufferCapacity = 4096;

    private readonly Dictionary<TextShapingOptions, Entries> _entries = [];
    private ShapingBuffer? _buffer;

    /// <summary>Returns a buffer for the shaping (the one of the last shaping when no other shaping uses it).</summary>
    public ShapingBuffer RentBuffer() => Interlocked.Exchange(ref _buffer, null) ?? new ShapingBuffer();

    /// <summary>Returns a buffer after a shaping: it is kept for the next one when it is not too large.</summary>
    public void ReturnBuffer(ShapingBuffer buffer)
    {
        if (buffer.Capacity > MaxKeptBufferCapacity || Interlocked.CompareExchange(ref _buffer, buffer, null) is not null)
        {
            buffer.Dispose();
        }
    }
    private int _count;
    private int _characters;

    /// <summary>The number of cached entries.</summary>
    public int Count => _count;

    public bool TryGetShapedText(ReadOnlySpan<char> text, in TextShapingOptions options, [NotNullWhen(true)] out ShapedText? result)
    {
        if (_entries.TryGetValue(options, out var entries) && entries.Shaped is { } shaped)
            return shaped.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out result);

        result = null;
        return false;
    }

    public bool TryGetWidth(ReadOnlySpan<char> text, in TextShapingOptions options, out double width)
    {
        if (_entries.TryGetValue(options, out var entries))
        {
            if (entries.Widths is { } widths && widths.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out width))
                return true;

            if (entries.Shaped is { } shaped && shaped.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out var result))
            {
                width = result.Width;
                return true;
            }
        }

        width = 0;
        return false;
    }

    public void AddShapedText(ReadOnlySpan<char> text, in TextShapingOptions options, ShapedText value)
    {
        if (Reserve(text.Length))
        {
            var entries = GetEntries(options);
            (entries.Shaped ??= new(StringComparer.Ordinal)).GetAlternateLookup<ReadOnlySpan<char>>()[text] = value;
        }
    }

    public void AddWidth(ReadOnlySpan<char> text, in TextShapingOptions options, double width)
    {
        if (Reserve(text.Length))
        {
            var entries = GetEntries(options);
            (entries.Widths ??= new(StringComparer.Ordinal)).GetAlternateLookup<ReadOnlySpan<char>>()[text] = width;
        }
    }

    /// <summary>Removes all the entries.</summary>
    public void Clear()
    {
        _entries.Clear();
        _count = 0;
        _characters = 0;
    }

    private Entries GetEntries(in TextShapingOptions options)
    {
        if (!_entries.TryGetValue(options, out var entries))
        {
            entries = new Entries();
            _entries[options] = entries;
        }

        return entries;
    }

    /// <summary>Makes room for an entry, clearing the cache when it is full; false when the text is too long to be cached.</summary>
    private bool Reserve(int length)
    {
        if (length > MaxCharacters / 16)
            return false;

        if (_count >= MaxEntries || _characters + length > MaxCharacters)
        {
            Clear();
        }

        _count++;
        _characters += length;
        return true;
    }

    private sealed class Entries
    {
        public Dictionary<string, ShapedText>? Shaped { get; set; }

        public Dictionary<string, double>? Widths { get; set; }
    }
}
