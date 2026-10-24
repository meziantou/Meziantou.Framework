namespace Meziantou.Framework.Pdf;

/// <summary>A conformance level of PDF/UA (ISO 14289), the PDF of universal accessibility, that a document is saved with.</summary>
public enum PdfUAConformance
{
    /// <summary>The document does not claim a PDF/UA conformance.</summary>
    None,

    /// <summary>PDF/UA-1 (ISO 14289-1): the document is tagged, has a title and a language, and its content is either in the structure tree or an artifact.</summary>
    PdfUA1,

    /// <summary>
    /// PDF/UA-2 (ISO 14289-2, PDF 2.0): PDF/UA-1 with the structure of PDF 2.0 (a single <c>Document</c> element of
    /// the PDF 2.0 namespace, no <c>H</c> element) and structure destinations
    /// (<see cref="PdfDestination.WithStructureElement"/>). It can be combined with PDF/A-4 only.
    /// </summary>
    PdfUA2,
}
