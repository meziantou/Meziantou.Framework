using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The value of <c>initial-letter</c> of an initial letter box (https://drafts.csswg.org/css-inline-3/#sizing-drop-initials):
/// the number of lines the initial letter occupies, and the number of lines it sinks (a dropped initial sinks its size, a
/// raised initial 1 line, a sunken initial is in between).
/// </summary>
/// <param name="Size">The initial letter size.</param>
/// <param name="Sink">The initial letter sink.</param>
/// <param name="LineHeight">The line height of the containing block, the unit of the size and of the sink.</param>
/// <param name="CapHeight">The cap height of the first available font of the containing block.</param>
internal sealed record InitialLetterBox(double Size, int Sink, double LineHeight, double CapHeight)
{
    /// <summary>
    /// The distance by which the initial letter extends above the cap height of its originating line: "Initial letters that extend
    /// above the first line of text, known as raised caps or sunken caps, do not extend up into previous elements"
    /// (https://drafts.csswg.org/css-inline-3/#raised-sunken-caps): the line is moved down by the lines that are not sunk.
    /// </summary>
    public double Raise => Math.Max(0, (Size - Sink) * LineHeight);
}

/// <summary>
/// The initial letters (https://drafts.csswg.org/css-inline-3/#initial-letter-styling): a <c>::first-letter</c> pseudo-element
/// whose <c>initial-letter</c> is not <c>normal</c> is an initial letter box, sized to span lines of its containing block and
/// placed at the start of its line; the lines next to it are shortened as they are next to a float (see
/// <see cref="InlineLayoutAlgorithm"/>).
/// </summary>
internal static class InitialLetter
{
    /// <summary>The properties whose computed values are kept by the box: their font-relative lengths refer to the computed font size, not to the used one.</summary>
    private static readonly string[] BoxProperties =
    [
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "letter-spacing", "word-spacing",
    ];

    /// <summary>
    /// Parses a computed value of <c>initial-letter</c>: <c>normal | &lt;number&gt; &lt;integer&gt; | &lt;number&gt; &amp;&amp; [ drop | raise ]?</c>.
    /// Returns the size and the sink ("If the initial letter sink value is omitted, drop is assumed": "an initial letter sink equal
    /// to the initial letter size floored to the nearest positive whole number"; raise is a sink of 1), or null for <c>normal</c>.
    /// </summary>
    public static (double Size, int Sink)? Parse(string value)
    {
        if (value is "normal" or "")
            return null;

        double? size = null;
        int? sink = null;
        var drops = true;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "drop")
            {
                drops = true;
            }
            else if (part == "raise")
            {
                drops = false;
                sink = 1;
            }
            else if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                if (size is null)
                {
                    size = number;
                }
                else
                {
                    sink = (int)Math.Round(number, MidpointRounding.AwayFromZero);
                    drops = false;
                }
            }
            else
            {
                return null;
            }
        }

        if (size is not { } letterSize || !double.IsFinite(letterSize) || letterSize < 1)
            return null;

        return (letterSize, Math.Max(1, drops ? (int)Math.Floor(letterSize) : sink ?? 1));
    }

    /// <summary>
    /// Returns the used style of the initial letter box of a <c>::first-letter</c> pseudo-element whose <c>initial-letter</c> is
    /// not <c>normal</c>, with the values of the initial letter; null when the pseudo-element is not an initial letter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// https://drafts.csswg.org/css-inline-3/#sizing-initial-letter: the used font size is the one whose cap height spans the
    /// lines of the size: "Font size of drop cap = ((N-1) * line-height + [cap-height of para] * [font size of paragraph]) /
    /// [cap-height ratio of drop initial font]", "based only on computed values and font metrics"; <c>font-size</c> and
    /// <c>line-height</c> do not apply to the initial letter (https://drafts.csswg.org/css-inline-3/#initial-letter-properties).
    /// The line height of the box places the over alignment point of the letter (the cap height of its font; the over edge of
    /// its content area for the central baseline of vertical text) at the block-start edge of its content box.
    /// </para>
    /// <para>
    /// The box is taken out of the line as a float of the inline-start side is: the lines next to it are shortened, it is
    /// painted with the floats, and the text decorations of its ancestors are not drawn on it. The inline layout places it
    /// (it is not placed as a float).
    /// </para>
    /// </remarks>
    /// <param name="style">The computed style of the <c>::first-letter</c> pseudo-element.</param>
    /// <param name="containingBlock">The style of the block container whose line contains the first letter.</param>
    /// <param name="box">The values of the initial letter.</param>
    public static ComputedStyle? CreateBoxStyle(ComputedStyle style, ComputedStyle containingBlock, out InitialLetterBox? box)
    {
        box = null;
        if (style.Layout.IsFloating || style.Layout.IsAbsolutelyPositioned || Parse(style.GetComputedValue("initial-letter")) is not var (size, sink))
            return null;

        var blockLayout = containingBlock.Layout;
        var lineHeight = blockLayout.UsedLineHeight;
        var blockCapHeight = containingBlock.Fonts.PrimaryFont.GetMetrics(blockLayout.FontSize).CapHeight;
        var letterMetrics = style.Fonts.PrimaryFont.GetMetrics(style.Layout.FontSize);
        if (!(lineHeight > 0) || !(letterMetrics.CapHeight > 0) || !(letterMetrics.EmSize > 0))
            return null;

        // The cap height of the letter spans the lines: the cap height of the last line and the line heights before it
        var capHeight = ((size - 1) * lineHeight) + blockCapHeight;
        var fontSize = capHeight * style.Layout.FontSize / letterMetrics.CapHeight;
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            return null;

        // The line box of the letter: its baseline is at the cap height from the block-start edge (the half-leading is the cap
        // height minus the ascent); the content area of the font for the central baseline
        var usedMetrics = style.Fonts.PrimaryFont.GetMetrics(fontSize);
        var boxLineHeight = blockLayout.HasCentralDominantBaseline
            ? usedMetrics.Ascent + usedMetrics.Descent
            : Math.Max(0, (2 * usedMetrics.CapHeight) - usedMetrics.Ascent + usedMetrics.Descent);

        var declarations = new List<CssLonghandValue>(BoxProperties.Length + 4);
        foreach (var property in BoxProperties)
        {
            declarations.Add(new CssLonghandValue(property, style.GetComputedValue(property)));
        }

        // The effective zoom is applied to the used values: the declarations are computed values
        var zoom = style.Layout.EffectiveZoom;
        declarations.Add(new CssLonghandValue("font-size", string.Create(CultureInfo.InvariantCulture, $"{fontSize / zoom:R}px")));
        declarations.Add(new CssLonghandValue("line-height", string.Create(CultureInfo.InvariantCulture, $"{boxLineHeight / zoom:R}px")));
        declarations.Add(new CssLonghandValue("float", "inline-start"));
        declarations.Add(new CssLonghandValue("clear", "none"));
        box = new InitialLetterBox(size, sink, lineHeight, blockCapHeight);
        return style.WithDeclarations(declarations);
    }

    /// <summary>
    /// Returns the block size of the content box of an initial letter box whose block size is automatic, from its first layout
    /// (https://drafts.csswg.org/css-inline-3/#initial-letter-box-size): "its content box is sized to fit both: the specified
    /// sink (i.e the space between the over alignment point and the under alignment point); the glyph outlines of all the glyphs
    /// it contains". Its block-start content edge is at the over alignment point ("any content overflowing above that point is
    /// ignored for the purpose of layout"), so the box ends at the alphabetic baseline, or below it at the lowest glyph outline.
    /// Null when the size of the first layout is kept: the central baseline of vertical text (the box is the content area).
    /// </summary>
    public static double? GetContentBlockSize(BoxFragment fragment)
    {
        if (fragment.Box.Style.Layout.HasCentralDominantBaseline || fragment.FirstBaseline is not { } baseline)
            return null;

        var edges = fragment.WritingDirection.ToLogical(fragment.Border.Add(fragment.Padding));
        var sink = Math.Max(0, baseline - edges.BlockStart);
        return sink + GetInkDescent(fragment);
    }

    /// <summary>Returns the distance from the baseline to the lowest edge of the bounding boxes of the glyphs of a fragment (0 when no glyph goes below the baseline).</summary>
    private static double GetInkDescent(Fragment fragment)
    {
        var descent = 0d;
        if (fragment is TextFragment { ShapedText: { } shaped } text)
        {
            foreach (var run in shaped.Runs)
            {
                var from = Math.Max(run.GlyphStart, text.GlyphStart);
                var to = Math.Min(run.GlyphStart + run.GlyphCount, text.GlyphStart + text.GlyphCount);
                var scale = run.EmSize / run.Font.Font.UnitsPerEm;
                for (var i = from; i < to; i++)
                {
                    var glyph = shaped.Glyphs[i];
                    if (!glyph.IsInvisible && run.Font.Font.TryGetGlyphBounds(glyph.GlyphId, out var bounds))
                    {
                        descent = Math.Max(descent, glyph.OffsetY - (bounds.YMin * scale));
                    }
                }
            }

            return descent;
        }

        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is BoxFragment { Box.IsOutOfFlow: true })
                continue;

            descent = Math.Max(descent, GetInkDescent(child));
        }

        return descent;
    }
}
