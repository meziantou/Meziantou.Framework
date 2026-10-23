using Meziantou.Framework.WebPlatform.Css.Cascade;
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

    /// <summary>Returns the pages of the document being printed, after computing the layout when it is out of date (empty when the document is not rendered).</summary>
    internal IReadOnlyList<LayoutPage> GetPages()
    {
        EnsureLayout();
        return _pages ?? [];
    }

    /// <summary>A content page found by the breaker, before the blank pages are inserted.</summary>
    private readonly record struct ContentPage(double Start, string Name, BreakBetween Side);

    /// <summary>
    /// The start of a segment of the pages laid out in a strip of another inline size (see <see cref="LayoutPages"/>): the box
    /// that starts the first page of the segment (at the offset of the break from its block-start border edge), the size of
    /// the page area of that page, and the last page before it.
    /// </summary>
    private sealed record PageSegmentStart(ContainerBox Anchor, double AnchorOffset, PhysicalSize PageAreaSize, PageSelection Previous)
    {
        /// <summary>The fragments of the boxes fixed to the viewport, laid out in the initial containing block of the first segment (the page area of the first page).</summary>
        public required List<(BoxFragment Fragment, LogicalOffset Offset)>? FixedBoxes { get; init; }
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
        PhysicalSize? icbSize = null;
        try
        {
            do
            {
                if (segment is not null)
                {
                    context.ViewportSize = segment.PageAreaSize;
                }

                var count = pages.Count;
                segment = LayoutPageSegment(context, viewport, writingDirection, segment, pages, selections, geometries, out var segmentIcbSize);
                icbSize ??= segmentIcbSize;

                // (a segment always has a page: the next one starts after it)
                if (pages.Count == count)
                    break;
            }
            while (segment is not null && pages.Count < ColumnBreaker.MaxFragmentainers);
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
    private PageSegmentStart? LayoutPageSegment(LayoutContext context, ContainerBox viewport, WritingDirection writingDirection, PageSegmentStart? segment, List<LayoutPage> pages, List<PageSelection> selections, Dictionary<PageSelection, PageGeometry> geometries, out PhysicalSize icbSize)
    {
        // https://drafts.csswg.org/css-page-3/#progression: the first page is a right page when the pages progress from left to right
        var rightToLeft = writingDirection.IsVertical ? writingDirection.BlockStart == PhysicalSide.Right : writingDirection.IsRightToLeft;
        Dictionary<ContainerBox, FloatPush>? floatPushes = null;
        Dictionary<ContainerBox, GridLanesBreaks>? lanesBreaks = null;

        // The absolutely positioned boxes of the initial containing block that do not fit in the rest of their page: they are
        // fragmented apart
        HashSet<ContainerBox>? fragmentedBoxes = null;
        var stripRelayout = new StripRelayout();
        for (var pass = 0; ; pass++)
        {
            // The strip: the root element laid out in the initial containing block, as the content of a fragmentation context
            context.MultiColumnDepth++;
            var outerFloatPushes = context.FloatPushes;
            var outerLanesBreaks = context.GridLanesBreaks;
            var outerRelayout = context.StripRelayout;
            context.FloatPushes = floatPushes;
            context.GridLanesBreaks = lanesBreaks;
            context.StripRelayout = stripRelayout;
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
            var flow = FragmentationFlow.Collect(children, stripSize, writingDirection, spannerBoxes: null, viewport, separatesOutOfFlow: true, floatPushes, rules);
            flow.SlicesMonolithicContent = true;
            flow.Relayout = stripRelayout;

            // The start of the segment in this strip: the content before it is in a virtual first fragmentainer (the pages of
            // the previous segments), whose fragments are dropped
            var segmentStart = 0d;
            if (segment is not null)
            {
                segmentStart = FindBoxStart(children, stripSize, writingDirection, segment.Anchor, parentTop: 0) is { } anchorTop ? Math.Max(0, anchorTop + segment.AnchorOffset) : 0;
            }

            var baseIndex = segmentStart > 0 ? 1 : 0;

            // The breaks: the block size of a page depends on its page name and on its side, which depend on the breaks (the
            // content that starts the page, the blank pages inserted before the left and right pages)
            var firstName = rootFragment is null ? "" : segment is null ? rules.GetStartPageValue(rootFragment) : FindPageStart(rules, children, stripSize, writingDirection, segmentStart).Name;
            var firstGeometry = GetPageGeometry(geometries, new PageSelection(firstName, IsFirst: segment is null, IsLeft: segment is null ? rightToLeft : !segment.Previous.IsLeft, IsBlank: false));
            var sizes = new List<double> { writingDirection.ToLogical(firstGeometry.PageArea.Size).Block };
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
                        computed.Add(writingDirection.ToLogical(GetPageGeometry(geometries, selection).PageArea.Size).Block);
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

            // (a box placed on its page that does not fit in the rest of the page is fragmented apart by a new layout)
            var hasNewFragmentedBox = false;
            if (placedBoxes is not null && pass < FloatPusher.MaxPasses)
            {
                foreach (var box in placedBoxes)
                {
                    var (top, bottom, _) = FragmentationFlow.GetBlockRange(box, icbSize, writingDirection, parentTop: 0);
                    var (index, offset) = map.Map(top);
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
                var displayOffset = i == 0 ? 0 : breaks[i - 1].DisplayOffset;
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
                var blockSize = writingDirection.ToLogical(GetPageGeometry(geometries, selection).PageArea.Size).Block;
                fragmentainers.Add(new Fragmentainer(double.PositiveInfinity, double.PositiveInfinity, blockSize, 0, LogicalOffset.Zero));
                blockSizes.Add(blockSize);
                sequence.Add((last.Content, selection, true));
            }

            // The floats that do not fit are pushed to the next page, and the items of the grid lanes containers fragmented in their
            // stacking axis are placed again in the pages (see GridLanesFragmentation), by new layouts of the strip
            if (stripRelayout.UpdateBrokenBoxes(flow, map, children, stripSize, writingDirection, blockSizes))
            {
                // The broken boxes are laid out again (see StripRelayout): the floats are pushed again from the start
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
                    || stripRelayout.UpdateFloatAvoiders(flow, map, children, stripSize, writingDirection))
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

            // The page that ends the segment: the first page whose page area has another inline size than the strip and that
            // starts before a box of the block flow of the root element (its content is laid out again in a strip of its inline
            // size; the content of the other pages is laid out in the inline size of the strip)
            var segmentEnd = sequence.Count;
            PageSegmentStart? next = null;
            var contentIndex = 0;
            for (var i = 0; i < sequence.Count; i++)
            {
                var (_, selection, hasContent) = sequence[i];
                if (!hasContent)
                    continue;

                var areaSize = GetPageGeometry(geometries, selection).PageArea.Size;
                if (contentIndex > 0 && contentIndex <= breaks.Count && Math.Abs(writingDirection.ToLogical(areaSize).Inline - icbLogical.Inline) > 0.01
                    && FindBreakAnchor(children, stripSize, writingDirection, breaks[contentIndex - 1].Position, parentTop: 0) is var (anchor, anchorOffset))
                {
                    // (a blank page before the page is inserted again by the next segment)
                    segmentEnd = i > 0 && !sequence[i - 1].HasContent ? i - 1 : i;
                    next = new PageSegmentStart(anchor, anchorOffset, areaSize, sequence[segmentEnd - 1].Selection) { FixedBoxes = fixedPlacements };
                    break;
                }

                contentIndex++;
            }

            // The fragments of the strip in the page areas
            var slicer = new FragmentSlicer(flow, map, fragmentainers, writingDirection);
            var placedFragments = slicer.Slice(children, stripSize, spannerBoxes: null);
            ClearNestedColumnStates(context);

            contentIndex = 0;
            for (var sequenceIndex = 0; sequenceIndex < segmentEnd; sequenceIndex++)
            {
                var (_, selection, hasContent) = sequence[sequenceIndex];
                var geometry = GetPageGeometry(geometries, selection);
                var areaSize = geometry.PageArea.Size;
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
                    if (placedBoxes is not null)
                    {
                        foreach (var box in placedBoxes)
                        {
                            var logical = writingDirection.ToLogical(box.Offset - box.RelativeOffset, icbSize, box.Size);
                            var (boxIndex, offset) = map.Map(logical.Block);
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

    /// <summary>Returns the block-start border edge of the first fragment of a box in the strip (in the block axis of the strip), or null.</summary>
    private static double? FindBoxStart(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, ContainerBox box, double parentTop)
    {
        foreach (var child in children)
        {
            if (child is not BoxFragment childBox)
                continue;

            var (top, _, _) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            if (ReferenceEquals(childBox.Box, box))
                return top;

            if (!childBox.Box.IsOutOfFlow && childBox.WritingDirection.BlockStart == writingDirection.BlockStart && FindBoxStart(childBox.ChildrenSpan, childBox.Size, writingDirection, box, top) is { } result)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Returns the box that starts the content after a break of the strip, when the break is before an in-flow block-level box of
    /// the block flow of the root element (the box and its block containers are laid out the same way in a strip of another
    /// inline size), and the offset of the break from its block-start border edge (the margin preserved after a forced break);
    /// else null (a break inside a line box, a flex container...).
    /// </summary>
    private static (ContainerBox Box, double Offset)? FindBreakAnchor(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, double position, double parentTop, BoxFragment? parent = null)
    {
        const double Epsilon = 0.01;
        var inFlowCount = parent is null ? children.Length : Math.Min(parent.InFlowChildCount, children.Length);
        for (var i = 0; i < inFlowCount; i++)
        {
            var child = children[i];
            if (child is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true })
                continue;

            var (top, bottom, _) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            if (top >= position - Epsilon)
            {
                // (the box after the break: at the break, or after its block-start margin preserved after a forced break)
                if (child is not BoxFragment box || box.Box.IsInlineBox || box.Box.IsAtomicInline || top - position > Math.Max(0, writingDirection.ToLogical(box.Margin).BlockStart) + Epsilon)
                    return null;

                return (box.Box, position - top);
            }

            if (bottom > position + Epsilon)
            {
                return child is BoxFragment container && IsBlockFlowContainer(container, writingDirection)
                    ? FindBreakAnchor(container.ChildrenSpan, container.Size, writingDirection, position, top, container)
                    : null;
            }
        }

        return null;

        static bool IsBlockFlowContainer(BoxFragment box, WritingDirection writingDirection)
            => box.Box.Kind is ContainerBoxKind.Normal && !box.Box.ChildrenAreInline && box.Box.Display.Inside is DisplayInside.Flow or DisplayInside.FlowRoot
                && !box.Box.Style.Layout.IsMultiColumnContainer && box.WritingDirection.BlockStart == writingDirection.BlockStart
                && !FragmentationFlow.IsMonolithic(box, writingDirection, scrollContainersAreMonolithic: false);
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
    /// box has blank pages.
    /// </summary>
    internal DisplayList PaintPage(LayoutPage page)
    {
        using var suspension = _document.GetStyleEngine().SuspendUpdates();
        var geometry = page.Geometry;
        var builder = new DisplayListBuilder();

        // The page sheet is white paper
        builder.Add(new DrawPaintItem(new DisplayPaint(DisplayColor.White)));
        List<(BoxFragment Fragment, int ZIndex)>? marginBoxes = null;
        var viewport = _boxTree?.Viewport;
        // (as in Gecko, a document whose root element generates no box prints blank pages, without page boxes)
        if (geometry.Style is { } style && viewport is { IsEmpty: false })
        {
            // As in the browsers, the background of the page box covers the whole page, its margins included (whatever its
            // background-clip); its borders are in the page margins
            var background = new ContainerBox(ContainerBoxKind.Normal, style, Display.Block, style.Element, StyleEngine.PageContextPseudoElement);
            var backgroundFragment = new BoxFragment(background, geometry.PageSize, default, default, default, children: null);
            var borderStyle = style.CreateOverriddenStyle([("background-color", "transparent"), ("background-image", "none")]);
            var pageBox = new ContainerBox(ContainerBoxKind.Normal, borderStyle, Display.Block, style.Element, StyleEngine.PageContextPseudoElement);
            var borderBox = geometry.BorderBox;
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
        // (the origin of the page area is snapped to the device pixels: the document snaps its painting to them)
        var area = geometry.PageArea;
        var paddingBox = geometry.PaddingBox;
        var devicePixelRatio = _document.GetStyleEngine().Environment.DevicePixelRatio;
        builder.Save();
        builder.Add(new ClipRectItem(paddingBox));
        builder.Transform(DisplayMatrix.Translation(Math.Round(area.X * devicePixelRatio, MidpointRounding.AwayFromZero) / devicePixelRatio, Math.Round(area.Y * devicePixelRatio, MidpointRounding.AwayFromZero) / devicePixelRatio));
        builder.AddList(Painter.Paint(this, _document, page.Content, PhysicalOffset.Zero, DisplayColor.Transparent));
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
