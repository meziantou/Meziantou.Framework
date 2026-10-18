using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The shaped texts of a <see cref="TextRun"/>, kept in its <see cref="LayoutBox.LayoutState"/> between the layout passes: a
/// relayout that does not change the box tree (the box tree is rebuilt when the styles or the fonts change) reuses them, even
/// when the bounded cache of the fonts (<see cref="TextShapeCache"/>) cannot hold all the texts of a large document.
/// </summary>
internal sealed class RunShapeCache
{
    // A run usually has one shaped text; more when it is split by forced line breaks or bidi levels, or styled by ::first-line
    private const int Capacity = 4;

    private readonly (string Text, FontFallbackList Fonts, TextShapingOptions Options, ShapedText Shaped)[] _entries = new (string, FontFallbackList, TextShapingOptions, ShapedText)[Capacity];
    private int _count;
    private int _next;

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

        var shaped = TextShaper.Shape(text, fonts, options);
        if (run is not null)
        {
            if (cache is null)
            {
                cache = new RunShapeCache();
                run.LayoutState = cache;
            }

            cache._entries[cache._next] = (text.ToString(), fonts, options, shaped);
            cache._next = (cache._next + 1) % Capacity;
            cache._count = Math.Min(cache._count + 1, Capacity);
        }

        return shaped;
    }
}
