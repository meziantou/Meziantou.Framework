using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A push button (ISO 32000-2, 12.7.5.2.2): a button that runs an action, without a value.</summary>
public sealed class PdfPushButtonField : PdfFormField
{
    internal PdfPushButtonField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
        : base(form, dictionary, reference, parent)
    {
    }
}
