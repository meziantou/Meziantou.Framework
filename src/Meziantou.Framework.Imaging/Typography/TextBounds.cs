namespace Meziantou.Framework.Imaging.Typography;

/// <summary>The box of a text that <see cref="TextLayout.GetOrigin(PointD, TextHorizontalAnchor, TextVerticalAnchor, TextBounds)"/> places.</summary>
public enum TextBounds
{
    /// <summary>
    /// The box of the layout: <see cref="TextLayout.Width"/> by <see cref="TextLayout.Height"/>, from the advances of the
    /// glyphs and the ascents and descents of the fonts. Texts placed by this box line up with each other, whatever their letters.
    /// </summary>
    Layout = 0,

    /// <summary>
    /// What is drawn: <see cref="TextLayout.InkBounds"/>. It centers a text by its visible shape (a digit in a circle) and
    /// puts the visible edge of a glyph that overhangs its advance at the anchor. A layout that draws nothing uses its layout box.
    /// </summary>
    Ink = 1,
}
