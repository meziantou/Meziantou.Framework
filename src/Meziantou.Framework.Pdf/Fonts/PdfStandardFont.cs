namespace Meziantou.Framework.Pdf;

/// <summary>The standard 14 fonts (ISO 32000-2, 9.6.2.2): every reader has them, so a document that uses them does not embed them.</summary>
/// <remarks>PDF/A forbids fonts that are not embedded: use an embedded font for an archival document.</remarks>
public enum PdfStandardFont
{
    /// <summary>Times-Roman.</summary>
    TimesRoman,

    /// <summary>Times-Bold.</summary>
    TimesBold,

    /// <summary>Times-Italic.</summary>
    TimesItalic,

    /// <summary>Times-BoldItalic.</summary>
    TimesBoldItalic,

    /// <summary>Helvetica.</summary>
    Helvetica,

    /// <summary>Helvetica-Bold.</summary>
    HelveticaBold,

    /// <summary>Helvetica-Oblique.</summary>
    HelveticaOblique,

    /// <summary>Helvetica-BoldOblique.</summary>
    HelveticaBoldOblique,

    /// <summary>Courier, a monospaced font.</summary>
    Courier,

    /// <summary>Courier-Bold.</summary>
    CourierBold,

    /// <summary>Courier-Oblique.</summary>
    CourierOblique,

    /// <summary>Courier-BoldOblique.</summary>
    CourierBoldOblique,

    /// <summary>Symbol: Greek letters and mathematical symbols, with its own encoding.</summary>
    Symbol,

    /// <summary>ZapfDingbats: ornaments, with its own encoding.</summary>
    ZapfDingbats,
}
