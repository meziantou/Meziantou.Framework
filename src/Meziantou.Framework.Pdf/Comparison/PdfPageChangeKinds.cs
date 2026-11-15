namespace Meziantou.Framework.Pdf;

/// <summary>The kinds of changes <see cref="PdfDocumentComparer"/> finds on a page.</summary>
[Flags]
public enum PdfPageChangeKinds
{
    /// <summary>The page did not change.</summary>
    None = 0,

    /// <summary>The page is in the second document only.</summary>
    Added = 1 << 0,

    /// <summary>The page is in the first document only.</summary>
    Removed = 1 << 1,

    /// <summary>The size of the crop box of the page changed.</summary>
    Size = 1 << 2,

    /// <summary>The rotation of the page changed.</summary>
    Rotation = 1 << 3,

    /// <summary>Lines of text were added, removed or changed.</summary>
    Text = 1 << 4,

    /// <summary>The text is the same, but lines moved.</summary>
    TextGeometry = 1 << 5,

    /// <summary>Images were added, removed or changed.</summary>
    Images = 1 << 6,

    /// <summary>The same images are painted at other places.</summary>
    ImagePlacements = 1 << 7,

    /// <summary>Paths were added, removed, moved or painted in another way.</summary>
    Paths = 1 << 8,

    /// <summary>Shadings were added, removed or changed.</summary>
    Shadings = 1 << 9,
}
