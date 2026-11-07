using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The line breaking choices that depend on several lines: text-wrap-style (https://drafts.csswg.org/css-text-4/#text-wrap-style),
// hyphenate-limit-last (https://drafts.csswg.org/css-text-4/#hyphenate-limit-last), and the trimming of the first and last
// formatted lines by text-box-trim (https://drafts.csswg.org/css-inline-3/#text-box-trim).
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>
    /// The maximum number of lines balanced by <c>text-wrap-style: balance</c>: "UAs may treat this value as auto if there are
    /// more than ten lines" (as Firefox does; the balancing breaks the content several times).
    /// </summary>
    private const int MaxBalancedLines = 10;

    /// <summary>Whether the line breaking ignores the emergency opportunities of <c>overflow-wrap</c> (while the lines are balanced).</summary>
    private bool _ignoresEmergencyBreaks;

    /// <summary>The maximum inline size of the line box used by the line breaking of each line (by line index), see <see cref="ComputeWrapStyleLimits"/>.</summary>
    private readonly List<double> _lineWidthLimits = [];

    /// <summary>
    /// How much the space of each line (by line index) is reduced for its line breaking, see <see cref="BalanceLinesNextToFloats"/>:
    /// the lines next to floats have different inline sizes, so the lines are balanced by reducing them by the same amount.
    /// </summary>
    private readonly List<double> _lineWidthReductions = [];

    /// <summary>Whether the content of the line that <see cref="LayoutLine"/> returned fits in the space used by its line breaking.</summary>
    private bool _lineFits;

    /// <summary>
    /// Computes the inline sizes used to break the lines for <c>text-wrap-style</c> (https://drafts.csswg.org/css-text-4/#text-wrap-style),
    /// in <see cref="_lineWidthLimits"/> (the lines are aligned in the whole line box):
    /// <list type="bullet">
    /// <item><c>balance</c>: "line breaks are chosen to balance out the remaining (empty) space in each line box": the narrowest
    /// inline size that keeps the number of lines of the greedy line breaking;</item>
    /// <item><c>pretty</c> and <c>avoid-short-last-line</c>: the last line is not a single word when the line before it can give
    /// it a word without adding a line (the "orphan" avoidance of the browsers);</item>
    /// <item><c>stable</c> and <c>auto</c>: the greedy line breaking.</item>
    /// </list>
    /// The content beside floats and the content with floats is balanced by <see cref="BalanceLinesNextToFloats"/>; with
    /// <c>pretty</c>, it is broken greedily.
    /// </summary>
    private void ComputeWrapStyleLimits(FragmentBuilder builder, in ContentArea content)
    {
        _lineWidthLimits.Clear();
        _lineWidthReductions.Clear();
        var layout = _block.Style.Layout;
        var wrapStyle = layout.Text.TextWrapStyle;
        if (wrapStyle is TextWrapStyle.Auto or TextWrapStyle.Stable || layout.TextWrapMode == TextWrapMode.NoWrap)
            return;

        var size = content.InlineSize;
        if (!double.IsFinite(size) || size <= 0)
            return;

        var hasFloats = !_blockFormattingContext!.Exclusions.IsEmpty;
        foreach (ref readonly var item in Items)
        {
            hasFloats |= item.IsFloat;
        }

        if (hasFloats)
        {
            if (wrapStyle == TextWrapStyle.Balance)
            {
                BalanceLinesNextToFloats(builder, content, size);
            }

            return;
        }

        var ends = new List<LineEnd>();
        var count = BreakAllLines(content, size, ends, out _);
        if (count < 2)
            return;

        if (wrapStyle == TextWrapStyle.Balance)
        {
            // https://drafts.csswg.org/css-overflow-4/#line-clamp: the lines are balanced after the clamping, only the lines before
            // the clamp point (WPT css/css-overflow/line-clamp/line-clamp-balance-*)
            if (_blockFormattingContext.LineClamp is { } lineClamp)
            {
                if (!lineClamp.Balances)
                {
                    lineClamp.SkippedBalance = true;
                    return;
                }

                var visible = lineClamp.LastVisibleLine == int.MaxValue ? int.MaxValue : lineClamp.LastVisibleLine - lineClamp.LineCount + 1;
                if (visible <= 0)
                    return;

                // (the last line of the content may get the ellipsis when the clamp point is after it: WPT
                // css/css-text/white-space/text-wrap-balance-line-clamp-007.html)
                if (visible < count || lineClamp.EllipsisLine == lineClamp.LineCount + count - 1)
                {
                    BalanceClampedLines(content, size, Math.Min(visible, count), lineClamp);
                    return;
                }
            }

            // "Line breaks are chosen to balance out the remaining (empty) space in each line box": the lines between two forced
            // line breaks are balanced together (each group has its own inline size), as Safari (WPT
            // css/css-text/white-space/text-wrap-balance-004.html); a group of more than MaxBalancedLines lines is not balanced
            var balances = false;
            var groupLines = 0;
            for (var i = 0; i < count; i++)
            {
                groupLines++;
                if (ends[i].IsForced || i == count - 1)
                {
                    balances |= groupLines is >= 2 and <= MaxBalancedLines;
                    groupLines = 0;
                }
            }

            if (!balances)
                return;

            // The narrowest inline size whose lines fit and are not more numerous (the number of lines decreases when the size
            // grows). The words are not broken by overflow-wrap to balance the lines: the emergency opportunities are only used
            // when the greedy line breaking needs them
            _ignoresEmergencyBreaks = true;
            _ignoresEmergencyBreaks = BreakAllLines(content, size, ends: null, out var fitsWithoutEmergency) == count && fitsWithoutEmergency;
            var groupStart = 0;
            for (var i = 0; i < count; i++)
            {
                if (!ends[i].IsForced && i < count - 1)
                    continue;

                // The lines of the groups before this one have their inline size, the lines after it are broken greedily
                var lines = i - groupStart + 1;
                var high = size;
                if (lines is >= 2 and <= MaxBalancedLines)
                {
                    var low = 0d;
                    while (high - low > 0.25)
                    {
                        var middle = (low + high) / 2;
                        SetGroupLimit(groupStart, lines, middle);
                        if (BreakAllLines(content, size, ends: null, out var fits) <= count && fits)
                        {
                            high = middle;
                        }
                        else
                        {
                            low = middle;
                        }
                    }
                }

                SetGroupLimit(groupStart, lines, high);
                groupStart = i + 1;
            }

            _ignoresEmergencyBreaks = false;
            return;

            void SetGroupLimit(int start, int lines, double limit)
            {
                _lineWidthLimits.RemoveRange(start, _lineWidthLimits.Count - start);
                for (var line = 0; line < lines; line++)
                {
                    _lineWidthLimits.Add(limit);
                }
            }
        }

        // pretty: the last line is a single word (no soft wrap opportunity on it), and the line before it ends with a soft wrap
        // opportunity: its last word moves to the last line, unless that adds a line
        var last = ends[^1];
        var previous = ends[^2];
        if (previous.IsForced || HasSoftWrapOpportunity(previous.Position, _length))
            return;

        for (var i = 0; i < count - 2; i++)
        {
            _lineWidthLimits.Add(double.PositiveInfinity);
        }

        var indent = GetTextIndent(content, isFirstLine: count == 2, afterForcedBreak: count > 2 && ends[^3].IsForced);
        // (narrower than the line before the last one by more than the tolerance of the line breaking)
        _lineWidthLimits.Add(previous.Width + indent - FitTolerance - 0.01);
        if (BreakAllLines(content, size, ends: null, out _) != count || last.Width <= 0)
        {
            _lineWidthLimits.Clear();
        }
    }

    /// <summary>
    /// Balances the lines of a block container that has floats or is next to floats (<c>text-wrap-style: balance</c>): the lines
    /// have different inline sizes (the floats shorten some of them), so "the remaining (empty) space in each line box" is
    /// balanced by reducing the space of each line by the same amount for its line breaking: the largest amount that keeps the
    /// number of lines of the greedy line breaking (as Gecko, which applies an inset to each line; WPT
    /// css/css-text/white-space/text-wrap-balance-float-005.html). The lines are laid out with their floats for each amount
    /// that is tried (the floats are placed in the space that the line box really has, and the content before a float placed on
    /// a line stays on that line), then the state of the block formatting context is restored.
    /// </summary>
    /// <remarks>
    /// The lines between two forced line breaks are balanced together, as without floats. The content is broken greedily when
    /// it has ruby annotations or an initial letter, in a line-clamp container, and when its first or last line is trimmed by
    /// <c>text-box-trim</c> (the block offsets of the lines, which the floats they are next to depend on, would need them).
    /// </remarks>
    private void BalanceLinesNextToFloats(FragmentBuilder builder, in ContentArea content, double size)
    {
        if (HasRuby || _blockFormattingContext!.LineClamp is not null || IsTextBoxTrimmed(_block, start: true) || IsTextBoxTrimmed(_block, start: false))
            return;

        foreach (ref readonly var item in Items)
        {
            if (item.Box is ContainerBox { InitialLetter: not null })
                return;
        }

        var ends = new List<LineEnd>();
        var count = LayoutAllLinesNextToFloats(builder, content, ends, out _);
        if (count < 2)
            return;

        var balances = false;
        var groupLines = 0;
        for (var i = 0; i < count; i++)
        {
            groupLines++;
            if (ends[i].IsForced || i == count - 1)
            {
                balances |= groupLines is >= 2 and <= MaxBalancedLines;
                groupLines = 0;
            }
        }

        if (!balances)
            return;

        // (the words are not broken by overflow-wrap to balance the lines, see ComputeWrapStyleLimits)
        _ignoresEmergencyBreaks = true;
        _ignoresEmergencyBreaks = LayoutAllLinesNextToFloats(builder, content, ends: null, out var fitsWithoutEmergency) == count && fitsWithoutEmergency;
        var groupStart = 0;
        for (var i = 0; i < count; i++)
        {
            if (!ends[i].IsForced && i < count - 1)
                continue;

            // The lines of the groups before this one have their reduction, the lines after it are broken greedily
            var lines = i - groupStart + 1;
            var low = 0d;
            if (lines is >= 2 and <= MaxBalancedLines)
            {
                var high = size;
                while (high - low > 0.25)
                {
                    var middle = (low + high) / 2;
                    SetGroupReduction(groupStart, lines, middle);
                    if (LayoutAllLinesNextToFloats(builder, content, ends: null, out var fits) <= count && fits)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }
            }

            SetGroupReduction(groupStart, lines, low);
            groupStart = i + 1;
        }

        _ignoresEmergencyBreaks = false;

        void SetGroupReduction(int start, int lines, double reduction)
        {
            _lineWidthReductions.RemoveRange(start, _lineWidthReductions.Count - start);
            for (var line = 0; line < lines; line++)
            {
                _lineWidthReductions.Add(reduction);
            }
        }
    }

    /// <summary>
    /// Lays out all the lines of the content next to the floats of the block formatting context, placing the floats of the
    /// content (see <see cref="LayoutLine"/>; the space of the lines is reduced by <see cref="_lineWidthReductions"/>), and
    /// returns their number; the lines and the floats are not kept: the state of the block formatting context and of the
    /// floats is restored. <paramref name="fits"/> tells whether the content of each line fits in the space used to break it.
    /// </summary>
    private int LayoutAllLinesNextToFloats(FragmentBuilder builder, in ContentArea content, List<LineEnd>? ends, out bool fits)
    {
        ends?.Clear();
        fits = true;
        var blockFormattingContext = _blockFormattingContext!;
        var snapshot = blockFormattingContext.Save();
        var scratch = new FragmentBuilder(builder.WritingDirection);

        // The placement of a float adds the relative offsets of its inline ancestors to its fragment
        List<(BoxFragment Fragment, PhysicalOffset RelativeOffset)>? floats = null;
        foreach (ref readonly var item in Items)
        {
            if (item.IsFloat && item.Fragment is { } floatFragment)
            {
                (floats ??= []).Add((floatFragment, floatFragment.RelativeOffset));
            }
        }

        var count = 0;
        var position = new LinePosition(0, 0);
        var blockOffset = 0d;
        var afterForcedBreak = false;
        var items = _items.Count;
        while (position.Item < items)
        {
            var isFirstLine = count == 0;
            var indent = GetTextIndent(content, isFirstLine, afterForcedBreak);
            _lineAfterForcedBreak = afterForcedBreak;
            _lineAllowsHyphens = true;
            var (end, line, _, lineOffset) = LayoutLine(scratch, position, blockOffset, indent, isFirstLine, count);
            fits &= _lineFits;
            ends?.Add(end);
            if (!line.IsEmpty)
            {
                blockOffset = lineOffset + line.BlockSize;
            }

            // The floats that did not fit on the line are placed below it
            foreach (var index in _deferredFloats)
            {
                PlaceFloat(scratch, index, blockOffset);
            }

            _deferredFloats.Clear();
            if (end.IsForced && GetClearOffsetAfterForcedBreak(end) is { } clearOffset && clearOffset > blockOffset)
            {
                blockOffset = clearOffset;
            }

            position = new LinePosition(end.Item, end.Position);
            afterForcedBreak = end.IsForced;
            count++;
        }

        blockFormattingContext.Restore(snapshot);
        _outOfFlowPositions.Clear();
        foreach (ref var item in Items)
        {
            if (item.IsFloat)
            {
                item.Flags &= ~InlineItemFlags.FloatPlaced;
            }
        }

        if (floats is not null)
        {
            foreach (var (fragment, relativeOffset) in floats)
            {
                fragment.RelativeOffset = relativeOffset;
            }
        }

        return count;
    }

    /// <summary>
    /// Balances the lines before the clamp point of a line-clamp container (the first <paramref name="lines"/> lines of the content,
    /// the last one with the block overflow ellipsis when it gets it): the narrowest inline size whose lines contain the content of
    /// the clamped greedy lines.
    /// </summary>
    private void BalanceClampedLines(in ContentArea content, double size, int lines, LineClamp lineClamp)
    {
        if (lines > MaxBalancedLines)
            return;

        var ellipsis = lineClamp.EllipsisLine == lineClamp.LineCount + lines - 1 ? CreateBlockEllipsis(lineClamp.Ellipsis) : null;
        _blockEllipsisIsLegacy = lineClamp.IsLegacy;
        var target = BreakClampedLines(content, size, lines, ellipsis, out _);
        var low = 0d;
        var high = size;
        while (high - low > 0.25)
        {
            var middle = (low + high) / 2;
            var end = BreakClampedLines(content, middle, lines, ellipsis, out var fits);
            if (fits && (end.Item > target.Item || (end.Item == target.Item && end.Position >= target.Position)))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        for (var i = 0; i < lines; i++)
        {
            _lineWidthLimits.Add(high);
        }
    }

    /// <summary>Breaks the first lines of the content in line boxes of an inline size, the last one with an ellipsis (see <see cref="BreakAllLines"/>), and returns their end.</summary>
    private LinePosition BreakClampedLines(in ContentArea content, double size, int lines, LineEllipsis? ellipsis, out bool fits)
    {
        fits = true;
        var position = new LinePosition(0, 0);
        var afterForcedBreak = false;
        for (var count = 0; count < lines && position.Item < _items.Count; count++)
        {
            var isFirstLine = count == 0;
            var indent = GetTextIndent(content, isFirstLine, afterForcedBreak);
            _lineStartOffset = indent;
            _lineAfterForcedBreak = afterForcedBreak;
            _lineAllowsHyphens = true;
            var lineSize = size - indent;
            if (count == lines - 1 && ellipsis is not null)
            {
                _blockEllipsis = ellipsis;
                lineSize -= GetBlockEllipsisReservedSize(ellipsis);
            }

            var end = BreakLine(position, lineSize, isFirstLine, IntrinsicSizeMode.None);
            _blockEllipsis = null;
            fits &= end.Width <= lineSize + FitTolerance;
            position = new LinePosition(end.Item, end.Position);
            afterForcedBreak = end.IsForced;
        }

        return position;
    }

    /// <summary>
    /// Breaks all the lines of the content (without building them) in line boxes of an inline size (limited by <see cref="_lineWidthLimits"/>),
    /// and returns their number; <paramref name="fits"/> tells whether the content of each line fits.
    /// </summary>
    private int BreakAllLines(in ContentArea content, double size, List<LineEnd>? ends, out bool fits)
    {
        ends?.Clear();
        fits = true;
        var count = 0;
        var position = new LinePosition(0, 0);
        var afterForcedBreak = false;
        while (position.Item < _items.Count)
        {
            var isFirstLine = count == 0;
            var indent = GetTextIndent(content, isFirstLine, afterForcedBreak);
            var lineSize = count < _lineWidthLimits.Count ? Math.Min(size, _lineWidthLimits[count]) : size;
            _lineStartOffset = indent;
            _lineAfterForcedBreak = afterForcedBreak;
            _lineAllowsHyphens = true;
            var end = BreakLine(position, lineSize - indent, isFirstLine, IntrinsicSizeMode.None);
            if (end.StopsAtFloat)
                return int.MaxValue;

            fits &= end.Width <= lineSize - indent + FitTolerance;
            ends?.Add(end);
            position = new LinePosition(end.Item, end.Position);
            afterForcedBreak = end.IsForced;
            count++;
        }

        return count;
    }

    /// <summary>Whether the text from <paramref name="start"/> to <paramref name="end"/> has a soft wrap opportunity (after its first character).</summary>
    private bool HasSoftWrapOpportunity(int start, int end)
    {
        if (!_opportunitiesComputed)
        {
            ComputeBreakOpportunities();
        }

        // The collapsible spaces at the start of the line are not on it
        var text = _text.AsSpan(0, _length);
        while (start < end && text[start] == ' ')
        {
            start++;
        }

        // The spaces at the end of the content are removed or hang
        while (end > start && IsSpace(text[end - 1]))
        {
            end--;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (_opportunities[i] is LineBreakOpportunity.Allowed or LineBreakOpportunity.Phrase or LineBreakOpportunity.Hyphen or LineBreakOpportunity.AutoHyphen)
                return true;
        }

        return false;
    }

    /// <summary>Whether the line after a line that ends at <paramref name="end"/> is the last line of the content (<c>hyphenate-limit-last: always</c>).</summary>
    private bool IsBeforeLastLine(in LineEnd end, double availableSize)
    {
        var next = BreakLine(new LinePosition(end.Item, end.Position), availableSize, isFirstLine: false, IntrinsicSizeMode.None);
        return !next.StopsAtFloat && next.Item >= _items.Count;
    }

    /// <summary>
    /// Whether the first (last) formatted line of a block container is trimmed by <c>text-box-trim</c>
    /// (https://drafts.csswg.org/css-inline-3/#text-box-trim): the block container, or an ancestor block container whose first
    /// (last) formatted line is in it, "trim the block-start side of the first formatted line [...] If there is no such line, or
    /// if there is intervening non-zero padding or borders, there is no effect". As <c>::first-line</c>, it does not propagate
    /// through the flex, grid and table formatting contexts.
    /// </summary>
    internal static bool IsTextBoxTrimmed(ContainerBox box, bool start)
    {
        var flag = start ? TextBoxTrim.TrimStart : TextBoxTrim.TrimEnd;
        for (var current = box; ;)
        {
            if ((current.Style.Layout.Text.TextBoxTrim & flag) != TextBoxTrim.None)
                return true;

            // text-box-trim is not inherited: most block containers have no ancestor that trims, the edges are checked after it
            if (current.Kind == ContainerBoxKind.Viewport || current.IsOutOfFlow || current.Parent is not { IsBlockContainer: true, ChildrenAreInline: false } parent
                || !HasTrimmingAncestor(parent, flag) || HasBlockEdge(current, start) || !ReferenceEquals(GetEdgeInFlowChild(parent, start), current))
            {
                return false;
            }

            current = parent;
        }

        static bool HasTrimmingAncestor(ContainerBox box, TextBoxTrim flag)
        {
            for (var current = box; current is { IsBlockContainer: true }; current = current.Parent)
            {
                if ((current.Style.Layout.Text.TextBoxTrim & flag) != TextBoxTrim.None)
                    return true;

                if (current.Kind == ContainerBoxKind.Viewport || current.IsOutOfFlow)
                    return false;
            }

            return false;
        }

        // Whether the box has a border or a padding on its block-start (block-end) side
        static bool HasBlockEdge(ContainerBox box, bool start) => InlineLayoutAlgorithm.HasBlockEdge(box, start);

        // The first (last) in-flow child of a box
        static ContainerBox? GetEdgeInFlowChild(ContainerBox parent, bool first)
        {
            for (var child = first ? parent.FirstChild : parent.LastChild; child is not null; child = first ? child.NextSibling : child.PreviousSibling)
            {
                // An anonymous block box without formatted line (e.g. the empty part of an inline box split around a block)
                // is not the box of the first (last) formatted line
                if (child is ContainerBox { IsOutsideMarker: false, IsOutOfFlow: false } container && !(container.Kind == ContainerBoxKind.Anonymous && container.ChildrenAreInline && !HasFormattedContent(container)))
                    return container;
            }

            return null;
        }

        // Whether the inline content of a box can make a formatted line: text other than collapsible white space, an atomic
        // inline, or an inline box with a margin, a border or a padding
        static bool HasFormattedContent(ContainerBox box)
        {
            for (var child = box.FirstChild; child is not null; child = child.NextSibling)
            {
                switch (child)
                {
                    case TextRun run:
                        if (run.Kind != TextRunKind.Text || run.Style.Layout.WhiteSpaceCollapse is not (WhiteSpaceCollapse.Collapse or WhiteSpaceCollapse.Discard) || run.Text.ContainsAnyExcept(' ', '\t', '\n'))
                            return true;

                        break;

                    case ContainerBox { IsOutOfFlow: true }:
                        break;

                    case ContainerBox { IsInlineBox: true } inline:
                        if (HasInlineEdge(inline) || HasFormattedContent(inline))
                            return true;

                        break;

                    default:
                        return true;
                }
            }

            return false;
        }

        static bool HasInlineEdge(ContainerBox box)
        {
            var layout = box.Style.Layout;
            if (!layout.MayHaveBoxEdges)
                return false;

            var boxModel = layout.Box;
            return boxModel.BorderWidth.Left != 0 || boxModel.BorderWidth.Right != 0 || boxModel.BorderWidth.Top != 0 || boxModel.BorderWidth.Bottom != 0
                || !boxModel.Padding.Left.IsZero || !boxModel.Padding.Right.IsZero || !boxModel.Padding.Top.IsZero || !boxModel.Padding.Bottom.IsZero
                || IsNotZero(boxModel.Margin.Left) || IsNotZero(boxModel.Margin.Right) || IsNotZero(boxModel.Margin.Top) || IsNotZero(boxModel.Margin.Bottom);

            static bool IsNotZero(LengthPercentageAuto margin) => !margin.IsAuto && !margin.Value.IsZero;
        }
    }

    /// <summary>Whether a box has a border or a padding on its block-start (block-end) side.</summary>
    private static bool HasBlockEdge(ContainerBox box, bool start)
    {
        var layout = box.Style.Layout;
        if (!layout.MayHaveBoxEdges)
            return false;

        var writingDirection = WritingDirection.FromStyle(layout);
        var side = start ? writingDirection.BlockStart : writingDirection.BlockEnd;
        return layout.Box.BorderWidth.Get(side) != 0 || !layout.Box.Padding.Get(side).IsZero;
    }

    /// <summary>
    /// Whether the last line before the clamp point of a line-clamp container (https://drafts.csswg.org/css-overflow-4/#line-clamp),
    /// which is the last formatted line of the container (the content after it is hidden), is trimmed on its block-end side by
    /// <c>text-box-trim</c>: by the block container or by an ancestor up to the line-clamp container, without block-end border or
    /// padding between them.
    /// </summary>
    private static bool IsTextBoxTrimmedAtClampPoint(ContainerBox box)
    {
        for (var current = box; ;)
        {
            if ((current.Style.Layout.Text.TextBoxTrim & TextBoxTrim.TrimEnd) != TextBoxTrim.None)
                return true;

            if (LineClamp.IsLineClampContainer(current) || current.Kind == ContainerBoxKind.Viewport || current.Parent is not { IsBlockContainer: true } parent || HasBlockEdge(current, start: false))
                return false;

            current = parent;
        }
    }

    /// <summary>
    /// Returns how much the block-start and block-end sides of a line are trimmed by <c>text-box-trim</c>: the line box is cut
    /// "to the specified metric of its root inline box", the <c>text-box-edge</c> of the block container
    /// (https://drafts.csswg.org/css-inline-3/#text-box-edge). The block-start side is the line-over side, except in <c>vertical-lr</c>.
    /// </summary>
    private (double Start, double End) GetTextBoxTrimAmounts(in LineBoxResult line, LineTrim trim, bool isFirstLine)
    {
        if (!trim.Start && !trim.End)
            return default;

        // (the root inline box has the used font size of the line when text-fit scales it)
        _fontScale = line.FontScale;
        var rootStyle = GetLineStyle(_block, isFirstLine);
        _fontScale = 1;
        var (start, end) = GetTextBoxTrimAmounts(GetMetrics(rootStyle), _block.Style.Layout.Text.TextBoxEdge, _writingDirection, line.BlockSize, line.Baseline);
        return (trim.Start ? start : 0, trim.End ? end : 0);
    }

    private static (double Start, double End) GetTextBoxTrimAmounts(in InlineMetrics metrics, TextEdge edge, WritingDirection writingDirection, double blockSize, double baseline)
    {
        var flipped = writingDirection.IsLineOverBlockEnd;
        var startMetric = flipped ? metrics.GetUnderEdge(edge.Under) : metrics.GetOverEdge(edge.Over);
        var endMetric = flipped ? metrics.GetOverEdge(edge.Over) : metrics.GetUnderEdge(edge.Under);
        return (baseline - startMetric, blockSize - baseline - endMetric);
    }

    /// <summary>
    /// Returns how much the block-start and block-end sides of a line box of a strip are trimmed by <c>text-box-trim</c> when the
    /// line is the first or the last formatted line of a fragmentainer (see <see cref="GetTextBoxTrimAmounts(in LineBoxResult, LineTrim, bool)"/>;
    /// <see cref="FragmentationFlow"/>): the line box is cut to the <c>text-box-edge</c> metric of its root inline box. The
    /// amounts are not negative (a line box that ends before the metric is not extended at a break).
    /// </summary>
    /// <param name="line">The line box.</param>
    /// <param name="blockSize">The block size of the line box.</param>
    /// <param name="writingDirection">The writing mode of the block container of the line.</param>
    internal static (double Start, double End) GetFragmentainerTextBoxTrim(LineBoxFragment line, double blockSize, WritingDirection writingDirection)
    {
        if (line.Style is not { } style)
            return default;

        var (start, end) = GetTextBoxTrimAmounts(CreateMetrics(style), style.Layout.Text.TextBoxEdge, writingDirection, blockSize, line.Baseline);
        return (Math.Max(0, start), Math.Max(0, end));
    }
}
