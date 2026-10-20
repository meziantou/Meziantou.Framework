using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The shaped texts of a <see cref="TextRun"/>, kept in its <see cref="LayoutBox.LayoutState"/> between the layout passes: a
/// relayout that does not change the box tree (the box tree is rebuilt when the styles or the fonts change) reuses them, even
/// when the bounded cache of the fonts (<see cref="TextShapeCache"/>) cannot hold all the texts of a large document.
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

    private RunShapeCache(in Entry first)
    {
        _first = first;
        _count = 1;
        _next = 1;
    }

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

        var shaped = TextShaper.Shape(text, fonts, options);
        if (run is not null)
        {
            var entry = new Entry(text.ToString(), fonts, options, shaped);
            if (cache is null)
            {
                run.LayoutState = new RunShapeCache(entry);
            }
            else
            {
                cache.GetEntry(cache._next) = entry;
                cache._next = (cache._next + 1) % Capacity;
                cache._count = Math.Min(cache._count + 1, Capacity);
            }
        }

        return shaped;
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
