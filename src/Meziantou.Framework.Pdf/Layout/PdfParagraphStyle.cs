using Meziantou.Framework.Pdf.Layout;

namespace Meziantou.Framework.Pdf;

/// <summary>The style of a paragraph of a flow document.</summary>
public sealed record PdfParagraphStyle
{
    /// <summary>Gets the alignment of the lines, or <see langword="null"/> for the start of the line: the left, or the right of a right-to-left paragraph.</summary>
    public PdfTextAlignment? Alignment { get; init; }

    /// <summary>Gets the space before the paragraph, in points, or <see langword="null"/> for the default.</summary>
    public double? SpaceBefore { get; init; }

    /// <summary>Gets the space after the paragraph, in points, or <see langword="null"/> for the default (<see cref="PdfFlowOptions.ParagraphSpacing"/>).</summary>
    public double? SpaceAfter { get; init; }

    /// <summary>Gets the indentation of the lines from the start of the line, in points.</summary>
    public double Indent { get; init; }

    /// <summary>Gets the style of the text of the paragraph, or <see langword="null"/> for the style of the document.</summary>
    public PdfTextStyle? TextStyle { get; init; }
}
