namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The group alignment of the line boxes of a block container (<c>text-group-align</c>,
/// https://drafts.csswg.org/css-text-4/#text-group-align-property): "Group alignment is performed by finding the line box with
/// the shortest remaining space and adding that amount of space as padding to one or both sides of the line box, reducing the
/// amount of space available for its contents; text alignment is then applied to its contents within the remaining space.
/// All descendant in-flow line boxes within the same block formatting context are considered both when searching for the
/// shortest remaining space and when adding the padding; the contents of descendants that establish independent formatting
/// contexts are skipped."
/// </summary>
/// <remarks>
/// The contents of the block container are laid out a first time to measure the remaining space of their line boxes
/// (<see cref="Measure"/>), then again with the padding (<see cref="GetLineLeftPadding"/>): the padding is not larger than the
/// remaining space of any line, so the lines are broken at the same positions; they are aligned and justified in the space
/// that remains. The state is the one of the boxes that participate in the block formatting context of the container while
/// its contents are laid out (<see cref="BlockFormattingContextState.TextGroupAlignment"/>); the line boxes of a descendant
/// with its own <c>text-group-align</c> are aligned by that descendant only.
/// </remarks>
internal sealed class TextGroupAlignment
{
    private double _remainingSpace = double.PositiveInfinity;

    private TextGroupAlignment(ContainerBox owner, TextGroupAlign align, double? padding)
    {
        Owner = owner;
        Align = align;
        Padding = padding;
    }

    /// <summary>The block container whose <c>text-group-align</c> aligns the line boxes.</summary>
    public ContainerBox Owner { get; }

    public TextGroupAlign Align { get; }

    /// <summary>The space added to the sides of each line box, or null in the layout that measures it.</summary>
    public double? Padding { get; }

    /// <summary>Returns whether the line boxes of the contents of a box are aligned as a group by the box.</summary>
    public static bool Applies(ContainerBox box, in ContentArea content)
    {
        if (box.Kind != ContainerBoxKind.Normal || box.IsInlineBox || !box.IsBlockContainer || box.Style.Layout.TextGroupAlign == TextGroupAlign.None)
            return false;

        // (not in the layouts of the group alignment itself)
        return (content.BlockFormattingContext?.TextGroupAlignment ?? content.TextGroupAlignment)?.Owner != box;
    }

    /// <summary>Records the remaining space of a line box (the space of the line that its content does not use, before the justification).</summary>
    public void Measure(double remainingSpace) => _remainingSpace = Math.Min(_remainingSpace, Math.Max(0, remainingSpace));

    /// <summary>
    /// Returns the part of <see cref="Padding"/> that is on the line-left side of the line boxes of a block container:
    /// <c>start</c> pads the inline-end side, <c>end</c> the inline-start side, <c>left</c> the line-right side, <c>right</c> the
    /// line-left side, and <c>center</c> both sides, "half the spacing to each side".
    /// </summary>
    public double GetLineLeftPadding(bool isRightToLeft)
    {
        var padding = Padding ?? 0;
        return Align switch
        {
            TextGroupAlign.Start => isRightToLeft ? padding : 0,
            TextGroupAlign.End => isRightToLeft ? 0 : padding,
            TextGroupAlign.Right => padding,
            TextGroupAlign.Center => padding / 2,
            _ => 0,
        };
    }

    /// <summary>
    /// Lays out the contents of a block container whose <c>text-group-align</c> is not <c>none</c> with its formatting context: a
    /// first layout measures the shortest remaining space of the line boxes; when it is not zero, the contents are laid out
    /// again with the padding.
    /// </summary>
    public static ContentLayoutResult Layout(LayoutContext context, FormattingContext formattingContext, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        var align = box.Style.Layout.TextGroupAlign;
        var measure = new TextGroupAlignment(box, align, padding: null);
        var state = content.BlockFormattingContext?.Save();
        var markerWasPlaced = content.OutsideMarker?.IsPlaced;
        var result = LayoutPass(context, formattingContext, box, builder, content, measure);
        if (double.IsInfinity(measure._remainingSpace) || measure._remainingSpace <= 0)
            return result;

        if (state is { } snapshot)
        {
            content.BlockFormattingContext!.Restore(snapshot);
        }

        if (content.OutsideMarker is { } marker && markerWasPlaced == false)
        {
            marker.IsPlaced = false;
        }

        builder.Reset(builder.WritingDirection);
        return LayoutPass(context, formattingContext, box, builder, content, new TextGroupAlignment(box, align, measure._remainingSpace));
    }

    private static ContentLayoutResult LayoutPass(LayoutContext context, FormattingContext formattingContext, ContainerBox box, FragmentBuilder builder, in ContentArea content, TextGroupAlignment group)
    {
        // The boxes that participate in the block formatting context of an ancestor share its state: the group is the current
        // one while the contents of the box are laid out
        if (content.BlockFormattingContext is not { } blockFormattingContext)
            return formattingContext.LayoutFlow(context, box, builder, content with { TextGroupAlignment = group });

        var previous = blockFormattingContext.TextGroupAlignment;
        blockFormattingContext.TextGroupAlignment = group;
        try
        {
            return formattingContext.LayoutFlow(context, box, builder, content);
        }
        finally
        {
            blockFormattingContext.TextGroupAlignment = previous;
        }
    }
}
