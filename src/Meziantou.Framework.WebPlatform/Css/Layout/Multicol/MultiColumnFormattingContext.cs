using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The content of a multi-column container being laid out in a single strip (see <see cref="MultiColumnFormattingContext"/>),
/// shared by the boxes of its block formatting context (<see cref="BlockFormattingContextState.MultiColumnFlow"/>): the column
/// spanners lay out in the content box of the container.
/// </summary>
internal sealed class MultiColumnFlow
{
    public MultiColumnFlow(ContainerBox container, WritingDirection writingDirection, double inlineSize, double? blockSize)
    {
        Container = container;
        WritingDirection = writingDirection;
        InlineSize = inlineSize;
        BlockSize = blockSize;
    }

    /// <summary>The multi-column container.</summary>
    public ContainerBox Container { get; }

    /// <summary>The writing mode of the multi-column container.</summary>
    public WritingDirection WritingDirection { get; }

    /// <summary>The inline size of the content box of the multi-column container: the containing block of the column spanners.</summary>
    public double InlineSize { get; }

    /// <summary>The block size of the content box of the multi-column container when it is definite, else null.</summary>
    public double? BlockSize { get; }

    /// <summary>The column spanners laid out in the strip (https://drafts.csswg.org/css-multicol-1/#column-span).</summary>
    public HashSet<ContainerBox> SpannerBoxes { get; } = [];

    /// <summary>The margin boxes of the column spanners laid out in the strip, in the order of their layout: their extent in the block formatting context of the strip.</summary>
    private readonly List<(ContainerBox Box, double Start, double End, int Sequence)> _spannerRanges = [];
    private Dictionary<ContainerBox, double>? _spannerOverflows;
    private int _sequence;

    /// <summary>The block-end edge of the contents of the boxes of the strip that are shorter than their contents, in the block formatting context of the strip (the last layout of a box wins).</summary>
    private Dictionary<ContainerBox, double>? _overflowEnds;

    /// <summary>Records the block-end edge of the contents of a box of the strip that overflow it.</summary>
    public void SetOverflowEnd(ContainerBox box, double? end)
    {
        if (end is { } value)
        {
            (_overflowEnds ??= [])[box] = value;
        }
        else
        {
            _overflowEnds?.Remove(box);
        }
    }

    /// <summary>
    /// Returns the block-end edge of the content of the strip that overflows the boxes laid out so far, or null: a column spanner
    /// ends the row of columns of the content before it, so it is placed after that content in the strip.
    /// </summary>
    public double? GetOverflowEnd()
    {
        if (_overflowEnds is not { Count: > 0 } ends)
            return null;

        var result = double.NegativeInfinity;
        foreach (var end in ends.Values)
        {
            result = Math.Max(result, end);
        }

        return result;
    }

    /// <summary>The number of column spanners laid out so far (see <see cref="ExtendBlockSize"/>).</summary>
    public int SpannerSequence => _sequence;

    /// <summary>Records the extent of the margin box of a column spanner in the block formatting context of the strip (the last layout of the spanner wins).</summary>
    public void AddSpannerRange(ContainerBox box, double start, double end)
    {
        for (var i = _spannerRanges.Count - 1; i >= 0; i--)
        {
            if (_spannerRanges[i].Box == box)
            {
                _spannerRanges.RemoveAt(i);
            }
        }

        _spannerRanges.Add((box, start, end, _sequence++));
    }

    /// <summary>
    /// Returns the block size of the content box of a box of the strip whose block size does not follow its contents, with the
    /// column spanners of its contents: a spanner interrupts the box, which continues after it with the block size that remains
    /// (the block size of a box is the sum of the block sizes of its fragments in the columns, as in the browsers). When the box
    /// ends before a spanner, the content after the box is still after the spanner: the distance from the end of the box to
    /// the end of the spanner is recorded (<see cref="GetSpannerOverflow"/>).
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="sequence">The value of <see cref="SpannerSequence"/> before the contents of the box were laid out.</param>
    /// <param name="contentStart">The block-start edge of the content box in the block formatting context of the strip.</param>
    /// <param name="contentBlockSize">The block size of the content box.</param>
    /// <param name="blockEndEdges">The block-end padding and border of the box.</param>
    public double ExtendBlockSize(ContainerBox box, int sequence, double contentStart, double contentBlockSize, double blockEndEdges)
    {
        _spannerOverflows?.Remove(box);
        if (sequence == _sequence)
            return contentBlockSize;

        var end = contentStart + contentBlockSize;
        var overflow = 0d;
        foreach (var range in _spannerRanges)
        {
            if (range.Sequence < sequence)
                continue;

            if (range.Start < end - 0.01)
            {
                end += range.End - range.Start;
                overflow = 0;
            }
            else
            {
                overflow = Math.Max(overflow, range.End - end - blockEndEdges);
            }
        }

        if (overflow > 0)
        {
            (_spannerOverflows ??= [])[box] = overflow;
        }

        return end - contentStart;
    }

    /// <summary>Returns the distance from the block-end edge of a box of the strip to the end of the last column spanner of its contents when the box ends before it, else 0.</summary>
    public double GetSpannerOverflow(ContainerBox box) => _spannerOverflows is { } overflows && overflows.TryGetValue(box, out var overflow) ? overflow : 0;

    /// <summary>
    /// Returns whether a box laid out in the block formatting context of the multi-column container is a column spanner
    /// (https://drafts.csswg.org/css-multicol-1/#column-span): an in-flow block-level box with <c>column-span: all</c> (the caller
    /// checks that it participates in the block formatting context of the container, "the nearest multicol ancestor in the same
    /// block formatting context").
    /// </summary>
    public static bool IsColumnSpanner(ContainerBox box)
        => box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.TableWrapper or ContainerBoxKind.Replaced && box.IsBlockLevel && !box.IsOutOfFlow
            && box.Style.GetComputedValue("column-span") == "all";

    /// <summary>
    /// Returns the multi-column container of a column spanner (https://drafts.csswg.org/css-multicol-1/#column-span: "the nearest
    /// multicol ancestor in the same block formatting context"), or null when the box is not a spanner: a box with
    /// <c>column-span: all</c> whose ancestors up to the container participate in its block formatting context (and do not
    /// establish a containing block for the fixed positioned boxes, see <see cref="IsValidColumnSpanner"/>).
    /// </summary>
    public static ContainerBox? GetColumnSpannerContainer(ContainerBox box)
    {
        if (!IsColumnSpanner(box))
            return null;

        for (var ancestor = box.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Style.Layout.IsMultiColumnContainer)
                return ancestor;

            if (ancestor.Style.Layout.EstablishesFixedPositionContainingBlock || ancestor.Parent is not { } parent
                || BlockFormattingContext.EstablishesNewFormattingContext(ancestor, WritingDirection.FromStyle(parent.Style.Layout)))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether a box laid out in the block formatting context of the multi-column container is a column spanner of the
    /// container (see <see cref="IsColumnSpanner"/>). As in Blink, a box is not a spanner when one of its ancestors in the
    /// container establishes a containing block for the fixed positioned boxes (a transform, a filter...): the spanner would be
    /// laid out outside of the box that contains it.
    /// </summary>
    public bool IsValidColumnSpanner(ContainerBox box)
    {
        if (!IsColumnSpanner(box))
            return false;

        for (var ancestor = box.Parent; ancestor is not null && ancestor != Container; ancestor = ancestor.Parent)
        {
            if (ancestor.Style.Layout.EstablishesFixedPositionContainingBlock)
                return false;
        }

        return true;
    }
}

/// <summary>
/// The layout of a multi-column container (https://drafts.csswg.org/css-multicol-1/): its content is laid out in a single strip of
/// the column width, then fragmented into column boxes (https://drafts.csswg.org/css-break-3/), balanced or filled sequentially,
/// with the column spanners between the rows of columns.
/// </summary>
/// <remarks>
/// <para>
/// The column count and width come from the pseudo-algorithm of https://drafts.csswg.org/css-multicol-1/#pseudo-algorithm. The
/// content is laid out once by the block or inline layout of the container (the column boxes establish an independent block
/// formatting context), in a strip of the column width: the column spanners (<c>column-span: all</c>) are laid out in the inline
/// size of the container where they are met, and divide the strip into multicol rows. The possible breaks of the strip are
/// collected (<see cref="FragmentationFlow"/>), the breaker chooses the breaks of each row (<see cref="ColumnBreaker"/>):
/// balanced columns (<c>column-fill: balance</c>, and the rows before a spanner) get the smallest block size for which the
/// content fits in the column count, the last row of a container whose block size is definite or limited and
/// <c>column-fill: auto</c> fills its columns sequentially, <c>column-height</c> (https://drafts.csswg.org/css-multicol-2/#ch)
/// fixes the block size of the rows, which are then at fixed positions (the spanners are placed in them). The content that does
/// not fit creates overflow columns in the inline direction, or new rows with <c>column-wrap: wrap</c>. Then the fragments of the
/// strip are distributed to the column boxes (<see cref="FragmentSlicer"/>): the boxes that span several columns get one fragment
/// per column. There is no fragment for the column boxes: the fragments of the contents are children of the fragment of the
/// container.
/// </para>
/// <para>
/// The layout of the content does not depend on the breaks (no relayout): the boxes after a break are not laid out again in the
/// space that remains (e.g. the percentages of a box broken over several columns, the clearance after a fragmented float). A
/// multi-column container laid out in the strip of another one keeps what the outer one needs to fragment it
/// (<see cref="NestedColumns"/>, https://drafts.csswg.org/css-multicol-1/#nested-multicol): its own layout in the strip is used
/// when it is not broken.
/// </para>
/// </remarks>
internal sealed class MultiColumnFormattingContext : FormattingContext
{
    public static MultiColumnFormattingContext Instance { get; } = new();

    private MultiColumnFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Block;

    /// <summary>
    /// Returns whether a box laid out by the block or inline layout is a multi-column container laid out by this formatting
    /// context: a block container (not a table wrapper, https://drafts.csswg.org/css-multicol-1/#cc: "Applies to: block containers
    /// except table wrapper boxes") whose <c>column-count</c>, <c>column-width</c> or <c>column-height</c> is not <c>auto</c>.
    /// </summary>
    public static bool IsMultiColumnContainer(ContainerBox box) => box.Kind == ContainerBoxKind.Normal && box.Style.Layout.IsMultiColumnContainer;

    private static FormattingContext GetContentFormattingContext(ContainerBox box) => box.ChildrenAreInline ? InlineFormattingContext.Instance : BlockFormattingContext.Instance;

    /// <summary>
    /// Returns the min-content and max-content inline sizes of a multi-column container: the intrinsic sizes of its content (one
    /// column) converted to the container with the column count and the gaps, as in the browsers (the definition of the former
    /// https://www.w3.org/TR/2016/WD-css-sizing-3-20160510/#multicol-intrinsic, https://github.com/w3c/csswg-drafts/issues/1742):
    /// a specified <c>column-width</c> limits the min-content size and is the max-content size of a column, and the min-content
    /// size ignores the column count when <c>column-width</c> is specified. The column spanners are not in a column: the container
    /// is at least as wide as their contributions.
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        // The column spanners are laid out in the content box of the container, not in a column: the container is at least as
        // wide as their contributions (as in the browsers)
        var outerSpannerSizes = context.ColumnSpannerSizes;
        var spannerSizes = new ColumnSpannerIntrinsicSizes();
        context.ColumnSpannerSizes = spannerSizes;
        IntrinsicSizes contentSizes;
        try
        {
            contentSizes = GetContentFormattingContext(box).ComputeIntrinsicInlineSizes(context, box, input);
        }
        finally
        {
            context.ColumnSpannerSizes = outerSpannerSizes;
        }

        var sizes = GetContainerIntrinsicSizes(box.Style.Layout, contentSizes);
        var minContent = Math.Max(sizes.MinContent, spannerSizes.MinContent);
        return new IntrinsicSizes(minContent, Math.Max(minContent, Math.Max(sizes.MaxContent, spannerSizes.MaxContent)));
    }

    /// <summary>
    /// Returns the min-content and max-content inline sizes of a multi-column container with size containment and no
    /// <c>contain-intrinsic-size</c>: the sizes of the columns of an empty container (the column widths and the gaps, as in the browsers).
    /// </summary>
    protected internal override IntrinsicSizes ComputeEmptyIntrinsicInlineSizes(LayoutContext context, ContainerBox box, double? contentBlockSize)
        => GetContainerIntrinsicSizes(box.Style.Layout, default);

    /// <summary>Converts the intrinsic sizes of the content of a multi-column container (one column) to the intrinsic sizes of the container (see <see cref="ComputeIntrinsicInlineSizes"/>).</summary>
    private static IntrinsicSizes GetContainerIntrinsicSizes(LayoutStyle layout, IntrinsicSizes content)
    {
        var fragmentation = layout.Fragmentation;
        var minContent = content.MinContent;
        var maxContent = content.MaxContent;
        var columnCount = Math.Max(1, fragmentation.ColumnCount ?? 1);
        var gaps = GetColumnGap(layout, 0) * (columnCount - 1);
        if (fragmentation.ColumnWidth.TryResolve(0) is { } columnWidth)
        {
            // (https://drafts.csswg.org/css-sizing-3/#multicol-intrinsic: with a column count, the max-content size of a column is
            // "the larger of its column-width and the largest min-content inline-size contribution of its contents")
            maxContent = Math.Max(columnWidth, fragmentation.ColumnCount is null ? Math.Min(minContent, columnWidth) : minContent);
            minContent = Math.Min(minContent, columnWidth);
        }
        else
        {
            minContent = (minContent * columnCount) + gaps;
        }

        maxContent = (maxContent * columnCount) + gaps;
        return new IntrinsicSizes(minContent, Math.Max(minContent, maxContent));
    }

    /// <summary>Returns the used column gap (https://drafts.csswg.org/css-multicol-1/#cg): <c>normal</c> is 1em in a multi-column container.</summary>
    private static double GetColumnGap(LayoutStyle layout, double percentageBasis) => Math.Max(0, layout.Alignment.ColumnGap.Resolve(percentageBasis, normalValue: layout.FontSize));

    /// <summary>
    /// Returns the used column count and column width of a multi-column container (https://drafts.csswg.org/css-multicol-1/#pseudo-algorithm)
    /// whose content box has the given inline size.
    /// </summary>
    internal static (int Count, double Width) GetColumns(FragmentationStyle fragmentation, double availableInlineSize, double gap)
    {
        // "For the purpose of finding the number of auto-repeated columns, the UA must floor the column size to a UA-specified value to
        // avoid division by zero" (1px); the count is limited, as in the browsers
        const int MaxColumnCount = 1000;
        var available = Math.Max(0, availableInlineSize);
        int count;
        if (fragmentation.ColumnWidth.TryResolve(available) is not { } columnWidth)
        {
            count = fragmentation.ColumnCount ?? 1;
        }
        else
        {
            var fitting = (int)Math.Min(MaxColumnCount, Math.Max(1, Math.Floor((available + gap) / (Math.Max(columnWidth, 1) + gap))));
            count = fragmentation.ColumnCount is { } columnCount ? Math.Min(columnCount, fitting) : fitting;
        }

        count = Math.Clamp(count, 1, MaxColumnCount);
        return (count, Math.Max(0, ((available + gap) / count) - gap));
    }

    /// <summary>
    /// Returns the offset of a column from the inline-start edge of the content box: the columns are distributed with the precision
    /// of the layout units of the browsers (1/64 px), so that the fractional column widths and gaps do not accumulate errors.
    /// </summary>
    internal static double GetColumnInlineOffset(int index, double stride) => Math.Round(index * stride * 64, MidpointRounding.AwayFromZero) / 64;

    private static double FloorToLayoutUnit(double value) => Math.Floor((value * 64) + 1e-6) / 64;

    /// <summary>The content of a multi-column container laid out in a strip, and its possible breaks.</summary>
    private readonly record struct ColumnStrip(Fragment[] Children, PhysicalSize Size, FragmentationFlow Flow, HashSet<ContainerBox>? SpannerBoxes, List<(OutOfFlowCandidate Candidate, PhysicalOffset StaticPosition)> OutOfFlow);

    /// <summary>
    /// Lays out the content of a multi-column container in a single strip of the column width
    /// (https://drafts.csswg.org/css-multicol-1/#the-multi-column-model: the column boxes establish an independent block formatting
    /// context, the containing block of the in-flow content) and collects its possible breaks. All its fragments are needed by the
    /// fragmentation, even when only the block size of the container is measured.
    /// </summary>
    /// <param name="separatesOutOfFlow">Whether the absolutely positioned boxes of the strip are fragmented apart (see <see cref="FragmentationFlow.OutOfFlowItems"/>).</param>
    /// <param name="floatPushes">The floats that are pushed to the start of a fragmentainer (see <see cref="FloatPush"/>), or null.</param>
    /// <param name="lanesBreaks">The fragmentainers of the grid lanes containers whose items are placed in each fragmentainer (see <see cref="GridLanesBreaks"/>), or null.</param>
    /// <param name="lineClamp">The state of the block overflow ellipsis of a container with <c>continue: discard</c> (see <see cref="LineClamp.CreateLineRecorder"/>), or null.</param>
    /// <param name="relayout">The inputs of the layout of the strip that depend on its fragmentation (see <see cref="StripRelayout"/>), or null.</param>
    private static ColumnStrip LayoutStrip(LayoutContext context, ContainerBox box, in ContentArea content, double columnWidth, bool separatesOutOfFlow = false, Dictionary<ContainerBox, FloatPush>? floatPushes = null, Dictionary<ContainerBox, GridLanesBreaks>? lanesBreaks = null, LineClamp? lineClamp = null, StripRelayout? relayout = null)
    {
        var writingDirection = content.WritingDirection;
        var multiColumnFlow = new MultiColumnFlow(box, writingDirection, content.InlineSize, content.PercentageBlockSize);
        var stripBuilder = new FragmentBuilder(writingDirection);
        var stripContent = new ContentArea(writingDirection, LogicalOffset.Zero, columnWidth, content.BlockSize)
        {
            MultiColumnFlow = multiColumnFlow,

            // https://drafts.csswg.org/css-box-4/#margin-trim applies to the multi-column containers: the margins of the content
            // of the columns and of the column spanners that are adjacent to the edges of the container
            TrimsBlockStartMargins = content.TrimsBlockStartMargins,
            TrimsBlockEndMargins = content.TrimsBlockEndMargins,
            IsBlockSizeIndefinite = content.IsBlockSizeIndefinite,
            IsTableCellMeasure = content.IsTableCellMeasure,
            OutsideMarker = content.OutsideMarker,
            QuirksPercentageBlockSize = content.QuirksPercentageBlockSize,
            LineClamp = lineClamp,
        };

        // The multi-column containers of the strip keep what the fragmentation of this container needs to fragment them (see NestedColumns)
        context.MultiColumnDepth++;
        var outerFloatPushes = context.FloatPushes;
        var outerLanesBreaks = context.GridLanesBreaks;
        var outerRelayout = context.StripRelayout;
        context.FloatPushes = floatPushes;
        context.GridLanesBreaks = lanesBreaks;
        context.StripRelayout = relayout;
        relayout?.BeginLayout();
        ContentLayoutResult result;
        try
        {
            result = GetContentFormattingContext(box).LayoutFlow(context, box, stripBuilder, stripContent);
        }
        finally
        {
            context.MultiColumnDepth--;
            context.FloatPushes = outerFloatPushes;
            context.GridLanesBreaks = outerLanesBreaks;
            context.StripRelayout = outerRelayout;
        }

        // (the floats of the block formatting context are given to the line clamping: the strip contains them)
        if (lineClamp is not null && lineClamp.FloatsEnd > result.BlockSize + result.EndMargin.Sum)
        {
            result = result with { BlockSize = lineClamp.FloatsEnd, EndMargin = default };
        }

        var stripSize = writingDirection.ToPhysical(new LogicalSize(columnWidth, Math.Max(0, result.BlockSize + result.EndMargin.Sum)));
        var children = stripBuilder.Build(stripSize) ?? [];
        var outOfFlow = CollectOutOfFlowCandidates(stripBuilder, children);
        children = PositionedLayout.LayoutInlineContainingBlockCandidatesOfStrip(context, box, children, stripSize, outOfFlow);
        var spannerBoxes = multiColumnFlow.SpannerBoxes.Count > 0 ? multiColumnFlow.SpannerBoxes : null;
        var flow = FragmentationFlow.Collect(children, stripSize, writingDirection, spannerBoxes, box, separatesOutOfFlow, floatPushes);
        flow.Relayout = relayout;
        return new ColumnStrip(children, stripSize, flow, spannerBoxes, outOfFlow);
    }

    /// <summary>
    /// Lays out the content of a multi-column container nested in another fragmentation context in a new strip, for the
    /// fragmentation of the outer context (see <see cref="NestedColumns"/>).
    /// </summary>
    private static NestedColumns CreateNestedColumns(LayoutContext context, ContainerBox box, ContentArea content)
    {
        var layout = box.Style.Layout;
        var fragmentation = layout.Fragmentation;
        var gap = GetColumnGap(layout, content.InlineSize);
        var (columnCount, exactColumnWidth) = GetColumns(fragmentation, content.InlineSize, gap);
        var columnWidth = FloorToLayoutUnit(exactColumnWidth);
        // (as in a container that is not nested, the absolutely positioned boxes of the strip are fragmented apart)
        var strip = LayoutStrip(context, box, content, columnWidth, separatesOutOfFlow: true);
        var wraps = fragmentation.ColumnWrap == ColumnWrap.Wrap;
        return new NestedColumns
        {
            Children = strip.Children,
            StripSize = strip.Size,
            Flow = strip.Flow,
            SpannerBoxes = strip.SpannerBoxes,
            OutOfFlow = strip.OutOfFlow.Count > 0 ? strip.OutOfFlow : null,
            StripStaticPositions = (box.LayoutState as MultiColumnState)?.StaticPositions,
            WritingDirection = content.WritingDirection,
            ColumnCount = columnCount,
            ColumnStride = exactColumnWidth + gap,
            ColumnGap = gap,
            RowGap = wraps ? Math.Max(0, layout.Alignment.RowGap.Resolve(content.PercentageBlockSize ?? 0)) : 0,
            SpecifiedColumnCount = fragmentation.ColumnCount is null ? null : columnCount,
            HasGapRules = layout.HasGapRules,
            FillsSequentially = fragmentation.ColumnFill == ColumnFill.Auto,
            Wraps = wraps,
            RowBlockSize = fragmentation.ColumnHeight,
            BlockSize = content.BlockSize ?? content.ContainedBlockSize,
            MaxBlockSize = content.BlockSize is null && content.ContainedBlockSize is null ? content.MaxBlockSize : null,
            ContentOffset = content.Offset,
            ContentInlineSize = content.InlineSize,
            CreateTrialFlow = () => LayoutStrip(context, box, content, columnWidth, separatesOutOfFlow: true).Flow,
        };
    }

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        // (the fragmentation is not cached by the layout result cache)
        context.PreventLayoutCaching();
        var layout = box.Style.Layout;
        var fragmentation = layout.Fragmentation;
        var writingDirection = content.WritingDirection;
        var gap = GetColumnGap(layout, content.InlineSize);
        var (columnCount, exactColumnWidth) = GetColumns(fragmentation, content.InlineSize, gap);
        var columnWidth = FloorToLayoutUnit(exactColumnWidth);
        var columnStride = exactColumnWidth + gap;

        // A container laid out in the strip of another fragmentation context is fragmented by it (see NestedColumns)
        MultiColumnState? ownState = null;
        if (context.MultiColumnDepth > 0)
        {
            var nestedContent = content;
            var state = new MultiColumnState(() => CreateNestedColumns(context, box, nestedContent));
            box.LayoutState = state;
            context.NestedColumnStates.Add(state);
            ownState = state;
        }
        else
        {
            box.LayoutState = null;
        }

        // The multicol rows (https://drafts.csswg.org/css-multicol-2/#multicol-row) between the column spanners
        var wraps = fragmentation.ColumnWrap == ColumnWrap.Wrap;
        var rowGap = wraps ? Math.Max(0, layout.Alignment.RowGap.Resolve(content.PercentageBlockSize ?? 0)) : 0;
        var limit = content.BlockSize ?? content.ContainedBlockSize ?? content.MaxBlockSize;

        // (a container without block size in the strip of another fragmentation context is laid out there as if its columns were
        // balanced: the outer context fragments it, see NestedColumns)
        var fillsSequentially = fragmentation.ColumnFill == ColumnFill.Auto && (limit is not null || context.MultiColumnDepth == 0);

        // The block size of the rows when it does not depend on their content: column-height, or the block size of a container
        // whose columns wrap (https://drafts.csswg.org/css-multicol-2/#ch, https://drafts.csswg.org/css-multicol-2/#cwr)
        var rowBlockSize = fragmentation.ColumnHeight ?? (wraps ? limit : null);
        var rowPitch = (rowBlockSize ?? 0) + rowGap;
        var breaks = new List<FragmentainerBreak>();

        // The content is laid out in the strip, then fragmented. The strip is laid out again while the fragmentation finds floats
        // to push to the next fragmentainer (see FloatPusher): the content beside and after them is laid out without them; and
        // while the fragmentainers of the grid lanes containers fragmented in their stacking axis change (see GridLanesFragmentation)
        Fragment[] children;
        PhysicalSize stripSize;
        FragmentationFlow flow;
        HashSet<ContainerBox>? spannerBoxes;
        List<(OutOfFlowCandidate Candidate, PhysicalOffset StaticPosition)> outOfFlow;
        List<Fragmentainer> fragmentainers;
        List<int> setEnds;
        List<GapRow>? gapRows;
        FlowMap map;
        (int ColumnIndex, double BlockOffset, double BlockSize) lastRow;
        double blockOffset;
        double[] spannerOffsets;
        Dictionary<ContainerBox, FloatPush>? floatPushes = null;
        Dictionary<ContainerBox, GridLanesBreaks>? lanesBreaks = null;

        // https://drafts.csswg.org/css-overflow-4/#valdef-continue-discard: "Content after the first region break is not
        // rendered": the overflow columns (the columns after the last column of a row) and the content after them, see DiscardPoint
        var discards = layout.Continue == ContinueKind.Discard;
        DiscardPoint? discard;

        // (the line boxes are recorded to give the block overflow ellipsis to the last line before the region break)
        var lineClamp = discards && layout.Overflow.BlockEllipsis.Kind != TextOverflowKind.Clip ? LineClamp.CreateLineRecorder(layout.Overflow.BlockEllipsis) : null;

        // (the strip is laid out again for the boxes whose layout depends on the fragmentainers, see StripRelayout; not in the strip
        // of a container nested in another fragmentation context, which is fragmented by it)
        var stripRelayout = context.MultiColumnDepth == 0 ? new StripRelayout() : null;
        for (var pass = 0; ; pass++)
        {
            discard = null;
            (children, stripSize, flow, spannerBoxes, outOfFlow) = LayoutStrip(context, box, content, columnWidth, separatesOutOfFlow: true, floatPushes, lanesBreaks, lineClamp, stripRelayout);
            fragmentainers = [];
            setEnds = [];
            gapRows = layout.HasGapRules ? [] : null;
            map = new FlowMap();
            var outOfFlowFragmentainers = new OutOfFlowFragmentainers(map);

            // The last row of columns: the absolutely positioned boxes that end after the in-flow content add columns to it
            lastRow = (ColumnIndex: 0, BlockOffset: 0d, BlockSize: 0d);
            blockOffset = 0d;
            spannerOffsets = new double[flow.Spanners.Count];

            // The block-start edge of the current row and the block size that is used in it (the rows of a fixed block size)
            var rowStart = 0d;
            var rowUsed = 0d;
            for (var set = 0; set <= flow.Spanners.Count; set++)
            {
                var isLastSet = set == flow.Spanners.Count;
                var start = set == 0 ? 0 : flow.Spanners[set - 1].MarginBoxEnd;
                var end = isLastSet ? Math.Max(flow.ContentEnd, start) : Math.Max(flow.Spanners[set].MarginBoxStart, start);
                var extentEnd = isLastSet ? Math.Max(flow.ExtentEnd, end) : end;
                var isEmpty = extentEnd - start <= 0.01;
                var balances = !(isLastSet && fillsSequentially);
                outOfFlowFragmentainers.PortionStarts.Add(fragmentainers.Count);
                if (rowBlockSize is { } fixedBlockSize)
                {
                    // The rows are at fixed positions (as in Blink): the content fills the block size that remains in the current row,
                    // then the next rows; the content of each row is balanced in its block size (the rows that are not full: the last
                    // one, the ones with forced breaks), unless the columns are filled sequentially
                    // (a column has a block size of 1px at least for its content, as in Blink: the content makes progress)
                    var firstAvailable = Math.Max(0, fixedBlockSize - rowUsed);
                    var breakerBlockSize = Math.Max(MinColumnBlockSize, fixedBlockSize);
                    var columnsPerRow = wraps ? columnCount : int.MaxValue;
                    var sizes = new FragmentainerSizes([fixedBlockSize < MinColumnBlockSize ? MinColumnBlockSize : firstAvailable, breakerBlockSize], columnsPerRow);
                    var balancesRows = fragmentation.ColumnFill != ColumnFill.Auto;
                    var balancesLastRow = balancesRows || !isLastSet;
                    if (!isEmpty && balancesLastRow)
                    {
                        ColumnBreaker.FindBreaks(flow, start, end, sizes, breaks, out _);
                        var rowCount = wraps ? (breaks.Count / columnCount) + 1 : 1;
                        var rowSizes = new double[rowCount];
                        var rowBreaks = new List<FragmentainerBreak>(breaks);
                        for (var row = 0; row < rowCount; row++)
                        {
                            var rowContentStart = row == 0 ? start : rowBreaks[(row * columnCount) - 1].Position;
                            var isLastRow = row == rowCount - 1;
                            var rowContentEnd = isLastRow ? end : rowBreaks[((row + 1) * columnCount) - 1].Position;
                            var maxBlockSize = row == 0 ? firstAvailable : fixedBlockSize;
                            rowSizes[row] = isLastRow || balancesRows
                                ? ColumnBreaker.Balance(flow, rowContentStart, rowContentEnd, columnCount, maxBlockSize, breaks, isLastRow ? extentEnd : rowContentEnd)
                                : maxBlockSize;
                            if (fixedBlockSize < MinColumnBlockSize)
                            {
                                rowSizes[row] = MinColumnBlockSize;
                            }
                        }

                        sizes = new FragmentainerSizes(rowSizes, columnsPerRow);
                    }

                    ColumnBreaker.FindBreaks(flow, start, end, sizes, breaks, out _, out var lastExtent, map, fragmentainers.Count);
                    var count = breaks.Count + 1;
                    var rows = wraps ? (count + columnCount - 1) / columnCount : 1;
                    var firstRowOffset = rowStart + rowUsed;
                    var lastRowExtent = 0d;
                    for (var column = 0; column < count; column++)
                    {
                        var columnStart = column == 0 ? start : breaks[column - 1].Position;
                        var columnEnd = column < count - 1 ? breaks[column].Position : isLastSet ? double.PositiveInfinity : end;
                        var displayOffset = column == 0 ? 0 : breaks[column - 1].DisplayOffset;
                        var row = wraps ? column / columnCount : 0;
                        var columnBlockSize = fixedBlockSize < MinColumnBlockSize ? fixedBlockSize : sizes.Get(column);
                        var blockSize = GetColumnBlockSize(breaks, column, columnStart, displayOffset, columnBlockSize, isLastSet, end);
                        var position = new LogicalOffset(GetColumnInlineOffset(wraps ? column % columnCount : column, columnStride), row == 0 ? firstRowOffset : rowStart + (row * rowPitch));
                        fragmentainers.Add(new Fragmentainer(columnStart, columnEnd, blockSize, displayOffset, position));
                        outOfFlowFragmentainers.BlockSizes.Add(columnBlockSize);
                        lastRow = ((wraps ? column % columnCount : column) + 1, position.Block, columnBlockSize);
                        if (row == rows - 1)
                        {
                            // The block size of the row that is used by its content
                            lastRowExtent = Math.Max(lastRowExtent, column < count - 1 ? columnBlockSize : Math.Min(columnBlockSize, Math.Max(0, lastExtent)));
                        }
                    }

                    if (gapRows is not null && (!isEmpty || flow.Spanners.Count == 0))
                    {
                        for (var row = 0; row < rows; row++)
                        {
                            var top = row == 0 ? firstRowOffset : rowStart + (row * rowPitch);
                            gapRows.Add(new GapRow(wraps ? Math.Min(columnCount, count - (row * columnCount)) : count, top, top + Math.Min(fixedBlockSize, sizes.Get(row * columnCount))));
                        }
                    }

                    if (discards && discard is null && !wraps && count > columnCount)
                    {
                        discard = new DiscardPoint(fragmentainers.Count - count + columnCount, set, rowStart + fixedBlockSize, gapRows?.Count ?? 0);
                    }

                    if (rows > 1)
                    {
                        rowStart += (rows - 1) * rowPitch;
                        rowUsed = 0;
                    }

                    // The last row of the container has its whole block size, even when its content is balanced (as in Blink)
                    rowUsed += lastRowExtent;
                    blockOffset = Math.Max(blockOffset, rowStart + (isLastSet && !isEmpty ? Math.Max(rowUsed, fixedBlockSize) : rowUsed));
                    setEnds.Add(fragmentainers.Count);
                    if (!isLastSet)
                    {
                        // A spanner that does not fit in the block size that remains in the row is in the next row; the content after it
                        // continues in the row where it ends
                        var spannerSize = GetSpannerExtent(flow, set, writingDirection, isEmpty);
                        if (rowUsed > 0.01 && spannerSize > fixedBlockSize - rowUsed + 0.01)
                        {
                            rowStart += rowPitch;
                            rowUsed = 0;
                        }

                        var spannerStart = rowStart + rowUsed + spannerSize - (flow.Spanners[set].MarginBoxEnd - flow.Spanners[set].MarginBoxStart);
                        spannerOffsets[set] = spannerStart;
                        rowUsed += spannerSize;
                        blockOffset = Math.Max(blockOffset, rowStart + rowUsed);
                        gapRows?.Add(new GapRow(GapRow.Spanner, spannerStart, rowStart + rowUsed));
                        if (rowPitch > 0.01 && rowUsed >= fixedBlockSize - 0.01)
                        {
                            var skipped = Math.Floor((rowUsed + 0.01) / rowPitch);
                            rowStart += skipped * rowPitch;
                            rowUsed = Math.Max(0, rowUsed - (skipped * rowPitch));
                            if (rowUsed >= fixedBlockSize - 0.01)
                            {
                                rowStart += rowPitch;
                                rowUsed = 0;
                            }
                        }
                    }

                    continue;
                }

                // The rows of the set: the columns of a container without a block size for its rows wrap at the forced breaks only
                // (each row has the block size of its content)
                var rowEnds = wraps ? GetForcedRowEnds(flow, start, end, columnCount) : null;
                for (var row = 0; row <= (rowEnds?.Count ?? 0); row++)
                {
                    var isLastRow = row == (rowEnds?.Count ?? 0);
                    var segmentStart = row == 0 ? start : rowEnds![row - 1].Position;
                    var segmentEnd = isLastRow ? end : rowEnds![row].Position;
                    var segmentExtentEnd = isLastRow ? extentEnd : rowEnds![row].ContentEnd;

                    // (a column has a block size of 1px at least for its content, as in Blink: the content makes progress)
                    var remaining = limit is { } available ? Math.Max(MinColumnBlockSize, available - blockOffset) : double.PositiveInfinity;

                    // https://drafts.csswg.org/css-multicol-1/#cf: "balance: Balance content equally between columns, as far as possible";
                    // "auto: Fills columns sequentially"; the columns before a spanner are always balanced
                    var includesEnd = isLastSet && isLastRow;
                    var columnHeight = balances || !isLastRow ? ColumnBreaker.Balance(flow, segmentStart, segmentEnd, columnCount, remaining, breaks, segmentExtentEnd, includesEnd, includesEnd ? outOfFlowFragmentainers : null) : remaining;
                    if (discards && discard is null && (balances || !isLastRow) && double.IsFinite(columnHeight))
                    {
                        // The content after the region break is discarded: the columns are balanced without it
                        ColumnBreaker.FindBreaks(flow, segmentStart, segmentEnd, columnHeight, breaks, out _);
                        if (breaks.Count >= columnCount)
                        {
                            var regionBreak = breaks[columnCount - 1].Position;
                            columnHeight = Math.Min(columnHeight, ColumnBreaker.Balance(flow, segmentStart, regionBreak, columnCount, remaining, breaks, regionBreak));
                        }
                    }

                    ColumnBreaker.FindBreaks(flow, segmentStart, segmentEnd, new FragmentainerSizes([columnHeight], int.MaxValue), breaks, out _, out var lastExtent, map, fragmentainers.Count);
                    var columns = breaks.Count + 1;
                    if (double.IsPositiveInfinity(columnHeight))
                    {
                        // The columns of a container without block size that are filled sequentially (the content is in the first
                        // one, and in the ones after the forced breaks) are as tall as their content
                        var tallest = lastExtent;
                        for (var column = 0; column < breaks.Count; column++)
                        {
                            tallest = Math.Max(tallest, breaks[column].ContentEnd - (column == 0 ? segmentStart : breaks[column - 1].Position - breaks[column - 1].DisplayOffset));
                        }

                        columnHeight = Math.Max(0, tallest);
                    }

                    lastRow = (columns, blockOffset, columnHeight);
                    for (var column = 0; column < columns; column++)
                    {
                        outOfFlowFragmentainers.BlockSizes.Add(columnHeight);
                        var columnStart = column == 0 ? segmentStart : breaks[column - 1].Position;
                        var columnEnd = column < columns - 1 ? breaks[column].Position : isLastSet && isLastRow ? double.PositiveInfinity : segmentEnd;
                        var displayOffset = column == 0 ? 0 : breaks[column - 1].DisplayOffset;
                        var blockSize = isLastRow
                            ? GetColumnBlockSize(breaks, column, columnStart, displayOffset, columnHeight, isLastSet, end)
                            : column < columns - 1 ? GetColumnBlockSize(breaks, column, columnStart, displayOffset, columnHeight, isLastSet: true, end) : Math.Max(0, lastExtent);
                        fragmentainers.Add(new Fragmentainer(columnStart, columnEnd, blockSize, displayOffset, new LogicalOffset(GetColumnInlineOffset(column, columnStride), blockOffset)));
                    }

                    if (!includesEnd && flow.OutOfFlowItems is not null)
                    {
                        // As in Blink, the absolutely positioned boxes use all the columns of a row before they continue in the next
                        // row (after the column spanner): the columns without in-flow content
                        for (var column = columns; column < columnCount; column++)
                        {
                            fragmentainers.Add(new Fragmentainer(double.PositiveInfinity, double.PositiveInfinity, columnHeight, 0, new LogicalOffset(GetColumnInlineOffset(column, columnStride), blockOffset)));
                            outOfFlowFragmentainers.BlockSizes.Add(columnHeight);
                        }
                    }

                    // The columns filled sequentially in a container whose block size is only limited (max-height) are as tall as their
                    // content; the available block size that is used up has no column
                    if (!balances && isLastRow && content.BlockSize is null && content.ContainedBlockSize is null && columns == 1)
                    {
                        columnHeight = Math.Min(columnHeight, Math.Max(0, extentEnd - segmentStart));
                    }

                    if (limit is { } limited && columnHeight > limited - blockOffset)
                    {
                        columnHeight = Math.Max(0, limited - blockOffset);
                    }

                    // The rows of columns of the set, for the gap decorations: a set without content before or after a spanner has no column
                    if (gapRows is not null && (end > start || flow.Spanners.Count == 0))
                    {
                        // (the column boxes of the last row of a container with a definite block size are as tall as what remains of
                        // its content box, also when their content is balanced)
                        var rowEnd = isLastSet && isLastRow && (content.BlockSize ?? content.ContainedBlockSize) is { } definite ? Math.Max(blockOffset + columnHeight, definite) : blockOffset + columnHeight;
                        gapRows.Add(new GapRow(columns, blockOffset, rowEnd));
                    }

                    if (discards && discard is null && columns > columnCount)
                    {
                        discard = new DiscardPoint(fragmentainers.Count - columns + columnCount, set, blockOffset + columnHeight, gapRows?.Count ?? 0);
                    }

                    blockOffset += columnHeight + (isLastRow ? 0 : rowGap);
                }

                setEnds.Add(fragmentainers.Count);
                if (!isLastSet)
                {
                    var spanner = flow.Spanners[set];
                    var spannerSize = GetSpannerExtent(flow, set, writingDirection, isEmpty);
                    spannerOffsets[set] = blockOffset + spannerSize - (spanner.MarginBoxEnd - spanner.MarginBoxStart);
                    blockOffset += spannerSize;
                    gapRows?.Add(new GapRow(GapRow.Spanner, spannerOffsets[set], blockOffset));
                }
            }

            // The absolutely positioned boxes whose containing block is in the strip, in the sequence of the columns: the ones that
            // end after the in-flow content add columns to the last row
            if (flow.OutOfFlowItems is not null || flow.HasNestedColumns)
            {
                // (also the boxes of the nested multi-column containers, which need rows of columns in the next columns)
                var placedOutOfFlow = ColumnBreaker.PlaceOutOfFlow(flow, map, outOfFlowFragmentainers.BlockSizes, lastRow.BlockSize, outOfFlowFragmentainers.PortionStarts);
                var outOfFlowCount = Math.Max(placedOutOfFlow.Count, placedOutOfFlow.NestedCount);
                var added = 0;
                for (; fragmentainers.Count < outOfFlowCount; added++)
                {
                    fragmentainers.Add(new Fragmentainer(double.PositiveInfinity, double.PositiveInfinity, Math.Max(MinColumnBlockSize, lastRow.BlockSize), 0, new LogicalOffset(GetColumnInlineOffset(lastRow.ColumnIndex + added, columnStride), lastRow.BlockOffset)));
                }

                if (added > 0)
                {
                    setEnds[^1] = fragmentainers.Count;
                    if (gapRows is not null)
                    {
                        // (the columns of the last row, for the gap decorations)
                        for (var i = gapRows.Count - 1; i >= 0; i--)
                        {
                            if (!gapRows[i].IsSpanner)
                            {
                                gapRows[i] = gapRows[i] with { Columns = gapRows[i].Columns + added };
                                break;
                            }
                        }
                    }
                }

                if (discards && discard is null && lastRow.ColumnIndex + added > columnCount)
                {
                    discard = new DiscardPoint(fragmentainers.Count - added + Math.Max(0, columnCount - lastRow.ColumnIndex), setEnds.Count - 1, lastRow.BlockOffset + lastRow.BlockSize, gapRows?.Count ?? 0);
                }
            }

            // https://drafts.csswg.org/css-overflow-4/#block-ellipsis: the last line before the region break gets the block overflow
            // ellipsis: the content is laid out again, then fragmented again
            var relayout = false;
            var restarts = false;
            if (lineClamp is { RecordsLines: true } recorder && discard is { } point)
            {
                lineClamp = recorder.CreateEllipsisClamp(fragmentainers[point.Index].Start);
                if (lineClamp is not null)
                {
                    relayout = true;
                    floatPushes = null;
                    lanesBreaks = null;
                    pass = -1;
                }
            }
            else if (stripRelayout?.UpdateBrokenBoxes(flow, map, children, stripSize, writingDirection, outOfFlowFragmentainers.BlockSizes) == true
                || stripRelayout?.UpdateNestedAnchors(flow, map) == true)
            {
                // The broken boxes (and the anchor-positioned boxes of the anchors in the nested multi-column containers) are laid
                // out again (see StripRelayout): the floats are pushed again from the start
                restarts = true;
                floatPushes = null;
                lanesBreaks = null;
                pass = -1;
            }
            else if (pass >= FloatPusher.MaxPasses)
            {
                break;
            }

            if (!restarts)
            {
                var pushesFloats = FloatPusher.Update(context, flow, map, outOfFlowFragmentainers.BlockSizes, fragmentainers, ref floatPushes);
                if (!relayout && !GridLanesFragmentation.Update(flow, children, stripSize, writingDirection, map, outOfFlowFragmentainers.BlockSizes, ref lanesBreaks) && !pushesFloats
                    && stripRelayout?.UpdateFloatAvoiders(flow, map, children, stripSize, writingDirection) != true)
                    break;
            }

            // (the nested multi-column containers of the strip are laid out again with it)
            if (context.MultiColumnDepth == 0)
            {
                foreach (var nestedState in context.NestedColumnStates)
                {
                    nestedState.Clear();
                }

                context.NestedColumnStates.Clear();
            }
        }

        // The flows of the static positions (the fragments are not in the strip anymore once they are sliced)
        var staticPositionMaps = new FlowMap?[outOfFlow.Count];
        (int Index, double Offset, double InlineShift)?[]? nestedStaticPositions = null;
        for (var i = 0; i < staticPositionMaps.Length; i++)
        {
            // (a static position in a nested multi-column container is in the columns that the breaker placed)
            if (ColumnBreaker.TryGetNestedStaticPosition(flow, outOfFlow[i].Candidate.Box, out var nestedIndex, out var nestedOffset, out var nestedInlineShift))
            {
                (nestedStaticPositions ??= new (int, double, double)?[outOfFlow.Count])[i] = (nestedIndex, nestedOffset, nestedInlineShift);
            }

            staticPositionMaps[i] = outOfFlow[i].Candidate.StaticPositionContainer is { } carrier ? flow.GetContainingItem(carrier)?.Map : null;
            outOfFlow[i].Candidate.StaticPositionContainer = null;
        }

        // The fragments of the strip in the column boxes, with the column spanners between the rows
        var slicer = new FragmentSlicer(flow, map, fragmentainers, writingDirection);
        var placed = slicer.Slice(children, stripSize, spannerBoxes);
        if (context.MultiColumnDepth == 0 && context.NestedColumnStates.Count > 0)
        {
            // The nested containers are fragmented: their states no longer need the layout pass
            foreach (var nestedState in context.NestedColumnStates)
            {
                nestedState.Clear();
            }

            context.NestedColumnStates.Clear();
        }

        // https://drafts.csswg.org/css-align-3/#baseline-export: as in the browsers, the first baseline is the highest first baseline of
        // the columns and spanners, the last baseline the lowest last baseline (https://github.com/w3c/csswg-drafts/issues/7639)
        double? firstBaseline = null;
        double? lastBaseline = null;
        var keptFragmentainers = discard?.Index ?? fragmentainers.Count;
        var keptSpanners = discard?.Set ?? flow.Spanners.Count;
        var index = 0;
        for (var set = 0; set < setEnds.Count; set++)
        {
            for (; index < Math.Min(setEnds[set], keptFragmentainers); index++)
            {
                var position = fragmentainers[index].Position;
                double? columnFirstBaseline = null;
                double? columnLastBaseline = null;
                foreach (var (fragment, offset) in placed[index])
                {
                    builder.AddChild(fragment, new LogicalOffset(content.Offset.Inline + position.Inline + offset.Inline, content.Offset.Block + position.Block + offset.Block));
                    if (GetBaselines(fragment, writingDirection) is var (first, last))
                    {
                        columnFirstBaseline ??= first + offset.Block;
                        columnLastBaseline = last + offset.Block ?? columnLastBaseline;
                    }
                }

                AddBaselines(position.Block + columnFirstBaseline, position.Block + columnLastBaseline);
            }

            if (set < keptSpanners)
            {
                var spanner = flow.Spanners[set].Fragment;
                var margin = writingDirection.ToLogical(spanner.Margin);
                var blockStart = spannerOffsets[set] + margin.BlockStart;
                builder.AddChild(spanner, new LogicalOffset(content.Offset.Inline + margin.InlineStart, content.Offset.Block + blockStart));
                if (GetBaselines(spanner, writingDirection) is var (first, last))
                {
                    AddBaselines(blockStart + first, blockStart + last);
                }

                // The outside marker of the container that is aligned with the first line of the spanner
                if (slicer.SpannerMarkers is { } markers)
                {
                    foreach (var (spannerIndex, marker, offset) in markers)
                    {
                        if (spannerIndex == set)
                        {
                            builder.AddChild(marker, new LogicalOffset(content.Offset.Inline + offset.Inline, content.Offset.Block + spannerOffsets[set] + offset.Block));
                        }
                    }
                }
            }
        }

        // The absolutely positioned descendants whose containing block is the container or an ancestor: their static positions in the columns
        for (var i = 0; i < outOfFlow.Count; i++)
        {
            var (candidate, staticPosition) = outOfFlow[i];
            var logical = writingDirection.ToLogical(staticPosition, stripSize, PhysicalSize.Zero);
            if (nestedStaticPositions?[i] is var (nestedColumn, nestedBlockOffset, nestedShift))
            {
                if (nestedColumn >= keptFragmentainers)
                    continue;

                var origin = fragmentainers[Math.Clamp(nestedColumn, 0, fragmentainers.Count - 1)].Position;
                var nestedPosition = new LogicalOffset(content.Offset.Inline + origin.Inline + logical.Inline + nestedShift, content.Offset.Block + origin.Block + nestedBlockOffset);
                builder.AddOutOfFlowCandidate(candidate, nestedPosition);
                ownState?.AddStaticPosition(candidate.Box, nestedPosition);
                continue;
            }

            if (staticPositionMaps[i] is { } staticPositionMap)
            {
                // The static position is in a parallel flow (the contents of an absolutely positioned box...)
                var place = staticPositionMap.MapEnd(logical.Block, double.NegativeInfinity);
                var column = Math.Clamp(place.Index, 0, fragmentainers.Count - 1);
                if (column >= keptFragmentainers)
                    continue;

                var origin = fragmentainers[column].Position;
                var parallelPosition = new LogicalOffset(content.Offset.Inline + origin.Inline + logical.Inline, content.Offset.Block + origin.Block + (column == place.Index ? place.Offset : staticPositionMap.GetOffsetIn(column, logical.Block)));
                builder.AddOutOfFlowCandidate(candidate, parallelPosition);
                ownState?.AddStaticPosition(candidate.Box, parallelPosition);
                continue;
            }

            // A static position at a break is at the end of the previous column when it has room: the box precedes the content after the break
            var fragmentainer = slicer.GetFragmentainerIndex(logical.Block);
            if (fragmentainer > 0 && Math.Abs(fragmentainers[fragmentainer].Start - logical.Block) < 0.01 && Math.Abs(fragmentainers[fragmentainer - 1].End - logical.Block) < 0.01
                && (slicer.ToFragmentainerOffset(fragmentainer - 1, logical.Block) < fragmentainers[fragmentainer - 1].BlockSize - 0.01 || map.IsTruncated(fragmentainer - 1, logical.Block)))
            {
                fragmentainer--;
            }

            if (fragmentainer >= keptFragmentainers)
                continue;

            var position = fragmentainers[fragmentainer].Position;
            var staticOffset = slicer.ToFragmentainerOffset(fragmentainer, logical.Block);
            if (map.IsTruncated(fragmentainer, logical.Block))
            {
                // (after a margin that is truncated at the break)
                staticOffset = Math.Min(staticOffset, fragmentainers[fragmentainer].BlockSize);
            }

            var columnPosition = new LogicalOffset(content.Offset.Inline + position.Inline + logical.Inline, content.Offset.Block + position.Block + staticOffset);
            builder.AddOutOfFlowCandidate(candidate, columnPosition);
            ownState?.AddStaticPosition(candidate.Box, columnPosition);
        }

        if (discard is { } discarded)
        {
            // The content box ends with the row of the region break (the rows of columns and the spanners after it are discarded)
            blockOffset = discarded.BlockEnd;
            if (gapRows is not null)
            {
                gapRows.RemoveRange(discarded.GapRowCount, gapRows.Count - discarded.GapRowCount);
                if (gapRows.Count > 0 && !gapRows[^1].IsSpanner)
                {
                    gapRows[^1] = gapRows[^1] with { Columns = Math.Min(gapRows[^1].Columns, columnCount) };
                }
            }
        }

        if (gapRows is not null)
        {
            builder.GapGeometry = BuildGapGeometry(gapRows, content.Offset, content.InlineSize, fragmentation.ColumnCount is null ? null : columnCount, columnStride, gap, rowGap);
        }

        // https://drafts.csswg.org/css-multicol-2/#column-pseudo: the column boxes, for their scroll markers, their snap areas and
        // the scrollable overflow (see BoxFragment.ColumnRects)
        if (fragmentainers.Count > 0)
        {
            var columnRects = new LogicalRect[keptFragmentainers];
            for (var i = 0; i < columnRects.Length; i++)
            {
                var position = fragmentainers[i].Position;
                columnRects[i] = new LogicalRect(new LogicalOffset(content.Offset.Inline + position.Inline, content.Offset.Block + position.Block), new LogicalSize(columnWidth, double.IsFinite(fragmentainers[i].BlockSize) ? fragmentainers[i].BlockSize : 0));
            }

            builder.ColumnRects = columnRects;
        }

        return new ContentLayoutResult(blockOffset, firstBaseline, lastBaseline);

        void AddBaselines(double? first, double? last)
        {
            if (first is { } firstValue)
            {
                firstBaseline = Math.Min(firstBaseline ?? double.PositiveInfinity, firstValue);
            }

            if (last is { } lastValue)
            {
                lastBaseline = Math.Max(lastBaseline ?? double.NegativeInfinity, lastValue);
            }
        }
    }

    /// <summary>
    /// The first region break of a multi-column container with <c>continue: discard</c>
    /// (https://drafts.csswg.org/css-overflow-4/#valdef-continue-discard): the end of the last column of the row whose content
    /// overflows it into overflow columns. The content after it is discarded: the fragmentainers from <paramref name="Index"/>, the
    /// column spanners from <paramref name="Set"/>, the rows after it.
    /// </summary>
    /// <param name="Index">The index of the first discarded fragmentainer (the first overflow column).</param>
    /// <param name="Set">The index of the set of columns of the region break: the column spanners from this index are discarded.</param>
    /// <param name="BlockEnd">The block-end edge of the row of the region break in the content box: the end of the contents.</param>
    /// <param name="GapRowCount">The number of rows of the gap decorations to keep.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct DiscardPoint(int Index, int Set, double BlockEnd, int GapRowCount);

    /// <summary>The smallest block size of a column for the fragmentation of its content, as in Blink: the content makes progress in a container without block size.</summary>
    private const double MinColumnBlockSize = 1;

    /// <summary>
    /// Returns the block size of a column, for the boxes that are broken at its end: they extend to the end of the column (as in
    /// Blink); the last column before a spanner ends with its content.
    /// </summary>
    private static double GetColumnBlockSize(List<FragmentainerBreak> breaks, int column, double columnStart, double displayOffset, double columnBlockSize, bool isLastSet, double end)
    {
        // (a gap or a margin that disappears at the break is not content of the column)
        if (column < breaks.Count)
            return Math.Max(columnBlockSize, breaks[column].ContentEnd - columnStart + displayOffset);

        return isLastSet ? columnBlockSize : end - columnStart + displayOffset;
    }

    /// <summary>
    /// Returns the forced breaks of a portion of the strip that end a row of columns: every forced break that ends the last
    /// column of a row (the position where the next row starts and the end of the content before it), or null.
    /// </summary>
    private static List<(double Position, double ContentEnd)>? GetForcedRowEnds(FragmentationFlow flow, double start, double end, int columnCount)
    {
        List<(double Position, double ContentEnd)>? result = null;
        var column = 0;
        foreach (var candidate in flow.Candidates)
        {
            if (candidate.Position <= start + 0.01 || candidate.Appeal != BreakAppeal.Forced)
                continue;

            if (candidate.Position >= end - 0.01)
                break;

            if (++column == columnCount)
            {
                (result ??= []).Add((candidate.Position, candidate.ContentEnd));
                column = 0;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the block size a column spanner adds to the content of the container: its margin box, less the margin that collapses
    /// with the block-end margin of the spanner before it when they are siblings and no content separates them
    /// (https://drafts.csswg.org/css-multicol-1/#spanning-columns: "margins of two adjacent spanners collapse with each other";
    /// as in the browsers, not the margins of a spanner and of a spanner nested in the next box).
    /// </summary>
    private static double GetSpannerExtent(FragmentationFlow flow, int index, WritingDirection writingDirection, bool isAfterEmptySet)
    {
        var spanner = flow.Spanners[index];
        var extent = spanner.MarginBoxEnd - spanner.MarginBoxStart;
        if (index == 0 || !isAfterEmptySet || spanner.Fragment.Box.Parent != flow.Spanners[index - 1].Fragment.Box.Parent)
            return extent;

        var previousEnd = writingDirection.ToLogical(flow.Spanners[index - 1].Fragment.Margin).BlockEnd;
        var start = writingDirection.ToLogical(spanner.Fragment.Margin).BlockStart;
        return extent - (previousEnd + start - MarginStrut.FromMargin(previousEnd).Append(start).Sum);
    }

    /// <summary>
    /// Returns the first and last baselines of a fragment of a column (from its block-start edge, in the writing mode of the
    /// container), or null when it has none: the baselines of a line box, or of a box fragment with the same block-start side (a
    /// block-level table gives no last baseline, as in the block layout).
    /// </summary>
    private static (double? First, double? Last)? GetBaselines(Fragment fragment, WritingDirection writingDirection)
    {
        switch (fragment)
        {
            case LineBoxFragment line:
                return (line.Baseline, line.Baseline);

            case BoxFragment box when !box.Box.IsOutOfFlow && !box.Box.IsOutsideMarker && box.WritingDirection.BlockStart == writingDirection.BlockStart
                && (box.FirstBaseline is not null || box.LastBaseline is not null):
                return (box.FirstBaseline, box.Box.Kind == ContainerBoxKind.TableWrapper ? null : box.LastBaseline);

            default:
                return null;
        }
    }

    /// <summary>
    /// Takes the absolutely positioned boxes of the strip whose containing block is the multi-column container or an ancestor
    /// (the candidates of the strip and the ones carried by its fragments), with their static positions in the coordinates of the strip.
    /// </summary>
    private static List<(OutOfFlowCandidate Candidate, PhysicalOffset StaticPosition)> CollectOutOfFlowCandidates(FragmentBuilder stripBuilder, Fragment[] children)
    {
        var result = new List<(OutOfFlowCandidate, PhysicalOffset)>();
        var own = stripBuilder.TakeOutOfFlowCandidates();
        var ownIndex = 0;
        var candidates = new List<OutOfFlowCandidate>();
        for (var i = 0; i <= children.Length; i++)
        {
            while (own is not null && ownIndex < own.Count && own[ownIndex].ChildIndex <= i)
            {
                var candidate = own[ownIndex++].Candidate;
                result.Add((candidate, candidate.StaticPosition));
            }

            if (i < children.Length)
            {
                candidates.Clear();
                PositionedLayout.CollectCandidates(children[i], children[i].Offset, candidates);
                foreach (var candidate in candidates)
                {
                    result.Add((candidate, candidate.StaticPosition));
                }
            }
        }

        return result;
    }

    /// <summary>A row of columns of a multi-column container, or a column spanner, for the geometry of the gaps: the number of columns that have content and the extent of the row in the block axis (from the content box).</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct GapRow(int Columns, double Start, double End)
    {
        /// <summary>The number of columns of the row of a column spanner.</summary>
        public const int Spanner = -1;

        public bool IsSpanner => Columns == Spanner;
    }

    /// <summary>
    /// Returns the gaps of a multi-column container (https://drafts.csswg.org/css-gaps-1/#gap-multicol): "A column gap is the gutter
    /// between adjacent column boxes [...] A row gap is the gutter between the rows of column boxes". A column gap runs from the
    /// first to the last row of columns and is broken by the column spanners ("column gaps break when they encounter the spanner");
    /// it exists between the columns of the specified <c>column-count</c> even when they have no content, so that
    /// <c>rule-visibility-items</c> decides whether it is decorated (by default, between two columns that both have content,
    /// https://drafts.csswg.org/css-multicol-1/#crw).
    /// </summary>
    /// <param name="rows">The rows of columns and the spanners, in the block axis.</param>
    /// <param name="contentOffset">The offset of the content box in the fragment.</param>
    /// <param name="contentInlineSize">The inline size of the content box.</param>
    /// <param name="specifiedColumnCount">The specified <c>column-count</c>, or null.</param>
    /// <param name="columnStride">The distance between the inline-start edges of two columns.</param>
    /// <param name="columnGap">The size of the column gaps.</param>
    /// <param name="rowGap">The size of the row gaps.</param>
    /// <param name="rowGapBaseIndex">The number of row gaps of the container before the ones of the rows (in the fragments of the container in the previous fragmentainers, and at the breaks): they consume the values of the row rules.</param>
    /// <param name="rowGapTotalCount">The number of row gaps of the container, in all its fragments, or null when the rows are all its rows.</param>
    internal static GapGeometry? BuildGapGeometry(List<GapRow> rows, LogicalOffset contentOffset, double contentInlineSize, int? specifiedColumnCount, double columnStride, double columnGap, double rowGap, int rowGapBaseIndex = 0, int? rowGapTotalCount = null)
    {
        // The adjacent column spanners are one row
        for (var i = rows.Count - 1; i > 0; i--)
        {
            if (rows[i].IsSpanner && rows[i - 1].IsSpanner)
            {
                rows[i - 1] = rows[i - 1] with { End = rows[i].End };
                rows.RemoveAt(i);
            }
        }

        var maxColumns = 0;
        var firstColumnRow = -1;
        var lastColumnRow = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsSpanner)
            {
                maxColumns = Math.Max(maxColumns, rows[i].Columns);
                firstColumnRow = firstColumnRow < 0 ? i : firstColumnRow;
                lastColumnRow = i;
            }
        }

        if (firstColumnRow < 0)
            return null;

        var columnGapCount = Math.Max(maxColumns, specifiedColumnCount ?? 0) - 1;

        // The row gaps: between two consecutive rows of columns
        var rowGapIndexes = new int[rows.Count];
        var rowGapCount = 0;
        for (var i = 0; i + 1 < rows.Count; i++)
        {
            rowGapIndexes[i] = !rows[i].IsSpanner && !rows[i + 1].IsSpanner ? rowGapCount++ : -1;
        }

        if (columnGapCount <= 0 && rowGapCount == 0)
            return null;

        columnGapCount = Math.Max(0, columnGapCount);
        var inlineOffset = contentOffset.Inline;
        var blockOffset = contentOffset.Block;
        var columnGaps = new GapLine[columnGapCount];
        for (var gap = 0; gap < columnGapCount; gap++)
        {
            var intersections = new GapIntersection[rows.Count + 1];
            intersections[0] = GapIntersection.Edge(blockOffset + rows[firstColumnRow].Start, GetColumnGapState(gap, rows[0]));
            for (var i = 1; i < rows.Count; i++)
            {
                var state = GetColumnGapState(gap, rows[i]);
                if (rowGapIndexes[i - 1] is var rowGapIndex and >= 0)
                {
                    var intersection = GapIntersection.Junction(blockOffset + rows[i - 1].End + (rowGap / 2), rowGap, rowGapIndex, state);
                    intersection.HasCrossStates = true;
                    intersection.CrossBefore = GetRowGapState(gap, rows[i - 1], rows[i]);
                    intersection.CrossAfter = GetRowGapState(gap + 1, rows[i - 1], rows[i]);
                    intersections[i] = intersection;
                }
                else
                {
                    // The edge of a column spanner: the gap ends before it and starts again after it
                    intersections[i] = GapIntersection.Edge(blockOffset + (rows[i].IsSpanner ? rows[i].Start : rows[i - 1].End), state);
                }
            }

            intersections[^1] = GapIntersection.Edge(blockOffset + rows[lastColumnRow].End);
            columnGaps[gap] = new GapLine
            {
                Center = inlineOffset + GetColumnInlineOffset(gap + 1, columnStride) - (columnGap / 2),
                Size = columnGap,
                Intersections = intersections,
                ValueIndex = gap,
                ValueCount = columnGapCount,
            };
        }

        var rowGaps = new GapLine[rowGapCount];
        var inlineEnd = Math.Max(inlineOffset + contentInlineSize, columnGapCount > 0 ? columnGaps[^1].Center : 0);
        for (var i = 0; i + 1 < rows.Count; i++)
        {
            if (rowGapIndexes[i] is not (var index and >= 0))
                continue;

            var intersections = new GapIntersection[columnGapCount + 2];
            intersections[0] = GapIntersection.Edge(inlineOffset, GetRowGapState(0, rows[i], rows[i + 1]));
            for (var gap = 0; gap < columnGapCount; gap++)
            {
                var intersection = GapIntersection.Junction(columnGaps[gap].Center, columnGap, gap, GetRowGapState(gap + 1, rows[i], rows[i + 1]));
                intersection.HasCrossStates = true;
                intersection.CrossBefore = GetColumnGapState(gap, rows[i]);
                intersection.CrossAfter = GetColumnGapState(gap, rows[i + 1]);
                intersections[gap + 1] = intersection;
            }

            intersections[^1] = GapIntersection.Edge(inlineEnd);
            rowGaps[index] = new GapLine
            {
                Center = blockOffset + rows[i].End + (rowGap / 2),
                Size = rowGap,
                Intersections = intersections,
                ValueIndex = rowGapBaseIndex + index,
                ValueCount = rowGapTotalCount ?? rowGapCount,
                IsMain = true,
            };
        }

        return new GapGeometry { Kind = GapContainerKind.MultiColumn, ColumnGaps = columnGaps, RowGaps = rowGaps };

        // The state of the gap between the columns `gap` and `gap + 1` along a row
        GapSegmentState GetColumnGapState(int gap, GapRow row)
        {
            if (!row.IsSpanner && gap + 1 < row.Columns)
                return GapSegmentState.None;

            // As in Blink, the gaps after the specified columns (the columns that overflow) are not interrupted by the spanners
            if (row.IsSpanner && (specifiedColumnCount is null || gap + 1 < specifiedColumnCount))
                return GapSegmentState.Blocked;

            return gap + 1 == row.Columns ? GapSegmentState.EmptyAfter : GapSegmentState.EmptyBefore | GapSegmentState.EmptyAfter;
        }

        // The state of the gap between two rows of columns along a column
        static GapSegmentState GetRowGapState(int column, GapRow above, GapRow below)
        {
            if (column >= Math.Min(above.Columns, below.Columns) && column < Math.Max(above.Columns, below.Columns))
                return above.Columns > below.Columns ? GapSegmentState.EmptyAfter : GapSegmentState.EmptyBefore;

            return GapSegmentState.None;
        }
    }
}
