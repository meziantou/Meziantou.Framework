namespace Meziantou.Framework.Pdf;

/// <summary>A conformance level of PDF/A (ISO 19005), the PDF of long-term archiving, that a document is saved with.</summary>
public enum PdfAConformance
{
    /// <summary>The document does not claim a PDF/A conformance.</summary>
    None,

    /// <summary>PDF/A-2b (ISO 19005-2, PDF 1.7): the visual appearance is preserved.</summary>
    PdfA2B,

    /// <summary>PDF/A-2u: PDF/A-2b, and every glyph maps to Unicode.</summary>
    PdfA2U,

    /// <summary>PDF/A-2a: PDF/A-2u, and the document is tagged (<see cref="PdfDocument.StructureTree"/>).</summary>
    PdfA2A,

    /// <summary>PDF/A-3b (ISO 19005-3): PDF/A-2b, which can embed files of any format.</summary>
    PdfA3B,

    /// <summary>PDF/A-3u: PDF/A-3b, and every glyph maps to Unicode.</summary>
    PdfA3U,

    /// <summary>PDF/A-3a: PDF/A-3u, and the document is tagged.</summary>
    PdfA3A,

    /// <summary>PDF/A-4 (ISO 19005-4, PDF 2.0).</summary>
    PdfA4,
}
