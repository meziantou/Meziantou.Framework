namespace Meziantou.Framework.Pdf;

/// <summary>
/// The operations the user password of an encrypted document allows (ISO 32000-2, 7.6.4.2, table 22). The owner password
/// allows all of them. The library does not enforce them: the applications that open a document with the user password
/// must.
/// </summary>
[Flags]
#pragma warning disable CA1714 // Flags enums should have plural names: the name is plural
public enum PdfPermissions
#pragma warning restore CA1714
{
    /// <summary>No operation.</summary>
    None = 0,

    /// <summary>Print the document (bit 3), in a degraded quality without <see cref="PrintHighQuality"/>.</summary>
    Print = 1 << 2,

    /// <summary>Modify the document in ways other than <see cref="Annotate"/>, <see cref="FillForms"/> and <see cref="Assemble"/> (bit 4).</summary>
    Modify = 1 << 3,

    /// <summary>Copy or extract the text and the graphics (bit 5).</summary>
    CopyContent = 1 << 4,

    /// <summary>Add or modify the annotations, and fill the form fields (bit 6).</summary>
    Annotate = 1 << 5,

    /// <summary>Fill the form fields, even without <see cref="Annotate"/> (bit 9).</summary>
    FillForms = 1 << 8,

    /// <summary>Extract the text and the graphics for accessibility (bit 10). PDF 2.0 deprecates it: readers ignore it.</summary>
    ExtractForAccessibility = 1 << 9,

    /// <summary>Assemble the document: insert, rotate and delete pages, create bookmarks and thumbnails (bit 11).</summary>
    Assemble = 1 << 10,

    /// <summary>Print the document in a faithful quality (bit 12).</summary>
    PrintHighQuality = 1 << 11,

    /// <summary>All the operations.</summary>
    All = Print | Modify | CopyContent | Annotate | FillForms | ExtractForAccessibility | Assemble | PrintHighQuality,
}
