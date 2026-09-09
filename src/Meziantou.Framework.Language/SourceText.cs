using System.Collections.ObjectModel;

namespace Meziantou.Framework.Language;

/// <summary>Represents immutable source text with line information and text-change application helpers.</summary>
public sealed class SourceText
{
    private readonly TextLine[] _lines;
    private readonly ReadOnlyCollection<TextLine> _linesView;

    private SourceText(string text)
    {
        Text = text;
        _lines = BuildLines(text);
        _linesView = new ReadOnlyCollection<TextLine>(_lines);
    }

    public string Text { get; }
    public int Length => Text.Length;
    public IReadOnlyList<TextLine> Lines => _linesView;

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SourceText From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SourceText(text);
    }

    /// <summary>
    /// Applies <paramref name="changes"/> from the end of the text backwards, so the spans of the changes that come
    /// earlier in the text stay valid. A change reaching past the end of the text is ignored.
    /// </summary>
    public SourceText WithChanges(IEnumerable<TextChange> changes)
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
    /// <remarks>
    /// <paramref name="position"/> may be <see cref="Length"/>, which is where a span that runs to the end of the
    /// text finishes; anything past that indexes no character and is rejected rather than reported as the last line.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is negative or past the end.</exception>
    public TextLine GetLine(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);

        // A line's End stops before its line break, so a position is matched against where the next line starts
        // instead. Testing against End would put the second half of a CRLF on the following line. The answer is
        // therefore the last line that starts at or before the position.
        var low = 0;
        var high = _lines.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (_lines[middle].Start <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _lines[low];
    }

    /// <summary>Returns the characters covered by <paramref name="span"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> reaches past the end of the text.</exception>
    public string ToString(TextSpan span)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, Text.Length, nameof(span));

        return Text.Substring(span.Start, span.Length);
    }

    /// <summary>Returns the text covered by <paramref name="span"/> as its own <see cref="SourceText"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> reaches past the end of the text.</exception>
    public SourceText GetSubText(TextSpan span) => From(ToString(span));

    /// <summary>
    /// Describes how <paramref name="oldText"/> was changed to produce this text, as spans of <paramref name="oldText"/>.
    /// </summary>
    /// <remarks>
    /// The result is derived from the two texts alone, by trimming the common prefix and the common suffix, so it is
    /// always a single range. It does not attempt to recover the individual edits that were originally applied.
    /// </remarks>
    /// <returns>An empty list when the texts are equal, otherwise the single range covering everything that differs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldText"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChangeRange> GetChangeRanges(SourceText oldText)
    {
        ArgumentNullException.ThrowIfNull(oldText);

        if (ReferenceEquals(this, oldText))
            return [];

        var oldSpan = oldText.Text.AsSpan();
        var newSpan = Text.AsSpan();
        if (oldSpan.SequenceEqual(newSpan))
            return [];

        var prefix = oldSpan.CommonPrefixLength(newSpan);

        // Never split a surrogate pair: neither half is text on its own, so an edit that changes one of them changes
        // the character both belong to.
        if (prefix > 0 && char.IsHighSurrogate(oldSpan[prefix - 1]))
        {
            prefix--;
        }

        var maxSuffix = Math.Min(oldSpan.Length, newSpan.Length) - prefix;
        var suffix = 0;
        while (suffix < maxSuffix && oldSpan[^(suffix + 1)] == newSpan[^(suffix + 1)])
        {
            suffix++;
        }

        if (suffix > 0 && char.IsLowSurrogate(oldSpan[^suffix]))
        {
            suffix--;
        }

        var changedOldSpan = TextSpan.FromBounds(prefix, oldSpan.Length - suffix);

        return [new TextChangeRange(changedOldSpan, newSpan.Length - suffix - prefix)];
    }

    public override string ToString() => Text;

    private static TextLine[] BuildLines(string text)
    {
        if (text.Length == 0)
            return [new TextLine(0, 0, 0, string.Empty)];

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

        return [.. lines];
    }

    internal static int GetLineBreakLength(string text, int index)
    {
        var current = text[index];
        if (current == '\r')
            return index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;

        return current == '\n' ? 1 : 0;
    }
}
