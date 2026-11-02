namespace Meziantou.Framework.Pdf;

/// <summary>A glyph of the text of a page: its text and where it is painted (see <see cref="PdfPage.ExtractTextLayout(PdfTextExtractionOptions?)"/>).</summary>
/// <remarks>The coordinates are in the default user space of the page: the space of its media box, in points, with the y axis upward, before the rotation of the page (<see cref="PdfPage.Rotation"/>).</remarks>
public sealed class PdfTextGlyph
{
    internal PdfTextGlyph(string text, PdfPoint origin, PdfPoint end, PdfRectangle bounds, double fontSize, string? fontName, double angle, bool isVertical)
    {
        Text = text;
        Origin = origin;
        End = end;
        Bounds = bounds;
        FontSize = fontSize;
        FontName = fontName;
        Angle = angle;
        IsVertical = isVertical;
    }

    /// <summary>Gets the text of the glyph: the characters its font maps it to, or the replacement text (<c>/ActualText</c>) of the glyphs it stands for.</summary>
    public string Text { get; }

    /// <summary>Gets the point where the glyph is painted: the origin of the glyph on the baseline, or its vertical origin for a font written vertically.</summary>
    public PdfPoint Origin { get; }

    /// <summary>Gets the point where the next glyph would be painted: the origin moved by the displacement of the glyph and the character and word spacing.</summary>
    public PdfPoint End { get; }

    /// <summary>
    /// Gets the smallest rectangle that contains the box of the glyph: from <see cref="Origin"/> to <see cref="End"/>, and
    /// from the descent to the ascent of the font (<c>/Descent</c> and <c>/Ascent</c> of its font descriptor) across the
    /// baseline; the box of a glyph written vertically is as wide as the glyph, around its vertical origin.
    /// </summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the font size of the glyph in user space units: the font size of the text state scaled by the text and transformation matrices.</summary>
    public double FontSize { get; }

    /// <summary>Gets the PostScript name of the font (<c>/BaseFont</c>), or <see langword="null"/>.</summary>
    public string? FontName { get; }

    /// <summary>Gets the direction of the writing, in degrees counterclockwise from the x axis: 0 for horizontal text, -90 for text written vertically from top to bottom.</summary>
    public double Angle { get; }

    /// <summary>Gets a value indicating whether the font of the glyph is written vertically (<c>/WMode 1</c>).</summary>
    public bool IsVertical { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
