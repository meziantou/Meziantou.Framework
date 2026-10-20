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

    private readonly Dictionary<TextShapingOptions, Entries> _entries = [];
    private ShapingWorkspace? _workspace;

    // The entries of the last options looked up: the texts of a run of lines share their options, and comparing the options
    // is cheaper than hashing them
    private TextShapingOptions _lastOptions;
    private Entries? _lastEntries;

    /// <summary>Returns what the shaping needs (the one of the last shaping when no other shaping uses it).</summary>
    public ShapingWorkspace RentWorkspace() => Interlocked.Exchange(ref _workspace, null) ?? new ShapingWorkspace();

    /// <summary>Returns what a shaping used: it is kept for the next one (without the glyphs of a long run).</summary>
    public void ReturnWorkspace(ShapingWorkspace workspace)
    {
        workspace.TrimBuffer();
        if (Interlocked.CompareExchange(ref _workspace, workspace, null) is not null)
        {
            workspace.Dispose();
        }
    }

    private int _count;
    private int _characters;

    /// <summary>The number of cached entries.</summary>
    public int Count => _count;

    public bool TryGetShapedText(ReadOnlySpan<char> text, in TextShapingOptions options, [NotNullWhen(true)] out ShapedText? result)
    {
        if (FindEntries(options) is { Shaped: { } shaped })
            return shaped.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out result);

        result = null;
        return false;
    }

    /// <summary>Returns a cached shaped text, and the text of the entry (<paramref name="key"/>): it can be kept instead of a copy of <paramref name="text"/>.</summary>
    public bool TryGetShapedText(ReadOnlySpan<char> text, in TextShapingOptions options, [NotNullWhen(true)] out ShapedText? result, [NotNullWhen(true)] out string? key)
    {
        if (_entries.TryGetValue(options, out var entries) && entries.Shaped is { } shaped)
            return shaped.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out key, out result);

        key = null;
        result = null;
        return false;
    }

    public bool TryGetWidth(ReadOnlySpan<char> text, in TextShapingOptions options, out double width)
    {
        if (FindEntries(options) is { } entries)
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

    /// <summary>Caches a shaped text, and returns the text of the entry, or null when the text is too long to be cached.</summary>
    public string? AddShapedText(ReadOnlySpan<char> text, in TextShapingOptions options, ShapedText value)
    {
        if (!Reserve(text.Length))
            return null;

        var entries = GetEntries(options);
        var key = text.ToString();
        (entries.Shaped ??= new(StringComparer.Ordinal))[key] = value;
        return key;
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
        _lastEntries = null;
        _count = 0;
        _characters = 0;
    }

    private Entries? FindEntries(in TextShapingOptions options)
    {
        if (_lastEntries is { } last && _lastOptions == options)
            return last;

        if (!_entries.TryGetValue(options, out var entries))
            return null;

        _lastOptions = options;
        _lastEntries = entries;
        return entries;
    }

    private Entries GetEntries(in TextShapingOptions options)
    {
        if (FindEntries(options) is { } entries)
            return entries;

        entries = new Entries();
        _entries[options] = entries;
        _lastOptions = options;
        _lastEntries = entries;
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
