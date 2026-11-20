using Meziantou.Framework.Pdf.Layout;

namespace Meziantou.Framework.Pdf;

/// <summary>The style of a run of text of a flow document (<see cref="PdfInlineBuilder"/>): the properties that are <see langword="null"/> are those of the paragraph.</summary>
public sealed record PdfTextStyle
{
    /// <summary>Gets the font, or <see langword="null"/> for the font of the paragraph.</summary>
    public PdfFont? Font { get; init; }

    /// <summary>Gets the font size, in points, or <see langword="null"/> for the size of the paragraph.</summary>
    public double? FontSize { get; init; }

    /// <summary>Gets the color of the text, or <see langword="null"/> for the color of the paragraph.</summary>
    public PdfColor? Color { get; init; }
}
