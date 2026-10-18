using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A gap decoration (https://drafts.csswg.org/css-gaps-1/#gap-decoration): a line of a style and a color filling a rectangle, in the coordinates of the fragment of its container.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GapDecoration(PhysicalRect Rect, bool IsVertical, BorderStyle Style, DisplayColor Color);

/// <summary>
/// Computes the gap decorations of a container (https://drafts.csswg.org/css-gaps-1/#gap-decorations) from the geometry of its
/// gaps (<see cref="BoxFragment.GapGeometry"/>) and its gap decoration properties: the values assigned to each gap, the gap
/// decoration segments of the gap (https://drafts.csswg.org/css-gaps-1/#determine-gap-decoration-segments) with the break
/// behavior and the visibility of the rules, and their insets.
/// </summary>
internal static class GapDecorations
{
    /// <summary>
    /// Returns the gap decorations of a fragment in painting order (https://drafts.csswg.org/css-gaps-1/#overlap: the row
    /// decorations are above the column decorations, unless <c>rule-overlap</c> is <c>column-over-row</c>), or null when it has none.
    /// </summary>
    public static List<GapDecoration>? GetDecorations(BoxFragment fragment)
    {
        if (fragment.GapGeometry is not { } geometry)
            return null;

        var computedStyle = fragment.Box.Style;
        var style = computedStyle.Layout.GapDecorations;
        if (!style.HasRules)
            return null;

        var result = new List<GapDecoration>();
        if (style.ColumnOverRow)
        {
            AddDecorations(result, fragment, geometry, style, isColumn: false);
            AddDecorations(result, fragment, geometry, style, isColumn: true);
        }
        else
        {
            AddDecorations(result, fragment, geometry, style, isColumn: true);
            AddDecorations(result, fragment, geometry, style, isColumn: false);
        }

        return result;
    }

    private static void AddDecorations(List<GapDecoration> result, BoxFragment fragment, GapGeometry geometry, GapDecorationStyle style, bool isColumn)
    {
        var rule = isColumn ? style.Column : style.Row;
        var gaps = isColumn ? geometry.ColumnGaps : geometry.RowGaps;
        if (!rule.IsVisible || gaps.Length == 0)
            return;

        var crossRule = isColumn ? style.Row : style.Column;
        var crossGaps = isColumn ? geometry.RowGaps : geometry.ColumnGaps;
        var kind = geometry.Kind;
        var ruleBreak = ResolveBreak(rule.Break, kind, isColumn);
        var visibility = ResolveVisibility(rule.VisibilityItems, kind);
        var crossVisibility = ResolveVisibility(crossRule.VisibilityItems, kind);
        var colors = GapValueList<DisplayColor>.Parse(
            fragment.Box.Style.GetUsedValue(isColumn ? "column-rule-color" : "row-rule-color"),
            static value => DisplayColor.Parse(value) ?? DisplayColor.Transparent,
            DisplayColor.Transparent);
        var writingDirection = fragment.WritingDirection;
        var isVertical = isColumn == writingDirection.IsHorizontal;

        foreach (var gap in gaps)
        {
            var count = Math.Max(1, gap.ValueCount);
            var index = Math.Clamp(gap.ValueIndex, 0, count - 1);
            var width = rule.Widths.Get(index, count);

            // https://drafts.csswg.org/css-multicol-1/#crs: inset is painted as ridge and outset as groove (a rule is a line, not a box)
            var lineStyle = rule.Styles.Get(index, count) switch
            {
                BorderStyle.Inset => BorderStyle.Ridge,
                BorderStyle.Outset => BorderStyle.Groove,
                var other => other,
            };
            var color = colors.Get(index, count);
            if (lineStyle is BorderStyle.None or BorderStyle.Hidden || width <= 0 || color.IsTransparent)
                continue;

            var intersections = gap.Intersections;
            var last = intersections.Length - 1;
            var start = 0;
            while (start < last)
            {
                // https://drafts.csswg.org/css-gaps-1/#determine-gap-decoration-segments: the first pair of endpoints that is not
                // discontiguous (nor hidden by rule-visibility-items), extended while the next pair can be joined
                while (start < last && SkipsStart(intersections, start, kind, ruleBreak, visibility))
                {
                    start++;
                }

                if (start >= last)
                    break;

                var end = start + 1;
                while (end < last && ContinuesThrough(intersections, end, kind, ruleBreak, visibility))
                {
                    end++;
                }

                var startOffset = intersections[start].Offset + (intersections[start].CrossWidth / 2) + GetInset(intersections[start], isStart: true);
                var endOffset = intersections[end].Offset - (intersections[end].CrossWidth / 2) - GetInset(intersections[end], isStart: false);
                if (endOffset > startOffset)
                {
                    var crossStart = gap.Center - (width / 2);
                    var logical = isColumn
                        ? new LogicalRect(new LogicalOffset(crossStart, startOffset), new LogicalSize(width, endOffset - startOffset))
                        : new LogicalRect(new LogicalOffset(startOffset, crossStart), new LogicalSize(endOffset - startOffset, width));
                    result.Add(new GapDecoration(writingDirection.ToPhysical(logical, fragment.Size), isVertical, lineStyle, color));
                }

                start = end;
            }

            // https://drafts.csswg.org/css-gaps-1/#inset: the offset of a segment endpoint, towards the inside of the segment
            double GetInset(in GapIntersection intersection, bool isStart)
            {
                var isCap = IsCap(intersection, kind, visibility, crossVisibility);
                var inset = isCap
                    ? (isStart ? rule.InsetCapStart : rule.InsetCapEnd)
                    : (isStart ? rule.InsetJunctionStart : rule.InsetJunctionEnd);
                if (!inset.IsOverlapJoin)
                    return inset.Length.Resolve(intersection.InsetBasis);

                // "At a cap segment endpoint, overlap-join is treated as 0"
                if (isCap)
                    return 0;

                // "At a junction segment endpoint, the gap decoration extends into the gap junction by half the crossing gap
                // width, plus half the used value of the crossing gap's column-rule-width or row-rule-width": the gaps that
                // abut the gaps between the lines of a flex container or the rows of a multi-column container end at their center, as
                // in Blink
                var extent = intersection.InsetBasis / 2;
                if (gap.IsMain && kind is GapContainerKind.Flex or GapContainerKind.MultiColumn)
                    return -extent;

                if (intersection.CrossGapIndex >= 0 && intersection.CrossGapIndex < crossGaps.Length)
                {
                    var crossGap = crossGaps[intersection.CrossGapIndex];
                    var crossCount = Math.Max(1, crossGap.ValueCount);
                    extent += crossRule.Widths.Get(Math.Clamp(crossGap.ValueIndex, 0, crossCount - 1), crossCount) / 2;
                }

                return -extent;
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-gaps-1/#break: the used break behavior (<c>normal</c> depends on the container).</summary>
    private static RuleBreak ResolveBreak(RuleBreak value, GapContainerKind kind, bool isColumn)
    {
        if (value != RuleBreak.Normal)
            return value;

        return kind switch
        {
            // "Flex containers: Behaves the same as none"
            GapContainerKind.Flex => RuleBreak.None,

            // "Multicol containers: On column-rule-break, behaves the same as intersection. On row-rule-break, behaves the same as none"
            GapContainerKind.MultiColumn => isColumn ? RuleBreak.Intersection : RuleBreak.None,
            _ => RuleBreak.Normal,
        };
    }

    /// <summary>https://drafts.csswg.org/css-gaps-1/#visibility: the used visibility (<c>normal</c> depends on the container).</summary>
    private static RuleVisibilityItems ResolveVisibility(RuleVisibilityItems value, GapContainerKind kind)
    {
        if (value != RuleVisibilityItems.Normal)
            return value;

        return kind is GapContainerKind.Grid or GapContainerKind.GridLanes ? RuleVisibilityItems.All : RuleVisibilityItems.Between;
    }

    private static bool IsVisible(GapSegmentState state, RuleVisibilityItems visibility)
    {
        const GapSegmentState Empty = GapSegmentState.EmptyBefore | GapSegmentState.EmptyAfter;
        return visibility switch
        {
            // "Paint decorations in a gap segment if at least one of the two adjacent areas is occupied by an item"
            RuleVisibilityItems.Around => state != Empty,

            // "Paint decorations in a gap segment if both adjacent areas are occupied by items"
            RuleVisibilityItems.Between => (state & Empty) == GapSegmentState.None,
            _ => true,
        };
    }

    private static bool IsBlocked(GapSegmentState state) => (state & GapSegmentState.Blocked) != GapSegmentState.None;

    /// <summary>Returns whether a segment cannot start at an intersection: the portion of the gap after it is not painted.</summary>
    private static bool SkipsStart(GapIntersection[] intersections, int index, GapContainerKind kind, RuleBreak ruleBreak, RuleVisibilityItems visibility)
    {
        ref readonly var intersection = ref intersections[index];
        var visible = IsVisible(intersection.State, visibility);

        // A segment does not start inside the junction of overlapping crossing gaps: it starts after the junction
        if (kind == GapContainerKind.Flex && intersection.Window != GapOverlapWindow.None)
            return intersection.Window == GapOverlapWindow.Open;

        // "If break is none": a single decoration from one end of the gap to the other, behind the items
        if (ruleBreak == RuleBreak.None)
            return !visible;

        if (kind == GapContainerKind.GridLanes && intersection.Window == GapOverlapWindow.Open)
            return true;

        return IsBlocked(intersection.State) || !visible;
    }

    /// <summary>Returns whether a segment that reaches an intersection continues through it (the union with the next pair of endpoints).</summary>
    private static bool ContinuesThrough(GapIntersection[] intersections, int index, GapContainerKind kind, RuleBreak ruleBreak, RuleVisibilityItems visibility)
    {
        ref readonly var intersection = ref intersections[index];
        if (!IsVisible(intersection.State, visibility))
            return false;

        if (ruleBreak == RuleBreak.None)
            return true;

        var blocked = IsBlocked(intersection.State);
        if (ruleBreak == RuleBreak.Normal)
        {
            // "If candidate's endpoints are discontiguous, exit this loop": the decorations break at the "T" intersections only
            if (blocked)
                return false;

            // The junction of overlapping crossing gaps of a grid lanes container: no item spans across it
            if (kind == GapContainerKind.GridLanes && intersection.Window == GapOverlapWindow.Open && index + 1 < intersections.Length)
                return !IsBlocked(intersections[index + 1].State);

            return true;
        }

        // "If break is intersection and candidate is not flanked by spanning items, exit this loop"
        if (kind == GapContainerKind.Flex)
            return intersection.Window == GapOverlapWindow.Close;

        if (blocked || kind != GapContainerKind.Grid)
            return false;

        // "there exist items on opposing sides of the gap that span across all perpendicular gaps contributing to that junction"
        return intersection.HasCrossStates && IsBlocked(intersection.CrossBefore) && IsBlocked(intersection.CrossAfter);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-gaps-1/#cap-segment-endpoint: "any segment endpoint that is not a junction segment endpoint.
    /// This includes endpoints at the container's content edges, as well as endpoints at a gap junction where no other gap
    /// decoration segment is present" (as in Blink, the decorations of the crossing gap are only looked up when the rules are
    /// painted between the items).
    /// </summary>
    private static bool IsCap(in GapIntersection intersection, GapContainerKind kind, RuleVisibilityItems visibility, RuleVisibilityItems crossVisibility)
    {
        if (intersection.IsEdge)
            return true;

        if (kind is not (GapContainerKind.Grid or GapContainerKind.MultiColumn) || visibility != RuleVisibilityItems.Between || !intersection.HasCrossStates)
            return false;

        var before = IsVisible(intersection.CrossBefore, crossVisibility) && !IsBlocked(intersection.CrossBefore);
        var after = IsVisible(intersection.CrossAfter, crossVisibility) && !IsBlocked(intersection.CrossAfter);
        return !before && !after;
    }
}
