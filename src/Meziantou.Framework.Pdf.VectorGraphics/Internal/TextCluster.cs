namespace Meziantou.Framework.Pdf.VectorGraphics.Internal;

/// <summary>A character of a laid out text: a grapheme cluster, or the characters a glyph of a ligature shows.</summary>
/// <param name="Text">The characters, in logical order.</param>
/// <param name="X">The position of the start of the cluster, from the start of the text, spacing included.</param>
/// <param name="Advance">The advance of the cluster, spacing included.</param>
/// <param name="Spacing">The spacing the advance includes.</param>
/// <param name="Glyphs">The glyphs of an embedded font, from <see cref="X"/>, or <see langword="null"/> for a standard font.</param>
internal sealed record TextCluster(string Text, double X, double Advance, double Spacing, PdfGlyph[]? Glyphs)
{
    /// <summary>Gets the index of the first character of the cluster in the text.</summary>
    public int Start { get; init; }

    /// <summary>Gets a value that indicates whether the cluster is in a run written from right to left, whose glyphs have the spacing before them.</summary>
    public bool IsRightToLeft { get; init; }
}
