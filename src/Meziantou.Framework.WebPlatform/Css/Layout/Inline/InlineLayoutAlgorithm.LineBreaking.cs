using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The line breaking (https://drafts.csswg.org/css-text-4/#line-breaking): a greedy algorithm that puts on each line the
// content up to the last soft wrap opportunity that fits, measuring the text with the advances of the shaped items.
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>The intrinsic size computed by a <see cref="BreakLine"/>, or none for the layout of the lines.</summary>
    private enum IntrinsicSizeMode : byte
    {
        None,

        /// <summary>The min-content size: every soft wrap opportunity is taken, the emergency ones only with <c>overflow-wrap: anywhere</c>.</summary>
        MinContent,

        /// <summary>The max-content size: only the forced line breaks end the lines.</summary>
        MaxContent,
    }

    /// <summary>The end of a line.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LineEnd
    {
        /// <summary>The first item of the next line.</summary>
        public int Item { get; init; }

        /// <summary>The position of the next line in the text (the start of <see cref="Item"/>, or a position inside a text item).</summary>
        public int Position { get; init; }

        /// <summary>Whether the line ends with a forced line break.</summary>
        public bool IsForced { get; init; }

        /// <summary>Whether the line is broken at a hyphenation opportunity (after a soft hyphen, or an automatic one): a hyphen is displayed at its end (https://drafts.csswg.org/css-text-4/#hyphenation).</summary>
        public bool IsHyphenated { get; init; }

        /// <summary>The position of the first character of the line after the collapsible spaces removed at its start.</summary>
        public int ContentStart { get; init; }

        /// <summary>The inline size of the content of the line, without the spaces removed or hanging at its end.</summary>
        public double Width { get; init; }

        /// <summary>
        /// Whether the line breaking stopped at a float whose position is not decided yet (<see cref="Item"/>): the float is placed
        /// on the line or below it, then the line is broken again. <see cref="Width"/> is the inline size of the content before it.
        /// </summary>
        public bool StopsAtFloat { get; init; }
    }

    /// <summary>A soft wrap opportunity where the line may be broken.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct BreakCandidate
    {
        public int Item;
        public int Position;
        public double Width;
        public bool IsHyphen;
        public bool Exists;
    }

    /// <summary>
    /// Adds a float to the line measured for the max-content size (the floats are side by side on the line of the content around
    /// them) and returns the change of the width of the line: a float with <c>clear</c> is placed below the floats of the line
    /// that it clears, which no longer are next to the content after it (the width of the line with them is kept in
    /// <see cref="_measuredWidthBeforeClearance"/>).
    /// </summary>
    private double AddMeasuredFloat(in InlineItem item, double width)
    {
        if (item.Box is not ContainerBox box)
            return item.InlineSize;

        var layout = box.Style.Layout;
        var clear = ExclusionSpace.GetClearSides(layout.Clear, _writingDirection);
        var cleared = 0d;
        if ((clear & ClearSides.LineLeft) != ClearSides.None)
        {
            cleared += _measuredLineLeftFloats;
            _measuredLineLeftFloats = 0;
        }

        if ((clear & ClearSides.LineRight) != ClearSides.None)
        {
            cleared += _measuredLineRightFloats;
            _measuredLineRightFloats = 0;
        }

        if (cleared > 0)
        {
            _measuredWidthBeforeClearance = Math.Max(_measuredWidthBeforeClearance, width);
        }

        if (ExclusionSpace.GetFloatSide(layout.Float, _writingDirection) == FloatSide.LineLeft)
        {
            _measuredLineLeftFloats += item.InlineSize;
        }
        else
        {
            _measuredLineRightFloats += item.InlineSize;
        }

        return item.InlineSize - cleared;
    }

    /// <summary>
    /// Finds the end of the line that starts at <paramref name="start"/>: the last soft wrap opportunity such that the content
    /// before it (without the spaces removed or hanging at the end of the line) fits in <paramref name="availableSize"/>, or
    /// the first opportunity when nothing fits (the content overflows), or a forced line break. The emergency opportunities of
    /// <c>overflow-wrap</c> are used when there is no other opportunity that fits
    /// (https://drafts.csswg.org/css-text-4/#overflow-wrap-property).
    /// </summary>
    /// <remarks>
    /// The items without character at a break (the starts of the inline boxes, the out-of-flow boxes) go to the next line,
    /// the ends of the inline boxes stay on the line. The collapsible spaces at the start of the line are skipped
    /// (https://drafts.csswg.org/css-text-4/#white-space-phase-2).
    /// </remarks>
    private LineEnd BreakLine(LinePosition start, double availableSize, bool isFirstLine, IntrinsicSizeMode mode)
    {
        // (the characters removed by the spelling change of the hyphenation break of the previous line are not on the line)
        start = SkipHyphenationRemovedCharacters(start);
        _measuredLineLeftFloats = 0;
        _measuredLineRightFloats = 0;
        _measuredWidthBeforeClearance = 0;

        // The content that fits exactly fits despite the rounding errors of the used sizes (e.g. a width of 4ch is rounded to
        // 6 decimals, a little less than the advances of 4 characters, and three inline-blocks of 33.333333% are a little wider
        // than their container): as in the browsers, whose sizes are multiples of their layout unit, 1/64px
        availableSize += FitTolerance;

        // https://drafts.csswg.org/css-overflow-4/#block-ellipsis: the block overflow ellipsis "makes room as necessary [...] by
        // displacing content from the end of the line as if wrapping, until the last soft wrap opportunity" that fits
        var displaces = _blockEllipsis is not null && mode == IntrinsicSizeMode.None;

        // The content that does not fit with the ellipsis is displaced entirely, but with -webkit-line-clamp (it is truncated)
        var displacesAll = displaces && !_blockEllipsisIsLegacy;

        // Most lines of the documents are short: the break opportunities are computed only when the rest of the content (up to
        // the next forced line break) does not fit
        if (!_opportunitiesComputed)
        {
            if (mode != IntrinsicSizeMode.MinContent && !_hasMandatoryBreakCharacters && TryFitLine(start, availableSize, isFirstLine, mode, out var fitted))
                return fitted;

            ComputeBreakOpportunities();
        }

        var items = Items;
        var text = _text.AsSpan(0, _length);
        var opportunities = _opportunities.AsSpan(0, _length);
        var width = 0d;
        var trailing = 0d;

        // The part of the trailing spaces that are preserved: they conditionally hang before a forced line break or at the end of
        // the content (https://drafts.csswg.org/css-text-4/#white-space-phase-2): they count for the max-content size
        var trailingPreserved = 0d;
        var hasContent = false;

        // Whether a character is on the line: a soft wrap opportunity after preserved spaces at the start of a line can be taken
        var hasCharacters = false;
        var contentStart = start.Position;
        var best = default(BreakCandidate);

        // The last opportunity that fits inside a phrase of word-break: auto-phrase (or a hyphenation opportunity of it), taken
        // when no other one fits; the last emergency opportunity of overflow-wrap
        var phrase = default(BreakCandidate);
        var emergency = default(BreakCandidate);
        var pendingItem = -1;
        var pendingWidth = 0d;
        var pendingTrailing = 0d;
        var lastTextItem = -1;

        // The last character of the content before the trailing spaces (its spacing is adjusted at the end of the line), and
        // whether the first character of the line was met (its spacing is adjusted at the start of the line)
        var lastContent = -1;
        var lastContentItem = -1;
        var startChecked = false;

        // https://drafts.csswg.org/css-break-4/#break-decoration: with box-decoration-break: clone, "Each box fragment is
        // independently wrapped with the border, padding, and margin": the inline boxes that are open at the start of the line
        // have their inline-start edges there, and the ones that are open where the line ends have their inline-end edges
        // there (clonedEnd, which the content of the line must leave room for)
        var clonedEnd = 0d;
        var pendingClonedEnd = 0d;
        if (_hasClonedEdges)
        {
            (width, clonedEnd) = GetClonedEdgesAtLineStart(start.Item);
        }

        for (var index = start.Item; index < items.Length; index++)
        {
            ref readonly var item = ref items[index];
            switch (item.Kind)
            {
                case InlineItemKind.OpenTag:
                    // An empty inline box (its content is collapsed away) after collapsible spaces stays on the line with them, as
                    // in the browsers (WPT css/css-text/white-space/trailing-space-position-001.html): it is not at the start of the
                    // content after the soft wrap opportunity
                    if (pendingItem < 0 && !(trailing > trailingPreserved && hasContent && IsEmptyInlineBoxWithoutEdges(index)))
                    {
                        pendingItem = index;
                        pendingWidth = width;
                        pendingTrailing = trailing;
                        pendingClonedEnd = clonedEnd;
                    }

                    width += item.InlineSize;
                    if (item.HasFlag(InlineItemFlags.ClonesEdges))
                    {
                        var (endMargin, endBorderAndPadding) = GetInlineEdgeParts(item.Container, start: false);
                        clonedEnd += endMargin + endBorderAndPadding;
                    }

                    continue;

                case InlineItemKind.OutOfFlow:
                    // An out-of-flow box at a break stays on the line (its static position is at the end of the line, as in the
                    // browsers), with the starts of the inline boxes before it when they have no inline size (the line can break
                    // after the box, as in Blink: the inline boxes then start on the line, which matters for the containing
                    // block that they form), else it goes to the next line with them. An out-of-flow box between collapsible
                    // spaces and the content after them is after the soft wrap opportunity that follows the spaces: it goes to
                    // the next line with that content ("Out-of-flow boxes ... do not introduce a forced line break or soft wrap
                    // opportunity", https://drafts.csswg.org/css-text-4/#line-break-details; WPT
                    // css/css-text/line-breaking/line-breaking-019.html); one followed by a collapsed space is inside the
                    // spaces, before their opportunity, and stays on the line (WPT css/CSS2/positioning/inline-static-position-001.html)
                    if (!item.IsFloat && pendingItem >= 0 && width == pendingWidth)
                    {
                        pendingItem = -1;
                    }
                    else if (!item.IsFloat && pendingItem < 0 && trailing > trailingPreserved && hasContent && _wrappingCollapsedSpaces.BinarySearch(item.Start) < 0)
                    {
                        pendingItem = index;
                        pendingWidth = width;
                        pendingTrailing = trailing;
                        pendingClonedEnd = clonedEnd;
                    }

                    if (item.IsFloat)
                    {
                        if (mode == IntrinsicSizeMode.MaxContent)
                        {
                            // The floats are on the line of the content around them
                            width += AddMeasuredFloat(item, width - trailing);
                        }
                        else if (mode == IntrinsicSizeMode.None && !item.HasFlag(InlineItemFlags.FloatPlaced))
                        {
                            // https://drafts.csswg.org/css2/#float-position: the float is placed on the current line when it fits next to
                            // the content before it (which must fit on the line), else below the line
                            if (width - trailing + clonedEnd > availableSize && hasContent && (best.Exists || phrase.Exists || emergency.Exists || displacesAll))
                                return ToFallbackEnd();

                            return new LineEnd { Item = index, Position = item.Start, StopsAtFloat = true, ContentStart = contentStart, Width = width - trailing + (hasCharacters ? clonedEnd : 0) };
                        }
                    }

                    continue;

                case InlineItemKind.CloseTag:
                    // The content of an inline box must fit before its negative inline-end margin is applied: as in the browsers,
                    // the margin does not bring back on the line the content that overflows it (WPT css/css-grid/firefox-bug-1881495.html)
                    if (item.InlineSize < 0 && hasContent && (best.Exists || phrase.Exists || emergency.Exists || displacesAll))
                    {
                        var closeEdge = GetLineEndSpacing(lastContent, lastContentItem, item.Start, isHyphenated: false, isLastLine: false);
                        if (width - trailing + closeEdge.Unconditional + closeEdge.Conditional > availableSize)
                            return ToFallbackEnd();
                    }

                    width += item.InlineSize;
                    if (item.HasFlag(InlineItemFlags.ClonesEdges))
                    {
                        // The tag has the inline-end edges of the box, which is no longer open
                        clonedEnd = Math.Max(0, clonedEnd - item.InlineSize);
                    }

                    continue;

                case InlineItemKind.ForcedBreak:
                    // The content before the forced break must fit: else the line breaks at the last opportunity that fits
                    var forcedEdge = GetLineEndSpacing(lastContent, lastContentItem, item.Start, isHyphenated: false, isLastLine: false);
                    if (width - trailing + clonedEnd + forcedEdge.Unconditional + forcedEdge.Conditional > availableSize && hasContent && (best.Exists || phrase.Exists || emergency.Exists || displacesAll))
                        return ToFallbackEnd();

                    var forcedConditional = mode == IntrinsicSizeMode.MaxContent ? trailingPreserved : 0;

                    // The ends of the inline boxes after the break (e.g. the box of a br element) stay on the line, as the
                    // control codes reopened after it (they have no advance)
                    var next = index + 1;
                    while (next < items.Length && items[next].Kind is InlineItemKind.CloseTag or InlineItemKind.Control && items[next].Box is not TextRun)
                    {
                        width += items[next].InlineSize;
                        if (items[next].HasFlag(InlineItemFlags.ClonesEdges) && items[next].Kind == InlineItemKind.CloseTag)
                        {
                            clonedEnd = Math.Max(0, clonedEnd - items[next].InlineSize);
                        }

                        next++;
                    }

                    return new LineEnd { Item = next, Position = item.End, IsForced = true, ContentStart = contentStart, Width = width - trailing + clonedEnd + forcedConditional + forcedEdge.Unconditional + GetRemovedSpaceShapingCorrection(lastContent, lastContentItem, isFirstLine) };

                case InlineItemKind.Control:
                    for (var i = Math.Max(item.Start, start.Position); i < item.End; i++)
                    {
                        if (CheckOpportunity(index, i, out var end))
                            return end;
                    }

                    // The bidi control codes at the start of a ruby container go to the next line with it (see the ruby columns)
                    if (item.Box is not ContainerBox { Display.Inside: DisplayInside.Ruby })
                    {
                        pendingItem = -1;
                    }

                    continue;

                case InlineItemKind.RubyColumn:
                    // https://drafts.csswg.org/css-ruby-1/#break-between: a ruby column is not broken. The opportunity before it is the
                    // one before its first base character; the line ends before the column (or before the ruby container it starts)
                    var columnIndex = item.RubyColumn - 1;
                    var breakPosition = GetRubyColumnBreakPosition(columnIndex);
                    if (breakPosition < _length && CheckOpportunity(index, breakPosition, out var columnEnd))
                        return columnEnd.Position == breakPosition ? columnEnd with { Position = items[columnEnd.Item].Start } : columnEnd;

                    // The floats in the column are placed when the line breaking meets the column (see the OutOfFlow items)
                    if (mode == IntrinsicSizeMode.None && FindUnplacedRubyFloat(columnIndex) is var floatIndex and >= 0)
                        return new LineEnd { Item = floatIndex, Position = items[floatIndex].Start, StopsAtFloat = true, ContentStart = contentStart, Width = width - trailing };

                    var columnWidth = ComputeRubyColumnLayout(columnIndex, isFirstLine, mode == IntrinsicSizeMode.MinContent, tabOrigin: width);
                    width += columnWidth;
                    var (overhangStart, overhangEnd) = GetRubyOverhang(columnIndex, isFirstLine, start.Item, int.MaxValue, endPosition: width);
                    width -= overhangStart + overhangEnd;

                    // The collapsible spaces at the end of the column are removed at the end of the line
                    trailing = GetRubyColumnTrailingWidth(columnIndex, isFirstLine, mode == IntrinsicSizeMode.MinContent, columnWidth);
                    trailingPreserved = 0;
                    hasContent = true;
                    hasCharacters = true;
                    pendingItem = -1;
                    lastContent = -1;
                    startChecked = true;
                    index = _rubyColumns[columnIndex].EndItem - 1;
                    continue;

                case InlineItemKind.Atomic:
                    // The content that overflows the line and cannot be broken at an earlier opportunity is not broken before
                    // an atomic inline whose negative margin brings it back in the available space (as in Blink and WebKit, WPT
                    // css/CSS2/linebox/inline-negative-margin-001.html); when it can be broken earlier, it is, as before the end of
                    // an inline box with a negative margin
                    var fitsWithNegativeMargin = item.InlineSize < 0 && !best.Exists && !phrase.Exists && !emergency.Exists && mode == IntrinsicSizeMode.None
                        && width - trailing > availableSize && width - trailing + item.InlineSize <= availableSize;
                    if (!fitsWithNegativeMargin && CheckOpportunity(index, item.Start, out var atomicEnd))
                        return atomicEnd;

                    width += mode == IntrinsicSizeMode.MinContent ? item.MinContentInlineSize : item.InlineSize;

                    // The letter spacing after a run of atomic inlines is not applied at the end of a line
                    trailing = item.HasFlag(InlineItemFlags.LetterSpaced) ? item.Container.Style.Layout.Text.LetterSpacing : 0;
                    trailingPreserved = 0;
                    hasContent = true;
                    hasCharacters = true;
                    pendingItem = -1;
                    lastContent = -1;
                    startChecked = true;
                    continue;
            }

            if (item.HasFlag(InlineItemFlags.Tab))
            {
                // A preserved tab: its advance depends on its position on the line
                var tabPosition = Math.Max(item.Start, start.Position);
                if (tabPosition < item.End)
                {
                    if (opportunities[tabPosition] != LineBreakOpportunity.None && CheckOpportunity(index, tabPosition, out var tabEnd))
                        return tabEnd;

                    var advance = GetTabAdvance(item, width);
                    items[index].InlineSize = advance;
                    width += advance;
                    if (item.HasFlag(InlineItemFlags.HangsSpaces))
                    {
                        trailing += advance;
                        trailingPreserved = trailing;
                    }
                    else
                    {
                        trailing = 0;
                        trailingPreserved = 0;
                        hasContent = true;
                        lastContent = -1;
                    }

                    startChecked = true;
                    hasCharacters = true;
                    pendingItem = -1;
                }

                lastTextItem = index;
                continue;
            }

            // A text item, measured by segments between the soft wrap opportunities
            var offsets = GetShaped(index, isFirstLine).Offsets;
            var collapses = item.HasFlag(InlineItemFlags.CollapsesSpaces);
            var trailingSpaces = collapses || item.HasFlag(InlineItemFlags.HangsSpaces);
            var position = Math.Max(item.Start, start.Position);
            while (position < item.End)
            {
                if (opportunities[position] != LineBreakOpportunity.None && CheckOpportunity(index, position, out var end))
                    return end;

                var segmentEnd = position + 1;
                while (segmentEnd < item.End && opportunities[segmentEnd] == LineBreakOpportunity.None)
                {
                    segmentEnd++;
                }

                // https://drafts.csswg.org/css-text-4/#white-space-phase-2: "A sequence of collapsible spaces at the beginning of a line is removed"
                // (not the ones after preserved spaces, which are on the line)
                var segmentStart = position;
                if (!hasCharacters && collapses)
                {
                    while (segmentStart < segmentEnd && text[segmentStart] == ' ')
                    {
                        segmentStart++;
                    }

                    if (segmentStart > position)
                    {
                        contentStart = segmentStart;
                    }
                }

                if (segmentStart < segmentEnd)
                {
                    // The spaces at the end of the segment are removed or hang if the line ends after them
                    var spacesStart = segmentEnd;
                    if (trailingSpaces)
                    {
                        while (spacesStart > segmentStart && IsSpace(text[spacesStart - 1]))
                        {
                            spacesStart--;
                        }
                    }

                    var segmentWidth = offsets[segmentEnd - item.Start] - offsets[segmentStart - item.Start];
                    width += segmentWidth;
                    hasCharacters = true;
                    if (!startChecked)
                    {
                        // The spacing of the first character of the line (text-spacing-trim, hanging-punctuation: first)
                        width += GetLineStartSpacing(segmentStart, index, isFirstLine).Unconditional;
                        startChecked = true;
                    }

                    var preserved = item.HasFlag(InlineItemFlags.HangsSpaces);
                    if (spacesStart > segmentStart)
                    {
                        trailing = offsets[segmentEnd - item.Start] - offsets[spacesStart - item.Start];
                        trailingPreserved = preserved ? trailing : 0;
                        hasContent = true;

                        // (the last character of the content is before the formatting characters that end it)
                        if (SkipTrailingFormatCharacters(segmentStart, spacesStart) is var contentEnd && contentEnd > segmentStart)
                        {
                            lastContent = contentEnd - 1;
                            lastContentItem = index;
                        }
                    }
                    else
                    {
                        // (the white space before preserved spaces that end the line does not hang when they fit: hanging is
                        // only at the end of a line; the preserved spaces followed by other white space hang unconditionally)
                        trailing += segmentWidth;
                        trailingPreserved = preserved ? trailing : 0;
                    }

                    pendingItem = -1;
                }

                position = segmentEnd;
            }

            lastTextItem = index;
        }

        // The end of the content: the last line, unless it does not fit
        var edge = GetLineEndSpacing(lastContent, lastContentItem, _length, isHyphenated: false, isLastLine: true);
        var contentWidth = width - trailing + edge.Unconditional + GetRemovedSpaceShapingCorrection(lastContent, lastContentItem, isFirstLine);
        if (contentWidth + edge.Conditional > availableSize && hasContent && (best.Exists || phrase.Exists || emergency.Exists || displacesAll))
            return ToFallbackEnd();

        return new LineEnd { Item = items.Length, Position = _length, ContentStart = contentStart, Width = contentWidth + (mode == IntrinsicSizeMode.MaxContent ? trailingPreserved : 0) };

        // Checks the soft wrap opportunity before the character at position: records it when the content before it fits, or
        // returns the end of the line when it does not fit
        bool CheckOpportunity(int index, int position, out LineEnd end)
        {
            end = default;
            if (position <= contentStart || !hasCharacters)
                return false;

            var opportunity = _opportunities[position];
            if (opportunity == LineBreakOpportunity.None)
                return false;

            var isHyphen = opportunity is LineBreakOpportunity.Hyphen or LineBreakOpportunity.AutoHyphen;
            if (isHyphen && (!_lineAllowsHyphens
                || (opportunity == LineBreakOpportunity.Hyphen ? !IsHyphenationAllowed(position, GetOpportunityStyle(index)) : mode != IntrinsicSizeMode.MinContent && !IsAutomaticHyphenationAllowed(position, start.Position))))
            {
                return false;
            }

            var breakItem = pendingItem >= 0 ? pendingItem : index;
            var breakWidth = pendingItem >= 0 ? pendingWidth - pendingTrailing + pendingClonedEnd : width - trailing + clonedEnd;

            // The spacing of the last character of the line (letter-spacing, text-spacing-trim, hanging-punctuation)
            var lineEdge = GetLineEndSpacing(lastContent, lastContentItem, position, isHyphen, isLastLine: false);
            breakWidth += lineEdge.Unconditional;
            if (opportunity == LineBreakOpportunity.Mandatory)
            {
                // A forced line break of UAX #14 that is not a segment break (e.g. U+2028)
                end = new LineEnd { Item = breakItem, Position = position, IsForced = true, ContentStart = contentStart, Width = breakWidth };
                return true;
            }

            // The hyphen has the style of the text before it: the current item when the opportunity is inside it
            var hyphenItem = Items[index].Kind == InlineItemKind.Text && position > Items[index].Start ? index : lastTextItem;
            if (isHyphen && hyphenItem >= 0)
            {
                breakWidth += MeasureHyphenationBreak(hyphenItem, position, isFirstLine);
            }

            var fitWidth = breakWidth + lineEdge.Conditional;
            var candidate = new BreakCandidate { Item = breakItem, Position = position, Width = breakWidth, IsHyphen = isHyphen, Exists = true };
            if (opportunity == LineBreakOpportunity.Phrase || (isHyphen && GetLineBreakOptions(GetOpportunityStyle(index)).WordBreak == WordBreakMode.AutoPhrase))
            {
                // https://drafts.csswg.org/css-text-4/#valdef-word-break-auto-phrase: the phrases are kept together and the
                // hyphenation opportunities suppressed, unless the line would overflow (as in Chrome, which breaks the line again
                // without them): they do not contribute to the min-content size (WPT css/css-text/word-break/auto-phrase)
                if (mode == IntrinsicSizeMode.MinContent)
                    return false;

                if (fitWidth <= availableSize)
                {
                    phrase = candidate;
                    return false;
                }
            }
            else if (opportunity == LineBreakOpportunity.Emergency)
            {
                // https://drafts.csswg.org/css-overflow-4/#block-ellipsis: "Soft wrap opportunities added by overflow-wrap are ignored"
                if (displaces || _ignoresEmergencyBreaks)
                    return false;

                // https://drafts.csswg.org/css-text-4/#overflow-wrap-property: only overflow-wrap: anywhere contributes to the min-content size
                if (mode == IntrinsicSizeMode.MinContent && GetLineBreakOptions(GetOpportunityStyle(index)).OverflowWrap != OverflowWrapMode.Anywhere
                    && GetBreakOptionsBefore(position).OverflowWrap != OverflowWrapMode.Anywhere)
                {
                    return false;
                }

                if (fitWidth <= availableSize)
                {
                    emergency = candidate;
                    return false;
                }
            }
            else if (fitWidth <= availableSize)
            {
                best = candidate;
                return false;
            }

            // The content does not fit: break at the last opportunity that fits, or here
            end = best.Exists || phrase.Exists || emergency.Exists || displacesAll ? ToFallbackEnd() : ToLineEnd(candidate);
            return true;
        }

        LineEnd ToLineEnd(BreakCandidate candidate) => new() { Item = candidate.Item, Position = candidate.Position, IsHyphenated = candidate.IsHyphen, ContentStart = contentStart, Width = candidate.Width };

        // The end of a line whose content does not fit: the last opportunity that fits, or the last one inside a phrase, or the
        // last emergency one; with the block
        // overflow ellipsis, "If this results in the entire contents of the line box being displaced, the line box is considered
        // to contain a strut": the line is empty
        LineEnd ToFallbackEnd()
        {
            if (best.Exists)
                return ToLineEnd(best);

            if (phrase.Exists)
                return ToLineEnd(phrase);

            if (emergency.Exists)
                return ToLineEnd(emergency);

            return new LineEnd { Item = start.Item, Position = start.Position, ContentStart = start.Position };
        }
    }

    /// <summary>
    /// Returns the end of the line when the content from <paramref name="start"/> to the next forced line break (or to the end)
    /// fits in <paramref name="availableSize"/>, measured as <see cref="BreakLine"/> does, without the break opportunities.
    /// </summary>
    private bool TryFitLine(LinePosition start, double availableSize, bool isFirstLine, IntrinsicSizeMode mode, out LineEnd end)
    {
        end = default;
        var items = Items;
        var text = _text.AsSpan(0, _length);
        var width = 0d;
        var trailing = 0d;
        var trailingPreserved = 0d;
        var hasCharacters = false;
        var contentStart = start.Position;
        var lastContent = -1;
        var lastContentItem = -1;
        var startChecked = false;

        // (the inline edges repeated by box-decoration-break: clone: see BreakLine)
        var clonedEnd = 0d;
        if (_hasClonedEdges)
        {
            (width, clonedEnd) = GetClonedEdgesAtLineStart(start.Item);
        }

        for (var index = start.Item; index < items.Length; index++)
        {
            ref readonly var item = ref items[index];
            switch (item.Kind)
            {
                case InlineItemKind.OutOfFlow:
                    // A float whose position is not decided stops the line breaking (see BreakLine)
                    if (item.IsFloat)
                    {
                        if (mode == IntrinsicSizeMode.MaxContent)
                        {
                            width += AddMeasuredFloat(item, width - trailing);
                        }
                        else if (!item.HasFlag(InlineItemFlags.FloatPlaced))
                        {
                            return false;
                        }
                    }

                    continue;

                case InlineItemKind.OpenTag or InlineItemKind.CloseTag:
                    // (the content of an inline box must fit before its negative inline-end margin is applied: see BreakLine)
                    if (item.InlineSize < 0 && item.Kind == InlineItemKind.CloseTag && width - trailing > availableSize)
                        return false;

                    width += item.InlineSize;
                    if (item.HasFlag(InlineItemFlags.ClonesEdges))
                    {
                        if (item.Kind == InlineItemKind.OpenTag)
                        {
                            var (endMargin, endBorderAndPadding) = GetInlineEdgeParts(item.Container, start: false);
                            clonedEnd += endMargin + endBorderAndPadding;
                        }
                        else
                        {
                            clonedEnd = Math.Max(0, clonedEnd - item.InlineSize);
                        }
                    }

                    continue;

                case InlineItemKind.Control:
                    continue;

                case InlineItemKind.RubyColumn:
                    // A ruby column is a unit (see BreakLine)
                    if (mode != IntrinsicSizeMode.MaxContent && FindUnplacedRubyFloat(item.RubyColumn - 1) >= 0)
                        return false;


                    width += ComputeRubyColumnLayout(item.RubyColumn - 1, isFirstLine, minContent: false, tabOrigin: width);
                    var (fitOverhangStart, fitOverhangEnd) = GetRubyOverhang(item.RubyColumn - 1, isFirstLine, start.Item, int.MaxValue, endPosition: width);
                    width -= fitOverhangStart + fitOverhangEnd;
                    trailing = 0;
                    trailingPreserved = 0;
                    lastContent = -1;
                    startChecked = true;
                    hasCharacters = true;
                    index = _rubyColumns[item.RubyColumn - 1].EndItem - 1;
                    continue;

                case InlineItemKind.Atomic:
                    // (the content before an atomic inline whose negative margin brings the line back in the available space
                    // must fit when the line can be broken before it: see BreakLine)
                    if (item.InlineSize < 0 && width - trailing > availableSize)
                        return false;

                    width += item.InlineSize;
                    trailing = item.HasFlag(InlineItemFlags.LetterSpaced) ? item.Container.Style.Layout.Text.LetterSpacing : 0;
                    trailingPreserved = 0;
                    hasCharacters = true;
                    lastContent = -1;
                    startChecked = true;
                    continue;

                case InlineItemKind.ForcedBreak:
                    var next = index + 1;
                    while (next < items.Length && items[next].Kind is InlineItemKind.CloseTag or InlineItemKind.Control && items[next].Box is not TextRun)
                    {
                        width += items[next].InlineSize;
                        if (items[next].HasFlag(InlineItemFlags.ClonesEdges) && items[next].Kind == InlineItemKind.CloseTag)
                        {
                            clonedEnd = Math.Max(0, clonedEnd - items[next].InlineSize);
                        }

                        next++;
                    }

                    var forcedEdge = GetLineEndSpacing(lastContent, lastContentItem, item.Start, isHyphenated: false, isLastLine: false);
                    var forcedConditional = mode == IntrinsicSizeMode.MaxContent ? trailingPreserved : 0;
                    var forcedCorrection = GetRemovedSpaceShapingCorrection(lastContent, lastContentItem, isFirstLine);
                    end = new LineEnd { Item = next, Position = item.End, IsForced = true, ContentStart = contentStart, Width = width - trailing + clonedEnd + forcedConditional + forcedEdge.Unconditional + forcedCorrection };
                    return width - trailing + clonedEnd + forcedEdge.Unconditional + forcedEdge.Conditional + forcedCorrection <= availableSize;
            }

            if (item.HasFlag(InlineItemFlags.Tab))
            {
                if (Math.Max(item.Start, start.Position) < item.End)
                {
                    var advance = GetTabAdvance(item, width);
                    items[index].InlineSize = advance;
                    width += advance;
                    hasCharacters = true;
                    if (item.HasFlag(InlineItemFlags.HangsSpaces))
                    {
                        trailing += advance;
                        trailingPreserved = trailing;
                    }
                    else
                    {
                        trailing = 0;
                        trailingPreserved = 0;
                        lastContent = -1;
                    }

                    startChecked = true;
                }

                continue;
            }

            var offsets = GetShaped(index, isFirstLine).Offsets;
            var from = Math.Max(item.Start, start.Position);
            if (!hasCharacters && item.HasFlag(InlineItemFlags.CollapsesSpaces))
            {
                var skipped = from;
                while (from < item.End && text[from] == ' ')
                {
                    from++;
                }

                if (from > skipped)
                {
                    contentStart = from;
                }
            }

            if (from < item.End)
            {
                hasCharacters = true;
                var spacesStart = item.End;
                if (item.HasFlag(InlineItemFlags.CollapsesSpaces) || item.HasFlag(InlineItemFlags.HangsSpaces))
                {
                    while (spacesStart > from && IsSpace(text[spacesStart - 1]))
                    {
                        spacesStart--;
                    }
                }

                var itemWidth = offsets[item.End - item.Start] - offsets[from - item.Start];
                width += itemWidth;
                if (!startChecked)
                {
                    width += GetLineStartSpacing(from, index, isFirstLine).Unconditional;
                    startChecked = true;
                }

                var preserved = item.HasFlag(InlineItemFlags.HangsSpaces);
                if (spacesStart > from)
                {
                    trailing = offsets[item.End - item.Start] - offsets[spacesStart - item.Start];
                    trailingPreserved = preserved ? trailing : 0;
                    if (SkipTrailingFormatCharacters(from, spacesStart) is var contentEnd && contentEnd > from)
                    {
                        lastContent = contentEnd - 1;
                        lastContentItem = index;
                    }
                }
                else
                {
                    trailing += itemWidth;
                    trailingPreserved = preserved ? trailing : 0;
                }
            }
        }

        var edge = GetLineEndSpacing(lastContent, lastContentItem, _length, isHyphenated: false, isLastLine: true);
        var correction = GetRemovedSpaceShapingCorrection(lastContent, lastContentItem, isFirstLine);
        end = new LineEnd { Item = items.Length, Position = _length, ContentStart = contentStart, Width = width - trailing + (mode == IntrinsicSizeMode.MaxContent ? trailingPreserved : 0) + edge.Unconditional + correction };
        return width - trailing + edge.Unconditional + edge.Conditional + correction <= availableSize;
    }

    /// <summary>
    /// Returns the difference between the width of the text of a line that ends with the character at
    /// <paramref name="lastContent"/>, followed in its text item by collapsible spaces that are removed at the end of the line
    /// (after the invisible formatting characters that end the content, e.g. a zero width joiner),
    /// and its width in the shaped item: where the text is unsafe to break (a kerning pair between the last letter and the
    /// space, e.g. "A " in Times), the line is built with the end of the text shaped again without the space
    /// (<see cref="ShapeLineEdges"/>). Without it, the max-content size of a text that ends with such a letter and a
    /// collapsible space would be narrower than the text of its line.
    /// </summary>
    private double GetRemovedSpaceShapingCorrection(int lastContent, int lastContentItem, bool isFirstLine)
    {
        if (lastContentItem < 0)
            return 0;

        ref readonly var item = ref Items[lastContentItem];
        if (item.Kind != InlineItemKind.Text || !item.HasFlag(InlineItemFlags.CollapsesSpaces) || item.HasFlag(InlineItemFlags.Tab))
            return 0;

        // The removed spaces follow the invisible formatting characters that end the content (see SkipTrailingFormatCharacters):
        // the text of the line ends with them
        var contentEnd = lastContent + 1;
        while (contentEnd < item.End && _text[contentEnd] >= 0xAD && char.GetUnicodeCategory(_text[contentEnd]) == System.Globalization.UnicodeCategory.Format)
        {
            contentEnd++;
        }

        var end = contentEnd - item.Start;
        var shaped = GetShaped(lastContentItem, isFirstLine);
        if (end <= 0 || end >= shaped.Length || shaped.IsCombinedUpright || !shaped.IsUnsafeToBreak(end))
            return 0;

        return ShapeLineEdges(lastContentItem, shaped, 0, end, isFirstLine).GetWidth(0, end) - shaped.GetWidth(0, end);
    }

    /// <summary>
    /// Whether a character is a white space that is removed or hangs at the end of a line (https://drafts.csswg.org/css-text-4/#white-space-phase-2):
    /// the spaces, the tabs and the other space separators (General_Category Zs except U+0020 and U+00A0). Only the collapsible
    /// spaces (U+0020) are removed at the start of a line.
    /// </summary>
    private static bool IsSpace(char c) => c is ' ' or '\t' or (char)0x1680 or (>= (char)0x2000 and <= (char)0x200A) or (char)0x202F or (char)0x205F or (char)0x3000;

    /// <summary>
    /// Whether the open tag at <paramref name="index"/> starts an inline box without character, inline box nor atomic inline (its
    /// text is collapsed away) and without margin, border and padding on its inline edges.
    /// </summary>
    private bool IsEmptyInlineBoxWithoutEdges(int index)
    {
        var items = Items;
        ref readonly var open = ref items[index];
        if (open.InlineSize != 0 || open.HasFlag(InlineItemFlags.HasEdges))
            return false;

        for (var i = index + 1; i < items.Length; i++)
        {
            ref readonly var item = ref items[i];
            if (item.Kind == InlineItemKind.CloseTag && item.Container == open.Container)
                return item.InlineSize == 0 && !item.HasFlag(InlineItemFlags.HasEdges);

            if (item.Kind != InlineItemKind.Text || item.HasText)
                return false;
        }

        return false;
    }

    /// <summary>The options of the line breaking of the character before <paramref name="position"/> (once the opportunities are computed).</summary>
    private LineBreakOptions GetBreakOptionsBefore(int position)
    {
        var runs = _breakRuns;
        var low = 0;
        var high = runs.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (runs[middle].Start <= position - 1)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return runs[low].Options;
    }

    /// <summary>The style that controls the opportunities of an item.</summary>
    private ComputedStyle GetOpportunityStyle(int index)
    {
        ref readonly var item = ref Items[index];
        return item.Kind is InlineItemKind.Text or InlineItemKind.ForcedBreak ? item.Style : item.Container.Style;
    }

    /// <summary>
    /// The hyphen displayed at the end of a line broken at a hyphenation opportunity after <paramref name="codePointBefore"/>:
    /// <c>hyphenate-character</c>, or with <c>auto</c> (the string is chosen by the user agent "based on the content language’s
    /// typographic conventions", https://drafts.csswg.org/css-text-4/#hyphenate-character): U+1400 CANADIAN SYLLABICS HYPHEN after
    /// the Canadian syllabics, U+0640 ARABIC TATWEEL after an Arabic letter in Uyghur (WPT css/css-text/hyphens/i18n), else
    /// U+2010 HYPHEN when the first available font has it, else U+002D HYPHEN-MINUS, as in Chrome. The bundled fonts stand for the
    /// fonts of the system, whose monospace fonts have U+2010 (the bundled one, Cousine, does not): when the first available font
    /// is a bundled font, U+2010 is used when a bundled font has it.
    /// </summary>
    private static string GetHyphen(ComputedStyle style, int codePointBefore, HyphenationLanguage language)
    {
        if (style.Layout.Text.HyphenateCharacter is { } hyphen)
            return hyphen;

        if (codePointBefore >= 0x0600)
        {
            var script = UnicodeScripts.GetScript(codePointBefore);
            if (script == UnicodeScript.CanadianAboriginal)
                return "\U00001400";

            if (script == UnicodeScript.Arabic && language == HyphenationLanguage.Uyghur)
                return "\U00000640";
        }

        var primary = style.Fonts.PrimaryFont;
        return primary.HasGlyph(0x2010) || (primary.IsBundled && style.Fonts.FindFontForCharacter(0x2010) is { IsBundled: true }) ? "\u2010" : "-";
    }

    /// <summary>
    /// The shaping options of the hyphen of a style: without <c>letter-spacing</c>, which "is not applied at the end of a line"
    /// (https://drafts.csswg.org/css-text-4/#letter-spacing-property).
    /// </summary>
    private static TextShapingOptions GetHyphenShapingOptions(ComputedStyle style) => GetShapingOptions(style, isRightToLeft: false) with { LetterSpacing = 0 };

    /// <summary>
    /// Whether a word may be hyphenated at a hyphenation opportunity with <c>hyphenate-limit-chars</c>
    /// (https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars-property): "If the word does not meet the required minimum
    /// number of characters in the word / before the hyphen / after the hyphen, then the word must not be hyphenated. Nonspacing
    /// combining marks (Mn) and intra-word punctuation (P*) do not count towards the minimum". The <c>auto</c> values do not
    /// restrict the soft hyphens (the explicit hyphenation opportunities). The automatic hyphenation opportunities are limited by
    /// <see cref="HyphenationDictionary.FindOpportunities"/>.
    /// </summary>
    private bool IsHyphenationAllowed(int position, ComputedStyle style)
    {
        var limits = style.Layout.Text.HyphenateLimitChars;
        if (limits == default)
            return true;

        var text = _text.AsSpan(0, _length);
        var before = 0;
        for (var i = position - 1; i >= 0 && IsWordCharacter(text[i]); i--)
        {
            before += CountsTowardsLimit(text[i]) ? 1 : 0;
        }

        var after = 0;
        for (var i = position; i < text.Length && IsWordCharacter(text[i]); i++)
        {
            after += CountsTowardsLimit(text[i]) ? 1 : 0;
        }

        return before + after >= limits.Word && before >= limits.Before && after >= limits.After;

        static bool IsWordCharacter(char c) => !char.IsWhiteSpace(c) && c != BidiControls.ObjectReplacementCharacter && c != (char)0x200B;

        static bool CountsTowardsLimit(char c)
        {
            if (char.IsLowSurrogate(c))
                return false;

            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.Format or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation);
        }
    }
}
