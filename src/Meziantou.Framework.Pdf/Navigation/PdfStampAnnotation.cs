using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A rubber stamp annotation (ISO 32000-2, 12.5.6.12): a word such as <c>APPROVED</c> or <c>DRAFT</c>, or an image, stamped on the page.</summary>
public sealed class PdfStampAnnotation : PdfMarkupAnnotation
{
    internal PdfStampAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the name of the stamp (<c>/Name</c>): <c>Approved</c>, <c>Experimental</c>, <c>NotApproved</c>, <c>AsIs</c>, <c>Expired</c>, <c>NotForPublicRelease</c>, <c>Confidential</c>, <c>Final</c>, <c>Sold</c>, <c>Departmental</c>, <c>ForComment</c>, <c>TopSecret</c>, <c>Draft</c>, <c>ForPublicRelease</c>, or a name a viewer knows. The default is <c>Draft</c>.</summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public string Icon
    {
        get => Dictionary.GetName(PdfNames.Name)?.Value ?? "Draft";
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            Dictionary.SetName(PdfNames.Name, value);
        }
    }
}
