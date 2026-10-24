namespace Meziantou.Framework.Pdf;

/// <summary>A conformance level of PDF/A (ISO 19005), the PDF of long-term archiving, that a document is saved with.</summary>
/// <remarks>The values are not in the order of the parts of ISO 19005: the levels added after the first release of the library follow the others.</remarks>
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

    /// <summary>PDF/A-1b (ISO 19005-1, PDF 1.4): the visual appearance is preserved, without transparency, layers or embedded files.</summary>
    PdfA1B,

    /// <summary>PDF/A-1a: PDF/A-1b, and the document is tagged and every glyph maps to Unicode.</summary>
    PdfA1A,

    /// <summary>PDF/A-4e (ISO 19005-4): PDF/A-4 for engineering documents, which can have 3D and rich media content.</summary>
    PdfA4E,

    /// <summary>PDF/A-4f (ISO 19005-4): PDF/A-4 with one or more embedded files of any format (<see cref="PdfDocument.EmbeddedFiles"/>).</summary>
    PdfA4F,
}
