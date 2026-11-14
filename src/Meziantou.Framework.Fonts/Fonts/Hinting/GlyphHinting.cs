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
    /// The hints of the font fit the outlines, and the advances are whole numbers of pixels. The TrueType instructions
    /// fit TrueType outlines along both axes; an instance of a variable font is hinted with its own points and control
    /// values (<c>gvar</c> and <c>cvar</c>). The stem hints and the alignment zones of CFF and CFF2 outlines fit them
    /// along the vertical axis. TrueType outlines without instructions (no font program nor control value program) are
    /// hinted as with <see cref="Light"/>.
    /// </summary>
    Full,
}
