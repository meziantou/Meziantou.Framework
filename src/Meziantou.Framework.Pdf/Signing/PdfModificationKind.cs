namespace Meziantou.Framework.Pdf.Signing;

/// <summary>What a change made to a document after a signature is, from the least to the most significant.</summary>
public enum PdfModificationKind
{
    /// <summary>Validation data of the signatures: the document security store (ISO 32000-2, 12.8.4.3).</summary>
    ValidationData,

    /// <summary>A document time stamp (ISO 32000-2, 12.8.5).</summary>
    DocumentTimestamp,

    /// <summary>A signature: a signature field, its widget and its signature dictionary.</summary>
    Signature,

    /// <summary>The value or the appearance of a field that existed when the document was signed.</summary>
    FormFilling,

    /// <summary>An annotation other than a widget: added, changed or deleted.</summary>
    Annotation,

    /// <summary>Any other change: the content of a page, the metadata, a new field that is not a signature, and so on.</summary>
    Other,
}
