using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The symbol of a list marker that is drawn as a shape instead of the glyph of its marker string, as in Blink and Gecko: the
/// markers of the <c>disc</c>, <c>circle</c>, <c>square</c>, <c>disclosure-open</c> and <c>disclosure-closed</c> counter
/// styles whose <c>content</c> is <c>normal</c> (https://drafts.csswg.org/css-counter-styles-3/#simple-symbolic: "user agents
/// may instead render these styles using a UA-generated image or a UA-chosen font").
/// </summary>
internal enum MarkerSymbol : byte
{
    None,
    Disc,
    Circle,
    Square,
    DisclosureOpen,
    DisclosureClosed,
}

/// <summary>
/// The geometry of the symbols of the list markers (<see cref="MarkerSymbol"/>), the one of Blink (<c>ListMarker::WidthOfSymbol</c>,
/// <c>RelativeSymbolMarkerRect</c> and <c>InlineMarginsForOutside</c>): computed from the ascent of the primary font of the
/// marker rounded to a whole pixel (A), a bullet is a square of <c>(⌊2A/3⌋ + 1) / 2</c> pixels (integer divisions) in a box of
/// that size plus 2px, 1px from its start and <c>3 (A - ⌊2A/3⌋) / 2</c> below the top of the content area of the font; a
/// disclosure triangle fills a square of 0.66em whose bottom is on the baseline. The box is as tall as the used line height,
/// as the text of the marker in Blink. The sizes are the zoomed ones of the layout.
/// </summary>
internal static class MarkerSymbols
{
    /// <summary>The space between an outside marker and the content of its list item (Blink's <c>kCMarkerPaddingPx</c>, plus 1px for the bullets).</summary>
    private const double OutsideMarkerPadding = 7;

    /// <summary>Returns the symbol of a list-style-type, or <see cref="MarkerSymbol.None"/>.</summary>
    public static MarkerSymbol Get(string listStyleType) => listStyleType switch
    {
        "disc" => MarkerSymbol.Disc,
        "circle" => MarkerSymbol.Circle,
        "square" => MarkerSymbol.Square,
        "disclosure-open" => MarkerSymbol.DisclosureOpen,
        "disclosure-closed" => MarkerSymbol.DisclosureClosed,
        _ => MarkerSymbol.None,
    };

    private static bool IsDisclosure(MarkerSymbol symbol) => symbol is MarkerSymbol.DisclosureOpen or MarkerSymbol.DisclosureClosed;

    /// <summary>The rounded ascent of the primary font of a style, at its zoomed font size (Blink's <c>FontMetrics::Ascent()</c>).</summary>
    private static int GetRoundedAscent(ComputedStyle style)
        => (int)Math.Round(style.Fonts.PrimaryFont.GetMetrics(style.Layout.FontSize).Ascent, MidpointRounding.AwayFromZero);

    /// <summary>The size of a disclosure triangle: 0.66em, truncated to the 1/64px of the layout units of Blink.</summary>
    private static double GetDisclosureSize(ComputedStyle style) => Math.Floor(style.Layout.FontSize * 0.66 * 64) / 64;

    /// <summary>
    /// Returns the size of the box of a symbol in the axes of its line: the inline size of the symbol (0 with a font size of 0),
    /// and, as for the text of the marker in Blink, the used line height, with the baseline of the text of the inline layout
    /// (the ascent of the primary font and the half-leading, https://drafts.csswg.org/css-inline-3/#half-leading).
    /// </summary>
    public static (double Inline, double Block, double Ascent) GetSize(ComputedStyle style, MarkerSymbol symbol)
    {
        var ascent = GetRoundedAscent(style);
        double inline;
        if (style.Layout.FontSize <= 0)
        {
            inline = 0;
        }
        else if (IsDisclosure(symbol))
        {
            inline = GetDisclosureSize(style);
        }
        else
        {
            inline = ((ascent * 2 / 3) + 1) / 2 + 2;
        }

        return (inline, style.Layout.UsedLineHeight, GetBaseline(style));
    }

    /// <summary>
    /// The baseline of the box of a symbol, from its line-over edge (see <see cref="GetSize"/>): the ascent and the half-leading
    /// above it, floored to whole pixels as the one of the text (see <c>InlineLayoutAlgorithm.GetHalfLeadings</c>).
    /// </summary>
    private static double GetBaseline(ComputedStyle style)
    {
        var metrics = style.Fonts.PrimaryFont.GetMetrics(style.Layout.FontSize);
        return metrics.Ascent + Math.Floor((style.Layout.UsedLineHeight - (metrics.Ascent + metrics.Descent)) / 2);
    }

    /// <summary>
    /// Returns the space between the box of the symbol of an outside marker and the content of its list item: in Blink, the
    /// marker box is <c>⌊2A/3⌋ + 8</c> pixels (the size of a disclosure triangle + 7px) before the content.
    /// </summary>
    public static double GetOutsideGap(ComputedStyle style, MarkerSymbol symbol)
    {
        var (inline, _, _) = GetSize(style, symbol);
        var ascent = GetRoundedAscent(style);
        var offset = IsDisclosure(symbol) ? GetDisclosureSize(style) : ascent * 2 / 3;
        return offset + OutsideMarkerPadding + 1 - inline;
    }

    /// <summary>Returns the rectangle of the shape of a symbol in its box, line-relative (from the line-left and line-over edges).</summary>
    public static (double LineLeft, double LineOver, double InlineSize, double BlockSize) GetShapeRect(ComputedStyle style, MarkerSymbol symbol)
    {
        // (the shapes are positioned from the baseline, as in Blink where the top of the text is the baseline minus the rounded ascent)
        var ascent = GetRoundedAscent(style);
        var baseline = GetBaseline(style);
        if (IsDisclosure(symbol))
        {
            var size = GetDisclosureSize(style);
            return (0, baseline - size, size, size);
        }

        var twoThirds = ascent * 2 / 3;
        var bullet = (twoThirds + 1) / 2;
        return (1, baseline - ascent + (3 * (ascent - twoThirds) / 2), bullet, bullet);
    }
}
