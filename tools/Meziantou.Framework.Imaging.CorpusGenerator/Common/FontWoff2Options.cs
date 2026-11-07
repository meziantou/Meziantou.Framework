namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>What <see cref="FontWoff2Writer"/> transforms.</summary>
internal sealed class FontWoff2Options
{
    /// <summary>Gets or sets a value indicating whether the glyph tables are stored in their transformed form.</summary>
    public bool TransformGlyphs { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the bounding box of every glyph is stored, not only the ones that cannot be computed.</summary>
    public bool ExplicitBoundingBoxes { get; set; }

    /// <summary>
    /// Gets or sets the flags of the transformed horizontal metrics, or 0 to store the table as it is. Bit 0 omits the
    /// left side bearings of the glyphs that have an advance, bit 1 the ones of the other glyphs.
    /// </summary>
    public int HorizontalMetricsFlags { get; set; }
}
