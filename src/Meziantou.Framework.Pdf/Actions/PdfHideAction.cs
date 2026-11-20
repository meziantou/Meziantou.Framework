using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that hides or shows annotations or form fields (<c>Hide</c>, ISO 32000-2, 12.6.4.11).</summary>
public sealed class PdfHideAction : PdfAction
{
    internal PdfHideAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the fully qualified names of the fields (<c>/T</c>): the names, and the names of the field dictionaries and widgets it designates.</summary>
    public IReadOnlyList<string> FieldNames => ReadFieldNames(Dictionary[PdfName.Create("T")]);

    /// <summary>Gets a value indicating whether the targets are hidden (<c>/H</c>, the default) or shown.</summary>
    public bool Hides => Dictionary.GetBoolean(PdfName.Create("H"), defaultValue: true);
}
