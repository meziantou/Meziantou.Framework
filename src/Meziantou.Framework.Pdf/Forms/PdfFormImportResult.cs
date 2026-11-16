namespace Meziantou.Framework.Pdf;

/// <summary>The result of <see cref="PdfInteractiveForm.ImportData(PdfFormData)"/>.</summary>
public sealed class PdfFormImportResult
{
    internal PdfFormImportResult(IReadOnlyList<string> updatedFields, IReadOnlyList<string> unknownFields, IReadOnlyDictionary<string, string> rejectedFields)
    {
        UpdatedFields = updatedFields;
        UnknownFields = unknownFields;
        RejectedFields = rejectedFields;
    }

    /// <summary>Gets the names of the fields whose value was set.</summary>
    public IReadOnlyList<string> UpdatedFields { get; }

    /// <summary>Gets the names of the data that the form has no field for.</summary>
    public IReadOnlyList<string> UnknownFields { get; }

    /// <summary>Gets the fields whose value was not set, with the reason: a read-only field, a value that is not an option, a field that has no value (a push button or a signature).</summary>
    public IReadOnlyDictionary<string, string> RejectedFields { get; }
}
