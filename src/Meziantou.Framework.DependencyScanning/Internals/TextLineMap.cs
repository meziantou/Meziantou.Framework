namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>Maps character offsets in a text to 1-based line and column numbers.</summary>
/// <remarks>
/// <c>\r\n</c>, <c>\r</c> and <c>\n</c> each end a line, which is the rule <see cref="TextReader.ReadLine"/> and the YAML
/// parser use. Every location computed or applied on text must follow it, or it points at the wrong line.
/// </remarks>
internal sealed class TextLineMap
{
    private readonly int[] _lineStarts;

    public TextLineMap(string text)
    {
        TextLength = text.Length;

        var lineStarts = new List<int> { 0 };
        var index = 0;
        while ((index = GetNextLineStart(text, index)) >= 0)
        {
            lineStarts.Add(index);
        }

        _lineStarts = [.. lineStarts];
    }

    public int TextLength { get; }

    /// <summary>Gets the offset of the first character of the 1-based <paramref name="line"/>, or -1 when the text has fewer lines.</summary>
    public int GetLineStart(int line)
    {
        if (line < 1 || line > _lineStarts.Length)
            return -1;

        return _lineStarts[line - 1];
    }

    /// <summary>Gets the 1-based line and column of the character at <paramref name="index"/>.</summary>
    public (int Line, int Column) GetLinePosition(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, TextLength);

        var lineIndex = Array.BinarySearch(_lineStarts, index);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }

        return (lineIndex + 1, index - _lineStarts[lineIndex] + 1);
    }

    /// <summary>Gets the offset of the line break that ends the line containing <paramref name="index"/>, or the text length for the last line.</summary>
    public static int GetLineEnd(string text, int index)
    {
        var lineBreak = text.AsSpan(index).IndexOfAny('\r', '\n');
        return lineBreak < 0 ? text.Length : index + lineBreak;
    }

    /// <summary>Gets the offset of the line that follows the one containing <paramref name="index"/>, or -1 when it is the last line.</summary>
    private static int GetNextLineStart(string text, int index)
    {
        var lineEnd = GetLineEnd(text, index);
        if (lineEnd == text.Length)
            return -1;

        if (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n')
            return lineEnd + 2;

        return lineEnd + 1;
    }
}
