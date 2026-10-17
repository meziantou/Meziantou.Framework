using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The inputs of the layout of a strip (see <see cref="FragmentationFlow"/>) that depend on its fragmentation, other than the
/// pushed floats (<see cref="FloatPusher"/>) and the items of the grid lanes containers (<see cref="GridLanesFragmentation"/>).
/// </summary>
/// <remarks>
/// <para>
/// The content of a fragmentation context is laid out once in a strip and then fragmented: the layout does not know the
/// fragmentainers. A few layouts depend on them: the layout of the strip records the boxes whose layout may depend on them
/// (the <c>Record</c> methods, only while a strip is laid out: <see cref="LayoutContext.StripRelayout"/>), the fragmentation
/// finds the ones that are broken (<see cref="Update"/>), and the strip is laid out again with the inputs of these boxes (a
/// targeted relayout: the other boxes are laid out the same way), then fragmented again, until the inputs are stable:
/// </para>
/// <list type="bullet">
/// <item>A box that avoids the floats (<see cref="GetFloatAvoiderBreaks"/>): as in Blink, each of its fragments has the inline
/// size and the position of the layout opportunity beside the floats of its fragmentainer
/// (https://drafts.csswg.org/css-break-3/#varying-size-boxes). It is laid out in the area of its first fragment, and again in
/// the area of each next fragment whose area differs (<see cref="InlineVariant"/>): the content of each fragment comes from
/// the layout in its area.</item>
/// <item>A box whose block size is limited and whose contents overflow it into a later fragmentainer
/// (<see cref="SeparatesEndMargin"/>): the block-end margin of its last child, which is in the parallel flow of its overflowing
/// contents, does not collapse through its block-end edge (as in Blink).</item>
/// <item>A block container whose contents are aligned by <c>align-content</c> and that is broken (<see cref="IsUnaligned"/>):
/// the alignment of fragmented contents is undefined (https://drafts.csswg.org/css-align-3/#distribution-block), the contents
/// are not aligned (one of the two behaviors that the WPT tests accept).</item>
/// </list>
/// </remarks>
internal sealed class StripRelayout
{
    private const double Epsilon = 0.01;

    /// <summary>The boxes of the strip that avoid the floats, laid out beside floats, and the block offset of their border box in their block formatting context.</summary>
    private Dictionary<ContainerBox, double>? _floatAvoiders;

    /// <summary>The block containers of the strip whose contents are aligned by <c>align-content</c>.</summary>
    private HashSet<ContainerBox>? _alignedBoxes;

    /// <summary>
    /// The boxes of the strip whose contents overflow their limited block size and whose block-end margin collapsed with the
    /// block-end margin of their last children, and the offset of these margins from the block-start edge of their border box.
    /// </summary>
    private Dictionary<ContainerBox, double>? _collapsedEndMargins;

    /// <summary>The breaks of the boxes that avoid the floats (see <see cref="GetFloatAvoiderBreaks"/>).</summary>
    private Dictionary<ContainerBox, double[]>? _floatAvoiderBreaks;

    /// <summary>The broken block containers whose contents are not aligned (see <see cref="IsUnaligned"/>).</summary>
    private HashSet<ContainerBox>? _unalignedBoxes;

    /// <summary>The boxes whose block-end margin does not collapse with the ones of their last children (see <see cref="SeparatesEndMargin"/>).</summary>
    private HashSet<ContainerBox>? _separatedEndMargins;

    /// <summary>The fragments of the boxes laid out in another area for their next fragments (see <see cref="InlineVariant"/>).</summary>
    private Dictionary<BoxFragment, InlineVariant[]>? _inlineVariants;

    /// <summary>Starts a layout of the strip: the records of the previous layout are cleared (the inputs are kept).</summary>
    public void BeginLayout()
    {
        _floatAvoiders?.Clear();
        _alignedBoxes?.Clear();
        _collapsedEndMargins?.Clear();
        _inlineVariants?.Clear();
    }

    /// <summary>Records a box that avoids the floats, placed beside floats at a block offset of its block formatting context (its border box).</summary>
    public void RecordFloatAvoider(ContainerBox box, double blockOffset) => (_floatAvoiders ??= [])[box] = blockOffset;

    /// <summary>Records a block container whose contents are aligned by <c>align-content</c>.</summary>
    public void RecordAlignedBox(ContainerBox box) => (_alignedBoxes ??= []).Add(box);

    /// <summary>
    /// Records a box whose contents overflow its limited block size and whose block-end margin collapsed with the block-end
    /// margin of its last children, which are at an offset from the block-start edge of its border box.
    /// </summary>
    public void RecordCollapsedEndMargin(ContainerBox box, double offset) => (_collapsedEndMargins ??= [])[box] = offset;

    /// <summary>
    /// Returns the positions of the breaks inside a box that avoids the floats, in the coordinates of its block formatting
    /// context (the block-start edges of its fragments after the first one), or null: the box is laid out in the layout
    /// opportunity of each of its fragments.
    /// </summary>
    public double[]? GetFloatAvoiderBreaks(ContainerBox box) => _floatAvoiderBreaks is { } breaks && breaks.TryGetValue(box, out var result) ? result : null;

    /// <summary>Returns whether the contents of a block container are not aligned by <c>align-content</c>: it is broken.</summary>
    public bool IsUnaligned(ContainerBox box) => _unalignedBoxes is not null && _unalignedBoxes.Contains(box);

    /// <summary>
    /// Returns whether the block-end margin of a box does not collapse with the block-end margins of its last children: they
    /// are in the contents that overflow its limited block size in a later fragmentainer than its block-end edge.
    /// </summary>
    public bool SeparatesEndMargin(ContainerBox box) => _separatedEndMargins is not null && _separatedEndMargins.Contains(box);

    /// <summary>Records the layouts of a box in the areas of its next fragments (see <see cref="InlineVariant"/>).</summary>
    public void SetInlineVariants(BoxFragment fragment, InlineVariant[] variants) => (_inlineVariants ??= [])[fragment] = variants;

    /// <summary>Returns the layouts of a box of the strip in the areas of its next fragments (see <see cref="InlineVariant"/>), or null.</summary>
    public InlineVariant[]? GetInlineVariants(BoxFragment fragment) => _inlineVariants is { } variants && variants.TryGetValue(fragment, out var result) ? result : null;

    /// <summary>
    /// Returns the variant of a box whose part starts at an offset from the block-start edge of its border box (see
    /// <see cref="InlineVariant"/>), or null when it is the area of its first fragment.
    /// </summary>
    public static InlineVariant? FindVariant(InlineVariant[] variants, double offset)
    {
        InlineVariant? result = null;
        foreach (var variant in variants)
        {
            if (variant.Start <= offset + Epsilon)
            {
                result = variant;
            }
        }

        return result;
    }

    /// <summary>
    /// Updates the inputs of the broken boxes whose layout does not depend on the floats (the alignment of their contents, their
    /// block-end margin) from the fragmentation of the strip. Returns whether the strip must be laid out again: these inputs
    /// only grow, and the floats are then pushed again from the start (the breaks of the content may change).
    /// </summary>
    /// <param name="flow">The strip.</param>
    /// <param name="map">The breaks of the flow of the strip.</param>
    /// <param name="children">The fragments of the strip.</param>
    /// <param name="stripSize">The physical size of the strip.</param>
    /// <param name="writingDirection">The writing mode of the fragmentation context.</param>
    /// <param name="blockSizes">The block size of each fragmentainer.</param>
    public bool UpdateBrokenBoxes(FragmentationFlow flow, FlowMap map, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, List<double> blockSizes)
        => (_alignedBoxes is { Count: > 0 } || _collapsedEndMargins is { Count: > 0 }) && Update(flow, map, children, stripSize, writingDirection, blockSizes, floatAvoiders: false);

    /// <summary>
    /// Updates the breaks of the boxes that avoid the floats (see <see cref="GetFloatAvoiderBreaks"/>) from the fragmentation of
    /// the strip, once its floats are pushed. Returns whether the strip must be laid out again.
    /// </summary>
    /// <param name="flow">The strip.</param>
    /// <param name="map">The breaks of the flow of the strip.</param>
    /// <param name="children">The fragments of the strip.</param>
    /// <param name="stripSize">The physical size of the strip.</param>
    /// <param name="writingDirection">The writing mode of the fragmentation context.</param>
    public bool UpdateFloatAvoiders(FragmentationFlow flow, FlowMap map, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection)
        => (_floatAvoiders is { Count: > 0 } || _floatAvoiderBreaks is { Count: > 0 }) && Update(flow, map, children, stripSize, writingDirection, blockSizes: null, floatAvoiders: true);

    private bool Update(FragmentationFlow flow, FlowMap map, ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, List<double>? blockSizes, bool floatAvoiders)
    {
        var found = new List<(BoxFragment Fragment, double Top, double Bottom)>();
        Find(children, stripSize, writingDirection, parentTop: 0, found);
        var changed = false;
        foreach (var (fragment, top, bottom) in found)
        {
            var box = fragment.Box;
            var parentMap = (fragment.Parent is BoxFragment parent ? flow.GetContainingItem(parent)?.Map : null) ?? map;
            var start = parentMap.Map(top).Index;
            var end = parentMap.MapEnd(bottom, top).Index;
            if (!floatAvoiders && _alignedBoxes is not null && _alignedBoxes.Contains(box) && end > start && (_unalignedBoxes ??= []).Add(box))
            {
                changed = true;
            }

            // (the margins are after the end of the fragmentainer where the box ends)
            if (!floatAvoiders && _collapsedEndMargins is not null && _collapsedEndMargins.TryGetValue(box, out var marginOffset) && blockSizes is { Count: > 0 }
                && parentMap.GetOffsetIn(end, top + marginOffset) > blockSizes[Math.Min(end, blockSizes.Count - 1)] + Epsilon
                && (_separatedEndMargins ??= []).Add(box))
            {
                changed = true;
            }

            if (floatAvoiders && _floatAvoiders is not null && _floatAvoiders.TryGetValue(box, out var blockOffset))
            {
                // The block-start edges of the fragments after the first one, in the block formatting context of the box
                List<double>? breaks = null;
                for (var index = start + 1; index <= end; index++)
                {
                    if (parentMap.GetRange(index) is var (rangeStart, _) && rangeStart > top + Epsilon && rangeStart < bottom - Epsilon)
                    {
                        (breaks ??= []).Add(blockOffset + rangeStart - top);
                    }
                }

                var previous = GetFloatAvoiderBreaks(box);
                if (!AreEqual(previous, breaks))
                {
                    if (breaks is null)
                    {
                        _floatAvoiderBreaks!.Remove(box);
                    }
                    else
                    {
                        (_floatAvoiderBreaks ??= [])[box] = [.. breaks];
                    }

                    changed = true;
                }
            }
        }

        return changed;

        static bool AreEqual(double[]? previous, List<double>? breaks)
        {
            if ((previous?.Length ?? 0) != (breaks?.Count ?? 0))
                return false;

            for (var i = 0; i < (breaks?.Count ?? 0); i++)
            {
                if (Math.Abs(previous![i] - breaks![i]) > Epsilon)
                    return false;
            }

            return true;
        }
    }

    /// <summary>Finds the fragments of the recorded boxes in the in-flow content of the strip, and their block ranges in the strip.</summary>
    private void Find(ReadOnlySpan<Fragment> children, PhysicalSize parentSize, WritingDirection writingDirection, double parentTop, List<(BoxFragment Fragment, double Top, double Bottom)> found)
    {
        foreach (var child in children)
        {
            if (child is not BoxFragment childBox || childBox.Box.IsOutOfFlow || childBox.Box.IsOutsideMarker)
                continue;

            var (top, bottom, _) = FragmentationFlow.GetBlockRange(child, parentSize, writingDirection, parentTop);
            var box = childBox.Box;
            if ((_floatAvoiders?.ContainsKey(box) ?? false) || (_alignedBoxes?.Contains(box) ?? false) || (_collapsedEndMargins?.ContainsKey(box) ?? false))
            {
                found.Add((childBox, top, bottom));
            }

            // (the boxes of another block direction are monolithic, the nested multi-column containers have their own strip)
            if (childBox.WritingDirection.BlockStart == writingDirection.BlockStart && !box.Style.Layout.IsMultiColumnContainer)
            {
                Find(childBox.ChildrenSpan[..childBox.InFlowChildCount], childBox.Size, writingDirection, top, found);
            }
        }
    }
}

/// <summary>
/// The layout of a box that avoids the floats in the area of its fragments from a position (see <see cref="StripRelayout"/>):
/// the content of the box from that position comes from that layout (its fragment in the strip contains it), and its fragments
/// there have another inline size and position.
/// </summary>
/// <param name="Start">The offset of the first fragment that has this area from the block-start edge of the border box of the box (logical, in its writing mode).</param>
/// <param name="InlineDelta">The offset of the inline-start edge of the border box in this area from the one of the first fragment (logical).</param>
/// <param name="InlineSize">The inline size of the border box in this area.</param>
/// <param name="PhysicalDelta">The physical offset of the border box in this area from the one of the first fragment.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct InlineVariant(double Start, double InlineDelta, double InlineSize, PhysicalOffset PhysicalDelta);
