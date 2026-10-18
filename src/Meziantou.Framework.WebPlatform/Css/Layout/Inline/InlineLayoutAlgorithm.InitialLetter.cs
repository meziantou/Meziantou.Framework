namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The initial letters (https://drafts.csswg.org/css-inline-3/#initial-letter-styling): the initial letter box of the
// ::first-letter pseudo-element (see InitialLetter) is an item of the line it starts, placed at the inline-start edge of the
// line; its margin box is an exclusion of the block formatting context (ExclusionSpace.AddInitialLetter), so its line and the
// next ones (also the ones of the next blocks) are shortened as they are next to a float.
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>The item of the initial letter box placed on the line being laid out (its block position depends on the baseline of the line), or -1.</summary>
    private int _initialLetterItem = -1;

    /// <summary>The exclusion of <see cref="_initialLetterItem"/> in the block formatting context.</summary>
    private int _initialLetterExclusion;

    /// <summary>The line-left edge of the margin box of <see cref="_initialLetterItem"/> (with the indentation of the line) in the block formatting context.</summary>
    private double _initialLetterLineLeft;

    /// <summary>The indentation of the line of <see cref="_initialLetterItem"/>, which precedes the initial letter.</summary>
    private double _initialLetterIndent;

    /// <summary>The baseline of the line assumed by the position of <see cref="_initialLetterItem"/>.</summary>
    private double _initialLetterBaseline;

    /// <summary>The distance the line of <see cref="_initialLetterItem"/> was moved down by for the lines of the initial letter that are not sunk.</summary>
    private double _initialLetterRaise;

    /// <summary>Whether the block-start side of the line being laid out is trimmed (https://drafts.csswg.org/css-inline-3/#text-box-trim): it is the first formatted line.</summary>
    private bool _lineIsTrimmedAtStart;

    /// <summary>
    /// Lays out an initial letter box in the content box of the block container, as a float is (see <see cref="LayoutAtomicOrFloat"/>):
    /// its inline size is its shrink-to-fit size, and its automatic block size fits its sink and the outlines of its glyphs
    /// (<see cref="InitialLetter.GetContentBlockSize"/>).
    /// </summary>
    private (BoxFragment? Fragment, double MinContentInlineSize, double InlineSize) LayoutInitialLetter(ContainerBox box)
    {
        var result = LayoutAtomicOrFloat(box);
        if (result.Fragment is not { } fragment || !box.Style.Layout.Box.GetSize(!_writingDirection.IsHorizontal).IsAuto)
            return result;

        if (InitialLetter.GetContentBlockSize(fragment) is not { } contentBlockSize)
            return result;

        var edges = _writingDirection.ToLogical(fragment.Border.Add(fragment.Padding));
        var blockSize = contentBlockSize + edges.BlockSum();
        if (Math.Abs(blockSize - _writingDirection.ToLogical(fragment.Size).Block) < LayoutEpsilon)
            return result;

        fragment = _context.LayoutBox(box, _childSpace with { FixedBlockSize = blockSize });
        return (fragment, result.MinContentInlineSize, result.InlineSize);
    }

    /// <summary>
    /// Places the initial letter box met at the start of a line (https://drafts.csswg.org/css-inline-3/#initial-letter-position)
    /// and returns the block offset of the line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The initial letter is positioned in the block axis "to satisfy its under alignment, and then shifted by (sink - 1) x
    /// line-height of containing block towards the containing block's block end": its over alignment point (the block-start
    /// edge of its content box) is at the cap height above the baseline of the line, minus the lines of its size that are not
    /// sunk. A raised or sunken initial "can force its originating line box (and subsequent content) to shift further away
    /// from that edge": as in Chrome, the line is moved down by these lines. The line is also below the initial letter of a
    /// previous block ("If the subsequent block starts with an initial letter [...] then it must clear the previous block's
    /// initial letter"). The baseline of the line is the one of its strut until the line is built (see
    /// <see cref="FinishInitialLetter"/>).
    /// </para>
    /// <para>
    /// In the inline axis, the box is at the inline-start edge of the line, after its indentation (<c>text-indent</c>
    /// "cause[s] a shift in the start of the line's contents including the initial letter itself"), next to the floats that
    /// intrude into its block extent ("initial letter boxes must not overlap the margin boxes of any floats"). Its margin box
    /// (https://drafts.csswg.org/css-inline-3/#initial-letter-wrapping: <c>initial-letter-wrap: none</c>) shortens the lines.
    /// </para>
    /// </remarks>
    private double BeginInitialLetter(int index, double blockOffset, double indent)
    {
        ref readonly var item = ref Items[index];
        var letter = ((ContainerBox)item.Box).InitialLetter!;
        var exclusions = _blockFormattingContext!.Exclusions;
        var origin = _content.BlockFormattingContextOffset;
        var side = _writingDirection.IsRightToLeft ? FloatSide.LineRight : FloatSide.LineLeft;
        if (exclusions.GetInitialLetterEnd(side) is { } previousEnd)
        {
            blockOffset = Math.Max(blockOffset, previousEnd - origin.Block);
        }

        // (the space above a line whose block-start side is trimmed by text-box-trim is trimmed too: the initial letter
        // overflows the block container, WPT css/css-inline/text-box-trim/text-box-trim-initial-letter-start-001.html)
        _initialLetterRaise = _lineIsTrimmedAtStart ? 0 : letter.Raise;
        blockOffset += _initialLetterRaise;

        // The baseline of the strut of the line, from the block-start edge of the line box
        var strut = GetMetrics(_block.Style);
        var baseline = ((strut.LineHeight - (strut.Ascent + strut.Descent)) / 2) + (_writingDirection.IsLineOverBlockEnd ? strut.Descent : strut.Ascent);
        var (blockStart, blockEnd, _) = GetInitialLetterBlockExtent(item, blockOffset, baseline);
        var margin = item.Fragment!.Margin;
        var inlineSize = indent + margin.Get(_writingDirection.LineLeft) + _writingDirection.ToLogical(item.Fragment.Size).Inline + margin.Get(_writingDirection.LineRight);
        var opportunity = exclusions.GetOpportunity(blockStart, blockEnd - blockStart, origin.Inline, _content.InlineSize, useFloatAreas: false);
        var lineLeft = side == FloatSide.LineLeft ? opportunity.LineLeft : opportunity.LineRight - inlineSize;
        _initialLetterItem = index;
        _initialLetterLineLeft = lineLeft;
        _initialLetterIndent = indent;
        _initialLetterBaseline = baseline;
        _initialLetterExclusion = exclusions.AddInitialLetter(side, new LogicalRect(new LogicalOffset(lineLeft, blockStart), new LogicalSize(inlineSize, blockEnd - blockStart)));
        return blockOffset;
    }

    /// <summary>
    /// Returns the block extent of the margin box of an initial letter box in the block formatting context, for a line at
    /// <paramref name="lineBlockOffset"/> whose baseline is at <paramref name="baseline"/> from its block-start edge, and the
    /// block-start edge of its border box. The exclusion starts at the block-start edge of the line at the latest: the initial
    /// letter is content of the line. As in Chrome, the block-start margin moves the box down.
    /// </summary>
    private (double BlockStart, double BlockEnd, double BorderBoxBlockStart) GetInitialLetterBlockExtent(in InlineItem item, double lineBlockOffset, double baseline)
    {
        var letter = ((ContainerBox)item.Box).InitialLetter!;
        var fragment = item.Fragment!;
        var margin = _writingDirection.ToLogical(fragment.Margin);
        var edges = _writingDirection.ToLogical(fragment.Border.Add(fragment.Padding));
        var lineStart = _content.BlockFormattingContextOffset.Block + lineBlockOffset;
        var contentStart = lineStart + baseline - letter.CapHeight - ((letter.Size - letter.Sink) * letter.LineHeight) + margin.BlockStart;
        var borderBoxStart = contentStart - edges.BlockStart;
        var blockStart = Math.Min(lineStart, borderBoxStart - margin.BlockStart);
        var blockEnd = Math.Max(blockStart, borderBoxStart + _writingDirection.ToLogical(fragment.Size).Block + margin.BlockEnd);
        return (blockStart, blockEnd, borderBoxStart);
    }

    /// <summary>
    /// Adds the fragment of the initial letter box of the line that was built to the fragment of the block container, at its
    /// position for the baseline of the line (the content of the line may move it from the baseline of the strut, e.g. a ruby
    /// annotation), and moves its exclusion there. Returns the block offset of the line.
    /// </summary>
    private double FinishInitialLetter(FragmentBuilder builder, in LineBoxResult line, double blockOffset)
    {
        ref readonly var item = ref Items[_initialLetterItem];
        _initialLetterItem = -1;
        var fragment = item.Fragment!;
        var baseline = line.IsEmpty ? _initialLetterBaseline : line.Baseline;

        // The line of a raised or sunken initial was moved down for the baseline of its strut: the content that moves the
        // baseline of the line away from its block-start edge (e.g. a ruby annotation) uses that space first (as in Chrome)
        var raise = _initialLetterRaise;
        if (raise > 0 && baseline > _initialLetterBaseline)
        {
            blockOffset -= Math.Min(raise, baseline - _initialLetterBaseline);
        }
        var (blockStart, blockEnd, borderBoxStart) = GetInitialLetterBlockExtent(item, blockOffset, baseline);
        var exclusions = _blockFormattingContext!.Exclusions;
        var exclusion = exclusions.Exclusions[_initialLetterExclusion];
        var marginBox = new LogicalRect(new LogicalOffset(_initialLetterLineLeft, blockStart), new LogicalSize(exclusion.MarginBox.Size.Inline, blockEnd - blockStart));
        if (marginBox != exclusion.MarginBox)
        {
            exclusions.MoveInitialLetter(_initialLetterExclusion, marginBox);
        }

        // The border box in the content box of the block container (from its inline-start edge), as for a float
        var origin = _content.BlockFormattingContextOffset;
        var size = _writingDirection.ToLogical(fragment.Size);
        var borderBoxLineLeft = _writingDirection.IsRightToLeft
            ? marginBox.InlineEnd - _initialLetterIndent - fragment.Margin.Get(_writingDirection.LineRight) - size.Inline
            : marginBox.InlineStart + _initialLetterIndent + fragment.Margin.Get(_writingDirection.LineLeft);
        var inlineStart = _writingDirection.IsRightToLeft ? _content.InlineSize - (borderBoxLineLeft - origin.Inline) - size.Inline : borderBoxLineLeft - origin.Inline;
        fragment.IsHiddenForPaint = _hidesLines;
        fragment.RelativeOffset += GetInlineAncestorsRelativeOffset(item.Container);
        builder.AddChild(fragment, new LogicalOffset(_content.Offset.Inline + inlineStart, _content.Offset.Block + borderBoxStart - origin.Block));
        return blockOffset;
    }
}
