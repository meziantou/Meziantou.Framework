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
    // A run usually has one shaped text; more when it is split by forced line breaks or bidi levels, or styled by ::first-line
    private const int Capacity = 4;

    // (the arrays grow up to the capacity: most runs have one entry)
    private (string Text, FontFallbackList Fonts, TextShapingOptions Options, ShapedText Shaped)[] _entries = [];
    private int _count;
    private int _next;

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
                ref readonly var entry = ref cache._entries[i];
                if (ReferenceEquals(entry.Fonts, fonts) && entry.Options == options && text.SequenceEqual(entry.Text))
                    return entry.Shaped;
            }
        }

        if (run is null)
            return TextShaper.Shape(text, fonts, options);

        // (the text of the entry of the cache of the fonts is shared)
        var shaped = TextShaper.ShapeWithCachedText(text, fonts, options, out var key);
        cache ??= GetOrCreate(run);
        Add(ref cache._entries, ref cache._count, ref cache._next, (key ?? text.ToString(), fonts, options, shaped));
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
        Add(ref cache._slices, ref cache._sliceCount, ref cache._nextSlice, (shaped, start, end, slice));
        return slice;
    }

    /// <summary>Adds an entry to the entries of a cache, replacing the oldest one when the capacity is reached.</summary>
    private static void Add<T>(ref T[] entries, ref int count, ref int next, T entry)
    {
        if (count == entries.Length && count < Capacity)
        {
            Array.Resize(ref entries, Math.Min(Math.Max(count * 2, 1), Capacity));
        }

        entries[next] = entry;
        next = (next + 1) % Capacity;
        count = Math.Min(count + 1, Capacity);
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
}
