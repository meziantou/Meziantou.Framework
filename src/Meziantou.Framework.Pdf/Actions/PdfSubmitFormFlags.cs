namespace Meziantou.Framework.Pdf;

/// <summary>The flags of a submit-form action (<c>/Flags</c>, ISO 32000-2, 12.7.6.2, table 239).</summary>
[Flags]
public enum PdfSubmitFormFlags
{
    /// <summary>No flag: the fields are sent as HTML form data with the POST method.</summary>
    None = 0,

    /// <summary>The fields named are the fields not sent (bit 1).</summary>
    Exclude = 1,

    /// <summary>The fields without a value are sent too (bit 2).</summary>
    IncludeNoValueFields = 1 << 1,

    /// <summary>The fields are sent as HTML form data, not FDF (bit 3).</summary>
    ExportFormat = 1 << 2,

    /// <summary>The HTML form data is sent with the GET method (bit 4).</summary>
    GetMethod = 1 << 3,

    /// <summary>The coordinates of the mouse are sent (bit 5).</summary>
    SubmitCoordinates = 1 << 4,

    /// <summary>The fields are sent as XFDF (bit 6).</summary>
    Xfdf = 1 << 5,

    /// <summary>The incremental updates of the file are sent with the FDF (bit 7).</summary>
    IncludeAppendSaves = 1 << 6,

    /// <summary>The markup annotations are sent with the FDF (bit 8).</summary>
    IncludeAnnotations = 1 << 7,

    /// <summary>The whole document is sent as PDF (bit 9).</summary>
    SubmitPdf = 1 << 8,

    /// <summary>The dates are converted to the canonical format (bit 10).</summary>
    CanonicalFormat = 1 << 9,

    /// <summary>Only the annotations of the user are sent (bit 11).</summary>
    ExcludeNonUserAnnotations = 1 << 10,

    /// <summary>The <c>/F</c> entry is not sent (bit 12).</summary>
    ExcludeFKey = 1 << 11,

    /// <summary>The document is embedded in the FDF (bit 14).</summary>
    EmbedForm = 1 << 13,
}
