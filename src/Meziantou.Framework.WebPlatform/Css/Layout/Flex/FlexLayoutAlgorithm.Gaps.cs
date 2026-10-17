using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The geometry of the gaps of a flex container, for the painting of the gap decorations (https://drafts.csswg.org/css-gaps-1/)
internal sealed partial class FlexLayoutAlgorithm
{
    /// <summary>The gap between two consecutive items of a flex line: its centerline and its size in the main axis (logical coordinates of the content box), and its index in the placement order of the container.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ItemGap(double Center, double Size, int ValueIndex, int FlowKey, ContainerBox EndItem);

    /// <summary>
    /// Returns the gaps of the flex container (https://drafts.csswg.org/css-gaps-1/#gap-flex), or null when it paints no gap
    /// decoration: the gutters between the items of each line (with the space distributed by <c>justify-content</c>: "additional
    /// spacing may be added by justify-content/align-content. Such additional space increases the size of the corresponding gaps"),
    /// which run across their line, and the gutters between the lines, which run across the container and are abutted by the gaps
    /// of the two lines ("unlike in grid, gaps in flex do not overlap").
    /// </summary>
    private GapGeometry? BuildGapGeometry()
    {
        if (!_box.Style.Layout.HasGapRules || _lines.Count == 0)
            return null;

        // The lines and the gaps between their items in the order of the logical coordinates (the reversed directions place the
        // lines or the items from the end side); the values are assigned in the placement order
        // (https://drafts.csswg.org/css-gaps-1/#assigning: "values are assigned starting at the side where placement begins and
        // proceeding in the placement direction", "the decoration pattern is assigned to gaps sequentially across the flexbox and
        // does not restart at the beginning of each flex line")
        var lineCount = _lines.Count;
        var lines = new (double Start, double End, ItemGap[] Gaps)[lineCount];
        var itemGapCount = 0;
        for (var i = 0; i < lineCount; i++)
        {
            var line = _lines[i];
            var gaps = new List<ItemGap>();
            FlexItem? previous = null;
            for (var j = line.Start; j < line.End; j++)
            {
                var item = _items[j];
                if (item.IsCollapsed && line.IsCollapsedPass)
                    continue;

                if (previous is not null)
                {
                    var start = previous.MainPosition + previous.UsedMarginMainStart + previous.TargetMainSize + previous.UsedMarginMainEnd;
                    var end = item.MainPosition;
                    var center = (start + end) / 2;
                    // (the items of a line of a multi-line column container are a parallel flow of its fragmentation)
                    gaps.Add(new ItemGap(_properties.IsMainReverse ? _mainSize - center : center, Math.Max(0, end - start), itemGapCount++, !_isRow && lineCount > 1 ? i + 1 : 0, _properties.IsMainReverse ? previous.Box : item.Box));
                }

                previous = item;
            }

            if (_properties.IsMainReverse)
            {
                gaps.Reverse();
            }

            var lineStart = _properties.IsCrossReverse ? _crossSize - line.CrossOffset - line.CrossSize : line.CrossOffset;
            lines[_properties.IsCrossReverse ? lineCount - 1 - i : i] = (lineStart, lineStart + line.CrossSize, [.. gaps]);
        }

        if (itemGapCount == 0 && lineCount < 2)
            return null;

        var mainOffset = _isRow ? _content.Offset.Inline : _content.Offset.Block;
        var crossOffset = _isRow ? _content.Offset.Block : _content.Offset.Inline;

        // The gaps between the lines, in the placement order of the lines for the values
        var lineGaps = new GapLine[lineCount - 1];
        for (var i = 0; i < lineGaps.Length; i++)
        {
            lineGaps[i] = new GapLine
            {
                Center = crossOffset + ((lines[i].End + lines[i + 1].Start) / 2),
                Size = Math.Max(0, lines[i + 1].Start - lines[i].End),
                Intersections = CreateLineGapIntersections(lines[i].Gaps, lines[i + 1].Gaps, mainOffset),
                ValueIndex = _properties.IsCrossReverse ? lineGaps.Length - 1 - i : i,
                ValueCount = lineGaps.Length,
                IsMain = true,
            };
        }

        // The gaps between the items: from the gap before their line (or the start of the first line) to the gap after it (or
        // the end of the last line)
        var itemGaps = new List<GapLine>(itemGapCount);
        for (var i = 0; i < lineCount; i++)
        {
            foreach (var gap in lines[i].Gaps)
            {
                var start = i == 0
                    ? GapIntersection.Edge(crossOffset + lines[i].Start)
                    : GapIntersection.Junction(lineGaps[i - 1].Center, Math.Max(0, lines[i].Start - lines[i - 1].End), i - 1);
                var end = i == lineCount - 1
                    ? GapIntersection.Edge(crossOffset + lines[i].End)
                    : GapIntersection.Junction(lineGaps[i].Center, Math.Max(0, lines[i + 1].Start - lines[i].End), i);
                itemGaps.Add(new GapLine
                {
                    Center = mainOffset + gap.Center,
                    Size = gap.Size,
                    Intersections = [start, end],
                    ValueIndex = gap.ValueIndex,
                    ValueCount = itemGapCount,
                    FlowKey = gap.FlowKey,
                    EndItem = _isRow ? gap.EndItem : null,
                });
            }
        }

        return new GapGeometry
        {
            Kind = GapContainerKind.Flex,
            ColumnGaps = _isRow ? [.. itemGaps] : lineGaps,
            RowGaps = _isRow ? lineGaps : [.. itemGaps],
        };
    }

    /// <summary>
    /// Records the gaps between the lines of a multi-line row container (in <see cref="LayoutBox.LayoutState"/>) for the
    /// fragmentation: "the gap disappears when it coincides with a fragmentation break" (https://drafts.csswg.org/css-gaps-1/#gaps).
    /// </summary>
    private void RecordLineGaps()
    {
        if (_items.Count == 0)
        {
            if (_box.LayoutState is FlexContainerState)
            {
                _box.LayoutState = null;
            }

            return;
        }

        // The lines in the block axis of a row container (stacked from the block-end edge with wrap-reverse)
        var lines = new (double Start, double End)[_isRow ? _lines.Count : 0];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = _lines[i];
            var start = _properties.IsCrossReverse ? _crossSize - line.CrossOffset - line.CrossSize : line.CrossOffset;
            lines[i] = (_content.Offset.Block + start, _content.Offset.Block + start + line.CrossSize);
        }

        var gaps = new List<(double Start, double End)>();
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            var (before, after) = _properties.IsCrossReverse ? (lines[i + 1], lines[i]) : (lines[i], lines[i + 1]);
            if (after.Start > before.End)
            {
                gaps.Add((before.End, after.Start));
            }
        }

        var items = new Dictionary<ContainerBox, FlexItemState>(_items.Count);
        foreach (var item in _items)
        {
            items[item.Box] = new FlexItemState(_lines.IndexOf(item.Line!), item.Stretches, item.HasDefiniteFlexBasis);
        }

        _box.LayoutState = new FlexContainerState(gaps) { IsRow = _isRow, IsSingleLine = !_properties.IsMultiLine, LineCount = _lines.Count, Lines = lines, Items = items };
    }

    /// <summary>
    /// Returns the intersections of the gap between two lines: the edges of the content box, and the gaps between the items of
    /// the two lines, which abut it. The gaps of the two lines that overlap one another (their intervals intersect) are one gap
    /// junction (https://drafts.csswg.org/css-gaps-1/#crossing-gap-width: "in cases where gaps intersect or abut one another at
    /// overlapping intervals, the crossing gap width will be determined by the union of such intervals"): the first and the last
    /// gaps of the junction are kept, as the opening and the closing of an overlap window.
    /// </summary>
    private GapIntersection[] CreateLineGapIntersections(ItemGap[] before, ItemGap[] after, double mainOffset)
    {
        var result = new List<GapIntersection>(before.Length + after.Length + 2);
        var first = Math.Min(before.Length > 0 ? before[0].Center : double.PositiveInfinity, after.Length > 0 ? after[0].Center : double.PositiveInfinity);
        var last = Math.Max(before.Length > 0 ? before[^1].Center : double.NegativeInfinity, after.Length > 0 ? after[^1].Center : double.NegativeInfinity);
        result.Add(GapIntersection.Edge(mainOffset + Math.Min(0, first)));

        var beforeIndex = 0;
        var afterIndex = 0;
        while (beforeIndex < before.Length || afterIndex < after.Length)
        {
            ItemGap gap;
            if (afterIndex >= after.Length || (beforeIndex < before.Length && before[beforeIndex].Center <= after[afterIndex].Center))
            {
                gap = before[beforeIndex++];

                // The aligned gaps of the two lines are one intersection
                if (afterIndex < after.Length && after[afterIndex].Center == gap.Center)
                {
                    afterIndex++;
                }
            }
            else
            {
                gap = after[afterIndex++];
            }

            var offset = mainOffset + gap.Center;
            var previous = result[^1];
            if (result.Count > 1 && offset - previous.Offset < (previous.CrossWidth + gap.Size) / 2)
            {
                if (previous.Window == GapOverlapWindow.Open)
                {
                    result.Add(new GapIntersection { Offset = offset, CrossWidth = gap.Size, CrossGapIndex = -1, Window = GapOverlapWindow.Close, FlowKey = gap.FlowKey });
                }
                else
                {
                    // The window continues: its closing moves to this gap
                    previous.Offset = offset;
                    previous.CrossWidth = gap.Size;
                    previous.FlowKey = gap.FlowKey;
                    result[^1] = previous;
                }
            }
            else
            {
                // The previous gap did not open a window; this one may
                if (previous.Window == GapOverlapWindow.Open)
                {
                    previous.Window = GapOverlapWindow.None;
                    result[^1] = previous;
                }

                result.Add(new GapIntersection { Offset = offset, CrossWidth = gap.Size, CrossGapIndex = -1, Window = GapOverlapWindow.Open, FlowKey = gap.FlowKey });
            }
        }

        if (result[^1].Window == GapOverlapWindow.Open)
        {
            var previous = result[^1];
            previous.Window = GapOverlapWindow.None;
            result[^1] = previous;
        }

        // The percentages of the insets resolve against the size of the junction
        for (var i = 1; i < result.Count; i++)
        {
            var intersection = result[i];
            if (intersection.Window == GapOverlapWindow.Open && i + 1 < result.Count)
            {
                var closing = result[i + 1];
                var size = closing.Offset + (closing.CrossWidth / 2) - (intersection.Offset - (intersection.CrossWidth / 2));
                intersection.InsetBasis = size;
                closing.InsetBasis = size;
                result[i] = intersection;
                result[i + 1] = closing;
                i++;
            }
            else
            {
                intersection.InsetBasis = intersection.CrossWidth;
                result[i] = intersection;
            }
        }

        result.Add(GapIntersection.Edge(mainOffset + Math.Max(_mainSize, last)));
        return [.. result];
    }
}

/// <summary>
/// The state of the last layout of a flex container (in <see cref="LayoutBox.LayoutState"/>), for its fragmentation
/// (https://drafts.csswg.org/css-flexbox-1/#pagination): its lines and the line of each item, and the gaps between the lines
/// of a row container (their block-start and block-end edges, from the block-start edge of the border box of the container),
/// which disappear at the fragmentation breaks.
/// </summary>
internal sealed class FlexContainerState(List<(double Start, double End)> lineGaps)
{
    public List<(double Start, double End)> LineGaps { get; } = lineGaps;

    /// <summary>Whether the main axis of the container is its inline axis.</summary>
    public bool IsRow { get; init; }

    /// <summary>Whether the container is single-line (<c>flex-wrap: nowrap</c>).</summary>
    public bool IsSingleLine { get; init; }

    public int LineCount { get; init; }

    /// <summary>The block-start and block-end edges of the lines of a row container, from the block-start edge of the border box of the container.</summary>
    public (double Start, double End)[] Lines { get; init; } = [];

    /// <summary>The line of each item, and whether it is stretched in the cross axis.</summary>
    public Dictionary<ContainerBox, FlexItemState> Items { get; init; } = [];
}

/// <summary>The line of a flex item, whether it is stretched in the cross axis, and whether its flex basis is definite (see <see cref="FlexContainerState"/>).</summary>
internal readonly record struct FlexItemState(int Line, bool Stretches, bool HasDefiniteFlexBasis);
