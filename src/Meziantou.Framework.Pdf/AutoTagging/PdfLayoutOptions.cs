namespace Meziantou.Framework.Pdf;

/// <summary>The options of the extraction of the layout of pages (<see cref="PdfPage.ExtractLayout(PdfLayoutOptions?)"/>, <see cref="PdfDocument.ExtractLayout(PdfLayoutOptions?)"/>).</summary>
public sealed class PdfLayoutOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfLayoutOptions Default { get; } = new();

    /// <summary>Gets a value indicating whether the text repeated at the same place at the top or the bottom of the pages is a header or a footer. The default is <see langword="true"/>. A single page has none.</summary>
    public bool DetectHeadersAndFooters { get; init; } = true;

    /// <summary>Gets a value indicating whether the grids of rules are tables (<see cref="PdfTableExtractor"/>). The default is <see langword="true"/>.</summary>
    public bool DetectTables { get; init; } = true;

    /// <summary>Gets the ratio of the font size of the body text from which a block is a heading. The default is 1.15.</summary>
    public double HeadingSizeRatio { get; init; } = 1.15;
}
