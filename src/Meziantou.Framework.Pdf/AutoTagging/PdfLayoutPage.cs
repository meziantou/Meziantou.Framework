namespace Meziantou.Framework.Pdf;

/// <summary>The layout of a page: its nodes in reading order (<see cref="PdfPage.ExtractLayout(PdfLayoutOptions?)"/>).</summary>
public sealed class PdfLayoutPage
{
    internal PdfLayoutPage(PdfPage page, IReadOnlyList<PdfLayoutNode> nodes)
    {
        Page = page;
        Nodes = nodes;
    }

    /// <summary>Gets the page.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the nodes of the page, in reading order: the headers first, the footers last.</summary>
    public IReadOnlyList<PdfLayoutNode> Nodes { get; }
}
