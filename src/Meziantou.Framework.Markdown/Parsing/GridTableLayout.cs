namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The columns, the rows and the cells of a grid table, read from its lines as the grid table parser of Meziantou.Framework.Markdown
/// 1.x (Markdig) reads them. A line starts with the first character of the table: <c>+</c> or <c>|</c>.
/// </summary>
internal sealed class GridTableLayout
{
    private GridTableLayout(List<Column> columns, List<Row> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public List<Column> Columns { get; }

    public List<Row> Rows { get; }

    /// <summary>
    /// Reads the columns of the first line of a table, <c>+---+:--:+</c>: each <c>+</c> starts a column, whose dashes can have
    /// colons and whitespace around them. The last <c>+</c> can be omitted. Returns <see langword="null"/> when the line is not one.
    /// </summary>
    public static List<Column>? ReadColumns(ReadOnlySpan<char> line)
    {
        List<Column>? columns = null;
        var position = 0;
        while (position < line.Length && line[position] == '+')
        {
            var columnStart = position;
            position = SkipWhitespace(line, position + 1);
            if (position == line.Length)
            {
                break;
            }

            if (!TryReadColumnHeader(line, ref position, out var alignment))
            {
                return null;
            }

            (columns ??= []).Add(new Column(columnStart, position, alignment));
        }

        return position < line.Length ? null : columns;
    }

    // An optional colon, dashes and an optional colon, with whitespace around them
    private static bool TryReadColumnHeader(ReadOnlySpan<char> line, ref int position, out MarkdownTableAlignment alignment)
    {
        alignment = MarkdownTableAlignment.None;
        position = SkipWhitespace(line, position);
        var left = position < line.Length && line[position] == ':';
        if (left)
        {
            position++;
        }

        position = SkipWhitespace(line, position);
        var dashStart = position;
        while (position < line.Length && line[position] == '-')
        {
            position++;
        }

        if (position == dashStart)
        {
            return false;
        }

        position = SkipWhitespace(line, position);
        var right = position < line.Length && line[position] == ':';
        if (right)
        {
            position++;
        }

        position = SkipWhitespace(line, position);
        alignment = (left, right) switch
        {
            (true, true) => MarkdownTableAlignment.Center,
            (true, false) => MarkdownTableAlignment.Left,
            (false, true) => MarkdownTableAlignment.Right,
            _ => MarkdownTableAlignment.None,
        };
        return true;
    }

    private static int SkipWhitespace(ReadOnlySpan<char> line, int position)
    {
        while (position < line.Length && PipeTableScanner.IsWhitespace(line[position]))
        {
            position++;
        }

        return position;
    }

    /// <summary>
    /// Reads the rows of a table, whose first line gives the columns: the next lines start with <c>+</c>, which ends the
    /// cells that it separates, or with <c>|</c>, which separates the cells of a row. Returns <see langword="null"/> when the
    /// lines are not a table: a row has more columns than the table, or a cell spans more rows than the table has.
    /// </summary>
    /// <param name="force">Whether the rows are read even when the lines are not a table, as Markdig keeps a grid table that ends a cell.</param>
    public static GridTableLayout? Read(IReadOnlyList<string> lines, bool force = false)
    {
        if (lines.Count == 0 || ReadColumns(lines[0]) is not { } columns)
        {
            return null;
        }

        var reader = new Reader(columns, lines);
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i] is ['+', ..])
            {
                reader.ReadSeparatorLine(i);
            }
            else
            {
                reader.ReadContentLine(i, isRowLine: false);
            }
        }

        reader.EndRow(isLastRow: true);
        return force || reader.IsValid() ? new GridTableLayout(columns, reader.Rows) : null;
    }

    /// <summary>A column: where its <c>+</c> is, where the next one is, and its alignment.</summary>
    internal sealed class Column(int start, int end, MarkdownTableAlignment alignment)
    {
        public int Start { get; } = start;

        public int End { get; } = end;

        public MarkdownTableAlignment Alignment { get; } = alignment;

        // The number of columns of the cell that starts in the column on the current line, which is 0 when another cell covers it
        public int CurrentColumnSpan { get; set; } = -1;

        public int PreviousColumnSpan { get; set; }

        public Cell? CurrentCell { get; set; }
    }

    internal sealed class Row
    {
        public bool IsHeader { get; set; }

        // The line where Markdig starts the row: the first line of its first cell in the order of the columns, which can be a cell
        // of a row above that spans it
        public int Line { get; set; } = -1;

        public List<Cell> Cells { get; } = [];
    }

    /// <summary>A cell: its first column, the columns and the rows it spans, and its text, a range of each of its lines.</summary>
    internal sealed class Cell(int columnIndex, int columnSpan)
    {
        public int ColumnIndex { get; } = columnIndex;

        public int ColumnSpan { get; } = columnSpan;

        public int RowSpan { get; set; } = 1;

        public bool AllowClose { get; set; } = true;

        public bool IsInRow { get; set; }

        public bool IsClosed { get; set; } = true;

        public List<(int Line, int Start, int End)> Segments { get; } = [];
    }

    private sealed class Reader(List<Column> columns, IReadOnlyList<string> lines)
    {
        public List<Row> Rows { get; } = [];

        private static char PeekChar(string line, int index) => index >= 0 && index < line.Length ? line[index] : '\0';

        // A line that starts with +: the cells that it does not cross continue in the next row, and the rows before a line of
        // = are the header
        public void ReadSeparatorLine(int lineIndex)
        {
            var line = lines[lineIndex];
            var isHeaderRow = PeekChar(line, 1) == '=' || PeekChar(line, 2) == '=';
            var hasRowSpan = false;
            foreach (var column in columns)
            {
                if (column.CurrentCell is { } cell)
                {
                    var (start, end) = PipeTableScanner.Trim(line, Math.Min(column.Start + 1, line.Length), Math.Min(column.End, line.Length));
                    if (end <= start || !IsRowSeparator(line.AsSpan(start, end - start)))
                    {
                        hasRowSpan = true;
                        cell.RowSpan++;
                        cell.AllowClose = false;
                    }
                    else
                    {
                        cell.AllowClose = true;
                    }
                }
            }

            SetColumnSpans(line);
            EndRow(isLastRow: false);
            if (isHeaderRow)
            {
                foreach (var row in Rows)
                {
                    row.IsHeader = true;
                }
            }

            if (hasRowSpan)
            {
                ReadContentLine(lineIndex, isRowLine: true);
            }
        }

        private static bool IsRowSeparator(ReadOnlySpan<char> text)
        {
            foreach (var c in text)
            {
                if (c is not ('-' or '=' or ':'))
                {
                    return false;
                }
            }

            return true;
        }

        // Each | or + at the start of a column starts a cell, which spans the columns up to the next one
        private void SetColumnSpans(string line)
        {
            foreach (var column in columns)
            {
                column.PreviousColumnSpan = column.CurrentColumnSpan;
                column.CurrentColumnSpan = 0;
            }

            var columnIndex = -1;
            for (var i = 0; i < columns.Count; i++)
            {
                if (PeekChar(line, columns[i].Start) is '|' or '+')
                {
                    columnIndex = i;
                }

                if (columnIndex >= 0)
                {
                    columns[columnIndex].CurrentColumnSpan++;
                }
            }
        }

        // The cells of the columns are added to a row, unless they continue in the next row
        public void EndRow(bool isLastRow)
        {
            Row? row = null;
            foreach (var column in columns)
            {
                if (column.CurrentCell is { } cell)
                {
                    row ??= new Row { Line = cell.Segments.Count > 0 ? cell.Segments[0].Line : -1 };
                    if (!cell.IsInRow)
                    {
                        cell.IsInRow = true;
                        row.Cells.Add(cell);
                    }
                }

                // Markdig does not close a cell that the last line spans: the grid table that ends it is kept
                if (isLastRow && column.CurrentCell is { AllowClose: false } openCell)
                {
                    openCell.IsClosed = false;
                }

                if (isLastRow || column.CurrentColumnSpan == 0 || column.CurrentCell is { AllowClose: true })
                {
                    column.CurrentCell = null;
                }
            }

            if (row is { Cells.Count: > 0 })
            {
                Rows.Add(row);
            }
        }

        // The text of each cell of the line, from after its | to before the next one. On a line that starts with +, only the
        // cells that continue have text.
        public void ReadContentLine(int lineIndex, bool isRowLine)
        {
            var line = lines[lineIndex];
            SetColumnSpans(line);
            if (!isRowLine && !CanContinueRow())
            {
                EndRow(isLastRow: false);
            }

            for (var i = 0; i < columns.Count;)
            {
                var column = columns[i];
                var nextColumnIndex = i + column.CurrentColumnSpan;
                if (nextColumnIndex == i)
                {
                    break;
                }

                var start = column.Start + 1;
                int end;
                if (nextColumnIndex < columns.Count)
                {
                    end = columns[nextColumnIndex].Start;
                }
                else
                {
                    // The last cell ends at the end of the table when it has a | there, else at the | that ends the line, else
                    // with the line
                    var columnEnd = columns[^1].End;
                    var columnEndChar = PeekChar(line, columnEnd);
                    end = columnEndChar == '|' || (isRowLine && columnEndChar == '+') ? columnEnd : line is [.., '|'] ? line.Length - 1 : line.Length;
                }

                end = Math.Min(end, line.Length);
                while (end > start && PipeTableScanner.IsWhitespace(line[end - 1]))
                {
                    end--;
                }

                end = Math.Max(start, end);
                start = Math.Min(start, end);
                // On a line that starts with +, the dashes of a cell, or no text, separate the rows
                if (!isRowLine || !IsRowSeparator(line.AsSpan(start, end - start)))
                {
                    column.CurrentCell ??= new Cell(i, column.CurrentColumnSpan);
                    column.CurrentCell.Segments.Add((lineIndex, start, end));
                }

                i = nextColumnIndex;
            }
        }

        private bool CanContinueRow()
        {
            foreach (var column in columns)
            {
                if (column.PreviousColumnSpan != column.CurrentColumnSpan)
                {
                    return false;
                }
            }

            return true;
        }

        // Every row has at most as many columns as the table, with the cells of the rows above that span it
        public bool IsValid()
        {
            if (Rows.Count == 0)
            {
                return false;
            }

            var counts = new int[Rows.Count];
            for (var i = 0; i < Rows.Count; i++)
            {
                foreach (var cell in Rows[i].Cells)
                {
                    counts[i] += cell.ColumnSpan;
                    for (var rowSpan = cell.RowSpan - 1; rowSpan > 0; rowSpan--)
                    {
                        if (i + rowSpan > Rows.Count - 1)
                        {
                            return false;
                        }

                        counts[i + rowSpan] += cell.ColumnSpan;
                    }
                }

                if (counts[i] > columns.Count)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
