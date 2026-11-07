namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How the outlines of the glyphs of a text are fitted to the pixel grid (<see cref="TextLayoutOptions.Hinting"/>).</summary>
public enum TextHinting
{
    /// <summary>The outlines are scaled exactly and the glyphs are placed at fractions of a pixel: no hinting.</summary>
    None = 0,

    /// <summary>
    /// The outlines are fitted along the vertical axis only, without the instructions of the font: the baseline, the
    /// x-height, the cap height, the ascenders and the descenders on whole pixels, and the horizontal stems a whole
    /// number of pixels thick. The advances and the horizontal positions do not change. Works with every font.
    /// </summary>
    Light = 1,

    /// <summary>
    /// The TrueType instructions of the font fit the outlines along both axes, the advances are whole numbers of
    /// pixels and each glyph starts on a whole pixel. A font without instructions (CFF outlines, or TrueType outlines
    /// without a font program nor a control value program) and an instance of a variable font are hinted as with
    /// <see cref="Light"/>.
    /// </summary>
    Full = 2,
}
