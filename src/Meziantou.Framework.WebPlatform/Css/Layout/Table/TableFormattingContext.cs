namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a table grid box (https://drafts.csswg.org/css-tables-3/#table-grid-box) and of the internal table boxes (the
/// row groups, the rows, the columns and the column groups, which are laid out by the table layout of their table).
/// </summary>
/// <remarks>
/// The table grid box is laid out by its table wrapper box (<see cref="TableWrapperFormattingContext"/>), which computes the
/// width of the table with its captions: <see cref="TableLayoutAlgorithm"/>. The table cells are block containers: their
/// contents are laid out by the block and inline layouts, in the sizes given by the table layout.
/// </remarks>
internal sealed class TableFormattingContext : FormattingContext
{
    public static TableFormattingContext Instance { get; } = new();

    private TableFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Table;

    /// <summary>
    /// Lays out a table grid box on its own (its table wrapper box lays it out with <see cref="TableLayoutAlgorithm"/>): its width
    /// is computed in the available space without caption. An internal table box is never laid out on its own: it gets an empty
    /// fragment.
    /// </summary>
    public override BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        if (box.Kind != ContainerBoxKind.TableGrid)
            return new BoxFragment(box, PhysicalSize.Zero, default, default, default, children: null);

        var algorithm = new TableLayoutAlgorithm(context, box);
        var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
        var isOrthogonal = writingDirection.IsOrthogonalTo(space.WritingDirection);
        var percentageBasis = space.GetPercentageBasis(writingDirection.IsHorizontal);
        var inlineSize = (isOrthogonal ? space.FixedBlockSize : space.FixedInlineSize)
            ?? algorithm.ComputeTableInlineSize(isOrthogonal ? space.AvailableBlockSize ?? context.InitialContainingBlockSize.Width : space.AvailableInlineSize, percentageBasis, captionMin: 0);
        return algorithm.Layout(inlineSize, space.PercentageInlineSize, space.GetPercentageBasis(!writingDirection.IsHorizontal), isOrthogonal ? space.FixedInlineSize : space.FixedBlockSize);
    }

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content) => new(0);

    /// <summary>
    /// Returns the intrinsic inline sizes of the content box of a table grid box (https://drafts.csswg.org/css-tables-3/#intrinsic-sizing):
    /// GRIDMIN and GRIDMAX without its used borders and paddings. The internal table boxes have none.
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        if (box.Kind != ContainerBoxKind.TableGrid)
            return default;

        var algorithm = new TableLayoutAlgorithm(context, box);
        var edges = algorithm.Border.InlineSum() + algorithm.GetPadding(0).InlineSum();
        return algorithm.ComputeGridIntrinsicSizes().Add(-edges);
    }
}
