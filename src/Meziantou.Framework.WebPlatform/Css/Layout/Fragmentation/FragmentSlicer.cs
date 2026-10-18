using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A fragmentainer (https://drafts.csswg.org/css-break-3/#fragmentation-container) of a fragmentation context: a column box.</summary>
/// <param name="Start">The position of the strip where its content starts.</param>
/// <param name="End">The position of the strip where its content ends (the start of the next fragmentainer, the start of a column spanner, or +∞).</param>
/// <param name="BlockSize">The block size of the fragmentainer: the boxes broken at its end extend to it.</param>
/// <param name="DisplayOffset">The space taken at its start by the block-start edges repeated by the boxes with <c>box-decoration-break: clone</c>.</param>
/// <param name="Position">The position of the fragmentainer in the content box of the fragmentation context root (logical).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Fragmentainer(double Start, double End, double BlockSize, double DisplayOffset, LogicalOffset Position);

/// <summary>
/// Distributes the fragments of a strip (see <see cref="FragmentationFlow"/>) to the fragmentainers chosen by the breaker
/// (https://drafts.csswg.org/css-break-3/#box-splitting): a fragment whose subtree is in one fragmentainer is moved there; a box
/// that spans several fragmentainers is split into one fragment per fragmentainer (<see cref="BoxFragment.IsFirstFragment"/>,
/// <see cref="BoxFragment.IsLastFragment"/>), whose children are distributed recursively.
/// </summary>
/// <remarks>
/// <para>
/// As in the browsers, the fragment of a box that is broken at the end of a fragmentainer extends to the end of the fragmentainer,
/// and the content that overflows the end of a box continues in empty fragments of the box in the next fragmentainers (the
/// overflow of a box that clips it, https://drafts.csswg.org/css-overflow-3/#valdef-overflow-clip, stays in its last fragment).
/// The block-start border and padding of a box are on its first fragment and the block-end ones on the fragment where its border
/// box ends (<c>box-decoration-break: slice</c>), or on every fragment (<c>clone</c>, https://drafts.csswg.org/css-break-3/#break-decoration).
/// The line boxes and the monolithic boxes go to the fragmentainer where they start.
/// </para>
/// </remarks>
internal sealed class FragmentSlicer
{
    private readonly FragmentationFlow _flow;
    private readonly FlowMap _map;
    private readonly List<Fragmentainer> _fragmentainers;
    private readonly WritingDirection _writingDirection;
    private Dictionary<FlowMap, FlowMap>? _extendedMaps;
    private double[]? _noOpenOffsets;

    /// <summary>
    /// The first fragmentainer after the last column spanner that was met: the in-flow content that follows a spanner is in the
    /// columns after it, even when a negative margin places it before the spanner in the strip.
    /// </summary>
    private int _firstAfterSpanners;

    /// <param name="flow">The strip.</param>
    /// <param name="map">The breaks of the content of the fragmentation context root.</param>
    /// <param name="fragmentainers">The fragmentainers.</param>
    /// <param name="writingDirection">The writing mode of the fragmentation context.</param>
    public FragmentSlicer(FragmentationFlow flow, FlowMap map, List<Fragmentainer> fragmentainers, WritingDirection writingDirection)
    {
        _flow = flow;
        _map = map;
        _fragmentainers = fragmentainers;
        _writingDirection = writingDirection;
    }

    /// <summary>A fragment placed in a fragmentainer: its logical offset in the fragmentainer (the block offset after the display offset).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct PlacedFragment(Fragment Fragment, LogicalOffset Offset);

    /// <summary>The fragments of a fragment of the strip in the fragmentainers it is in, from the fragmentainer <paramref name="First"/>, and their block offsets in the fragmentainers.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Slices(int First, (Fragment Fragment, double BlockOffset)[] Fragments)
    {
        public int Last => First + Fragments.Length - 1;

        /// <summary>The fragmentainer where the border box ends.</summary>
        public int BoxEnd { get; init; } = First;

        /// <summary>
        /// The offsets of the inline-start edges of the fragments from the inline-start edge of the fragment of the strip (logical),
        /// or null when they are all 0 (see <see cref="InlineVariant"/>).
        /// </summary>
        public double[]? InlineShifts { get; init; }

        /// <summary>Returns the offset of the inline-start edge of the fragment in a fragmentainer from the one of the fragment of the strip (see <see cref="InlineShifts"/>).</summary>
        public double GetInlineShift(int index) => InlineShifts?[index - First] ?? 0;
    }

    /// <summary>
    /// The outside markers of the strip that are aligned with the first line of a column spanner (the list item is the
    /// fragmentation context root and the spanner is its first content): the index of the spanner, the marker, and its logical
    /// offset from the block-start edge of the margin box of the spanner. They are placed with the spanner.
    /// </summary>
    public List<(int Spanner, Fragment Marker, LogicalOffset Offset)>? SpannerMarkers { get; private set; }

    /// <summary>
    /// Distributes the fragments of the strip to the fragmentainers: returns, for each fragmentainer, its fragments with their
    /// logical offsets in it. The fragments of the column spanners are skipped.
    /// </summary>
    public List<PlacedFragment>[] Slice(ReadOnlySpan<Fragment> children, PhysicalSize stripSize, HashSet<ContainerBox>? spannerBoxes)
    {
        var result = new List<PlacedFragment>[_fragmentainers.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = [];
        }

        var noOpenOffsets = new double[_fragmentainers.Count];
        var siblingEnd = 0;
        foreach (var child in children)
        {
            var (top, bottom, inline) = FragmentationFlow.GetBlockRange(child, stripSize, _writingDirection, parentTop: 0);
            if (child is BoxFragment { Box: var spanner } spannerFragment && spannerBoxes is not null && spannerBoxes.Contains(spanner))
            {
                SkipSpanner(spannerFragment, bottom);
                continue;
            }

            if (child is BoxFragment { Box.IsOutsideMarker: true } && GetSpannerAt(top) is var spannerIndex and >= 0)
            {
                (SpannerMarkers ??= []).Add((spannerIndex, child, new LogicalOffset(inline, top - _flow.Spanners[spannerIndex].MarginBoxStart)));
                continue;
            }

            var isOutOfFlow = child is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true };
            var childMap = child is BoxFragment floatChild && isOutOfFlow && _flow.GetFloatMap(floatChild) is { } floatMap ? floatMap : _map;
            (int Index, double Offset)? childStart = null;
            (int Index, double Offset)? childEnd = null;
            if (isOutOfFlow && child is BoxFragment stripBox && _flow.GetOutOfFlowItem(stripBox) is { Map: { } stripMap, OutOfFlow: { } placement } stripItem)
            {
                // An absolutely positioned box of the strip that is fragmented apart (its static position is in a nested multi-column container)
                childMap = stripMap;
                childStart = (placement.StartIndex, placement.StartOffset);
                childEnd = stripItem.IsBoxDone ? (stripItem.EndIndex, stripItem.EndOffset) : null;
                inline += GetInlineShift(placement, _writingDirection);
            }

            var slices = SliceAll(child, top, bottom, childMap, childMap, childEnd, minFirst: isOutOfFlow ? 0 : Math.Max(_firstAfterSpanners, siblingEnd), cap: _fragmentainers.Count - 1, noOpenOffsets, spannerBoxes, childStart);
            if (!isOutOfFlow)
            {
                siblingEnd = Math.Max(siblingEnd, slices.BoxEnd);
            }
            for (var index = slices.First; index <= slices.Last; index++)
            {
                var (fragment, blockOffset) = slices.Fragments[index - slices.First];
                result[index].Add(new PlacedFragment(fragment, new LogicalOffset(inline + slices.GetInlineShift(index), blockOffset)));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the fragments of a monolithic fragment (a monolithic box, a line box) that the pages slice (see
    /// <see cref="FragmentationFlow.SlicesMonolithicContent"/>): the fragment in the page where it starts, and a copy of it in
    /// each next page it or its overflowing contents are in, offset by the block size of the previous pages (the page area clips it).
    /// </summary>
    private Slices SliceMonolithic(Fragment fragment, double top, double bottom, FlowMap map, int first, double topOffset, int cap)
    {
        // (with the contents that overflow a monolithic box, see FragmentationFlow.GetSubtreeEnd)
        var last = Math.Clamp(map.MapEnd(_flow.GetSubtreeEnd(fragment, bottom), top).Index, first, Math.Max(first, cap));
        if (last == first)
            return new Slices(first, [(fragment, topOffset)]);

        var result = new (Fragment, double)[last - first + 1];
        result[0] = (fragment, topOffset);
        for (var index = first + 1; index <= last; index++)
        {
            result[index - first] = (fragment.CloneSubtree(), map.GetOffsetIn(index, top));
        }

        return new Slices(first, result) { BoxEnd = last };
    }

    /// <summary>Returns the index of the fragmentainer that contains a position of the strip (in the flow of the fragmentation context root).</summary>
    public int GetFragmentainerIndex(double position) => _map.Map(position).Index;

    /// <summary>Returns the block offset of a position of the strip in a fragmentainer.</summary>
    public double ToFragmentainerOffset(int index, double position) => _map.GetOffsetIn(index, position);

    private bool IsSplittable(Fragment fragment) => fragment is BoxFragment box && !_flow.IsMonolithic(box);

    /// <summary>
    /// Returns the fragments of a fragment of the strip in the fragmentainers it is in (the fragment itself when it is entirely in
    /// one, else a new fragment of its box per fragmentainer) and their block offsets in the fragmentainers.
    /// </summary>
    /// <param name="fragment">The fragment.</param>
    /// <param name="top">The block-start edge of its border box in the strip.</param>
    /// <param name="bottom">The block-end edge of its border box in the strip.</param>
    /// <param name="map">The breaks of the flow of the fragment and of its in-flow contents.</param>
    /// <param name="outOfFlowMap">The breaks of the flow that places the block-end edge of the fragment: its absolutely positioned children are placed in its border box.</param>
    /// <param name="end">The fragmentainer and offset of the block-end edge of the border box when it is not the one of <paramref name="bottom"/> in <paramref name="map"/> (the root of a parallel flow).</param>
    /// <param name="minFirst">The first fragmentainer of the parent.</param>
    /// <param name="cap">The last fragmentainer of the nearest ancestor that clips its overflow.</param>
    /// <param name="openOffsets">
    /// For each fragmentainer, the block offset of the start of the content of the parent when the parent continues from a
    /// previous fragmentainer (after its repeated block-start edges).
    /// </param>
    /// <param name="spannerBoxes">The column spanners.</param>
    /// <param name="start">The fragmentainer and offset of the block-start edge of the border box when it is not the one of <paramref name="top"/> in <paramref name="map"/> (an absolutely positioned box that is fragmented apart).</param>
    /// <param name="closeOffsets">
    /// For each fragmentainer, the block offset where a fragment that is broken at its end ends when it is not the end of the
    /// fragmentainer (before the table footer that is repeated there), or null.
    /// </param>
    private Slices SliceAll(Fragment fragment, double top, double bottom, FlowMap map, FlowMap outOfFlowMap, (int Index, double Offset)? end, int minFirst, int cap, double[] openOffsets, HashSet<ContainerBox>? spannerBoxes, (int Index, double Offset)? start = null, double[]? closeOffsets = null)
    {
        // A box that contains a column spanner is interrupted by it: the extent of the spanner in the strip is not a part of the box
        var unclampedTop = top;
        var parentMap = map;
        if (_flow.Spanners.Count > 0 && fragment is BoxFragment)
        {
            top = ClampToColumns(top, isEnd: false);
            bottom = Math.Max(top, ClampToColumns(bottom, isEnd: true));
        }

        if (end is null && fragment is BoxFragment definiteBox && _flow.GetDefiniteBox(definiteBox) is { Map: { } contentMap } definiteItem && !ReferenceEquals(contentMap, map))
        {
            // A box whose block size does not follow its contents: its border box is placed by the flow of its container, its
            // contents are a parallel flow
            end = definiteItem.IsBoxDone ? (definiteItem.EndIndex, definiteItem.EndOffset) : map.MapEnd(bottom, top, growsWithTrack: false);
            outOfFlowMap = map = contentMap;
        }

        // A box without block size at a break is before the break when the break follows it in the content
        var place = map.Map(top);
        if (fragment is BoxFragment && bottom - top <= 0.01 && _flow.GetEmptyOrdinal(fragment) is { } ordinal)
        {
            place = map.MapEmpty(top, ordinal);
            end ??= (Math.Clamp(place.Index, minFirst, Math.Max(minFirst, cap)), map.GetOffsetIn(Math.Clamp(place.Index, minFirst, Math.Max(minFirst, cap)), top));
        }

        if (_flow.GetForcedStart(fragment) is { } forcedStart)
        {
            // An item that starts a fragmentainer after a forced break, before the position of the break (a negative margin)
            place = (map.Map(forcedStart).Index, 0);
        }

        var first = Math.Clamp(start?.Index ?? place.Index, minFirst, Math.Max(minFirst, cap));
        var topOffset = start is var (startIndex, startOffset) && startIndex == first ? startOffset : map.GetOffsetIn(first, top);
        if (fragment is BoxFragment nestedBox && _flow.GetNestedColumns(nestedBox) is { } nestedItem)
            return SliceNestedColumns(nestedBox, nestedItem, top, bottom, map, end, first, topOffset, cap, openOffsets);

        if (fragment is not BoxFragment box || !IsSplittable(fragment))
            return _flow.SlicesMonolithicContent ? SliceMonolithic(fragment, top, bottom, map, first, topOffset, cap) : new Slices(first, [(fragment, topOffset)]);

        // The fragmentainer where the border box ends; the next ones only contain overflowing descendants
        var (endIndex, endOffset) = end ?? map.MapEnd(bottom, top);
        var borderBoxLast = Math.Clamp(endIndex, first, Math.Max(first, cap));
        var hasGroup = _flow.ContainsGroup(box);

        // (a box that ends in a margin that is truncated at a break is shorter than in the strip)
        var isTruncated = endIndex == first && endOffset - topOffset < _writingDirection.ToLogical(box.Size).Block - 0.01 && bottom - top > endOffset - topOffset + 0.01;

        // (a box that grows without being broken: its contents are taller than the box in the strip, or a track of its parallel flows grows)
        isTruncated |= endIndex == first && endOffset - topOffset > bottom - top + 0.01 && (end is not null || hasGroup) && _flow.IsRootGrowingUnbroken;

        // A fragment in a single fragmentainer is kept, unless it contains a column spanner (placed by the multi-column container)
        // or parallel flows (which have their own breaks)
        if (!hasGroup && !isTruncated && (spannerBoxes is null || !ContainsSpanner(box, spannerBoxes)))
        {
            var last = Math.Clamp(Math.Max(borderBoxLast, map.MapEnd(ClampToColumns(_flow.GetSubtreeEnd(fragment, bottom), isEnd: true), top).Index), first, Math.Max(first, cap));
            if (first == last)
                return new Slices(first, [(fragment, topOffset)]);
        }

        var clone = box.Box.Style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
        var writingDirection = box.WritingDirection;
        var inlineSize = writingDirection.ToLogical(box.Size).Inline;

        // A box laid out in other areas for its next fragments (see InlineVariant): the inline size and the position of each
        // fragment (the content of the fragment of the strip is placed in these areas)
        var variants = _flow.Relayout?.GetInlineVariants(box);
        double[]? inlineShifts = null;
        var startEdges = _writingDirection.ToLogical(box.Border.Add(box.Padding)).BlockStart;

        // The start of the content of the fragment in the fragmentainers where it continues
        var childOpenOffsets = openOffsets;
        if (clone && startEdges != 0)
        {
            childOpenOffsets = new double[openOffsets.Length];
            for (var index = 0; index < childOpenOffsets.Length; index++)
            {
                // (the contents that overflow the box after its last fragment are not after repeated edges)
                childOpenOffsets[index] = openOffsets[index] + (index != first && index <= borderBoxLast ? startEdges : 0);
            }
        }

        // https://drafts.csswg.org/css-tables-3/#repeated-headers: the sections of a table continue after its repeated header,
        // and the ones that are broken end before its repeated footer
        var repeated = box.Box.Kind == ContainerBoxKind.TableGrid ? _flow.GetRepeatedSections(box) : null;
        var childCloseOffsets = closeOffsets;
        var endEdges = _writingDirection.ToLogical(box.Border.Add(box.Padding)).BlockEnd;
        if (clone && endEdges != 0)
        {
            // The contents that are broken end before the block-end edges that the box repeats (not in its last fragment, whose
            // block-end edges are not repeated ones: the contents that overflow the box are over them)
            childCloseOffsets = new double[openOffsets.Length];
            for (var index = 0; index < childCloseOffsets.Length; index++)
            {
                childCloseOffsets[index] = (closeOffsets?[index] ?? GetBreakEnd(map, index)) - (index >= first && index < borderBoxLast ? endEdges : 0);
            }
        }

        if (repeated is { Footer: not null, IsFooterDisabled: false })
        {
            var outerCloseOffsets = childCloseOffsets;
            childCloseOffsets = new double[openOffsets.Length];
            for (var index = 0; index < childCloseOffsets.Length; index++)
            {
                childCloseOffsets[index] = (outerCloseOffsets?[index] ?? GetBreakEnd(map, index)) - (index < borderBoxLast ? repeated.FooterSize : 0);
            }
        }

        if (repeated is { Header: not null, IsHeaderDisabled: false })
        {
            if (ReferenceEquals(childOpenOffsets, openOffsets))
            {
                childOpenOffsets = [.. openOffsets];
            }

            for (var index = first + 1; index < childOpenOffsets.Length; index++)
            {
                childOpenOffsets[index] += repeated.HeaderSize;
            }
        }

        // A box that avoids the floats continues below the floats that were pushed to a fragmentainer
        double[]? startShifts = null;
        if (_flow.HasPushedFloats || !ReferenceEquals(map, _map))
        {
            for (var index = first + 1; index <= borderBoxLast && index < openOffsets.Length; index++)
            {
                if (map.GetStartShift(index, box) is var startShift and > 0)
                {
                    if (ReferenceEquals(childOpenOffsets, openOffsets))
                    {
                        childOpenOffsets = [.. openOffsets];
                    }

                    childOpenOffsets[index] += startShift;
                    (startShifts ??= new double[openOffsets.Length])[index] = startShift;
                }
            }
        }

        // The children of the fragment in each fragmentainer (their ranges are computed before any of them is moved)
        var childCap = (_writingDirection.IsHorizontal ? box.ClipsOverflowY : box.ClipsOverflowX) ? Math.Min(cap, borderBoxLast) : cap;
        var children = box.ChildrenSpan;
        var inFlowCount = box.InFlowChildCount;
        var group = hasGroup ? _flow.GetGroup(box) : null;
        var childSlices = new Dictionary<int, List<(Fragment Fragment, double Inline, double BlockOffset, bool InFlow)>>();
        var lastIndex = borderBoxLast;

        // The last fragment with a section that is not the repeated header or footer, and the fragment of the footer
        var lastBodyIndex = first;
        var footerIndex = borderBoxLast;

        // (an absolutely positioned descendant placed before the box may be in a fragmentainer before the first one of the box)
        var firstIndex = first;

        // The fragmentainer where the in-flow sibling before a child ends: the child is not before it (a negative margin places
        // the child before the end of its sibling, at a negative offset in the fragmentainer when the sibling is broken)
        var siblingEnd = first;
        var isBlockFlow = box.Box.IsBlockContainer && !box.Box.EstablishesIndependentChildrenLayout;
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var (childTop, childBottom, childInline) = FragmentationFlow.GetBlockRange(child, box.Size, writingDirection, unclampedTop);
            if (child is BoxFragment { Box: var spanner } spannerFragment && spannerBoxes is not null && spannerBoxes.Contains(spanner))
            {
                SkipSpanner(spannerFragment, childBottom);
                continue;
            }

            var isInFlow = i < inFlowCount;
            var childMap = map;
            var childOutOfFlowMap = map;
            (int Index, double Offset)? childEnd = null;
            (int Index, double Offset)? childStart = null;
            if (!isInFlow && child is BoxFragment outOfFlowChild && _flow.GetOutOfFlowItem(outOfFlowChild) is { Map: { } outOfFlowChildMap, OutOfFlow: { } placement })
            {
                // An absolutely positioned child that is fragmented apart: the breaker placed it from its offset in the box
                childMap = childOutOfFlowMap = outOfFlowChildMap;
                childStart = (placement.StartIndex, placement.StartOffset);
                if (_flow.GetOutOfFlowItem(outOfFlowChild) is { IsBoxDone: true } placedItem)
                {
                    childEnd = (placedItem.EndIndex, placedItem.EndOffset);
                }

                // (a static position in a nested multi-column container: in the column where the breaker placed it)
                childInline += GetInlineShift(placement, writingDirection);
            }
            else if (isInFlow && child is BoxFragment { Box.IsOutOfFlow: true } floatChild && _flow.GetFloatMap(floatChild) is { } floatMap)
            {
                // A float that was pushed to the start of a fragmentainer and is fragmented apart (the content beside it is shifted)
                childMap = childOutOfFlowMap = floatMap;
            }
            else if (!isInFlow)
            {
                // The absolutely positioned children are placed in the border box of the fragments: a fragment that is broken
                // extends to the end of its fragmentainer, after the position of the break in the strip
                childMap = childOutOfFlowMap = GetExtendedMap(outOfFlowMap);
            }
            else if (group is not null && group.ItemsByFragment.TryGetValue(child, out var item) && item.Map is { } itemMap)
            {
                // The root of a parallel flow: its block-end edge is placed by the flow of its container, or follows its contents
                if (item.Box is not null && !item.IsMonolithic)
                {
                    var containerEnd = map.MapEnd(childBottom, childTop, growsWithTrack: item.Stretches);
                    childEnd = containerEnd;
                    if (item.CanGrow)
                    {
                        var contentEnd = itemMap.MapEnd(item.IntrinsicEnd, childTop);
                        if (contentEnd.Index > containerEnd.Index || (contentEnd.Index == containerEnd.Index && contentEnd.Offset > containerEnd.Offset))
                        {
                            childEnd = contentEnd;
                        }

                        childOutOfFlowMap = itemMap;
                    }

                    if (item.IsRowSubgrid && itemMap.MapEnd(item.IntrinsicEnd, childTop) is var rowsEnd
                        && (item.CanGrow || rowsEnd.Index < containerEnd.Index || (rowsEnd.Index == containerEnd.Index && rowsEnd.Offset < containerEnd.Offset)))
                    {
                        // (a subgrid ends with its rows, without the gutters that disappeared at its breaks)
                        childEnd = rowsEnd;
                    }
                }
                else
                {
                    childOutOfFlowMap = itemMap;
                }

                childMap = itemMap;
            }

            var isInFlowContent = isInFlow && child is not BoxFragment { Box.IsOutOfFlow: true } and not BoxFragment { Box.IsOutsideMarker: true };
            var childMinFirst = isInFlowContent && ReferenceEquals(childMap, _map) ? Math.Max(first, Math.Min(_firstAfterSpanners, Math.Max(first, childCap))) : first;
            if (isBlockFlow && isInFlowContent && ReferenceEquals(childMap, map))
            {
                childMinFirst = Math.Max(childMinFirst, Math.Min(siblingEnd, Math.Max(first, childCap)));
            }

            // (an absolutely positioned child that is fragmented apart is not placed after the edges that the boxes around it repeat)
            var slices = SliceAll(child, childTop, childBottom, childMap, childOutOfFlowMap, childEnd, childStart is null ? childMinFirst : 0, childCap, childStart is null ? childOpenOffsets : (_noOpenOffsets ??= new double[_fragmentainers.Count]), spannerBoxes, childStart, isInFlowContent || (isInFlow && clone) ? childCloseOffsets : null);
            if (isInFlowContent && ReferenceEquals(childMap, map))
            {
                siblingEnd = Math.Max(siblingEnd, slices.BoxEnd);
            }

            for (var index = slices.First; index <= slices.Last; index++)
            {
                if (!childSlices.TryGetValue(index, out var list))
                {
                    childSlices[index] = list = [];
                }

                var (childFragment, childOffset) = slices.Fragments[index - slices.First];
                list.Add((childFragment, childInline + slices.GetInlineShift(index), childOffset, isInFlow));
            }

            firstIndex = Math.Min(firstIndex, slices.First);
            lastIndex = Math.Max(lastIndex, slices.Last);
            if (repeated is not null && isInFlowContent)
            {
                if (ReferenceEquals(child, repeated.Footer))
                {
                    footerIndex = slices.First;
                }
                else if (!ReferenceEquals(child, repeated.Header))
                {
                    lastBodyIndex = Math.Max(lastBodyIndex, Math.Min(slices.BoxEnd, borderBoxLast));
                }
            }
        }

        // https://drafts.csswg.org/css-break-3/#break-decoration: the edges of the box at the breaks
        var blockStart = _writingDirection.BlockStart;
        var blockEnd = _writingDirection.BlockEnd;
        var result = new (Fragment Fragment, double BlockOffset)[lastIndex - firstIndex + 1];
        if (result.Length == 1 && !isTruncated && (spannerBoxes is null || !ContainsSpanner(box, spannerBoxes)))
            return new Slices(first, [(fragment, topOffset)]);

        for (var index = firstIndex; index <= lastIndex; index++)
        {
            // The extent of the fragment in the fragmentainer: a fragment broken at the end of a fragmentainer extends to its end
            // (the fragments before the start of the box and after its end are empty)
            var sliceTop = index == first ? topOffset : index < first ? _fragmentainers[index].BlockSize : openOffsets[index] + (startShifts?[index] ?? 0);
            var sliceBottom = index > borderBoxLast || index < first ? sliceTop
                : index == borderBoxLast ? Math.Max(sliceTop, endIndex == index ? endOffset : map.GetOffsetIn(index, bottom))
                : Math.Max(sliceTop, closeOffsets?[index] ?? GetBreakEnd(map, index));
            var isPastEnd = index > borderBoxLast || index < first;
            var border = box.Border;
            var padding = box.Padding;
            var margin = box.Margin;
            if (isPastEnd || (index != first && !clone))
            {
                border = border.With(blockStart, 0);
                padding = padding.With(blockStart, 0);
            }

            if (isPastEnd || (index != borderBoxLast && !clone))
            {
                border = border.With(blockEnd, 0);
                padding = padding.With(blockEnd, 0);
            }

            if (index != first)
            {
                margin = margin.With(blockStart, 0);
            }

            if (index != borderBoxLast)
            {
                margin = margin.With(blockEnd, 0);
            }

            var variant = variants is not null && index > first && parentMap.GetRange(index) is var (variantStart, _) ? StripRelayout.FindVariant(variants, variantStart - unclampedTop) : null;
            var inlineShift = variant?.InlineDelta ?? 0;
            if (inlineShift != 0)
            {
                (inlineShifts ??= new double[lastIndex - firstIndex + 1])[index - firstIndex] = inlineShift;
            }

            var size = writingDirection.ToPhysical(new LogicalSize(variant?.InlineSize ?? inlineSize, sliceBottom - sliceTop));
            List<Fragment>? inFlow = null;
            List<BoxFragment>? outOfFlow = null;
            double? firstChildBaseline = null;
            double? lastChildBaseline = null;
            if (repeated is not null && !isPastEnd)
            {
                // A copy of the header at the start of the fragments after the first one, and a copy of the footer at the end of
                // the fragments before the last one
                if (!childSlices.TryGetValue(index, out var sections))
                {
                    childSlices[index] = sections = [];
                }

                if (repeated is { Header: { } header, IsHeaderDisabled: false } && index > first && index <= lastBodyIndex)
                {
                    var inline = FragmentationFlow.GetBlockRange(header, box.Size, writingDirection, 0).Inline;
                    sections.Insert(0, (header.CloneSubtree(), inline, openOffsets[index] + (clone ? startEdges : 0), true));
                }

                if (repeated is { Footer: { } footer, IsFooterDisabled: false } && index < footerIndex)
                {
                    // As in Blink, the footer follows the last row of the fragment: a row that is broken extends to the end of the
                    // fragmentainer (less the footer), a break between two rows leaves the footer after the row before it
                    var inline = FragmentationFlow.GetBlockRange(footer, box.Size, writingDirection, 0).Inline;
                    var footerOffset = Math.Max(sliceBottom, _fragmentainers[index].BlockSize) - _writingDirection.ToLogical(border.Add(padding)).BlockEnd - repeated.FooterBlockSize;
                    if (map.GetRange(index + 1) is var (breakPosition, _) && GetRowEndBefore(box, unclampedTop, breakPosition) is { } rowEnd)
                    {
                        footerOffset = Math.Min(footerOffset, map.MapEnd(rowEnd, top).Offset + repeated.FooterSize - repeated.FooterBlockSize);
                    }

                    sections.Add((footer.CloneSubtree(), inline, footerOffset, true));
                }
            }

            if (childSlices.TryGetValue(index, out var placed))
            {
                foreach (var (childFragment, childInline, childOffset, isInFlow) in placed)
                {
                    var physical = writingDirection.ToPhysical(new LogicalOffset(childInline - inlineShift, childOffset - sliceTop), size, childFragment.Size);
                    if (childFragment is BoxFragment childBox)
                    {
                        physical += childBox.RelativeOffset + childBox.StickyOffset;
                    }

                    childFragment.Offset = physical;
                    if (isInFlow)
                    {
                        (inFlow ??= []).Add(childFragment);

                        // The baselines of the content of the fragment (the line boxes and the boxes of the same writing mode)
                        var (childFirst, childLast) = childFragment switch
                        {
                            LineBoxFragment line => (line.Baseline, line.Baseline),
                            BoxFragment { Box.IsOutOfFlow: false } inFlowBox when inFlowBox.WritingDirection.BlockStart == _writingDirection.BlockStart => (inFlowBox.FirstBaseline, inFlowBox.LastBaseline),
                            _ => ((double?)null, (double?)null),
                        };
                        firstChildBaseline ??= childOffset - sliceTop + childFirst;
                        lastChildBaseline = childOffset - sliceTop + childLast ?? lastChildBaseline;
                    }
                    else
                    {
                        (outOfFlow ??= []).Add((BoxFragment)childFragment);
                    }
                }
            }

            // The baselines in the fragment that contains them; the other fragments have the baselines of their own content
            double? firstBaseline = box.FirstBaseline is { } baseline && map.Map(top + baseline) is var firstPlace && firstPlace.Index == index ? firstPlace.Offset - sliceTop : null;
            double? lastBaseline = box.LastBaseline is { } lastValue && map.Map(top + lastValue) is var lastPlace && lastPlace.Index == index ? lastPlace.Offset - sliceTop : null;
            if (box.FirstBaseline is not null && group is null)
            {
                firstBaseline ??= firstChildBaseline;
                lastBaseline ??= lastChildBaseline;
            }

            var slice = box.CreateSlice(size, border, padding, margin, inFlow?.ToArray(), outOfFlow, isFirst: box.IsFirstFragment && index == firstIndex, isLast: box.IsLastFragment && index == lastIndex, firstBaseline, lastBaseline);
            if (index < first)
            {
                slice.DisableOverflowClip();
            }

            if (box.GapGeometry is { } gaps && index >= first && map.GetRange(index) is var (rangeStart, rangeEnd))
            {
                // The gaps of the part of the container in the fragmentainer, in the coordinates of its fragment: their block
                // offsets are the ones of the fragmented content (the breaks of the flow of the container, and of the
                // parallel flows of the lines of a multi-line column flex container)
                var fragmentainer = index;
                var fragmentTop = sliceTop;
                var containerTop = top;
                var flowMap = map;
                var mainSlice = new GapFlowSlice(index == first ? double.NegativeInfinity : rangeStart - top, rangeEnd - top, offset => flowMap.GetOffsetIn(fragmentainer, containerTop + offset) - fragmentTop);
                Func<int, GapFlowSlice?>? getFlow = null;
                if (group is not null && gaps.Kind == GapContainerKind.Flex)
                {
                    var lineGroup = group;
                    getFlow = key =>
                    {
                        foreach (var item in lineGroup.Items)
                        {
                            if (item.Line == key - 1 && item.Map is { } lineMap)
                            {
                                return lineMap.GetRange(fragmentainer) is var (lineStart, lineEnd)
                                    ? new GapFlowSlice(lineMap.IsFirst(fragmentainer) ? double.NegativeInfinity : lineStart - containerTop, lineEnd - containerTop, offset => lineMap.GetOffsetIn(fragmentainer, containerTop + offset) - fragmentTop)
                                    : null;
                            }
                        }

                        return null;
                    };
                }

                // (the gaps between the tracks that overflow a container whose block-end edge is in the fragmentainer, or before
                // it, run to the end of the fragmentainer)
                var contentEnd = index >= borderBoxLast
                    ? Math.Max(sliceBottom, _fragmentainers[index].BlockSize) - sliceTop
                    : sliceBottom - sliceTop - _writingDirection.ToLogical(border.Add(padding)).BlockEnd;
                if (index < borderBoxLast && inFlow is not null)
                {
                    // (as in Blink, a gap that continues runs to the end of the monolithic items that overflow the fragmentainer)
                    foreach (var inFlowChild in inFlow)
                    {
                        if (inFlowChild is BoxFragment { Box.IsOutOfFlow: false } itemFragment && _flow.IsMonolithic(itemFragment))
                        {
                            var itemRect = writingDirection.ToLogical(itemFragment.Rect.Translate(-(itemFragment.RelativeOffset + itemFragment.StickyOffset)), size);
                            contentEnd = Math.Max(contentEnd, itemRect.Offset.Block + itemRect.Size.Block);
                        }
                    }
                }

                // (a gap between two items of a line of a row flex container needs a fragment of the item after it)
                Func<ContainerBox, bool>? hasFragment = null;
                if (gaps.Kind == GapContainerKind.Flex)
                {
                    var sliceItems = inFlow ?? [];
                    hasFragment = itemBox =>
                    {
                        foreach (var sliceItem in sliceItems)
                        {
                            if (sliceItem is BoxFragment itemSlice && ReferenceEquals(itemSlice.Box, itemBox))
                                return true;
                        }

                        return false;
                    };
                }

                slice.GapGeometry = gaps.Slice(mainSlice, getFlow, contentEnd, hasFragment);
            }

            result[index - firstIndex] = (slice, sliceTop);
        }

        Stitch(box, result, clone);
        return new Slices(firstIndex, result) { BoxEnd = borderBoxLast, InlineShifts = inlineShifts };
    }

    /// <summary>
    /// Returns the offset in a fragmentainer where the boxes of a flow that are broken at its end end: the block size of the
    /// fragmentainer (with the unbreakable content of the strip that overflows it), or for a parallel flow the offset where
    /// that flow is broken (it is not broken where the content of another flow that overflows the fragmentainer ends).
    /// </summary>
    private double GetBreakEnd(FlowMap map, int index)
        => ReferenceEquals(map, _map) ? _fragmentainers[index].BlockSize : Math.Min(_fragmentainers[index].BlockSize, map.GetBreakEnd(index) ?? _fragmentainers[index].BlockSize);

    /// <summary>
    /// Returns the block-end edge in the strip of the last row of a table grid box before a break when the break is between two
    /// rows, or null when it is inside a row.
    /// </summary>
    private double? GetRowEndBefore(BoxFragment tableGrid, double top, double breakPosition)
    {
        double? result = null;
        var repeated = _flow.GetRepeatedSections(tableGrid);
        foreach (var child in tableGrid.ChildrenSpan)
        {
            if (child is not BoxFragment { Box.IsOutOfFlow: false } childBox || ReferenceEquals(childBox, repeated?.Header) || ReferenceEquals(childBox, repeated?.Footer))
                continue;

            var (childTop, childBottom, _) = FragmentationFlow.GetBlockRange(childBox, tableGrid.Size, tableGrid.WritingDirection, top);
            if (!childBox.Box.Display.IsTableRowGroup)
            {
                // (a row that is a child of the table grid box)
                if (!AddRow(childTop, childBottom))
                    return null;

                continue;
            }

            foreach (var row in childBox.ChildrenSpan)
            {
                if (row is not BoxFragment rowBox)
                    continue;

                var (rowTop, rowBottom, _) = FragmentationFlow.GetBlockRange(rowBox, childBox.Size, childBox.WritingDirection, childTop);
                if (!AddRow(rowTop, rowBottom))
                    return null;
            }
        }

        return result;

        bool AddRow(double rowTop, double rowBottom)
        {
            if (rowTop < breakPosition - 0.01 && rowBottom > breakPosition + 0.01)
                return false;

            if (rowBottom <= breakPosition + 0.01)
            {
                result = Math.Max(result ?? rowBottom, rowBottom);
            }

            return true;
        }
    }

    /// <summary>Records the box that the fragments of a box make when they are stitched together, for its decorations (<c>box-decoration-break: slice</c>).</summary>
    private static void Stitch(BoxFragment box, (Fragment Fragment, double BlockOffset)[] slices, bool clone)
    {
        if (clone || slices.Length < 2 || box.StitchedBox is not null)
            return;

        var fragments = new BoxFragment[slices.Length];
        for (var i = 0; i < fragments.Length; i++)
        {
            fragments[i] = (BoxFragment)slices[i].Fragment;
        }

        BoxFragment.Stitch(box, fragments);
    }

    /// <summary>
    /// Returns the fragments of a box that is fragmented apart from the strip (a column spanner of a nested multi-column container,
    /// see <see cref="FragmentationFlow.CollectBox"/>): the index of the fragmentainer, the fragment and its block offset in the fragmentainer.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="blockSize">The block size of its border box.</param>
    /// <param name="end">The fragmentainer and offset of the block-end edge of its border box.</param>
    public List<(int Index, Fragment Fragment, double BlockOffset)> SliceBox(BoxFragment box, double blockSize, (int Index, double Offset)? end)
    {
        var slices = SliceAll(box, 0, blockSize, _map, _map, end, minFirst: 0, cap: _fragmentainers.Count - 1, new double[_fragmentainers.Count], spannerBoxes: null);
        var result = new List<(int, Fragment, double)>(slices.Fragments.Length);
        for (var index = slices.First; index <= slices.Last; index++)
        {
            var (fragment, blockOffset) = slices.Fragments[index - slices.First];
            result.Add((index, fragment, blockOffset));
        }

        return result;
    }

    /// <summary>
    /// Returns the fragments of a multi-column container nested in the fragmentation context
    /// (https://drafts.csswg.org/css-multicol-1/#nested-multicol): one fragment per fragmentainer, with the columns and the parts
    /// of the column spanners that the breaker placed in it (see <see cref="NestedColumns"/>).
    /// </summary>
    private Slices SliceNestedColumns(BoxFragment box, ParallelItem item, double top, double bottom, FlowMap map, (int Index, double Offset)? end, int first, double topOffset, int cap, double[] openOffsets)
    {
        var nested = item.Nested!;
        if (nested.Columns.Count == 0 && nested.Spanners.Count == 0)
            return new Slices(first, [(box, topOffset)]);

        // The absolutely positioned children of the container that are fragmented apart: the breaker placed them from their
        // offsets in the container, in the fragmentainers (not in the columns of the container)
        var writingDirection = box.WritingDirection;
        List<(double Inline, Slices Slices)>? placedChildren = null;
        var firstIndex = first;
        var lastChildIndex = first;
        var boxChildren = box.ChildrenSpan;
        for (var i = box.InFlowChildCount; i < boxChildren.Length; i++)
        {
            if (boxChildren[i] is not BoxFragment child || _flow.GetOutOfFlowItem(child) is not { Map: { } childMap, OutOfFlow: { } placement } childItem)
                continue;

            var (childTop, childBottom, childInline) = FragmentationFlow.GetBlockRange(child, box.Size, writingDirection, top);
            var slices = SliceAll(child, childTop, childBottom, childMap, childMap, childItem.IsBoxDone ? (childItem.EndIndex, childItem.EndOffset) : null, minFirst: 0, cap, _noOpenOffsets ??= new double[_fragmentainers.Count], spannerBoxes: null, (placement.StartIndex, placement.StartOffset));
            (placedChildren ??= []).Add((childInline + GetInlineShift(placement, writingDirection), slices));
            firstIndex = Math.Min(firstIndex, slices.First);
            lastChildIndex = Math.Max(lastChildIndex, slices.Last);
        }

        // The fragmentainer where the border box ends
        var (endIndex, endOffset) = end ?? (item.IsBoxDone ? (item.EndIndex, item.EndOffset) : map.MapEnd(bottom, top, growsWithTrack: false));
        if (item.IsBoxDone && item.CanGrow && (item.EndIndex > endIndex || (item.EndIndex == endIndex && item.EndOffset > endOffset)))
        {
            (endIndex, endOffset) = (item.EndIndex, item.EndOffset);
        }

        endIndex = Math.Clamp(endIndex, first, Math.Max(first, cap));
        var lastIndex = endIndex;
        foreach (var column in nested.Columns)
        {
            lastIndex = Math.Max(lastIndex, column.OuterIndex);
        }

        // The parts of the column spanners in the fragmentainers
        var spannerSlices = new List<(BoxFragment Spanner, List<(int Index, Fragment Fragment, double BlockOffset)> Slices)>(nested.Spanners.Count);
        foreach (var spanner in nested.Spanners)
        {
            var slicer = new FragmentSlicer(spanner.Flow, spanner.Map, _fragmentainers, _writingDirection);
            var slices = slicer.SliceBox(spanner.Fragment, _writingDirection.ToLogical(spanner.Fragment.Size).Block, spanner.End);
            spannerSlices.Add((spanner.Fragment, slices));
            foreach (var slice in slices)
            {
                lastIndex = Math.Max(lastIndex, slice.Index);
            }
        }

        lastIndex = Math.Clamp(Math.Max(lastIndex, lastChildIndex), first, Math.Max(first, cap));
        var blockSize = _writingDirection.ToLogical(box.Size).Block;
        if (lastIndex == first && Math.Abs(endOffset - topOffset - blockSize) < 0.01 && placedChildren is null && nested.Flow.OutOfFlowItems is null)
        {
            // The container is in one fragmentainer, as tall as in the strip: its layout in the strip is its layout in the fragmentainer
            return new Slices(first, [(box, topOffset)]);
        }

        // The content of the columns
        var columns = new List<Fragmentainer>(nested.Columns.Count);
        foreach (var column in nested.Columns)
        {
            columns.Add(new Fragmentainer(column.Start, column.End, column.BlockSize, column.DisplayOffset, new LogicalOffset(MultiColumnFormattingContext.GetColumnInlineOffset(column.IndexInRow, nested.ColumnStride), column.BlockOffset)));
        }

        var placed = new FragmentSlicer(nested.Flow, nested.Map, columns, nested.WritingDirection).Slice(nested.Children, nested.StripSize, nested.SpannerBoxes);
        var clone = box.Box.Style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
        var blockStart = _writingDirection.BlockStart;
        var blockEnd = _writingDirection.BlockEnd;
        var inlineSize = writingDirection.ToLogical(box.Size).Inline;
        // (an absolutely positioned child placed before the container may be in a fragmentainer before the first one of the container)
        var result = new (Fragment Fragment, double BlockOffset)[lastIndex - firstIndex + 1];
        var children = new List<(Fragment Fragment, LogicalOffset Offset)>();
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var sliceTop = index == first ? topOffset : index < first ? _fragmentainers[index].BlockSize : openOffsets[index];
            var sliceBottom = index > endIndex || index < first ? sliceTop
                : index == endIndex ? Math.Max(sliceTop, endOffset)
                : Math.Max(sliceTop, _fragmentainers[index].BlockSize);
            var isPastEnd = index > endIndex || index < first;
            var border = box.Border;
            var padding = box.Padding;
            var margin = box.Margin;
            if (isPastEnd || (index != first && !clone))
            {
                border = border.With(blockStart, 0);
                padding = padding.With(blockStart, 0);
            }

            if (isPastEnd || (index != endIndex && !clone))
            {
                border = border.With(blockEnd, 0);
                padding = padding.With(blockEnd, 0);
            }

            if (index != first)
            {
                margin = margin.With(blockStart, 0);
            }

            if (index != endIndex)
            {
                margin = margin.With(blockEnd, 0);
            }

            // The fragments of the columns and of the spanners that are in the fragmentainer, in the fragment of the container
            children.Clear();
            for (var i = 0; i < nested.Columns.Count; i++)
            {
                if (Math.Clamp(nested.Columns[i].OuterIndex, first, lastIndex) != index)
                    continue;

                var position = columns[i].Position;
                foreach (var (fragment, offset) in placed[i])
                {
                    children.Add((fragment, new LogicalOffset(nested.ContentOffset.Inline + position.Inline + offset.Inline, position.Block - sliceTop + offset.Block)));
                }
            }

            foreach (var (spanner, slices) in spannerSlices)
            {
                var spannerMargin = _writingDirection.ToLogical(spanner.Margin);
                foreach (var slice in slices)
                {
                    if (Math.Clamp(slice.Index, first, lastIndex) == index)
                    {
                        children.Add((slice.Fragment, new LogicalOffset(nested.ContentOffset.Inline + spannerMargin.InlineStart, slice.BlockOffset - sliceTop)));
                    }
                }
            }

            var size = writingDirection.ToPhysical(new LogicalSize(inlineSize, sliceBottom - sliceTop));
            var inFlow = new Fragment[children.Count];
            for (var i = 0; i < inFlow.Length; i++)
            {
                var (fragment, offset) = children[i];
                var physical = writingDirection.ToPhysical(offset, size, fragment.Size);
                if (fragment is BoxFragment childBox)
                {
                    physical += childBox.RelativeOffset + childBox.StickyOffset;
                }

                fragment.Offset = physical;
                inFlow[i] = fragment;
            }

            // The absolutely positioned children of the container that are not fragmented apart are in its first fragment
            List<BoxFragment>? outOfFlow = null;
            if (index == first)
            {
                for (var i = box.InFlowChildCount; i < boxChildren.Length; i++)
                {
                    if (boxChildren[i] is BoxFragment outOfFlowChild && _flow.GetOutOfFlowItem(outOfFlowChild) is not { Map: not null, OutOfFlow: not null })
                    {
                        (outOfFlow ??= []).Add(outOfFlowChild);
                    }
                }
            }

            if (placedChildren is not null)
            {
                foreach (var (childInline, slices) in placedChildren)
                {
                    if (index < slices.First || index > slices.Last)
                        continue;

                    var (childFragment, childOffset) = slices.Fragments[index - slices.First];
                    var physical = writingDirection.ToPhysical(new LogicalOffset(childInline + slices.GetInlineShift(index), childOffset - sliceTop), size, childFragment.Size);
                    if (childFragment is BoxFragment childBox)
                    {
                        physical += childBox.RelativeOffset + childBox.StickyOffset;
                        (outOfFlow ??= []).Add(childBox);
                    }

                    childFragment.Offset = physical;
                }
            }

            var part = box.CreateSlice(size, border, padding, margin, inFlow, outOfFlow, isFirst: box.IsFirstFragment && index == firstIndex, isLast: box.IsLastFragment && index == lastIndex, firstBaseline: null, lastBaseline: null);
            if (index < first)
            {
                part.DisableOverflowClip();
            }

            if (nested.HasGapRules)
            {
                // The gaps of the rows of columns that are in the fragmentainer. The row gaps before them (in the previous
                // fragmentainers, and the ones that disappear at the breaks) consume the values of the row rules
                var rows = new List<MultiColumnFormattingContext.GapRow>();
                var rowGapBaseIndex = 0;
                var rowGapCount = 0;
                for (var i = 0; i < nested.Rows.Count; i++)
                {
                    var row = nested.Rows[i];
                    if (row.OuterIndex == index)
                    {
                        if (rows.Count == 0)
                        {
                            rowGapBaseIndex = rowGapCount + (i > 0 && row.Columns >= 0 && !row.IsContinuation && nested.Rows[i - 1].Columns >= 0 ? 1 : 0);
                        }

                        rows.Add(new MultiColumnFormattingContext.GapRow(row.Columns, row.Start - sliceTop, row.End - sliceTop));
                    }

                    if (i > 0 && row.Columns >= 0 && !row.IsContinuation && nested.Rows[i - 1].Columns >= 0)
                    {
                        rowGapCount++;
                    }
                }

                part.GapGeometry = MultiColumnFormattingContext.BuildGapGeometry(rows, new LogicalOffset(nested.ContentOffset.Inline, 0), nested.ContentInlineSize, nested.SpecifiedColumnCount, nested.ColumnStride, nested.ColumnGap, nested.RowGap, rowGapBaseIndex, rowGapCount);
            }

            result[index - firstIndex] = (part, sliceTop);
        }

        Stitch(box, result, clone);
        return new Slices(firstIndex, result) { BoxEnd = endIndex };
    }

    /// <summary>
    /// Returns the distance in the inline axis of a box, in the coordinates of its parent fragment, between the static position of
    /// an absolutely positioned box in the columns of a nested multi-column container and the one it was laid out from (see
    /// <see cref="OutOfFlowPlacement.InlineShift"/>).
    /// </summary>
    private double GetInlineShift(OutOfFlowPlacement placement, WritingDirection parentWritingDirection)
        => placement.InlineShift == 0 ? 0 : parentWritingDirection.InlineStart == _writingDirection.InlineStart ? placement.InlineShift : -placement.InlineShift;

    /// <summary>Records that a column spanner was met: the in-flow content after it is in the columns that follow it.</summary>
    private void SkipSpanner(BoxFragment spanner, double bottom)
    {
        var end = bottom + _writingDirection.ToLogical(spanner.Margin).BlockEnd;
        _firstAfterSpanners = Math.Max(_firstAfterSpanners, _map.Map(end).Index);
    }

    /// <summary>Returns the index of the column spanner whose margin box contains a position of the strip, or -1.</summary>
    private int GetSpannerAt(double position)
    {
        var spanners = _flow.Spanners;
        for (var i = 0; i < spanners.Count; i++)
        {
            if (position >= spanners[i].MarginBoxStart - 0.01 && position < spanners[i].MarginBoxEnd - 0.01)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns a position of the strip moved out of the extent of the column spanners (their margin boxes, which are not in the
    /// columns): the start of the spanner that contains it. A position where content ends is not in a spanner that starts there.
    /// </summary>
    private double ClampToColumns(double position, bool isEnd)
    {
        foreach (var spanner in _flow.Spanners)
        {
            if (position > spanner.MarginBoxStart + 0.01 && (isEnd ? position <= spanner.MarginBoxEnd + 0.01 : position < spanner.MarginBoxEnd - 0.01))
                return spanner.MarginBoxStart;
        }

        return position;
    }

    /// <summary>
    /// Returns the map of a flow for the absolutely positioned boxes: a fragmentainer that ends before its block-end edge (a
    /// break before content that does not fit) contains the positions of the strip up to its block-end edge.
    /// </summary>
    private FlowMap GetExtendedMap(FlowMap map)
    {
        if (!(_extendedMaps ??= []).TryGetValue(map, out var result))
        {
            result = map.Extend(_fragmentainers);
            _extendedMaps[map] = result;
        }

        return result;
    }

    /// <summary>Returns whether the in-flow descendants of a fragment include a column spanner.</summary>
    private static bool ContainsSpanner(BoxFragment fragment, HashSet<ContainerBox> spannerBoxes)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is BoxFragment childBox && (spannerBoxes.Contains(childBox.Box) || ContainsSpanner(childBox, spannerBoxes)))
                return true;
        }

        return false;
    }
}
