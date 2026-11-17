namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How the outlines of the glyphs of a text are fitted to the pixel grid (<see cref="TextLayoutOptions.Hinting"/>).</summary>
public enum TextHinting
{
    /// <summary>The outlines are scaled exactly and the glyphs are placed at fractions of a pixel: no hinting.</summary>
    None = 0,

    /// <summary>
    /// The outlines are fitted along the vertical axis only, without the instructions of the font: the baseline, the
    /// x-height, the cap height, the ascenders and the descenders on whole pixels, and the horizontal stems a whole
    /// number of pixels thick. The advances and the horizontal positions do not change. Works with every font. In a
    /// vertical layout, an upright glyph starts on a whole pixel down the column, and a glyph on its side is fitted
    /// along the x axis of the image, where the y axis of its outline is.
    /// </summary>
    Light = 1,

    /// <summary>
    /// The hints of the font fit the outlines, the advances are whole numbers of pixels and each glyph starts on a whole
    /// pixel: the TrueType instructions along both axes (an instance of a variable font with its own outlines and
    /// control values), the stem hints and the alignment zones of CFF and CFF2 outlines along the vertical axis.
    /// TrueType outlines without instructions (no font program nor control value program) are hinted as with
    /// <see cref="Light"/>.
    /// </summary>
    Full = 2,
}
