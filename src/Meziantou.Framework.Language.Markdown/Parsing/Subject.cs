using System.Text;

namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>
/// The text of the inlines of a leaf block: its lines joined by <c>\n</c>, without the markers of the containers and the
/// indentation that are between them in the source. Each character knows where it is in the source, so the tokens found in
/// the subject are ranges of the source.
/// </summary>
internal sealed class Subject
{
    private Subject(string text, int[] sourceStart, int[] sourceEnd)
    {
        Text = text;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
    }

    public string Text { get; }

    // Where each character starts and ends in the source: a line break is the whole line ending (\r\n)
    public int[] SourceStart { get; }

    public int[] SourceEnd { get; }

    public int Length => Text.Length;

    public static Subject FromLines(string source, IReadOnlyList<Line> lines)
    {
        var length = lines.Count - 1;
        foreach (var line in lines)
        {
            length += line.End - line.Start;
        }

        var builder = new StringBuilder(length);
        var starts = new List<int>(length);
        var ends = new List<int>(length);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            for (var j = line.Start; j < line.End; j++)
            {
                builder.Append(source[j]);
                starts.Add(j);
                ends.Add(j + 1);
            }

            if (i < lines.Count - 1)
            {
                builder.Append('\n');
                starts.Add(line.End);
                ends.Add(line.EolEnd);
            }
        }

        return new Subject(builder.ToString(), [.. starts], [.. ends]);
    }

    /// <summary>
    /// The content of a cell of a table, where a pipe escaped by a backslash is a pipe: GFM removes the backslash before it
    /// parses the inlines, even in a code span. The pipe starts at its backslash in the source, so that the tokens cover it.
    /// </summary>
    public static Subject FromTableCell(string source, int start, int end)
    {
        var builder = new StringBuilder(end - start);
        var starts = new List<int>(end - start);
        var ends = new List<int>(end - start);
        for (var i = start; i < end; i++)
        {
            var sourceStart = i;
            if (source[i] == '\\' && i + 1 < end && source[i + 1] == '|')
            {
                i++;
            }

            builder.Append(source[i]);
            starts.Add(sourceStart);
            ends.Add(i + 1);
        }

        return new Subject(builder.ToString(), [.. starts], [.. ends]);
    }

    public int ToSourceStart(int index) => index < SourceStart.Length ? SourceStart[index] : SourceEnd[^1];

    public int ToSourceEnd(int endIndex) => endIndex == 0 ? SourceStart[0] : SourceEnd[endIndex - 1];
}
