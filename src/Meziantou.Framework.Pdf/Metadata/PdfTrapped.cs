namespace Meziantou.Framework.Pdf;

/// <summary>Whether a document includes trapping information (ISO 32000-2, 14.3.3 and 14.11.6): the <c>/Trapped</c> entry of the document information.</summary>
public enum PdfTrapped
{
    /// <summary>It is not known whether the document is trapped, or it is partly trapped.</summary>
    Unknown,

    /// <summary>The document is trapped: it needs no further trapping.</summary>
    True,

    /// <summary>The document is not trapped.</summary>
    False,
}
