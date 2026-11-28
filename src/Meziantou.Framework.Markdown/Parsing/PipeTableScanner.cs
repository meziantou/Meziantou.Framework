using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>Reads the delimiter rows of the pipe tables of Markdig, as Meziantou.Framework.Markdown 1.x does.</summary>
internal static class PipeTableScanner
{
    /// <summary>
    /// Reads the columns of a delimiter row: the cells between its pipes, such as <c>:--</c>, which can be empty, and the cell
    /// after its last pipe. A pipe that only whitespace precedes starts the row. Returns <see langword="null"/> when a cell is
    /// not a delimiter, or when the row has no dash.
    /// </summary>
    /// <param name="pipes">The positions of the pipes of the row, in the order of the text.</param>
    /// <param name="hasLineEnd">
    /// Whether the row ends with a line break of the table. Without one, as for Markdig, the cell after the last pipe is only read
    /// when the pipe does not start the row: <c>| --</c> has no column then.
    /// </param>
    public static List<(MarkdownTableAlignment Alignment, int DashCount)>? ReadColumns(string text, int start, int end, IReadOnlyList<int> pipes, bool hasLineEnd)
    {
        List<(MarkdownTableAlignment Alignment, int DashCount)>? columns = null;
        var dashCount = 0;
        var cellStart = start;
        for (var i = 0; i < pipes.Count; i++)
        {
            var pipe = pipes[i];
            if (i == 0 && IsWhitespace(text, start, pipe))
            {
                cellStart = pipe + 1;
                continue;
            }

            if (!TryReadColumn(text, cellStart, pipe, out var column))
            {
                return null;
            }

            columns ??= [];
            columns.Add(column);
            dashCount += column.DashCount;
            cellStart = pipe + 1;
            if (!hasLineEnd && i == pipes.Count - 1)
            {
                return ReadLastColumn(text, cellStart, end, columns, dashCount, allowEmpty: true);
            }
        }

        return hasLineEnd ? ReadLastColumn(text, cellStart, end, columns, dashCount, allowEmpty: columns is { Count: > 0 }) : null;
    }

    private static List<(MarkdownTableAlignment Alignment, int DashCount)>? ReadLastColumn(string text, int start, int end, List<(MarkdownTableAlignment Alignment, int DashCount)>? columns, int dashCount, bool allowEmpty)
    {
        if (IsWhitespace(text, start, end))
        {
            return allowEmpty && dashCount > 0 ? columns : null;
        }

        if (!TryReadColumn(text, start, end, out var column))
        {
            return null;
        }

        columns ??= [];
        columns.Add(column);
        return dashCount + column.DashCount > 0 ? columns : null;
    }

    // A cell of a delimiter row: whitespace, or an optional colon, dashes and an optional colon, with whitespace around them
    // Markdig reads the text before the pipe, which an escaped character starts: the characters before it are not read
    private static bool TryReadColumn(string text, int start, int end, out (MarkdownTableAlignment Alignment, int DashCount) column)
    {
        column = (MarkdownTableAlignment.None, 0);
        if (text.AsSpan(start, end - start).LastIndexOf('\\') is >= 0 and var escape)
        {
            start += escape;
        }

        var position = SkipWhitespace(text, start, end);
        if (position == end)
        {
            return true;
        }

        var left = IsAt(text, ref position, end, ':');
        if (left)
        {
            position = SkipWhitespace(text, position, end);
        }

        var dashes = 0;
        while (IsAt(text, ref position, end, '-'))
        {
            dashes++;
        }

        if (dashes == 0)
        {
            return false;
        }

        position = SkipWhitespace(text, position, end);
        var right = IsAt(text, ref position, end, ':');
        if (right)
        {
            position = SkipWhitespace(text, position, end);
        }

        if (position != end)
        {
            return false;
        }

        column = ((left, right) switch
        {
            (true, true) => MarkdownTableAlignment.Center,
            (true, false) => MarkdownTableAlignment.Left,
            (false, true) => MarkdownTableAlignment.Right,
            _ => MarkdownTableAlignment.None,
        }, dashes);
        return true;
    }

    // Whether the character, which a backslash can escape, is at the position, which moves after it
    private static bool IsAt(string text, ref int position, int end, char c)
    {
        if (position < end && text[position] == c)
        {
            position++;
            return true;
        }

        if (position + 1 < end && text[position] == '\\' && text[position + 1] == c)
        {
            position += 2;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Makes generic attributes of a delimiter row whitespace, with the text before them in their cell: Markdig reads the text
    /// before a pipe, which the attributes start, as an escaped character does. The attributes are given in the order of the
    /// row, and the characters before the previous ones are not searched again, so blanking the attributes of a row is linear.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct AttributesBlanker
    {
        // The characters before it were searched for a pipe or blanked, and the cell of the previous attributes starts at _cellStart
        private int _searchedEnd;
        private int _cellStart;

        public void Blank(Span<char> text, int start, int end)
        {
            // The cell starts after the last pipe before the attributes. Without a pipe since the previous attributes, it is the
            // cell of the previous attributes, which is blank up to them.
            var pipe = text[_searchedEnd..start].LastIndexOf('|');
            if (pipe >= 0)
            {
                _cellStart = _searchedEnd + pipe + 1;
            }

            text[Math.Max(_cellStart, _searchedEnd)..end].Fill(' ');
            _searchedEnd = end;
        }
    }

    /// <summary>Whether the characters of the line are the ones of a delimiter row: pipes, dashes, colons, whitespace, and the dashes and colons that a backslash escapes.</summary>
    public static bool IsDelimiterRowText(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (text[i] == '\\' && i + 1 < end && text[i + 1] is '-' or ':')
            {
                i++;
            }
            else if (text[i] is not ('|' or '-' or ':') && !IsWhitespace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsWhitespace(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (!IsWhitespace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    // The whitespace that Markdig trims around the cells: the Unicode whitespace of CommonMark
    public static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r' || (c >= '\u00A0' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);

    public static int SkipWhitespace(string text, int start, int end)
    {
        while (start < end && IsWhitespace(text[start]))
        {
            start++;
        }

        return start;
    }

    public static (int Start, int End) Trim(string text, int start, int end)
    {
        start = SkipWhitespace(text, start, end);
        while (end > start && IsWhitespace(text[end - 1]))
        {
            end--;
        }

        return (start, end);
    }
}
