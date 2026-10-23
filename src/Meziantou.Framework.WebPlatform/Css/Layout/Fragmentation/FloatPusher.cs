namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Finds the floats of a strip that do not fit in what remains of their fragmentainer and must be pushed to the next one
/// (https://drafts.csswg.org/css-break-3/#parallel-flows: a float is a parallel flow; as in Blink, it is pushed alone, the in-flow
/// content beside and after it stays in the fragmentainer and is laid out without it, and the next floats and the boxes that
/// clear it follow it).
/// </summary>
/// <remarks>
/// The content of a fragmentation context is laid out once in a strip and then fragmented (see <see cref="FragmentationFlow"/>):
/// the layout does not know the fragmentainers. A float that is pushed changes the layout of the content around it (the line
/// boxes beside it, the boxes that avoid the floats, the clearance), so the strip is laid out again with the float placed at
/// or below the position of the strip where the next fragmentainer starts (<see cref="FloatPush"/>), and fragmented again,
/// until no float needs to be pushed (the floats of one fragmentainer per pass: the next ones depend on them).
/// </remarks>
internal static class FloatPusher
{
    private const double Epsilon = 0.01;

    /// <summary>The maximum number of times a strip is laid out again for its floats.</summary>
    public const int MaxPasses = 12;

    /// <summary>
    /// Updates the floats to push from the fragmentation of the strip. Returns whether the strip must be laid out again.
    /// </summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="flow">The strip.</param>
    /// <param name="map">The breaks of the flow of the strip.</param>
    /// <param name="blockSizes">The block size of each fragmentainer.</param>
    /// <param name="fragmentainers">The fragmentainers.</param>
    /// <param name="pushes">The floats that are pushed.</param>
    public static bool Update(LayoutContext context, FragmentationFlow flow, FlowMap map, List<double> blockSizes, List<Fragmentainer> fragmentainers, ref Dictionary<ContainerBox, FloatPush>? pushes)
    {
        if (flow.Floats is not { } floats || blockSizes.Count == 0)
            return false;

        var firstIndex = int.MaxValue;
        List<(FloatPush Push, double Min)>? moved = null;
        List<(FloatExtent Extent, int Index, double Target, double Placed)>? found = null;
        List<(FloatExtent Extent, int Index, double Min)>? deferred = null;
        foreach (var extent in floats)
        {
            var box = extent.Fragment.Box;
            var floatMap = extent.Flow.OwnerItem is { } owner ? owner.Map : map;
            if (floatMap is null || !context.FloatPlacements.TryGetValue(box, out var placed))
                continue;

            if (pushes is not null && pushes.TryGetValue(box, out var push))
            {
                // The float follows the start of its fragmentainer (the breaks of the in-flow content changed with its layout)
                if (floatMap.GetRange(push.TargetIndex) is var (start, _) && double.IsFinite(start))
                {
                    var min = placed - extent.Start + start;
                    if (Math.Abs(min - push.MinBlockOffset) > Epsilon)
                    {
                        (moved ??= []).Add((push, min));
                    }
                }

                continue;
            }

            var (index, offset) = floatMap.Map(extent.Start);
            if (index < 0 || index >= fragmentainers.Count)
                continue;

            if (IsDeferred(extent, floatMap, index, flow, map) && floatMap.GetRange(index) is var (indexStart, _) && double.IsFinite(indexStart))
            {
                (deferred ??= []).Add((extent, index, placed - extent.Start + indexStart));
                continue;
            }

            // A float in a fragmentainer whose flow was broken before the end of the previous one (because of the float, or
            // of the content around it) is first placed in what remains of the previous fragmentainer
            if (index > 0 && Math.Abs(fragmentainers[index - 1].End - fragmentainers[index].Start) <= Epsilon
                && floatMap.GetOffsetIn(index - 1, extent.Start) < GetBlockSize(index - 1) - Epsilon)
            {
                index--;
                offset = floatMap.GetOffsetIn(index, extent.Start);
            }

            if (index > firstIndex || !IsPushed(extent, floatMap, index, offset, GetBlockSize(index), fragmentainers[index].DisplayOffset))
                continue;

            if (index < firstIndex)
            {
                firstIndex = index;
                found?.Clear();
            }

            (found ??= []).Add((extent, index, extent.Start + (GetBlockSize(index) - offset), placed));
        }

        // The floats that are pushed follow the start of their fragmentainer, once the floats of the previous fragmentainers
        // are pushed (the breaks before them depend on these floats)
        var changed = false;
        if (found is null || found.Count == 0)
        {
            firstIndex = int.MaxValue;
        }

        if (moved is not null)
        {
            foreach (var (push, min) in moved)
            {
                if (push.TargetIndex <= firstIndex)
                {
                    push.MinBlockOffset = min;
                    changed = true;
                }
            }
        }

        if (deferred is not null)
        {
            pushes ??= [];
            foreach (var (extent, index, min) in deferred)
            {
                pushes[extent.Fragment.Box] = new FloatPush { TargetIndex = index, MinBlockOffset = min, IsDeferred = true };
            }

            changed = true;
        }

        if (found is not null)
        {
            pushes ??= [];
            foreach (var (extent, index, target, placed) in found)
            {
                // A float inside a float that is pushed moves with it: it is pushed (or not) once its ancestor is placed again
                if (HasPushedAncestor(extent.Fragment, found))
                    continue;

                pushes[extent.Fragment.Box] = new FloatPush { TargetIndex = index + 1, MinBlockOffset = placed - extent.Start + target };
                changed = true;
            }
        }

        return changed;

        double GetBlockSize(int index) => blockSizes[Math.Min(index, blockSizes.Count - 1)];
    }

    /// <summary>Returns whether a float is inside another float that is pushed in the same pass.</summary>
    private static bool HasPushedAncestor(BoxFragment fragment, List<(FloatExtent Extent, int Index, double Target, double Placed)> found)
    {
        for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            foreach (var entry in found)
            {
                if (ReferenceEquals(entry.Extent.Fragment, ancestor))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a float is in a later fragmentainer than the position of a box that was moved below it (its clearance,
    /// the offset of a box that avoids the floats): the float is in a parallel flow (the contents that overflow a box) that
    /// reaches it after a break, and the box, which follows that flow in the content, is laid out in the previous
    /// fragmentainer, where the float is not (as in Blink).
    /// </summary>
    private static bool IsDeferred(in FloatExtent extent, FlowMap floatMap, int index, FragmentationFlow flow, FlowMap map)
    {
        if (index == 0 || flow.ClearanceGaps is not { } gaps)
            return false;

        foreach (var gap in gaps)
        {
            if (ReferenceEquals(gap.Flow, extent.Flow) || gap.Start >= extent.End - Epsilon || gap.End <= extent.Start + Epsilon)
                continue;

            var gapMap = gap.Flow.OwnerItem is { } owner ? owner.Map : map;
            if (gapMap is not null && !ReferenceEquals(gapMap, floatMap) && gapMap.Map(gap.Start).Index < index)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether a float that is at an offset of a fragmentainer is pushed to the next one.</summary>
    private static bool IsPushed(in FloatExtent extent, FlowMap map, int index, double offset, double blockSize, double displayOffset)
    {
        if (!double.IsFinite(blockSize) || (offset + (extent.End - extent.Start) <= blockSize + Epsilon && !extent.HasForcedBreakBefore))
            return false;

        // A float at the start of the fragmentainer (after the edges repeated by the boxes with box-decoration-break: clone)
        // stays there: it would not fit better in the next one
        if (offset <= displayOffset + Epsilon)
            return false;

        if (map.GetOffsetIn(index, extent.LeadingStart) <= displayOffset + Epsilon && extent.Start - extent.LeadingStart <= extent.LeadingEdges + Epsilon)
            return false;

        // A float with a forced break before it starts the next fragmentainer (as in Gecko; https://drafts.csswg.org/css2/#page-break-props:
        // page-break-before applies to the floats, WPT css/CSS2/pagination/page-break-before-020.xht)
        if (extent.HasForcedBreakBefore)
            return true;

        // A float that is not broken (monolithic, break-inside: avoid); a float whose border box fits but not its block-end
        // margin, which is not truncated (as in Blink, https://github.com/w3c/csswg-drafts/issues/9191); a float whose first
        // content is monolithic and does not fit
        return extent.IsUnbreakable
            || offset + (extent.BorderBoxEnd - extent.Start) <= blockSize + Epsilon
            || (!double.IsNaN(extent.LeadingEnd) && offset + (extent.LeadingEnd - extent.Start) > blockSize + Epsilon);
    }
}
