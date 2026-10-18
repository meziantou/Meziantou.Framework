using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The placement of a grid item in one axis (https://drafts.csswg.org/css-grid-2/#grid-placement): a definite position (its start
/// and end lines, numbered from the first explicit line, 0) or an automatic position with a span.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GridAxisPlacement(bool IsDefinite, int Start, int End)
{
    /// <summary>The number of tracks spanned.</summary>
    public int Span => End - Start;

    /// <summary>Returns an automatic position with the given span.</summary>
    public static GridAxisPlacement Auto(int span) => new(IsDefinite: false, 0, span);

    /// <summary>Returns a definite position at the given lines.</summary>
    public static GridAxisPlacement Definite(int start, int end) => new(IsDefinite: true, start, end);
}

/// <summary>
/// The grid placement algorithms (https://drafts.csswg.org/css-grid-2/#placement): the resolution of the grid-placement
/// properties against the lines of the explicit grid (https://drafts.csswg.org/css-grid-2/#line-placement), with the conflict
/// handling (https://drafts.csswg.org/css-grid-2/#grid-placement-errors), and the grid item placement algorithm
/// (https://drafts.csswg.org/css-grid-2/#auto-placement-algo).
/// </summary>
internal static class GridPlacement
{
    /// <summary>
    /// Resolves the grid-placement properties of an item in one axis (https://drafts.csswg.org/css-grid-2/#line-placement), and
    /// applies the conflict handling: "If the placement for a grid item contains two lines, and the start line is further
    /// end-ward than the end line, swap the two lines. If the start line is equal to the end line, remove the end line. If the
    /// placement contains two spans, remove the one contributed by the end grid-placement property. If the placement contains
    /// only a span for a named line, replace it with a span of 1."
    /// </summary>
    public static GridAxisPlacement Resolve(GridLine start, GridLine end, GridExplicitAxis axis)
    {
        var startIsLine = !start.IsAuto && !start.IsSpan;
        var endIsLine = !end.IsAuto && !end.IsSpan;
        if (!startIsLine && !endIsLine)
        {
            // Automatic position: the span of the start property, else of the end property ("If the placement contains two spans,
            // remove the one contributed by the end grid-placement property"); a named span is a span of 1
            var span = start.IsSpan ? start : end.IsSpan ? end : GridLine.Auto;
            return GridAxisPlacement.Auto(span.IsSpan && span.Name is null ? Math.Clamp(span.Integer, 1, GridExplicitAxis.MaxLine) : 1);
        }

        if (startIsLine && endIsLine)
        {
            var startLine = ResolveLine(start, axis, isStart: true);
            var endLine = ResolveLine(end, axis, isStart: false);
            if (startLine > endLine)
            {
                (startLine, endLine) = (endLine, startLine);
            }
            else if (startLine == endLine)
            {
                endLine = startLine + 1;
            }

            return Clamp(startLine, endLine);
        }

        if (startIsLine)
        {
            var startLine = ResolveLine(start, axis, isStart: true);
            var endLine = end.IsSpan ? ResolveSpan(end, axis, startLine, forward: true) : startLine + 1;
            return Clamp(startLine, endLine);
        }
        else
        {
            var endLine = ResolveLine(end, axis, isStart: false);
            var startLine = start.IsSpan ? ResolveSpan(start, axis, endLine, forward: false) : endLine - 1;
            return Clamp(startLine, endLine);
        }
    }

    /// <summary>
    /// Resolves the grid-placement properties of an absolutely positioned box whose containing block is a grid container
    /// (https://drafts.csswg.org/css-grid-2/#abspos-items): "Instead of auto-placement, an auto value for a grid-placement
    /// property contributes a special line to the placement whose position is that of the corresponding padding edge of the grid
    /// container [...] If the placement only contains a grid span, replace it with the two auto lines in that axis." Returns the
    /// lines (numbered from the first explicit line), null for the auto lines.
    /// </summary>
    public static (int? Start, int? End) ResolveAbsolute(GridLine start, GridLine end, GridExplicitAxis axis)
    {
        var startIsLine = !start.IsAuto && !start.IsSpan;
        var endIsLine = !end.IsAuto && !end.IsSpan;
        if (startIsLine && endIsLine)
        {
            var startLine = ResolveLine(start, axis, isStart: true);
            var endLine = ResolveLine(end, axis, isStart: false);
            if (startLine > endLine)
            {
                (startLine, endLine) = (endLine, startLine);
            }
            else if (startLine == endLine)
            {
                endLine = startLine + 1;
            }

            return (startLine, endLine);
        }

        if (startIsLine)
        {
            var startLine = ResolveLine(start, axis, isStart: true);
            return (startLine, end.IsSpan ? ResolveSpan(end, axis, startLine, forward: true) : null);
        }

        if (endIsLine)
        {
            var endLine = ResolveLine(end, axis, isStart: false);
            return (start.IsSpan ? ResolveSpan(start, axis, endLine, forward: false) : null, endLine);
        }

        return (null, null);
    }

    /// <summary>
    /// "If the grid area would span outside the limited grid, its span is clamped to the last line of the limited grid. If the grid
    /// area would be placed completely outside the limited grid, its span must be truncated to 1 and the area repositioned into
    /// the last grid track on that side of the grid." (https://drafts.csswg.org/css-grid-2/#overlarge-grids)
    /// </summary>
    private static GridAxisPlacement Clamp(int start, int end)
    {
        const int Max = GridExplicitAxis.MaxLine;
        if (start >= Max)
            return GridAxisPlacement.Definite(Max - 1, Max);

        if (end <= -Max)
            return GridAxisPlacement.Definite(-Max, -Max + 1);

        return GridAxisPlacement.Definite(Math.Max(start, -Max), Math.Min(end, Max));
    }

    /// <summary>
    /// Resolves a definite grid line (https://drafts.csswg.org/css-grid-2/#grid-placement-int): a line number, a named line
    /// (<c>&lt;custom-ident&gt;</c>, the <c>-start</c>/<c>-end</c> line of a named area first), or the Nth line with a name, counted
    /// from the start or the end of the explicit grid; the implicit lines are assumed to have all the names.
    /// </summary>
    private static int ResolveLine(GridLine line, GridExplicitAxis axis, bool isStart)
    {
        var count = axis.TrackCount;
        if (line.Name is not { } name)
        {
            // "Contributes the Nth grid line to the grid item's placement. If a negative integer is given, it instead counts in
            // reverse, starting from the end edge of the explicit grid."
            return line.Integer > 0 ? line.Integer - 1 : count + 1 + line.Integer;
        }

        if (line.Integer == 0)
        {
            // "First attempt to match the grid area's edge to a named grid area: if there is a grid line whose line name is
            // <custom-ident>-start (for grid-*-start) / <custom-ident>-end (for grid-*-end), contributes the first such line"
            if (axis.GetLines(name + (isStart ? "-start" : "-end")) is { Count: > 0 } areaLines)
                return areaLines[0];

            // "Otherwise, treat this as if the integer 1 had been specified along with the <custom-ident>"
            return ResolveNthNamedLine(axis, name, 1);
        }

        return ResolveNthNamedLine(axis, name, line.Integer);
    }

    /// <summary>
    /// Returns the Nth line with a name (from the end when N is negative): "If not enough lines with that name exist, all implicit
    /// grid lines are assumed to have that name for the purpose of finding this position."
    /// </summary>
    private static int ResolveNthNamedLine(GridExplicitAxis axis, string name, int n)
    {
        var lines = axis.GetLines(name);
        var found = lines?.Count ?? 0;
        if (n > 0)
            return n <= found ? lines![n - 1] : axis.TrackCount + (n - found);

        var fromEnd = -n;
        return fromEnd <= found ? lines![found - fromEnd] : -(fromEnd - found);
    }

    /// <summary>
    /// Resolves a span from a line (https://drafts.csswg.org/css-grid-2/#grid-placement-span-int): the line N lines away in the
    /// search direction, counting only the lines with the name when it is given ("If not enough lines with that name exist, all
    /// implicit grid lines on the side of the explicit grid corresponding to the search direction are assumed to have that name").
    /// </summary>
    private static int ResolveSpan(GridLine span, GridExplicitAxis axis, int from, bool forward)
    {
        var n = Math.Max(1, span.Integer);
        if (span.Name is not { } name)
            return forward ? from + n : from - n;

        var lines = axis.GetLines(name);
        if (forward)
        {
            if (lines is not null)
            {
                foreach (var line in lines)
                {
                    if (line > from && --n == 0)
                        return line;
                }
            }

            return Math.Max(from, axis.TrackCount) + n;
        }

        if (lines is not null)
        {
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i] < from && --n == 0)
                    return lines[i];
            }
        }

        return Math.Min(from, 0) - n;
    }

    /// <summary>
    /// The grid item placement algorithm (https://drafts.csswg.org/css-grid-2/#auto-placement-algo): resolves the automatic
    /// positions of the items into definite positions, creating the implicit tracks. The items are given in order-modified
    /// document order; their placements are resolved against the explicit grid (lines numbered from 0, the first explicit line).
    /// </summary>
    /// <param name="columns">The placement of each item in the columns (updated with the definite positions).</param>
    /// <param name="rows">The placement of each item in the rows (updated with the definite positions).</param>
    /// <param name="explicitColumns">The number of explicit columns.</param>
    /// <param name="explicitRows">The number of explicit rows.</param>
    /// <param name="flow">The grid-auto-flow of the container.</param>
    /// <param name="columnLimit">The number of columns when the grid cannot have implicit columns (a subgridded axis), else null.</param>
    /// <param name="rowLimit">The number of rows when the grid cannot have implicit rows (a subgridded axis), else null.</param>
    public static void PlaceItems(GridAxisPlacement[] columns, GridAxisPlacement[] rows, int explicitColumns, int explicitRows, GridAutoFlow flow, int? columnLimit = null, int? rowLimit = null)
    {
        // The algorithm is written for grid-auto-flow: row; with column, the axes are swapped: the "major" axis is the axis in
        // which new tracks are created (the rows for row), the "minor" axis is the other one
        var major = flow.IsColumn ? columns : rows;
        var minor = flow.IsColumn ? rows : columns;
        var explicitMinor = flow.IsColumn ? explicitRows : explicitColumns;
        var count = major.Length;

        // The grid cells are indexed from the start-most line of the implicit grid: the definite positions may be negative
        var minorOffset = 0;
        var majorOffset = 0;
        for (var i = 0; i < count; i++)
        {
            if (minor[i].IsDefinite)
            {
                minorOffset = Math.Max(minorOffset, -minor[i].Start);
            }

            if (major[i].IsDefinite)
            {
                majorOffset = Math.Max(majorOffset, -major[i].Start);
            }
        }

        var occupancy = new GridOccupancy(minorOffset, majorOffset);

        // 1. "Position anything that's not auto-positioned."
        for (var i = 0; i < count; i++)
        {
            if (major[i].IsDefinite && minor[i].IsDefinite)
            {
                occupancy.Occupy(minor[i].Start, minor[i].End, major[i].Start, major[i].End);
            }
        }

        // 2. "Process the items locked to a given row": "sparse" packing sets the column-start line "past any grid items
        // previously placed in this row by this step"; "dense" packing to the earliest line
        Dictionary<int, int>? rowCursors = null;
        for (var i = 0; i < count; i++)
        {
            if (!major[i].IsDefinite || minor[i].IsDefinite)
                continue;

            var span = minor[i].Span;
            var start = -minorOffset;
            if (!flow.IsDense)
            {
                rowCursors ??= [];
                if (rowCursors.TryGetValue(major[i].Start, out var cursor))
                {
                    start = cursor;
                }
            }

            while (occupancy.IsOccupied(start, start + span, major[i].Start, major[i].End))
            {
                start++;
            }

            minor[i] = GridAxisPlacement.Definite(start, start + span);
            occupancy.Occupy(start, start + span, major[i].Start, major[i].End);
            if (rowCursors is not null)
            {
                rowCursors[major[i].Start] = start + span;
            }
        }

        // 3. "Determine the columns in the implicit grid": the explicit columns, extended to the items with a definite column
        // position, and to the largest span of the items without one
        var minorEnd = explicitMinor;
        for (var i = 0; i < count; i++)
        {
            if (minor[i].IsDefinite)
            {
                minorEnd = Math.Max(minorEnd, minor[i].End);
            }
            else
            {
                minorEnd = Math.Max(minorEnd, minor[i].Span - minorOffset);
            }
        }

        // A subgridded axis has no implicit tracks: the auto-placed items are clamped to its explicit grid (see below)
        var minorLimit = flow.IsColumn ? rowLimit : columnLimit;
        if (minorLimit is { } limit)
        {
            minorEnd = Math.Max(1, limit);
        }

        // 4. "Position the remaining grid items" with the auto-placement cursor, from the start-most row and column lines
        var cursorMinor = -minorOffset;
        var cursorMajor = -majorOffset;
        for (var i = 0; i < count; i++)
        {
            if (major[i].IsDefinite)
                continue;

            var majorSpan = major[i].Span;
            if (minor[i].IsDefinite)
            {
                // "If the item has a definite column position: Set the column position of the cursor to the grid item's
                // column-start line. If this is less than the previous column position of the cursor, increment the row position
                // by 1. Increment the cursor's row position until a value is found where the grid item does not overlap any
                // occupied grid cells" (dense: from the start-most row line)
                if (flow.IsDense)
                {
                    cursorMajor = -majorOffset;
                }
                else if (minor[i].Start < cursorMinor)
                {
                    cursorMajor++;
                }

                cursorMinor = minor[i].Start;
                while (occupancy.IsOccupied(minor[i].Start, minor[i].End, cursorMajor, cursorMajor + majorSpan))
                {
                    cursorMajor++;
                }

                major[i] = GridAxisPlacement.Definite(cursorMajor, cursorMajor + majorSpan);
            }
            else
            {
                // "If the item has an automatic grid position in both axes: Increment the column position of the auto-placement
                // cursor until either this item's grid area does not overlap any occupied grid cells, or the cursor's column
                // position, plus the item's column span, overflow the number of columns in the implicit grid [...]. Otherwise,
                // increment the auto-placement cursor's row position, set its column position to the start-most column line in
                // the implicit grid, and return to the previous step."
                var minorSpan = Math.Min(minor[i].Span, minorEnd + minorOffset);
                if (flow.IsDense)
                {
                    cursorMinor = -minorOffset;
                    cursorMajor = -majorOffset;
                }

                while (true)
                {
                    var placed = false;
                    while (cursorMinor + minorSpan <= minorEnd)
                    {
                        if (!occupancy.IsOccupied(cursorMinor, cursorMinor + minorSpan, cursorMajor, cursorMajor + majorSpan))
                        {
                            placed = true;
                            break;
                        }

                        cursorMinor++;
                    }

                    if (placed)
                        break;

                    cursorMajor++;
                    cursorMinor = -minorOffset;

                    // A grid without enough columns for the item (a clamped subgrid): placed in the first row with room
                    if (cursorMajor > GridExplicitAxis.MaxLine)
                        break;
                }

                minor[i] = GridAxisPlacement.Definite(cursorMinor, cursorMinor + minorSpan);
                major[i] = GridAxisPlacement.Definite(cursorMajor, cursorMajor + majorSpan);
            }

            occupancy.Occupy(minor[i].Start, minor[i].End, major[i].Start, major[i].End);
            if (!flow.IsDense)
            {
                // The cursor moves past the item
                cursorMinor = minor[i].End;
            }
        }

    }

    /// <summary>The occupied grid cells of the auto-placement algorithm, indexed by the lines of the explicit grid (possibly negative, from the given offsets).</summary>
    private sealed class GridOccupancy(int minorOffset, int majorOffset)
    {
        private readonly List<List<(int Start, int End)>?> _rows = [];

        public void Occupy(int minorStart, int minorEnd, int majorStart, int majorEnd)
        {
            for (var row = majorStart; row < majorEnd; row++)
            {
                var index = row + majorOffset;
                if (index < 0)
                    continue;

                while (_rows.Count <= index)
                {
                    _rows.Add(null);
                }

                (_rows[index] ??= []).Add((minorStart + minorOffset, minorEnd + minorOffset));
            }
        }

        public bool IsOccupied(int minorStart, int minorEnd, int majorStart, int majorEnd)
        {
            var start = minorStart + minorOffset;
            var end = minorEnd + minorOffset;
            for (var row = majorStart; row < majorEnd; row++)
            {
                var index = row + majorOffset;
                if (index < 0 || index >= _rows.Count || _rows[index] is not { } intervals)
                    continue;

                foreach (var (occupiedStart, occupiedEnd) in intervals)
                {
                    if (occupiedStart < end && start < occupiedEnd)
                        return true;
                }
            }

            return false;
        }
    }
}
