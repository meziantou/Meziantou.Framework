using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// The lines of a paragraph that a <see cref="MarkdownParagraphExtension"/> reads, and where it reports the parts of its block:
/// markers, text and inlines, each on one line. The positions are relative to the start of their line.
/// </summary>
/// <remarks>
/// The parts are reported in the order of the text, without overlapping. The characters of the lines of the block that no part
/// covers are text, except the whitespace around the parts. The node of the block has every character of its lines, so the
/// tree still reproduces the document.
/// </remarks>
public ref struct MarkdownParagraphConvertContext
{
    private readonly string _text;
    private readonly ReadOnlySpan<Line> _lines;
    private readonly List<ParagraphExtensionRange> _ranges;
    private readonly SyntaxExtensionStates? _states;
    private readonly int _index;
    private int _endLine;
    private int _end;
    private int _firstLine;
    private int _lineCount;

    internal MarkdownParagraphConvertContext(string text, ReadOnlySpan<Line> lines, MarkdownParseOptions options, List<ParagraphExtensionRange> ranges, SyntaxExtensionStates? states, int index)
    {
        _text = text;
        _lines = lines;
        Options = options;
        _ranges = ranges;
        _states = states;
        _index = index;
    }

    /// <summary>Gets the number of lines of the paragraph.</summary>
    public readonly int LineCount => _lines.Length;

    /// <summary>Gets the options of the parser.</summary>
    public readonly MarkdownParseOptions Options { get; }

    /// <summary>Gets the state of the extension for the document, which <see cref="MarkdownSyntaxExtension.CreateState"/> creates.</summary>
    public readonly object? State => _states?.Get(_index);

    /// <summary>Gets the state of another extension of the options for the document, such as the block extension whose definitions an inline extension reads.</summary>
    /// <exception cref="ArgumentException"><paramref name="extension"/> is not in <see cref="MarkdownParseOptions.SyntaxExtensions"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public readonly object? GetState(MarkdownSyntaxExtension extension) => _states is null ? throw new ArgumentException("The options have no syntax extension.", nameof(extension)) : _states.Get(extension);

    /// <summary>Gets the first line of the block, once <see cref="Complete"/> is called.</summary>
    public readonly int FirstLine => _firstLine;

    /// <summary>Gets the number of lines of the block, once <see cref="Complete"/> is called, or 0.</summary>
    public readonly int CompletedLineCount => _lineCount;

    /// <summary>Gets a line of the paragraph, without its line ending, and without the markers of its containers.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative, or not less than <see cref="LineCount"/>.</exception>
    public readonly ReadOnlySpan<char> GetLine(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _lines.Length);
        var line = _lines[index];
        return _text.AsSpan(line.Start, line.End - line.Start);
    }

    /// <summary>Reports a marker of the block, which the HTML does not have by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, is not in its line, or starts before the end of the previous part.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was called.</exception>
    public void AddMarker(int line, int start, int length) => Add(ExtensionRangeKind.Marker, line, start, length);

    /// <summary>Reports text of the block, which the HTML has by default, escaped.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, is not in its line, or starts before the end of the previous part.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was called.</exception>
    public void AddText(int line, int start, int length) => Add(ExtensionRangeKind.Text, line, start, length);

    /// <summary>Reports content that the parser reads as inlines, such as emphasis and links. It cannot be the first part of the block: report a marker or text first.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, is not in its line, or starts before the end of the previous part.</exception>
    /// <exception cref="InvalidOperationException">No marker or text is reported before, or <see cref="Complete"/> was called.</exception>
    public void AddInlines(int line, int start, int length)
    {
        if (_ranges.Count == 0)
        {
            throw new InvalidOperationException("The first part of a block is a marker or text.");
        }

        Add(ExtensionRangeKind.Inlines, line, start, length);
    }

    /// <summary>Ends the block, which has the lines from <paramref name="firstLine"/>: the lines before stay a paragraph, and the lines after are offered to the converters again.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The lines are not lines of the paragraph, or a part is not in them.</exception>
    /// <exception cref="InvalidOperationException">No part is reported, or <see cref="Complete"/> was called.</exception>
    public void Complete(int firstLine, int lineCount)
    {
        ThrowIfCompleted();
        if (_ranges.Count == 0)
        {
            throw new InvalidOperationException("The block has no part: report a marker or text.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(firstLine);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lineCount, _lines.Length - firstLine);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstLine, _ranges[0].Line);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstLine + lineCount, _endLine + 1, nameof(lineCount));
        _firstLine = firstLine;
        _lineCount = lineCount;
    }

    private void Add(ExtensionRangeKind kind, int line, int start, int length)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, _lines.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, _endLine);
        if (line == _endLine)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(start, _end);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var lineLength = _lines[line].End - _lines[line].Start;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, lineLength - start);
        _ranges.Add(new ParagraphExtensionRange(kind, line, start, length));
        _endLine = line;
        _end = start + length;
    }

    private readonly void ThrowIfCompleted()
    {
        if (_lineCount > 0)
        {
            throw new InvalidOperationException("The block is complete.");
        }
    }
}
