namespace Meziantou.Framework.Pdf;

/// <summary>The layout of a text box (<see cref="PdfContentBuilder.DrawTextBox(string, PdfFont, double, PdfRectangle, PdfTextBoxOptions?)"/>).</summary>
public sealed class PdfTextBoxOptions
{
    /// <summary>Gets or sets the horizontal alignment of the lines. The default is <see cref="PdfTextAlignment.Left"/>.</summary>
    public PdfTextAlignment Alignment { get; set; }

    /// <summary>Gets or sets the distance between the baselines of two lines, as a multiple of the font size. The default is 1.2.</summary>
    public double LineHeight { get; set; } = 1.2;
}
