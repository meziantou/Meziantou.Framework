namespace Meziantou.Framework.Pdf.Rendering.Graphics;

internal sealed record RichTextStyle(double Size, bool Bold, bool Italic, RichTextFamily Family, (double R, double G, double B)? Color, int Alignment)
{
    /// <summary>Gets the standard 14 font of the style.</summary>
    public string BaseFont => (Family, Bold, Italic) switch
    {
        (RichTextFamily.Serif, false, false) => "Times-Roman",
        (RichTextFamily.Serif, true, false) => "Times-Bold",
        (RichTextFamily.Serif, false, true) => "Times-Italic",
        (RichTextFamily.Serif, true, true) => "Times-BoldItalic",
        (RichTextFamily.Monospace, false, false) => "Courier",
        (RichTextFamily.Monospace, true, false) => "Courier-Bold",
        (RichTextFamily.Monospace, false, true) => "Courier-Oblique",
        (RichTextFamily.Monospace, true, true) => "Courier-BoldOblique",
        (_, false, false) => "Helvetica",
        (_, true, false) => "Helvetica-Bold",
        (_, false, true) => "Helvetica-Oblique",
        _ => "Helvetica-BoldOblique",
    };
}
