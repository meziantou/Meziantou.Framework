namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The input of a subgrid (https://drafts.csswg.org/css-grid-2/#subgrids), given by its parent grid before the subgrid is laid
/// out or measured (in <see cref="GridContainerState.Subgrid"/>): for each subgridded axis of the subgrid (its own axes), the
/// tracks of the parent grid it spans.
/// </summary>
internal sealed class SubgridInput
{
    /// <summary>The layout pass of the parent grid that gave the input (an input of a previous pass is stale).</summary>
    public int Pass { get; init; }

    /// <summary>The columns of the subgrid (its inline axis), when they are subgridded.</summary>
    public SubgridAxisInput? Columns { get; init; }

    /// <summary>The rows of the subgrid (its block axis), when they are subgridded.</summary>
    public SubgridAxisInput? Rows { get; init; }

    public SubgridAxisInput? Get(GridAxis axis) => axis == GridAxis.Columns ? Columns : Rows;
}

/// <summary>A subgridded axis of a subgrid (see <see cref="SubgridInput"/>).</summary>
internal sealed class SubgridAxisInput
{
    /// <summary>The axis of the parent grid the axis corresponds to (the axes are swapped when the subgrid is orthogonal to its parent).</summary>
    public GridAxis ParentAxis { get; init; }

    /// <summary>Whether the lines of the subgrid are numbered in the opposite direction of the lines of the parent grid (its start edge is the end edge of the parent).</summary>
    public bool IsReversed { get; init; }

    /// <summary>
    /// The number of tracks of the subgrid: "the number of explicit tracks in each subgridded dimension is taken from its used grid
    /// span in that dimension" (https://drafts.csswg.org/css-grid-2/#subgrid-span).
    /// </summary>
    public int Span { get; init; }

    /// <summary>
    /// The names the lines of the subgrid receive from the parent grid (one entry per line, null when a line has none): the
    /// explicitly-assigned line names of the corresponding lines of the parent (https://drafts.csswg.org/css-grid-2/#subgrid-line-name-inheritance)
    /// and the implicitly-assigned names of the named areas of the parent that the subgrid overlaps (https://drafts.csswg.org/css-grid-2/#subgrid-area-inheritance).
    /// </summary>
    public string[]?[]? LineNames { get; init; }

    /// <summary>The named areas of the parent grid that the subgrid overlaps, in the lines of the subgrid (inherited by its own subgrids).</summary>
    public List<(string Name, int Start, int End)>? Areas { get; init; }

    /// <summary>
    /// The extra margins of the items at the start and end edges of the subgrid: "the sum of the subgrid's margin, padding, scrollbar
    /// gutter, and border at each edge are applied as an extra layer of (potentially negative) margin to the items at those edges.
    /// This extra layer of margin accumulates through multiple levels of subgrids" (https://drafts.csswg.org/css-grid-2/#subgrid-margins).
    /// </summary>
    public double EdgeStart { get; init; }

    public double EdgeEnd { get; init; }

    /// <summary>The used gap of the parent grid in the axis (the difference with the gap of the subgrid is an extra margin of the items not at the edges, https://drafts.csswg.org/css-grid-2/#subgrid-gaps).</summary>
    public double ParentGap { get; init; }

    /// <summary>
    /// The used gap of the parent grid when it differs from the gap of the tracks (the parent is itself a subgrid with its own
    /// gap): the gap of a subgrid whose gap is <c>normal</c> ("the subgrid has the same size gutters as its parent grid").
    /// </summary>
    public double InheritedGap { get; init; }

    /// <summary>
    /// The shared baselines of the first-baseline groups of the parent grid in the tracks of the subgrid (by track, from the start
    /// edge of the track), which the items of the subgrid align with, or null.
    /// </summary>
    public Dictionary<int, double>? StartBaselines { get; set; }

    /// <summary>The shared baselines of the last-baseline groups of the parent grid (from the end edge of the track), or null.</summary>
    public Dictionary<int, double>? EndBaselines { get; set; }

    /// <summary>
    /// The positions of the start and end edges of the tracks, from the start edge of the content box of the subgrid (in its own
    /// direction), once the parent grid sized the tracks, else null.
    /// </summary>
    public (double Start, double End)[]? Tracks { get; set; }
}
