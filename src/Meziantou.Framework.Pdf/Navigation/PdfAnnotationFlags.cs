namespace Meziantou.Framework.Pdf;

/// <summary>The flags of an annotation (ISO 32000-2, 12.5.3, table 167). The bits the enumeration does not name are kept as they are.</summary>
[Flags]
public enum PdfAnnotationFlags
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>An annotation of an unknown type is not shown (bit 1).</summary>
    Invisible = 1,

    /// <summary>The annotation is neither shown nor printed, and the user cannot interact with it (bit 2).</summary>
    Hidden = 1 << 1,

    /// <summary>The annotation is printed (bit 3).</summary>
    Print = 1 << 2,

    /// <summary>The appearance of the annotation is not scaled with the zoom factor of the page (bit 4).</summary>
    NoZoom = 1 << 3,

    /// <summary>The appearance of the annotation is not rotated with the page (bit 5).</summary>
    NoRotate = 1 << 4,

    /// <summary>The annotation is not shown on the screen, but can be printed (bit 6).</summary>
    NoView = 1 << 5,

    /// <summary>The user cannot interact with the annotation (bit 7).</summary>
    ReadOnly = 1 << 6,

    /// <summary>The annotation cannot be deleted, nor its properties changed, by the user (bit 8).</summary>
    Locked = 1 << 7,

    /// <summary>The <see cref="NoView"/> flag is inverted by some events (bit 9).</summary>
    ToggleNoView = 1 << 8,

    /// <summary>The contents of the annotation cannot be changed by the user (bit 10).</summary>
    LockedContents = 1 << 9,
}
