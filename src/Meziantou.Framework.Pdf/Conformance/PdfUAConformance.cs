namespace Meziantou.Framework.Pdf;

/// <summary>A conformance level of PDF/UA (ISO 14289), the PDF of universal accessibility, that a document is saved with.</summary>
public enum PdfUAConformance
{
    /// <summary>The document does not claim a PDF/UA conformance.</summary>
    None,

    /// <summary>PDF/UA-1 (ISO 14289-1): the document is tagged, has a title and a language, and its content is either in the structure tree or an artifact.</summary>
    PdfUA1,
}
