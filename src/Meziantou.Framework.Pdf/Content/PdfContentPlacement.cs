namespace Meziantou.Framework.Pdf;

/// <summary>Where new content is painted relative to the content of a page (<see cref="PdfPage.CreateContentBuilder(PdfContentPlacement)"/>).</summary>
public enum PdfContentPlacement
{
    /// <summary>Over the content of the page: the new content covers it.</summary>
    Over,

    /// <summary>Under the content of the page: the content of the page covers the new content.</summary>
    Under,
}
