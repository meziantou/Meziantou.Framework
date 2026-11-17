namespace Meziantou.Framework.Pdf;

/// <summary>The harmless issues <see cref="PdfSaveSanitizer"/> repairs.</summary>
public enum PdfSanitizerRepairKind
{
    /// <summary>The <c>/Outlines</c> entry of the catalog is not an outline dictionary, such as a reference to an object the file does not have: the entry is removed.</summary>
    DanglingOutlineRoot,

    /// <summary>The outline dictionary has no first item: the <c>/Outlines</c> entry of the catalog is removed.</summary>
    EmptyOutlineRoot,

    /// <summary>The crop box of a page has no area, or does not intersect the media box: the crop box is removed, so that it defaults to the media box (ISO 32000-2, 14.11.2).</summary>
    DegenerateCropBox,

    /// <summary>The crop box of a page extends outside the media box: the crop box is set to its intersection with the media box, which is what readers show.</summary>
    CropBoxOutsideMediaBox,
}
