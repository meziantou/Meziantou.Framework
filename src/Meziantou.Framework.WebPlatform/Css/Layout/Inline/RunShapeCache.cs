using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The shaped texts of a <see cref="TextRun"/>, kept in its <see cref="LayoutBox.LayoutState"/> between the layout passes: a
/// relayout that does not change the box tree (the box tree is rebuilt when the styles or the fonts change) reuses them, even
/// when the bounded cache of the fonts (<see cref="TextShapeCache"/>) cannot hold all the texts of a large document. It also
/// keeps the parts of the texts shaped with the neighbors of the run (<see cref="Slice"/>).
/// </summary>
internal sealed class RunShapeCache
{
    // A run usually has one shaped text, kept in the cache itself; more when it is split by forced line breaks or bidi levels,
    // or styled by ::first-line, kept in the other entries (allocated on first use)
    private const int Capacity = 4;

    private Entry _first;
    private Entry[]? _others;
    private int _count;
    private int _next;

    // (the array of the slices grows up to the capacity: most runs have none or one)
    private (ShapedText Source, int Start, int End, ShapedText Slice)[] _slices = [];
    private int _sliceCount;
    private int _nextSlice;

    public static ShapedText Shape(TextRun? run, ReadOnlySpan<char> text, FontFallbackList fonts, in TextShapingOptions options)
    {
        var cache = run?.LayoutState as RunShapeCache;
        if (cache is not null)
        {
            for (var i = 0; i < cache._count; i++)
            {
                ref readonly var entry = ref cache.GetEntry(i);
                if (ReferenceEquals(entry.Fonts, fonts) && entry.Options == options && text.SequenceEqual(entry.Text))
                    return entry.Shaped;
            }
        }

        if (run is null)
            return TextShaper.Shape(text, fonts, options);

        // (the text of the entry of the cache of the fonts is shared)
        var shaped = TextShaper.ShapeWithCachedText(text, fonts, options, out var key);
        cache ??= GetOrCreate(run);
        cache.GetEntry(cache._next) = new Entry(key ?? text.ToString(), fonts, options, shaped);
        cache._next = (cache._next + 1) % Capacity;
        cache._count = Math.Min(cache._count + 1, Capacity);
        return shaped;
    }

    /// <summary>
    /// Returns the glyphs of the characters from <paramref name="start"/> to <paramref name="end"/> of a text shaped with the
    /// texts of the neighbors of a run (see <see cref="ShapedText.Slice"/>): the part of the run, which is the same in the next
    /// layouts while the shaped text of the group is the same.
    /// </summary>
    public static ShapedText Slice(TextRun? run, ShapedText shaped, int start, int end)
    {
        if (run is null || (start == 0 && end == shaped.Length))
            return shaped.Slice(start, end);

        var cache = GetOrCreate(run);
        for (var i = 0; i < cache._sliceCount; i++)
        {
            ref readonly var entry = ref cache._slices[i];
            if (ReferenceEquals(entry.Source, shaped) && entry.Start == start && entry.End == end)
                return entry.Slice;
        }

        var slice = shaped.Slice(start, end);
        if (cache._sliceCount == cache._slices.Length && cache._sliceCount < Capacity)
        {
            Array.Resize(ref cache._slices, Math.Min(Math.Max(cache._sliceCount * 2, 1), Capacity));
        }

        cache._slices[cache._nextSlice] = (shaped, start, end, slice);
        cache._nextSlice = (cache._nextSlice + 1) % Capacity;
        cache._sliceCount = Math.Min(cache._sliceCount + 1, Capacity);
        return slice;
    }

    private static RunShapeCache GetOrCreate(TextRun run)
    {
        if (run.LayoutState is not RunShapeCache cache)
        {
            cache = new RunShapeCache();
            run.LayoutState = cache;
        }

        return cache;
    }

    private ref Entry GetEntry(int index)
    {
        if (index == 0)
            return ref _first;

        _others ??= new Entry[Capacity - 1];
        return ref _others[index - 1];
    }

    private readonly record struct Entry(string Text, FontFallbackList Fonts, TextShapingOptions Options, ShapedText Shaped);
}
