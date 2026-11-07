namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a grid container (https://drafts.csswg.org/css-grid-2/).
/// </summary>
/// <remarks>
/// The shared sizing of <see cref="FormattingContext.Layout"/> sizes the container (https://drafts.csswg.org/css-grid-2/#intrinsic-sizes:
/// "As a block-level box in a block formatting context, it is sized like a block box that establishes a formatting context [...]
/// As an inline-level box in an inline formatting context, it is sized as an atomic inline-level box"; its intrinsic sizes come
/// from <see cref="ComputeIntrinsicInlineSizes"/>), then <see cref="GridLayoutAlgorithm"/> lays out the grid items in its content
/// box and returns the block size of the contents: the size of its rows ("the grid container's auto block size is its
/// max-content size").
/// </remarks>
internal sealed class GridFormattingContext : FormattingContext
{
    public static GridFormattingContext Instance { get; } = new();

    private GridFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Grid;

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
        => GridLayoutAlgorithm.ForLayout(context, box, content).Layout(builder);

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a grid container
    /// (https://drafts.csswg.org/css-grid-2/#intrinsic-sizes): "the sum of the grid container's track sizes (including gutters) in
    /// the appropriate axis, when the grid is sized under a max-content constraint (min-content constraint)".
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
        => GridLayoutAlgorithm.ForIntrinsicSizes(context, box, input.ContentBlockSize, minInlineSize: input.MinInlineSize, maxInlineSize: input.MaxInlineSize, minBlockSize: input.MinBlockSize, maxBlockSize: input.MaxBlockSize).ComputeIntrinsicInlineSizes();

    /// <summary>The intrinsic inline sizes of a grid container with size containment: the sizes of its tracks without items (https://drafts.csswg.org/css-contain-2/#containment-size).</summary>
    protected internal override IntrinsicSizes ComputeEmptyIntrinsicInlineSizes(LayoutContext context, ContainerBox box, double? contentBlockSize)
        => GridLayoutAlgorithm.ForIntrinsicSizes(context, box, contentBlockSize, ignoresItems: true).ComputeIntrinsicInlineSizes();

    /// <summary>The automatic block size of a grid container with size containment: the size of its rows without items.</summary>
    protected internal override double ComputeEmptyContentBlockSize(LayoutContext context, ContainerBox box, in ContentArea content)
        => GridLayoutAlgorithm.ForLayout(context, box, content, ignoresItems: true).ComputeRowsSize();
}
