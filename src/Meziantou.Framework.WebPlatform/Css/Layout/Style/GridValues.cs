using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kind of a track sizing function bound (https://drafts.csswg.org/css-grid-2/#track-sizes).</summary>
internal enum GridBreadthKind : byte
{
    /// <summary>A fixed &lt;length-percentage&gt;.</summary>
    LengthPercentage,

    /// <summary>A &lt;flex&gt; (fr).</summary>
    Flex,
    Auto,
    MinContent,
    MaxContent,
}

/// <summary>A bound of a track sizing function: a &lt;length-percentage&gt;, a &lt;flex&gt; or an intrinsic keyword.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GridBreadth(GridBreadthKind Kind, LengthPercentage Length = default, double Flex = 0)
{
    public static GridBreadth Auto => new(GridBreadthKind.Auto);

    public bool IsIntrinsic => Kind is GridBreadthKind.Auto or GridBreadthKind.MinContent or GridBreadthKind.MaxContent;

    internal static GridBreadth? Parse(CssComponentValue value, double zoom)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Ident } ident:
                return InfraAscii.ToAsciiLowercase(ident.Token.Value) switch
                {
                    "auto" => Auto,
                    "min-content" => new GridBreadth(GridBreadthKind.MinContent),
                    "max-content" => new GridBreadth(GridBreadthKind.MaxContent),
                    _ => null,
                };

            case CssPreservedToken { Type: CssTokenType.Dimension } dimension when string.Equals(dimension.Token.Value, "fr", StringComparison.OrdinalIgnoreCase):
                return new GridBreadth(GridBreadthKind.Flex, Flex: dimension.Token.NumericValue);

            default:
                return LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length) ? new GridBreadth(GridBreadthKind.LengthPercentage, length) : null;
        }
    }
}

/// <summary>
/// A track sizing function (https://drafts.csswg.org/css-grid-2/#typedef-track-size): its minimum and maximum track
/// sizing functions (https://drafts.csswg.org/css-grid-2/#min-track-sizing-function). A single breadth is both, except
/// a &lt;flex&gt;, whose minimum is auto. <c>fit-content(L)</c> is <c>minmax(auto, max-content)</c> clamped to L
/// (<see cref="IsFitContent"/>, the limit is <see cref="Max"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GridTrackSize(GridBreadth Min, GridBreadth Max, bool IsFitContent = false)
{
    public static GridTrackSize Auto => new(GridBreadth.Auto, GridBreadth.Auto);

    internal static GridTrackSize? Parse(CssComponentValue value, double zoom)
    {
        if (value is CssFunction function)
        {
            var name = InfraAscii.ToAsciiLowercase(function.Name);
            if (name == "minmax")
            {
                var arguments = CssValueComputer.SplitArguments(ValueMatcher.RemoveWhitespace(function.Value));
                if (arguments is [[var min], [var max]] && GridBreadth.Parse(min, zoom) is { } minBreadth && GridBreadth.Parse(max, zoom) is { } maxBreadth)
                    return new GridTrackSize(minBreadth.Kind == GridBreadthKind.Flex ? GridBreadth.Auto : minBreadth, maxBreadth);

                return null;
            }

            if (name == "fit-content")
            {
                return ValueMatcher.RemoveWhitespace(function.Value) is [var argument] && LengthPercentage.TryParse(argument, zoom, nonNegative: true, out var limit)
                    ? new GridTrackSize(GridBreadth.Auto, new GridBreadth(GridBreadthKind.LengthPercentage, limit), IsFitContent: true)
                    : null;
            }
        }

        if (GridBreadth.Parse(value, zoom) is not { } breadth)
            return null;

        return new GridTrackSize(breadth.Kind == GridBreadthKind.Flex ? GridBreadth.Auto : breadth, breadth);
    }
}

/// <summary>The repetition of a <see cref="GridTrackSegment"/> (https://drafts.csswg.org/css-grid-2/#repeat-notation).</summary>
internal enum GridRepeatKind : byte
{
    /// <summary>The tracks are not in a repeat() function (they are listed once).</summary>
    None,

    /// <summary><c>repeat(&lt;integer&gt;, ...)</c>.</summary>
    Count,
    AutoFill,
    AutoFit,
}

/// <summary>
/// A part of a track list: tracks that are listed once, or the tracks of a repeat() function. <see cref="LineNames"/>
/// has one more item than <see cref="Tracks"/>: the names of the line before each track, then the names of the last line.
/// The names of the last line of a segment and of the first line of the next one are the same line (they merge).
/// </summary>
internal sealed class GridTrackSegment(GridRepeatKind repeat, int count, GridTrackSize[] tracks, string[][] lineNames)
{
    public GridRepeatKind Repeat { get; } = repeat;

    /// <summary>The number of repetitions of <see cref="GridRepeatKind.Count"/> (1 otherwise).</summary>
    public int Count { get; } = count;

    public GridTrackSize[] Tracks { get; } = tracks;

    public string[][] LineNames { get; } = lineNames;
}

/// <summary>
/// The computed value of grid-template-rows or grid-template-columns (https://drafts.csswg.org/css-grid-2/#track-sizing):
/// none, a track list (with at most one automatic repetition), or a subgrid with its line names.
/// </summary>
internal sealed class GridTrackList
{
    private GridTrackList(bool isSubgrid, GridTrackSegment[] segments)
    {
        IsSubgrid = isSubgrid;
        Segments = segments;
    }

    public static GridTrackList None { get; } = new(isSubgrid: false, []);

    /// <summary>Whether the value is none (no explicit track).</summary>
    public bool IsNone => !IsSubgrid && Segments.Length == 0;

    /// <summary>Whether the value is subgrid (https://drafts.csswg.org/css-grid-2/#subgrids): the segments only have line names.</summary>
    public bool IsSubgrid { get; }

    public GridTrackSegment[] Segments { get; }

    /// <summary>The automatic repetition (auto-fill or auto-fit), or null.</summary>
    public GridTrackSegment? AutoRepeat => Array.Find(Segments, segment => segment.Repeat is GridRepeatKind.AutoFill or GridRepeatKind.AutoFit);

    /// <summary>Parses a computed track list (e.g. <c>[a] 100px repeat(2, [b] 1fr minmax(10px, auto)) [c]</c>).</summary>
    public static GridTrackList Parse(string value, double zoom)
    {
        if (value is "none" or "" or "masonry")
            return None;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        var isSubgrid = values is [CssPreservedToken { Type: CssTokenType.Ident } first, ..] && first.Token.IsIdent("subgrid");
        var segments = new List<GridTrackSegment>();
        var tracks = new List<GridTrackSize>();
        var names = new List<string[]>();
        string[] pendingNames = [];
        for (var i = isSubgrid ? 1 : 0; i < values.Count; i++)
        {
            switch (values[i])
            {
                case CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } block:
                    pendingNames = [.. pendingNames, .. GetLineNames(block)];

                    // The line names of a subgrid are listed without tracks
                    if (isSubgrid)
                    {
                        names.Add(pendingNames);
                        pendingNames = [];
                    }

                    break;

                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"):
                    if (tracks.Count > 0 || names.Count > 0 || pendingNames.Length > 0)
                    {
                        AddSegment(segments, tracks, names, pendingNames, isSubgrid);
                        pendingNames = [];
                    }

                    if (ParseRepeat(function, zoom, isSubgrid) is not { } repeat)
                        return None;

                    segments.Add(repeat);
                    break;

                case var component:
                    if (isSubgrid || GridTrackSize.Parse(component, zoom) is not { } track)
                        return None;

                    names.Add(pendingNames);
                    pendingNames = [];
                    tracks.Add(track);
                    break;
            }
        }

        if (tracks.Count > 0 || names.Count > 0 || pendingNames.Length > 0)
        {
            AddSegment(segments, tracks, names, pendingNames, isSubgrid);
        }

        return segments.Count == 0 && !isSubgrid ? None : new GridTrackList(isSubgrid, [.. segments]);
    }

    private static void AddSegment(List<GridTrackSegment> segments, List<GridTrackSize> tracks, List<string[]> names, string[] lastNames, bool isSubgrid)
    {
        if (!isSubgrid || lastNames.Length > 0)
        {
            names.Add(lastNames);
        }

        segments.Add(new GridTrackSegment(GridRepeatKind.None, 1, [.. tracks], [.. names]));
        tracks.Clear();
        names.Clear();
    }

    private static GridTrackSegment? ParseRepeat(CssFunction function, double zoom, bool isSubgrid)
    {
        var arguments = CssValueComputer.SplitArguments(ValueMatcher.RemoveWhitespace(function.Value));
        if (arguments is not [[CssPreservedToken countToken], var repeated])
            return null;

        GridRepeatKind kind;
        var count = 1;
        if (countToken.Token.Type == CssTokenType.Number)
        {
            kind = GridRepeatKind.Count;
            count = (int)Math.Clamp(countToken.Token.NumericValue, 1, int.MaxValue);
        }
        else if (countToken.Token.IsIdent("auto-fill"))
        {
            kind = GridRepeatKind.AutoFill;
        }
        else if (countToken.Token.IsIdent("auto-fit"))
        {
            kind = GridRepeatKind.AutoFit;
        }
        else
        {
            return null;
        }

        var tracks = new List<GridTrackSize>();
        var names = new List<string[]>();
        string[] pendingNames = [];
        foreach (var value in repeated)
        {
            if (value is CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } block)
            {
                pendingNames = [.. pendingNames, .. GetLineNames(block)];
                if (isSubgrid)
                {
                    names.Add(pendingNames);
                    pendingNames = [];
                }
            }
            else if (!isSubgrid && GridTrackSize.Parse(value, zoom) is { } track)
            {
                names.Add(pendingNames);
                pendingNames = [];
                tracks.Add(track);
            }
            else
            {
                return null;
            }
        }

        if (!isSubgrid)
        {
            names.Add(pendingNames);
        }

        return new GridTrackSegment(kind, count, [.. tracks], [.. names]);
    }

    private static string[] GetLineNames(CssSimpleBlock block)
    {
        var names = new List<string>();
        foreach (var value in block.Value)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident)
            {
                names.Add(ident.Token.Value);
            }
        }

        return [.. names];
    }

    /// <summary>Parses a computed grid-auto-rows or grid-auto-columns value: a list of track sizes.</summary>
    public static GridTrackSize[] ParseAutoTracks(string value, double zoom)
    {
        if (value == "auto")
            return [GridTrackSize.Auto];

        var result = new List<GridTrackSize>();
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            if (GridTrackSize.Parse(component, zoom) is not { } track)
                return [GridTrackSize.Auto];

            result.Add(track);
        }

        return result.Count == 0 ? [GridTrackSize.Auto] : [.. result];
    }
}

/// <summary>
/// The computed value of grid-template-areas (https://drafts.csswg.org/css-grid-2/#grid-template-areas-property): the
/// cells of each row (null for a null cell token) and the named grid areas (whose computed value guarantees that they are
/// rectangles).
/// </summary>
internal sealed class GridTemplateAreas
{
    private readonly Dictionary<string, (int RowStart, int RowEnd, int ColumnStart, int ColumnEnd)> _areas = new(StringComparer.Ordinal);

    private GridTemplateAreas(string?[][] rows)
    {
        Rows = rows;
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is not { } name)
                    continue;

                // The lines are numbered from 1; the end lines are exclusive
                _areas[name] = _areas.TryGetValue(name, out var area)
                    ? (Math.Min(area.RowStart, row + 1), Math.Max(area.RowEnd, row + 2), Math.Min(area.ColumnStart, column + 1), Math.Max(area.ColumnEnd, column + 2))
                    : (row + 1, row + 2, column + 1, column + 2);
            }
        }
    }

    public static GridTemplateAreas None { get; } = new([]);

    public bool IsNone => Rows.Length == 0;

    public string?[][] Rows { get; }

    public int RowCount => Rows.Length;

    public int ColumnCount => Rows.Length == 0 ? 0 : Rows[0].Length;

    /// <summary>The names of the named grid areas.</summary>
    public IEnumerable<string> AreaNames => _areas.Keys;

    /// <summary>Returns the lines (1-based, the end lines exclusive) of a named grid area.</summary>
    public bool TryGetArea(string name, out (int RowStart, int RowEnd, int ColumnStart, int ColumnEnd) area) => _areas.TryGetValue(name, out area);

    public static GridTemplateAreas Parse(string value)
    {
        if (value == "none")
            return None;

        var rows = new List<string?[]>();
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            if (component is not CssPreservedToken { Type: CssTokenType.String } stringToken || CssGrid.TokenizeAreaString(stringToken.Token.Value) is not { } cells)
                return None;

            rows.Add([.. cells.Select(cell => cell == "." ? null : cell)]);
        }

        return rows.Count == 0 ? None : new GridTemplateAreas([.. rows]);
    }
}

/// <summary>
/// A grid placement (https://drafts.csswg.org/css-grid-2/#typedef-grid-row-start-grid-line): auto, a line number and/or
/// name, or a span.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GridLine(bool IsAuto, bool IsSpan, int Integer, string? Name)
{
    public static GridLine Auto => new(IsAuto: true, IsSpan: false, 0, Name: null);

    /// <summary>Parses a computed value: <c>auto</c>, <c>2</c>, <c>-1</c>, <c>a</c>, <c>a 2</c>, <c>span 2</c>, <c>span a</c>, <c>span 2 a</c>.</summary>
    public static GridLine Parse(string value)
    {
        if (value == "auto")
            return Auto;

        var span = false;
        var integer = 0;
        string? name = null;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "span")
            {
                span = true;
            }
            else if (int.TryParse(part, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                integer = number;
            }
            else
            {
                name = part;
            }
        }

        // https://drafts.csswg.org/css-grid-2/#grid-placement-span-int: the integer of a span defaults to 1
        if (span && integer == 0)
        {
            integer = 1;
        }

        return new GridLine(IsAuto: false, span, integer, name);
    }
}

/// <summary>https://drafts.csswg.org/css-grid-2/#grid-auto-flow-property</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GridAutoFlow(bool IsColumn, bool IsDense)
{
    public static GridAutoFlow Parse(string value) => new(value.Contains("column", StringComparison.Ordinal), value.Contains("dense", StringComparison.Ordinal));
}

/// <summary>The orientation of a grid lanes container (https://drafts.csswg.org/css-grid-3/#grid-lanes-orientation).</summary>
internal enum GridLanesOrientation : byte
{
    /// <summary>From the <c>grid-template-*</c> properties: rows when <c>grid-template-columns</c> is <c>none</c> and <c>grid-template-rows</c> is not, else columns.</summary>
    Normal,

    /// <summary>Row lanes: the grid axis is the block axis, the items stack in the inline axis.</summary>
    Row,

    /// <summary>Column lanes: the grid axis is the inline axis, the items stack in the block axis.</summary>
    Column,
}

/// <summary>
/// <c>grid-lanes-direction</c> (<c>normal | [ row | column ] [ fill-reverse || track-reverse ]?</c>): the property of the WPT tests
/// (tentative) for the orientation that CSS Grid 3 leaves to a "TBD property" (https://drafts.csswg.org/css-grid-3/#grid-lanes-orientation,
/// https://github.com/w3c/csswg-drafts/issues/12803). <c>fill-reverse</c> stacks the items from the end edge of the stacking axis,
/// <c>track-reverse</c> auto-places them from the last track.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GridLanesDirection(GridLanesOrientation Orientation, bool FillReverse, bool TrackReverse)
{
    public static GridLanesDirection Parse(string value)
    {
        if (value is "normal" or "")
            return default;

        var orientation = GridLanesOrientation.Normal;
        var fillReverse = false;
        var trackReverse = false;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "row":
                    orientation = GridLanesOrientation.Row;
                    break;
                case "column":
                    orientation = GridLanesOrientation.Column;
                    break;
                case "fill-reverse":
                    fillReverse = true;
                    break;
                case "track-reverse":
                    trackReverse = true;
                    break;
            }
        }

        return new GridLanesDirection(orientation, fillReverse, trackReverse);
    }
}

/// <summary>https://drafts.csswg.org/css-grid-3/#placement-tolerance: the <c>fit-tolerance</c> property (formerly <c>flow-tolerance</c>).</summary>
/// <param name="IsNormal">Whether the value is <c>normal</c>: 1em in grid lanes layout.</param>
/// <param name="IsInfinite">Whether the value is <c>infinite</c>: the items are placed strictly in order.</param>
/// <param name="Length">The &lt;length-percentage&gt; (the percentages resolve against the grid-axis content box size).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FlowTolerance(bool IsNormal, bool IsInfinite, LengthPercentage Length)
{
    public static FlowTolerance Parse(string value, double zoom) => value switch
    {
        "normal" => new FlowTolerance(IsNormal: true, IsInfinite: false, default),
        "infinite" => new FlowTolerance(IsNormal: false, IsInfinite: true, default),
        _ => LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length) ? new FlowTolerance(IsNormal: false, IsInfinite: false, length) : new FlowTolerance(IsNormal: true, IsInfinite: false, default),
    };
}
