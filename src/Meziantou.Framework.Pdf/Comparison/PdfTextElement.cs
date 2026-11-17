namespace Meziantou.Framework.Pdf;

/// <summary>A line of text of a page (<see cref="PdfPage.ExtractTextLayout"/>): its text and its bounding box.</summary>
/// <param name="Text">The text of the line.</param>
/// <param name="Bounds">The bounding box of the glyphs of the line, in default user space.</param>
public sealed record PdfTextElement(string Text, PdfRectangle Bounds)
{
    /// <inheritdoc/>
    public override string ToString() => $"\"{Text}\" {Bounds}";
}
