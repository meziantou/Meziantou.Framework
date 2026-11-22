using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The paginated layout of a document being printed (https://drafts.csswg.org/css-page-3/, see PrintSession): the document is
// laid out in a strip of the inline size of the page area of the first page (the initial containing block), then fragmented
// into pages by the fragmentation of the multi-column layout (FragmentationFlow, ColumnBreaker, FragmentSlicer): the page areas
// are the fragmentainers. The fixed positioned boxes are repeated on every page.
internal sealed partial class LayoutEngine
{
    private readonly PrintSession? _printSession;
    private List<LayoutPage>? _pages;

    /// <summary>Creates the layout engine of the printing of a document (see <see cref="PrintSession"/>).</summary>
    internal LayoutEngine(Dom.Document document, PrintSession printSession)
        : this(document)
    {
        _printSession = printSession;
    }

    /// <summary>Whether the engine paginates the document for its printing (see <see cref="PrintSession"/>).</summary>
    internal bool IsPrintLayout => _printSession is not null;

    /// <summary>Whether the document is printed without its backgrounds (<see cref="Dom.PrintOptions.PrintBackground"/> is false).</summary>
    internal bool PrintsWithoutBackgrounds => _printSession is { Options.PrintBackground: false };

    /// <summary>Returns the pages of the document being printed, after computing the layout when it is out of date (empty when the document is not rendered).</summary>
    internal IReadOnlyList<LayoutPage> GetPages()
    {
        EnsureLayout();
        return _pages ?? [];
    }

    /// <summary>A content page found by the breaker, before the blank pages are inserted.</summary>
    private readonly record struct ContentPage(double Start, string Name, BreakBetween Side);

    /// <summary>
    /// The start of a segment of the pages laid out in a strip of another inline size (see <see cref="LayoutPages"/>): the
    /// content that starts the first page of the segment (see <see cref="BreakAnchor"/>), the size of the page area of that page,
    /// and the last page before it.
    /// </summary>
    private sealed record PageSegmentStart(BreakAnchor Anchor, PhysicalSize PageAreaSize, PageSelection Previous)
    {
        /// <summary>
        /// Whether the header and the footer of the tables are not repeated, by table grid box (see
        /// <see cref="RepeatedSections.IsHeaderDisabled"/>): the breaker decides it in the page where a table starts, which is in
        /// a previous segment for the tables broken by the start of the segment.
        /// </summary>
        public Dictionary<ContainerBox, (bool IsHeaderDisabled, bool IsFooterDisabled)>? RepeatedSectionsDisabled { get; init; }

        /// <summary>The fragments of the boxes fixed to the viewport, laid out in the initial containing block of the first segment (the page area of the first page).</summary>
        public required List<(BoxFragment Fragment, LogicalOffset Offset)>? FixedBoxes { get; init; }

        /// <summary>The position of the break in the strip of the previous segment (see <see cref="LayoutPages"/>: a break whose content after it cannot be laid out in the page area of the segment).</summary>
        public double BreakPosition { get; init; }
    }

    /// <summary>
    /// Lays out the document into pages: the root element is laid out in the initial containing block (the page area of the
    /// first page) as a strip, which is fragmented into the page areas. The pages whose page area has another inline size than
    /// the strip are laid out again: from the box that starts the first of them, the pages are a segment of a strip laid out
    /// in its page area (see <see cref="LayoutPageSegment"/>), as many times as the inline size changes. Returns the fragment
    /// of the page area of the first page (the root of the fragment tree, see <see cref="EnsureLayout"/>); the pages are in
    /// <see cref="_pages"/>.
    /// </summary>
    private BoxFragment LayoutPages(LayoutContext context, ContainerBox viewport)
    {
        var styleEngine = _document.GetStyleEngine();
        var writingDirection = WritingDirection.FromStyle(viewport.Style.Layout);
        var geometries = new Dictionary<PageSelection, PageGeometry>();
        var pages = new List<LayoutPage>();
        var selections = new List<PageSelection>();
        var viewportSize = context.ViewportSize;
        PageSegmentStart? segment = null;

        // The segments laid out (their start and the index of their first page), and the breaks of a segment whose content after
        // them cannot be laid out in the page area of the next segment (see MeasureRowBreak), by index of its first page
        var segments = new List<(PageSegmentStart? Start, int FirstPage)>();
        Dictionary<int, HashSet<double>>? rejectedBreaks = null;
        PhysicalSize? icbSize = null;
        try
        {
            while (true)
            {
                context.ViewportSize = segment?.PageAreaSize ?? viewportSize;
                var count = pages.Count;
                var next = LayoutPageSegment(context, viewport, writingDirection, segment, pages, selections, geometries, rejectedBreaks?.GetValueOrDefault(count), out var segmentIcbSize, out var isRejected);
                if (isRejected && segment is not null && segments.Count > 0)
                {
                    // The content after the break that starts the segment cannot be laid out in its page area: the previous segment
                    // is laid out again, without ending at that break (the breaks rejected in the segments after it no longer apply)
                    var (previous, firstPage) = segments[^1];
                    segments.RemoveAt(segments.Count - 1);
                    pages.RemoveRange(firstPage, pages.Count - firstPage);
                    selections.RemoveRange(firstPage, selections.Count - firstPage);
                    rejectedBreaks ??= [];
                    foreach (var key in rejectedBreaks.Keys.Where(key => key > firstPage).ToList())
                    {
                        rejectedBreaks.Remove(key);
                    }

                    if (!rejectedBreaks.TryGetValue(firstPage, out var rejected))
                    {
                        rejected = [];
                        rejectedBreaks[firstPage] = rejected;
                    }

                    rejected.Add(segment.BreakPosition);
                    segment = previous;
                    continue;
                }

                icbSize ??= segmentIcbSize;

                // (a segment always has a page: the next one starts after it)
                if (pages.Count == count || next is null || pages.Count >= ColumnBreaker.MaxFragmentainers)
                    break;

                segments.Add((segment, count));
                segment = next;
            }
        }
        finally
        {
            context.ViewportSize = viewportSize;
        }

        // https://drafts.csswg.org/css-page-3/#margin-boxes: the page-margin boxes, once the number of pages is known
        var counters = new PageCounters(_document.DocumentElement is { } root ? styleEngine.GetStyle(root) : null);
        for (var i = 0; i < pages.Count; i++)
        {
            pages[i].MarginBoxes = PageMarginBoxes.Layout(context, pages[i], selections[i], pages.Count, counters.Next(pages[i].Geometry.Style), _printSession!.Options.SafePrintableInset);
        }

        _pages = pages;
        context.InitialContainingBlockSize = icbSize ?? context.InitialContainingBlockSize;
        return pages[0].Content;
    }

    /// <summary>Returns the geometry of a page.</summary>
    private PageGeometry GetPageGeometry(Dictionary<PageSelection, PageGeometry> geometries, PageSelection selection)
    {
        if (!geometries.TryGetValue(selection, out var geometry))
        {
            var session = _printSession!;
            var styleEngine = _document.GetStyleEngine();
            var style = styleEngine.GetPageStyle(selection, session.DefaultMargins, out var size, out var orientation, out var marginSafety);
            geometry = PageGeometry.Resolve(style, size, orientation, marginSafety, session.Options, styleEngine);
            geometries[selection] = geometry;
        }

        return geometry;
    }

    /// <summary>
    /// Lays out the root element in a strip of the inline size of the initial containing block (the page area of the first page
    /// of the segment) and fragments it into pages, from the start of the segment (the start of the content for the first
    /// one). The pages are added to <paramref name="pages"/> until the first page whose page area has another inline size than
    /// the strip and that starts at a break before a box of the block flow of the root element: the next segment starts there
    /// (it is returned), else null.
    /// </summary>
    private PageSegmentStart? LayoutPageSegment(LayoutContext context, ContainerBox viewport, WritingDirection writingDirection, PageSegmentStart? segment, List<LayoutPage> pages, List<PageSelection> selections, Dictionary<PageSelection, PageGeometry> geometries, HashSet<double>? rejectedBreaks, out PhysicalSize icbSize, out bool isRejected)
    {
        isRejected = false;
        // https://drafts.csswg.org/css-page-3/#progression: the first page is a right page when the pages progress from left to right
        var rightToLeft = writingDirection.IsVertical ? writingDirection.BlockStart == PhysicalSide.Right : writingDirection.IsRightToLeft;
        Dictionary<ContainerBox, FloatPush>? floatPushes = null;
        Dictionary<ContainerBox, GridLanesBreaks>? lanesBreaks = null;

        // The absolutely positioned boxes of the initial containing block that do not fit in the rest of their page: they are
        // fragmented apart
        HashSet<ContainerBox>? fragmentedBoxes = null;
        var stripRelayout = new StripRelayout();

        // The content of a row broken by the break of the segment (see BreakAnchor.Items): its items, laid out again in the strip,
        // start their content after the break at the same position (see LayoutContext.AnchorPushes)
        // (also in the columns of a multi-column container, see BreakAnchor.ColumnContent)
        var rowAnchor = (segment?.Anchor.ColumnContent ?? segment?.Anchor) is { Items: not null } brokenRow ? brokenRow : null;
        Dictionary<ContainerBox, double>? anchorPushes = rowAnchor is null ? null : [];
        var alignmentPasses = 0;
        for (var pass = 0; ; pass++)
        {
            // The strip: the root element laid out in the initial containing block, as the content of a fragmentation context
            context.MultiColumnDepth++;
            var outerFloatPushes = context.FloatPushes;
            var outerLanesBreaks = context.GridLanesBreaks;
            var outerRelayout = context.StripRelayout;
            var outerResumes = context.InlineResumes;
            var outerRowResumes = context.RowResumes;
            var outerAnchorPushes = context.AnchorPushes;
            var outerResumedItems = context.ResumedRowItems;
            context.FloatPushes = floatPushes;
            context.GridLanesBreaks = lanesBreaks;
            context.StripRelayout = stripRelayout;
            context.InlineResumes = segment is null ? null : GetInlineResumes(segment.Anchor);
            context.RowResumes = segment is null ? null : GetRowResumes(segment.Anchor);
            context.AnchorPushes = anchorPushes;
            context.ResumedRowItems = rowAnchor is null ? null : GetResumedRowItems(rowAnchor);
            context.StripLayoutCount++;
            stripRelayout.BeginLayout();
            BoxFragment initialContainingBlock;
            try
            {
                initialContainingBlock = LayoutViewport(context, viewport, default, Scrollbars.None);
            }
            finally
            {
                context.MultiColumnDepth--;
                context.FloatPushes = outerFloatPushes;
                context.GridLanesBreaks = outerLanesBreaks;
                context.StripRelayout = outerRelayout;
                context.InlineResumes = outerResumes;
                context.RowResumes = outerRowResumes;
                context.AnchorPushes = outerAnchorPushes;
                context.ResumedRowItems = outerResumedItems;
            }

            icbSize = initialContainingBlock.Size;
            BoxFragment? rootFragment = null;
            List<BoxFragment>? fixedBoxes = null;
            List<BoxFragment>? absoluteBoxes = null;
            foreach (var child in initialContainingBlock.ChildrenSpan)
            {
                if (child is not BoxFragment box)
                    continue;

                if (!box.Box.IsOutOfFlow)
                {
                    rootFragment ??= box;
                }
                else if (box.Box.IsFixedToViewport)
                {
                    (fixedBoxes ??= []).Add(box);
                }
                else
                {
                    (absoluteBoxes ??= []).Add(box);
                }
            }

            // (the strip is as tall as the margin box of the root element: the pages end with it)
            var icbLogical = writingDirection.ToLogical(icbSize);
            var stripBlockSize = 0d;
            var stripBuilder = new FragmentBuilder(writingDirection);
            if (rootFragment is not null)
            {
                var (_, bottom, _) = FragmentationFlow.GetBlockRange(rootFragment, icbSize, writingDirection, parentTop: 0);
                stripBlockSize = Math.Max(stripBlockSize, bottom + Math.Max(0, writingDirection.ToLogical(rootFragment.Margin).BlockEnd));
                stripBuilder.AddChild(rootFragment, writingDirection.ToLogical(rootFragment.Offset - rootFragment.RelativeOffset, icbSize, rootFragment.Size));
            }

            // The absolutely positioned boxes whose containing block is the initial containing block are on the page of their
            // position in the content, unbroken, when they fit in the rest of the page (with their descendants); the other ones
            // are fragmented apart, from their position in the initial containing block (the pages they reach are added)
            List<BoxFragment>? placedBoxes = null;
            if (absoluteBoxes is not null)
            {
                foreach (var box in absoluteBoxes)
                {
                    if (fragmentedBoxes?.Contains(box.Box) != true)
                    {
                        (placedBoxes ??= []).Add(box);
                        continue;
                    }

                    stripBuilder.AddChild(box, writingDirection.ToLogical(box.Offset - box.RelativeOffset, icbSize, box.Size));
                }
            }

            var stripSize = writingDirection.ToPhysical(new LogicalSize(icbLogical.Inline, stripBlockSize));
            var children = stripBuilder.Build(stripSize) ?? [];
            var initialContainingBlockFragment = new BoxFragment(viewport, writingDirection.ToPhysical(icbLogical), default, default, default, children: null);
            var rules = new PageBreakRules { InitialContainingBlock = initialContainingBlockFragment };
            var flow = FragmentationFlow.Collect(children, stripSize, writingDirection, spannerBoxes: null, viewport, separatesOutOfFlow: true, floatPushes, rules, stripRelayout);
            flow.SlicesMonolithicContent = true;

            // (the tables that start in a previous segment repeat their header and footer as decided there; the breaker decides it
            // again for the tables that start in this segment)
            if (segment?.RepeatedSectionsDisabled is { } disabledSections && flow.AllRepeatedSections is { } repeatedSections)
            {
                foreach (var (tableGrid, sections) in repeatedSections)
                {
                    if (disabledSections.TryGetValue(tableGrid.Box, out var disabled))
                    {
                        sections.IsHeaderDisabled = disabled.IsHeaderDisabled;
                        sections.IsFooterDisabled = disabled.IsFooterDisabled;
                    }
                }
            }

            // The start of the segment in this strip: the content before it is in a virtual first fragmentainer (the pages of
            // the previous segments), whose fragments are dropped
            var segmentStart = 0d;
            if (rowAnchor is not null)
            {
                // (the items of the broken row whose content after the break starts before the others are moved down to them, by
                // new layouts of the strip; when they are not in one row or do not reach the same position, the previous segment
                // is laid out again without ending at the break, see LayoutPages)
                // (a row in the columns of a multi-column container is measured in the strip of its columns)
                var rowColumns = segment!.Anchor.ColumnContent is null ? null : FindResumableColumns(flow, children, stripSize, writingDirection, segment.Anchor);
                if ((segment.Anchor.ColumnContent is not null && rowColumns is null)
                    || (rowColumns is var (columnsOfRow, _)
                        ? MeasureRowBreak(columnsOfRow.Children, columnsOfRow.StripSize, columnsOfRow.WritingDirection, rowAnchor, anchorPushes)
                        : MeasureRowBreak(children, stripSize, writingDirection, rowAnchor, anchorPushes)) is not var (rowItems, rowStart))
                {
                    isRejected = true;
                    ClearNestedColumnStates(context);
                    return null;
                }

                {
                    var aligned = true;
                    foreach (var (box, itemPosition) in rowItems)
                    {
                        if (rowStart - itemPosition > BreakAnchorEpsilon)
                        {
                            aligned = false;
                            if (alignmentPasses < MaxRowAlignmentPasses)
                            {
                                var pushes = anchorPushes!;
                                var push = pushes.GetValueOrDefault(box) + rowStart - itemPosition;
                                pushes[box] = push;
                                foreach (var item in rowAnchor.Items!)
                                {
                                    if (ReferenceEquals(item.Box, box) && item.PushedWith is { } pushedWith)
                                    {
                                        foreach (var mate in pushedWith)
                                        {
                                            pushes[mate] = push;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (!aligned && alignmentPasses < MaxRowAlignmentPasses)
                    {
                        alignmentPasses++;
                        ClearNestedColumnStates(context);
                        continue;
                    }

                    if (!aligned)
                    {
                        isRejected = true;
                        ClearNestedColumnStates(context);
                        return null;
                    }

                    if (rowColumns is var (rowNested, rowColumnsStart))
                    {
                        if (!ResumeColumns(rowNested, Math.Max(0, rowStart), isInRows: true))
                        {
                            isRejected = true;
                            ClearNestedColumnStates(context);
                            return null;
                        }

                        segmentStart = rowColumnsStart;
                    }
                    else
                    {
                        segmentStart = Math.Max(0, rowStart);
                    }
                }
            }
            else if (segment?.Anchor.Slice is { } slice)
            {
                // (the content that the break slices continues in the page: it must be laid out the same way in this strip, else the
                // previous segment is laid out again without ending at the break, see LayoutPages)
                if (MeasureSlicedContent(children, stripSize, writingDirection, segment.Anchor) is not { } sliced || !sliced.IsSameAs(slice))
                {
                    isRejected = true;
                    ClearNestedColumnStates(context);
                    return null;
                }

                segmentStart = sliced.Top + segment.Anchor.SliceOffset.GetValueOrDefault();
            }
            else if (segment?.Anchor.ColumnContent is { } columnContent)
            {
                // (the columns of the multi-column container continue from the content that starts the first column of the page: it
                // must be found in this strip, else the previous segment is laid out again without ending at the break, see LayoutPages)
                if (FindColumnsStart(flow, children, stripSize, writingDirection, segment.Anchor, columnContent) is not { } columnsStart)
                {
                    isRejected = true;
                    ClearNestedColumnStates(context);
                    return null;
                }

                segmentStart = columnsStart;
            }
            else if (segment is not null)
            {
                segmentStart = FindSegmentStart(children, stripSize, writingDirection, segment.Anchor) is { } anchorStart ? Math.Max(0, anchorStart) : 0;
            }

            var baseIndex = segmentStart > 0 ? 1 : 0;

            // The breaks: the block size of a page depends on its page name and on its side, which depend on the breaks (the
            // content that starts the page, the blank pages inserted before the left and right pages)
            var firstName = rootFragment is null ? "" : segment is null ? rules.GetStartPageValue(rootFragment) : FindPageStart(rules, children, stripSize, writingDirection, segmentStart).Name;
            var firstGeometry = GetPageGeometry(geometries, new PageSelection(firstName, IsFirst: segment is null, IsLeft: segment is null ? rightToLeft : !segment.Previous.IsLeft, IsBlank: false));
            var sizes = new List<double> { writingDirection.ToLogical(firstGeometry.LayoutAreaSize).Block };
            var breaks = new List<FragmentainerBreak>();
            FlowMap map;
            List<(ContentPage Content, PageSelection Selection, bool HasContent)> sequence;
            for (var iteration = 0; ; iteration++)
            {
                map = new FlowMap();
                if (baseIndex != 0)
                {
                    map.Add(double.NegativeInfinity, 0, 0, isBreak: true);
                }

                ColumnBreaker.FindBreaks(flow, segmentStart, flow.ContentEnd, new FragmentainerSizes([.. sizes], 1), breaks, out _, out _, map, baseIndex);
                sequence = GetPageSequence(rules, children, stripSize, writingDirection, breaks, rootFragment, rightToLeft, segmentStart, segment?.Previous);
                var computed = new List<double>();
                foreach (var (_, selection, hasContent) in sequence)
                {
                    if (hasContent)
                    {
                        computed.Add(writingDirection.ToLogical(GetPageGeometry(geometries, selection).LayoutAreaSize).Block);
                    }
                }

                var stable = true;
                for (var i = 0; i < computed.Count; i++)
                {
                    stable &= Math.Abs(computed[i] - sizes[Math.Min(i, sizes.Count - 1)]) < 0.001;
                }

                if (stable || iteration >= 4)
                    break;

                sizes = computed;
            }

            // The page and the offset of an absolutely positioned box of the initial containing block: a box placed by its insets
            // in the block axis is in the initial containing block, the page area of the first page (the pages follow it by
            // their block sizes, whatever the breaks of the content), a box at its static position or anchored follows the
            // content at its position
            (int Index, double Offset) MapPlacedBox(BoxFragment box, double position)
            {
                var anchor = box.Box.Style.Layout.Anchor;
                if (segment is not null || PageBreakRules.IsAtStaticBlockPosition(box) || box.Box.Style.PositionBaseStyle is not null || anchor.UsesAnchorFunctions
                    || anchor.PositionArea is not null || anchor.PositionAnchor is PositionAnchorKind.Auto or PositionAnchorKind.Name or PositionAnchorKind.MatchParent)
                {
                    var placement = map.Map(position);
                    var breakIndex = placement.Index - baseIndex - 1;
                    if (PageBreakRules.IsAtStaticBlockPosition(box) && breakIndex >= 0 && breakIndex < breaks.Count && position >= breaks[breakIndex].Position - 0.01
                        && FindBreakAnchor(children, stripSize, writingDirection, breaks[breakIndex].Position, parentTop: 0) is { } breakAnchor
                        && IsBeforeInTreeOrder(box.Box, breakAnchor.Box) && map.GetOffsetIn(placement.Index - 1, position) is var previousOffset
                        && previousOffset <= sizes[Math.Clamp(breakIndex, 0, sizes.Count - 1)] + 0.01)
                    {
                        // (a box whose static position is before the box after a break, in its block-start margin preserved after a
                        // forced break: as in Blink, it is in the page before the break)
                        return (placement.Index - 1, previousOffset);
                    }

                    return placement;
                }

                var pageIndex = 0;
                while (pageIndex < sizes.Count - 1 && position >= sizes[pageIndex] - 0.01)
                {
                    position -= sizes[pageIndex];
                    pageIndex++;
                }

                return (pageIndex, position);
            }

            // (a box placed on its page that does not fit in the rest of the page is fragmented apart by a new layout)
            var hasNewFragmentedBox = false;
            if (placedBoxes is not null && pass < FloatPusher.MaxPasses)
            {
                foreach (var box in placedBoxes)
                {
                    var (top, bottom, _) = FragmentationFlow.GetBlockRange(box, icbSize, writingDirection, parentTop: 0);
                    var (index, offset) = MapPlacedBox(box, top);
                    if (index < baseIndex)
                        continue;

                    // (also a box whose content has a forced page break: it is broken there)
                    var extent = GetSubtreeBlockEnd(box, writingDirection, top, bottom) - top;
                    if ((offset + extent > sizes[Math.Clamp(index - baseIndex, 0, sizes.Count - 1)] + 0.01 || (index - baseIndex >= breaks.Count && top > flow.ContentEnd + 0.01 && offset > sizes[^1] - 0.01) || ContainsForcedPageBreak(rules, box, PageBreakRules.IsAtStaticBlockPosition(box)))
                        && (fragmentedBoxes ??= []).Add(box.Box))
                    {
                        hasNewFragmentedBox = true;
                    }
                }
            }

            if (hasNewFragmentedBox)
            {
                ClearNestedColumnStates(context);
                continue;
            }

            // The fragmentainers: the page areas of the pages that have content (after the virtual one of the previous segments)
            var fragmentainers = new List<Fragmentainer>();
            var blockSizes = new List<double>();
            if (baseIndex != 0)
            {
                fragmentainers.Add(new Fragmentainer(double.NegativeInfinity, segmentStart, segmentStart, 0, LogicalOffset.Zero));
                blockSizes.Add(segmentStart);
            }

            for (var i = 0; i <= breaks.Count; i++)
            {
                var start = i == 0 ? segmentStart : breaks[i - 1].Position;
                var displayOffset = i > 0 ? breaks[i - 1].DisplayOffset : segment is null ? 0 : ColumnBreaker.GetDisplayOffsetAt(flow, segmentStart);
                var blockSize = sizes[Math.Min(i, sizes.Count - 1)];
                blockSizes.Add(blockSize);

                // (a box broken at the end of a page extends to the end of the page area, also when its content overflows it)
                var end = i < breaks.Count ? breaks[i].Position : double.PositiveInfinity;
                var fragmentainerBlockSize = i < breaks.Count ? Math.Max(blockSize, breaks[i].ContentEnd - start + displayOffset) : blockSize;
                fragmentainers.Add(new Fragmentainer(start, end, fragmentainerBlockSize, displayOffset, LogicalOffset.Zero));
            }

            // The absolutely positioned boxes whose containing block is in the strip, in the sequence of the pages: the ones that
            // end after the in-flow content add pages
            // (the pages they add alternate between left and right pages, with the block sizes of their page areas)
            if (flow.OutOfFlowItems is not null || flow.HasNestedColumns)
            {
                var placed = ColumnBreaker.PlaceOutOfFlow(flow, map, blockSizes, blockSizes[^1], [0], addFragmentainer: AddPage);
                for (var count = Math.Max(placed.Count, placed.NestedCount); fragmentainers.Count < count;)
                {
                    AddPage();
                }
            }

            void AddPage()
            {
                var last = sequence[^1];
                var selection = last.Selection with { IsFirst = false, IsLeft = !last.Selection.IsLeft, IsBlank = false };
                var blockSize = writingDirection.ToLogical(GetPageGeometry(geometries, selection).LayoutAreaSize).Block;
                fragmentainers.Add(new Fragmentainer(double.PositiveInfinity, double.PositiveInfinity, blockSize, 0, LogicalOffset.Zero));
                blockSizes.Add(blockSize);
                sequence.Add((last.Content, selection, true));
            }

            // The floats that do not fit are pushed to the next page, and the items of the grid lanes containers fragmented in their
            // stacking axis are placed again in the pages (see GridLanesFragmentation), by new layouts of the strip
            if (stripRelayout.UpdateBrokenBoxes(flow, map, children, stripSize, writingDirection, blockSizes) || stripRelayout.UpdateNestedAnchors(flow, map))
            {
                // The broken boxes (and the anchor-positioned boxes of the anchors in the nested multi-column containers) are laid
                // out again (see StripRelayout): the floats are pushed again from the start
                floatPushes = null;
                lanesBreaks = null;
                pass = -1;
                ClearNestedColumnStates(context);
                continue;
            }

            if (pass < FloatPusher.MaxPasses)
            {
                var pushesFloats = FloatPusher.Update(context, flow, map, blockSizes, fragmentainers, ref floatPushes);
                if (GridLanesFragmentation.Update(flow, children, stripSize, writingDirection, map, blockSizes, ref lanesBreaks) || pushesFloats
                    || stripRelayout.UpdateFloatAvoiders(flow, map, children, stripSize, writingDirection) || stripRelayout.UpdateFloatExclusionEnds(context, flow))
                {
                    ClearNestedColumnStates(context);
                    continue;
                }
            }

            // The boxes fixed to the viewport are laid out in the initial containing block of the first segment (the page area of
            // the first page), as in Blink
            var fixedPlacements = segment?.FixedBoxes;
            if (segment is null && fixedBoxes is not null)
            {
                fixedPlacements = new List<(BoxFragment Fragment, LogicalOffset Offset)>(fixedBoxes.Count);
                foreach (var box in fixedBoxes)
                {
                    fixedPlacements.Add((box, writingDirection.ToLogical(box.Offset - box.RelativeOffset, icbSize, box.Size)));
                }
            }

            // The pages that may end the segment: the pages whose page area has another inline size than the strip and that start at
            // a break where the content after it can be found in a strip of their inline size (see FindBreakAnchor: their content is
            // laid out again in a strip of their inline size; the content of the other pages is laid out in the inline size of the
            // strip). (They are found before the slicer moves the fragments of the strip to the pages.)
            List<(int SequenceIndex, int Index, BreakAnchor Anchor, PhysicalSize AreaSize, double Position)>? segmentEnds = null;
            var contentIndex = 0;
            for (var i = 0; i < sequence.Count; i++)
            {
                var (_, selection, hasContent) = sequence[i];
                if (!hasContent)
                    continue;

                var areaSize = GetPageGeometry(geometries, selection).LayoutAreaSize;
                if (contentIndex > 0 && contentIndex <= breaks.Count && Math.Abs(writingDirection.ToLogical(areaSize).Inline - icbLogical.Inline) > 0.01
                    && rejectedBreaks?.Contains(breaks[contentIndex - 1].Position) != true
                    && !ContinuesBeforeBreak(flow, breaks[contentIndex - 1].Position, baseIndex + contentIndex)
                    && FindSegmentAnchor(children, stripSize, writingDirection, breaks[contentIndex - 1].Position) is { } found
                    && (found.IsInColumns ? ResolveColumnAnchor(found, flow, children, stripSize, writingDirection, baseIndex + contentIndex) : found) is { } anchor)
                {
                    (segmentEnds ??= []).Add((i, baseIndex + contentIndex, anchor, areaSize, breaks[contentIndex - 1].Position));
                    if (!anchor.IsInRows && anchor.ColumnContent?.IsInRows != true)
                        break;
                }

                contentIndex++;
            }

            // The pages of the absolutely positioned boxes placed at their position (before the slicer places the fragments of the
            // strip in the pages)
            List<(BoxFragment Box, int Index, double Offset)>? placedBoxPages = null;
            if (placedBoxes is not null)
            {
                placedBoxPages = new(placedBoxes.Count);
                foreach (var box in placedBoxes)
                {
                    var (boxIndex, boxOffset) = MapPlacedBox(box, writingDirection.ToLogical(box.Offset - box.RelativeOffset, icbSize, box.Size).Block);
                    placedBoxPages.Add((box, boxIndex, boxOffset));
                }
            }

            // The fragments of the strip in the page areas
            var slicer = new FragmentSlicer(flow, map, fragmentainers, writingDirection);
            var placedFragments = slicer.Slice(children, stripSize, spannerBoxes: null);
            ClearNestedColumnStates(context);

            // The page that ends the segment: the first of these pages whose content starts with the anchor (see ResolveAnchorOfPage)
            var segmentEnd = sequence.Count;
            PageSegmentStart? next = null;
            if (segmentEnds is not null)
            {
                foreach (var (sequenceIndex, index, candidate, areaSize, breakPosition) in segmentEnds)
                {
                    var anchor = candidate;
                    if (candidate.IsInRows)
                    {
                        if (ResolveAnchorOfPage(candidate, placedFragments[index - 1], placedFragments[index], writingDirection) is not { } resolved)
                            continue;

                        anchor = resolved;
                    }
                    else if (candidate.ColumnContent is { IsInRows: true } columnContent)
                    {
                        // (a break between the rows of a container in the columns of a multi-column container, or in its only row that
                        // the break crosses: the rows are checked against the content of the pages, the columns of the container)
                        if (ResolveAnchorOfPage(columnContent, placedFragments[index - 1], placedFragments[index], writingDirection) is not { } resolved)
                            continue;

                        anchor = candidate with { ColumnContent = resolved };
                    }

                    // (a blank page before the page is inserted again by the next segment)
                    segmentEnd = sequenceIndex > 0 && !sequence[sequenceIndex - 1].HasContent ? sequenceIndex - 1 : sequenceIndex;
                    next = new PageSegmentStart(anchor, areaSize, sequence[segmentEnd - 1].Selection) { FixedBoxes = fixedPlacements, RepeatedSectionsDisabled = GetRepeatedSectionsDisabled(flow), BreakPosition = breakPosition };
                    break;
                }
            }

            contentIndex = 0;
            for (var sequenceIndex = 0; sequenceIndex < segmentEnd; sequenceIndex++)
            {
                var (_, selection, hasContent) = sequence[sequenceIndex];
                var geometry = GetPageGeometry(geometries, selection);
                var areaSize = geometry.LayoutAreaSize;
                var builder = new FragmentBuilder(writingDirection);
                if (hasContent)
                {
                    var index = baseIndex + contentIndex;
                    foreach (var (fragment, offset) in placedFragments[index])
                    {
                        builder.AddChild(fragment, offset);
                    }

                    // The absolutely positioned boxes of the initial containing block at their static position or anchored are on
                    // the page where the content at their position is, unbroken
                    if (placedBoxPages is not null)
                    {
                        foreach (var (box, boxIndex, offset) in placedBoxPages)
                        {
                            var logical = writingDirection.ToLogical(box.Offset - box.RelativeOffset, icbSize, box.Size);
                            if (Math.Clamp(boxIndex, 0, fragmentainers.Count - 1) == index)
                            {
                                builder.AddChild(box, new LogicalOffset(logical.Inline, boxIndex == index ? offset : logical.Block - fragmentainers[index].Start + fragmentainers[index].DisplayOffset));
                            }
                        }
                    }

                    // https://drafts.csswg.org/css-position-3/#fixed-pos: "the box is positioned relative to the page area of each
                    // page" and repeated on every page
                    if (fixedPlacements is not null)
                    {
                        foreach (var (box, logical) in fixedPlacements)
                        {
                            builder.AddChild(pages.Count == 0 ? box : box.CloneSubtree(), logical);
                        }
                    }

                    contentIndex++;
                }

                // (the fragments of a segment followed by another one are copied: the layout of the next strip may return and move
                // the fragments that are laid out the same way)
                var content = new BoxFragment(viewport, areaSize, default, default, default, builder.Build(areaSize));
                if (next is not null)
                {
                    content = (BoxFragment)content.CloneSubtree();
                }

                pages.Add(new LayoutPage
                {
                    Index = pages.Count,
                    Name = selection.Name,
                    IsBlank = !hasContent,
                    IsLeft = selection.IsLeft,
                    Geometry = geometry,
                    Content = content,
                });
                selections.Add(selection);
            }

            return next;
        }
    }

    /// <summary>
    /// Returns whether content before a break of the strip continues in the page after it: a parallel flow that ends before the
    /// break in the strip (the cells of a table row, the items of a grid row or of a flex line) ends in the page or after it, its
    /// own breaks being before the break of the strip (e.g. its lines do not fill the pages). The content after the break does
    /// not start that page: it is not laid out again in another inline size from there.
    /// </summary>
    private static bool ContinuesBeforeBreak(FragmentationFlow flow, double position, int index)
    {
        if (flow.Groups is not { } groups)
            return false;

        foreach (var group in groups)
        {
            if (group.Start >= position - BreakAnchorEpsilon)
                continue;

            foreach (var item in group.Items)
            {
                if (item.End <= position + BreakAnchorEpsilon && item.EndIndex >= index)
                    return true;
            }
        }

        return false;
    }

    /// <summary>The number of layouts of a strip that move the content after the break of the items of a broken row to the same position (see <see cref="MeasureRowBreak"/>).</summary>
    private const int MaxRowAlignmentPasses = 4;

    /// <summary>
    /// Returns the containers whose flex lines or rows depend on the inline size and whose items after a break start a new flex
    /// line or row in the strip of a segment (see <see cref="LayoutContext.RowResumes"/>): the container of the anchor, and the
    /// containers of the rows broken in the items of a broken row (see <see cref="BreakAnchor.NestedRows"/>), or null.
    /// </summary>
    private static Dictionary<ContainerBox, RowResume>? GetRowResumes(BreakAnchor anchor)
    {
        if (anchor.ColumnContent is { } content)
            return GetRowResumes(content);

        Dictionary<ContainerBox, RowResume>? resumes = null;
        Add(anchor);
        if (anchor.NestedRows is { } nestedRows)
        {
            foreach (var nestedRow in nestedRows)
            {
                Add(nestedRow);
            }
        }

        return resumes;

        void Add(BreakAnchor row)
        {
            if (row is { Container: { } container } && (row.Following is not null || row.RowItems is not null) && (IsFlexLineContainer(container) || IsAutoRepeatGridContainer(container)))
            {
                (resumes ??= [])[container] = new RowResume(row.Following ?? [], row.RowItems) { Preceding = row.Preceding };
            }
        }
    }

    /// <summary>Returns the items of a broken row and of the rows broken in its items (see <see cref="LayoutContext.ResumedRowItems"/>).</summary>
    private static HashSet<ContainerBox>? GetResumedRowItems(BreakAnchor anchor)
    {
        if (anchor.NestedRows is not { } nestedRows)
            return anchor.RowItems;

        var items = new HashSet<ContainerBox>(anchor.RowItems ?? []);
        foreach (var nestedRow in nestedRows)
        {
            items.UnionWith(nestedRow.RowItems ?? []);
        }

        return items;
    }

    /// <summary>Returns the block containers whose line boxes start at a position of their inline content in the strip of a segment (see <see cref="LayoutContext.InlineResumes"/>), or null.</summary>
    private static Dictionary<ContainerBox, InlineResume>? GetInlineResumes(BreakAnchor anchor)
    {
        if (anchor.Resume is { } resume)
            return new() { [anchor.Box] = resume };

        if (anchor.ColumnContent is { } content)
            return GetInlineResumes(content);

        Dictionary<ContainerBox, InlineResume>? resumes = null;
        if (anchor.Items is { } items)
        {
            foreach (var item in items)
            {
                if (item.Resume is { } itemResume)
                {
                    (resumes ??= [])[item.Box] = itemResume;
                }
            }
        }

        return resumes;
    }

    /// <summary>Returns whether the header and the footer of the tables of a flow are not repeated (see <see cref="PageSegmentStart.RepeatedSectionsDisabled"/>), or null.</summary>
    private static Dictionary<ContainerBox, (bool IsHeaderDisabled, bool IsFooterDisabled)>? GetRepeatedSectionsDisabled(FragmentationFlow flow)
    {
        if (flow.AllRepeatedSections is not { Count: > 0 } repeatedSections)
            return null;

        var result = new Dictionary<ContainerBox, (bool IsHeaderDisabled, bool IsFooterDisabled)>(repeatedSections.Count);
        foreach (var (tableGrid, sections) in repeatedSections)
        {
            result[tableGrid.Box] = (sections.IsHeaderDisabled, sections.IsFooterDisabled);
        }

        return result;
    }

    /// <summary>Returns the first fragment of a box in the strip and its block-start border edge (in the block axis of the strip), or null.</summary>
    private static (BoxFragment Fragment, double Top)? FindBoxFragment(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, ContainerBox box, double parentTop)
    {
        foreach (var child in children)
        {
            if (child is not BoxFragment childBox)
                continue;

            var (top, _, _) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            if (ReferenceEquals(childBox.Box, box))
                return (childBox, top);

            if (!childBox.Box.IsOutOfFlow && childBox.WritingDirection.BlockStart == writingDirection.BlockStart && FindBoxFragment(childBox.ChildrenSpan, childBox.Size, writingDirection, box, top) is { } result)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Returns the position in the strip where the content of a segment starts (see <see cref="BreakAnchor"/>): the block-start
    /// border edge of the anchor box, offset by the break; or, at a break between the rows of a container, the block-start edge
    /// of its first child after the break offset by the break, but not before the end of its children before the break (they
    /// may end later in another inline size, e.g. with another alignment). Null when the anchor is not in the strip.
    /// </summary>
    private static double? FindSegmentStart(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, BreakAnchor anchor)
    {
        if (anchor.Container is null || anchor.Following is null)
            return FindBoxFragment(children, parentSize, writingDirection, anchor.Box, parentTop: 0) is var (_, top) ? top + anchor.Offset : null;

        if (FindBoxFragment(children, parentSize, writingDirection, anchor.Container, parentTop: 0) is not var (container, containerTop))
            return null;

        var firstTop = double.PositiveInfinity;
        var previousEnd = double.NegativeInfinity;
        Visit(container, containerTop);
        return double.IsPositiveInfinity(firstTop) ? null : Math.Max(previousEnd, firstTop + anchor.Offset);

        // (the items of a subgrid of the rows of a grid are the children of the grid, see FindBreakAnchorIn: the subgrid spans the
        // rows of the break)
        void Visit(BoxFragment parent, double parentTop)
        {
            var parentChildren = parent.ChildrenSpan;
            for (var i = 0; i < parent.InFlowChildCount && i < parentChildren.Length; i++)
            {
                if (parentChildren[i] is not BoxFragment child || child.Box.IsOutOfFlow || child.Box.IsOutsideMarker)
                    continue;

                var (top, bottom, _) = FragmentationFlow.GetBlockRange(child, parent.Size, writingDirection, parentTop);
                if (anchor.Following.Contains(child.Box))
                {
                    firstTop = Math.Min(firstTop, top);
                }
                else if (parent.Box.Display.Inside == DisplayInside.Grid && !IsAutoRepeatGridContainer(parent.Box) && IsRowSubgrid(child))
                {
                    Visit(child, top);
                }
                else
                {
                    previousEnd = Math.Max(previousEnd, bottom);
                }
            }
        }
    }

    /// <summary>Returns whether a box precedes another box in the tree order of their elements.</summary>
    private static bool IsBeforeInTreeOrder(ContainerBox box, ContainerBox other)
    {
        return GetNode(box) is { } node && GetNode(other) is { } otherNode && !ReferenceEquals(node, otherNode)
            && (node.CompareDocumentPosition(otherNode) & Node.DocumentPositionFollowing) != 0;

        static Node? GetNode(ContainerBox box)
        {
            for (ContainerBox? current = box; current is not null; current = current.FirstChild as ContainerBox)
            {
                if (current.Element is { } element)
                    return element;
            }

            return null;
        }
    }

    /// <summary>
    /// The content after a break of the strip, found again in a strip of another inline size (see <see cref="FindBreakAnchor"/>):
    /// the box after the break and the offset of the break from its block-start border edge (the margin preserved after a forced
    /// break); with <see cref="Resume"/>, the block container whose line boxes the break separates, the offset of its content box
    /// and the position of its inline content where the line box after the break starts; with <see cref="Container"/>, a
    /// container broken between its rows (table rows, flex items, grid items): <see cref="Following"/> are its children after
    /// the break, <see cref="Box"/> the first of them, and <see cref="Offset"/> the offset of the break from its block-start edge.
    /// </summary>
    private sealed record BreakAnchor(ContainerBox Box, double Offset, InlineResume? Resume = null, ContainerBox? Container = null, HashSet<ContainerBox>? Following = null)
    {
        /// <summary>
        /// The rows before the break of the containers broken between or inside their rows: their content must not continue in the
        /// page after the break (the parallel flow of a row may continue after the end of its row, e.g. a grid item that avoids
        /// breaks inside it, or that overflows), else the content after the break is not found again in another inline size.
        /// </summary>
        public HashSet<ContainerBox>? Preceding { get; init; }

        /// <summary>
        /// Whether the break is inside a container broken between or inside its rows: the rows are parallel flows, whose breaks
        /// may differ from the break of the strip, so the anchor is checked against the content of the pages (see
        /// <see cref="ResolveAnchorOfPage"/>).
        /// </summary>
        public bool IsInRows { get; init; }

        /// <summary>
        /// The anchors of the items of a row broken by the break, whose items are side by side (a table row, a flex line, a grid
        /// row, see <see cref="FindRowBreakAnchor"/>): the content after the break in each item that continues after it (a box
        /// or a block container resumed at a line box, see <see cref="Resume"/>). <see cref="Container"/> is the box whose row
        /// is broken. Null otherwise.
        /// </summary>
        public IReadOnlyList<BreakAnchor>? Items { get; init; }

        /// <summary>The items of the broken row whose content ends before the break (see <see cref="Items"/>), or null.</summary>
        public HashSet<ContainerBox>? Finished { get; init; }

        /// <summary>The items of the broken row (see <see cref="Items"/>), or null.</summary>
        public HashSet<ContainerBox>? RowItems { get; init; }

        /// <summary>The items of <see cref="RowItems"/> that span the rows before the broken row, which start before it, or null.</summary>
        public HashSet<ContainerBox>? SpanningItems { get; init; }

        /// <summary>
        /// At a break between the rows of a grid in an item of a broken row: the other items of the first row after the break,
        /// moved down with <see cref="Box"/> (see <see cref="LayoutContext.AnchorPushes"/>), or null.
        /// </summary>
        public HashSet<ContainerBox>? PushedWith { get; init; }

        /// <summary>
        /// The rows broken by the break in the items of the broken row (a grid row, a table row, a flex line of an item), or null:
        /// their items are in <see cref="Items"/> (the content after the break of all the items starts at the same position), and
        /// each one keeps the items of its row (<see cref="RowItems"/>, <see cref="Finished"/>, <see cref="SpanningItems"/>), the
        /// items before and after the break of its container (<see cref="Preceding"/>, <see cref="Following"/>), and its
        /// container (see <see cref="LayoutContext.RowResumes"/>, <see cref="LayoutContext.ResumedRowItems"/>).
        /// </summary>
        public IReadOnlyList<BreakAnchor>? NestedRows { get; init; }

        /// <summary>
        /// When the break slices the content after it (a monolithic box, <see cref="Box"/>, or the line box of <see cref="Resume"/>,
        /// taller than the page): the offset of the break from its block-start edge, else null.
        /// </summary>
        public double? SliceOffset { get; init; }

        /// <summary>The geometry of the content that the break slices in the strip of the break (see <see cref="SliceOffset"/>), or null.</summary>
        public SlicedContent? Slice { get; init; }

        /// <summary>
        /// For the line box of <see cref="Resume"/> that the break crosses in an item of a row: the offset of the break from its
        /// block-start edge, else null. The break of the item may be before the line (the line starts the next page), or the break
        /// slices it, a line taller than the page: then it is in both pages, and the anchor is its rest (see
        /// <see cref="ResolveAnchorOfPage"/>, <see cref="SliceOffset"/>).
        /// </summary>
        public double? SlicedLineOffset { get; init; }

        /// <summary>
        /// Whether the break crosses <see cref="Box"/>, a multi-column container whose rows of columns are placed in the pages (see
        /// <see cref="NestedColumns"/>): the page after the break starts with the content of its first column, found in the strip of
        /// the columns of the container (see <see cref="ColumnContent"/>).
        /// </summary>
        public bool IsInColumns { get; init; }

        /// <summary>
        /// For a break inside a multi-column container (see <see cref="IsInColumns"/>): the content after the break in the strip of
        /// its columns (an anchor of that strip, see <see cref="FindBreakAnchor"/>, but not monolithic content that the break
        /// slices), laid out again in the columns of the inline size of the page, or null.
        /// </summary>
        public BreakAnchor? ColumnContent { get; init; }
    }

    /// <summary>
    /// The content that a break slices (see <see cref="BreakAnchor.SliceOffset"/>), in a strip: its block-start edge, the extent
    /// of its content in the inline axis, its block size, the rectangles of the fragments of its content (from the start of its
    /// content, in the axes of the strip: the boxes and the text of a line box, the descendants of a monolithic box, whose border
    /// box may keep its size while its content moves) and, for a line box, the position of the inline content where the next
    /// line box starts (or null). The part of the content before the break is in the previous page: the page of another inline
    /// size starts inside it only when it is laid out the same way in the strip of that inline size, the part after the break
    /// continuing the part before it.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SlicedContent(double Top, double InlineStart, double InlineEnd, double BlockSize, IReadOnlyList<LogicalRect> Contents, InlineResume? NextLine)
    {
        public bool IsSameAs(SlicedContent other)
        {
            return Math.Abs(InlineStart - other.InlineStart) <= BreakAnchorEpsilon && Math.Abs(InlineEnd - other.InlineEnd) <= BreakAnchorEpsilon
                && Math.Abs(BlockSize - other.BlockSize) <= BreakAnchorEpsilon && NextLine == other.NextLine && HasSameContents(other);
        }

        private bool HasSameContents(SlicedContent other)
        {
            if (Contents.Count != other.Contents.Count)
                return false;

            for (var i = 0; i < Contents.Count; i++)
            {
                var (rect, otherRect) = (Contents[i], other.Contents[i]);
                if (Math.Abs(rect.InlineStart - otherRect.InlineStart) > BreakAnchorEpsilon || Math.Abs(rect.BlockStart - otherRect.BlockStart) > BreakAnchorEpsilon
                    || Math.Abs(rect.Size.Inline - otherRect.Size.Inline) > BreakAnchorEpsilon || Math.Abs(rect.Size.Block - otherRect.Size.Block) > BreakAnchorEpsilon)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns whether the content has the size of another one: in the items of a row, the content moves with its item, whose
        /// position in the row depends on the inline size of the row (e.g. the columns of a grid).
        /// </summary>
        public bool HasSameSize(SlicedContent other)
        {
            return Math.Abs(InlineEnd - InlineStart - (other.InlineEnd - other.InlineStart)) <= BreakAnchorEpsilon
                && Math.Abs(BlockSize - other.BlockSize) <= BreakAnchorEpsilon && NextLine == other.NextLine && HasSameContents(other);
        }
    }

    /// <summary>Returns the rectangles of the descendants of a fragment, in the axes of the strip, from an origin (the offset of the fragment from it).</summary>
    private static List<LogicalRect> GetContentRects(Fragment fragment, WritingDirection writingDirection, LogicalOffset offset)
    {
        var rects = new List<LogicalRect>();
        Collect(fragment, offset);
        return rects;

        void Collect(Fragment parent, LogicalOffset parentOffset)
        {
            foreach (var child in parent.ChildrenSpan)
            {
                var rect = writingDirection.ToLogical(child.Rect, parent.Size);
                var childOffset = new LogicalOffset(parentOffset.Inline + rect.Offset.Inline, parentOffset.Block + rect.Offset.Block);
                rects.Add(new LogicalRect(childOffset, rect.Size));
                Collect(child, childOffset);
            }
        }
    }

    /// <summary>
    /// Returns the content after a break of the strip that can be found again in a strip of another inline size (see
    /// <see cref="FindBreakAnchor"/>), with the geometry of the content that it slices (see <see cref="BreakAnchor.Slice"/>), or null.
    /// </summary>
    private static BreakAnchor? FindSegmentAnchor(ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, double position)
    {
        if (FindBreakAnchor(children, stripSize, writingDirection, position, parentTop: 0) is not { } anchor)
            return null;

        // (the content that the break slices in the items of a broken row)
        // (and the lines that the break crosses, which it may slice)
        if (anchor.Items is { } items && items.Any(item => item.SliceOffset is not null || item.SlicedLineOffset is not null))
        {
            var measured = new List<BreakAnchor>(items.Count);
            foreach (var item in items)
            {
                if (item.SliceOffset is null && item.SlicedLineOffset is null)
                {
                    measured.Add(item);
                }
                else if (MeasureSlicedContent(children, stripSize, writingDirection, item) is { } itemSlice)
                {
                    measured.Add(item with { Slice = itemSlice });
                }
                else if (item.SliceOffset is null)
                {
                    measured.Add(item);
                }
                else
                {
                    return null;
                }
            }

            anchor = anchor with { Items = measured };
        }

        if (anchor.SliceOffset is null)
            return anchor;

        return MeasureSlicedContent(children, stripSize, writingDirection, anchor) is { } slice ? anchor with { Slice = slice } : null;
    }

    /// <summary>
    /// Returns the anchor of a break inside a multi-column container (see <see cref="BreakAnchor.IsInColumns"/>) with the content
    /// that starts the first column of the page after the break (see <see cref="BreakAnchor.ColumnContent"/>), or null: the
    /// columns of the page continue the columns of the previous page, and that content can be found again in columns of another
    /// inline size.
    /// </summary>
    /// <param name="index">The index of the fragmentainer of the page after the break.</param>
    private static BreakAnchor? ResolveColumnAnchor(BreakAnchor anchor, FragmentationFlow flow, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, int index)
    {
        if (FindBoxFragment(children, stripSize, writingDirection, anchor.Box, parentTop: 0) is not var (fragment, _)
            || flow.GetNestedColumns(fragment) is not { Nested: { } nested } || !IsResumableColumns(nested))
        {
            return null;
        }

        // (the first column of the page, which continues the last column of the previous page)
        var columns = nested.Columns;
        var first = -1;
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].OuterIndex == index && !double.IsInfinity(columns[i].Start))
            {
                first = i;
                break;
            }
        }

        // (a column spanner that starts the page, before its first column: the page starts with the spanner, after the columns of
        // the previous page)
        var spanners = nested.Flow.Spanners;
        double position;
        if (nested.Spanners.Find(placed => placed.Map.Map(0).Index == index) is { } startingSpanner
            && spanners.FindIndex(spanner => ReferenceEquals(spanner.Fragment, startingSpanner.Fragment)) is var startingIndex and >= 0
            && (first < 0 || columns[first].Start >= spanners[startingIndex].MarginBoxEnd - BreakAnchorEpsilon))
        {
            position = spanners[startingIndex].MarginBoxStart;
            var previous = columns.FindLastIndex(column => !double.IsInfinity(column.Start) && column.Start < position - BreakAnchorEpsilon);
            if (previous < 0 || columns[previous].OuterIndex != index - 1 || Math.Abs(columns[previous].End - position) > BreakAnchorEpsilon)
                return null;

            first = previous + 1;
        }
        else
        {
            if (first <= 0)
                return null;

            // (the column continues the last column of the previous page, or starts the row of columns after a column spanner that
            // ends in the previous page)
            position = columns[first].Start;
            if (columns[first - 1].OuterIndex != index - 1 || Math.Abs(columns[first - 1].End - position) > BreakAnchorEpsilon)
            {
                var spanner = spanners.FindLastIndex(spanner => spanner.MarginBoxEnd <= position + BreakAnchorEpsilon);
                if (spanner < 0 || Math.Abs(spanners[spanner].MarginBoxEnd - position) > BreakAnchorEpsilon
                    || nested.Spanners.Find(placed => ReferenceEquals(placed.Fragment, spanners[spanner].Fragment)) is not { End.Index: var spannerEnd } || spannerEnd != index - 1)
                {
                    return null;
                }
            }
        }

        // (the content after the break is a box, a block container resumed at a line box, the rows after a break between the rows
        // of a container or in its only row that the break crosses, or the items of a broken row, see FindSegmentAnchor; not
        // monolithic content that the break slices)
        if (position <= BreakAnchorEpsilon || ContinuesBeforeBreak(nested.Flow, position, first)
            || FindSegmentAnchor(nested.Children, nested.StripSize, nested.WritingDirection, position) is not { SliceOffset: null, IsInColumns: false } content
            || (!content.IsInRows && CrossesParallelFlow(nested.Flow, position)))
        {
            return null;
        }

        return anchor with { ColumnContent = content };
    }

    /// <summary>
    /// Returns the position in the strip where the content of a segment that starts inside a multi-column container starts (see
    /// <see cref="BreakAnchor.ColumnContent"/>), or null: the rows of columns of the container are placed from the content of
    /// the anchor in its strip (see <see cref="ResumeColumns"/>).
    /// </summary>
    private static double? FindColumnsStart(FragmentationFlow flow, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, BreakAnchor anchor, BreakAnchor content)
    {
        if (FindResumableColumns(flow, children, stripSize, writingDirection, anchor) is not var (nested, start)
            || FindSegmentStart(nested.Children, nested.StripSize, nested.WritingDirection, content) is not { } contentStart)
        {
            return null;
        }

        // (a block container resumed at a line box starts with that line box: its content before it is not laid out again)
        return ResumeColumns(nested, Math.Max(0, contentStart), content.IsInRows) ? start : null;
    }

    /// <summary>
    /// Returns the content of the multi-column container of an anchor inside its columns (see <see cref="BreakAnchor.IsInColumns"/>)
    /// in the strip of a segment, and the position in the strip where the content of the segment starts, or null: the content of
    /// the strip starts inside the container, after its block-start edges (which are in the previous page).
    /// </summary>
    private static (NestedColumns Nested, double Start)? FindResumableColumns(FragmentationFlow flow, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, BreakAnchor anchor)
    {
        if (FindBoxFragment(children, stripSize, writingDirection, anchor.Box, parentTop: 0) is not var (fragment, top)
            || flow.GetNestedColumns(fragment) is not { Nested: { } nested } item || !IsResumableColumns(nested))
        {
            return null;
        }

        var start = top + Math.Max(writingDirection.ToLogical(fragment.Border.Add(fragment.Padding).Add(fragment.Scrollbars)).BlockStart, 2 * BreakAnchorEpsilon);
        return start < item.End - BreakAnchorEpsilon ? (nested, start) : null;
    }

    /// <summary>
    /// Places the rows of columns of a multi-column container from a position of its strip (see <see cref="NestedColumns.ResumeStart"/>),
    /// or returns false: the position is not in a column spanner, nor in a parallel flow unless the content after the break is
    /// in rows (see <see cref="BreakAnchor.IsInRows"/>: the parallel flows are resumed from it).
    /// </summary>
    private static bool ResumeColumns(NestedColumns nested, double position, bool isInRows)
    {
        if ((!isInRows && CrossesParallelFlow(nested.Flow, position)) || nested.Flow.Spanners.Exists(spanner => spanner.MarginBoxStart < position - BreakAnchorEpsilon && spanner.MarginBoxEnd > position + BreakAnchorEpsilon))
            return false;

        nested.ResumeStart = position;
        return true;
    }

    /// <summary>
    /// Returns whether the rows of columns of a multi-column container can be placed from a position of its strip in a page of
    /// another inline size (see <see cref="BreakAnchor.IsInColumns"/>): its rows of columns (between its column spanners) have the
    /// block size that remains in the page (no <c>column-height</c>, no definite or maximum block size), without absolutely
    /// positioned boxes or nested multi-column containers.
    /// </summary>
    private static bool IsResumableColumns(NestedColumns nested)
        => nested.RowBlockSize is null && nested.BlockSize is null && nested.MaxBlockSize is null
            && nested.Flow.OutOfFlowItems is null && !nested.Flow.HasNestedColumns;

    /// <summary>Returns whether a parallel flow of a flow (the items of a grid, of a flex container, the cells of a table) starts before a position and ends after it.</summary>
    private static bool CrossesParallelFlow(FragmentationFlow flow, double position)
    {
        if (flow.Groups is not { } groups)
            return false;

        foreach (var group in groups)
        {
            if (group.Start < position - BreakAnchorEpsilon && group.TrackEnds[^1] > position + BreakAnchorEpsilon)
                return true;
        }

        return false;
    }

    /// <summary>Returns the geometry in a strip of the content that the break of an anchor slices (see <see cref="SlicedContent"/>), or null.</summary>
    private static SlicedContent? MeasureSlicedContent(ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, BreakAnchor anchor)
    {
        if (FindBoxPosition(children, stripSize, writingDirection, anchor.Box, parentTop: 0, parentInlineStart: 0) is not var (box, top, inlineStart))
            return null;

        if (anchor.Resume is not { } resume)
        {
            var size = writingDirection.ToLogical(box.Size);
            return new SlicedContent(top, inlineStart, inlineStart + size.Inline, size.Block, GetContentRects(box, writingDirection, LogicalOffset.Zero), NextLine: null);
        }

        // (the line box that starts at the position of the anchor, the extent of its content, and the start of the next line box)
        var boxChildren = box.ChildrenSpan;
        for (var i = 0; i < boxChildren.Length; i++)
        {
            if (boxChildren[i] is not LineBoxFragment { StartPosition: var lineStart } line || lineStart != resume)
                continue;

            var (lineTop, lineBottom, lineInlineStart) = FragmentationFlow.GetBlockRange(line, box.Size, writingDirection, top);
            var contentStart = double.PositiveInfinity;
            var contentEnd = double.NegativeInfinity;
            foreach (var child in line.ChildrenSpan)
            {
                var rect = writingDirection.ToLogical(child.Rect, line.Size);
                contentStart = Math.Min(contentStart, rect.InlineStart);
                contentEnd = Math.Max(contentEnd, rect.InlineEnd);
            }

            if (contentStart > contentEnd)
            {
                contentStart = 0;
                contentEnd = 0;
            }

            InlineResume? nextLine = null;
            for (var j = i + 1; j < boxChildren.Length; j++)
            {
                if (boxChildren[j] is LineBoxFragment { StartPosition: var nextStart })
                {
                    nextLine = nextStart;
                    break;
                }
            }

            var lineOrigin = inlineStart + lineInlineStart;
            return new SlicedContent(lineTop, lineOrigin + contentStart, lineOrigin + contentEnd, lineBottom - lineTop, GetContentRects(line, writingDirection, new LogicalOffset(-contentStart, 0)), nextLine);
        }

        return null;
    }

    /// <summary>Returns the first fragment of a box in the strip, its block-start border edge and its inline-start border edge (in the axes of the strip), or null.</summary>
    private static (BoxFragment Fragment, double Top, double InlineStart)? FindBoxPosition(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, ContainerBox box, double parentTop, double parentInlineStart)
    {
        foreach (var child in children)
        {
            if (child is not BoxFragment childBox)
                continue;

            var (top, _, inline) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            var inlineStart = parentInlineStart + inline;
            if (ReferenceEquals(childBox.Box, box))
                return (childBox, top, inlineStart);

            if (!childBox.Box.IsOutOfFlow && childBox.WritingDirection.BlockStart == writingDirection.BlockStart
                && FindBoxPosition(childBox.ChildrenSpan, childBox.Size, writingDirection, box, top, inlineStart) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the content after a break of the strip when it can be found again in a strip of another inline size: the content
    /// before it is the same whatever the inline size. That is the case of a break before an in-flow block-level box of the
    /// block flow of the root element (the box and its block containers are laid out the same way in a strip of another inline
    /// size), of a break between two line boxes of a block container of that block flow (its line boxes are laid out from
    /// there in the strip of another inline size, see <see cref="InlineResume"/>), of a break between the rows of a
    /// container whose rows are the same whatever its inline size (see <see cref="IsRowContainer"/>), or inside the only row
    /// that the break crosses when the rows are not side by side, and of a break between the flex lines of a multi-line row
    /// flex container or between the rows of a grid container whose columns depend on its inline size (the items after the break
    /// start a new flex line or a new row in the strip of another inline size, see <see cref="LayoutContext.RowResumes"/>), and of a
    /// break that slices a line box or a monolithic box of that block flow, taller than the page (see
    /// <see cref="BreakAnchor.SliceOffset"/>); else null (a break inside a flex line...).
    /// </summary>
    private static BreakAnchor? FindBreakAnchor(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, double position, double parentTop, BoxFragment? parent = null, bool isParallelFlow = false)
    {
        var inFlowCount = parent is null ? children.Length : Math.Min(parent.InFlowChildCount, children.Length);
        for (var i = 0; i < inFlowCount; i++)
        {
            var child = children[i];
            if (child is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true })
                continue;

            var (top, bottom, _) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            if (top >= position - BreakAnchorEpsilon)
            {
                // A line box after the break: the line boxes of its block container are laid out again from its start
                if (child is LineBoxFragment { StartPosition: { } resume } && parent is not null && top - position <= BreakAnchorEpsilon)
                    return new BreakAnchor(parent.Box, writingDirection.ToLogical(parent.Border.Add(parent.Padding).Add(parent.Scrollbars)).BlockStart, resume);

                // (the box after the break: at the break, or after its block-start margin preserved after a forced break)
                if (child is not BoxFragment box || box.Box.IsInlineBox || box.Box.IsAtomicInline || top - position > Math.Max(0, writingDirection.ToLogical(box.Margin).BlockStart) + BreakAnchorEpsilon)
                    return null;

                return new BreakAnchor(box.Box, position - top);
            }

            if (bottom > position + BreakAnchorEpsilon)
            {
                // (in an item of a row, a line box that the break crosses starts the content of the item after its own break)
                // (or, when the break slices it, a line taller than the page, its rest: see BreakAnchor.SlicedLineOffset)
                if (isParallelFlow && child is LineBoxFragment { StartPosition: { } crossedResume } && parent is not null)
                    return new BreakAnchor(parent.Box, writingDirection.ToLogical(parent.Border.Add(parent.Padding).Add(parent.Scrollbars)).BlockStart, crossedResume) { SlicedLineOffset = position - top };

                // (a line box taller than the page, which the break slices: the line boxes of its block container are laid out again
                // from its start, and the page starts inside it, see SlicedContent)
                if (child is LineBoxFragment { StartPosition: { } slicedResume } && parent is not null)
                    return new BreakAnchor(parent.Box, writingDirection.ToLogical(parent.Border.Add(parent.Padding).Add(parent.Scrollbars)).BlockStart, slicedResume) { SliceOffset = position - top };

                return child is BoxFragment container ? FindBreakAnchorIn(container, writingDirection, position, top, isParallelFlow) : null;
            }
        }

        return null;
    }

    private const double BreakAnchorEpsilon = 0.01;

    /// <summary>Returns the content after a break that crosses a box (see <see cref="FindBreakAnchor"/>), whose block-start border edge is at <paramref name="top"/>.</summary>
    private static BreakAnchor? FindBreakAnchorIn(BoxFragment box, WritingDirection writingDirection, double position, double top, bool isParallelFlow = false)
    {
        if (box.WritingDirection.BlockStart != writingDirection.BlockStart || FragmentationFlow.IsMonolithic(box, writingDirection, scrollContainersAreMonolithic: false))
        {
            // (a multi-column container whose rows of columns are placed in the pages: the page starts in its columns, see
            // BreakAnchor.IsInColumns; not in an item of a row, whose content after the break must start at the same position)
            if (box.Box.Kind == ContainerBoxKind.Normal && box.Box.Style.Layout.IsMultiColumnContainer)
                return !isParallelFlow && box.Box.Style.Layout.WritingMode == writingDirection.WritingMode ? new BreakAnchor(box.Box, position - top) { IsInColumns = true } : null;

            // (a monolithic block-level box, such as an image, taller than the page, which the break slices: the page starts inside
            // it, see SlicedContent)
            if (box.Box.IsInlineBox || box.Box.IsAtomicInline || box.Box.Style.Layout.IsMultiColumnContainer)
                return null;

            return new BreakAnchor(box.Box, position - top) { SliceOffset = position - top };
        }

        // (a block container whose children are block-level, or whose line boxes may be laid out from a position of its content; not
        // a table part, whose children are side by side or are rows)
        if (box.Box.Kind is ContainerBoxKind.Normal && (!box.Box.Display.IsInternal || box.Box.Display.Internal == DisplayInternal.TableCell)
            && box.Box.Display.Inside is DisplayInside.Flow or DisplayInside.FlowRoot && !box.Box.Style.Layout.IsMultiColumnContainer)
        {
            return FindBreakAnchor(box.ChildrenSpan, box.Size, writingDirection, position, top, box, isParallelFlow);
        }

        bool rowsSideBySide;
        var isSingleRow = false;
        if (!IsRowContainer(box.Box, out rowsSideBySide))
        {
            // (a table row, a single-line row flex container: their children are the items of one row)
            if (!IsSingleRowContainer(box.Box))
                return null;

            isSingleRow = true;
            rowsSideBySide = true;
        }

        // The rows before the break, the ones after it and the one that the break crosses
        var children = box.ChildrenSpan;
        BoxFragment? crossed = null;
        var crossedTop = 0d;
        var crossedCount = 0;
        List<(BoxFragment Item, double Top)>? crossedItems = null;
        HashSet<ContainerBox>? following = null;
        HashSet<ContainerBox>? preceding = null;
        HashSet<ContainerBox>? spanning = null;
        BoxFragment? first = null;
        var firstTop = double.PositiveInfinity;

        // (the items of a grid and of a flex container that wraps are in rows, their tracks: the items of the row that the break
        // crosses are the items of that track, also the ones that end before the break or start after it, e.g. aligned in it)
        var itemTracks = rowsSideBySide && !isSingleRow ? GetItemTracks(box, position - top) : null;
        for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
        {
            if (children[i] is not BoxFragment child || child.Box.IsOutOfFlow || child.Box.IsOutsideMarker)
                continue;

            var (childTop, childBottom, _) = FragmentationFlow.GetBlockRange(child, box.Size, writingDirection, top);
            if (isSingleRow)
            {
                (crossedItems ??= []).Add((child, childTop));
            }
            else if (itemTracks is { } tracks)
            {
                if (!ClassifyTrackItem(child, childTop, tracks, box))
                    return null;
            }
            else if (childTop >= position - BreakAnchorEpsilon)
            {
                (following ??= []).Add(child.Box);
                if (childTop < firstTop)
                {
                    first = child;
                    firstTop = childTop;
                }
            }
            else if (childBottom > position + BreakAnchorEpsilon)
            {
                crossed = child;
                crossedTop = childTop;
                crossedCount++;
                (crossedItems ??= []).Add((child, childTop));
            }
            else if (child.Box.Display.Internal is not (DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup))
            {
                // (the header and the footer of a table are repeated in the pages)
                (preceding ??= []).Add(child.Box);
            }
        }

        // (the items of a grid whose rows depend on its inline size are placed again after the break in the strip of another inline
        // size: they must be in their order)
        if (itemTracks is not null && IsAutoRepeatGridContainer(box.Box) && !IsPlacedInOrder(box, preceding, crossedItems, following))
            return null;

        // A break between the rows: the rows after it start the content
        if (crossedCount == 0 && !isSingleRow)
        {
            if (first is null)
                return null;

            // (the items of the first row of a grid after the break move down with the first of them, see BreakAnchor.PushedWith)
            HashSet<ContainerBox>? pushedWith = null;
            if (itemTracks is { } firstTracks && box.Box.Display.Inside == DisplayInside.Grid)
            {
                for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
                {
                    if (children[i] is BoxFragment child && !ReferenceEquals(child, first) && following!.Contains(child.Box) && firstTracks.Items.TryGetValue(child, out var childTrack) && childTrack.Start == firstTracks.After)
                    {
                        (pushedWith ??= []).Add(child.Box);
                    }
                }
            }

            return new BreakAnchor(first.Box, position - firstTop, Container: box.Box, Following: following) { Preceding = preceding, IsInRows = true, PushedWith = pushedWith };
        }

        // A break inside a row whose items are side by side: the content after it in each item starts the content
        if (rowsSideBySide)
        {
            if (crossedItems is null)
                return null;

            // (without the tracks of the items, an item after the break that overlaps the crossed items may be an item of their row
            // aligned after the break: its position depends on the block size of the row)
            if (!isSingleRow && itemTracks is null && following is not null)
            {
                var crossedBottom = double.NegativeInfinity;
                foreach (var (item, itemTop) in crossedItems)
                {
                    crossedBottom = Math.Max(crossedBottom, itemTop + writingDirection.ToLogical(item.Size).Block);
                }

                foreach (var child in children[..Math.Min(box.InFlowChildCount, children.Length)])
                {
                    if (child is BoxFragment followingChild && following.Contains(followingChild.Box)
                        && FragmentationFlow.GetBlockRange(followingChild, box.Size, writingDirection, top).Top < crossedBottom - BreakAnchorEpsilon)
                        return null;
                }
            }

            return FindRowBreakAnchor(box, crossedItems, following, preceding, spanning, writingDirection, position);
        }

        // A break inside the only row that it crosses, when the rows are stacked: the content after it in that row starts the
        // content (the rows before it end before it, the rows after it start after it)
        if (crossedCount != 1 || FindBreakAnchorIn(crossed!, writingDirection, position, crossedTop, isParallelFlow) is not { SliceOffset: null, IsInColumns: false } inner)
            return null;

        if (preceding is not null && inner.Preceding is not null)
        {
            preceding.UnionWith(inner.Preceding);
        }

        // (the rows after it start after the content after the break of the items of a broken row: a cell that spans them does
        // not end its row, see MeasureRowBreak)
        var innerFollowing = inner.Following;
        if (inner.Items is not null && following is not null)
        {
            innerFollowing = innerFollowing is null ? following : [.. innerFollowing, .. following];
        }

        return inner with { Preceding = preceding ?? inner.Preceding, IsInRows = true, Following = innerFollowing };

        // Classifies an item of a grid or of a multi-line flex container in the rows before the break, after it, or of the row
        // that the break crosses, by its tracks (see GetItemTracks); false when it cannot be found again after the break
        bool ClassifyTrackItem(BoxFragment child, double childTop, (Dictionary<BoxFragment, (int Start, int End)> Items, int Crossed, int After) tracks, BoxFragment grid)
        {
            if (!tracks.Items.TryGetValue(child, out var itemTrack))
                return false;

            var isAutoRepeat = IsAutoRepeatGridContainer(grid.Box);
            if (itemTrack.End < tracks.Crossed || (tracks.Crossed < 0 && itemTrack.End < tracks.After))
            {
                (preceding ??= []).Add(child.Box);
                return true;
            }

            if (itemTrack.Start > tracks.Crossed && itemTrack.Start >= tracks.After)
            {
                (following ??= []).Add(child.Box);
                if (childTop < firstTop)
                {
                    first = child;
                    firstTop = childTop;
                }

                return true;
            }

            // (the items of a subgrid whose rows are the rows of a grid whose rows do not depend on its inline size are in the
            // rows of the grid: they are classified by the rows of the subgrid, the subgrid itself continues with them)
            if (grid.Box.Display.Inside == DisplayInside.Grid && !isAutoRepeat && IsRowSubgrid(child))
            {
                if (GetItemTracks(child, position - childTop) is not { } subgridTracks)
                    return false;

                var subgridChildren = child.ChildrenSpan;
                for (var k = 0; k < child.InFlowChildCount && k < subgridChildren.Length; k++)
                {
                    if (subgridChildren[k] is not BoxFragment subgridItem || subgridItem.Box.IsOutOfFlow)
                        continue;

                    var (subgridItemTop, _, _) = FragmentationFlow.GetBlockRange(subgridItem, child.Size, writingDirection, childTop);
                    if (!ClassifyTrackItem(subgridItem, subgridItemTop, subgridTracks, child))
                        return false;
                }

                return true;
            }

            if (itemTrack.Start == tracks.Crossed)
            {
                // (an item that spans the rows after the row continues beside them, as a table cell that spans rows; in a grid
                // whose rows depend on its inline size, the items after the row start the row after it, beside the item, see
                // GridPlacement.PlaceItems)
                crossedCount++;
                (crossedItems ??= []).Add((child, childTop));
                return true;
            }

            if (grid.Box.Display.Inside == DisplayInside.Grid && !isAutoRepeat
                && itemTrack.Start < (tracks.Crossed >= 0 ? tracks.Crossed : tracks.After) && itemTrack.End >= (tracks.Crossed >= 0 ? tracks.Crossed : tracks.After))
            {
                // (an item of a grid whose rows do not depend on its inline size that spans the rows before the row that the break
                // crosses, or the two rows that the break separates, continues beside them: it starts at the start of its first
                // row, before the start of the others)
                crossedCount++;
                (crossedItems ??= []).Add((child, childTop));
                (spanning ??= []).Add(child.Box);
                return true;
            }

            // (an item that spans the row that the break crosses and the rows before it in a grid whose rows depend on its inline
            // size)
            return false;
        }
    }

    /// <summary>
    /// Returns the content after a break inside a row whose items are side by side (a table row, a flex line, a grid row): the
    /// items are in the same row whatever the inline size (the cells of a table row, the items of a grid whose columns do not
    /// depend on its inline size; the items of a flex line and of a grid with <c>auto-fill</c> columns start a new line or row,
    /// see <see cref="LayoutContext.RowResumes"/>), and the content after the break in each item is found again in another
    /// inline size (a box, a line box of a block container). In the strip of another inline size, the content after the break
    /// of all the items starts at the same position (see <see cref="LayoutContext.AnchorPushes"/>): the items are not aligned in
    /// the row there (see <see cref="LayoutContext.ResumedRowItems"/>). A monolithic item that the break slices (an image)
    /// continues with its rest (see <see cref="BreakAnchor.SliceOffset"/>). Null when an item aligned in the block size of the
    /// row starts after the break, when an item does not start at the start of the row (but it is aligned in it), or has content
    /// after the break that cannot be found again.
    /// </summary>
    private static BreakAnchor? FindRowBreakAnchor(BoxFragment container, List<(BoxFragment Item, double Top)> crossed, HashSet<ContainerBox>? following, HashSet<ContainerBox>? preceding, HashSet<ContainerBox>? spanning, WritingDirection writingDirection, double position)
    {
        var isTableRow = container.Box.Display.Internal == DisplayInternal.TableRow;
        FlexContainerProperties? flexProperties = container.Box.Display.Inside == DisplayInside.Flex ? FlexContainerProperties.Get(container.Box) : null;
        if (flexProperties is { IsCrossReverse: true } or { IsWebkitBox: true })
            return null;

        // (the start of the row: the items that span the rows before it start before it)
        var rowTop = double.PositiveInfinity;
        foreach (var (item, itemTop) in crossed)
        {
            if (spanning?.Contains(item.Box) != true)
            {
                rowTop = Math.Min(rowTop, itemTop);
            }
        }

        List<BreakAnchor>? items = null;
        List<BreakAnchor>? nestedRows = null;
        HashSet<ContainerBox>? finished = null;
        var rowItems = new HashSet<ContainerBox>();
        foreach (var (item, itemTop) in crossed)
        {
            if (item.WritingDirection.BlockStart != writingDirection.BlockStart)
                return null;

            if (!isTableRow)
            {
                // (the items of a flex or grid row start at its block-start edge, or are aligned in it: an aligned item continues
                // at the start of the next page, see LayoutContext.ResumedRowItems; an aligned item that starts after the break
                // starts the next page whole, moved down to the content after the break of the other items, see
                // LayoutContext.AnchorPushes)
                var alignSelf = flexProperties is { } flex ? flex.GetAlignSelf(item.Box.Style.Layout) : BoxAlignment.GetAlignSelf(item.Box.Style.Layout, container.Box.Style.Layout);
                var isAligned = alignSelf.Keyword is not (AlignmentKeyword.Auto or AlignmentKeyword.Normal or AlignmentKeyword.Stretch or AlignmentKeyword.Start or AlignmentKeyword.SelfStart or AlignmentKeyword.FlexStart);
                if (isAligned && itemTop >= position - BreakAnchorEpsilon)
                {
                    rowItems.Add(item.Box);
                    (items ??= []).Add(new BreakAnchor(item.Box, 0));
                    continue;
                }

                if (!isAligned && spanning?.Contains(item.Box) != true && Math.Abs(itemTop - rowTop) > BreakAnchorEpsilon)
                    return null;
            }
            else if (Math.Abs(itemTop - rowTop) > BreakAnchorEpsilon)
            {
                // (the cells of a table row start at its block-start edge; a cell that spans the rows after its row continues beside
                // them)
                return null;
            }

            rowItems.Add(item.Box);

            // (a monolithic item, such as an image, that the break crosses is sliced: its rest continues at the start of the next
            // page, see SlicedContent; not a multi-column container, whose columns are fragmented)
            if (FragmentationFlow.IsMonolithic(item, writingDirection, scrollContainersAreMonolithic: false))
            {
                if (itemTop + writingDirection.ToLogical(item.Size).Block > position + BreakAnchorEpsilon)
                {
                    if (item.Box.Style.Layout.IsMultiColumnContainer)
                        return null;

                    (items ??= []).Add(new BreakAnchor(item.Box, position - itemTop) { SliceOffset = position - itemTop });
                    continue;
                }

                (finished ??= []).Add(item.Box);
                continue;
            }

            if (GetContentBlockEnd(item, writingDirection, itemTop) <= position + BreakAnchorEpsilon)
            {
                (finished ??= []).Add(item.Box);
                continue;
            }

            // (the content after the break of an item may start between the rows of a table or the items of a column flex container
            // of the item, which are moved down with the first of them, see LayoutContext.AnchorPushes)
            // (or inside a row of the item, whose items are items of the row, see BreakAnchor.NestedRows)
            if (FindBreakAnchorIn(item, writingDirection, position, itemTop, isParallelFlow: true) is not { } inner || (inner is { Items: null, Container: { } nested } && !IsPushableRowContainer(nested)))
                return null;

            if (inner.Preceding is not null)
            {
                (preceding ??= []).UnionWith(inner.Preceding);
            }

            if (inner.Items is { } nestedItems)
            {
                (items ??= []).AddRange(nestedItems);
                nestedRows ??= [];
                nestedRows.Add(inner with { NestedRows = null });
                if (inner.NestedRows is { } innerNestedRows)
                {
                    nestedRows.AddRange(innerNestedRows);
                }

                continue;
            }

            (items ??= []).Add(inner with { Preceding = null });
        }

        if (items is null)
            return null;

        return new BreakAnchor(items[0].Box, 0, Container: container.Box, Following: following) { Preceding = preceding, IsInRows = true, Items = items, Finished = finished, RowItems = rowItems, SpanningItems = spanning, NestedRows = nestedRows };
    }

    /// <summary>
    /// Returns the tracks of the items of a grid or of a multi-line row flex container (the first and the last row or flex line of
    /// each item, see <see cref="ParallelStructure"/>), the track that a break crosses (at <paramref name="position"/> from the
    /// block-start edge of the border box of the container), or -1 when it is between two tracks, and the first track after the
    /// break. Null when the tracks are not known.
    /// </summary>
    private static (Dictionary<BoxFragment, (int Start, int End)> Items, int Crossed, int After)? GetItemTracks(BoxFragment box, double position)
    {
        if (ParallelStructure.TryCreate(box) is not { Kind: ParallelStructureKind.FlexRow or ParallelStructureKind.Grid } structure)
            return null;

        var crossed = -1;
        var after = structure.Tracks.Length;
        for (var i = 0; i < structure.Tracks.Length; i++)
        {
            if (structure.Tracks[i].Start >= position - BreakAnchorEpsilon)
            {
                after = i;
                break;
            }

            if (structure.Tracks[i].End > position + BreakAnchorEpsilon)
            {
                crossed = i;
                after = i + 1;
                break;
            }
        }

        var items = new Dictionary<BoxFragment, (int Start, int End)>(structure.Items.Count);
        foreach (var (fragment, startTrack, endTrack, _, _) in structure.Items)
        {
            items[fragment] = (startTrack, endTrack);
        }

        return (items, crossed, after);
    }

    /// <summary>Returns the block-end edge of the in-flow content of a box and of its descendants (in the block axis of the strip), or its block-start edge when it has no content.</summary>
    private static double GetContentBlockEnd(BoxFragment box, WritingDirection writingDirection, double top)
    {
        var end = top;
        var children = box.ChildrenSpan;
        for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
        {
            var (childTop, childBottom, _) = FragmentationFlow.GetBlockRange(children[i], box.Size, writingDirection, top);
            end = Math.Max(end, GetSubtreeBlockEnd(children[i], writingDirection, childTop, childBottom));
        }

        return end;
    }

    /// <summary>
    /// Returns whether a box is a container whose in-flow children are the items of one row, side by side: a table row, a
    /// single-line row flex container.
    /// </summary>
    private static bool IsSingleRowContainer(ContainerBox box)
    {
        if (box.Display.Internal == DisplayInternal.TableRow)
            return true;

        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || box.Display.IsInternal || box.Display.Inside != DisplayInside.Flex)
            return false;

        return FlexContainerProperties.Get(box) is { IsRow: true, IsMultiLine: false, IsWebkitBox: false };
    }

    /// <summary>
    /// The positions of the content after the break of the items of a row broken by the break of a segment (see
    /// <see cref="BreakAnchor.Items"/>) in the strip of the segment: by item, the box whose space before it moves it
    /// (<see cref="LayoutContext.AnchorPushes"/>) and the position of its content after the break; and the position where the
    /// content of the segment starts: the lowest of them, not before the end of the content of the items that end before the
    /// break. Null when the row is not in the strip, or when its items are not in one row in it (e.g. the items of a flex line
    /// that do not fit in one line in another inline size).
    /// </summary>
    private static (List<(ContainerBox Box, double Position)> Items, double Start)? MeasureRowBreak(ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, BreakAnchor anchor, Dictionary<ContainerBox, double>? pushes)
    {
        var positions = new List<(ContainerBox Box, double Position)>();
        var start = double.NegativeInfinity;
        foreach (var item in anchor.Items!)
        {
            if (FindBoxFragment(children, stripSize, writingDirection, item.Box, parentTop: 0) is not var (_, top))
                return null;

            // (the space before the first line box of a block container resumed at a line box is in its content box)
            var itemPosition = top + item.Offset + (item.Resume is not null && pushes is not null ? pushes.GetValueOrDefault(item.Box) : 0);
            if (item is { Slice: { } slice, SliceOffset: not null })
            {
                // (the content that the break slices has the same size in this strip: its rest continues the part in the previous
                // page)
                if (MeasureSlicedContent(children, stripSize, writingDirection, item) is not { } sliced || !sliced.HasSameSize(slice))
                    return null;

                itemPosition = sliced.Top + item.SliceOffset.GetValueOrDefault();
            }

            // (at a break between the rows of a container of the item, the rows after the break start after the first of them: they
            // are moved down with it)
            if (item is { Container: not null, Following: { } itemFollowing })
            {
                foreach (var box in itemFollowing)
                {
                    if (FindBoxFragment(children, stripSize, writingDirection, box, parentTop: 0) is (_, var followingTop) && followingTop < top - BreakAnchorEpsilon)
                        return null;
                }
            }

            positions.Add((item.Box, itemPosition));
            start = Math.Max(start, itemPosition);
        }

        // (the items of the row, and of each row broken in its items, are in one row, see MeasureRow)
        if (!MeasureRow(anchor, ref start, children, stripSize, writingDirection, pushes))
            return null;

        if (anchor.NestedRows is { } nestedRows)
        {
            foreach (var nestedRow in nestedRows)
            {
                if (!MeasureRow(nestedRow, ref start, children, stripSize, writingDirection, pushes))
                    return null;
            }
        }

        // (the items after the break are after the start: their content is not in the previous pages)
        if (!StartsAfter(anchor.Following, start, children, stripSize, writingDirection))
            return null;

        if (anchor.NestedRows is { } rows)
        {
            foreach (var nestedRow in rows)
            {
                if (!StartsAfter(nestedRow.Following, start, children, stripSize, writingDirection))
                    return null;
            }
        }

        return (positions, start);

        // Returns whether the items of a row are in one row: they start at the same position, before the space added to the margins
        // of the items moved down (see LayoutContext.AnchorPushes); the start is not before the end of the content of its finished
        // items
        static bool MeasureRow(BreakAnchor row, ref double start, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, Dictionary<ContainerBox, double>? pushes)
        {
            double? rowTop = null;
            foreach (var box in row.RowItems!)
            {
                if (FindBoxFragment(children, stripSize, writingDirection, box, parentTop: 0) is not var (_, itemTop))
                    return false;

                if (row.SpanningItems?.Contains(box) == true)
                    continue;

                if (pushes is not null && !row.Items!.Any(item => item.Resume is not null && ReferenceEquals(item.Box, box)))
                {
                    itemTop -= pushes.GetValueOrDefault(box);
                }

                if (rowTop is { } expected && Math.Abs(itemTop - expected) > BreakAnchorEpsilon)
                    return false;

                rowTop = itemTop;
            }

            if (row.Finished is { } finished)
            {
                foreach (var box in finished)
                {
                    if (FindBoxFragment(children, stripSize, writingDirection, box, parentTop: 0) is var (fragment, top))
                    {
                        // (the content of a monolithic item, such as an image, is the item itself)
                        var end = FragmentationFlow.IsMonolithic(fragment, writingDirection, scrollContainersAreMonolithic: false) ? top + writingDirection.ToLogical(fragment.Size).Block : GetContentBlockEnd(fragment, writingDirection, top);
                        start = Math.Max(start, end);
                    }
                }
            }

            // (the content of the rows before the break is in the previous pages: the start is after its end in this strip, e.g. a
            // table cell of a row before the broken row that spans it, whose lines end later in another inline size)
            if (row.Preceding is { } precedingRows)
            {
                foreach (var box in precedingRows)
                {
                    if (FindBoxFragment(children, stripSize, writingDirection, box, parentTop: 0) is not var (fragment, top))
                        continue;

                    if (box.Display.Internal != DisplayInternal.TableRow)
                    {
                        start = Math.Max(start, GetContentBlockEnd(fragment, writingDirection, top));
                        continue;
                    }

                    // (the cells of a table row may span the rows after it: their content, not their box)
                    var cells = fragment.ChildrenSpan;
                    for (var i = 0; i < fragment.InFlowChildCount && i < cells.Length; i++)
                    {
                        if (cells[i] is BoxFragment cell)
                        {
                            var (cellTop, _, _) = FragmentationFlow.GetBlockRange(cell, fragment.Size, writingDirection, top);
                            start = Math.Max(start, GetContentBlockEnd(cell, writingDirection, cellTop));
                        }
                    }
                }
            }

            // (when an item spans the rows before the broken row, its content after the break may start in them in this strip: the
            // start is after the end of the items of these rows, which are in the previous pages)
            if (row is { SpanningItems: not null, Preceding: { } preceding })
            {
                foreach (var box in preceding)
                {
                    if (FindBoxFragment(children, stripSize, writingDirection, box, parentTop: 0) is var (fragment, top))
                    {
                        start = Math.Max(start, top + writingDirection.ToLogical(fragment.Size).Block);
                    }
                }
            }

            return true;
        }

        static bool StartsAfter(HashSet<ContainerBox>? boxes, double start, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection)
        {
            if (boxes is not null)
            {
                foreach (var box in boxes)
                {
                    if (FindBoxFragment(children, stripSize, writingDirection, box, parentTop: 0) is (_, var top) && top < start - BreakAnchorEpsilon)
                        return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Returns the anchor of the break of a page when the content of the page starts with it (see <see cref="BreakAnchor.IsInRows"/>),
    /// else null: the rows before the break do not continue in it, the rows after the break and the anchor box are not in the
    /// previous page, and the anchor box is in it, starting with the line box of <see cref="BreakAnchor.Resume"/> (or with the rest
    /// of that line, when the break slices it: the anchor of the item is then its rest, see <see cref="BreakAnchor.SlicedLineOffset"/>).
    /// Then the content before the anchor is in the previous pages, and the content after it is laid out again from it.
    /// </summary>
    private static BreakAnchor? ResolveAnchorOfPage(BreakAnchor anchor, List<FragmentSlicer.PlacedFragment> previousPage, List<FragmentSlicer.PlacedFragment> page, WritingDirection writingDirection)
    {
        if (anchor.Preceding is { } preceding && ContainsAnyBox(page, preceding))
            return null;

        if (anchor.Following is { } following && ContainsAnyBox(previousPage, following))
            return null;

        if (anchor.Items is { } items)
        {
            // (the content after the break of each item of a broken row starts the page)
            List<BreakAnchor>? resolvedItems = null;
            for (var i = 0; i < items.Count; i++)
            {
                if (ResolveAnchorOfPage(items[i], previousPage, page, writingDirection) is not { } resolvedItem)
                    return null;

                if (!ReferenceEquals(resolvedItem, items[i]))
                {
                    resolvedItems ??= [.. items];
                    resolvedItems[i] = resolvedItem;
                }
            }

            // (the items that end before the break of the strip have no content in the page: their own break is not before it, and
            // they are not pushed to the page, e.g. an item that avoids breaks; the same for the rows broken in the items)
            if (!IsFinishedRowOfPage(anchor))
                return null;

            if (anchor.NestedRows is { } nestedRows)
            {
                foreach (var nestedRow in nestedRows)
                {
                    if ((nestedRow.Preceding is { } nestedPreceding && ContainsAnyBox(page, nestedPreceding))
                        || (nestedRow.Following is { } nestedFollowing && ContainsAnyBox(previousPage, nestedFollowing))
                        || !IsFinishedRowOfPage(nestedRow))
                        return null;
                }
            }

            return resolvedItems is null ? anchor : anchor with { Items = resolvedItems };
        }

        if (FindFirstFragment(page, anchor.Box) is not { } fragment)
            return null;

        // (the content that the break slices is in both pages, and its part in the page starts at the break: the break of its
        // own flow is the break of the strip)
        if (anchor.SliceOffset is { } sliceOffset)
            return FindFirstFragment(previousPage, anchor.Box) is not null && FindBlockOffset(page, anchor.Box, writingDirection) is { } offset && Math.Abs(offset + sliceOffset) <= BreakAnchorEpsilon ? anchor : null;

        if (anchor.Resume is { } resume)
        {
            // (the first line box of the page is the line of the anchor)
            var fragmentTop = FindBlockOffset(page, anchor.Box, writingDirection);
            LineBoxFragment? firstLine = null;
            var firstLineTop = 0d;
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is LineBoxFragment line)
                {
                    firstLine = line;
                    firstLineTop = FragmentationFlow.GetBlockRange(line, fragment.Size, writingDirection, fragmentTop.GetValueOrDefault()).Top;
                    break;
                }
            }

            if (firstLine?.StartPosition != resume)
                return null;

            // (the line box is not in the previous page; a line sliced by the break, which could not be pushed to the next page, is
            // in both: its rest starts the page at the offset of the break)
            if (FindFirstFragment(previousPage, anchor.Box) is { } previousFragment)
            {
                foreach (var child in previousFragment.ChildrenSpan)
                {
                    if (child is LineBoxFragment { StartPosition: var previousStart } && previousStart == resume)
                    {
                        return anchor is { SlicedLineOffset: { } slicedOffset, Slice: not null } && fragmentTop is not null && Math.Abs(firstLineTop + slicedOffset) <= BreakAnchorEpsilon
                            ? anchor with { SliceOffset = slicedOffset }
                            : null;
                    }
                }
            }

            return anchor;
        }

        return FindFirstFragment(previousPage, anchor.Box) is null ? anchor : null;

        // Returns whether the finished items of a broken row have no content in the page
        bool IsFinishedRowOfPage(BreakAnchor row)
        {
            if (row.Finished is { } finished)
            {
                foreach (var box in finished)
                {
                    if (FindFirstFragment(page, box) is { } finishedFragment && (FindFirstFragment(previousPage, box) is null || HasContentStartingInPage(finishedFragment, previousPage)))
                        return false;
                }
            }

            return true;
        }

        // Returns whether a fragment has a line box or a box whose content is not in the previous page
        static bool HasContentStartingInPage(Fragment fragment, List<FragmentSlicer.PlacedFragment> previousPage)
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is LineBoxFragment || (child is BoxFragment childBox && FindFirstFragment(previousPage, childBox.Box) is null) || HasContentStartingInPage(child, previousPage))
                    return true;
            }

            return false;
        }

        // Returns the block offset in the page of the first fragment of a box, or null
        static double? FindBlockOffset(List<FragmentSlicer.PlacedFragment> fragments, ContainerBox box, WritingDirection writingDirection)
        {
            foreach (var (fragment, offset) in fragments)
            {
                if (Find(fragment, offset.Block, box, writingDirection) is { } result)
                    return result;
            }

            return null;

            static double? Find(Fragment fragment, double top, ContainerBox box, WritingDirection writingDirection)
            {
                if (fragment is BoxFragment boxFragment && ReferenceEquals(boxFragment.Box, box))
                    return top;

                foreach (var child in fragment.ChildrenSpan)
                {
                    if (Find(child, FragmentationFlow.GetBlockRange(child, fragment.Size, writingDirection, top).Top, box, writingDirection) is { } result)
                        return result;
                }

                return null;
            }
        }

        static BoxFragment? FindFirstFragment(List<FragmentSlicer.PlacedFragment> fragments, ContainerBox box)
        {
            foreach (var (fragment, _) in fragments)
            {
                if (Find(fragment, box) is { } result)
                    return result;
            }

            return null;

            static BoxFragment? Find(Fragment fragment, ContainerBox box)
            {
                if (fragment is BoxFragment boxFragment && ReferenceEquals(boxFragment.Box, box))
                    return boxFragment;

                foreach (var child in fragment.ChildrenSpan)
                {
                    if (Find(child, box) is { } result)
                        return result;
                }

                return null;
            }
        }
    }

    /// <summary>Returns whether fragments of a page contain a fragment of one of the boxes (not the header and the footer of a table, which are repeated in the pages).</summary>
    private static bool ContainsAnyBox(List<FragmentSlicer.PlacedFragment> fragments, HashSet<ContainerBox> boxes)
    {
        foreach (var (fragment, _) in fragments)
        {
            if (Contains(fragment))
                return true;
        }

        return false;

        bool Contains(Fragment fragment)
        {
            if (fragment is BoxFragment box && boxes.Contains(box.Box) && box.Box.Display.Internal is not (DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup))
                return true;

            foreach (var child in fragment.ChildrenSpan)
            {
                if (Contains(child))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns whether a box is a container whose rows (its in-flow children, in the block axis) are the same whatever its inline
    /// size: a table wrapper box (the captions and the table grid box), a table grid box (the row groups), a row group (the
    /// rows), a single-line column flex container (the items), and a grid container whose columns do not depend on its inline
    /// size (no <c>auto-fill</c> nor <c>auto-fit</c> repetition, not a subgrid: the items are placed in the same rows), a grid
    /// container whose items are placed in the order of the items (see <see cref="IsAutoRepeatGridContainer"/>: the items after a
    /// break between its rows start a new row in another inline size), and a multi-line row flex container (see
    /// <see cref="IsFlexLineContainer"/>: its flex lines are the rows, the items after a break between them start a new flex line
    /// in another inline size). <paramref name="sideBySide"/> is whether rows may be side by
    /// side (the items of a grid row or of a flex line): a break can only be found again between its rows.
    /// </summary>
    private static bool IsRowContainer(ContainerBox box, out bool sideBySide)
    {
        sideBySide = false;
        if (box.Kind is ContainerBoxKind.TableWrapper or ContainerBoxKind.TableGrid)
            return true;

        if (box.Display.Internal is DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup)
            return true;

        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || box.Display.IsInternal)
            return false;

        switch (box.Display.Inside)
        {
            case DisplayInside.Flex:
                var flex = box.Style.Layout.Flex;
                if (IsFlexLineContainer(box))
                {
                    sideBySide = true;
                    return true;
                }

                return flex.Direction is FlexDirection.Column or FlexDirection.ColumnReverse && flex.Wrap == FlexWrap.NoWrap;

            case DisplayInside.Grid:
                var columns = box.Style.Layout.Grid.TemplateColumns;
                sideBySide = true;
                // (a subgrid of the columns of its grid has the number of columns that it spans: its rows are its own, unless it is
                // also a subgrid of the rows, whose items are in the rows of its grid, see FindBreakAnchorIn)
                return (columns.AutoRepeat is null && (!columns.IsSubgrid || !box.Style.Layout.Grid.TemplateRows.IsSubgrid)) || IsAutoRepeatGridContainer(box);

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns whether the rows of a container in an item of a broken row can be moved down from the first row after the break
    /// (see <see cref="LayoutContext.AnchorPushes"/>): the row groups of a table grid box and the rows of a row group (in
    /// <c>TableLayoutAlgorithm</c>), the items of a single-line column flex container (in <c>FlexLayoutAlgorithm</c>).
    /// </summary>
    private static bool IsPushableRowContainer(ContainerBox box)
    {
        if (box.Kind == ContainerBoxKind.TableGrid || box.Display.Internal == DisplayInternal.TableRowGroup)
            return true;

        // (the items of the first row of a grid whose rows do not depend on its inline size, see BreakAnchor.PushedWith)
        if (box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous && !box.Display.IsInternal && box.Display.Inside == DisplayInside.Grid)
            return !IsAutoRepeatGridContainer(box) && box.Style.Layout.Grid.TemplateColumns.AutoRepeat is null && !box.Style.Layout.Grid.TemplateRows.IsSubgrid;

        return box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous && !box.Display.IsInternal && box.Display.Inside == DisplayInside.Flex
            && FlexContainerProperties.Get(box) is { IsRow: false, IsMainReverse: false, IsMultiLine: false, IsWebkitBox: false };
    }

    /// <summary>
    /// Returns whether a box is a multi-line row flex container whose flex lines are stacked in the block axis in the order of the
    /// items (<c>flex-wrap: wrap</c>, not balanced, not a <c>-webkit-box</c>): the items after a break between two of its flex
    /// lines start a new flex line in the strip of another inline size (see <see cref="LayoutContext.RowResumes"/>).
    /// </summary>
    private static bool IsFlexLineContainer(ContainerBox box)
    {
        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || box.Display.IsInternal || box.Display.Inside != DisplayInside.Flex)
            return false;

        var properties = FlexContainerProperties.Get(box);
        return properties is { IsRow: true, IsMultiLine: true, IsCrossReverse: false, IsBalanced: false, IsWebkitBox: false };
    }

    /// <summary>
    /// Returns whether a grid item is a subgrid whose rows are the rows of its grid (<c>grid-template-rows: subgrid</c>): its items
    /// are in the rows of the grid.
    /// </summary>
    private static bool IsRowSubgrid(BoxFragment item) => item.Box.LayoutState is GridContainerState { Subgrid: { Rows: not null } input } subgrid && input.Pass == subgrid.Pass;

    /// <summary>
    /// Returns whether a box is a grid container whose number of columns depends on its inline size (an <c>auto-fill</c> or
    /// <c>auto-fit</c> repetition of its columns, not a subgrid) and whose items are placed in rows (<c>grid-auto-flow: row</c>,
    /// dense or not): the items after a break between two of its rows start a new row in the strip of another inline size (see
    /// <see cref="LayoutContext.RowResumes"/>). The items must be in their order at the break (see <see cref="IsPlacedInOrder"/>).
    /// </summary>
    private static bool IsAutoRepeatGridContainer(ContainerBox box)
    {
        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || box.Display.IsInternal || box.Display.Inside != DisplayInside.Grid)
            return false;

        var grid = box.Style.Layout.Grid;
        return grid.TemplateColumns.AutoRepeat is not null && !grid.TemplateColumns.IsSubgrid && !grid.TemplateRows.IsSubgrid && !grid.AutoFlow.IsColumn;
    }

    /// <summary>
    /// Returns whether the items of a grid container with <c>auto-fill</c> or <c>auto-fit</c> columns (see
    /// <see cref="IsAutoRepeatGridContainer"/>) are in their order at a break: the items whose row is placed by the auto-placement
    /// (in order-modified document order, the order of their fragments) are the items before the break, then the items of the row
    /// that the break crosses, then the items after the break, and the items with a definite row are before the break. In the strip
    /// of another inline size, the items after the break start a new row after the rows of the items before it, and after the rows of
    /// the items with a definite row (see <see cref="RowResume.Preceding"/>); with <c>dense</c> packing, they do not fill the holes
    /// before it (see <see cref="GridPlacement.PlaceItems"/>).
    /// </summary>
    private static bool IsPlacedInOrder(BoxFragment box, HashSet<ContainerBox>? preceding, List<(BoxFragment Item, double Top)>? crossed, HashSet<ContainerBox>? following)
    {
        var group = 0;
        var children = box.ChildrenSpan;
        for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
        {
            if (children[i] is not BoxFragment child || child.Box.IsOutOfFlow)
                continue;

            var itemGrid = child.Box.Style.Layout.Grid;
            var isDefinite = !IsAutomatic(itemGrid.RowStart) || !IsAutomatic(itemGrid.RowEnd);
            var itemGroup = preceding?.Contains(child.Box) == true ? 0 : following?.Contains(child.Box) == true ? 2 : 1;
            if (isDefinite)
            {
                if (itemGroup != 0)
                    return false;

                continue;
            }

            if (itemGroup == 1 && crossed?.Exists(item => ReferenceEquals(item.Item, child)) != true)
                return false;

            if (itemGroup < group)
                return false;

            group = itemGroup;
        }

        return true;

        static bool IsAutomatic(GridLine line) => line.IsAuto || line.IsSpan;
    }

    /// <summary>
    /// Returns whether the in-flow content of a box has a forced page break between two siblings (or a change of the page name,
    /// when <paramref name="namesBreak"/>).
    /// </summary>
    private static bool ContainsForcedPageBreak(PageBreakRules rules, BoxFragment box, bool namesBreak)
    {
        if (box.Box.EstablishesIndependentChildrenLayout || UsedContainment.HasLayoutContainment(box.Box))
            return false;

        var children = box.ChildrenSpan;
        BoxFragment? previous = null;
        for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
        {
            if (children[i] is not BoxFragment child || child.Box.IsOutOfFlow)
                continue;

            if (previous is not null && (PageBreakRules.IsForced(previous.Box.Style.Layout.Fragmentation.BreakAfter) || PageBreakRules.IsForced(child.Box.Style.Layout.Fragmentation.BreakBefore)
                || (namesBreak && !string.Equals(rules.GetEndPageValue(previous), rules.GetStartPageValue(child), StringComparison.Ordinal))))
                return true;

            if (ContainsForcedPageBreak(rules, child, namesBreak))
                return true;

            previous = child;
        }

        return false;
    }

    /// <summary>Returns the block-end edge of a fragment and of its descendants (in the block axis of the strip).</summary>
    private static double GetSubtreeBlockEnd(Fragment fragment, WritingDirection writingDirection, double top, double bottom)
    {
        var end = bottom;
        foreach (var child in fragment.ChildrenSpan)
        {
            var (childTop, childBottom, _) = FragmentationFlow.GetBlockRange(child, fragment.Size, writingDirection, top);
            end = Math.Max(end, GetSubtreeBlockEnd(child, writingDirection, childTop, childBottom));
        }

        return end;
    }

    /// <summary>The nested multi-column containers of the strip are fragmented (or laid out again): their states no longer need the layout pass.</summary>
    private static void ClearNestedColumnStates(LayoutContext context)
    {
        foreach (var nestedState in context.NestedColumnStates)
        {
            nestedState.Clear();
        }

        context.NestedColumnStates.Clear();
    }

    /// <summary>
    /// Returns the pages of the document from the breaks of the strip (https://drafts.csswg.org/css-page-3/#page-model): the name
    /// of each page is the start page value of the content after its break (or the used page value of the box broken by it),
    /// and a blank page is inserted before a page that starts after a forced break to a left or right page when the page would
    /// be on the other side (https://drafts.csswg.org/css-break-3/#valdef-break-before-left). The first page is a right page
    /// when the pages progress from left to right, unless the content starts with a forced break to a side.
    /// </summary>
    private static List<(ContentPage Content, PageSelection Selection, bool HasContent)> GetPageSequence(PageBreakRules rules, Fragment[] children, PhysicalSize stripSize, WritingDirection writingDirection, List<FragmentainerBreak> breaks, BoxFragment? rootFragment, bool rightToLeft, double segmentStart, PageSelection? previousPage)
    {
        var result = new List<(ContentPage, PageSelection, bool)>(breaks.Count + 1);
        for (var i = 0; i <= breaks.Count; i++)
        {
            var start = i == 0 ? segmentStart : breaks[i - 1].Position;
            var (name, side) = i == 0 && rootFragment is not null && previousPage is null
                ? (rules.GetStartPageValue(rootFragment), GetLeadingSide(rootFragment))
                : FindPageStart(rules, children, stripSize, writingDirection, start);
            var content = new ContentPage(start, name, side);
            bool isLeft;
            if (result.Count == 0 && previousPage is null)
            {
                isLeft = side switch
                {
                    BreakBetween.Left => true,
                    BreakBetween.Right => false,
                    BreakBetween.Recto => rightToLeft,
                    BreakBetween.Verso => !rightToLeft,
                    _ => rightToLeft,
                };
            }
            else
            {
                var previous = result.Count > 0 ? result[^1].Item2 : previousPage!.Value;
                isLeft = !previous.IsLeft;
                var wantsLeft = side switch
                {
                    BreakBetween.Left => true,
                    BreakBetween.Right => false,
                    BreakBetween.Recto => rightToLeft,
                    BreakBetween.Verso => !rightToLeft,
                    _ => (bool?)null,
                };

                if (wantsLeft is { } left && left != isLeft)
                {
                    result.Add((content, new PageSelection(name, IsFirst: false, IsLeft: isLeft, IsBlank: true), false));
                    isLeft = !isLeft;
                }
            }

            result.Add((content, new PageSelection(name, IsFirst: result.Count == 0 && previousPage is null, IsLeft: isLeft, IsBlank: false), true));
        }

        return result;
    }

    /// <summary>
    /// Returns the page name and the side of the page that starts at a position of the strip: the start page value of the first
    /// box that starts at or after the position, and the forced break to a side between it and the box before it (or the used
    /// page value of the box that the position breaks, without side).
    /// </summary>
    private static (string Name, BreakBetween Side) FindPageStart(PageBreakRules rules, ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, double position, BoxFragment? parent = null, double parentTop = 0)
    {
        const double Epsilon = 0.01;
        BoxFragment? previous = null;
        var inFlowCount = parent is null ? children.Length : Math.Min(parent.InFlowChildCount, children.Length);
        for (var i = 0; i < inFlowCount; i++)
        {
            var child = children[i];
            if (child is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true })
                continue;

            var (top, bottom, _) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            if (top >= position - Epsilon)
            {
                if (child is not BoxFragment box || box.Box.IsInlineBox || box.Box.IsAtomicInline)
                    break;

                var side = GetSide(box.Box.Style.Layout.Fragmentation.BreakBefore);
                for (var first = PageBreakRules.GetEdgeChild(box, first: true); first is not null && side == BreakBetween.Auto; first = PageBreakRules.GetEdgeChild(first, first: true))
                {
                    side = GetSide(first.Box.Style.Layout.Fragmentation.BreakBefore);
                }

                for (var last = previous; last is not null && side == BreakBetween.Auto; last = PageBreakRules.GetEdgeChild(last, first: false))
                {
                    side = GetSide(last.Box.Style.Layout.Fragmentation.BreakAfter);
                }

                return (rules.GetStartPageValue(box), side);
            }

            if (bottom > position + Epsilon && child is BoxFragment container && !FragmentationFlow.IsMonolithic(container, writingDirection, scrollContainersAreMonolithic: false) && container.WritingDirection.BlockStart == writingDirection.BlockStart)
                return FindPageStart(rules, container.ChildrenSpan, container.Size, writingDirection, position, container, top);

            previous = child as BoxFragment;
        }

        return (parent is null ? "" : rules.GetUsedPageValue(parent.Box), BreakBetween.Auto);
    }

    /// <summary>Returns the forced break to a side that starts the content of a box (its break-before value and the ones of its first children).</summary>
    private static BreakBetween GetLeadingSide(BoxFragment box)
    {
        for (BoxFragment? current = box; current is not null; current = PageBreakRules.GetEdgeChild(current, first: true))
        {
            if (GetSide(current.Box.Style.Layout.Fragmentation.BreakBefore) is not BreakBetween.Auto and var side)
                return side;
        }

        return BreakBetween.Auto;
    }

    private static BreakBetween GetSide(BreakBetween value) => value is BreakBetween.Left or BreakBetween.Right or BreakBetween.Recto or BreakBetween.Verso ? value : BreakBetween.Auto;

    /// <summary>
    /// Paints a page (https://drafts.csswg.org/css-page-3/#painting): the background and the borders of the page box, the
    /// page-margin boxes whose <c>z-index</c> is negative, then the document in the page area (its canvas background covers the
    /// padding box of the page box), then the other page-margin boxes. As in Blink, each page-margin box is painted atomically, as a stacking
    /// context, in the order of its <c>z-index</c> (<c>auto</c> is 0), then in the order of the boxes (from the top-left
    /// corner, clockwise). The display list is in the coordinates of the page box. A document whose root element generates no
    /// box has blank pages. Without <paramref name="paper"/>, the page is painted over a transparent sheet.
    /// </summary>
    internal DisplayList PaintPage(LayoutPage page, bool paper = true)
    {
        using var suspension = _document.GetStyleEngine().SuspendUpdates();
        var geometry = page.Geometry;
        var builder = new DisplayListBuilder();

        // The page sheet is white paper
        if (paper)
        {
            builder.Add(new DrawPaintItem(new DisplayPaint(DisplayColor.White)));
        }

        List<(BoxFragment Fragment, int ZIndex)>? marginBoxes = null;
        var viewport = _boxTree?.Viewport;
        // (as in Gecko, a document whose root element generates no box prints blank pages, without page boxes)
        if (geometry.Style is { } style && viewport is { IsEmpty: false })
        {
            // As in the browsers, the background of the page box covers the whole page, its margins included (whatever its
            // background-clip), and its images are positioned in the page box (see Painter.PaintBackground); its borders are in
            // the page margins
            var borderBox = geometry.BorderBox;
            var backgroundStyle = style.CreateOverriddenStyle([("border-top-style", "none"), ("border-right-style", "none"), ("border-bottom-style", "none"), ("border-left-style", "none")]);
            var background = new ContainerBox(ContainerBoxKind.Normal, backgroundStyle, Display.Block, style.Element, StyleEngine.PageContextPseudoElement);
            var backgroundFragment = new BoxFragment(background, borderBox.Size, geometry.Border, geometry.Padding, geometry.Margin, children: null) { Offset = borderBox.Offset };
            var borderStyle = style.CreateOverriddenStyle([("background-color", "transparent"), ("background-image", "none")]);
            var pageBox = new ContainerBox(ContainerBoxKind.Normal, borderStyle, Display.Block, style.Element, StyleEngine.PageContextPseudoElement);
            var pageBoxFragment = new BoxFragment(pageBox, borderBox.Size, geometry.Border, geometry.Padding, geometry.Margin, children: null) { Offset = borderBox.Offset };
            var sheet = new BoxFragment(viewport, geometry.PageSize, default, default, default, [backgroundFragment, pageBoxFragment]);
            builder.AddList(Painter.Paint(this, _document, sheet, PhysicalOffset.Zero, DisplayColor.Transparent));

            if (page.MarginBoxes is { Count: > 0 } boxes)
            {
                marginBoxes = new List<(BoxFragment, int)>(boxes.Count);
                foreach (var box in boxes)
                {
                    marginBoxes.Add((box, int.TryParse(box.Box.Style.GetComputedValue("z-index"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var zIndex) ? zIndex : 0));
                }

                // (a stable sort: the boxes keep their order for the same z-index)
                marginBoxes = [.. marginBoxes.OrderBy(static box => box.ZIndex)];
                PaintMarginBoxes(negative: true);
            }
        }

        // The document is painted in the page area; as in Blink, its canvas background covers the padding box of the page box,
        // and the content that overflows it is clipped
        // (the document is painted at the position of the page area, which may be fractional: its painting is snapped to the
        // device pixels of the page)
        // (the content of a scaled page is scaled from the origin of the page area, see PrintOptions.Scale)
        var area = geometry.PageArea;
        var paddingBox = geometry.PaddingBox;
        builder.Save();
        builder.Add(new ClipRectItem(paddingBox));
        if (geometry.ContentScale != 1)
        {
            builder.Transform(DisplayMatrix.Translation(area.X, area.Y).PreConcat(DisplayMatrix.Scale(geometry.ContentScale, geometry.ContentScale)).PreConcat(DisplayMatrix.Translation(-area.X, -area.Y)));
        }

        builder.AddList(Painter.PaintPageContent(this, _document, page.Content, new PhysicalOffset(area.X, area.Y)));
        builder.Restore();
        PaintMarginBoxes(negative: false);
        return builder.ToDisplayList(geometry.PageSize);

        void PaintMarginBoxes(bool negative)
        {
            if (marginBoxes is null || viewport is null)
                return;

            foreach (var (fragment, zIndex) in marginBoxes)
            {
                if (zIndex < 0 == negative)
                {
                    var sheet = new BoxFragment(viewport, geometry.PageSize, default, default, default, [fragment]);
                    builder.AddList(Painter.Paint(this, _document, sheet, PhysicalOffset.Zero, DisplayColor.Transparent));
                }
            }
        }
    }
}
