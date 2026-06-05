using System.Collections.ObjectModel;

namespace Meziantou.Framework.Xml;

/// <summary>Represents immutable XML text with line information and text-change application helpers.</summary>
/// <example>
/// <code>
/// var source = SourceText.From("&lt;root/&gt;");
/// var updated = source.WithChanges([new XmlTextChange(new TextSpan(1, 4), "item")]);
/// </code>
/// </example>
public sealed class SourceText
{
    private readonly IReadOnlyList<TextLine> _lines;

    private SourceText(string text)
    {
        Text = text;
        _lines = BuildLines(text);
    }

    public string Text { get; }
    public int Length => Text.Length;
    public IReadOnlyList<TextLine> Lines => _lines;

    public static SourceText From(string text)
    {
        return new SourceText(text ?? string.Empty);
    }

    public SourceText WithChanges(IEnumerable<XmlTextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var updatedText = Text;
        foreach (var change in changes.OrderByDescending(change => change.Span.Start))
        {
            if (change.Span.Start < 0 || change.Span.End > updatedText.Length)
                continue;

            updatedText = string.Concat(updatedText.AsSpan(0, change.Span.Start), change.NewText, updatedText.AsSpan(change.Span.End));
        }

        return new SourceText(updatedText);
    }

    private static ReadOnlyCollection<TextLine> BuildLines(string text)
    {
        if (text.Length == 0)
            return new ReadOnlyCollection<TextLine>([new(0, 0, 0, string.Empty)]);

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

        if (lineStart <= text.Length)
        {
            lines.Add(new TextLine(lineNumber, lineStart, text.Length, text[lineStart..]));
        }

        return new ReadOnlyCollection<TextLine>(lines);
    }

    private static int GetLineBreakLength(string text, int index)
    {
        var current = text[index];
        if (current == '\r')
            return index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;

        return current == '\n' ? 1 : 0;
    }
}
