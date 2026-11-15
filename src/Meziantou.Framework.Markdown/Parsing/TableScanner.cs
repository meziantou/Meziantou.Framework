namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>Reads the rows of the tables of GitHub Flavored Markdown, as the reference implementation (cmark-gfm) does.</summary>
internal static class TableScanner
{
    /// <summary>
    /// Reads a row: cells separated by pipes, with optional pipes around them. A pipe escaped by a backslash is part of its cell.
    /// Returns the number of cells, and adds their content without the whitespace around it, and the positions of the pipes.
    /// </summary>
    public static int ScanRow(string text, int start, int end, List<(int Start, int End)>? cells, List<int>? pipes)
    {
        var count = 0;
        var position = start;
        if (position < end && text[position] == '|')
        {
            pipes?.Add(position);
            position = SkipSpaces(text, position + 1, end);
        }

        while (position < end)
        {
            var cellStart = position;
            while (position < end && text[position] != '|')
            {
                position += text[position] == '\\' && position + 1 < end && text[position + 1] == '|' ? 2 : 1;
            }

            var pipe = position < end;
            if (position > cellStart || pipe)
            {
                count++;
                cells?.Add(Trim(text, cellStart, position));
            }

            if (!pipe)
            {
                break;
            }

            pipes?.Add(position);
            position = SkipSpaces(text, position + 1, end);
        }

        return count;
    }

    /// <summary>
    /// Reads a delimiter row: cells of hyphens with an optional colon on each side, separated by pipes, with optional pipes
    /// around them. Returns the number of cells, or 0 when the line is not a delimiter row.
    /// </summary>
    public static int ScanDelimiterRow(string text, int start, int end, List<(int Start, int End)>? cells, List<int>? pipes)
    {
        var count = 0;
        var position = start;
        if (position < end && text[position] == '|')
        {
            pipes?.Add(position);
            position++;
        }

        while (true)
        {
            position = SkipSpaces(text, position, end);
            var cellStart = position;
            if (position < end && text[position] == ':')
            {
                position++;
            }

            if (position >= end || text[position] != '-')
            {
                return 0;
            }

            while (position < end && text[position] == '-')
            {
                position++;
            }

            if (position < end && text[position] == ':')
            {
                position++;
            }

            cells?.Add((cellStart, position));
            count++;
            position = SkipSpaces(text, position, end);
            if (position == end)
            {
                return count;
            }

            if (text[position] != '|')
            {
                return 0;
            }

            pipes?.Add(position);
            position++;

            // A pipe after the last cell
            if (SkipSpaces(text, position, end) == end)
            {
                return count;
            }
        }
    }

    public static bool IsSpace(char c) => c is ' ' or '\t' or '\v' or '\f';

    private static int SkipSpaces(string text, int position, int end)
    {
        while (position < end && IsSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static (int Start, int End) Trim(string text, int start, int end)
    {
        while (start < end && IsSpace(text[start]))
        {
            start++;
        }

        while (end > start && IsSpace(text[end - 1]))
        {
            end--;
        }

        return (start, end);
    }
}
