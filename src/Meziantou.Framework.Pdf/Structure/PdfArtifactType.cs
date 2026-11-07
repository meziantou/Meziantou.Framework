namespace Meziantou.Framework.Pdf;

/// <summary>The kind of an artifact (ISO 32000-2, 14.8.2.2.2): content that is not part of the logical structure of the document.</summary>
public enum PdfArtifactType
{
    /// <summary>The kind is not given.</summary>
    Unspecified,

    /// <summary>The content that paginates the document: running heads, page numbers.</summary>
    Pagination,

    /// <summary>The content that lays out the page: rules, frames, background shading.</summary>
    Layout,

    /// <summary>The content of the page as a physical medium: crop marks, color bars.</summary>
    Page,

    /// <summary>The background of the page: a watermark, a background image (PDF 1.7).</summary>
    Background,
}
