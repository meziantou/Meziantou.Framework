using System.Collections.ObjectModel;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents immutable regular-expression pattern text with line information and text-change application helpers.</summary>
public sealed class SourceText
{
    private SourceText(string text)
    {
        Text = text;
        Lines = BuildLines(text);
    }

    public string Text { get; }
    public int Length => Text.Length;
    public IReadOnlyList<TextLine> Lines { get; }

    public static SourceText From(string text) => new(text ?? string.Empty);

    public SourceText WithChanges(IEnumerable<RegexTextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var updatedText = Text;
        foreach (var change in changes.OrderByDescending(change => change.Span.Start))
        {
            if (change.Span.End > updatedText.Length)
                continue;

            updatedText = string.Concat(updatedText.AsSpan(0, change.Span.Start), change.NewText, updatedText.AsSpan(change.Span.End));
        }

        return new SourceText(updatedText);
    }

    /// <summary>Gets the line containing the specified character position.</summary>
    public TextLine GetLine(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        // A line's End stops before its line break, so a position is matched against where the next line starts
        // instead. Testing against End would put the second half of a CRLF on the following line.
        for (var index = 0; index < Lines.Count - 1; index++)
        {
            if (position < Lines[index + 1].Start)
                return Lines[index];
        }

        return Lines[^1];
    }

    /// <summary>Returns the source text.</summary>
    public override string ToString() => Text;

    private static ReadOnlyCollection<TextLine> BuildLines(string text)
    {
        if (text.Length == 0)
            return new ReadOnlyCollection<TextLine>([new TextLine(0, 0, 0, string.Empty)]);

        var lines = new List<TextLine>();
        var lineNumber = 0;
        var lineStart = 0;
        var index = 0;
        while (index < text.Length)
        {
            var lineBreakLength = GetLineBreakLength(text, index);
            if (lineBreakLength == 0)
            {
                index++;
                continue;
            }

            lines.Add(new TextLine(lineNumber, lineStart, index, text[lineStart..index]));
            lineNumber++;
            index += lineBreakLength;
            lineStart = index;
        }

        lines.Add(new TextLine(lineNumber, lineStart, text.Length, text[lineStart..]));

        return new ReadOnlyCollection<TextLine>(lines);
    }

    internal static int GetLineBreakLength(string text, int index)
    {
        var current = text[index];
        if (current == '\r')
            return index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;

        return current == '\n' ? 1 : 0;
    }
}
