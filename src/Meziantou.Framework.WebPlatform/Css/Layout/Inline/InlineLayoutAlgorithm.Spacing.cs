using System.Buffers;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Selectors;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The spacing of the text (https://drafts.csswg.org/css-text-4/#spacing) that depends on the context of the characters: the
// CJK punctuation trimming (text-spacing-trim), the inter-script spacing (text-autospace), the tab stops (tab-size), the
// letter-spacing that is not applied at the end of a line, the hanging punctuation (hanging-punctuation) and the
// justification (text-justify). The spacing between the characters of an item is applied to its glyphs when it is shaped; the
// spacing at the edges of the lines and the justification are applied to the glyphs of the line (ShapedText.WithSpacing).
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>The space inserted before (after) each character of the text on the line being built (indexed by the position in the text).</summary>
    private double[] _spacingBefore = [];
    private double[] _spacingAfter = [];

    /// <summary>The range of the positions of <see cref="_spacingBefore"/> and <see cref="_spacingAfter"/> that may be non-zero.</summary>
    private int _spacingStart = int.MaxValue;
    private int _spacingEnd;

    /// <summary>The distance from the start content edge of the block container to the start edge of the line being broken (the tab stops).</summary>
    private double _lineStartOffset;

    /// <summary>Whether the line being broken or built follows a forced line break (<c>text-spacing-trim: space-first</c>).</summary>
    private bool _lineAfterForcedBreak;

    /// <summary>Whether the line being broken may end at a hyphenation opportunity (<c>hyphenate-limit-lines</c>, <c>hyphenate-limit-last</c>).</summary>
    private bool _lineAllowsHyphens = true;

    private ComputedStyle? _lastIdeographicStyle;
    private double _lastIdeographicAdvance;
    private double? _blockSpaceAdvance;

    /// <summary>The adjustments of the spacing at an edge of a line: the space added (negative: removed) and the advance of the punctuation that hangs.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LineEdgeSpacing(int Position, double Spacing, double ConditionalSpacing, double Hang, double ConditionalHang)
    {
        /// <summary>The change of the inline size of the content of the line when the content fits.</summary>
        public double Unconditional => Spacing - Hang;

        /// <summary>The additional change of the inline size of the content of the line when it does not fit otherwise.</summary>
        public double Conditional => ConditionalSpacing - ConditionalHang;

        /// <summary>
        /// The part of <see cref="Spacing"/> applied before the character instead of after it: the <c>letter-spacing</c> removed
        /// at the end of a line of right-to-left text, which is on the right of the glyphs (see <see cref="TextShaper"/>).
        /// </summary>
        public double SpacingBefore { get; init; }
    }

    /// <summary>
    /// Applies the spacing between the characters of a text item to its glyphs: the trimming of the adjacent fullwidth punctuation
    /// (https://drafts.csswg.org/css-text-4/#fullwidth-collapsing) and the inter-script spacing of <c>text-autospace</c>
    /// (https://drafts.csswg.org/css-text-4/#inter-script-spacing). The spacing at a boundary with the next item is added to the
    /// last character of the item. The spacing at the edges of the lines is applied when the line is built.
    /// </summary>
    private ShapedText ApplyItemSpacing(int index, ComputedStyle style, ShapedText shaped)
    {
        ref readonly var item = ref Items[index];
        var text = style.Layout.Text;
        var autospace = text.TextAutospace & (TextAutospace.IdeographAlpha | TextAutospace.IdeographNumeric);
        if (text.TextSpacingTrim == TextSpacingTrim.SpaceAll && autospace == TextAutospace.None)
            return shaped;

        // Most texts have no CJK character: the classes of the spacing are all below U+00B7 (or U+2E80 for the ideographs)
        var characters = _text.AsSpan(item.Start, item.End - item.Start);
        // (or the marks of its last character in the next item, see GetItemSpacing)
        if (!MayNeedSpacing(characters) && !(autospace != TextAutospace.None && GetAdjacentCharacter(item.End - 1, index, forward: true).CodePoint is var next && (next >= 0x2E80 || (next >= 0x300 && Unicode.UnicodeProperties.IsMark(next)))))
            return shaped;

        var length = characters.Length;
        var before = ArrayPool<double>.Shared.Rent(length);
        var after = ArrayPool<double>.Shared.Rent(length);
        try
        {
            var changed = false;
            for (var position = item.Start; position < item.End; position++)
            {
                var (b, trimAfter, autospaceAfter) = GetItemSpacing(position, index, style, shaped);
                before[position - item.Start] = b;
                after[position - item.Start] = trimAfter + autospaceAfter;
                changed |= b != 0 || trimAfter != 0 || autospaceAfter != 0;
            }

            return changed ? shaped.WithSpacing(0, length, before.AsSpan(0, length), after.AsSpan(0, length)) : shaped;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(before);
            ArrayPool<double>.Shared.Return(after);
        }

        static bool MayNeedSpacing(ReadOnlySpan<char> characters)
        {
            foreach (var c in characters)
            {
                if (c is (char)0xB7 or >= (char)0x2018)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the spacing of the character at a position of a text item, from its neighbors on the line: the trimmed blank half
    /// of a fullwidth punctuation (before an opening punctuation, after a closing one, https://drafts.csswg.org/css-text-4/#fullwidth-collapsing),
    /// and the inter-script spacing after the character (https://drafts.csswg.org/css-text-4/#inter-script-spacing).
    /// </summary>
    private (double Before, double TrimAfter, double AutospaceAfter) GetItemSpacing(int position, int itemIndex, ComputedStyle style, ShapedText shaped)
    {
        var characters = _text.AsSpan(0, _length);
        if (char.IsLowSurrogate(characters[position]) && position > 0 && char.IsHighSurrogate(characters[position - 1]))
            return default;

        ref readonly var item = ref Items[itemIndex];
        var codePoint = GetCodePointAt(characters, position);
        var layout = style.Layout;
        var text = layout.Text;
        var trim = text.TextSpacingTrim;
        var before = 0d;
        var trimAfter = 0d;
        var autospaceAfter = 0d;
        var next = (CodePoint: -1, Item: -1);
        var nextFetched = false;

        // https://drafts.csswg.org/css-text-4/#text-spacing-trim-property
        if (trim != TextSpacingTrim.SpaceAll && codePoint >= 0xB7)
        {
            var punctuation = TextSpacingClasses.GetFullwidthPunctuation(codePoint, GetPunctuationConvention(item, codePoint));
            if (punctuation is FullwidthPunctuation.Opening or FullwidthPunctuation.Closing or FullwidthPunctuation.MiddleDot)
            {
                var half = GetHalfAdvance(shaped, position - item.Start);
                if (half > 0)
                {
                    switch (punctuation)
                    {
                        case FullwidthPunctuation.Opening when trim == TextSpacingTrim.TrimAll || IsOpeningTrimmedByPrevious(position, itemIndex, layout.FontSize):
                            before = -half;
                            break;

                        case FullwidthPunctuation.Closing:
                            next = GetAdjacentCharacter(position, itemIndex, forward: true);
                            nextFetched = true;
                            if (trim == TextSpacingTrim.TrimAll || IsClosingTrimmedByNext(next.CodePoint, next.Item, layout.FontSize))
                            {
                                trimAfter = -half;
                            }

                            break;

                        // trim-all: "Set fullwidth opening punctuation, fullwidth closing punctuation, and fullwidth middle dot punctuation with half-width glyphs"
                        case FullwidthPunctuation.MiddleDot when trim == TextSpacingTrim.TrimAll:
                            before = -half / 2;
                            trimAfter = -half / 2;
                            break;
                    }
                }
            }
        }

        // https://drafts.csswg.org/css-text-4/#text-autospace-property: 1/8ic between the ideographs and the non-ideographic
        // letters or numerals that are directly adjoining on a line (added after the first character of the boundary)
        var autospace = text.TextAutospace;
        if ((autospace & (TextAutospace.IdeographAlpha | TextAutospace.IdeographNumeric)) != TextAutospace.None)
        {
            if (!nextFetched)
            {
                next = GetAdjacentCharacter(position, itemIndex, forward: true);
            }

            // The combining marks and the variation selectors belong to the typographic character unit of their base: the spacing
            // is after the unit, between its base and the next character. The marks of the following items that are shaped with
            // this one are in the cluster of the base, whose glyphs are in this item: the spacing after them is added here
            var current = itemIndex;
            var nextPosition = next.Item == itemIndex ? -1 : Items[Math.Max(next.Item, 0)].Start;
            while (next.CodePoint >= 0 && next.Item != itemIndex && Unicode.UnicodeProperties.IsMark(next.CodePoint))
            {
                if (next.Item != current && !CanShapeTogether(current, next.Item))
                {
                    next = (-1, -1);
                    break;
                }

                current = next.Item;
                var following = GetAdjacentCharacter(nextPosition, current, forward: true);
                nextPosition = following.Item == current ? nextPosition + (next.CodePoint > char.MaxValue ? 2 : 1) : Items[Math.Max(following.Item, 0)].Start;
                next = following;
            }

            if (next.CodePoint >= 0 && !Unicode.UnicodeProperties.IsMark(next.CodePoint))
            {
                var vertical = layout.IsVerticalWritingMode;
                // (the base can be in another inline box than its marks, WPT css/css-text/text-autospace/text-autospace-elements-007.html)
                var first = codePoint;
                var firstPosition = position;
                var firstItem = itemIndex;
                while (Unicode.UnicodeProperties.IsMark(first))
                {
                    var (previous, previousItem) = GetAdjacentCharacter(firstPosition, firstItem, forward: false);
                    if (previous < 0)
                        break;

                    firstPosition = (previousItem == firstItem ? firstPosition : Items[previousItem].End) - (previous > char.MaxValue ? 2 : 1);
                    firstItem = previousItem;
                    first = previous;
                }

                var nextStyle = Items[next.Item].Style;
                if (IsAutospaceBoundary(first, style, next.CodePoint, nextStyle, autospace, vertical))
                {
                    autospaceAfter = GetIdeographicAdvance(style) / 8;
                }
                else if ((autospace & TextAutospace.Replace) != TextAutospace.None && codePoint == ' ' && position > 0)
                {
                    // "replace": a space (U+0020) between the classes is replaced by the spacing
                    var previous = GetAdjacentCharacter(position, itemIndex, forward: false);
                    if (previous.CodePoint >= 0 && IsAutospaceBoundary(previous.CodePoint, Items[previous.Item].Style, next.CodePoint, nextStyle, autospace, vertical))
                    {
                        autospaceAfter = (GetIdeographicAdvance(style) / 8) - shaped.GetWidth(position - item.Start, position - item.Start + 1);
                    }
                }
            }
        }

        return (before, trimAfter, autospaceAfter);
    }

    /// <summary>
    /// Whether there is an inter-script boundary of <c>text-autospace</c> between two characters; each one is upright or not in
    /// vertical text by the style of its own element.
    /// </summary>
    private static bool IsAutospaceBoundary(int first, ComputedStyle firstStyle, int second, ComputedStyle secondStyle, TextAutospace autospace, bool vertical)
    {
        if (first < 0x2E80 && second < 0x2E80)
            return false;

        var alpha = (autospace & TextAutospace.IdeographAlpha) != TextAutospace.None;
        var numeric = (autospace & TextAutospace.IdeographNumeric) != TextAutospace.None;
        if (TextSpacingClasses.IsIdeograph(first))
            return IsLetterOrNumeral(second, secondStyle);

        return TextSpacingClasses.IsIdeograph(second) && IsLetterOrNumeral(first, firstStyle);

        // "is upright in vertical text flow using the text-orientation property or the text-combine-upright property"
        bool IsLetterOrNumeral(int codePoint, ComputedStyle style)
        {
            var upright = vertical && style.Layout.IsVerticalTypographicMode && (IsCombinedUpright(style) || (style.Layout.Text.TextCombineUpright.Digits > 0 && codePoint is >= '0' and <= '9') || style.Layout.TextOrientation == TextOrientation.Upright
                || (style.Layout.TextOrientation == TextOrientation.Mixed && Unicode.UnicodeProperties.GetVerticalOrientation(codePoint) is Unicode.VerticalOrientation.U or Unicode.VerticalOrientation.Tu));
            return (alpha && TextSpacingClasses.IsNonIdeographicLetter(codePoint, upright)) || (numeric && TextSpacingClasses.IsNonIdeographicNumeral(codePoint, upright));
        }
    }

    /// <summary>
    /// Whether a fullwidth opening punctuation is set half-width because of the previous character: "a fullwidth opening
    /// punctuation, a fullwidth middle dot punctuation, an ideographic space (U+3000), a fullwidth closing punctuation of an
    /// equivalent or larger font-size, a character belonging to Unicode general category Ps".
    /// </summary>
    private bool IsOpeningTrimmedByPrevious(int position, int itemIndex, double fontSize)
    {
        var (previous, previousItem) = GetAdjacentCharacter(position, itemIndex, forward: false);
        if (previous < 0)
            return false;

        return TextSpacingClasses.GetFullwidthPunctuation(previous, GetPunctuationConvention(Items[previousItem], previous)) switch
        {
            FullwidthPunctuation.Opening or FullwidthPunctuation.MiddleDot or FullwidthPunctuation.IdeographicSpace => true,
            FullwidthPunctuation.Closing => Items[previousItem].Style.Layout.FontSize >= fontSize,
            _ => Unicode.UnicodeProperties.GetGeneralCategory(previous) == UnicodeCategory.OpenPunctuation,
        };
    }

    /// <summary>
    /// Whether a fullwidth closing punctuation is set half-width because of the next character: "a fullwidth closing punctuation,
    /// a fullwidth middle dot punctuation, an ideographic space (U+3000), a fullwidth opening punctuation of a larger font-size,
    /// a character belonging to Unicode general category Pe".
    /// </summary>
    private bool IsClosingTrimmedByNext(int next, int nextItem, double fontSize)
    {
        if (next < 0)
            return false;

        return TextSpacingClasses.GetFullwidthPunctuation(next, GetPunctuationConvention(Items[nextItem], next)) switch
        {
            FullwidthPunctuation.Closing or FullwidthPunctuation.MiddleDot or FullwidthPunctuation.IdeographicSpace => true,
            FullwidthPunctuation.Opening => Items[nextItem].Style.Layout.FontSize > fontSize,
            _ => Unicode.UnicodeProperties.GetGeneralCategory(next) == UnicodeCategory.ClosePunctuation,
        };
    }

    /// <summary>
    /// Returns the character adjacent to the character at a position on a line, in the same inline formatting context: the next
    /// (previous) character of the text, unless an atomic inline, a forced line break, a <c>wbr</c> or the margins, borders or
    /// paddings of an inline box separate them (https://drafts.csswg.org/css-text-4/#inter-script-spacing: "directly adjoining
    /// on a line, i.e. without any intervening non-zero margin, border, or padding"). Returns -1 when there is none.
    /// </summary>
    private (int CodePoint, int Item) GetAdjacentCharacter(int position, int itemIndex, bool forward)
    {
        var characters = _text.AsSpan(0, _length);
        var items = Items;
        ref readonly var item = ref items[itemIndex];

        // The text of the ruby annotations is not adjacent to the text of the line (and the reverse)
        var inAnnotation = item.HasFlag(InlineItemFlags.RubyAnnotation);
        if (forward)
        {
            var next = position + (char.IsHighSurrogate(characters[position]) && position + 1 < item.End ? 2 : 1);
            if (next < item.End)
                return (GetCodePointAt(characters, next), itemIndex);
        }
        else if (position > item.Start)
        {
            return (GetCodePointBefore(characters[item.Start..position]), itemIndex);
        }

        var step = forward ? 1 : -1;
        for (var i = itemIndex + step; i >= 0 && i < items.Length; i += step)
        {
            ref readonly var other = ref items[i];
            if (other.HasFlag(InlineItemFlags.RubyAnnotation) != inAnnotation)
            {
                if (inAnnotation)
                    return (-1, -1);

                continue;
            }

            switch (other.Kind)
            {
                case InlineItemKind.OpenTag or InlineItemKind.CloseTag when other.InlineSize == 0:
                case InlineItemKind.OutOfFlow:
                case InlineItemKind.RubyColumn:
                    continue;

                // The bidi control codes are ignored, the U+200B of a wbr is a character between them
                case InlineItemKind.Control when other.Box is not TextRun:
                    continue;

                case InlineItemKind.Text when other.HasText && !other.HasFlag(InlineItemFlags.Tab):
                    return forward ? (GetCodePointAt(characters, other.Start), i) : (GetCodePointBefore(characters[other.Start..other.End]), i);

                default:
                    return (-1, -1);
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Returns the classification of the fullwidth colon and dot punctuation for the content language of an item
    /// (https://drafts.csswg.org/css-text-4/#fullwidth-ambiguous): Simplified and Traditional Chinese, else the Japanese convention.
    /// </summary>
    private static PunctuationConvention GetPunctuationConvention(in InlineItem item, int codePoint)
    {
        if (codePoint is not (0xFF1A or 0xFF1B or 0x3001 or 0x3002 or 0xFF0C or 0xFF0E) || item.Style.Element is not { } element)
            return PunctuationConvention.Japanese;

        var language = HtmlElementStates.GetLanguage(element);
        if (string.IsNullOrEmpty(language) || !language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return PunctuationConvention.Japanese;

        return language.Contains("hant", StringComparison.OrdinalIgnoreCase) || language.Contains("-tw", StringComparison.OrdinalIgnoreCase)
            || language.Contains("-hk", StringComparison.OrdinalIgnoreCase) || language.Contains("-mo", StringComparison.OrdinalIgnoreCase)
            ? PunctuationConvention.TraditionalChinese
            : PunctuationConvention.SimplifiedChinese;
    }

    /// <summary>
    /// Returns half the advance of the glyph of the character at an index of a shaped text when it is a fullwidth glyph (its advance
    /// in the font is at least about 1em), else 0: "for such proportional glyphs, the given advance width is considered
    /// simultaneously full-width and half-width: the UA must not add or remove space to these glyphs"
    /// (https://drafts.csswg.org/css-text-4/#fullwidth-collapsing). As Chrome (HanKerning), only the glyphs of a font that has
    /// the <c>halt</c> feature (<c>vhal</c> for the upright glyphs of vertical text) are trimmed: the UA "may choose not to trim
    /// when [it] determined the trimming may cause glyph cut-offs, collisions, or excessive kerning through glyph bounding box,
    /// glyph metrics, or font features", and nothing says where the ink of the punctuation of a font without these features
    /// is. The trimmed half is half of the advance (the values of the feature are not read). Nothing is trimmed either when
    /// the text enables a feature that changes the widths of the glyphs
    /// (<c>halt</c>, <c>vhal</c>, <c>palt</c>, <c>vpal</c>, <c>valt</c>, <c>hwid</c>, <c>pwid</c>, <c>qwid</c>, <c>twid</c>).
    /// </summary>
    private static double GetHalfAdvance(ShapedText shaped, int index)
    {
        if (shaped.Options.Features is { } features && HasExclusiveWidthFeature(features))
            return 0;

        var glyphs = shaped.Glyphs;
        var glyphIndex = ShapedText.FindGlyph(glyphs, index);
        if (glyphIndex >= glyphs.Length || glyphs[glyphIndex].Cluster != index)
            return 0;

        foreach (var run in shaped.Runs)
        {
            if (glyphIndex >= run.GlyphStart && glyphIndex < run.GlyphStart + run.GlyphCount)
            {
                // The font says that its fullwidth punctuation has a blank half with the halt feature (vhal for the upright
                // glyphs of vertical text)
                const uint HaltFeature = 0x68616C74;
                const uint VhalFeature = 0x7668616C;
                if (run.Font.Font.LayoutGpos is not { } gpos || gpos.FindFeature(run.IsUpright ? VhalFeature : HaltFeature) < 0)
                    return 0;

                var advance = run.Font.GetAdvance(glyphs[glyphIndex].GlyphId, run.EmSize, run.IsUpright);
                return advance >= run.EmSize * 0.9 ? advance / 2 : 0;
            }
        }

        return 0;
    }

    private static bool HasExclusiveWidthFeature(FontFeatureList features)
    {
        foreach (var feature in features.Items)
        {
            // halt, vhal, palt, vpal, valt, hwid, pwid, qwid, twid
            if (feature.Value != 0 && feature.Tag is 0x68616C74 or 0x7668616C or 0x70616C74 or 0x7670616C or 0x76616C74 or 0x68776964 or 0x70776964 or 0x71776964 or 0x74776964)
                return true;
        }

        return false;
    }

    /// <summary>The CJK advance measure of a style (the <c>ic</c> unit: the advance of U+6C34 水, or 1em).</summary>
    private double GetIdeographicAdvance(ComputedStyle style)
    {
        if (ReferenceEquals(style, _lastIdeographicStyle))
            return _lastIdeographicAdvance;

        var layout = style.Layout;
        var vertical = layout.IsVerticalWritingMode;
        var advance = layout.FontSize;
        if (style.Fonts.FindFontForCharacter(0x6C34) is { } font)
        {
            var glyph = font.GetGlyph(0x6C34);
            if (glyph != 0)
            {
                advance = font.GetAdvance(glyph, font.GetEmSize(layout.FontSize), vertical);
            }
        }

        _lastIdeographicStyle = style;
        _lastIdeographicAdvance = advance;
        return advance;
    }

    /// <summary>
    /// Returns the advance of a preserved tab at an inline position of the line (https://drafts.csswg.org/css-text-4/#white-space-phase-2):
    /// "a horizontal shift that lines up the start edge of the next glyph with the next tab stop. If this distance is less than
    /// 0.5ch, then the subsequent tab stop is used instead. Tab stops occur at points that are multiples of the tab size from the
    /// starting content edge of the preserved tab's nearest block container ancestor".
    /// </summary>
    private double GetTabAdvance(in InlineItem item, double inlinePosition)
    {
        var tabSize = item.Style.Layout.Text.TabSize;

        // "A <number> represents the measure as a multiple of the advance width of the space character (U+0020) of the nearest
        // block container ancestor of the preserved tab, including its associated letter-spacing and word-spacing"
        var size = tabSize.IsLength ? tabSize.Value : tabSize.Value * GetBlockSpaceAdvance();
        if (size <= 0 || !double.IsFinite(size))
            return 0;

        var position = _lineStartOffset + inlinePosition;
        var stop = Math.Floor((position / size) + 1e-9) * size;
        while (stop - position < GetHalfCh(item.Style) - 1e-9)
        {
            stop += size;
        }

        return stop - position;
    }

    private double GetBlockSpaceAdvance()
    {
        if (_blockSpaceAdvance is { } advance)
            return advance;

        var style = _block.Style;
        advance = TextShaper.MeasureWidth(" ", style.Fonts, GetShapingOptions(style, isRightToLeft: false));
        _blockSpaceAdvance = advance;
        return advance;
    }

    /// <summary>Half the <c>ch</c> unit of a style: half the advance of "0".</summary>
    private static double GetHalfCh(ComputedStyle style) => TextShaper.MeasureWidth("0", style.Fonts, GetShapingOptions(style, isRightToLeft: false) with { LetterSpacing = 0, WordSpacing = 0 }) / 2;

    /// <summary>
    /// Returns the adjustments of the spacing of the first character of a line: the fullwidth opening punctuation trimmed at the
    /// start of a line (https://drafts.csswg.org/css-text-4/#text-spacing-trim-property), and the opening bracket or quote that
    /// hangs at the start of the first formatted line (<c>hanging-punctuation: first</c>).
    /// </summary>
    private LineEdgeSpacing GetLineStartSpacing(int position, int itemIndex, bool isFirstLine)
    {
        ref readonly var item = ref Items[itemIndex];
        if (item.Kind != InlineItemKind.Text || item.HasFlag(InlineItemFlags.Tab) || position >= item.End)
            return default;

        var codePoint = GetCodePointAt(_text.AsSpan(0, _length), position);
        if (codePoint < 0x22 || (codePoint < 0xB7 && item.Style.Layout.Text.HangingPunctuation == HangingPunctuation.None))
            return default;

        var style = item.Style;
        var text = style.Layout.Text;
        var shaped = item.Shaped!;
        var spacing = 0d;
        if (TextSpacingClasses.GetFullwidthPunctuation(codePoint, PunctuationConvention.Japanese) == FullwidthPunctuation.Opening)
        {
            var trimmed = text.TextSpacingTrim switch
            {
                TextSpacingTrim.TrimStart or TextSpacingTrim.TrimBoth or TextSpacingTrim.TrimAll => true,

                // "Set fullwidth opening punctuation with full-width glyphs (spaced) on the first line of the block container and
                // each line after a forced line break. Otherwise as normal" (trimmed)
                TextSpacingTrim.SpaceFirst => !isFirstLine && !_lineAfterForcedBreak,
                _ => false,
            };

            // The spacing applied with the item (the opening punctuation after another punctuation)
            var itemSpacing = GetItemSpacing(position, itemIndex, style, shaped).Before;
            var desired = trimmed ? -GetHalfAdvance(shaped, position - item.Start) : 0;
            spacing = desired - itemSpacing;
        }

        // "An opening bracket, quote, or ideographic space at the start of the first formatted line of an element hangs": not
        // after the margin, border or padding of an inline box, which is then at the start of the line
        var hang = 0d;
        if (isFirstLine && (text.HangingPunctuation & HangingPunctuation.First) != HangingPunctuation.None && (TextSpacingClasses.IsOpeningBracketOrQuote(codePoint) || codePoint == 0x3000)
            && !IsAfterInlineBoxEdges(itemIndex))
        {
            var start = position - item.Start;
            hang = shaped.GetWidth(start, GetClusterEnd(shaped, start)) + spacing;
        }

        return new LineEdgeSpacing(position, spacing, 0, hang, 0);
    }

    /// <summary>Whether the inline-start margin, border or padding of an inline box is directly before an item (only the starts and the ends of inline boxes are between them).</summary>
    private bool IsAfterInlineBoxEdges(int itemIndex)
    {
        var items = Items;
        for (var i = itemIndex - 1; i >= 0 && items[i].Kind is InlineItemKind.OpenTag or InlineItemKind.CloseTag or InlineItemKind.Control or InlineItemKind.OutOfFlow; i--)
        {
            if (items[i].Kind == InlineItemKind.OpenTag && items[i].InlineSize != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the adjustments of the spacing of the last character of the content of a line (before the spaces removed or hanging
    /// at its end): the <c>letter-spacing</c> that is not applied at the end of a line and the inter-script spacing after it
    /// (https://drafts.csswg.org/css-text-4/#letter-spacing-property), the fullwidth closing punctuation trimmed at the end of
    /// a line (conditionally for <c>text-spacing-trim: normal</c>), and the punctuation that hangs (<c>hanging-punctuation</c>).
    /// </summary>
    private LineEdgeSpacing GetLineEndSpacing(int position, int itemIndex, int lineEnd, bool isHyphenated, bool isLastLine)
    {
        if (position < 0 || itemIndex < 0)
            return default;

        ref readonly var item = ref Items[itemIndex];
        if (item.Kind != InlineItemKind.Text || item.HasFlag(InlineItemFlags.Tab) || position < item.Start || position >= item.End)
            return default;

        // Most lines end with a character that has no spacing to adjust (the inter-script spacing after a letter or a numeral
        // is the one before an ideograph)
        var shaped = item.Shaped!;
        var beforeIdeograph = position + 1 < _length && _text[position + 1] >= 0x2E80;
        if (shaped.Options.LetterSpacing == 0 && _text[position] < 0xB7 && !beforeIdeograph && item.Style.Layout.Text.HangingPunctuation == HangingPunctuation.None)
            return default;

        var local = shaped.GetClusterStart(position - item.Start);
        var clusterEnd = GetClusterEnd(shaped, local);
        var glyphs = shaped.Glyphs;
        var lastGlyph = ShapedText.FindGlyph(glyphs, clusterEnd) - 1;
        var spacing = 0d;
        var spacingBefore = 0d;

        // https://drafts.csswg.org/css-text-4/#letter-spacing-property: "it is not applied at the beginning or end of a line" (the
        // hyphen of a hyphenated line is after the character)
        if (!isHyphenated && lastGlyph >= 0 && (glyphs[lastGlyph].Flags & ShapedGlyphFlags.LetterSpaced) != ShapedGlyphFlags.None)
        {
            spacing -= shaped.Options.LetterSpacing;
            if (shaped.IsRightToLeft)
            {
                spacingBefore = -shaped.Options.LetterSpacing;
            }
        }

        position = item.Start + local;
        var characters = _text.AsSpan(0, _length);
        var codePoint = GetCodePointAt(characters, position);
        var style = item.Style;
        var text = style.Layout.Text;
        var conditional = 0d;
        if (codePoint >= 0xB7 || beforeIdeograph)
        {
            // The inter-script spacing is "between adjacent characters on the same line"
            var (_, trimAfter, autospaceAfter) = GetItemSpacing(position, itemIndex, style, shaped);
            spacing -= autospaceAfter;
            if (codePoint >= 0xB7 && TextSpacingClasses.GetFullwidthPunctuation(codePoint, GetPunctuationConvention(item, codePoint)) == FullwidthPunctuation.Closing && !isHyphenated
                && !IsFollowedOnLine(item.Start + clusterEnd, lineEnd))
            {
                var half = -GetHalfAdvance(shaped, local);
                switch (text.TextSpacingTrim)
                {
                    case TextSpacingTrim.TrimBoth:
                        spacing += half - trimAfter;
                        break;

                    // "set fullwidth closing punctuation with half-width glyphs (flush) at the end of each line if it does not
                    // otherwise fit prior to justification, else set the punctuation with full-width glyphs"
                    case TextSpacingTrim.Normal or TextSpacingTrim.TrimStart or TextSpacingTrim.SpaceFirst or TextSpacingTrim.Auto:
                        spacing -= trimAfter;
                        conditional = half;
                        break;
                }
            }
        }

        // https://drafts.csswg.org/css-text-4/#hanging-punctuation-property
        var hang = 0d;
        var conditionalHang = 0d;
        var hanging = text.HangingPunctuation;
        if (hanging != HangingPunctuation.None && !isHyphenated)
        {
            var width = shaped.GetWidth(local, clusterEnd) + spacing;
            if ((hanging & (HangingPunctuation.ForceEnd | HangingPunctuation.AllowEnd)) != HangingPunctuation.None && TextSpacingClasses.IsStopOrComma(codePoint))
            {
                if ((hanging & HangingPunctuation.ForceEnd) != HangingPunctuation.None)
                {
                    hang = width;
                }
                else
                {
                    conditionalHang = width + conditional;
                }
            }
            else if (isLastLine && (hanging & HangingPunctuation.Last) != HangingPunctuation.None && TextSpacingClasses.IsClosingBracketOrQuote(codePoint))
            {
                hang = width;
            }
        }

        return new LineEdgeSpacing(position, spacing, conditional, hang, conditionalHang) { SpacingBefore = spacingBefore };
    }

    /// <summary>
    /// Whether white space that is not removed at the end of the line follows the last character of the content of a line (from
    /// <paramref name="start"/> to <paramref name="end"/>): e.g. an ideographic space (U+3000) that hangs, which trims the
    /// fullwidth closing punctuation before it.
    /// </summary>
    private bool IsFollowedOnLine(int start, int end)
    {
        for (var i = start; i < end && i < _length; i++)
        {
            // The bidi control codes and the other invisible characters are not on the line
            if (_text[i] != ' ' && !Unicode.UnicodeProperties.IsDefaultIgnorable(_text[i]))
                return true;
        }

        return false;
    }

    /// <summary>The end of the cluster that starts at an index of a shaped text.</summary>
    private static int GetClusterEnd(ShapedText shaped, int clusterStart)
    {
        var glyphs = shaped.Glyphs;
        var glyph = ShapedText.FindGlyph(glyphs, clusterStart);
        while (glyph < glyphs.Length && glyphs[glyph].Cluster == clusterStart)
        {
            glyph++;
        }

        return glyph < glyphs.Length ? glyphs[glyph].Cluster : shaped.Length;
    }

    /// <summary>Adds spacing before (after) the character at a position of the text, for the line being built.</summary>
    private void AddLineSpacing(int position, double before, double after)
    {
        if (before == 0 && after == 0)
            return;

        if (_spacingBefore.Length < _length)
        {
            var size = Math.Max(_length, _spacingBefore.Length * 2);
            Array.Resize(ref _spacingBefore, size);
            Array.Resize(ref _spacingAfter, size);
        }

        _spacingBefore[position] += before;
        _spacingAfter[position] += after;
        _spacingStart = Math.Min(_spacingStart, position);
        _spacingEnd = Math.Max(_spacingEnd, position + 1);

        // The width of the leaf that contains the character
        foreach (ref var leaf in CollectionsMarshal.AsSpan(_leaves))
        {
            if (leaf.Kind == LineLeafKind.Text && position >= leaf.Start && position < leaf.End)
            {
                leaf.Width += before + after;
                break;
            }
        }
    }

    /// <summary>Clears the spacing of the line.</summary>
    private void ClearLineSpacing()
    {
        if (_spacingEnd > _spacingStart)
        {
            _spacingBefore.AsSpan(_spacingStart, _spacingEnd - _spacingStart).Clear();
            _spacingAfter.AsSpan(_spacingStart, _spacingEnd - _spacingStart).Clear();
        }

        _spacingStart = int.MaxValue;
        _spacingEnd = 0;
    }

    /// <summary>Whether a character of the line has an adjusted spacing.</summary>
    private bool HasLineSpacing(int start, int end)
    {
        start = Math.Max(start, _spacingStart);
        end = Math.Min(end, _spacingEnd);
        for (var i = start; i < end; i++)
        {
            if (_spacingBefore[i] != 0 || _spacingAfter[i] != 0)
                return true;
        }

        return false;
    }

    /// <summary>Applies the spacing of the line to the glyphs of its text leaves (a shaped text per leaf whose spacing changed).</summary>
    private void ApplyLineSpacing()
    {
        if (_spacingEnd <= _spacingStart)
            return;

        foreach (ref var leaf in CollectionsMarshal.AsSpan(_leaves))
        {
            if (leaf.Kind != LineLeafKind.Text || leaf.Shaped is null || !HasLineSpacing(leaf.Start, leaf.End))
                continue;

            var start = leaf.Start - Items[leaf.Item].Start;
            var length = leaf.End - leaf.Start;
            var shaped = leaf.Shaped.WithSpacing(start, start + length, _spacingBefore.AsSpan(leaf.Start, length), _spacingAfter.AsSpan(leaf.Start, length));
            leaf.Shaped = shaped;
            leaf.GlyphStart = 0;
            leaf.GlyphCount = shaped.Glyphs.Length;
            leaf.Width = shaped.Width;
        }

        ClearLineSpacing();
    }

    /// <summary>
    /// Applies the spacing at the edges of the line and the tab stops to the leaves (https://drafts.csswg.org/css-text-4/#spacing).
    /// Returns the advances of the punctuation that hangs at the start and at the end of the line (the content is measured without
    /// them), and updates <paramref name="contentWidth"/>.
    /// </summary>
    private (double StartHang, double EndHang) AdjustLineEdges(in LineEnd end, bool isFirstLine, double availableSize, ref double contentWidth, ref double hangWidth)
    {
        // The tab stops (their advance was computed by the line breaking)
        foreach (ref readonly var leaf in CollectionsMarshal.AsSpan(_leaves))
        {
            if (leaf.Kind == LineLeafKind.Text && leaf.End > leaf.Start && Items[leaf.Item].HasFlag(InlineItemFlags.Tab))
            {
                var delta = Items[leaf.Item].InlineSize - leaf.Width;
                AddLineSpacing(leaf.Start, 0, delta);
                if (leaf.HangStart == leaf.Start)
                {
                    hangWidth += delta;
                }
                else
                {
                    contentWidth += delta;
                }
            }
        }

        // The first and the last characters of the content (in logical order)
        var first = -1;
        var last = -1;
        var lastPosition = -1;
        var isHyphenated = false;
        var leaves = CollectionsMarshal.AsSpan(_leaves);
        for (var i = 0; i < leaves.Length; i++)
        {
            if (leaves[i].Kind == LineLeafKind.Atomic || leaves[i].RubyColumn > 0)
                break;

            if (leaves[i].Kind == LineLeafKind.Text && leaves[i].End > leaves[i].Start)
            {
                first = i;
                break;
            }
        }

        for (var i = leaves.Length - 1; i >= 0; i--)
        {
            if (leaves[i].Kind == LineLeafKind.Hyphen)
            {
                isHyphenated = true;
                continue;
            }

            if (leaves[i].Kind == LineLeafKind.Atomic || leaves[i].RubyColumn > 0)
            {
                // The letter spacing after the run of atomic inlines that ends the line is not applied
                if (leaves[i].Kind == LineLeafKind.Atomic && leaves[i].RubyColumn == 0 && Items[leaves[i].Item].HasFlag(InlineItemFlags.LetterSpaced))
                {
                    var atomicSpacing = Items[leaves[i].Item].Container.Style.Layout.Text.LetterSpacing;
                    leaves[i].Width -= atomicSpacing;
                    contentWidth -= atomicSpacing;
                }

                break;
            }

            if (leaves[i].Kind == LineLeafKind.Text && leaves[i].HangStart > leaves[i].Start)
            {
                // The last character of the line is the one before the formatting characters that end it
                var contentEnd = SkipTrailingFormatCharacters(leaves[i].Start, leaves[i].HangStart);
                if (contentEnd == leaves[i].Start)
                    continue;

                last = i;
                lastPosition = contentEnd - 1;
                break;
            }
        }

        var startHang = 0d;
        if (first >= 0)
        {
            var start = GetLineStartSpacing(leaves[first].Start, leaves[first].Item, isFirstLine);
            AddLineSpacing(start.Position, start.Spacing, 0);
            contentWidth += start.Unconditional;
            startHang = start.Hang;
        }

        var endHang = 0d;
        if (last >= 0)
        {
            var isLastLine = end.Item >= _items.Count;
            var lineEnd = GetLineEndSpacing(lastPosition, leaves[last].Item, leaves[last].End, isHyphenated, isLastLine);
            AddLineSpacing(lineEnd.Position, lineEnd.SpacingBefore, lineEnd.Spacing - lineEnd.SpacingBefore);
            contentWidth += lineEnd.Unconditional;
            endHang = lineEnd.Hang;

            // "if it does not otherwise fit prior to justification"
            if (contentWidth > availableSize + LayoutEpsilon && (lineEnd.ConditionalSpacing != 0 || lineEnd.ConditionalHang != 0))
            {
                AddLineSpacing(lineEnd.Position, 0, lineEnd.ConditionalSpacing);
                contentWidth += lineEnd.Conditional;
                endHang += lineEnd.ConditionalHang;
            }
        }

        return (startHang, endHang);
    }

    /// <summary>
    /// Returns the end of a range of the text without the invisible formatting characters that end it
    /// (https://drafts.csswg.org/css-text-4/#letter-spacing-property: "letter-spacing ignores invisible zero-width formatting
    /// characters (such as those from the Unicode Cf category): spacing is inserted as if those characters did not exist in
    /// the document"): the spacing of the character before them is the one of the end of the line.
    /// </summary>
    private int SkipTrailingFormatCharacters(int start, int end)
    {
        while (end > start && _text[end - 1] >= 0xAD && char.GetUnicodeCategory(_text[end - 1]) == System.Globalization.UnicodeCategory.Format)
        {
            end--;
        }

        return end;
    }

    /// <summary>The tolerance of the comparisons of the inline sizes of the lines (the sums of floating-point advances).</summary>
    private const double LayoutEpsilon = 1e-6;

    /// <summary>
    /// The tolerance of the comparisons of the content of a line with the available space (see <see cref="BreakLine"/>): the
    /// layout unit of the browsers, 1/64px.
    /// </summary>
    private const double FitTolerance = 1d / 64;

    /// <summary>
    /// Distributes space to the justification opportunities of the line (https://drafts.csswg.org/css-text-4/#justification):
    /// with <c>inter-word</c>, after the word separators; with <c>inter-character</c>, between the typographic character units;
    /// with <c>auto</c>, after the word separators and between the characters of the CJK scripts (which have no word separator).
    /// The opportunities at the end of the line and in the content that hangs are not expanded. Returns whether there is an
    /// opportunity.
    /// </summary>
    private bool Justify(double space)
    {
        var count = CountJustificationOpportunities(apply: 0);
        if (count == 0)
            return false;

        CountJustificationOpportunities(apply: space / count);
        return true;
    }

    /// <summary>
    /// Whether a character is a letter of a cursive script (its Joining_Type is not Non_Joining): the justification does not
    /// add space after the letters of the cursive scripts (https://drafts.csswg.org/css-text-4/#cursive-tracking: the spaces
    /// between their words are expanded), as in the browsers.
    /// </summary>
    private static bool IsCursiveLetter(int codePoint) => codePoint >= 0x0600 && Unicode.UnicodeProperties.GetJoiningType(codePoint) != Unicode.JoiningType.U;

    /// <summary>Counts the justification opportunities of the line, and expands them by <paramref name="apply"/> when it is not 0.</summary>
    private int CountJustificationOpportunities(double apply)
    {
        // The last character of the content of the line has no opportunity after it
        var lastLeaf = -1;
        for (var i = _leaves.Count - 1; i >= 0; i--)
        {
            var leaf = _leaves[i];
            if (leaf.Kind is LineLeafKind.Atomic or LineLeafKind.Hyphen || leaf.RubyColumn > 0 || (leaf.Kind == LineLeafKind.Text && leaf.HangStart > leaf.Start))
            {
                lastLeaf = i;
                break;
            }
        }

        // https://drafts.csswg.org/css-text-4/#text-align-property: "If the UA chooses to adjust the text, then it must ensure
        // that tab stops continue to line up as required by the white space processing rules": the content before the last
        // preserved tab of the line is not expanded (as Firefox)
        var firstLeaf = 0;
        for (var i = _leaves.Count - 1; i >= 0; i--)
        {
            if (_leaves[i].Kind == LineLeafKind.Text && Items[_leaves[i].Item].HasFlag(InlineItemFlags.Tab))
            {
                firstLeaf = i + 1;
                break;
            }
        }

        var characters = _text.AsSpan(0, _length);
        var count = 0;
        for (var i = firstLeaf; i <= lastLeaf; i++)
        {
            var leaf = _leaves[i];

            // https://drafts.csswg.org/css-text-4/#justification: with inter-character, a run of consecutive atomic inlines is a
            // typographic character unit: there is an opportunity after it (WPT css/css-text/text-justify/text-justify-inter-character-atomic-inline.html)
            if (leaf.Kind == LineLeafKind.Atomic && leaf.RubyColumn == 0 && i < lastLeaf && Items[leaf.Item].Container.Style.Layout.Text.TextJustify == TextJustify.InterCharacter
                && !IsFollowedByAtomic(i, lastLeaf))
            {
                count++;
                if (apply != 0)
                {
                    CollectionsMarshal.AsSpan(_leaves)[i].Width += apply;
                }

                continue;
            }

            if (leaf.Kind != LineLeafKind.Text || leaf.Shaped is null || Items[leaf.Item].HasFlag(InlineItemFlags.Tab) || leaf.RubyColumn > 0)
                continue;

            // text-justify applies to the text of each inline box (none: "there are no justification opportunities within the text")
            var item = Items[leaf.Item];
            var textJustify = item.Style.Layout.Text.TextJustify;
            if (textJustify == TextJustify.None)
                continue;

            var shaped = leaf.Shaped;
            var glyphs = shaped.Glyphs;
            var end = i == lastLeaf ? leaf.HangStart : leaf.End;
            var g = ShapedText.FindGlyph(glyphs, leaf.Start - item.Start);
            while (g < glyphs.Length && glyphs[g].Cluster + item.Start < end)
            {
                var cluster = glyphs[g].Cluster;
                var isSeparator = (glyphs[g].Flags & ShapedGlyphFlags.WordSeparator) != ShapedGlyphFlags.None;
                var invisible = glyphs[g].IsInvisible;
                g++;
                while (g < glyphs.Length && glyphs[g].Cluster == cluster)
                {
                    g++;
                }

                var clusterEnd = (g < glyphs.Length ? glyphs[g].Cluster : shaped.Length) + item.Start;
                if (i == lastLeaf && clusterEnd >= end)
                    break;

                var position = item.Start + cluster;
                var expands = textJustify switch
                {
                    TextJustify.InterWord => isSeparator,
                    TextJustify.InterCharacter => !invisible && !IsCursiveLetter(GetCodePointAt(characters, position)),
                    _ => isSeparator || TextSpacingClasses.IsCjkJustificationCharacter(GetCodePointAt(characters, position)),
                };

                if (expands)
                {
                    count++;
                    if (apply != 0)
                    {
                        AddLineSpacing(position, 0, apply);
                    }
                }
            }
        }

        return count;

        // Whether the next content of the line after the leaf is an atomic inline (of the same run)
        bool IsFollowedByAtomic(int index, int last)
        {
            for (var i = index + 1; i <= last; i++)
            {
                var next = _leaves[i];
                if (next.Kind is LineLeafKind.OutOfFlow or LineLeafKind.EmptyBox || (next.Kind == LineLeafKind.Text && next.End == next.Start))
                    continue;

                return next.Kind == LineLeafKind.Atomic && next.RubyColumn == 0;
            }

            return false;
        }
    }
}
