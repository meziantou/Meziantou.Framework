using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>A block of a <see cref="MarkdownFencedBlockExtension"/> that is closed, for <see cref="MarkdownFencedBlockExtension.OnClosed"/>.</summary>
public readonly ref struct MarkdownFencedBlockContext
{
    private readonly string _text;
    private readonly ReadOnlySpan<Line> _lines;
    private readonly SyntaxExtensionStates? _states;
    private readonly int _index;

    internal MarkdownFencedBlockContext(string text, ReadOnlySpan<char> openingFence, ReadOnlySpan<char> info, ReadOnlySpan<Line> lines, MarkdownParseOptions options, SyntaxExtensionStates? states, int index)
    {
        _text = text;
        OpeningFence = openingFence;
        Info = info;
        _lines = lines;
        Options = options;
        _states = states;
        _index = index;
    }

    /// <summary>Gets the opening fence.</summary>
    public ReadOnlySpan<char> OpeningFence { get; }

    /// <summary>Gets the text after the opening fence, without the whitespace around it.</summary>
    public ReadOnlySpan<char> Info { get; }

    /// <summary>Gets the options of the parser.</summary>
    public MarkdownParseOptions Options { get; }

    /// <summary>Gets the number of lines between the fences, for <see cref="MarkdownFencedBlockContent.RawLines"/>, or 0.</summary>
    public int LineCount => _lines.Length;

    /// <summary>Gets the state of the extension for the document, which <see cref="MarkdownSyntaxExtension.CreateState"/> creates.</summary>
    public object? State => _states?.Get(_index);

    /// <summary>Gets the state of another extension of the options for the document, such as the block extension whose definitions an inline extension reads.</summary>
    /// <exception cref="ArgumentException"><paramref name="extension"/> is not in <see cref="MarkdownParseOptions.SyntaxExtensions"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public object? GetState(MarkdownSyntaxExtension extension) => _states is null ? throw new ArgumentException("The options have no syntax extension.", nameof(extension)) : _states.Get(extension);

    /// <summary>Gets a line between the fences, without its line ending, and without the markers of its containers.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative, or not less than <see cref="LineCount"/>.</exception>
    public ReadOnlySpan<char> GetLine(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _lines.Length);
        var line = _lines[index];
        return _text.AsSpan(line.Start, line.End - line.Start);
    }
}
