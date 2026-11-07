using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The state of the layout of a multi-column container that is laid out in the strip of another fragmentation context (a nested
/// multi-column container), stored in <see cref="LayoutBox.LayoutState"/>: the outer fragmentation context fragments the
/// content of the container itself (<see cref="NestedColumns"/>), from a strip that the container lays out again for it.
/// </summary>
internal sealed class MultiColumnState(Func<NestedColumns> factory)
{
    private Func<NestedColumns>? _factory = factory;

    /// <summary>Lays out the content of the container in a new strip, for the outer fragmentation context (once), or returns null.</summary>
    public NestedColumns? Take()
    {
        var create = _factory;
        _factory = null;
        return create?.Invoke();
    }

    /// <summary>Releases the layout pass (the state stays on the box until its next layout).</summary>
    public void Clear() => _factory = null;

    /// <summary>
    /// The static positions that the layout of the container in the strip of the outer fragmentation context gave to the
    /// absolutely positioned boxes of its content whose containing block is the container or an ancestor (logical, in the border
    /// box of the container), by box: the outer context moves the boxes to their static positions in the columns that it
    /// places (see <see cref="NestedColumns.OutOfFlow"/>).
    /// </summary>
    public Dictionary<ContainerBox, LogicalOffset>? StaticPositions { get; private set; }

    public void AddStaticPosition(ContainerBox box, LogicalOffset position) => (StaticPositions ??= [])[box] = position;
}

/// <summary>
/// The content of a multi-column container nested in another fragmentation context: its strip, the possible breaks of the strip
/// and its columns. The breaker of the outer context (<see cref="ColumnBreaker"/>) places its rows of columns and its column
/// spanners in the outer fragmentainers: each outer fragmentainer has a row of columns as tall as the block size that remains
/// in it (https://drafts.csswg.org/css-multicol-1/#nested-multicol), and the content that does not fit in the columns of the row
/// continues in the next outer fragmentainer. The slicer (<see cref="FragmentSlicer"/>) then creates a fragment of the container
/// per outer fragmentainer.
/// </summary>
internal sealed class NestedColumns
{
    public NestedColumns() => Fragmentainers = new OutOfFlowFragmentainers(Map);

    /// <summary>The fragments of the strip (not fragmented yet).</summary>
    public required Fragment[] Children { get; init; }

    public required PhysicalSize StripSize { get; init; }

    /// <summary>The possible breaks of the strip.</summary>
    public required FragmentationFlow Flow { get; init; }

    public required HashSet<ContainerBox>? SpannerBoxes { get; init; }

    public required WritingDirection WritingDirection { get; init; }

    public required int ColumnCount { get; init; }

    /// <summary>The distance between the inline-start edges of two columns.</summary>
    public required double ColumnStride { get; init; }

    public required double ColumnGap { get; init; }

    public required double RowGap { get; init; }

    /// <summary>The specified <c>column-count</c>, for the gap decorations.</summary>
    public int? SpecifiedColumnCount { get; init; }

    public bool HasGapRules { get; init; }

    /// <summary>Whether the columns of the last row are filled sequentially (<c>column-fill: auto</c>).</summary>
    public bool FillsSequentially { get; init; }

    /// <summary>Whether the columns wrap in rows (<c>column-wrap: wrap</c>).</summary>
    public bool Wraps { get; init; }

    /// <summary>The block size of the rows when it does not depend on their content (<c>column-height</c>), else null.</summary>
    public double? RowBlockSize { get; init; }

    /// <summary>The block size of the content box of the container when it is definite, else null.</summary>
    public double? BlockSize { get; init; }

    /// <summary>
    /// The maximum block size of the content box of the container when its block size is not definite (<c>max-height</c>), else
    /// null: the content that does not fit in the columns of the row where the content box reaches it overflows in the inline
    /// direction, as in a container with a definite block size.
    /// </summary>
    public double? MaxBlockSize { get; init; }

    /// <summary>The offset of the content box in the border box of the container (its inline-start and block-start edges).</summary>
    public LogicalOffset ContentOffset { get; init; }

    /// <summary>The inline size of the content box.</summary>
    public double ContentInlineSize { get; init; }

    /// <summary>Lays out another strip and returns its possible breaks: the breaker tries block sizes on it when the flow has parallel flows (whose state belongs to the placement in progress).</summary>
    public Func<FragmentationFlow>? CreateTrialFlow { get; init; }

    /// <summary>
    /// The absolutely positioned boxes of the strip whose containing block is the container or an ancestor (the outer
    /// fragmentation context lays them out), with their static positions in the strip: the static position of a box is in the
    /// columns that the breaker places (see <c>ColumnBreaker.TryGetNestedStaticPosition</c>).
    /// </summary>
    public List<(OutOfFlowCandidate Candidate, PhysicalOffset StaticPosition)>? OutOfFlow { get; init; }

    /// <summary>The static positions of the boxes of <see cref="OutOfFlow"/> in the layout of the container that the outer fragmentation context used for them (see <see cref="MultiColumnState.StaticPositions"/>).</summary>
    public Dictionary<ContainerBox, LogicalOffset>? StripStaticPositions { get; init; }

    private FragmentationFlow? _trialFlow;

    /// <summary>The flow the block size of balanced columns is computed on.</summary>
    public FragmentationFlow BalanceFlow => Flow.Groups is null || CreateTrialFlow is null ? Flow : _trialFlow ??= CreateTrialFlow();

    // The placement chosen by the breaker of the outer fragmentation context (its last run)

    /// <summary>The breaks of the strip: the index of a fragmentainer is the index of a column in <see cref="Columns"/>.</summary>
    public FlowMap Map { get; private set; } = new();

    /// <summary>The columns, in the order of the content.</summary>
    public List<NestedColumn> Columns { get; } = [];

    /// <summary>The column spanners that are placed, in the order of the content.</summary>
    public List<NestedSpanner> Spanners { get; } = [];

    /// <summary>The rows of columns and the spanners in each outer fragmentainer, for the gap decorations.</summary>
    public List<NestedRow> Rows { get; } = [];

    /// <summary>
    /// The columns as a sequence of fragmentainers, for the placement of the absolutely positioned boxes of the strip
    /// (<see cref="ColumnBreaker.PlaceOutOfFlow"/>): the block size that the content of each column of <see cref="Columns"/> is
    /// fragmented with, and the first column of each portion of the strip.
    /// </summary>
    public OutOfFlowFragmentainers Fragmentainers { get; private set; }

    /// <summary>Whether the columns of the last row overflow in the inline direction (the content box of a container with a definite block size ends in the outer fragmentainer of the row): the row has no maximum number of columns.</summary>
    public bool LastRowOverflows { get; set; }

    /// <summary>
    /// The number of columns that the in-flow content was placed in, once the absolutely positioned boxes of the strip are
    /// placed (the next columns of <see cref="Columns"/> were created for them), else -1.
    /// </summary>
    public int InFlowColumnCount { get; set; } = -1;

    /// <summary>The rows of the in-flow content when the columns created for the absolutely positioned boxes changed <see cref="Rows"/>, else null.</summary>
    public NestedRow[]? InFlowRows { get; set; }

    /// <summary>Forgets the placement: the breaker runs again.</summary>
    public void Reset()
    {
        Map = new FlowMap();
        Fragmentainers = new OutOfFlowFragmentainers(Map);
        LastRowOverflows = false;
        InFlowColumnCount = -1;
        InFlowRows = null;
        Columns.Clear();
        Spanners.Clear();
        Rows.Clear();
    }
}

/// <summary>A column of a nested multi-column container (see <see cref="NestedColumns"/>).</summary>
internal sealed class NestedColumn
{
    /// <summary>The index of the outer fragmentainer the column is in.</summary>
    public int OuterIndex { get; init; }

    /// <summary>The position of the strip where the content of the column starts.</summary>
    public double Start { get; init; }

    /// <summary>The position of the strip where the content of the column ends.</summary>
    public double End { get; set; } = double.PositiveInfinity;

    public double BlockSize { get; set; }

    public double DisplayOffset { get; init; }

    /// <summary>The index of the column in its row.</summary>
    public int IndexInRow { get; init; }

    /// <summary>The block offset of the column in the outer fragmentainer.</summary>
    public double BlockOffset { get; init; }
}

/// <summary>A column spanner of a nested multi-column container, fragmented by the outer fragmentation context (see <see cref="NestedColumns"/>).</summary>
internal sealed class NestedSpanner
{
    public required BoxFragment Fragment { get; init; }

    /// <summary>The possible breaks of the spanner (the positions are relative to the block-start edge of its border box).</summary>
    public required FragmentationFlow Flow { get; init; }

    /// <summary>The breaks of the spanner in the outer fragmentainers.</summary>
    public required FlowMap Map { get; init; }

    /// <summary>The outer fragmentainer where the border box of the spanner ends and the offset of its block-end edge in it, once it is placed.</summary>
    public (int Index, double Offset)? End { get; set; }

    public object? Cursor { get; set; }
}

/// <summary>A row of columns, or the part of a column spanner, of a nested multi-column container in an outer fragmentainer.</summary>
/// <param name="OuterIndex">The index of the outer fragmentainer.</param>
/// <param name="Columns">The number of columns of the row, or -1 for a column spanner.</param>
/// <param name="Start">The block offset of the row in the outer fragmentainer.</param>
/// <param name="End">The block offset of the end of the row in the outer fragmentainer.</param>
/// <param name="IsContinuation">Whether the row continues a row of a fixed block size that is broken by the outer fragmentainers (no row gap before it).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct NestedRow(int OuterIndex, int Columns, double Start, double End, bool IsContinuation = false);
