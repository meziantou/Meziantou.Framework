using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The grid properties (https://drafts.csswg.org/css-grid/): the grid-template-areas strings, the line names of the track
/// lists, the &lt;grid-line&gt; values, and the grid, grid-template, grid-area, grid-row and grid-column shorthands, which
/// are not described by their grammar alone (omitted values, merged line names, areas and track sizes interleaved).
/// </summary>
internal static class CssGrid
{
    private static readonly string[] AreaLonghands = ["grid-row-start", "grid-column-start", "grid-row-end", "grid-column-end"];

    public static bool IsShorthand(string name) => name is "grid" or "grid-template" or "grid-area" or "grid-row" or "grid-column" or "grid-lanes";

    // ----------------------------------------------------------------------------------------------------------------
    // Longhands
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Validates and serializes a grid longhand in its canonical form. Returns null when the property is not handled, and
    /// the empty string when the value is invalid (e.g. grid-template-areas whose areas are not rectangles).
    /// </summary>
    public static string? CanonicalizeLonghand(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        switch (property.Name)
        {
            case "grid-template-areas":
                return significant is [CssPreservedToken { Type: CssTokenType.Ident }] ? "none" : SerializeAreas(significant) ?? "";

            case "grid-template-rows" or "grid-template-columns" when significant is [CssPreservedToken { Type: CssTokenType.Ident } first, ..] && first.Token.IsIdent("subgrid"):
            {
                // https://drafts.csswg.org/css-grid-2/#typedef-line-name-list: "subgrid <line-name-list>?" with at most one
                // repeat(auto-fill, ...); the empty line names are kept (each one names a line of the subgrid)
                var autoFill = 0;
                foreach (var value in significant)
                {
                    if (value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat") &&
                        SplitAtComma(ValueMatcher.RemoveWhitespace(function.Value)) is [[CssPreservedToken count], _] && count.Token.IsIdent("auto-fill"))
                    {
                        autoFill++;
                    }
                }

                return autoFill > 1 ? "" : SerializeTrackList(significant, property, keepsEmptyLineNames: true);
            }

            case "grid-template-rows" or "grid-template-columns" or "grid-auto-rows" or "grid-auto-columns":
                return SerializeTrackList(significant, property);

            case "grid-auto-flow":
            {
                var column = significant.Exists(v => v is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("column"));
                var dense = significant.Exists(v => v is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("dense"));
                return column ? dense ? "column dense" : "column" : dense ? "dense" : "row";
            }

            case "grid-row-start" or "grid-row-end" or "grid-column-start" or "grid-column-end":
                return SerializeGridLine(significant, property);

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid/#grid-template-areas-property: the strings, whose tokens are separated by a space
    /// (a sequence of "." is a single null cell token). Returns null when the strings are invalid: a trash token, a row
    /// without cells, rows with different numbers of columns, or a named area that is not a rectangle.
    /// </summary>
    private static string? SerializeAreas(List<CssComponentValue> strings)
    {
        var rows = new List<List<string>>();
        foreach (var value in strings)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.String } stringToken || TokenizeAreaString(stringToken.Token.Value) is not { Count: > 0 } cells)
                return null;

            if (rows.Count > 0 && cells.Count != rows[0].Count)
                return null;

            rows.Add(cells);
        }

        if (rows.Count == 0 || !AreAreasRectangles(rows))
            return null;

        return string.Join(' ', rows.Select(row => CssSerializer.SerializeString(string.Join(' ', row))));
    }

    /// <summary>https://drafts.csswg.org/css-grid/#valdef-grid-template-areas-string: the named cell tokens and the null cell tokens ("."), or null when there is a trash token.</summary>
    internal static List<string>? TokenizeAreaString(string value)
    {
        var cells = new List<string>();
        var position = 0;
        while (position < value.Length)
        {
            var c = value[position];
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                position++;
            }
            else if (c == '.')
            {
                while (position < value.Length && value[position] == '.')
                {
                    position++;
                }

                cells.Add(".");
            }
            else if (IsIdentCodePoint(c))
            {
                var start = position;
                while (position < value.Length && IsIdentCodePoint(value[position]))
                {
                    position++;
                }

                cells.Add(value[start..position]);
            }
            else
            {
                return null;
            }
        }

        return cells;

        static bool IsIdentCodePoint(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c >= 0x80;
    }

    private static bool AreAreasRectangles(List<List<string>> rows)
    {
        var areas = new Dictionary<string, (int Top, int Left, int Bottom, int Right)>(StringComparer.Ordinal);
        for (var row = 0; row < rows.Count; row++)
        {
            for (var column = 0; column < rows[row].Count; column++)
            {
                var name = rows[row][column];
                if (name == ".")
                    continue;

                areas[name] = areas.TryGetValue(name, out var area)
                    ? (Math.Min(area.Top, row), Math.Min(area.Left, column), Math.Max(area.Bottom, row), Math.Max(area.Right, column))
                    : (row, column, row, column);
            }
        }

        foreach (var (name, area) in areas)
        {
            for (var row = area.Top; row <= area.Bottom; row++)
            {
                for (var column = area.Left; column <= area.Right; column++)
                {
                    if (rows[row][column] != name)
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>Serializes a track list: the empty line names ([]) are omitted, inside repeat() too, except in the line name list of a subgrid.</summary>
    private static string SerializeTrackList(List<CssComponentValue> values, CssPropertyDefinition property, bool keepsEmptyLineNames = false)
    {
        var parts = new List<string>();
        foreach (var value in values)
        {
            switch (value)
            {
                case CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } block:
                    if (SerializeLineNames(block) is { } names)
                    {
                        parts.Add(names);
                    }
                    else if (keepsEmptyLineNames)
                    {
                        parts.Add("[]");
                    }

                    break;

                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"):
                {
                    var arguments = SplitAtComma(ValueMatcher.RemoveWhitespace(function.Value));
                    if (arguments.Count != 2)
                    {
                        parts.Add(CssSerializer.SerializeValue([value], property));
                        break;
                    }

                    parts.Add("repeat(" + CssSerializer.SerializeValue(InsertSpaces(arguments[0]), property) + ", " + SerializeTrackList(arguments[1], property, keepsEmptyLineNames) + ")");
                    break;
                }

                default:
                    parts.Add(CssSerializer.SerializeValue([value], property));
                    break;
            }
        }

        return string.Join(' ', parts);
    }

    private static string? SerializeLineNames(CssSimpleBlock block) => GetLineNames(block) is { Count: > 0 } names ? "[" + string.Join(' ', names) + "]" : null;

    private static List<string> GetLineNames(CssSimpleBlock block) => [.. ValueMatcher.RemoveWhitespace(block.Value).Select(v => CssSerializer.SerializeIdentifier(((CssPreservedToken)v).Token.Value))];

    /// <summary>
    /// https://drafts.csswg.org/css-grid/#typedef-grid-row-start-grid-line: span, then the integer, then the name; the
    /// integer 1 is omitted after span when there is a name.
    /// </summary>
    private static string SerializeGridLine(List<CssComponentValue> values, CssPropertyDefinition? property)
    {
        if (values is [CssPreservedToken { Type: CssTokenType.Ident }])
            return CssSerializer.SerializeValue(values, property);

        var span = values.Exists(v => v is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("span"));
        var integer = values.Find(v => v is CssPreservedToken { Type: CssTokenType.Number } or CssFunction);
        var name = values.Find(v => v is CssPreservedToken { Type: CssTokenType.Ident } ident && !ident.Token.IsIdent("span"));
        var parts = new List<string>(3);
        if (span)
        {
            parts.Add("span");
        }

        if (integer is not null && !(span && name is not null && integer is CssPreservedToken { Token.NumericValue: 1 }))
        {
            parts.Add(CssSerializer.SerializeValue([integer], property));
        }

        if (name is not null)
        {
            parts.Add(CssSerializer.SerializeValue([name], property));
        }

        return string.Join(' ', parts);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Expansion of the shorthands
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Expands a grid shorthand, or returns null when the value is invalid.</summary>
    public static List<CssLonghandValue>? Expand(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var result = shorthand.Name switch
        {
            "grid-row" or "grid-column" => ExpandLines(shorthand, significant),
            "grid-area" => ExpandArea(significant),
            "grid-template" => ExpandTemplate(significant),
            "grid" => ExpandGrid(significant),
            "grid-lanes" => ExpandLanes(significant),
            _ => null,
        };

        return result?.Select(pair => new CssLonghandValue(pair.Key, pair.Value)).ToList();
    }

    /// <summary>
    /// The <c>grid-lanes</c> shorthand of the WPT tests (css/css-grid/grid-lanes/tentative/parsing, the orientation of CSS Grid 3 is a
    /// "TBD property", https://github.com/w3c/csswg-drafts/issues/12803): <c>none | [ &lt;string&gt; || &lt;track-list&gt; ||
    /// &lt;'grid-lanes-direction'&gt; ]</c>. The track list is the template of the grid axis (the rows with <c>row</c>, else the
    /// columns), the other template is <c>none</c>; the string names the areas of the grid axis (one area per track: a row of
    /// areas, or a column of areas with <c>row</c>); the omitted direction is <c>column</c>, as in the browsers.
    /// </summary>
    private static Dictionary<string, string>? ExpandLanes(List<CssComponentValue> significant)
    {
        string? areas = null;
        var direction = new List<string>();
        var tracks = new List<CssComponentValue>();
        foreach (var value in significant)
        {
            if (value is CssPreservedToken { Type: CssTokenType.String } stringToken)
            {
                areas = stringToken.Token.Value;
            }
            else if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.ToAsciiLowercase(ident.Token.Value) is "normal" or "row" or "column" or "fill-reverse" or "track-reverse")
            {
                direction.Add(InfraAscii.ToAsciiLowercase(ident.Token.Value));
            }
            else if (value is not CssPreservedToken { Type: CssTokenType.Ident } noneIdent || !noneIdent.Token.IsIdent("none"))
            {
                tracks.Add(value);
            }
        }

        var directionValue = direction.Count == 0 ? "column" : string.Join(' ', direction);
        var isRow = direction is ["row", ..];
        var trackList = tracks.Count == 0 ? "none" : SerializeTrackList(tracks, CssPropertyRegistry.Get(isRow ? "grid-template-rows" : "grid-template-columns")!);
        var areasValue = "none";
        if (areas is not null)
        {
            if (TokenizeAreaString(areas) is not { Count: > 0 } cells)
                return null;

            // A row of areas, or a column of areas when the grid axis is the block axis
            var rows = isRow ? cells.Select(cell => new List<string> { cell }).ToList() : [cells];
            if (!AreAreasRectangles(rows))
                return null;

            areasValue = string.Join(' ', rows.Select(row => CssSerializer.SerializeString(string.Join(' ', row))));
        }

        return new(StringComparer.Ordinal)
        {
            ["grid-template-columns"] = isRow ? "none" : trackList,
            ["grid-template-rows"] = isRow ? trackList : "none",
            ["grid-template-areas"] = areasValue,
            ["grid-lanes-direction"] = directionValue,
        };
    }

    /// <summary>Serializes the <c>grid-lanes</c> shorthand (see <see cref="ExpandLanes"/>): the areas of the grid axis, its template and the direction (the template of the stacking axis is ignored).</summary>
    private static string? SerializeLanes(Dictionary<string, string> lookup)
    {
        var direction = lookup["grid-lanes-direction"];
        var isRow = direction.StartsWith("row", StringComparison.Ordinal);
        var tracks = lookup[isRow ? "grid-template-rows" : "grid-template-columns"];
        var areas = lookup["grid-template-areas"];
        var parts = new List<string>(3);
        if (areas != "none")
        {
            // The areas must be a single row of areas (a single column with row)
            var cells = new List<string>();
            var strings = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(areas));
            if (!isRow && strings.Count != 1)
                return null;

            foreach (var value in strings)
            {
                if (value is not CssPreservedToken { Type: CssTokenType.String } stringToken || TokenizeAreaString(stringToken.Token.Value) is not { } row || (isRow && row.Count != 1))
                    return null;

                cells.AddRange(row);
            }

            parts.Add(CssSerializer.SerializeString(string.Join(' ', cells)));
        }

        if (tracks != "none")
        {
            parts.Add(tracks);
        }

        parts.Add(direction);
        return string.Join(' ', parts);
    }

    private static Dictionary<string, string>? ExpandLines(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var parts = SplitAtSlash(significant);
        if (parts.Count > 2 || ParseGridLines(parts) is not { } lines)
            return null;

        var start = lines[0];
        var end = lines.Count > 1 ? lines[1] : GetOmittedLine(start);
        return new(StringComparer.Ordinal) { [shorthand.Longhands[0]] = start, [shorthand.Longhands[1]] = end };
    }

    /// <summary>https://drafts.csswg.org/css-grid/#propdef-grid-area: the omitted lines are the custom-ident of their pair, or auto.</summary>
    private static Dictionary<string, string>? ExpandArea(List<CssComponentValue> significant)
    {
        var parts = SplitAtSlash(significant);
        if (parts.Count > 4 || ParseGridLines(parts) is not { } lines)
            return null;

        var rowStart = lines[0];
        var columnStart = lines.Count > 1 ? lines[1] : GetOmittedLine(rowStart);
        var rowEnd = lines.Count > 2 ? lines[2] : GetOmittedLine(rowStart);
        var columnEnd = lines.Count > 3 ? lines[3] : GetOmittedLine(columnStart);
        return new(StringComparer.Ordinal)
        {
            ["grid-row-start"] = rowStart,
            ["grid-column-start"] = columnStart,
            ["grid-row-end"] = rowEnd,
            ["grid-column-end"] = columnEnd,
        };
    }

    private static List<string>? ParseGridLines(List<List<CssComponentValue>> parts)
    {
        var grammar = CssPropertyRegistry.GetTypeGrammar("<grid-line>");
        var property = CssPropertyRegistry.Get("grid-row-start");
        var result = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            if (grammar is null || part.Count == 0 || !ValueMatcher.Matches(grammar, part))
                return null;

            result.Add(SerializeGridLine(part, property));
        }

        return result;
    }

    private static string GetOmittedLine(string line) => IsCustomIdent(line) ? line : "auto";

    /// <summary>Returns whether a canonical &lt;grid-line&gt; is a single &lt;custom-ident&gt;.</summary>
    private static bool IsCustomIdent(string line) => line != "auto" && ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(line)) is [CssPreservedToken { Type: CssTokenType.Ident }];

    /// <summary>
    /// https://drafts.csswg.org/css-grid/#propdef-grid-template: none, rows / columns, or the areas with their row sizes and
    /// line names (the line names around a line are merged) and an optional explicit track list of columns.
    /// </summary>
    private static Dictionary<string, string>? ExpandTemplate(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } none] && none.Token.IsIdent("none"))
            return new(StringComparer.Ordinal) { ["grid-template-rows"] = "none", ["grid-template-columns"] = "none", ["grid-template-areas"] = "none" };

        var parts = SplitAtSlash(significant);
        if (parts.Count > 2 || parts.Exists(p => p.Count == 0))
            return null;

        if (parts[0].Exists(v => v is CssPreservedToken { Type: CssTokenType.String }))
            return ExpandTemplateAreas(parts);

        if (parts.Count != 2 || ParseLonghand("grid-template-rows", parts[0]) is not { } rows || ParseLonghand("grid-template-columns", parts[1]) is not { } columns)
            return null;

        return new(StringComparer.Ordinal) { ["grid-template-rows"] = rows, ["grid-template-columns"] = columns, ["grid-template-areas"] = "none" };
    }

    private static Dictionary<string, string>? ExpandTemplateAreas(List<List<CssComponentValue>> parts)
    {
        var trackSize = CssPropertyRegistry.GetTypeGrammar("<track-size>");
        var rowsProperty = CssPropertyRegistry.Get("grid-template-rows");
        var strings = new List<CssComponentValue>();
        var rows = new StringBuilder();
        var pendingNames = new List<string>();
        var values = parts[0];
        var position = 0;
        while (position < values.Count)
        {
            // [ <line-names>? <string> <track-size>? <line-names>? ]+
            if (values[position] is CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } before)
            {
                if (!IsLineNames(before))
                    return null;

                pendingNames.AddRange(GetLineNames(before));
                position++;
            }

            if (position >= values.Count || values[position] is not CssPreservedToken { Type: CssTokenType.String } stringToken)
                return null;

            strings.Add(stringToken);
            position++;
            AppendLineNames(rows, pendingNames);
            pendingNames.Clear();

            var size = "auto";
            if (position < values.Count && values[position] is not CssSimpleBlock and not CssPreservedToken { Type: CssTokenType.String })
            {
                if (trackSize is null || !ValueMatcher.Matches(trackSize, [values[position]]))
                    return null;

                size = CssSerializer.SerializeValue([values[position]], rowsProperty);
                position++;
            }

            AppendSeparator(rows).Append(size);
            if (position < values.Count && values[position] is CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } after)
            {
                if (!IsLineNames(after))
                    return null;

                pendingNames.AddRange(GetLineNames(after));
                position++;
            }
        }

        AppendLineNames(rows, pendingNames);
        if (SerializeAreas(strings) is not { } areas)
            return null;

        var columns = "none";
        if (parts.Count == 2)
        {
            var explicitTrackList = CssPropertyRegistry.GetTypeGrammar("<explicit-track-list>");
            if (explicitTrackList is null || !ValueMatcher.Matches(explicitTrackList, parts[1]))
                return null;

            columns = SerializeTrackList(parts[1], CssPropertyRegistry.Get("grid-template-columns")!);
        }

        return new(StringComparer.Ordinal) { ["grid-template-rows"] = rows.ToString(), ["grid-template-columns"] = columns, ["grid-template-areas"] = areas };

        static void AppendLineNames(StringBuilder builder, List<string> names)
        {
            if (names.Count > 0)
            {
                AppendSeparator(builder).Append('[').AppendJoin(' ', names).Append(']');
            }
        }

        static StringBuilder AppendSeparator(StringBuilder builder) => builder.Length > 0 ? builder.Append(' ') : builder;
    }

    /// <summary>https://drafts.csswg.org/css-grid/#propdef-grid: a grid-template value, or the auto-flow forms; the omitted longhands are reset.</summary>
    private static Dictionary<string, string>? ExpandGrid(List<CssComponentValue> significant)
    {
        if (ExpandTemplate(significant) is { } template)
        {
            template["grid-auto-rows"] = "auto";
            template["grid-auto-columns"] = "auto";
            template["grid-auto-flow"] = "row";
            return template;
        }

        var parts = SplitAtSlash(significant);
        if (parts.Count != 2)
            return null;

        var leftFlow = ParseAutoFlow(parts[0]);
        var rightFlow = ParseAutoFlow(parts[1]);
        if (leftFlow is { } rowFlow && rightFlow is null)
        {
            // [ auto-flow && dense? ] <'grid-auto-rows'>? / <'grid-template-columns'>
            if (ParseLonghand("grid-template-columns", parts[1]) is not { } columns || ParseAutoSizes("grid-auto-rows", rowFlow.Sizes) is not { } autoRows)
                return null;

            return new(StringComparer.Ordinal)
            {
                ["grid-template-rows"] = "none",
                ["grid-template-columns"] = columns,
                ["grid-template-areas"] = "none",
                ["grid-auto-rows"] = autoRows,
                ["grid-auto-columns"] = "auto",
                ["grid-auto-flow"] = rowFlow.Dense ? "dense" : "row",
            };
        }

        if (rightFlow is { } columnFlow && leftFlow is null)
        {
            // <'grid-template-rows'> / [ auto-flow && dense? ] <'grid-auto-columns'>?
            if (ParseLonghand("grid-template-rows", parts[0]) is not { } rows || ParseAutoSizes("grid-auto-columns", columnFlow.Sizes) is not { } autoColumns)
                return null;

            return new(StringComparer.Ordinal)
            {
                ["grid-template-rows"] = rows,
                ["grid-template-columns"] = "none",
                ["grid-template-areas"] = "none",
                ["grid-auto-rows"] = "auto",
                ["grid-auto-columns"] = autoColumns,
                ["grid-auto-flow"] = columnFlow.Dense ? "column dense" : "column",
            };
        }

        return null;
    }

    /// <summary>Parses [ auto-flow &amp;&amp; dense? ] followed by the track sizes, or returns null when there is no auto-flow keyword.</summary>
    private static (bool Dense, List<CssComponentValue> Sizes)? ParseAutoFlow(List<CssComponentValue> values)
    {
        var autoFlow = false;
        var dense = false;
        var position = 0;
        while (position < values.Count && values[position] is CssPreservedToken { Type: CssTokenType.Ident } ident && (ident.Token.IsIdent("auto-flow") || ident.Token.IsIdent("dense")))
        {
            if (ident.Token.IsIdent("auto-flow"))
            {
                if (autoFlow)
                    return null;

                autoFlow = true;
            }
            else
            {
                if (dense)
                    return null;

                dense = true;
            }

            position++;
        }

        return autoFlow ? (dense, values[position..]) : null;
    }

    private static string? ParseAutoSizes(string property, List<CssComponentValue> sizes) => sizes.Count == 0 ? "auto" : ParseLonghand(property, sizes);

    private static string? ParseLonghand(string name, List<CssComponentValue> values)
    {
        if (CssPropertyRegistry.Get(name) is not { } property || CssPropertyRegistry.GetGrammar(property) is not { } grammar || !ValueMatcher.Matches(grammar, values))
            return null;

        if (values.Exists(v => v is CssPreservedToken { Type: CssTokenType.Ident } ident && ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(ident.Token.Value))))
            return null;

        return CanonicalizeLonghand(property, values) ?? CssSerializer.SerializeValue(InsertSpaces(values), property);
    }

    private static bool IsLineNames(CssSimpleBlock block) => ValueMatcher.RemoveWhitespace(block.Value).TrueForAll(ValueMatcher.IsLineName);

    // ----------------------------------------------------------------------------------------------------------------
    // Serialization of the shorthands
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Serializes a grid shorthand from its longhands, or returns null when it cannot represent them.</summary>
    public static string? Serialize(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        switch (shorthand.Name)
        {
            case "grid-row" or "grid-column":
            {
                var start = lookup[shorthand.Longhands[0]];
                var end = lookup[shorthand.Longhands[1]];
                return end == GetOmittedLine(start) ? start : start + " / " + end;
            }

            case "grid-area":
            {
                var lines = AreaLonghands.Select(l => lookup[l]).ToList();
                if (lines[3] == GetOmittedLine(lines[1]))
                {
                    lines.RemoveAt(3);
                    if (lines[2] == GetOmittedLine(lines[0]))
                    {
                        lines.RemoveAt(2);
                        if (lines[1] == GetOmittedLine(lines[0]))
                        {
                            lines.RemoveAt(1);
                        }
                    }
                }

                return string.Join(" / ", lines);
            }

            case "grid-template":
                return SerializeTemplate(lookup);

            case "grid-lanes":
                return SerializeLanes(lookup);

            case "grid":
            {
                var autoRows = lookup["grid-auto-rows"];
                var autoColumns = lookup["grid-auto-columns"];
                var flow = lookup["grid-auto-flow"];
                var rows = lookup["grid-template-rows"];
                var columns = lookup["grid-template-columns"];
                var areas = lookup["grid-template-areas"];
                if (autoRows == "auto" && autoColumns == "auto" && flow == "row")
                    return SerializeTemplate(lookup);

                if (areas != "none")
                    return null;

                if (flow is "column" or "column dense" && autoRows == "auto" && columns == "none")
                    return rows + " / auto-flow" + (flow == "column dense" ? " dense" : "") + (autoColumns == "auto" ? "" : " " + autoColumns);

                if (flow is "row" or "dense" && autoColumns == "auto" && rows == "none")
                    return "auto-flow" + (flow == "dense" ? " dense" : "") + (autoRows == "auto" ? "" : " " + autoRows) + " / " + columns;

                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>https://drafts.csswg.org/css-grid/#propdef-grid-template: the areas form needs a row size (and its line names) per string, without repeat().</summary>
    private static string? SerializeTemplate(Dictionary<string, string> lookup)
    {
        var rows = lookup["grid-template-rows"];
        var columns = lookup["grid-template-columns"];
        var areas = lookup["grid-template-areas"];
        if (areas == "none")
            return rows == "none" && columns == "none" ? "none" : rows + " / " + columns;

        if (columns.Contains("repeat(", StringComparison.Ordinal) || rows.Contains("repeat(", StringComparison.Ordinal) || rows == "none")
            return null;

        var strings = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(areas));
        var rowValues = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(rows));
        var builder = new StringBuilder();
        var stringIndex = 0;
        foreach (var value in rowValues)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (value is CssSimpleBlock)
            {
                builder.Append(CssSerializer.SerializeValue([value], property: null));
                continue;
            }

            if (stringIndex >= strings.Count)
                return null;

            builder.Append(CssSerializer.SerializeValue([strings[stringIndex++]], property: null));
            var size = CssSerializer.SerializeValue([value], property: null);
            if (size != "auto")
            {
                builder.Append(' ').Append(size);
            }
        }

        if (stringIndex != strings.Count)
            return null;

        if (columns != "none")
        {
            builder.Append(" / ").Append(columns);
        }

        return builder.ToString();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Computed values of the track lists
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns whether a computed track list has a repeat(auto-fill | auto-fit) whose track sizes are not all
    /// &lt;fixed-size&gt;s: the intrinsic auto repeat of the grid lanes (https://drafts.csswg.org/css-grid-3/#masonry-intrinsic-repeat).
    /// </summary>
    public static bool HasIntrinsicAutoRepeat(string trackList)
    {
        if (!trackList.Contains("repeat(", StringComparison.Ordinal))
            return false;

        var fixedSize = CssPropertyRegistry.GetTypeGrammar("<fixed-size>");
        foreach (var value in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(trackList)))
        {
            if (value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat") &&
                SplitAtComma(ValueMatcher.RemoveWhitespace(function.Value)) is [[CssPreservedToken { Type: CssTokenType.Ident }], var tracks] &&
                tracks.Exists(track => track is not CssSimpleBlock && (fixedSize is null || !ValueMatcher.Matches(fixedSize, [track]))))
            {
                return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------------------------

    private static List<List<CssComponentValue>> SplitAtSlash(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken slash && slash.Token.IsDelim('/'))
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private static List<List<CssComponentValue>> SplitAtComma(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private static List<CssComponentValue> InsertSpaces(List<CssComponentValue> values)
    {
        var result = new List<CssComponentValue>(values.Count * 2);
        foreach (var value in values)
        {
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Whitespace, Value = " " }));
            }

            result.Add(value);
        }

        return result;
    }
}
