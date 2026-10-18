using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kinds of containers that have gaps (https://drafts.csswg.org/css-gaps-1/#gaps).</summary>
internal enum GapContainerKind : byte
{
    Grid,
    GridLanes,
    Flex,
    MultiColumn,
}

/// <summary>
/// The state of a portion of a gap between two consecutive intersections (https://drafts.csswg.org/css-gaps-1/#segments): whether
/// an item spans across it (the endpoints are discontiguous), and whether the areas on its two sides are empty
/// (https://drafts.csswg.org/css-gaps-1/#visibility).
/// </summary>
[Flags]
internal enum GapSegmentState : byte
{
    /// <summary>The areas on both sides are occupied by different items.</summary>
    None = 0,

    /// <summary>The area before the gap (block-start side of a row gap, inline-start side of a column gap) is empty.</summary>
    EmptyBefore = 1,

    /// <summary>The area after the gap is empty.</summary>
    EmptyAfter = 2,

    /// <summary>An item spans across the gap.</summary>
    Blocked = 4,
}

/// <summary>
/// The role of an intersection in an overlap window: the region of a gap of a container whose crossing gaps are not aligned
/// (flex lines, grid lanes) where the crossing gaps of its two sides overlap one another. The window is one gap junction
/// (https://drafts.csswg.org/css-gaps-1/#gap-junction): "in cases where gaps intersect or abut one another at overlapping
/// intervals, the crossing gap width will be determined by the union of such intervals".
/// </summary>
internal enum GapOverlapWindow : byte
{
    None,

    /// <summary>The first crossing gap of the window.</summary>
    Open,

    /// <summary>The last crossing gap of the window.</summary>
    Close,
}

/// <summary>
/// A point of the centerline of a gap where its decorations may start or end
/// (https://drafts.csswg.org/css-gaps-1/#segment-endpoints): a bound of the gap, or the center of a gap junction (the start and
/// end segment endpoints of the junction are at half the <see cref="CrossWidth"/> on each side).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct GapIntersection
{
    /// <summary>The position along the gap: the offset of the bound, or of the center of the crossing gap.</summary>
    public double Offset;

    /// <summary>The size of the crossing gap along the gap (0 at a bound).</summary>
    public double CrossWidth;

    /// <summary>https://drafts.csswg.org/css-gaps-1/#crossing-gap-width: the percentage basis of the insets (the size of the gap junction; 0 at a bound).</summary>
    public double InsetBasis;

    /// <summary>The state of the portion of the gap between this intersection and the next one.</summary>
    public GapSegmentState State;

    public GapOverlapWindow Window;

    /// <summary>Whether the intersection is a bound of the gap that no gap crosses (an edge of the content, a column spanner): a cap segment endpoint.</summary>
    public bool IsEdge;

    /// <summary>The index of the crossing gap in the gaps of the other direction (the width of its decoration is used by <c>overlap-join</c>), or -1.</summary>
    public int CrossGapIndex;

    /// <summary>Whether <see cref="CrossBefore"/> and <see cref="CrossAfter"/> are known (the containers whose gaps are aligned: grid, multi-column).</summary>
    public bool HasCrossStates;

    /// <summary>The state of the portion of the crossing gap that ends at the junction.</summary>
    public GapSegmentState CrossBefore;

    /// <summary>The state of the portion of the crossing gap that starts at the junction.</summary>
    public GapSegmentState CrossAfter;

    /// <summary>The flow of the crossing gap (see <see cref="GapLine.FlowKey"/>).</summary>
    public int FlowKey;

    public static GapIntersection Edge(double offset, GapSegmentState state = GapSegmentState.None)
        => new() { Offset = offset, State = state, IsEdge = true, CrossGapIndex = -1 };

    public static GapIntersection Junction(double offset, double crossWidth, int crossGapIndex, GapSegmentState state = GapSegmentState.None)
        => new() { Offset = offset, CrossWidth = crossWidth, InsetBasis = crossWidth, CrossGapIndex = crossGapIndex, State = state };
}

/// <summary>
/// A gap of a container (https://drafts.csswg.org/css-gaps-1/#gap) in the logical coordinates of the border box of the
/// container: the position of its centerline, its intersections in order, and the gap decoration values assigned to it
/// (https://drafts.csswg.org/css-gaps-1/#assigning).
/// </summary>
internal sealed class GapLine
{
    /// <summary>The position of the centerline: an inline offset for a column gap, a block offset for a row gap.</summary>
    public double Center { get; set; }

    /// <summary>The size of the gap across its centerline (the gutter, with the space added by the content distribution).</summary>
    public double Size { get; init; }

    /// <summary>The intersections, from the start to the end of the gap (at least two: its bounds).</summary>
    public required GapIntersection[] Intersections { get; init; }

    /// <summary>The index of the gap in the sequence of gaps the lists of values are assigned to.</summary>
    public int ValueIndex { get; set; }

    /// <summary>The number of gaps of the sequence (the auto repeaters fill the gaps without value).</summary>
    public int ValueCount { get; set; }

    /// <summary>
    /// Whether the gap runs along the lines of the container whose crossing gaps are not shared (the gaps between the flex lines,
    /// between the rows of columns, between the grid lanes): the crossing gaps abut it instead of crossing it.
    /// </summary>
    public bool IsMain { get; init; }

    /// <summary>
    /// The flow whose breaks give the block offsets of the gap in the fragments of a fragmented container (see
    /// <see cref="GapGeometry.Slice"/>): 0 for the flow of the container, else 1 + the index of the line of a multi-line column
    /// flex container, whose items follow each other in a parallel flow.
    /// </summary>
    public int FlowKey { get; init; }

    /// <summary>
    /// The item on the inline-end side of a gap between two items of a line of a row flex container, else null: as in Blink,
    /// the gap is in a fragment of a fragmented container when that item has a fragment there (not beside the rest of an item
    /// whose neighbor stayed in the previous fragmentainer).
    /// </summary>
    public ContainerBox? EndItem { get; init; }
}

/// <summary>The part of a flow of a fragmented container that is in a fragmentainer, for the gaps of its fragment (see <see cref="GapGeometry.Slice"/>).</summary>
/// <param name="Start">The block offset in the container where the part of the flow in the fragmentainer starts (-∞ for the first one).</param>
/// <param name="End">The block offset in the container where it ends.</param>
/// <param name="ToOffset">Returns the block offset in the fragment of the container of a block offset in the container.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GapFlowSlice(double Start, double End, Func<double, double> ToOffset);

/// <summary>
/// The gaps of a container (https://drafts.csswg.org/css-gaps-1/#gaps) computed by its layout, for the painting of the gap
/// decorations (https://drafts.csswg.org/css-gaps-1/#gap-decorations): the column gaps separate boxes in the inline axis of the
/// container (they run along its block axis), the row gaps separate boxes in its block axis. The coordinates are logical, from
/// the inline-start/block-start corner of the border box of the container.
/// </summary>
internal sealed class GapGeometry
{
    public required GapContainerKind Kind { get; init; }

    public required GapLine[] ColumnGaps { get; init; }

    public required GapLine[] RowGaps { get; init; }

    public bool IsEmpty => ColumnGaps.Length == 0 && RowGaps.Length == 0;

    /// <summary>
    /// Returns the gaps of the part of the container that is in a fragmentainer (https://drafts.csswg.org/css-gaps-1/#fragmentation:
    /// "gaps disappear where they coincide with a fragmentation break. Because there is no gap in such locations, no gap decoration
    /// is drawn in them either"), or null when it has none: the row gaps that are entirely in the fragmentainer and not adjacent to
    /// a break, and the parts of the column gaps in it. The gaps keep the values assigned to them in the whole container ("A gap
    /// that is suppressed due to fragmentation still consumes a gap decoration value from the pattern"). The gaps are placed
    /// from the fragmented positions of the content: each block offset is mapped by the breaks of its flow (the flow of the
    /// container, or the parallel flow of a line of a multi-line column flex container, see <see cref="GapLine.FlowKey"/>),
    /// so a gap follows the content after it when it is pushed by a break or shifted by a track that grows.
    /// </summary>
    /// <param name="main">The part of the flow of the container that is in the fragmentainer.</param>
    /// <param name="getFlow">Returns the part of a parallel flow of the container that is in the fragmentainer (see <see cref="GapLine.FlowKey"/>), or null when it has none.</param>
    /// <param name="contentEnd">The block offset in the fragment of its block-end content edge.</param>
    /// <param name="hasFragment">Returns whether an item of the container has a fragment in the fragmentainer (see <see cref="GapLine.EndItem"/>), or null.</param>
    public GapGeometry? Slice(in GapFlowSlice main, Func<int, GapFlowSlice?>? getFlow, double contentEnd, Func<ContainerBox, bool>? hasFragment = null)
    {
        const double Epsilon = 0.01;
        var rowIndexes = new int[RowGaps.Length];
        var rows = new List<GapLine>();
        for (var i = 0; i < RowGaps.Length; i++)
        {
            var gap = RowGaps[i];
            var gapStart = gap.Center - (gap.Size / 2);
            var gapEnd = gap.Center + (gap.Size / 2);
            rowIndexes[i] = -1;
            if ((gap.FlowKey == 0 ? main : getFlow?.Invoke(gap.FlowKey)) is not { } flow)
                continue;

            if (gapStart > flow.Start + Epsilon && gapEnd < flow.End - Epsilon)
            {
                // (the gap is placed before the content after it: the content before it may end earlier than in the container)
                rowIndexes[i] = rows.Count;
                rows.Add(new GapLine { Center = flow.ToOffset(gapEnd) - (gap.Size / 2), Size = gap.Size, Intersections = [.. gap.Intersections], ValueIndex = gap.ValueIndex, ValueCount = gap.ValueCount, IsMain = gap.IsMain, FlowKey = gap.FlowKey });
            }
        }

        var columnIndexes = new int[ColumnGaps.Length];
        var columns = new List<GapLine>();
        var intersections = new List<GapIntersection>();
        for (var i = 0; i < ColumnGaps.Length; i++)
        {
            var gap = ColumnGaps[i];
            var source = gap.Intersections;
            columnIndexes[i] = -1;
            if (Math.Min(source[^1].Offset, main.End) - Math.Max(source[0].Offset, main.Start) <= Epsilon)
                continue;

            if (gap.EndItem is { } endItem && hasFragment is not null && !hasFragment(endItem))
                continue;

            intersections.Clear();
            if (source[0].Offset >= main.Start - Epsilon)
            {
                var first = source[0];
                first.Offset = main.ToOffset(first.Offset);
                intersections.Add(first);
            }
            else
            {
                // The gap continues from the previous fragmentainer (after the row gap the break is in, if any)
                var segment = 0;
                for (var j = 1; j < source.Length - 1; j++)
                {
                    if (source[j].Offset - (source[j].CrossWidth / 2) - Epsilon > main.Start)
                        break;

                    segment = j;
                }

                intersections.Add(GapIntersection.Edge(main.ToOffset(main.Start), source[segment].State));
            }

            var isSorted = true;
            for (var j = 1; j < source.Length - 1; j++)
            {
                var junctionStart = source[j].Offset - (source[j].CrossWidth / 2);
                var junctionEnd = source[j].Offset + (source[j].CrossWidth / 2);
                if ((source[j].FlowKey == 0 ? main : getFlow?.Invoke(source[j].FlowKey)) is not { } flow)
                    continue;

                if (junctionStart > flow.Start + Epsilon && junctionEnd < flow.End - Epsilon)
                {
                    var intersection = source[j];
                    if (intersection.CrossGapIndex >= 0 && intersection.CrossGapIndex < rowIndexes.Length)
                    {
                        intersection.CrossGapIndex = rowIndexes[intersection.CrossGapIndex];
                    }

                    intersection.Offset = flow.ToOffset(junctionEnd) - (intersection.CrossWidth / 2);
                    isSorted &= intersection.Offset >= intersections[^1].Offset;
                    intersections.Add(intersection);
                }
            }

            if (!isSorted)
            {
                // (the junctions of two parallel flows, which are not shifted by the same breaks)
                intersections.Sort(1, intersections.Count - 1, Comparer<GapIntersection>.Create(static (a, b) => a.Offset.CompareTo(b.Offset)));
            }

            // A gap that continues in the next fragmentainer runs to the end of the fragment
            if (source[^1].Offset <= main.End + Epsilon)
            {
                var last = source[^1];
                last.Offset = Math.Max(main.ToOffset(last.Offset), intersections[^1].Offset);
                intersections.Add(last);
            }
            else
            {
                intersections.Add(GapIntersection.Edge(Math.Max(contentEnd, intersections[^1].Offset)));
            }

            if (intersections[^1].Offset - intersections[0].Offset <= Epsilon)
                continue;

            columnIndexes[i] = columns.Count;
            columns.Add(new GapLine { Center = gap.Center, Size = gap.Size, Intersections = [.. intersections], ValueIndex = gap.ValueIndex, ValueCount = gap.ValueCount, IsMain = gap.IsMain, FlowKey = gap.FlowKey, EndItem = gap.EndItem });
        }

        if (rows.Count == 0 && columns.Count == 0)
            return null;

        foreach (var row in rows)
        {
            var rowIntersections = row.Intersections;
            for (var j = 0; j < rowIntersections.Length; j++)
            {
                if (rowIntersections[j].CrossGapIndex >= 0 && rowIntersections[j].CrossGapIndex < columnIndexes.Length)
                {
                    rowIntersections[j].CrossGapIndex = columnIndexes[rowIntersections[j].CrossGapIndex];
                }
            }
        }

        return new GapGeometry { Kind = Kind, ColumnGaps = [.. columns], RowGaps = [.. rows] };
    }

    /// <summary>Moves the gaps in the block axis (the contents of a multi-column container aligned by <c>align-content</c>).</summary>
    public void MoveBlock(double offset)
    {
        foreach (var gap in RowGaps)
        {
            gap.Center += offset;
        }

        foreach (var gap in ColumnGaps)
        {
            var intersections = gap.Intersections;
            for (var i = 0; i < intersections.Length; i++)
            {
                intersections[i].Offset += offset;
            }
        }
    }
}
