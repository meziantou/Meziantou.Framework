namespace Meziantou.Framework.Pdf;

/// <summary>How an embedded file relates to the content of the document (<c>/AFRelationship</c>, ISO 32000-2, 7.11.3, Table 43).</summary>
public enum PdfAssociatedFileRelationship
{
    /// <summary>The relationship is not known or cannot be described with the other values.</summary>
    Unspecified,

    /// <summary>The original source material of the content, such as the spreadsheet a table was made from.</summary>
    Source,

    /// <summary>The data the content shows, such as the values of a chart.</summary>
    Data,

    /// <summary>An alternative representation of the content, such as audio.</summary>
    Alternative,

    /// <summary>A supplemental representation of the content, easier to process, such as the MathML of a formula.</summary>
    Supplement,

    /// <summary>An encrypted payload document that the document is a wrapper of.</summary>
    EncryptedPayload,

    /// <summary>The data of a form of the document.</summary>
    FormData,

    /// <summary>A schema definition of the content.</summary>
    Schema,
}
