namespace Meziantou.Framework.Pdf.Redaction;

/// <summary>What the redaction of an image did.</summary>
internal enum ImageRedactionKind
{
    /// <summary>No pixel of the image is under an area: it is kept as it is.</summary>
    Unchanged,

    /// <summary>No pixel of the image is left: it is removed.</summary>
    Removed,

    /// <summary>The image is replaced by a copy without its pixels under the areas.</summary>
    Replaced,
}
