namespace Meziantou.Framework;

public sealed class TextDiffOptions
{
    /// <summary>
    /// Gets or sets the algorithm used to compute the diff.
    /// <see cref="TextDiffAlgorithm.Myers"/> is the default and is recommended for general use.
    /// </summary>
    public TextDiffAlgorithm Algorithm { get; set; } = TextDiffAlgorithm.Myers;


    /// <summary>
    /// Gets or sets how the texts are split into the chunks that are compared against each other.
    /// Defaults to <see cref="TextChunker.Lines"/>.
    /// </summary>
    public TextChunker Chunker { get; set; } = TextChunker.Lines;

    /// <summary>
    /// Gets or sets a value indicating whether two chunks that differ only in casing are considered equal.
    /// The comparison is ordinal, so casing rules are not culture-sensitive.
    /// </summary>
    public bool IgnoreCase { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the leading and trailing whitespace of a chunk is ignored
    /// when comparing chunks.
    /// </summary>
    /// <remarks>
    /// Only the <em>edges</em> of a chunk are trimmed; whitespace inside a chunk remains significant. With the
    /// default <see cref="TextChunker.Lines"/> chunker, <c>"a  b"</c> and <c>"a b"</c> are still different. Use
    /// <see cref="TextChunker.Words"/> to make internal whitespace insignificant: it puts each run of whitespace
    /// in its own chunk, which trimming then reduces to an empty one.
    /// </remarks>
    public bool IgnoreWhitespace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether line terminators are normalized to <c>"\n"</c> before the texts
    /// are chunked, so that <c>"\r\n"</c>, <c>"\r"</c> and <c>"\n"</c> compare equal.
    /// </summary>
    /// <remarks>
    /// The normalization happens before chunking, so the entries of the result carry the normalized text rather
    /// than the original line terminators.
    /// </remarks>
    public bool IgnoreEndOfLine { get; set; }
}
