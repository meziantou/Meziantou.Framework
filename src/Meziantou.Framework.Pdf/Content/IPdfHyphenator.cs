namespace Meziantou.Framework.Pdf;

/// <summary>Finds the positions where a word can be hyphenated, for the text boxes and the tables of the content builder.</summary>
/// <remarks>
/// <para>
/// The content builder calls the hyphenator for the word that does not fit at the end of a line, and breaks the word at
/// the last position where the line, ended by a hyphen, fits. <see cref="PdfPatternHyphenator"/> uses the hyphenation
/// patterns of a language; implement the interface to use another source, such as a dictionary.
/// </para>
/// <para>The soft hyphens (U+00AD) of a text are break opportunities without a hyphenator.</para>
/// </remarks>
public interface IPdfHyphenator
{
    /// <summary>Finds the positions where a word can be hyphenated.</summary>
    /// <param name="word">The word: the letters and the combining marks of the text, without the punctuation and the spaces around it.</param>
    /// <param name="breakPositions">The buffer that receives the positions, in increasing order: a position is the index of the UTF-16 code unit of <paramref name="word"/> before which a hyphen can be inserted. Its length is the length of the word.</param>
    /// <returns>The number of positions written; 0 when the word cannot be hyphenated.</returns>
    int Hyphenate(ReadOnlySpan<char> word, Span<int> breakPositions);
}
