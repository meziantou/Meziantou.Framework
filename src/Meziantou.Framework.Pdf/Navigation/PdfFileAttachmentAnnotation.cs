using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A file attachment annotation (ISO 32000-2, 12.5.6.15): an icon of a page that holds an embedded file.</summary>
public sealed class PdfFileAttachmentAnnotation : PdfAnnotation
{
    internal static readonly PdfName FileAttachmentName = PdfName.Create("FileAttachment");
    internal static readonly PdfName FSName = PdfName.Create("FS");

    internal PdfFileAttachmentAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets the file of the annotation (<c>/FS</c>), or <see langword="null"/> when the annotation has no file specification dictionary.</summary>
    public PdfEmbeddedFile? File => Dictionary.Get<PdfDictionary>(FSName) is { } file ? new PdfEmbeddedFile(Page.Document, file, Dictionary[FSName] as PdfReference, key: null) : null;

    /// <summary>Gets the icon of the annotation (<c>/Name</c>); <see cref="PdfFileAttachmentIcon.PushPin"/> when it is missing or unknown.</summary>
    public PdfFileAttachmentIcon Icon => Dictionary.GetName(PdfNames.Name)?.Value switch
    {
        "Paperclip" => PdfFileAttachmentIcon.Paperclip,
        "Graph" => PdfFileAttachmentIcon.Graph,
        "Tag" => PdfFileAttachmentIcon.Tag,
        _ => PdfFileAttachmentIcon.PushPin,
    };

    internal static PdfName GetIconName(PdfFileAttachmentIcon icon) => icon switch
    {
        PdfFileAttachmentIcon.PushPin => PdfName.Create("PushPin"),
        PdfFileAttachmentIcon.Paperclip => PdfName.Create("Paperclip"),
        PdfFileAttachmentIcon.Graph => PdfName.Create("Graph"),
        PdfFileAttachmentIcon.Tag => PdfName.Create("Tag"),
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, "The icon is not valid"),
    };
}
