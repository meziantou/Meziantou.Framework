using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of an inline formatting context (https://drafts.csswg.org/css-inline-3/#inline-formatting-context): the
/// inline-level content of a block container is flattened into <see cref="InlineItem"/>s (white space processing, text
/// transformation, bidi control codes, atomic inlines), broken into lines (UAX #14 opportunities with the CSS tailorings),
/// and each line is turned into a line box (bidi reordering, inline box fragments, vertical alignment, text alignment).
/// </summary>
/// <remarks>
/// An instance holds the buffers of the algorithm (the text, the items, the line breaker and the bidi resolver), reused
/// from one formatting context to the next: the <see cref="LayoutEngine"/> keeps a pool of instances (an atomic inline
/// laid out while the items of its parent are collected uses another instance). It is not thread-safe.
/// </remarks>
internal sealed partial class InlineLayoutAlgorithm
{
    private readonly Unicode.LineBreaker _lineBreaker = new();
    private readonly BidiResolver _bidi = new();
    private List<InlineItem> _items = [];
    private List<InlineItem> _splitItems = [];
    private readonly List<LineBreakOptionsRun> _breakRuns = [];
    private readonly List<ContainerBox> _openBidiBoxes = [];
    private char[] _text = new char[256];
    private int[] _offsets = new int[256];
    private int _length;
    private LineBreakOpportunity[] _opportunities = [];

    /// <summary>Whether <see cref="_opportunities"/> was computed for the items: it is only needed when a line does not fit.</summary>
    private bool _opportunitiesComputed;

    /// <summary>Whether the text contains forced line breaks of UAX #14 that are not <see cref="InlineItemKind.ForcedBreak"/> items.</summary>
    private bool _hasMandatoryBreakCharacters;
    private BidiClass[] _bidiClasses = [];
    private char[] _mirrorBuffer = [];

    // The formatting context being laid out
    private LayoutContext _context = null!;
    private ContainerBox _block = null!;
    private ConstraintSpace _childSpace;
    private double _percentageBasis;
    private WritingDirection _writingDirection;

    /// <summary>
    /// Whether the intrinsic sizes of the content are computed (<see cref="ComputeIntrinsicInlineSizes"/>): the atomic inlines and the
    /// floats are not laid out, their intrinsic size contributions are used.
    /// </summary>
    private bool _measuring;

    /// <summary>The max-content contributions of the line-left and line-right floats of the line being measured, and the largest width of the line before a float cleared some of them (see <see cref="AddMeasuredFloat"/>).</summary>
    private double _measuredLineLeftFloats;
    private double _measuredLineRightFloats;
    private double _measuredWidthBeforeClearance;

    /// <summary>Whether the text needs the bidi algorithm: a right-to-left paragraph, a strong right-to-left character, or bidi control codes.</summary>
    private bool _needsBidi;

    /// <summary>Whether an item does not wrap (<c>text-wrap-mode: nowrap</c>): the soft wrap opportunities are filtered.</summary>
    private bool _hasNoWrap;

    /// <summary>Whether an inline box repeats its inline edges on each line (<c>box-decoration-break: clone</c>): the line breaking reserves their space.</summary>
    private bool _hasClonedEdges;

    /// <summary>The block container whose <c>::first-line</c> style applies to the first line of the formatting context, or null.</summary>
    private ContainerBox? _firstLineRoot;

    /// <summary>The outside marker that the first line box that is not empty places, or null.</summary>
    private PendingOutsideMarker? _outsideMarker;

    /// <summary>The block formatting context of the lines (the one of the block container, or its own when it is a root), during <see cref="Layout"/>.</summary>
    private BlockFormattingContextState? _blockFormattingContext;

    /// <summary>The content box of the block container, during <see cref="Layout"/>.</summary>
    private ContentArea _content;

    /// <summary>The floats that do not fit on the line being laid out: they are placed below it.</summary>
    private readonly List<int> _deferredFloats = [];
    private readonly Dictionary<ContainerBox, ComputedStyle> _firstLineStyles = new(ReferenceEqualityComparer.Instance);

    // The styles of the texts whose style is not the one of their box (the text of a display: contents element) on the first line
    private readonly Dictionary<ComputedStyle, ComputedStyle> _firstLineTextStyles = new(ReferenceEqualityComparer.Instance);

    private ComputedStyle? _lastBreakOptionsStyle;
    private LineBreakOptions _lastBreakOptions;
    private ContentLanguage? _blockContentLanguage;

    /// <summary>The items of the formatting context, after <see cref="CollectItems"/>.</summary>
    private Span<InlineItem> Items => CollectionsMarshal.AsSpan(_items);

    /// <summary>
    /// Lays out the inline-level content of a block container in line boxes, adds them to <paramref name="builder"/> and returns
    /// the block size of the lines (https://drafts.csswg.org/css2/#inline-formatting).
    /// </summary>
    public ContentLayoutResult Layout(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        Begin(context, box, content);
        try
        {
            // A block formatting context root whose children are inline creates the state of its context (for its floats)
            var isNewFormattingContext = content.BlockFormattingContext is null;
            _blockFormattingContext = content.BlockFormattingContext ?? new BlockFormattingContextState { LineClamp = content.LineClamp, TextGroupAlignment = content.TextGroupAlignment };
            _content = content;
            CollectItems();

            // https://drafts.csswg.org/css-lists-3/#list-style-position-outside: an outside marker is placed next to the first line
            // box of its list item, which may be in a descendant block container (the marker is then passed in the space)
            var marker = content.OutsideMarker;
            if (GetOutsideMarker(box) is { } ownMarker)
            {
                marker = new PendingOutsideMarker(ownMarker, box);
            }

            _firstLineRoot = FindFirstLineRoot(box);
            _outsideMarker = marker;
            ComputeWrapStyleLimits(builder, content);
            PrepareTextFit(content);

            // https://drafts.csswg.org/css-inline-3/#text-box-trim: the first and the last formatted lines trimmed by the block
            // container or by an ancestor
            var trimStart = IsTextBoxTrimmed(box, start: true);
            var trimEnd = IsTextBoxTrimmed(box, start: false);

            // https://drafts.csswg.org/css-text-4/#hyphenate-limit-lines: "the maximum number of successive hyphenated lines"
            var hyphenateLimitLines = box.Style.Layout.Text.HyphenateLimitLines;
            var hyphenatedLines = 0;
            var lines = new LineLayoutState(builder, content);
            var position = new LinePosition(0, 0);
            var items = _items.Count;

            // https://drafts.csswg.org/css-overflow-4/#line-clamp: the lines after the clamp point are hidden, and the block container
            // ends at the clamp point (the state of the lines and of the block formatting context is restored at the end)
            var lineClamp = _blockFormattingContext.LineClamp;
            ClampedLines? clamped = null;
            while (position.Item < items)
            {
                if (lineClamp is { IsClamped: true } && clamped is null)
                {
                    clamped = new ClampedLines(lines, _blockFormattingContext.Save(), _rubyBlockEndSpace, _rubyOverflowEnd);
                    _hidesLines = true;
                }

                var isFirstLine = lines.LineCount == 0;
                var indent = GetTextIndent(content, isFirstLine, lines.AfterForcedBreak);
                _lineAfterForcedBreak = lines.AfterForcedBreak;
                _lineAllowsHyphens = hyphenateLimitLines is not { } limit || hyphenatedLines < limit;

                // The line is placed after the ruby annotations that overflow the block-end side of the previous line (see
                // ApplyRubyOverflow): the floats and the initial letter boxes that shorten it are the ones at that position
                var lineBlockOffset = _rubyOverflowEnd > lines.BlockOffset && !_blockFormattingContext.Exclusions.IsEmpty ? _rubyOverflowEnd : lines.BlockOffset;
                _lineIsTrimmedAtStart = trimStart && !lines.HasFormattedLine;

                // https://drafts.csswg.org/css-inline-3/#text-box-trim: the block-start side of the first formatted line is
                // trimmed: the line box starts before the block-start edge of the content box, and so do the floats placed on it
                // ("the outer top of the floated box is aligned with the top of the current line box", as in the browsers)
                var floatTrim = _lineIsTrimmedAtStart ? MeasureStartTrimOfLineWithFloats(position, lineBlockOffset, indent, isFirstLine) : 0;
                var (end, line, opportunity, lineOffset) = LayoutLine(builder, position, lineBlockOffset - floatTrim, indent, isFirstLine, lines.LineCount);
                if (lineClamp is { IsEllipsisLine: true } && clamped is null && !line.IsEmpty)
                {
                    // https://drafts.csswg.org/css-overflow-4/#block-ellipsis: the last line before the clamp point is broken again,
                    // with the space of the block overflow ellipsis reserved at its end ("user agent must not shift down the line box
                    // where the ellipsis is to be placed in order to clear a float", unless the ellipsis itself does not fit, as Blink)
                    _blockEllipsis = CreateBlockEllipsis(lineClamp.Ellipsis);
                    _blockEllipsisIsLegacy = lineClamp.IsLegacy;
                    (end, line, opportunity, lineOffset) = LayoutLine(builder, position, lineBlockOffset - floatTrim, indent, isFirstLine, lines.LineCount);
                    _blockEllipsis = null;
                }

                lineOffset += floatTrim;

                hyphenatedLines = end.IsHyphenated ? hyphenatedLines + 1 : 0;
                // (the last line before the clamp point of a line-clamp container is the last formatted line)
                var trim = new LineTrim(trimStart && !lines.HasFormattedLine, (trimEnd && !HasFormattedLineAfter(end)) || (lineClamp is { IsClampLine: true } && clamped is null && !line.IsEmpty && IsTextBoxTrimmedAtClampPoint(box)));
                AddLine(ref lines, line, opportunity, marker, lineOffset, trim);
                if (line.Fragment is { } lineFragment)
                {
                    lineFragment.IsHiddenForPaint = clamped is not null;
                }

                if (!line.IsEmpty)
                {
                    lineClamp?.OnLine(content.BlockFormattingContextOffset.Block + lines.BlockOffset);
                }

                // The floats that did not fit on the line are placed below it
                foreach (var index in _deferredFloats)
                {
                    PlaceFloat(builder, index, lines.BlockOffset);
                }

                _deferredFloats.Clear();
                if (end.IsForced)
                {
                    ClearAfterForcedBreak(ref lines, end);
                }

                position = new LinePosition(end.Item, end.Position);
                lines.AfterForcedBreak = end.IsForced;
                lines.LineCount++;
            }

            // The block container ends at the clamp point: the lines after it do not change its size nor its baselines
            if (clamped is { } clampedLines)
            {
                lines = clampedLines.Lines;
                _blockFormattingContext.Restore(clampedLines.Context);
                _rubyBlockEndSpace = clampedLines.RubyBlockEndSpace;
                _rubyOverflowEnd = clampedLines.RubyOverflowEnd;
                _hidesLines = false;
            }

            // The annotations under the last line (not when the block-end side of the last line is trimmed: text-box-trim)
            if (HasRuby && !trimEnd)
            {
                ApplyRubyOverflow(ref lines, GetBlockEndPadding());
            }

            // A list item without line box: the marker is placed at the block-start edge of its content box, and the content
            // is at least as tall as the marker (as in the browsers)
            if (marker is { IsPlaced: false } && ReferenceEquals(marker.ListItem, box))
            {
                var markerSize = marker.PlaceWithoutLine(context, builder, content, firstBaseline: null);
                if (markerSize > lines.BlockOffset)
                {
                    if (!lines.HasLines && content.BlockFormattingContext is { } markerContext)
                    {
                        markerContext.CollapseMargins();
                    }

                    content.BlockFormattingContext?.AdvanceBlockPosition(markerSize - lines.BlockOffset);
                    lines.BlockOffset = markerSize;
                    lines.HasLines = true;
                }
            }

            // As in the browsers, an editing host always has the line of its caret: without line box (e.g. only collapsed white
            // space), it is as tall as a line
            if (!lines.HasLines && BlockFormattingContext.HasCaretLineIfEmpty(box) && box.Style.Layout.UsedLineHeight is var caretLineHeight && caretLineHeight > lines.BlockOffset)
            {
                if (content.BlockFormattingContext is { } caretLineContext)
                {
                    caretLineContext.CollapseMargins();
                    caretLineContext.AdvanceBlockPosition(caretLineHeight - lines.BlockOffset);
                }

                lines.BlockOffset = caretLineHeight;
                lines.HasLines = true;
            }

            // https://drafts.csswg.org/css2/#root-height: a block formatting context root contains its floats (the floats of a
            // line-clamp container are given to its line clamping: they are clipped to the content box of a clamped container)
            var blockSize = lines.BlockOffset;
            if (isNewFormattingContext && _blockFormattingContext.Exclusions.GetContentEnd() is { } floatsEnd && floatsEnd > blockSize)
            {
                if (lineClamp is not null)
                {
                    lineClamp.FloatsEnd = floatsEnd;
                }
                else
                {
                    blockSize = floatsEnd;
                }
            }

            // The line boxes separate the margins before and after them (https://drafts.csswg.org/css2/#collapsing-margins): a block
            // container without line box (or with only empty line boxes) collapses through
            return new ContentLayoutResult(blockSize, lines.FirstBaseline, lines.LastBaseline) { CollapsedThrough = !lines.HasLines };
        }
        finally
        {
            End();
        }
    }

    /// <summary>
    /// Computes the min-content and max-content inline sizes of the content (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes):
    /// the lines broken at every soft wrap opportunity (the emergency ones only with <c>overflow-wrap: anywhere</c>), and the
    /// lines broken only at the forced line breaks. The percentages of <c>text-indent</c> are 0.
    /// </summary>
    public (double MinContent, double MaxContent) ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in ContentArea content)
    {
        Begin(context, box, content);
        _measuring = true;
        try
        {
            CollectItems();
            _firstLineRoot = FindFirstLineRoot(box);
            var indentContent = content with { InlineSize = 0 };
            _lineAllowsHyphens = true;
            var maxContent = ComputeIntrinsicSize(IntrinsicSizeMode.MaxContent, double.PositiveInfinity, indentContent);
            var minContent = ComputeIntrinsicSize(IntrinsicSizeMode.MinContent, 0, indentContent);
            return (minContent, Math.Max(minContent, maxContent));
        }
        finally
        {
            End();
        }
    }

    private double ComputeIntrinsicSize(IntrinsicSizeMode mode, double availableSize, in ContentArea content)
    {
        var result = 0d;
        var position = new LinePosition(0, 0);
        var isFirstLine = true;
        var afterForcedBreak = false;
        while (position.Item < _items.Count)
        {
            var indent = GetTextIndent(content, isFirstLine, afterForcedBreak);
            _lineStartOffset = indent;
            _lineAfterForcedBreak = afterForcedBreak;
            var end = BreakLine(position, availableSize, isFirstLine, mode);
            result = Math.Max(result, Math.Max(end.Width, _measuredWidthBeforeClearance) + indent);
            position = new LinePosition(end.Item, end.Position);
            afterForcedBreak = end.IsForced;
            isFirstLine = false;
        }

        // The min-content contribution of a float is its margin box (it is on a line of its own); the max-content one is added to
        // its line (see BreakLine)
        if (mode == IntrinsicSizeMode.MinContent)
        {
            foreach (ref readonly var item in Items)
            {
                if (item.IsFloat)
                {
                    result = Math.Max(result, item.MinContentInlineSize);
                }
            }
        }

        return result;
    }

    private void Begin(LayoutContext context, ContainerBox box, in ContentArea content)
    {
        _context = context;
        _block = box;
        _childSpace = content.ChildSpace;
        _percentageBasis = content.InlineSize;
        _writingDirection = content.WritingDirection;
    }

    /// <summary>Releases the references to the boxes and the styles (the instance is pooled).</summary>
    private void End()
    {
        _items.Clear();
        _splitItems.Clear();
        _openBidiBoxes.Clear();
        _firstLineStyles.Clear();
        _firstLineTextStyles.Clear();
        _leaves.Clear();
        _nodes.Clear();
        _chain.Clear();
        _lineBoxes.Clear();
        _fragmentCounts.Clear();
        _firstLineRoot = null;
        _outsideMarker = null;
        _blockFormattingContext = null;
        _measuring = false;
        _content = default;
        _deferredFloats.Clear();
        _initialLetterItem = -1;
        _itemsData = null;
        _hasPercentageEdges = false;
        _lineIsTrimmedAtStart = false;
        _lastBreakOptionsStyle = null;
        _lastMetricsStyle = null;
        _lastIdeographicStyle = null;
        _blockSpaceAdvance = null;
        _blockEllipsis = null;
        _blockEllipsisText = null;
        _blockEllipsisHang = 0;
        _blockEllipsisIsLegacy = false;
        _hidesLines = false;
        _lineWidthLimits.Clear();
        _lineWidthReductions.Clear();
        _textFit = default;
        _fontScale = 1;
        _consistentFontScale = 1;
        _scaledStyles.Clear();
        _scaledShaped.Clear();
        _ignoresEmergencyBreaks = false;
        _lineStartOffset = 0;
        _lineAfterForcedBreak = false;
        _lineAllowsHyphens = true;
        ClearLineSpacing();
        ResetHyphenation();
        _blockContentLanguage = null;
        ClearRuby();
        _context = null!;
        _block = null!;
    }

    /// <summary>The block overflow ellipsis reserved at the end of the line being laid out (https://drafts.csswg.org/css-overflow-4/#block-ellipsis), or null.</summary>
    private LineEllipsis? _blockEllipsis;

    /// <summary>The &lt;string&gt; of <see cref="_blockEllipsis"/>, or null for the ellipsis character.</summary>
    private string? _blockEllipsisText;

    /// <summary>Whether <see cref="_blockEllipsis"/> is the one of <c>-webkit-line-clamp</c> (see <see cref="LineClamp.IsLegacy"/>).</summary>
    private bool _blockEllipsisIsLegacy;

    /// <summary>Whether the lines being laid out are after the clamp point of a line-clamp container: they and their floats are hidden.</summary>
    private bool _hidesLines;

    /// <summary>The state of the lines and of the block formatting context at the clamp point of a line-clamp container (see <see cref="LineClamp"/>).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ClampedLines(LineLayoutState Lines, BlockFormattingContextState.Snapshot Context, double RubyBlockEndSpace, double RubyOverflowEnd);

    /// <summary>
    /// Shapes the block overflow ellipsis (https://drafts.csswg.org/css-overflow-4/#block-ellipsis) of the block container: "The
    /// block overflow ellipsis is wrapped in an anonymous inline whose parent is the block container's root inline box": it has
    /// the style of the block container, not its <c>::first-line</c> style (WPT block-ellipsis-007). Its bidi runs are resolved
    /// with the direction of the block container (see <see cref="BuildLine"/> for the direction of the paragraph of the line).
    /// </summary>
    private LineEllipsis CreateBlockEllipsis(TextOverflowSide ellipsis)
    {
        _blockEllipsisText = ellipsis.Kind == TextOverflowKind.String ? ellipsis.String : null;
        var isRightToLeft = _block.Style.Layout.Direction == Direction.Rtl;
        var result = LineEllipsis.Create(_block, _block.Style, _blockEllipsisText, isRightToLeft);

        // https://drafts.csswg.org/css-text-4/#hanging-punctuation-property: the last character of the ellipsis is at the end of the
        // last line of the block container: a closing bracket or quote hangs with hanging-punctuation: last, a stop or a comma with
        // force-end; its advance is not reserved at the end of the line
        _blockEllipsisHang = 0;
        var hanging = _block.Style.Layout.Text.HangingPunctuation;
        var text = LineEllipsis.GetText(_block.Style, _blockEllipsisText);
        if (hanging != HangingPunctuation.None && text.Length > 0)
        {
            var last = char.IsLowSurrogate(text[^1]) && text.Length > 1 ? text[^2..] : text[^1..];
            var codePoint = char.ConvertToUtf32(last, 0);
            if (((hanging & HangingPunctuation.Last) != HangingPunctuation.None && TextSpacingClasses.IsClosingBracketOrQuote(codePoint))
                || ((hanging & HangingPunctuation.ForceEnd) != HangingPunctuation.None && TextSpacingClasses.IsStopOrComma(codePoint)))
            {
                _blockEllipsisHang = Math.Min(result.InlineSize, LineEllipsis.Create(_block, _block.Style, last, isRightToLeft).InlineSize);
            }
        }

        return result;
    }

    /// <summary>The advance of the end of the block overflow ellipsis that hangs (see <see cref="CreateBlockEllipsis"/>).</summary>
    private double _blockEllipsisHang;

    /// <summary>The space that the block overflow ellipsis takes at the end of its line: its advance, but its part that hangs.</summary>
    private double GetBlockEllipsisReservedSize(LineEllipsis? ellipsis) => ellipsis is null ? 0 : Math.Max(0, ellipsis.InlineSize - _blockEllipsisHang);

    /// <summary>The state of the lines of a formatting context.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct LineLayoutState(FragmentBuilder builder, in ContentArea content)
    {
        public readonly FragmentBuilder Builder = builder;
        public readonly ContentArea Content = content;

        /// <summary>The block offset of the next line, from the block-start edge of the content box.</summary>
        public double BlockOffset;

        public int LineCount;

        /// <summary>Whether a line box that is not empty was placed (it separates the margins).</summary>
        public bool HasLines;

        /// <summary>Whether a line box that is not empty was added (the first formatted line was laid out).</summary>
        public bool HasFormattedLine;

        /// <summary>Whether the previous line ended with a forced line break (<c>text-indent: each-line</c>, <c>text-align-last</c>).</summary>
        public bool AfterForcedBreak;

        public double? FirstBaseline;

        public double? LastBaseline;
    }

    /// <summary>A position in the items: the index of an item and a position in the text (inside a text item, or its start).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LinePosition(int Item, int Position);

    /// <summary>
    /// Returns whether the content after a line can make another formatted line: the line is the last formatted line
    /// (https://drafts.csswg.org/css-inline-3/#text-box-trim) when only empty inline boxes and out-of-flow boxes follow it
    /// (their line box is a zero-height line box).
    /// </summary>
    private bool HasFormattedLineAfter(in LineEnd end)
    {
        for (var i = end.Item; i < _items.Count; i++)
        {
            var item = _items[i];
            switch (item.Kind)
            {
                case InlineItemKind.OutOfFlow:
                    continue;

                case InlineItemKind.OpenTag or InlineItemKind.CloseTag when item.InlineSize == 0 && (item.Flags & InlineItemFlags.HasEdges) == InlineItemFlags.None:
                    continue;

                case InlineItemKind.Text when item.End <= Math.Max(item.Start, i == end.Item ? end.Position : item.Start):
                    continue;

                default:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The space available to a line box at a block offset (https://drafts.csswg.org/css2/#floats): its offset from the
    /// inline-start edge of the content box and its inline size, whether floats shorten it, and the block offset (from the
    /// block-start edge of the content box) where the first of these floats ends.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LineOpportunity(double Offset, double Size)
    {
        public bool IsShortened { get; init; }

        public double NextBlockOffset { get; init; }

        /// <summary>Whether <see cref="NextBlockOffset"/> is a step along a float area (see <see cref="LayoutOpportunity.StepsAlongShape"/>).</summary>
        public bool StepsAlongShape { get; init; }

        /// <summary>Whether a float with a float area is next to the band (see <see cref="LayoutOpportunity.HasFloatArea"/>).</summary>
        public bool HasFloatArea { get; init; }
    }

    /// <summary>
    /// Returns the space available to a line box placed at <paramref name="blockOffset"/> (from the block-start edge of the content
    /// box) whose block size is <paramref name="blockSize"/>: "line boxes next to the float are shortened to make room for the
    /// margin box of the float" (https://drafts.csswg.org/css2/#floats), or for its float area defined by <c>shape-outside</c>.
    /// </summary>
    private LineOpportunity GetLineOpportunity(double blockOffset, double blockSize)
    {
        var exclusions = _blockFormattingContext!.Exclusions;
        if (exclusions.IsEmpty)
            return new LineOpportunity(0, _content.InlineSize);

        var origin = _content.BlockFormattingContextOffset;
        var opportunity = exclusions.GetOpportunity(origin.Block + blockOffset, blockSize, origin.Inline, _content.InlineSize, useFloatAreas: true);
        if (!opportunity.IsShortened)
            return new LineOpportunity(0, _content.InlineSize) { HasFloatArea = opportunity.HasFloatArea };

        var offset = _writingDirection.IsRightToLeft ? origin.Inline + _content.InlineSize - opportunity.LineRight : opportunity.LineLeft - origin.Inline;
        return new LineOpportunity(offset, Math.Max(0, opportunity.InlineSize)) { IsShortened = true, NextBlockOffset = opportunity.NextBlockOffset - origin.Block, StepsAlongShape = opportunity.StepsAlongShape, HasFloatArea = opportunity.HasFloatArea };
    }

    /// <summary>The largest number of steps a line box is moved down along the float areas (see <see cref="ExclusionSpace.ShapeStep"/>) before it is placed where it is.</summary>
    private const int MaxShapeSteps = 100_000;

    /// <summary>
    /// Breaks and builds the line that starts at <paramref name="position"/>, at or below <paramref name="blockOffset"/>, next to
    /// the floats of the block formatting context. The floats met on the line are placed (or deferred below the line).
    /// Returns the end of the line, its line box, its opportunity and its block offset.
    /// </summary>
    /// <remarks>
    /// The line is first broken in the space at its block offset; when a float met on the line is placed, the line is broken
    /// again in the space that remains. "If a shortened line box is too small to contain any content, then the line box is
    /// shifted downward (and its width recomputed) until either some content fits or there are no more floats present"
    /// (https://drafts.csswg.org/css2/#floats). Once its height is known, the floats in the band of the line height shorten the
    /// line too: it is laid out again in that band when it is narrower.
    /// </remarks>
    private (LineEnd End, LineBoxResult Line, LineOpportunity Opportunity, double BlockOffset) LayoutLine(FragmentBuilder builder, LinePosition position, double blockOffset, double indent, bool isFirstLine, int lineIndex)
    {
        var bandSize = 0d;
        var attempts = 0;
        var shapeSteps = 0;
        var bandShrunk = false;

        // text-wrap-style: the line breaking may use a narrower space than the line box (see ComputeWrapStyleLimits); next to
        // floats, the space of each line is reduced by the same amount (see BalanceLinesNextToFloats)
        var limit = lineIndex < _lineWidthLimits.Count ? _lineWidthLimits[lineIndex] : double.PositiveInfinity;
        var reduction = lineIndex < _lineWidthReductions.Count ? _lineWidthReductions[lineIndex] : 0;

        // The content before a float that was placed on the line stays on the line (it fitted next to the float)
        var keptWidth = 0d;
        _lineFits = true;
        while (true)
        {
            var opportunity = GetLineOpportunity(blockOffset, bandSize);
            var space = opportunity.Size - indent - GetBlockEllipsisReservedSize(_blockEllipsis);
            var available = Math.Min(opportunity.Size, limit) - indent - GetBlockEllipsisReservedSize(_blockEllipsis);
            if (reduction > 0)
            {
                available = Math.Max(available - reduction, Math.Min(keptWidth, space));
            }

            _lineStartOffset = opportunity.Offset + indent;
            var end = BreakLine(position, available, isFirstLine, IntrinsicSizeMode.None);
            if (end.StopsAtFloat)
            {
                // https://drafts.csswg.org/css2/#float-position: "If there is a line box, the outer top of the floated box is aligned
                // with the top of the current line box": the float is placed on the line when it fits next to the content before it
                // (the line is then shortened), else below the line ("as high as possible", but not higher than the line box).
                // The floats after a float placed below the line are placed after it: "the outer top of a floating box may not be
                // higher than the outer top of any block or floated box generated by an element earlier in the source document"
                ref var item = ref CollectionsMarshal.AsSpan(_items)[end.Item];
                item.Flags |= InlineItemFlags.FloatPlaced;
                if (item.Box is ContainerBox { InitialLetter: not null })
                {
                    // https://drafts.csswg.org/css-inline-3/#initial-letter-styling: the initial letter box is placed at the start
                    // of its line (after the indentation of the line), which may move the line down
                    blockOffset = BeginInitialLetter(end.Item, blockOffset, indent);
                    indent = 0;
                    bandSize = 0;
                    continue;
                }

                // (the float fits in the space of the line, whatever the space the line breaking uses to balance the lines)
                var floatSpace = reduction > 0 ? space : available;
                if (_deferredFloats.Count > 0 || (end.Width > 0 && (end.Width + item.InlineSize > floatSpace || !ContentAfterFloatFits(position, floatSpace - item.InlineSize, isFirstLine))))
                {
                    _deferredFloats.Add(end.Item);
                }
                else
                {
                    PlaceFloat(builder, end.Item, blockOffset);
                    keptWidth = Math.Max(keptWidth, end.Width);
                }

                continue;
            }

            // https://drafts.csswg.org/css-text-4/#hyphenate-limit-last: with always, "the last full line of the element should
            // not be hyphenated": the line before the last line is broken again without its hyphenation opportunities
            if (end.IsHyphenated && _lineAllowsHyphens && _block.Style.Layout.Text.HyphenateLimitLastAlways && IsBeforeLastLine(end, available))
            {
                _lineAllowsHyphens = false;
                continue;
            }

            // (the steps along a float area do not count: they are bounded by the block size of the float)
            if (opportunity.StepsAlongShape && shapeSteps < MaxShapeSteps)
            {
                shapeSteps++;
            }
            else
            {
                attempts++;
            }

            // (the line of an initial letter box is not moved below the floats: the initial letter is at its start)
            var canMove = attempts < _blockFormattingContext!.Exclusions.Exclusions.Count * 4 + 8 && _initialLetterItem < 0;
            // (as in the browsers, the lines of a block container that does not wrap stay next to the floats: WPT
            // css/css-text/white-space/pre-float-001.html)
            var doesNotFit = end.Width > (reduction > 0 ? space : available) + FitTolerance && end.Width > 0 && _block.Style.Layout.TextWrapMode != TextWrapMode.NoWrap;
            if (opportunity.IsShortened && (doesNotFit || (_blockEllipsis is not null && available < -LayoutEpsilon)) && canMove && !double.IsPositiveInfinity(opportunity.NextBlockOffset))
            {
                blockOffset = opportunity.NextBlockOffset;
                bandSize = 0;
                bandShrunk = false;
                continue;
            }

            // A float deferred below the line while the content after it was measured is after the end of the line when the
            // line is broken before it: it is a float of the next line (https://drafts.csswg.org/css2/#float-position: "the
            // outer top of an element's floating box may not be higher than the top of any line-box containing a box generated
            // by an element earlier in the source document"; WPT css/CSS2/floats/floats-line-wrap-shifted-001.html)
            for (var i = _deferredFloats.Count - 1; i >= 0 && _deferredFloats[i] > end.Item; i--)
            {
                CollectionsMarshal.AsSpan(_items)[_deferredFloats[i]].Flags &= ~InlineItemFlags.FloatPlaced;
                _deferredFloats.RemoveAt(i);
            }

            _outOfFlowPositions.Clear();
            var line = BuildLine(position, end, opportunity, indent, isFirstLine);
            // The band of the line includes the ruby annotations that overflow its block-end side
            var lineBand = line.BlockSize + Math.Max(0, -line.RubyBlockEndSpace);
            if (!_blockFormattingContext.Exclusions.IsEmpty && lineBand > bandSize && canMove)
            {
                var band = GetLineOpportunity(blockOffset, lineBand);
                if (band.Size < opportunity.Size || band.Offset != opportunity.Offset)
                {
                    bandSize = lineBand;
                    continue;
                }
            }
            else if (opportunity.HasFloatArea && lineBand < bandSize && canMove && !bandShrunk)
            {
                // The line broken in the band of a taller line (the content that fitted before the band was known) is next to
                // a float area: it is laid out again in its own band, where the float area may leave more space (once: the
                // line may grow again)
                var band = GetLineOpportunity(blockOffset, lineBand);
                if (band.Size != opportunity.Size || band.Offset != opportunity.Offset)
                {
                    bandSize = lineBand;
                    bandShrunk = true;
                    continue;
                }
            }

            if (_initialLetterItem >= 0)
            {
                blockOffset = FinishInitialLetter(builder, line, blockOffset);
            }

            _lineFits = end.Width <= available + FitTolerance;
            return (end, line, opportunity, blockOffset);
        }
    }

    /// <summary>
    /// Whether the line that starts at <paramref name="position"/>, whose float met by the line breaking is flagged as placed,
    /// fits in <paramref name="availableSize"/> (the space next to the float): the content after the float up to the next
    /// soft wrap opportunity must fit next to it, else the float is placed below the line, as in Firefox (WPT
    /// css/CSS2/floats/float-nowrap-7.html: a float inside unbreakable text that overflows the line is not placed beside it).
    /// </summary>
    private bool ContentAfterFloatFits(LinePosition position, double availableSize, bool isFirstLine)
    {
        var end = BreakLine(position, availableSize, isFirstLine, IntrinsicSizeMode.None);
        return end.Width <= availableSize + FitTolerance;
    }

    /// <summary>
    /// Returns how much the block-start side of the line that starts at <paramref name="position"/> is trimmed by
    /// <c>text-box-trim</c> when floats are placed on it (0 for a line without float, or for a line that is not a formatted
    /// line): the line is broken and built without its floats, which are not placed (the trimmed amount is the one of the line
    /// with the content that fits without them).
    /// </summary>
    private double MeasureStartTrimOfLineWithFloats(LinePosition position, double blockOffset, double indent, bool isFirstLine)
    {
        var items = CollectionsMarshal.AsSpan(_items);
        var hasFloat = false;
        for (var i = position.Item; i < items.Length && !hasFloat; i++)
        {
            hasFloat = items[i].IsFloat && !items[i].HasFlag(InlineItemFlags.FloatPlaced);
        }

        if (!hasFloat)
            return 0;

        var opportunity = GetLineOpportunity(blockOffset, blockSize: 0);
        _lineStartOffset = opportunity.Offset + indent;
        List<int>? floats = null;
        LineEnd end;
        while (true)
        {
            end = BreakLine(position, opportunity.Size - indent, isFirstLine, IntrinsicSizeMode.None);
            if (!end.StopsAtFloat)
                break;

            items[end.Item].Flags |= InlineItemFlags.FloatPlaced;
            floats ??= [];
            floats.Add(end.Item);
        }

        if (floats is null)
            return 0;

        _outOfFlowPositions.Clear();
        var line = BuildLine(position, end, opportunity, indent, isFirstLine);
        _outOfFlowPositions.Clear();
        foreach (var index in floats)
        {
            items[index].Flags &= ~InlineItemFlags.FloatPlaced;
        }

        return line.IsEmpty ? 0 : GetTextBoxTrimAmounts(line, new LineTrim(Start: true, End: false), isFirstLine).Start;
    }

    /// <summary>Places a float item in the block formatting context, at or below <paramref name="blockOffset"/> (from the block-start edge of the content box).</summary>
    private void PlaceFloat(FragmentBuilder builder, int index, double blockOffset)
    {
        ref readonly var item = ref Items[index];

        // https://drafts.csswg.org/css-overflow-4/#line-clamp: the floats after the clamp point are hidden
        item.Fragment!.IsHiddenForPaint = _hidesLines;
        BlockFormattingContext.PlaceFloat(_context, _blockFormattingContext!, (ContainerBox)item.Box, item.Fragment!, _content, builder, _content.BlockFormattingContextOffset.Block + blockOffset, GetInlineAncestorsRelativeOffset(item.Container));
    }

    /// <summary>
    /// The <c>clear</c> property of a <c>br</c> element (https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3):
    /// as in the browsers, the line after its line break is placed below the floats it clears (the block container grows to
    /// contain them when it is the last line).
    /// </summary>
    private void ClearAfterForcedBreak(ref LineLayoutState lines, in LineEnd end)
    {
        if (GetClearOffsetAfterForcedBreak(end) is not { } blockOffset || blockOffset <= lines.BlockOffset)
            return;

        var blockFormattingContext = _blockFormattingContext!;

        if (!lines.HasLines)
        {
            blockFormattingContext.CollapseMargins();
        }

        blockFormattingContext.AdvanceBlockPosition(blockOffset - lines.BlockOffset);
        lines.BlockOffset = blockOffset;
        lines.HasLines = true;
    }

    /// <summary>
    /// Returns the block offset (from the block-start edge of the content box) below the floats that the <c>clear</c> property
    /// of the <c>br</c> element that ends a line clears (see <see cref="ClearAfterForcedBreak"/>), or null.
    /// </summary>
    private double? GetClearOffsetAfterForcedBreak(in LineEnd end)
    {
        // The forced line break of the line, before the ends of the inline boxes that stay on the line
        var items = Items;
        var index = Math.Min(end.Item, items.Length) - 1;
        while (index >= 0 && items[index].Kind is InlineItemKind.CloseTag or InlineItemKind.Control)
        {
            index--;
        }

        if (index < 0 || items[index].Kind != InlineItemKind.ForcedBreak || items[index].Box is not TextRun { Kind: TextRunKind.LineBreak } run)
            return null;

        var sides = ExclusionSpace.GetClearSides(run.Style.Layout.Clear, _writingDirection);
        if (sides == ClearSides.None || _blockFormattingContext!.Exclusions.GetClearanceOffset(sides) is not { } clearOffset)
            return null;

        return clearOffset - _content.BlockFormattingContextOffset.Block;
    }

    /// <summary>
    /// Returns the indentation of a line (https://drafts.csswg.org/css-text-4/#text-indent-property): the first line of the
    /// block container (and the lines after a forced line break with <c>each-line</c>), or the other lines with <c>hanging</c>.
    /// </summary>
    private double GetTextIndent(in ContentArea content, bool isFirstLine, bool afterForcedBreak)
    {
        var text = _block.Style.Layout.Text;
        var indented = isFirstLine || (afterForcedBreak && text.TextIndentEachLine);

        // "The first line of an anonymous block box is only affected if it is the first child of its parent element": the
        // anonymous blocks of a block container (not the anonymous flex and grid items, which are block containers of their own)
        if (isFirstLine && _block.IsAnonymous && _block.Parent is { Display.Inside: DisplayInside.Flow or DisplayInside.FlowRoot } && HasPreviousSiblingBox(_block))
        {
            indented = false;
        }

        if (text.TextIndentHanging)
        {
            indented = !indented;
        }

        return indented ? text.TextIndent.Resolve(content.InlineSize) : 0;
    }

    /// <summary>Whether a box follows another box of its parent (the outside marker of a list item is not in the flow of its children).</summary>
    private static bool HasPreviousSiblingBox(ContainerBox box)
    {
        for (var sibling = box.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
        {
            if (sibling is not ContainerBox { IsOutsideMarker: true })
                return true;
        }

        return false;
    }

    /// <summary>Returns the outside marker of a list item whose content is inline, or null.</summary>
    private static ContainerBox? GetOutsideMarker(ContainerBox box)
    {
        foreach (var child in box.Children)
        {
            if (child is ContainerBox { IsOutsideMarker: true } marker)
                return marker;
        }

        return null;
    }

    /// <summary>
    /// Returns the block container whose <c>::first-line</c> style (https://drafts.csswg.org/css-pseudo-4/#first-line-pseudo)
    /// applies to the first line of <paramref name="box"/>: the box itself, or an ancestor whose first formatted line is in
    /// the box (the box is in its first in-flow block-level children).
    /// </summary>
    private static ContainerBox? FindFirstLineRoot(ContainerBox box)
    {
        for (var current = box; current is not null; current = current.Parent)
        {
            if (current.FirstLineStyle is not null)
                return current;

            if (!IsOnFirstLineOfParent(current))
                return null;
        }

        return null;
    }

    /// <summary>Whether the first formatted line of a block container is the first formatted line of its parent: the box is its first in-flow block-level child.</summary>
    private static bool IsOnFirstLineOfParent(ContainerBox box)
    {
        // The floats, the absolutely positioned boxes and the outside markers are not on the first formatted line of their ancestors
        if (box.IsOutOfFlow || box.IsOutsideMarker || box.Kind == ContainerBoxKind.Viewport || box.Parent is not { IsBlockContainer: true, ChildrenAreInline: false } parent)
            return false;

        foreach (var sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, box))
                return true;

            if (sibling is ContainerBox { IsOutsideMarker: false, IsOutOfFlow: false })
                return false;
        }

        return false;
    }

    /// <summary>
    /// Returns the style of a box on the first formatted line: its style inheriting from the <c>::first-line</c> pseudo-element
    /// of the block container (https://drafts.csswg.org/css-pseudo-4/#first-line-inheritance), or its style when there is no
    /// <c>::first-line</c> style.
    /// </summary>
    private ComputedStyle GetFirstLineStyle(ContainerBox box)
    {
        if (_firstLineRoot is null)
            return box.Style;

        if (_firstLineStyles.TryGetValue(box, out var style))
            return style;

        if (ReferenceEquals(box, _block))
        {
            style = GetRootFirstLineStyle();
        }
        else
        {
            style = box.Parent is { } parent ? box.Style.WithParent(GetFirstLineStyle(parent)) : box.Style;
        }

        _firstLineStyles[box] = style;
        return style;
    }

    /// <summary>
    /// Returns the style of the root inline box on the first formatted line: "the fictional start tags of a ::first-line
    /// pseudo-element [are] nested just inside the innermost enclosing block-level element"
    /// (https://drafts.csswg.org/css-pseudo-4/#first-line-pseudo), so the <c>::first-line</c> style of an ancestor block
    /// container inherits from the style of the block container of the line (not from its originating element), and the ones
    /// of several ancestors are nested, the outermost first.
    /// </summary>
    private ComputedStyle GetRootFirstLineStyle()
    {
        if (ReferenceEquals(_block, _firstLineRoot) && !HasFirstLineAncestor(_block))
            return _block.FirstLineStyle!;

        var roots = new List<ContainerBox>();
        for (var current = _block; current is not null; current = current.Parent)
        {
            if (current.FirstLineStyle is not null)
            {
                roots.Add(current);
            }

            if (!IsOnFirstLineOfParent(current))
                break;
        }

        var style = _block.Style;
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            style = roots[i].FirstLineStyle!.WithParent(style);
        }

        return style;

        static bool HasFirstLineAncestor(ContainerBox box)
        {
            for (var current = box; IsOnFirstLineOfParent(current); current = current.Parent!)
            {
                if (current.Parent!.FirstLineStyle is not null)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Returns the style of a box on a line (with the used font size of the line being built when <c>text-fit</c> scales it).</summary>
    private ComputedStyle GetLineStyle(ContainerBox box, bool isFirstLine) => GetScaledStyle(isFirstLine && _firstLineRoot is not null ? GetFirstLineStyle(box) : box.Style);

    /// <summary>Returns the style of an item on a line (with the used font size of the line being built when <c>text-fit</c> scales it).</summary>
    private ComputedStyle GetLineStyle(in InlineItem item, bool isFirstLine) => GetScaledStyle(GetUnscaledLineStyle(item, isFirstLine));

    private ComputedStyle GetUnscaledLineStyle(in InlineItem item, bool isFirstLine)
    {
        if (!isFirstLine || _firstLineRoot is null)
            return item.Style;

        // The text of a ::first-letter box has the style of the box
        if (item.Kind is not (InlineItemKind.Text or InlineItemKind.ForcedBreak))
            return GetFirstLineStyle((ContainerBox)item.Box);

        var containerStyle = GetFirstLineStyle(item.Container);
        if (ReferenceEquals(item.Style, item.Container.Style) || ReferenceEquals(containerStyle, item.Container.Style) || item.Container.PseudoElement is not null)
            return containerStyle;

        // The text of a display: contents element has its style, inheriting from the first line style of the box it is in
        if (!_firstLineTextStyles.TryGetValue(item.Style, out var style))
        {
            style = item.Style.WithParent(containerStyle);
            _firstLineTextStyles[item.Style] = style;
        }

        return style;
    }

    /// <summary>Whether the block-start (block-end) side of a line is trimmed by <c>text-box-trim</c> (https://drafts.csswg.org/css-inline-3/#text-box-trim).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LineTrim(bool Start, bool End);

    /// <summary>Adds a line box to the block container and updates the state of the block formatting context.</summary>
    private void AddLine(ref LineLayoutState lines, in LineBoxResult line, LineOpportunity opportunity, PendingOutsideMarker? marker, double lineBlockOffset, LineTrim trim)
    {
        var content = lines.Content;
        var blockFormattingContext = _blockFormattingContext!;
        if (lineBlockOffset > lines.BlockOffset)
        {
            // The line was moved down below floats: the space separates the margins before and after it
            if (!lines.HasLines)
            {
                blockFormattingContext.CollapseMargins();
            }

            blockFormattingContext.AdvanceBlockPosition(lineBlockOffset - lines.BlockOffset);
            lines.BlockOffset = lineBlockOffset;
            lines.HasLines = true;
        }

        if (!line.IsEmpty)
        {
            ApplyRubyOverflow(ref lines, blockEndPadding: 0);
        }

        var blockOffset = lines.BlockOffset;
        var lineBlockSize = line.BlockSize;
        if (!line.IsEmpty)
        {
            // https://drafts.csswg.org/css-inline-3/#text-box-trim: the first (last) formatted line is trimmed on its block-start
            // (block-end) side to the metric of text-box-edge of its root inline box (the line moves toward the block-start edge)
            var (trimStart, trimEnd) = GetTextBoxTrimAmounts(line, trim, lines.LineCount == 0);
            blockOffset -= trimStart;
            lineBlockSize -= trimStart + trimEnd;

            // https://drafts.csswg.org/css2/#collapsing-margins: a line box separates the margins before and after it. The
            // position in the block formatting context is advanced line by line
            if (!lines.HasLines)
            {
                blockFormattingContext.CollapseMargins();
            }

            blockFormattingContext.AdvanceBlockPosition(lineBlockSize);
            lines.HasLines = true;
            lines.HasFormattedLine = true;
            lines.FirstBaseline ??= blockOffset + line.Baseline;
            lines.LastBaseline = blockOffset + line.Baseline;
        }

        var lineOffset = new LogicalOffset(content.Offset.Inline + opportunity.Offset, content.Offset.Block + blockOffset);
        if (line.Fragment is { } fragment)
        {
            lines.Builder.AddChild(fragment, lineOffset);
        }

        // The absolutely positioned boxes at their static position (https://drafts.csswg.org/css-position-3/#staticpos-rect), moved
        // with the relatively positioned inline boxes they are in (the floats are placed in the block formatting context)
        foreach (var (index, inlineOffset, afterContent, level) in _outOfFlowPositions)
        {
            ref readonly var item = ref Items[index];
            if (item.IsFloat)
                continue;

            // "The static-position rectangle is a zero-thickness rectangle spanning between the line-over/line-under sides of the
            // line box that would have contained its hypothetical box and positioned at its inline-start static position". A box
            // whose original display is block-level is on the next line when content precedes it on the line, at the start of the
            // line (as in the browsers)
            // (the box is aligned in its static-position rectangle by its self-alignment properties; the one of a block-level box
            // spans the inline-axis sides of the content box, as in block layout)
            var box = (ContainerBox)item.Box;
            var (inlineEdge, blockEdge) = PositionedLayout.GetStaticPositionAlignment(box, box.Parent ?? _block, _writingDirection);
            LogicalOffset staticPosition;
            if (PositionedLayout.IsOriginalDisplayInlineLevel(box.Style))
            {
                // The inline-start edge of the hypothetical box is the one of its bidirectional embedding level
                // (https://drafts.csswg.org/css-writing-modes-4/#bidi-algo): in a run whose direction is not the one of the
                // block container, the box extends toward the inline-start side of the block container (as in Blink)
                if (inlineEdge == StaticPositionEdge.Start && ((level & 1) != 0) != _writingDirection.IsRightToLeft)
                {
                    inlineEdge = StaticPositionEdge.End;
                }

                var blockStart = blockEdge switch
                {
                    StaticPositionEdge.End => line.BlockSize,
                    StaticPositionEdge.Center => line.BlockSize / 2,
                    _ => 0,
                };

                staticPosition = new LogicalOffset(lineOffset.Inline + inlineOffset, lineOffset.Block + blockStart);
            }
            else
            {
                var staticInline = inlineEdge switch
                {
                    StaticPositionEdge.End => content.Offset.Inline + content.InlineSize,
                    StaticPositionEdge.Center => content.Offset.Inline + (content.InlineSize / 2),

                    // The edge of the content box: the position does not avoid the floats (as in the browsers, WPT
                    // css/CSS2/abspos/between-float-and-text.html)
                    _ => content.Offset.Inline,
                };

                staticPosition = new LogicalOffset(staticInline, lineOffset.Block + (afterContent ? line.BlockSize : 0));
            }

            lines.Builder.AddOutOfFlowCandidate(box, staticPosition, inlineEdge, blockEdge, adjustment: GetInlineAncestorsRelativeOffset(item.Container));
        }

        _outOfFlowPositions.Clear();

        if (marker is { IsPlaced: false } && !line.IsEmpty)
        {
            PlaceOutsideMarker(marker, lines.Builder, content, lineOffset, line.Baseline);
        }

        lines.BlockOffset += lineBlockSize;

        // https://drafts.csswg.org/css-ruby-1/#line-height: the next line is placed after the annotations that overflow the block-end
        // side of the line (it may move back into the space before its own content, see ApplyRubyAnnotationOverflow); after the
        // last line, they may overflow into the block-end padding of the block container (see ApplyRubyOverflow)
        if (HasRuby && !line.IsEmpty)
        {
            _rubyBlockEndSpace = line.RubyBlockEndSpace;
            _rubyOverflowEnd = line.RubyBlockEndSpace < 0 ? lines.BlockOffset - line.RubyBlockEndSpace : double.NegativeInfinity;
        }
    }

    /// <summary>
    /// Returns the sum of the relative offsets (https://drafts.csswg.org/css-position-3/#relpos-insets) of an inline box and of its
    /// inline ancestors in the block container: the content of a relatively positioned inline box moves with it.
    /// </summary>
    private PhysicalOffset GetInlineAncestorsRelativeOffset(ContainerBox container)
    {
        var offset = PhysicalOffset.Zero;
        var width = _writingDirection.IsHorizontal ? _childSpace.PercentageInlineSize : _childSpace.PercentageBlockSize;
        var height = _writingDirection.IsHorizontal ? _childSpace.PercentageBlockSize : _childSpace.PercentageInlineSize;
        for (var box = container; box is { IsInlineBox: true } && !ReferenceEquals(box, _block); box = box.Parent)
        {
            if (box.Style.Layout.Position == PositionKind.Relative)
            {
                offset += PositionedLayout.GetRelativeOffset(box.Style.Layout, _writingDirection, width, height, out _);
            }
        }

        return offset;
    }

    /// <summary>
    /// Places an outside marker next to a line box: the inline-end edge of its margin box at the inline-start edge of the
    /// content box, its baseline aligned with the baseline of the line (as in the browsers, the line is at least as tall as
    /// the marker requires: see <see cref="AlignVertically"/>).
    /// </summary>
    private void PlaceOutsideMarker(PendingOutsideMarker marker, FragmentBuilder builder, in ContentArea content, LogicalOffset lineOffset, double lineBaseline)
    {
        var fragment = _context.LayoutBox(marker.Marker, _childSpace);
        var margin = _writingDirection.ToLogical(fragment.Margin);
        var size = _writingDirection.ToLogical(fragment.Size);
        // A marker without baseline gets one synthesized from its border box (its line-under edge, or its center for the
        // central baseline of vertical text)
        var baseline = fragment.FirstBaseline
            ?? (_block.Style.Layout.HasCentralDominantBaseline ? size.Block / 2 : _writingDirection.LineUnder == _writingDirection.BlockStart ? 0 : size.Block);
        // As in the browsers (Blink, WebKit), the marker is outside of the border box of its list item: its paddings and borders
        // do not move it (when the line is in a descendant block container, it is outside of the content box of that block)
        var start = ReferenceEquals(marker.ListItem, _block) ? 0 : content.Offset.Inline;

        // The marker is next to the line box: a line box shortened by a float on its start side moves it beside the float (as in
        // the browsers, WPT css/CSS2/lists/list-style-applies-to-017.html for a list item without content)
        start += Math.Max(0, lineOffset.Inline - content.Offset.Inline);
        var inlineOffset = start - margin.InlineEnd - size.Inline;
        builder.AddChild(fragment, new LogicalOffset(inlineOffset, lineOffset.Block + lineBaseline - baseline));
        marker.IsPlaced = true;
    }
}
