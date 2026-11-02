using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A signature field (ISO 32000-2, 12.7.5.5). Its signature is read and verified with <see cref="Signing.PdfSignatureVerifier"/>.</summary>
public sealed class PdfSignatureField : PdfFormField
{
    internal PdfSignatureField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
        : base(form, dictionary, reference, parent)
    {
    }

    /// <summary>Gets a value indicating whether the field is signed: its value is a signature dictionary.</summary>
    public bool IsSigned => GetInherited(VName) is PdfDictionary;

    /// <summary>Gets <see langword="null"/>: the value of a signature field is its signature dictionary.</summary>
    /// <exception cref="NotSupportedException">The value is set.</exception>
    public override string? Value
    {
        get => null;
        set => throw new NotSupportedException("The value of a signature field is set by signing the document");
    }
}
