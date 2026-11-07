namespace Meziantou.Framework.Pdf.Objects;

/// <summary>An entry of the indirect objects of a document: where the object is in the file, and its value once loaded.</summary>
internal sealed class IndirectObjectEntry(PdfReference reference)
{
    public PdfReference Reference { get; } = reference;

    public PdfObject? Value { get; set; }

    public IndirectObjectState State { get; set; }

    /// <summary>Gets or sets the offset of the object in the file (an uncompressed object), or -1.</summary>
    public long Offset { get; set; } = -1;

    /// <summary>Gets or sets the number of the object stream that contains the object, or 0.</summary>
    public int ContainerNumber { get; set; }

    /// <summary>Gets or sets the index of the object in its object stream.</summary>
    public int IndexInContainer { get; set; }

    /// <summary>Gets or sets a value indicating whether the object was added after the document was read: an incremental save writes it.</summary>
    public bool IsNew { get; set; }

    /// <summary>Gets or sets the fingerprint of the object when it was loaded: an incremental save writes the objects whose fingerprint changed.</summary>
    public PdfObjectFingerprint? OriginalFingerprint { get; set; }
}
