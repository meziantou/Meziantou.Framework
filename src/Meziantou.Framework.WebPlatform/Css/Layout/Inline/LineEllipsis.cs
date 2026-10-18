using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The ellipsis of a line: the string that represents the content elided by <c>text-overflow</c>
/// (https://drafts.csswg.org/css-overflow-4/#text-overflow) or by <c>block-ellipsis</c>
/// (https://drafts.csswg.org/css-overflow-4/#block-ellipsis), as text fragments attached to the line box (they are not in the
/// fragment tree: the geometry APIs, the hit testing and the scrollable overflow do not see them).
/// </summary>
/// <remarks>
/// <para>
/// The ellipsis is styled like the root inline box of the line (https://drafts.csswg.org/css-overflow-4/#ellipsing-details: "The
/// ellipsis is styled and baseline-aligned according to the block"): its fonts (U+2026, or three periods when the first available
/// font has no glyph for it), its color, its decorations (the decorations of the block container and of its ancestors, as for a
/// text directly in it) and the <c>::first-line</c> style on the first line. It is "treated as if it were wrapped in an anonymous
/// inline with <c>unicode-bidi: isolate</c> which inherits <c>direction</c> from the bidi paragraph": it has a fragment per bidi
/// level run, in visual order. The segment breaks of a &lt;string&gt; are transformed as collapsible segment breaks (a space, or
/// nothing between East Asian wide characters).
/// </para>
/// <para>
/// Usage: <see cref="Create(LineBoxFragment, string?)"/> shapes the ellipsis (its advance is <see cref="InlineSize"/>), then
/// <see cref="Place"/> puts it on its line at a line-relative position, its baseline on the baseline of the line. The painting
/// paints its <see cref="Fragments"/> after the inline content of the line (<see cref="PaintOrder"/>). <c>text-overflow</c> uses it
/// through <see cref="LineTruncator"/>; <c>block-ellipsis</c> places it in the space it reserves at the end of the line (see
/// <see cref="LineBoxFragment.BlockEllipsis"/>).
/// </para>
/// </remarks>
internal sealed class LineEllipsis
{
    private const char EllipsisCharacter = '\u2026';

    /// <summary>The characters of the segment breaks of a &lt;string&gt;.</summary>
    private const string SegmentBreaks = "\n\r\v\f\u0085\u2028\u2029";

    private readonly double _ascent;
    private readonly double _descent;
    private readonly WritingDirection _writingDirection;

    private LineEllipsis(TextFragment[] fragments, double inlineSize, double ascent, double descent, WritingDirection writingDirection)
    {
        Fragments = fragments;
        InlineSize = inlineSize;
        _ascent = ascent;
        _descent = descent;
        _writingDirection = writingDirection;
    }

    /// <summary>The text fragments of the ellipsis (one per bidi level run), in visual order from the line-left edge.</summary>
    public TextFragment[] Fragments { get; }

    /// <summary>The advance of the ellipsis along the line.</summary>
    public double InlineSize { get; }

    /// <summary>The position of the line-left edge of the ellipsis along its line, from the line-left edge of the line box (set by <see cref="Place"/>).</summary>
    public double LineLeft { get; private set; }

    /// <summary>
    /// Shapes the ellipsis of a line in the style of its root inline box (<see cref="LineBoxFragment.Style"/>): <paramref name="text"/>
    /// (a &lt;string&gt; value), or the ellipsis character when null. The line must be a child of the fragment of its block container.
    /// </summary>
    public static LineEllipsis Create(LineBoxFragment line, string? text)
    {
        var block = ((BoxFragment)line.Parent!).Box;
        return Create(block, line.Style ?? block.Style, text, line.IsRightToLeft);
    }

    /// <summary>
    /// Shapes an ellipsis for a line of a block container (see <see cref="Create(LineBoxFragment, string?)"/>): <paramref name="style"/>
    /// is the style of the root inline box of the line, <paramref name="isRightToLeft"/> the direction of its bidi paragraph.
    /// </summary>
    public static LineEllipsis Create(ContainerBox block, ComputedStyle style, string? text, bool isRightToLeft)
    {
        text = GetText(style, text);

        var writingDirection = new WritingDirection(style.Layout.WritingMode, Direction.Ltr);
        var (ascent, descent) = InlineLayoutAlgorithm.GetContentArea(style);
        var run = new TextRun(style, block.Element, pseudoElement: null, TextRunKind.Generated, node: null, text, 0, text.Length) { Parent = block };

        // The bidi level runs of the string, in an isolate with the direction of the paragraph
        List<BidiRun>? runs = null;
        if (isRightToLeft || NeedsBidi(text))
        {
            var levels = BidiResolver.GetLevels(text, isRightToLeft ? BidiParagraphDirection.RightToLeft : BidiParagraphDirection.LeftToRight);
            runs = [];
            BidiResolver.GetVisualRuns(levels, runs);
        }

        var count = runs?.Count ?? 1;
        var fragments = new TextFragment[count];
        var inlineSize = 0d;
        for (var i = 0; i < count; i++)
        {
            var (start, end, rightToLeft) = runs is null ? (0, text.Length, false) : (runs[i].Start, runs[i].End, runs[i].IsRightToLeft);
            var characters = text.AsSpan(start, end - start);
            if (rightToLeft)
            {
                characters = Mirror(characters);
            }

            var shaped = TextShaper.Shape(characters, style.Fonts, InlineLayoutAlgorithm.GetShapingOptions(style, rightToLeft));
            var size = writingDirection.ToPhysical(new LogicalSize(shaped.Width, ascent + descent));
            var baseline = writingDirection.IsLineOverBlockEnd ? descent : ascent;
            fragments[i] = new TextFragment(run, start, end, size, baseline, shaped, 0, shaped.Glyphs.Length) { IsEllipsis = true };
            inlineSize += shaped.Width;
        }

        return new LineEllipsis(fragments, inlineSize, ascent, descent, writingDirection);
    }

    /// <summary>
    /// Returns the string of an ellipsis in a style: a &lt;string&gt; value with its segment breaks transformed, or, for
    /// <paramref name="text"/> null, the ellipsis character.
    /// </summary>
    internal static string GetText(ComputedStyle style, string? text)
    {
        // "Implementations may substitute a more language, script, or writing-mode appropriate ellipsis character, or three dots
        // "..." if the ellipsis character is unavailable": the first available font decides, as in the browsers
        if (text is null)
            return style.Fonts.PrimaryFont.HasGlyph(EllipsisCharacter) ? "\u2026" : "...";

        return TransformSegmentBreaks(text);
    }

    /// <summary>Returns the string of the ellipsis of a side of <c>text-overflow</c> that elides (see <see cref="GetText(ComputedStyle, string?)"/>).</summary>
    internal static string GetText(ComputedStyle style, TextOverflowSide side) => GetText(style, side.Kind == TextOverflowKind.String ? side.String ?? "" : null);

    /// <summary>
    /// Places the ellipsis on a line box: its line-left edge at <paramref name="lineLeft"/> (a distance along the line from the
    /// line-left edge of the line box), its baseline on the baseline of the line box (the baseline of its root inline box). The
    /// fragments are attached to the line box (<see cref="Fragment.AttachTo"/>).
    /// </summary>
    public void Place(LineBoxFragment line, double lineLeft)
    {
        LineLeft = lineLeft;
        var lineSize = line.Size;
        var blockStart = line.Baseline - (_writingDirection.IsLineOverBlockEnd ? _descent : _ascent);
        var position = lineLeft;
        foreach (var fragment in Fragments)
        {
            var size = fragment.Size;
            var inlineSize = _writingDirection.IsHorizontal ? size.Width : size.Height;
            var x = _writingDirection.BlockStart switch
            {
                PhysicalSide.Left => blockStart,
                PhysicalSide.Right => lineSize.Width - blockStart - size.Width,
                _ => 0d,
            };
            var y = _writingDirection.BlockStart == PhysicalSide.Top ? blockStart : 0d;

            // The line-left side is the inline-start side of a left-to-right line
            switch (_writingDirection.InlineStart)
            {
                case PhysicalSide.Left:
                    x = position;
                    break;
                case PhysicalSide.Top:
                    y = position;
                    break;
                case PhysicalSide.Bottom:
                    y = lineSize.Height - position - inlineSize;
                    break;
            }

            fragment.AttachTo(line, new PhysicalOffset(x, y));
            position += inlineSize;
        }
    }

    /// <summary>
    /// Transforms the segment breaks of a &lt;string&gt; ("Forced line breaks within the string must be collapsed per CSS Text",
    /// https://drafts.csswg.org/css-text-4/#white-space-phase-1): a sequence of segment breaks (line feeds, carriage returns, and
    /// the other forced line breaks of UAX #14: the line and paragraph separators, as the browsers, the line tabulation, the
    /// form feed and the next line) collapses to one, which is transformed into a space, or removed
    /// between two East Asian wide characters (but Hangul and the emoji, as the browsers) or next to a U+200B
    /// (https://drafts.csswg.org/css-text-4/#line-break-transform).
    /// </summary>
    private static string TransformSegmentBreaks(string text)
    {
        if (text.AsSpan().IndexOfAny(SegmentBreaks) < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!IsSegmentBreak(c))
            {
                builder.Append(c);
                continue;
            }

            while (i + 1 < text.Length && IsSegmentBreak(text[i + 1]))
            {
                i++;
            }

            var before = builder.Length > 0 ? GetLastCodePoint(builder) : -1;
            var after = i + 1 < text.Length ? GraphemeClusters.ReadCodePoint(text, i + 1, out _) : -1;
            if (before < 0 || after < 0 || !LineBreaker.IsSegmentBreakRemoved(before, after) || UnicodeProperties.IsExtendedPictographic(before) || UnicodeProperties.IsExtendedPictographic(after))
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();

        static bool IsSegmentBreak(char c) => c is '\n' or '\r' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029';

        static int GetLastCodePoint(StringBuilder builder)
        {
            var last = builder[^1];
            return char.IsLowSurrogate(last) && builder.Length > 1 && char.IsHighSurrogate(builder[^2]) ? char.ConvertToUtf32(builder[^2], last) : last;
        }
    }

    /// <summary>Whether a text may need the bidi algorithm: a character from U+0590 (the right-to-left scripts, the bidi controls).</summary>
    internal static bool NeedsBidi(string text)
    {
        foreach (var c in text)
        {
            if (c >= 0x0590)
                return true;
        }

        return false;
    }

    /// <summary>Returns the characters of a right-to-left run with the mirrored glyphs of rule L4 (https://www.unicode.org/reports/tr9/#L4).</summary>
    internal static ReadOnlySpan<char> Mirror(ReadOnlySpan<char> text)
    {
        char[]? buffer = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (UnicodeProperties.IsBidiMirrored(c) && UnicodeProperties.GetBidiMirroringGlyph(c) is var mirrored and >= 0 and <= 0xFFFF)
            {
                buffer ??= text.ToArray();
                buffer[i] = (char)mirrored;
            }
        }

        return buffer ?? text;
    }
}
