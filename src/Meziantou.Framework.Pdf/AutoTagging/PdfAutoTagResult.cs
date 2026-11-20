namespace Meziantou.Framework.Pdf;

/// <summary>What <see cref="PdfDocument.AutoTag(PdfAutoTagOptions?)"/> tagged.</summary>
public sealed class PdfAutoTagResult
{
    internal PdfAutoTagResult()
    {
    }

    /// <summary>Gets the number of headings.</summary>
    public int Headings { get; internal set; }

    /// <summary>Gets the number of paragraphs.</summary>
    public int Paragraphs { get; internal set; }

    /// <summary>Gets the number of lists.</summary>
    public int Lists { get; internal set; }

    /// <summary>Gets the number of tables.</summary>
    public int Tables { get; internal set; }

    /// <summary>Gets the number of figures.</summary>
    public int Figures { get; internal set; }

    /// <summary>Gets the number of figures without an alternate description (<see cref="PdfAutoTagOptions.FigureAlternateText"/>): PDF/UA requires one.</summary>
    public int FiguresWithoutAlternateText { get; internal set; }

    /// <summary>Gets the number of operators of the content marked as artifacts: the headers and footers, the rules and the shapes.</summary>
    public int Artifacts { get; internal set; }
}
