namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Options for loading a font. Instances are immutable and can be shared.</summary>
public sealed class FontLoadOptions
{
    /// <summary>Gets the default options.</summary>
    public static FontLoadOptions Default { get; } = new();

    /// <summary>
    /// Gets the configuration: the limits that bound the bytes read (<see cref="ImageResourceLimits.MaxEncodedBytes"/>),
    /// the bytes the font keeps (<see cref="ImageResourceLimits.MaxFontBytes"/>) and the work of decoding one of its
    /// glyphs (<see cref="ImageResourceLimits.MaxGlyphComplexity"/>). Defaults to <see cref="ImageConfiguration.Default"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageConfiguration Configuration
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageConfiguration.Default;

    /// <summary>
    /// Gets a value indicating whether the bitmaps without colors a font has for some sizes replace the outlines of its
    /// glyphs at these sizes. <see langword="false"/> by default: a glyph is drawn from its outline at every size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some fonts, mostly fonts of Chinese, Japanese and Korean made for screens of low resolution, have for a few
    /// sizes a drawing of each glyph made pixel by pixel (the <c>EBLC</c> and <c>EBDT</c> tables). With this option, a
    /// text whose size, in pixels per em, is exactly the size of such a strike is drawn with these pixels: each one
    /// is a square of the outline of the glyph, so the text is filled, stroked, measured and placed on a path like
    /// any other, and a glyph drawn at a whole position has the pixels of its bitmap. At any other size, and for a
    /// glyph the strike does not have, the outline is drawn.
    /// </para>
    /// <para>
    /// Only bitmaps of one bit per pixel are used. The advances stay those of the outlines. The images with colors
    /// of a font (<c>sbix</c>, <c>CBDT</c>) do not depend on this option: they are always drawn.
    /// </para>
    /// </remarks>
    public bool UseEmbeddedBitmaps { get; init; }
}
