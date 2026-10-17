namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>The image of a glyph in a strike of a bitmap font under construction.</summary>
/// <param name="GlyphId">The glyph.</param>
/// <param name="Left">The distance from the origin of the glyph to the left of the image, in pixels.</param>
/// <param name="Bottom">The distance from the baseline up to the bottom of the image, in pixels.</param>
/// <param name="Width">The width of the image.</param>
/// <param name="Height">The height of the image.</param>
/// <param name="Data">The encoded image.</param>
internal sealed record FontBitmapGlyph(int GlyphId, int Left, int Bottom, int Width, int Height, byte[] Data)
{
    /// <summary>Gets the type of the image in the <c>sbix</c> table: four characters.</summary>
    public string Type { get; init; } = "png ";

    /// <summary>Gets the glyph whose image this glyph uses instead of its own, in the <c>sbix</c> table.</summary>
    public int? DuplicateOf { get; init; }
}
