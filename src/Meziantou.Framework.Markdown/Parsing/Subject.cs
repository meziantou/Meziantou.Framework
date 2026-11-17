using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The text of the inlines of a leaf block: its lines joined by <c>\n</c>, without the markers of the containers and the
/// indentation that are between them in the source. Each character knows where it is in the source, so the tokens found in
/// the subject are ranges of the source. The characters of the subject are the ones of <see cref="Text"/> from
/// <see cref="Start"/> to <see cref="End"/>.
/// </summary>
/// <remarks>
/// <para>
/// When the lines follow each other in the source, each one ended by <c>\n</c>, as the lines of most paragraphs and headings
/// outside containers do, the text is the source and the positions are the ones of the source: the subject copies nothing.
/// </para>
/// <para>
/// Otherwise the text is a copy, and the positions are kept by segment, a run of characters that are consecutive in the
/// source: a line, or the line ending between two lines. Most subjects have a few of them.
/// </para>
/// </remarks>
internal sealed class Subject
{
    // The segments of a copy, or null when the text is the source
    private readonly Segment[]? _segments;

    // Where a subject without characters is in the source
    private readonly int _emptyPosition;

    // The segment of the last position asked for: the tokens are asked for in the order of the text
    private int _lastSegment;

    private Subject(string text, int start, int end, Segment[]? segments, int emptyPosition)
    {
        Text = text;
        Start = start;
        End = end;
        _segments = segments;
        _emptyPosition = emptyPosition;
    }

    public string Text { get; }

    /// <summary>Gets where the subject starts in <see cref="Text"/>.</summary>
    public int Start { get; }

    /// <summary>Gets where the subject ends in <see cref="Text"/>.</summary>
    public int End { get; }

    /// <summary>Gets the characters of the subject from <paramref name="start"/> to its end.</summary>
    public ReadOnlySpan<char> From(int start) => Text.AsSpan(start, End - start);

    public static Subject FromLines(string source, ReadOnlySpan<Line> lines)
    {
        if (IsContiguous(source, lines))
        {
            return new Subject(source, lines[0].Start, lines[^1].End, segments: null, lines[0].Start);
        }

        var length = lines.Length - 1;
        var segmentCount = 0;
        foreach (var line in lines)
        {
            length += line.End - line.Start;
            segmentCount += line.End > line.Start ? 2 : 1;
        }

        var segments = new Segment[Math.Max(segmentCount - 1, 0)];
        var segmentIndex = 0;
        var subjectIndex = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.End > line.Start)
            {
                segments[segmentIndex++] = new Segment(subjectIndex, line.Start, line.End - line.Start, StartAdjust: 0, EndAdjust: 0);
                subjectIndex += line.End - line.Start;
            }

            // The line break is the whole line ending of the source: \r\n is one character of the subject
            if (i < lines.Length - 1)
            {
                segments[segmentIndex++] = new Segment(subjectIndex, line.End, 1, StartAdjust: 0, EndAdjust: line.EolEnd - line.End - 1);
                subjectIndex++;
            }
        }

        string text;
        if (lines.Length == 1)
        {
            text = source.Substring(lines[0].Start, lines[0].End - lines[0].Start);
        }
        else
        {
            var buffer = ArrayPool<char>.Shared.Rent(Math.Max(length, 0));
            var position = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                source.AsSpan(line.Start, line.End - line.Start).CopyTo(buffer.AsSpan(position));
                position += line.End - line.Start;
                if (i < lines.Length - 1)
                {
                    buffer[position++] = '\n';
                }
            }

            text = new string(buffer, 0, position);
            ArrayPool<char>.Shared.Return(buffer);
        }

        return new Subject(text, 0, text.Length, segmentIndex == segments.Length ? segments : segments[..segmentIndex], lines.IsEmpty ? 0 : lines[0].Start);
    }

    // Whether the lines follow each other in the source, each one ended by \n: the subject is then the source between them
    private static bool IsContiguous(string source, ReadOnlySpan<Line> lines)
    {
        if (lines.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < lines.Length - 1; i++)
        {
            var line = lines[i];
            if (line.EolEnd != line.End + 1 || source[line.End] != '\n' || lines[i + 1].Start != line.EolEnd)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The content of a cell of a table, where a pipe escaped by a backslash is a pipe: GFM removes the backslash before it
    /// parses the inlines, even in a code span. The pipe starts at its backslash in the source, so that the tokens cover it.
    /// </summary>
    public static Subject FromTableCell(string source, int start, int end)
    {
        var content = source.AsSpan(start, end - start);
        if (!content.Contains("\\|", StringComparison.Ordinal))
        {
            return new Subject(content.ToString(), 0, end - start, [new Segment(0, start, end - start, StartAdjust: 0, EndAdjust: 0)], start);
        }

        var text = new char[end - start];
        var segments = new List<Segment>();
        var length = 0;
        var segmentStart = start;
        var startAdjust = 0;
        for (var i = start; i < end; i++)
        {
            if (source[i] == '\\' && i + 1 < end && source[i + 1] == '|')
            {
                // The pipe starts a segment, which starts at the backslash
                if (i > segmentStart)
                {
                    segments.Add(new Segment(length - (i - segmentStart), segmentStart, i - segmentStart, startAdjust, EndAdjust: 0));
                }

                i++;
                segmentStart = i;
                startAdjust = 1;
            }

            text[length++] = source[i];
        }

        segments.Add(new Segment(length - (end - segmentStart), segmentStart, end - segmentStart, startAdjust, EndAdjust: 0));
        return new Subject(new string(text, 0, length), 0, length, [.. segments], start);
    }

    /// <summary>Where the character at <paramref name="index"/> starts in the source, or where the subject ends.</summary>
    public int ToSourceStart(int index)
    {
        // The text of a subject without segments is the source
        if (_segments is null)
        {
            return Math.Min(index, End);
        }

        if (index >= End)
        {
            return End == 0 ? _emptyPosition : ToSourceEnd(End);
        }

        var segment = FindSegment(index);
        return segment.SourceStart + (index - segment.SubjectStart) - (index == segment.SubjectStart ? segment.StartAdjust : 0);
    }

    /// <summary>Where the character before <paramref name="endIndex"/> ends in the source.</summary>
    public int ToSourceEnd(int endIndex)
    {
        if (_segments is null)
        {
            return Math.Min(endIndex, End);
        }

        if (endIndex == 0)
        {
            return ToSourceStart(0);
        }

        var index = endIndex - 1;
        var segment = FindSegment(index);
        return segment.SourceStart + (index - segment.SubjectStart) + 1 + (index == segment.SubjectStart + segment.Length - 1 ? segment.EndAdjust : 0);
    }

    private Segment FindSegment(int index)
    {
        var segments = _segments!;
        var last = segments[_lastSegment];
        if (index >= last.SubjectStart && index < last.SubjectStart + last.Length)
        {
            return last;
        }

        var low = 0;
        var high = segments.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (segments[middle].SubjectStart <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        _lastSegment = low;
        return segments[low];
    }

    /// <param name="StartAdjust">How many characters of the source before the first character belong to it: the backslash of an escaped pipe.</param>
    /// <param name="EndAdjust">How many characters of the source after the last character belong to it: the \n of a \r\n.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(int SubjectStart, int SourceStart, int Length, int StartAdjust, int EndAdjust);
}
