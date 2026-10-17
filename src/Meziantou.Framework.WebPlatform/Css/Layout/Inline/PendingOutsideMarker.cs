namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// An outside <c>::marker</c> (https://drafts.csswg.org/css-lists-3/#list-style-position-outside) waiting to be placed next to
/// the first line box of its list item. The list item passes it to its first in-flow child when its content is block-level
/// (<see cref="ConstraintSpace.OutsideMarker"/>), so the marker is aligned with the first line box even when it is in a
/// descendant block container, as in the browsers.
/// </summary>
internal sealed class PendingOutsideMarker(ContainerBox marker, ContainerBox listItem)
{
    /// <summary>The box of the marker (an inline-level flow-root box).</summary>
    public ContainerBox Marker { get; } = marker;

    /// <summary>The block container of the list item, which places the marker at its block-start edge when no line box placed it.</summary>
    public ContainerBox ListItem { get; } = listItem;

    /// <summary>Whether a line box placed the marker.</summary>
    public bool IsPlaced { get; set; }

    /// <summary>
    /// Places the marker in a descendant block container of the list item that has no line box but has a first baseline (e.g.
    /// its first child is a flex container): the marker is next to the content box of the block container, its baseline on
    /// the first baseline, like a marker placed by a line box of the block container.
    /// </summary>
    /// <param name="firstBaseline">The first baseline of the in-flow children of the block container, from the block-start edge of its content box.</param>
    public void PlaceAtBaseline(LayoutContext context, FragmentBuilder builder, in ContentArea content, double firstBaseline)
    {
        var writingDirection = content.WritingDirection;
        var fragment = context.LayoutBox(Marker, content.ChildSpace);
        var margin = writingDirection.ToLogical(fragment.Margin);
        var size = writingDirection.ToLogical(fragment.Size);
        var baseline = fragment.FirstBaseline ?? size.Block;
        builder.AddChild(fragment, new LogicalOffset(content.Offset.Inline - margin.InlineEnd - size.Inline, content.Offset.Block + firstBaseline - baseline));
        IsPlaced = true;
    }

    /// <summary>
    /// Places the marker of a list item that has no line box at the block-start edge of the content box of the list item, and
    /// returns the block size of its margin box (the content of the list item is at least as tall, as in the browsers). In
    /// horizontal writing modes, its baseline is aligned with the baseline of a line box of the list item (its strut), as in the
    /// browsers: the marker is at the same position as in a list item whose first line is empty. When the content of the list
    /// item has a first baseline that no line box of the block formatting context gives (the one of a flex or grid container,
    /// of a table, of a block container that establishes a formatting context), the marker is aligned with it, as in the browsers.
    /// </summary>
    /// <param name="firstBaseline">The first baseline of the in-flow children of the list item, from the block-start edge of its content box, or null.</param>
    /// <param name="floatIntrusion">The inline size that the floats take at the inline-start edge of the content box, at the position of the list item (the line box that the marker would be next to is shortened by them).</param>
    public double PlaceWithoutLine(LayoutContext context, FragmentBuilder builder, in ContentArea content, double? firstBaseline, double floatIntrusion = 0)
    {
        var writingDirection = content.WritingDirection;
        var fragment = context.LayoutBox(Marker, content.ChildSpace);
        var margin = writingDirection.ToLogical(fragment.Margin);
        var size = writingDirection.ToLogical(fragment.Size);
        var blockOffset = margin.BlockStart;
        if (firstBaseline is { } contentBaseline && fragment.FirstBaseline is { } markerBaseline)
        {
            blockOffset = contentBaseline - markerBaseline;
        }
        else if (writingDirection.IsHorizontal && fragment.FirstBaseline is { } baseline)
        {
            var layout = ListItem.Style.Layout;
            var metrics = ListItem.Style.Fonts.PrimaryFont.GetMetrics(layout.FontSize);
            var strutBaseline = metrics.Ascent + Math.Floor((layout.UsedLineHeight - (metrics.Ascent + metrics.Descent)) / 2);
            // (a marker taller than the strut makes this line taller: it starts at the block-start edge)
            blockOffset = Math.Max(0, strutBaseline - baseline);
        }

        // Outside of the border box of the list item (see InlineLayoutAlgorithm.PlaceOutsideMarker)
        builder.AddChild(fragment, new LogicalOffset(floatIntrusion - margin.InlineEnd - size.Inline, content.Offset.Block + blockOffset));
        IsPlaced = true;
        return Math.Max(0, blockOffset + size.Block + margin.BlockEnd);
    }
}
