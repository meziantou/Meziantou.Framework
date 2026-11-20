namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfDocument.AutoTag(PdfAutoTagOptions?)"/>.</summary>
public sealed class PdfAutoTagOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfAutoTagOptions Default { get; } = new();

    /// <summary>Gets the options of the detection of the layout.</summary>
    public PdfLayoutOptions Layout { get; init; } = PdfLayoutOptions.Default;

    /// <summary>Gets the natural language of the document (<see cref="PdfDocument.Language"/>), such as <c>en-US</c>, set when the document has none; <see langword="null"/> to leave it.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the function that gives the alternate description of an image, which screen readers read, or <see langword="null"/> for none: PDF/UA requires one.</summary>
    public Func<PdfImageOccurrence, string?>? FigureAlternateText { get; init; }

    /// <summary>Gets a value indicating whether the first row of a table of several rows is its header (<c>TH</c> cells). The default is <see langword="true"/>.</summary>
    public bool FirstTableRowIsHeader { get; init; } = true;
}
