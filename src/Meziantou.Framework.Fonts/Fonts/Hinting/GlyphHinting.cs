namespace Meziantou.Framework.Fonts;

/// <summary>How the outlines of a font are fitted to the pixel grid of a size (<see cref="GlyphHinter"/>).</summary>
public enum GlyphHinting
{
    /// <summary>The outlines are scaled exactly; nothing is fitted to the grid.</summary>
    None,

    /// <summary>
    /// The outlines are fitted along the vertical axis only, without the instructions of the font: the baseline, the
    /// x-height, the cap height, the ascenders and the descenders on whole pixels, and the horizontal stems a whole
    /// number of pixels thick. The horizontal coordinates and the advances do not change. Any outline format.
    /// </summary>
    Light,

    /// <summary>
    /// The TrueType instructions of the font fit the outlines along both axes, and the advances are whole numbers of
    /// pixels. A font without instructions (CFF outlines, TrueType outlines without a font program nor a control
    /// value program) and an instance of a variable font are hinted as with <see cref="Light"/>.
    /// </summary>
    Full,
}
