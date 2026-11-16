namespace Meziantou.Framework.FileConversionTool;

/// <summary>The PDF/A level of a PDF output.</summary>
internal enum PdfConformance
{
    /// <summary>A PDF file that does not claim a PDF/A conformance.</summary>
    None,

    /// <summary>PDF/A-2b: the pages look the same in every reader, in the long term.</summary>
    PdfA2B,

    /// <summary>PDF/A-2u: PDF/A-2b, and the text can be extracted as Unicode.</summary>
    PdfA2U,

    /// <summary>PDF/A-3b: PDF/A-2b, and the document can embed files of any format.</summary>
    PdfA3B,

    /// <summary>PDF/A-3u: PDF/A-3b, and the text can be extracted as Unicode.</summary>
    PdfA3U,
}
