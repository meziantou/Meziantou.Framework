using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that resets the form fields to their default values (<c>ResetForm</c>, ISO 32000-2, 12.7.6.3).</summary>
public sealed class PdfResetFormAction : PdfAction
{
    internal PdfResetFormAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the fully qualified names of the fields reset, or not reset when <see cref="Exclude"/> is <see langword="true"/> (<c>/Fields</c>); an empty list for all the fields.</summary>
    public IReadOnlyList<string> FieldNames => ReadFieldNames(Dictionary[PdfName.Create("Fields")]);

    /// <summary>Gets a value indicating whether <see cref="FieldNames"/> are the fields not reset (bit 1 of <c>/Flags</c>).</summary>
    public bool Exclude => (Dictionary.GetInteger(PdfName.Create("Flags"), 0) & 1) != 0;
}
