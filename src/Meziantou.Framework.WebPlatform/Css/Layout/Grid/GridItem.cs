namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The baseline-sharing group of a grid item in one axis (https://drafts.csswg.org/css-align-3/#baseline-sharing-group): the side of the alignment context its baseline is aligned from.</summary>
internal enum GridBaselineGroup : byte
{
    None,

    /// <summary>The first-baseline group: the baselines are measured from the start edge of the start track of the item.</summary>
    Start,

    /// <summary>The last-baseline group: the baselines are measured from the end edge of the end track of the item.</summary>
    End,
}

/// <summary>A grid item (https://drafts.csswg.org/css-grid-2/#grid-items) and the state of its layout in its grid.</summary>
internal sealed class GridItem(ContainerBox box, int index, int order)
{
    public ContainerBox Box { get; } = box;

    public LayoutStyle Style { get; } = box.Style.Layout;

    /// <summary>The position of the item among the children of the grid container (document order).</summary>
    public int Index { get; } = index;

    /// <summary>The <c>order</c> of the item (https://drafts.csswg.org/css-display/#order-property).</summary>
    public int Order { get; } = order;

    public WritingDirection WritingDirection { get; set; }

    /// <summary>Whether the item establishes an orthogonal flow (its inline axis is the block axis of the grid).</summary>
    public bool IsOrthogonal { get; set; }

    /// <summary>Whether the contributions of the item to the columns depend on the sizes of the rows (see <see cref="GridLayoutAlgorithm"/>), or null when it is not determined yet.</summary>
    public bool? DependsOnRows { get; set; }

    /// <summary>The placement of the item in the columns, resolved against the explicit grid (lines from 0, the first explicit line).</summary>
    public GridAxisPlacement ColumnPlacement { get; set; }

    /// <summary>The placement of the item in the rows.</summary>
    public GridAxisPlacement RowPlacement { get; set; }

    // The grid area: the indices of the first track and of the track after the last one, in the implicit grid (from 0)
    public int ColumnStart { get; set; }

    public int ColumnEnd { get; set; }

    public int RowStart { get; set; }

    public int RowEnd { get; set; }

    /// <summary>
    /// The extra margins of the item in each axis of its grid (start and end sides): the margins, borders, paddings and scrollbar
    /// gutters of the subgrids whose edges it is at, and half the difference of the gaps of the subgrids and of their parent
    /// grids at the other lines (https://drafts.csswg.org/css-grid-2/#subgrid-margins).
    /// </summary>
    public double ExtraColumnStart { get; set; }

    public double ExtraColumnEnd { get; set; }

    public double ExtraRowStart { get; set; }

    public double ExtraRowEnd { get; set; }

    /// <summary>The self-alignment of the item in the columns (<c>justify-self</c>).</summary>
    public AxisAlignment JustifySelf { get; set; }

    /// <summary>The self-alignment of the item in the rows (<c>align-self</c>).</summary>
    public AxisAlignment AlignSelf { get; set; }

    public bool JustifySafe { get; set; }

    public bool AlignSafe { get; set; }

    /// <summary>Whether the item is stretched in the inline axis of the grid (https://drafts.csswg.org/css-grid-2/#grid-item-sizing): its size fills its grid area.</summary>
    public bool StretchesColumns { get; set; }

    /// <summary>Whether the item is stretched in the block axis of the grid.</summary>
    public bool StretchesRows { get; set; }

    /// <summary>
    /// Whether the item would be stretched in the block axis of the grid if it had no preferred aspect ratio (<c>normal</c>
    /// alignment, an <c>auto</c> block size, no <c>auto</c> margin): the stretch-fit block size of its grid area is transferred
    /// through its aspect ratio to its contributions to the columns.
    /// </summary>
    public bool TransfersStretchedBlockSize { get; set; }

    /// <summary>Whether the size of the item in the inline axis of the grid is the one of a block-level box (<c>normal</c> with a preferred aspect ratio): it fills the area unless its aspect ratio transfers its block size.</summary>
    public bool FillsColumns { get; set; }

    // The auto margins in the axes of the grid (start and end)
    public bool AutoMarginColumnStart { get; set; }

    public bool AutoMarginColumnEnd { get; set; }

    public bool AutoMarginRowStart { get; set; }

    public bool AutoMarginRowEnd { get; set; }

    /// <summary>Whether the item is a subgrid subgridded in the columns of its grid: it is stretched in that axis, whatever its size and alignment.</summary>
    public bool SubgridsColumns { get; set; }

    /// <summary>Whether the item is a subgrid subgridded in the rows of its grid.</summary>
    public bool SubgridsRows { get; set; }

    /// <summary>The subgrid the item is, when it is a subgrid of its grid (https://drafts.csswg.org/css-grid-2/#subgrids), else null.</summary>
    public GridLayoutAlgorithm? Subgrid { get; set; }

    /// <summary>The grid whose tracks the item is placed in (the subgrid that contains a hoisted item).</summary>
    public GridLayoutAlgorithm? Owner { get; set; }

    /// <summary>The baseline-sharing group of the item in the columns (<c>justify-self: baseline</c>) and in the rows (<c>align-self: baseline</c>).</summary>
    public GridBaselineGroup ColumnBaselineGroup { get; set; }

    public GridBaselineGroup RowBaselineGroup { get; set; }

    /// <summary>
    /// The distance of the baseline of the item from the edge of its track on the side of its baseline group (its extra margins and
    /// its margin included), in the columns and in the rows.
    /// </summary>
    public double ColumnBaseline { get; set; }

    public double RowBaseline { get; set; }

    /// <summary>
    /// The baseline content-alignment of the item in the columns and in the rows
    /// (https://drafts.csswg.org/css-align-3/#baseline-align-content: <c>align-content: baseline</c> or <c>last baseline</c> on
    /// an item whose block axis is the axis and whose self-alignment is <c>stretch</c>, or the side of the baseline), or
    /// <see cref="AxisAlignment.Start"/> when its contents are not aligned by baseline: the item is in the baseline-sharing group
    /// of its track, and its shim is space added in its box, before (after) its contents.
    /// </summary>
    public AxisAlignment ColumnContentBaseline { get; set; }

    public AxisAlignment RowContentBaseline { get; set; }

    public AxisAlignment GetContentBaseline(GridAxis axis) => axis == GridAxis.Columns ? ColumnContentBaseline : RowContentBaseline;

    /// <summary>The shim of the baseline alignment (https://drafts.csswg.org/css-grid-2/#algo-baseline-shims) in the columns and in the rows: added to the margin on the side of the baseline group.</summary>
    public double ColumnShim { get; set; }

    public double RowShim { get; set; }

    /// <summary>
    /// How the shim of the item in the columns and in the rows varies with its size, for its contributions of another size than
    /// the one its baseline was measured with: the baseline of an item whose baseline is synthesized from its border box or measured
    /// from the other side moves with its size. The shim for an outer size is <c>shim + Factor × (MeasuredSize - size)</c>.
    /// </summary>
    public (double Factor, double MeasuredSize) ColumnShimVariation { get; set; }

    public (double Factor, double MeasuredSize) RowShimVariation { get; set; }

    /// <summary>The contributions of the item in the columns and in the rows, cached for the size of its area in the other axis.</summary>
    public GridContributions ColumnContributions;

    public GridContributions RowContributions;

    /// <summary>The last layout of the item and its constraint space.</summary>
    public BoxFragment? Fragment { get; set; }

    public ConstraintSpace Space { get; set; }

    /// <summary>The position of the border box of the item in the content box of the grid container, in the logical coordinates of the grid.</summary>
    public LogicalOffset Offset { get; set; }

    /// <summary>Whether the item of a grid lanes container has an automatic position in the grid axis: it is placed by the grid lanes placement algorithm.</summary>
    public bool IsLanesAuto { get; set; }

    /// <summary>
    /// The position of the item in the stacking axis of a grid lanes container: the start of its margin box from the start of the
    /// stacking range, in the direction of the stacking (from the end edge with <c>fill-reverse</c>).
    /// </summary>
    public double LanesPosition { get; set; }

    /// <summary>The outer size of the item in the stacking axis (floored at zero).</summary>
    public double LanesOuterSize { get; set; }

    /// <summary>The alignment container of the item in the stacking axis (https://drafts.csswg.org/css-grid-3/#stacking-axis-self-alignment): its start from the flow-start edge of the content box, and its size.</summary>
    public double LanesAreaStart { get; set; }

    public double LanesAreaSize { get; set; }

    /// <summary>The baseline shims of an auto-placed item of a grid lanes container in the grid axis, by start track (the item is sized in every position), or null.</summary>
    public double[]? LanesShims { get; set; }

    /// <summary>Whether the item is stretched in the stacking axis (<c>stretch</c> with an <c>auto</c> size and no <c>auto</c> margin).</summary>
    public bool StretchesStack { get; set; }

    public int GetStart(GridAxis axis) => axis == GridAxis.Columns ? ColumnStart : RowStart;

    public int GetEnd(GridAxis axis) => axis == GridAxis.Columns ? ColumnEnd : RowEnd;

    public int GetSpan(GridAxis axis) => GetEnd(axis) - GetStart(axis);

    public double GetExtraStart(GridAxis axis) => axis == GridAxis.Columns ? ExtraColumnStart : ExtraRowStart;

    public double GetExtraEnd(GridAxis axis) => axis == GridAxis.Columns ? ExtraColumnEnd : ExtraRowEnd;

    public AxisAlignment GetAlignment(GridAxis axis) => axis == GridAxis.Columns ? JustifySelf : AlignSelf;

    public bool Stretches(GridAxis axis) => axis == GridAxis.Columns ? StretchesColumns : StretchesRows;

    public GridBaselineGroup GetBaselineGroup(GridAxis axis) => axis == GridAxis.Columns ? ColumnBaselineGroup : RowBaselineGroup;

    public double GetShim(GridAxis axis) => axis == GridAxis.Columns ? ColumnShim : RowShim;

    public (double Factor, double MeasuredSize) GetShimVariation(GridAxis axis) => axis == GridAxis.Columns ? ColumnShimVariation : RowShimVariation;

    /// <summary>Returns whether the axis of the grid is the inline axis of the item.</summary>
    public bool IsInlineAxis(GridAxis axis) => (axis == GridAxis.Columns) != IsOrthogonal;
}

/// <summary>The intrinsic size contributions of a grid item in one axis (outer sizes), cached for the size of its grid area in the other axis.</summary>
internal struct GridContributions
{
    public bool IsValid;

    /// <summary>The size of the grid area in the other axis the contributions were computed with (null: indefinite).</summary>
    public double? OtherAreaSize;

    /// <summary>https://drafts.csswg.org/css-sizing-3/#min-content-contribution</summary>
    public double MinContent;

    /// <summary>https://drafts.csswg.org/css-sizing-3/#max-content-contribution</summary>
    public double MaxContent;

    /// <summary>
    /// Whether the preferred size of the item behaves as <c>auto</c> or depends on the size of its containing block: its minimum
    /// contribution is then its used minimum size plus its margins (https://drafts.csswg.org/css-grid-2/#minimum-contribution),
    /// else its min-content contribution.
    /// </summary>
    public bool DependsOnContainingBlock;

    /// <summary>Whether the minimum size of the item is <c>auto</c>: its automatic minimum size depends on the tracks it spans (https://drafts.csswg.org/css-grid-2/#min-size-auto).</summary>
    public bool MinSizeIsAuto;

    /// <summary>The used minimum size (border box) when it is not <c>auto</c>.</summary>
    public double MinSize;

    /// <summary>The content-based minimum size (border box) before the clamping by the tracks (https://drafts.csswg.org/css-grid-2/#content-based-minimum-size).</summary>
    public double ContentSuggestion;

    /// <summary>Whether the item scrolls in the axis: its automatic minimum size is 0.</summary>
    public bool IsScrollable;

    /// <summary>The paddings and borders of the item in the axis.</summary>
    public double PaddingAndBorder;

    /// <summary>The margins of the item in the axis.</summary>
    public double Margins;
}

/// <summary>
/// An item that participates in the sizing of the tracks of an axis of a grid (https://drafts.csswg.org/css-grid-2/#algo-track-sizing):
/// a grid item of the grid, or an item of a subgrid that is subgridded in that axis (https://drafts.csswg.org/css-grid-2/#subgrid-item-contribution),
/// with its span in the tracks of the grid and its extra margins.
/// </summary>
/// <param name="item">The grid item.</param>
/// <param name="owner">The grid the item is placed in (the subgrid of a nested item), which computes its contributions.</param>
/// <param name="ownerAxis">The axis of the owner that corresponds to the sized axis (the axes are swapped for an orthogonal subgrid).</param>
/// <param name="axis">The sized axis.</param>
/// <param name="start">The first track spanned in the sized grid.</param>
/// <param name="end">The track after the last track spanned in the sized grid.</param>
internal sealed class GridSizingItem(GridItem item, GridLayoutAlgorithm owner, GridAxis ownerAxis, GridAxis axis, int start, int end)
{
    public GridItem Item { get; } = item;

    public GridLayoutAlgorithm Owner { get; } = owner;

    public GridAxis OwnerAxis { get; } = ownerAxis;

    public GridAxis Axis { get; } = axis;

    public int Start { get; } = start;

    public int End { get; } = end;

    public int Span => End - Start;

    /// <summary>The extra margins of the item on the start and end sides of the sized axis (the margins, borders and paddings of the subgrids, https://drafts.csswg.org/css-grid-2/#subgrid-margins).</summary>
    public double ExtraStart { get; init; }

    public double ExtraEnd { get; init; }

    /// <summary>The baseline shim of the item (https://drafts.csswg.org/css-grid-2/#algo-baseline-shims).</summary>
    public double Shim { get; init; }

    /// <summary>How <see cref="Shim"/> varies with the size of the contributions (see <see cref="GridItem.ColumnShimVariation"/>).</summary>
    public (double Factor, double MeasuredSize) ShimVariation { get; init; }

    /// <summary>Whether the item is in a subgrid whose lines go in the opposite direction (its baselines are measured from the other side).</summary>
    public bool IsReversed { get; init; }

    /// <summary>
    /// Whether the item is a hypothetical item of an edge of a subgrid (<see cref="Item"/>, https://drafts.csswg.org/css-grid-2/#subgrid-edge-placeholders):
    /// its size is zero, it only contributes its extra margins (the margin, border and padding of the edge) to the sizing of the tracks.
    /// </summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>
    /// Whether the item is an item of a grid lanes subgrid with an automatic position in its grid axis, which is sized in every
    /// track it could be placed in (https://drafts.csswg.org/css-grid-3/#subgrid-item-contribution): it does not participate in
    /// the baseline alignment of the items of the grid (its track is not known yet).
    /// </summary>
    public bool IsHypothetical { get; init; }
}
