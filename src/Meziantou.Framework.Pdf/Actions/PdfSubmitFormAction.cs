using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that sends the values of the form fields to a URL (<c>SubmitForm</c>, ISO 32000-2, 12.7.6.2).</summary>
public sealed class PdfSubmitFormAction : PdfAction
{
    internal PdfSubmitFormAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the URL the values are sent to (<c>/F</c>), or <see langword="null"/>.</summary>
    public Uri? Url => ReadFileName(Dictionary[PdfNames.F]) is { } url && Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var result) ? result : null;

    /// <summary>Gets the fully qualified names of the fields sent, or not sent with <see cref="PdfSubmitFormFlags.Exclude"/> (<c>/Fields</c>); an empty list for all the fields.</summary>
    public IReadOnlyList<string> FieldNames => ReadFieldNames(Dictionary[PdfName.Create("Fields")]);

    /// <summary>Gets the flags (<c>/Flags</c>): the format of the data and the fields sent.</summary>
    public PdfSubmitFormFlags Flags => (PdfSubmitFormFlags)unchecked((int)Dictionary.GetInteger(PdfName.Create("Flags"), 0));
}
