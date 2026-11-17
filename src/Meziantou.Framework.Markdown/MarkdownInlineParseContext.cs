using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// The text where a <see cref="MarkdownInlineExtension"/> reads a construct, and where it reports the parts of the construct:
/// markers, text, and nested inlines. The positions are relative to the start of <see cref="Text"/>.
/// </summary>
/// <remarks>
/// The parts are reported in the order of the text, without overlapping. The characters of the construct that no part
/// covers are text. The node of the construct has every character of its range, so the tree still reproduces the document.
/// </remarks>
public ref struct MarkdownInlineParseContext
{
    private readonly List<ExtensionRange> _ranges;
    private readonly SyntaxExtensionStates? _states;
    private readonly int _index;
    private int _end;
    private int _length;

    internal MarkdownInlineParseContext(ReadOnlySpan<char> text, char previousCharacter, MarkdownParseOptions options, List<ExtensionRange> ranges, SyntaxExtensionStates? states, int index)
    {
        _states = states;
        _index = index;
        Text = text;
        PreviousCharacter = previousCharacter;
        Options = options;
        _ranges = ranges;
    }

    /// <summary>
    /// Gets the text from the trigger character to the end of the inlines of the block, such as a paragraph. The markers of the
    /// containers of the lines, such as the <c>&gt;</c> of a block quote, are not in it, and the lines end with <c>\n</c>.
    /// </summary>
    public readonly ReadOnlySpan<char> Text { get; }

    /// <summary>Gets the character before the trigger character, or <c>\0</c> at the start of the inlines.</summary>
    public readonly char PreviousCharacter { get; }

    /// <summary>Gets the options of the parser.</summary>
    public readonly MarkdownParseOptions Options { get; }

    /// <summary>
    /// Gets the state of the extension for the document, which <see cref="MarkdownSyntaxExtension.CreateState"/> creates: what
    /// the block extensions recorded in it, as every block of the document is read before the first inline.
    /// </summary>
    public readonly object? State => _states?.Get(_index);

    /// <summary>Gets the state of another extension of the options for the document, such as the block extension whose definitions an inline extension reads.</summary>
    /// <exception cref="ArgumentException"><paramref name="extension"/> is not in <see cref="MarkdownParseOptions.SyntaxExtensions"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public readonly object? GetState(MarkdownSyntaxExtension extension) => _states is null ? throw new ArgumentException("The options have no syntax extension.", nameof(extension)) : _states.Get(extension);

    /// <summary>Gets the length of the construct, once <see cref="Complete"/> is called, or 0.</summary>
    public readonly int Length => _length;

    /// <summary>Reports a marker of the construct, such as <c>@</c>, which the HTML does not have by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, is not in <see cref="Text"/>, or starts before the end of the previous part.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was called.</exception>
    public void AddMarker(int start, int length) => Add(ExtensionRangeKind.Marker, start, length);

    /// <summary>Reports text of the construct, such as the name of a mention, which the HTML has by default, escaped.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, is not in <see cref="Text"/>, or starts before the end of the previous part.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was called.</exception>
    public void AddText(int start, int length) => Add(ExtensionRangeKind.Text, start, length);

    /// <summary>
    /// Reports content that the parser reads as inlines, such as emphasis and links, on its own: its delimiters and its
    /// brackets do not match the ones outside. It cannot start the construct: the trigger character is a marker or text.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, starts the construct, is not in <see cref="Text"/>, or starts before the end of the previous part.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was called.</exception>
    public void AddInlines(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfZero(start);
        Add(ExtensionRangeKind.Inlines, start, length);
    }

    /// <summary>Ends the construct, which has the first <paramref name="length"/> characters of <see cref="Text"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive, is longer than <see cref="Text"/>, or ends before the last part.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was called.</exception>
    public void Complete(int length)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Text.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, _end);
        _length = length;
    }

    private void Add(ExtensionRangeKind kind, int start, int length)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfLessThan(start, _end);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Text.Length - start);
        _ranges.Add(new ExtensionRange(kind, start, length));
        _end = start + length;
    }

    private readonly void ThrowIfCompleted()
    {
        if (_length > 0)
        {
            throw new InvalidOperationException("The construct is complete.");
        }
    }
}
