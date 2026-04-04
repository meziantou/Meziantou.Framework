namespace Meziantou.Framework;

public sealed class TextDiffOptions
{
    /// <summary>
    /// Gets or sets the algorithm used to compute the diff.
    /// <see cref="TextDiffAlgorithm.Myers"/> is the default and is recommended for general use.
    /// </summary>
    public TextDiffAlgorithm Algorithm { get; set; } = TextDiffAlgorithm.Myers;

    public TextChunker Chunker { get; set; } = TextChunker.Lines;

    public bool IgnoreCase { get; set; }

    public bool IgnoreWhitespace { get; set; }

    public bool IgnoreEndOfLine { get; set; }
}
