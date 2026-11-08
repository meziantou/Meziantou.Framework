namespace Meziantou.Framework.Pdf;

/// <summary>Hyphenates the words with the hyphenation patterns of a language, loaded by <see cref="Meziantou.Framework.Hyphenator"/>.</summary>
/// <remarks>No pattern is included in the library: the hyph-utf8 project of the TeX community publishes patterns for many languages, each under its own license.</remarks>
public sealed class PdfPatternHyphenator : IPdfHyphenator
{
    /// <summary>Initializes a new instance of the <see cref="PdfPatternHyphenator"/> class.</summary>
    /// <param name="hyphenator">The patterns of the language.</param>
    public PdfPatternHyphenator(Hyphenator hyphenator)
    {
        ArgumentNullException.ThrowIfNull(hyphenator);
        Hyphenator = hyphenator;
    }

    /// <summary>Gets the patterns of the language.</summary>
    public Hyphenator Hyphenator { get; }

    /// <inheritdoc/>
    public int Hyphenate(ReadOnlySpan<char> word, Span<int> breakPositions) => Hyphenator.Hyphenate(word, breakPositions);
}
