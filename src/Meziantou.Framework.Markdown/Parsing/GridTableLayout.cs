using System.Runtime.InteropServices;

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

    // The work for a line is bounded by its length, not by the number of columns: the columns that start after the end of the
    // line are not read, as they all belong to the cell that starts last on the line. Only the columns where a cell starts and
    // the cells that are open are tracked.
    private sealed class Reader(List<Column> columns, IReadOnlyList<string> lines)
    {
        public List<Row> Rows { get; } = [];

        // The columns where a cell starts on the current line and on the previous one, in order: each cell spans the columns up
        // to the next one, and the last one up to the end of the table
        private List<int> _starts = [];
        private List<int> _previousStarts = [];
        private int _lineCount;

        // The number of columns of the cell that starts in a column on the current line, valid when the line stamp of the
        // column is the current one: 0 when another cell covers the column
        private readonly int[] _spans = new int[columns.Count];
        private readonly int[] _spanStamps = new int[columns.Count];
        private int _stamp;

        // The cell of each column that is not added to a row yet or that continues in the next row, and the columns that have one
        private readonly Cell?[] _cells = new Cell?[columns.Count];
        private readonly List<int> _openColumns = [];

        // The rows already marked as the header by a line of =
        private int _headerRowCount;

        private static char PeekChar(string line, int index) => index >= 0 && index < line.Length ? line[index] : '\0';

        private int GetSpan(int columnIndex) => _spanStamps[columnIndex] == _stamp ? _spans[columnIndex] : 0;

        // A line that starts with +: the cells that it does not cross continue in the next row, and the rows before a line of
        // = are the header
        public void ReadSeparatorLine(int lineIndex)
        {
            var line = lines[lineIndex];
            var isHeaderRow = PeekChar(line, 1) == '=' || PeekChar(line, 2) == '=';
            var hasRowSpan = false;
            foreach (var columnIndex in _openColumns)
            {
                var column = columns[columnIndex];
                var cell = _cells[columnIndex]!;
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

            SetColumnSpans(line);
            EndRow(isLastRow: false);
            if (isHeaderRow)
            {
                for (; _headerRowCount < Rows.Count; _headerRowCount++)
                {
                    Rows[_headerRowCount].IsHeader = true;
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

        // Each | or + at the start of a column starts a cell, which spans the columns up to the next one. Only the columns that
        // start before the end of the line can have one.
        private void SetColumnSpans(string line)
        {
            (_previousStarts, _starts) = (_starts, _previousStarts);
            _starts.Clear();
            _lineCount++;
            _stamp++;
            for (var i = 0; i < columns.Count && columns[i].Start < line.Length; i++)
            {
                if (line[columns[i].Start] is '|' or '+')
                {
                    _starts.Add(i);
                }
            }

            for (var i = 0; i < _starts.Count; i++)
            {
                var columnIndex = _starts[i];
                _spans[columnIndex] = (i + 1 < _starts.Count ? _starts[i + 1] : columns.Count) - columnIndex;
                _spanStamps[columnIndex] = _stamp;
            }
        }

        // The cells of the columns are added to a row, unless they continue in the next row
        public void EndRow(bool isLastRow)
        {
            Row? row = null;
            _openColumns.Sort();
            var openCount = 0;
            for (var i = 0; i < _openColumns.Count; i++)
            {
                var columnIndex = _openColumns[i];
                var cell = _cells[columnIndex]!;
                row ??= new Row { Line = cell.Segments.Count > 0 ? cell.Segments[0].Line : -1 };
                if (!cell.IsInRow)
                {
                    cell.IsInRow = true;
                    row.Cells.Add(cell);
                }

                // Markdig does not close a cell that the last line spans: the grid table that ends it is kept
                if (isLastRow && !cell.AllowClose)
                {
                    cell.IsClosed = false;
                }

                if (isLastRow || GetSpan(columnIndex) == 0 || cell.AllowClose)
                {
                    _cells[columnIndex] = null;
                }
                else
                {
                    _openColumns[openCount++] = columnIndex;
                }
            }

            _openColumns.RemoveRange(openCount, _openColumns.Count - openCount);
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

            // The cells start in the first column, else the line has none
            if (_starts is not [0, ..])
            {
                return;
            }

            for (var startIndex = 0; startIndex < _starts.Count; startIndex++)
            {
                var i = _starts[startIndex];
                var column = columns[i];
                var nextColumnIndex = startIndex + 1 < _starts.Count ? _starts[startIndex + 1] : columns.Count;
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
                    if (_cells[i] is not { } cell)
                    {
                        cell = new Cell(i, nextColumnIndex - i);
                        _cells[i] = cell;
                        _openColumns.Add(i);
                    }

                    cell.Segments.Add((lineIndex, start, end));
                }
            }
        }

        // The cells of the line start in the same columns as the ones of the previous line
        private bool CanContinueRow() => _lineCount > 1 && CollectionsMarshal.AsSpan(_starts).SequenceEqual(CollectionsMarshal.AsSpan(_previousStarts));

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
