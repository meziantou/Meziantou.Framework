namespace Meziantou.Framework.Markdown;

/// <summary>
/// An inline syntax that wraps content between runs of a character, such as <c>%%highlight%%</c>. The runs take part in
/// the processing of emphasis, as <see cref="MarkdownExtensions.Inserted"/> (<c>++</c>) and
/// <see cref="MarkdownExtensions.Marked"/> (<c>==</c>) do, so the construct nests with emphasis in both directions. The
/// construct is a <see cref="MarkdownExtensionInlineSyntax"/> whose markers are the runs and whose inlines are the content.
/// </summary>
/// <remarks>
/// A run opens or closes as a run of <c>*</c> does in CommonMark, a run shorter than <see cref="MinimumCount"/> is text,
/// and a match uses as many characters of the runs as it can, up to <see cref="MaximumCount"/>. The character must not be
/// one that an enabled extension of <see cref="MarkdownExtensions"/> reads, such as <c>~</c> with strikethrough: the parser
/// throws an <see cref="ArgumentException"/> for such options.
/// </remarks>
public sealed class MarkdownDelimiterExtension : MarkdownSyntaxExtension
{
    // The characters that CommonMark reads: escapes, code spans, emphasis, links, images, autolinks, HTML and entities
    private const string ReservedCharacters = "\\`*_[]!<&";

    /// <summary>Initializes a new instance of the <see cref="MarkdownDelimiterExtension"/> class.</summary>
    /// <param name="name">The name of the extension, which the nodes of its constructs have.</param>
    /// <param name="character">The character of the runs: ASCII punctuation that CommonMark does not read, such as <c>%</c>.</param>
    /// <param name="minimumCount">The length of the shortest run that is a delimiter.</param>
    /// <param name="maximumCount">The most characters of each run that one construct uses: <paramref name="minimumCount"/> or more.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or <paramref name="character"/> is not ASCII punctuation, or is one that CommonMark reads.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumCount"/> is not positive, or <paramref name="maximumCount"/> is less than <paramref name="minimumCount"/>.</exception>
    public MarkdownDelimiterExtension(string name, char character, int minimumCount = 2, int maximumCount = 2)
        : base(name)
    {
        if (!char.IsAsciiLetterOrDigit(character) && character is > ' ' and < '\x7f')
        {
            if (ReservedCharacters.Contains(character, StringComparison.Ordinal))
            {
                throw new ArgumentException($"The character '{character}' is read by CommonMark.", nameof(character));
            }
        }
        else
        {
            throw new ArgumentException($"The character U+{(int)character:X4} is not ASCII punctuation.", nameof(character));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, minimumCount);
        Character = character;
        MinimumCount = minimumCount;
        MaximumCount = maximumCount;
    }

    /// <summary>Gets the character of the runs.</summary>
    public char Character { get; }

    /// <summary>Gets the length of the shortest run that is a delimiter.</summary>
    public int MinimumCount { get; }

    /// <summary>Gets the most characters of each run that one construct uses.</summary>
    public int MaximumCount { get; }
}
