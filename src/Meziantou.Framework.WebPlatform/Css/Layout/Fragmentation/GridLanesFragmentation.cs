namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Finds the grid lanes containers of a strip that are fragmented in their stacking axis, whose items are placed again with the
/// fragmentainers (https://drafts.csswg.org/css-grid-3/#fragmentation, see <see cref="GridLanesBreaks"/>).
/// </summary>
/// <remarks>
/// The content of a fragmentation context is laid out once in a strip and then fragmented (see <see cref="FragmentationFlow"/>):
/// the layout does not know the fragmentainers. The placement of the items of a grid lanes container depends on them (an item
/// that does not fit in what remains of a fragmentainer is placed again in the next one, beside the items that follow it), so
/// the strip is laid out again with the positions of the strip where the fragmentainers of the container end, and fragmented
/// again, until they do not change (as the floats that are pushed, see <see cref="FloatPusher"/>). Only the containers in the
/// flow of the strip (not in a parallel flow) are placed again.
/// </remarks>
internal static class GridLanesFragmentation
{
    private const double Epsilon = 0.01;

    /// <summary>
    /// Updates the fragmentainers of the grid lanes containers from the fragmentation of the strip. Returns whether the strip must
    /// be laid out again.
    /// </summary>
    /// <param name="flow">The strip.</param>
    /// <param name="children">The fragments of the strip.</param>
    /// <param name="stripSize">The size of the strip.</param>
    /// <param name="writingDirection">The writing mode of the fragmentation context.</param>
    /// <param name="map">The breaks of the flow of the strip.</param>
    /// <param name="blockSizes">The block size of each fragmentainer.</param>
    /// <param name="breaks">The fragmentainers of the containers.</param>
    public static bool Update(FragmentationFlow flow, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, FlowMap map, List<double> blockSizes, ref Dictionary<ContainerBox, GridLanesBreaks>? breaks)
    {
        if (!flow.HasGridLanes || blockSizes.Count == 0)
            return false;

        var changed = false;
        Visit(children, stripSize, writingDirection, parentTop: 0, ref breaks);
        return changed;

        void Visit(ReadOnlySpan<Fragment> fragments, PhysicalSize parentSize, WritingDirection parentWritingDirection, double parentTop, ref Dictionary<ContainerBox, GridLanesBreaks>? breaks)
        {
            foreach (var fragment in fragments)
            {
                if (fragment is not BoxFragment { Box.IsOutOfFlow: false } box || flow.IsMonolithic(box))
                    continue;

                var (top, bottom, _) = FragmentationFlow.GetBlockRange(box, parentSize, parentWritingDirection, parentTop);
                if (box.Box.Display.Inside == DisplayInside.GridLanes)
                {
                    // (the stacking axis is the block axis: the lanes are columns)
                    if (GridLayoutAlgorithm.GetLaneAxis(box.Box.Style.Layout) == GridAxis.Columns && UpdateContainer(box, top, bottom, ref breaks))
                    {
                        changed = true;
                    }

                    continue;
                }

                // (the contents of a parallel flow have their own breaks)
                if (flow.GetGroup(box) is not null || flow.GetDefiniteBox(box) is not null)
                    continue;

                Visit(box.ChildrenSpan, box.Size, box.WritingDirection, top, ref breaks);
            }
        }

        bool UpdateContainer(BoxFragment container, double top, double bottom, ref Dictionary<ContainerBox, GridLanesBreaks>? breaks)
        {
            var edges = writingDirection.ToLogical(container.Border.Add(container.Padding).Add(container.Scrollbars));
            var contentTop = top + edges.BlockStart;
            var contentEnd = Math.Max(contentTop, bottom - edges.BlockEnd);
            var containerMap = flow.GetDefiniteBox(container)?.Map ?? map;
            var previous = breaks is not null && breaks.TryGetValue(container.Box, out var value) ? value : null;
            var (first, _) = containerMap.Map(contentTop);
            if (first < 0 || first >= blockSizes.Count)
                return false;

            // The block-end edge of each fragmentainer from the one where the content box starts, from the block-start edge of the
            // content box (a container whose content is in one fragmentainer is not placed again)
            var ends = new List<double>();
            for (var index = first; index < blockSizes.Count; index++)
            {
                var position = index == first ? contentTop : containerMap.GetRange(index) is var (start, _) ? start : double.NaN;
                if (double.IsNaN(position))
                    break;

                var end = position + (blockSizes[index] - containerMap.GetOffsetIn(index, position));
                ends.Add(end - contentTop);
                if (previous is null && index == first && end >= contentEnd - Epsilon)
                    return false;
            }

            var result = new GridLanesBreaks { Ends = [.. ends], NextBlockSize = blockSizes[^1], IsColumns = !flow.IsPaged };
            if (previous is not null && previous.IsSameAs(result))
                return false;

            (breaks ??= [])[container.Box] = result;
            return true;
        }
    }
}
