namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a block container whose children are inline-level: an inline formatting context
/// (https://drafts.csswg.org/css2/#inline-formatting), whose contents are laid out in line boxes by
/// <see cref="InlineLayoutAlgorithm"/>.
/// </summary>
/// <remarks>
/// The intrinsic sizes of the content (<see cref="ComputeIntrinsicInlineSizes"/>) are the sizes of its lines broken at every
/// soft wrap opportunity, or only at the forced line breaks; they give the shrink-to-fit inline size of the atomic inlines,
/// floats, absolutely positioned boxes, outside markers and buttons whose contents are inline (see
/// <see cref="FormattingContext.GetAutomaticInlineSize"/>).
/// </remarks>
internal sealed class InlineFormattingContext : FormattingContext
{
    public static InlineFormattingContext Instance { get; } = new();

    private InlineFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Inline;

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        // https://drafts.csswg.org/css-overflow-4/#line-clamp: a line-clamp container measures its clamp point, then is clamped
        if (content.BlockFormattingContext is null && content.LineClamp is null && LineClamp.IsLineClampContainer(box))
            return LineClamp.Layout(context, this, box, builder, content);

        // https://drafts.csswg.org/css-text-4/#text-group-align-property: the line boxes are measured, then aligned as a group
        if (TextGroupAlignment.Applies(box, content))
            return TextGroupAlignment.Layout(context, this, box, builder, content);

        var algorithm = context.RentInlineLayout();
        try
        {
            return algorithm.Layout(context, box, builder, content);
        }
        finally
        {
            context.ReturnInlineLayout(algorithm);
        }
    }

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a block container whose children are inline
    /// (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes): the largest unbreakable segment of its lines, and its lines
    /// broken only at the forced line breaks. The atomic inlines and the floats contribute their intrinsic size contributions
    /// (<see cref="IntrinsicSizing.GetContribution"/>): they are not laid out.
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        var algorithm = context.RentInlineLayout();
        try
        {
            var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
            var content = new ContentArea(writingDirection, LogicalOffset.Zero, InlineSize: 0, input.ContentBlockSize) { QuirksPercentageBlockSize = input.QuirksPercentageBlockSize };
            var (minContent, maxContent) = algorithm.ComputeIntrinsicInlineSizes(context, box, content);
            return new IntrinsicSizes(minContent, maxContent);
        }
        finally
        {
            context.ReturnInlineLayout(algorithm);
        }
    }
}
