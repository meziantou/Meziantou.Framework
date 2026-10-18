using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The fragmentainers of a fragmentation context root, for the placement of its absolutely positioned boxes
/// (<see cref="ColumnBreaker.PlaceOutOfFlow"/>): they are a sequence, across the column spanners.
/// </summary>
internal sealed class OutOfFlowFragmentainers(FlowMap map)
{
    /// <summary>The breaks of the flow of the strip.</summary>
    public FlowMap Map { get; } = map;

    /// <summary>The block size of each fragmentainer (the one its content is fragmented with).</summary>
    public List<double> BlockSizes { get; } = [];

    /// <summary>The first fragmentainer of each portion of the strip (the content between two column spanners).</summary>
    public List<int> PortionStarts { get; } = [];
}

internal static partial class ColumnBreaker
{
    /// <summary>The result of the placement of the absolutely positioned boxes of a strip (see <see cref="PlaceOutOfFlow"/>).</summary>
    /// <param name="Count">The number of fragmentainers that the boxes use.</param>
    /// <param name="Shortage">The smallest increase of the block size of the fragmentainers that lets one of them contain more content, or +∞.</param>
    /// <param name="IsViolating">Whether a break that violates a rule was chosen.</param>
    /// <param name="Overflow">The block size of the parts of the boxes that are in the fragmentainers that are not allowed.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct OutOfFlowResult(int Count, double Shortage, bool IsViolating, double Overflow)
    {
        /// <summary>The number of fragmentainers that the boxes of the nested multi-column containers use (see <see cref="PlaceNestedOutOfFlow(FragmentationFlow, List{double}, double, Action?, bool)"/>).</summary>
        public int NestedCount { get; init; }
    }

    /// <summary>
    /// Places the absolutely positioned boxes of a strip that are fragmented apart (<see cref="FragmentationFlow.OutOfFlowItems"/>),
    /// once the in-flow content is placed, as in Blink: a box starts at its offset from its containing block in the sequence of
    /// the fragmentainers (the fragmentainer where the containing block starts, or another one when the offset is larger than
    /// what remains of it, or negative; the fragmentainers after the last one of the in-flow content have the same block size),
    /// then its contents are broken as a flow of their own, and continue in the next fragmentainers (also after a column
    /// spanner). A specified block size of the box does not follow the breaks of its contents (the box uses what remains of
    /// each fragmentainer); a box whose block size is automatic grows when its contents are pushed. A box placed from the
    /// block-end edge of its containing block is placed from the end of the fragments of the containing block stitched
    /// together; a static position, or a grid area, is a position of the content of the containing block.
    /// </summary>
    /// <param name="flow">The strip.</param>
    /// <param name="map">The breaks of the flow of the strip.</param>
    /// <param name="blockSizes">The block sizes of the fragmentainers.</param>
    /// <param name="nextBlockSize">The block size of the fragmentainers after them.</param>
    /// <param name="portionStarts">The first fragmentainer of each portion of the strip (the content between two column spanners) that is placed: the boxes of the next portions are not placed.</param>
    /// <param name="allowedEnd">The number of fragmentainers that are allowed (see <see cref="OutOfFlowResult.Overflow"/>).</param>
    /// <param name="addFragmentainer">Creates the fragmentainer after the last one (it adds its block size to <paramref name="blockSizes"/>) when a box needs it, or null: the next fragmentainers have the block size <paramref name="nextBlockSize"/>.</param>
    public static OutOfFlowResult PlaceOutOfFlow(FragmentationFlow flow, FlowMap map, List<double> blockSizes, double nextBlockSize, List<int> portionStarts, int allowedEnd = int.MaxValue, Action? addFragmentainer = null)
    {
        var count = 0;
        var shortage = double.PositiveInfinity;
        var violating = false;
        var overflow = 0d;

        // The boxes of the nested multi-column containers that the in-flow content placed, first: the static position of a box
        // of this strip may be in the contents of one of them
        var nestedCount = flow.HasNestedColumns ? PlaceNestedOutOfFlow(flow, blockSizes, nextBlockSize, addFragmentainer) : 0;
        if (flow.OutOfFlowItems is not { } items)
            return new OutOfFlowResult(count, shortage, violating, overflow) { NestedCount = nestedCount };

        foreach (var item in items)
        {
            var placement = item.OutOfFlow!;
            item.Map = null;
            item.IsBoxDone = false;
            var containerMap = placement.Owner is null ? map : placement.Owner.Map;
            if (containerMap is null)
                continue;

            // The position that the box is placed from: a position of the content of the containing block (a static position
            // at a break is at the end of the fragmentainer before the break), or the block-start edge of the containing block
            // (the static position is the one of the margin box: the margin is applied in the fragmentainer of the static position)
            var reference = placement.IsPlacedInContent ? item.Start - placement.StaticMargin : placement.ContainerStart;
            var portion = flow.GetOutOfFlowPortion(item);
            if (portion >= portionStarts.Count)
                continue;

            var portionStart = portion == 0 ? double.NegativeInfinity : flow.Spanners[portion - 1].MarginBoxEnd;
            var (index, offset) = placement.IsPlacedInContent
                ? containerMap.MapEnd(reference, Math.Max(portionStart, placement.ContainerStart))
                : containerMap.Map(reference);
            if (placement.Owner is null)
            {
                // (the positions at the limits of the portions are in several of them)
                var first = portionStarts[portion];
                var last = portion + 1 < portionStarts.Count ? Math.Max(first, portionStarts[portion + 1] - 1) : int.MaxValue;
                if (index < first || index > last)
                {
                    index = Math.Clamp(index, first, last);
                    offset = containerMap.GetOffsetIn(index, reference);
                }
            }

            // A static position in the contents of a nested multi-column container is in the columns that the breaker placed
            placement.InlineShift = 0;
            if ((placement.IsStatic || placement.IsInlineStatic) && flow.HasNestedColumns && TryGetNestedStaticPosition(flow, item.Box!.Box, out var staticIndex, out var staticOffset, out var staticInlineShift))
            {
                placement.InlineShift = placement.IsInlineStatic ? staticInlineShift : 0;
                if (placement.IsStatic)
                {
                    (index, offset) = (staticIndex, staticOffset);
                }
            }

            // The block size that the fragments of the containing block stitched together gain on the containing block of the
            // strip, for a box whose block size follows it (the containing block of the strip includes the column spanners
            // that interrupt it; its fragments are taller when its contents are pushed by the breaks)
            var stretch = 0d;
            if (!placement.IsPlacedInContent)
            {
                var distance = item.Start - placement.ContainerStart;
                if (placement.EndRatio > 0 || placement.IsStretched)
                {
                    // The fragments of the containing block stitched together are taller than the containing block in the strip
                    // when its contents are pushed by the breaks
                    var (endIndex, endOffset) = placement.Owner is { IsBoxDone: true } owner && ReferenceEquals(owner.Box, placement.Container)
                        ? (owner.EndIndex, owner.EndOffset)
                        : flow.GetNestedColumns(placement.Container) is { IsBoxDone: true } nestedContainer ? (nestedContainer.EndIndex, nestedContainer.EndOffset)
                        : containerMap.MapEnd(placement.ContainerEnd, placement.ContainerStart);
                    var stitched = endOffset - offset;
                    for (var i = index; i < endIndex; i++)
                    {
                        stitched += GetBlockSize(i);
                    }

                    distance += placement.EndRatio * (stitched - (placement.ContainerEnd - placement.ContainerStart));
                    stretch = placement.IsStretched ? stitched - (placement.ContainerEnd - placement.ContainerStart) : 0;
                }

                offset += distance;
            }
            else
            {
                offset += placement.StaticMargin;
            }

            index = Math.Max(index, 0);
            while (offset >= GetBlockSize(index) - Epsilon && (offset > Epsilon || GetBlockSize(index) > Epsilon) && index < MaxFragmentainers)
            {
                offset -= GetBlockSize(index);
                index++;
            }

            // (a box placed before the start of its containing block may be in a previous fragmentainer)
            while (offset < -Epsilon && index > 0)
            {
                index--;
                offset += GetBlockSize(index);
            }

            placement.StartIndex = index;
            placement.StartOffset = offset;
            var origin = item.Start - offset;
            var itemMap = new FlowMap();
            itemMap.Add(double.NegativeInfinity, index, origin, isBreak: false);
            var contentEnd = Math.Max(item.Flow.ContentEnd, item.Start);

            // As in Blink, the contents of a box that does not start at the start of a fragmentainer are pushed to the next one when
            // they cannot be broken there without violating a rule (a first child with break-inside: avoid, a first line that
            // does not fit), while the box starts at its position. Not when the box, or its containing block, has a specified
            // block size and fits in the fragmentainer: its first content is then placed with it, even when it overflows the
            // fragmentainer (https://github.com/w3c/csswg-drafts/issues/6056)
            var firstBlockSize = GetBlockSize(index);
            var fits = placement.IsDefinite && offset + item.End - item.Start <= firstBlockSize + Epsilon;
            if (!fits && placement.IsContainerDefinite)
            {
                var containerEnd = placement.Owner is { IsBoxDone: true } containerItem && ReferenceEquals(containerItem.Box, placement.Container)
                    ? (containerItem.EndIndex, containerItem.EndOffset)
                    : containerMap.MapEnd(placement.ContainerEnd, placement.ContainerStart);
                fits = containerEnd.Item1 == index && containerEnd.Item2 <= firstBlockSize + Epsilon;
            }

            var canPushContents = !fits && !item.IsMonolithic && offset > Epsilon;
            var cursor = new FlowCursor(item.Flow, itemMap, item.Start, contentEnd, index, item.Flow.NextGeneration(), canPushContents, includesStart: true);
            cursor.SetOrigin(origin);
            if (!double.IsNaN(item.LeadingMonolithicEnd) && offset + item.End - item.Start <= firstBlockSize + Epsilon)
            {
                cursor.SetLeadingEnd(item.LeadingMonolithicEnd);
            }

            // The contents are placed fragmentainer after fragmentainer. A specified block size of the box does not follow them:
            // the box uses what remains of each fragmentainer (and extends over the monolithic contents that overflow it)
            var boxRemaining = Math.Max(0, Math.Min(item.End, placement.ClipEnd) - item.Start + stretch);
            var isFlowDone = false;
            var startOffset = offset;
            while (true)
            {
                var blockSize = GetBlockSize(index);
                if (index == allowedEnd || (index > allowedEnd && index == placement.StartIndex))
                {
                    overflow += Math.Max(item.IsBoxDone ? 0 : boxRemaining, isFlowDone ? 0 : Math.Max(0, contentEnd - cursor.Position));
                }

                var extent = 0d;
                if (!isFlowDone)
                {
                    var result = cursor.Advance(blockSize);
                    shortage = Math.Min(shortage, result.Shortage);
                    violating |= result.Appeal is BreakAppeal.Avoid or BreakAppeal.OrphansWidows or BreakAppeal.LastResort;
                    isFlowDone = result.IsFinished;
                    if (result.Appeal == BreakAppeal.LastResort)
                    {
                        extent = cursor.GetPlacedOffset(result.IsFinished ? contentEnd : item.Flow.GetGapStart(result.Position)) - startOffset;
                    }
                }

                if (!item.IsBoxDone)
                {
                    var available = Math.Max(Math.Max(0, blockSize - startOffset), extent);
                    if (boxRemaining <= available + Epsilon)
                    {
                        // (the part of the box after the block-end edge of an ancestor that clips it is not fragmented)
                        item.IsBoxDone = true;
                        item.EndIndex = index;
                        item.EndOffset = startOffset + boxRemaining + Math.Max(0, item.End - placement.ClipEnd);
                    }
                    else
                    {
                        boxRemaining -= available;
                    }
                }

                if ((isFlowDone && item.IsBoxDone) || index >= MaxFragmentainers)
                    break;

                index++;
                startOffset = 0;
            }

            // (a box whose block size is automatic grows with its contents when they are pushed by the breaks)
            if (!placement.IsDefinite && item.End <= placement.ClipEnd && itemMap.MapEnd(item.End, item.Start) is var contentPlace
                && (contentPlace.Index > item.EndIndex || (contentPlace.Index == item.EndIndex && contentPlace.Offset > item.EndOffset)))
            {
                (item.EndIndex, item.EndOffset) = contentPlace;
            }

            item.Map = itemMap;
            item.IsBoxDone = true;
            count = Math.Max(count, Math.Max(index, item.EndIndex) + 1);
        }

        // The boxes of the nested multi-column containers that are in the contents of the boxes (which were placed just now)
        if (flow.HasNestedColumns)
        {
            nestedCount = Math.Max(nestedCount, PlaceNestedOutOfFlow(flow, blockSizes, nextBlockSize, addFragmentainer, onlyNew: true));
        }

        return new OutOfFlowResult(count, shortage, violating, overflow) { NestedCount = nestedCount };

        // (a new fragmentainer has a block size of 1px at least for the fragmentation of its content, as in Blink)
        double GetBlockSize(int index) => GetFragmentainerBlockSize(blockSizes, nextBlockSize, addFragmentainer, index);
    }

    /// <summary>Returns the block size of a fragmentainer of a sequence, which is created when it is after the last one (see <see cref="PlaceOutOfFlow"/>).</summary>
    private static double GetFragmentainerBlockSize(List<double> blockSizes, double nextBlockSize, Action? addFragmentainer, int index)
    {
        while (addFragmentainer is not null && index >= blockSizes.Count && blockSizes.Count < MaxFragmentainers)
        {
            var count = blockSizes.Count;
            addFragmentainer();
            if (blockSizes.Count == count)
                break;
        }

        return index < blockSizes.Count ? blockSizes[Math.Max(0, index)] : Math.Max(MinBlockSize, nextBlockSize);
    }

    /// <summary>
    /// Places the absolutely positioned boxes of the nested multi-column containers of a flow (the ones that the breaker has
    /// placed), once the content of the flow is placed: see <see cref="PlaceNestedOutOfFlow(ParallelItem, NestedColumns, List{double}, double, Action?)"/>.
    /// Returns the number of fragmentainers of the flow that their columns use.
    /// </summary>
    /// <param name="onlyNew">Whether the containers whose boxes are placed are skipped (the breaker did not place their in-flow content again).</param>
    private static int PlaceNestedOutOfFlow(FragmentationFlow flow, List<double> blockSizes, double nextBlockSize, Action? addFragmentainer, bool onlyNew = false)
    {
        var count = 0;
        if (flow.NestedItems is { } items)
        {
            foreach (var item in items)
            {
                if (item.Nested is { Columns.Count: > 0 } nested && (nested.Flow.OutOfFlowItems is not null || nested.Flow.HasNestedColumns) && !(onlyNew && nested.InFlowColumnCount >= 0))
                {
                    count = Math.Max(count, PlaceNestedOutOfFlow(item, nested, blockSizes, nextBlockSize, addFragmentainer));
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Places the absolutely positioned boxes of the strip of a nested multi-column container (see <see cref="NestedColumns"/>)
    /// once its in-flow content is placed, as the ones of a container that is not nested (<see cref="PlaceOutOfFlow"/>): in the
    /// sequence of its columns, across its rows and the outer fragmentainers. The boxes that end after the in-flow content
    /// create columns: in the last row while it has room for them (a row has <c>column-count</c> columns; the last row of a
    /// container whose definite block size ends in the outer fragmentainer has overflow columns), then in new rows in the next
    /// outer fragmentainers (https://drafts.csswg.org/css-multicol-1/#pagination-and-overflow-outside-multicol: the columns that
    /// do not fit because of the size of the outer fragmentainer are moved to the next one; Blink adds them all to the last
    /// row, where they overflow in the inline direction). These rows are as tall as the outer fragmentainer (or as what
    /// remains of a content box with a definite block size); they do not move the block-end edge of the container nor the
    /// content after it. Returns the number of outer fragmentainers that the columns use.
    /// </summary>
    /// <param name="item">The parallel flow of the container.</param>
    /// <param name="nested">The content of the container.</param>
    /// <param name="blockSizes">The block sizes of the outer fragmentainers.</param>
    /// <param name="nextBlockSize">The block size of the outer fragmentainers after them.</param>
    /// <param name="addFragmentainer">Creates the outer fragmentainer after the last one, or null.</param>
    private static int PlaceNestedOutOfFlow(ParallelItem item, NestedColumns nested, List<double> blockSizes, double nextBlockSize, Action? addFragmentainer)
    {
        // (the boxes are placed again when the breaker tries other block sizes for the outer fragmentainers)
        var fragmentainers = nested.Fragmentainers;
        if (nested.InFlowColumnCount >= 0 && nested.InFlowColumnCount < nested.Columns.Count)
        {
            nested.Columns.RemoveRange(nested.InFlowColumnCount, nested.Columns.Count - nested.InFlowColumnCount);
            fragmentainers.BlockSizes.RemoveRange(nested.InFlowColumnCount, fragmentainers.BlockSizes.Count - nested.InFlowColumnCount);
        }

        if (nested.InFlowRows is { } inFlowRows)
        {
            nested.Rows.Clear();
            nested.Rows.AddRange(inFlowRows);
            nested.InFlowRows = null;
        }

        nested.InFlowColumnCount = nested.Columns.Count;
        if (fragmentainers.BlockSizes.Count != nested.Columns.Count)
            return 0;

        // The row where the next column is created: the last row of the in-flow content
        var box = item.Box!;
        var edges = nested.WritingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
        var clones = box.Box.Style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
        var last = nested.Columns[^1];
        var outerIndex = last.OuterIndex;
        var rowColumns = last.IndexInRow + 1;
        var rowOffset = last.BlockOffset;
        var rowBlockSize = fragmentainers.BlockSizes[^1];
        var capacity = nested.LastRowOverflows ? int.MaxValue : Math.Max(0, nested.ColumnCount - rowColumns);
        PlaceOutOfFlow(nested.Flow, nested.Map, fragmentainers.BlockSizes, rowBlockSize, fragmentainers.PortionStarts, addFragmentainer: AddColumn);
        return nested.Columns.Count > nested.InFlowColumnCount ? nested.Columns[^1].OuterIndex + 1 : 0;

        void AddColumn()
        {
            if (nested.Columns.Count >= MaxFragmentainers)
                return;

            if (capacity <= 0)
            {
                // A new row at the start of the next outer fragmentainer: in what remains of the content box of the container
                // (its last row has overflow columns when its block size is definite), or in the whole fragmentainer when the
                // container has ended
                outerIndex++;
                rowColumns = 0;
                var outerBlockSize = GetFragmentainerBlockSize(blockSizes, nextBlockSize, addFragmentainer, outerIndex);
                var endIndex = item.IsBoxDone ? item.EndIndex : int.MaxValue;
                if (outerIndex > endIndex)
                {
                    rowOffset = 0;
                    rowBlockSize = outerBlockSize;
                    capacity = nested.ColumnCount;
                }
                else
                {
                    rowOffset = clones ? edges.BlockStart : 0;
                    rowBlockSize = (outerIndex == endIndex ? item.EndOffset : outerBlockSize) - rowOffset - (clones || outerIndex == endIndex ? edges.BlockEnd : 0);
                    capacity = outerIndex == endIndex && nested.BlockSize is not null ? int.MaxValue : nested.ColumnCount;
                }

                if (nested.RowBlockSize is { } fixedBlockSize)
                {
                    rowBlockSize = Math.Min(rowBlockSize, fixedBlockSize);
                }
            }

            // (a column has a block size of 1px at least for the fragmentation of its content, as in Blink)
            var blockSize = Math.Max(MinBlockSize, rowBlockSize);
            nested.Columns.Add(new NestedColumn
            {
                OuterIndex = outerIndex,
                Start = double.PositiveInfinity,
                IndexInRow = rowColumns,
                BlockOffset = rowOffset,
                BlockSize = blockSize,
            });

            fragmentainers.BlockSizes.Add(blockSize);
            rowColumns++;
            if (capacity != int.MaxValue)
            {
                capacity--;
            }

            // The columns of the row, for the gap decorations
            nested.InFlowRows ??= [.. nested.Rows];
            var rows = nested.Rows;
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i].OuterIndex == outerIndex && rows[i].Columns >= 0 && Math.Abs(rows[i].Start - rowOffset) <= Epsilon)
                {
                    rows[i] = rows[i] with { Columns = Math.Max(rows[i].Columns, rowColumns) };
                    return;
                }

                if (rows[i].OuterIndex < outerIndex)
                    break;
            }

            rows.Add(new NestedRow(outerIndex, rowColumns, rowOffset, rowOffset + blockSize));
        }
    }

    /// <summary>
    /// Returns the static position of an absolutely positioned box whose static position is in the contents of a nested
    /// multi-column container of a flow (the containing block of the box is outside of the strip of that container: the box was
    /// laid out from the static position it has in the layout of the container in the strip of the flow, where the container is
    /// not fragmented): the fragmentainer of the flow and the offset in it of the static position in the columns that the
    /// breaker placed, and the distance in the inline axis between that position and the one that was used.
    /// </summary>
    public static bool TryGetNestedStaticPosition(FragmentationFlow flow, ContainerBox box, out int index, out double offset, out double inlineShift)
    {
        index = 0;
        offset = 0;
        inlineShift = 0;
        if (flow.GetNestedStaticPosition(box) is not var (item, candidateIndex) || item.Nested is not { Columns.Count: > 0, OutOfFlow: { } candidates } nested)
            return false;

        var (candidate, staticPosition) = candidates[candidateIndex];
        var logical = nested.WritingDirection.ToLogical(staticPosition, nested.StripSize, PhysicalSize.Zero);
        var columns = nested.Columns;
        int column;
        double columnOffset;
        if (TryGetNestedStaticPosition(nested.Flow, box, out column, out columnOffset, out var innerShift))
        {
            // (in a container nested in the nested container)
            logical = new LogicalOffset(logical.Inline + innerShift, logical.Block);
        }
        else if (candidate.StaticPositionContainer is { } carrier && nested.Flow.GetContainingItem(carrier)?.Map is { } parallelMap)
        {
            // The static position is in a parallel flow (the contents of an absolutely positioned box...)
            (column, columnOffset) = parallelMap.MapEnd(logical.Block, double.NegativeInfinity);
        }
        else
        {
            // A static position at a break is at the end of the previous column when it has room: the box precedes the content after the break
            var map = nested.Map;
            column = Math.Clamp(map.Map(logical.Block).Index, 0, columns.Count - 1);
            if (column > 0 && Math.Abs(columns[column].Start - logical.Block) < Epsilon && Math.Abs(columns[column - 1].End - logical.Block) < Epsilon
                && (map.GetOffsetIn(column - 1, logical.Block) < columns[column - 1].BlockSize - Epsilon || map.IsTruncated(column - 1, logical.Block)))
            {
                column--;
            }

            columnOffset = map.GetOffsetIn(column, logical.Block);
            if (map.IsTruncated(column, logical.Block))
            {
                // (after a margin that is truncated at the break)
                columnOffset = Math.Min(columnOffset, columns[column].BlockSize);
            }
        }

        var placed = columns[Math.Clamp(column, 0, columns.Count - 1)];
        index = placed.OuterIndex;
        offset = placed.BlockOffset + columnOffset;
        var inline = nested.ContentOffset.Inline + MultiColumnFormattingContext.GetColumnInlineOffset(placed.IndexInRow, nested.ColumnStride) + logical.Inline;
        inlineShift = nested.StripStaticPositions is { } positions && positions.TryGetValue(box, out var used) ? inline - used.Inline : 0;
        return true;
    }
}
