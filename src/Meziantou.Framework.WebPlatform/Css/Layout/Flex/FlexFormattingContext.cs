namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a flex container (https://drafts.csswg.org/css-flexbox-1/, with the balanced lines of
/// https://drafts.csswg.org/css-flexbox-2/), and of the legacy <c>-webkit-box</c>
/// (https://compat.spec.whatwg.org/#css-display-webkit-box), which the flex layout algorithm lays out with the properties of
/// the legacy box (<see cref="FlexContainerProperties"/>).
/// </summary>
/// <remarks>
/// The shared sizing of <see cref="FormattingContext.Layout"/> sizes the container (its <c>auto</c> inline size fills the
/// available space, or is its fit-content size from <see cref="ComputeIntrinsicInlineSizes"/>), then
/// <see cref="FlexLayoutAlgorithm"/> lays out the flex items in its content box (https://drafts.csswg.org/css-flexbox-1/#layout-algorithm)
/// and returns the block size of the contents: the cross size of the flex lines of a row container, the main size of the
/// items of a column container when its block size is indefinite.
/// </remarks>
internal sealed class FlexFormattingContext : FormattingContext
{
    public static FlexFormattingContext Instance { get; } = new();

    private FlexFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Flex;

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
        => new FlexLayoutAlgorithm(context, box, content).Layout(builder);

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a flex container
    /// (https://drafts.csswg.org/css-flexbox-1/#intrinsic-sizes): in the main axis of a row container, the sizes that give the
    /// items their contributions once their flexible lengths are resolved (<see cref="FlexLayoutAlgorithm.ComputeRowIntrinsicSize"/>);
    /// in the cross axis of a column container, the largest contributions of the items (the max-content size of a multi-line
    /// container whose block size is definite, or limited by a definite maximum, or that is balanced on several lines, is the
    /// sum of the cross sizes of its lines).
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        var layout = box.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);
        var properties = FlexContainerProperties.Get(box);
        if (properties.IsRow)
        {
            var min = FlexLayoutAlgorithm.ComputeRowIntrinsicSize(context, box, input.ContentBlockSize, minContent: true);
            var max = FlexLayoutAlgorithm.ComputeRowIntrinsicSize(context, box, input.ContentBlockSize, minContent: false);
            return new IntrinsicSizes(min, Math.Max(min, max));
        }

        var minContent = 0d;
        var maxContent = 0d;
        foreach (var child in box.Children)
        {
            if (child is not ContainerBox item || item.IsOutsideMarker || PositionedLayout.IsAbsolutelyPositioned(item))
                continue;

            var (itemMin, itemMax) = IntrinsicSizing.GetContribution(context, item, writingDirection, input.ContentBlockSize);
            minContent = Math.Max(minContent, itemMin);
            maxContent = Math.Max(maxContent, itemMax);
        }

        // A multi-line column container whose block size is definite: the items are collected into lines in its block size, its
        // max-content inline size is the sum of the cross sizes of the lines (as in the browsers)
        if (properties.IsMultiLine)
        {
            // The lines of a container with an indefinite block size break at its maximum block size, when it is a length
            double? maxContentBlockSize = null;
            var boxModel = layout.Box;
            if (input.ContentBlockSize is null && boxModel.GetMaxSize(!writingDirection.IsHorizontal) is { IsLengthPercentage: true, Value.HasPercentage: false } maxBlockSize)
            {
                var edges = layout.BoxSizing == BoxSizing.BorderBox ? writingDirection.ToLogical(ResolvePadding(boxModel, 0).Add(boxModel.BorderWidth)).BlockSum() : 0;
                maxContentBlockSize = Math.Max(0, maxBlockSize.Value.Resolve(0) - edges);
            }

            // (a balanced container has at least flex-line-count lines, whatever its block size)
            if (input.ContentBlockSize is not null || maxContentBlockSize is not null || (properties.IsBalanced && properties.LineCount > 1))
            {
                maxContent = Math.Max(minContent, FlexLayoutAlgorithm.ComputeColumnLinesCrossSize(context, box, input.ContentBlockSize, maxContentBlockSize));
            }
        }

        return new IntrinsicSizes(minContent, Math.Max(minContent, maxContent));
    }
}
