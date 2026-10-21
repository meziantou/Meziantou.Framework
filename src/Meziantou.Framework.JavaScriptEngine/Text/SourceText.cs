namespace Meziantou.Framework.JavaScriptEngine.Text;

/// <summary>
/// The text of a script, a module or a function, with the line map of the positions: the lines are separated by the
/// ECMAScript line terminators (LF, CR, CRLF, U+2028, U+2029) and the columns count UTF-16 code units.
/// </summary>
internal sealed class SourceText
{
    private int[]? _lineStarts;

    public SourceText(string text, string? url = null, int lineOffset = 0, int columnOffset = 0)
    {
        Text = text;
        Url = url;
        LineOffset = lineOffset;
        ColumnOffset = columnOffset;
    }

    public string Text { get; }

    /// <summary>Gets the URL (or file name) of the source, used in the error locations and the stack traces.</summary>
    public string? Url { get; }

    /// <summary>Gets the 0-based line of the first character in its document (e.g. an inline script of an HTML page).</summary>
    public int LineOffset { get; }

    /// <summary>Gets the 0-based column of the first character in its document (only the first line is shifted).</summary>
    public int ColumnOffset { get; }

    public int Length => Text.Length;

    public char this[int index] => Text[index];

    /// <summary>Gets the 1-based line and column of an offset, in the document of the source (with the offsets applied).</summary>
    public (int Line, int Column) GetLineAndColumn(int offset)
    {
        var lineStarts = _lineStarts ??= ComputeLineStarts(Text);
        var line = Array.BinarySearch(lineStarts, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var column = offset - lineStarts[line];
        if (line == 0)
        {
            column += ColumnOffset;
        }

        return (line + 1 + LineOffset, column + 1);
    }

    private static int[] ComputeLineStarts(string text)
    {
        var result = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                result.Add(i + 1);
            }
            else if (c is '\n' or '\u2028' or '\u2029')
            {
                result.Add(i + 1);
            }
        }

        return [.. result];
    }
}
