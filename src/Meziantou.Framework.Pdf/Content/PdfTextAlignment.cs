namespace Meziantou.Framework.Pdf;

/// <summary>The horizontal alignment of the lines of a text box (<see cref="PdfContentBuilder.DrawTextBox(string, PdfFont, double, PdfRectangle, PdfTextBoxOptions?)"/>).</summary>
public enum PdfTextAlignment
{
    /// <summary>The lines start at the left of the box.</summary>
    Left,

    /// <summary>The lines are centered in the box.</summary>
    Center,

    /// <summary>The lines end at the right of the box.</summary>
    Right,

    /// <summary>The lines fill the width of the box, with spaces between their words stretched; the last line of a paragraph starts at the left.</summary>
    Justify,
}
