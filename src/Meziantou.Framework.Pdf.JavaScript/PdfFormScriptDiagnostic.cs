namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>Something a script did that the host should know: an error, a limit exceeded, a blocked call, an alert.</summary>
public sealed class PdfFormScriptDiagnostic
{
    internal PdfFormScriptDiagnostic(PdfFormScriptDiagnosticKind kind, PdfFormScriptEvent scriptEvent, string? fieldName, string message)
    {
        Kind = kind;
        Event = scriptEvent;
        FieldName = fieldName;
        Message = message;
    }

    /// <summary>Gets the kind of diagnostic.</summary>
    public PdfFormScriptDiagnosticKind Kind { get; }

    /// <summary>Gets the script that produced it.</summary>
    public PdfFormScriptEvent Event { get; }

    /// <summary>Gets the fully qualified name of the field of the script, or <see langword="null"/> for a document-level script.</summary>
    public string? FieldName { get; }

    /// <summary>Gets the message: the error, the function blocked, the text of the alert.</summary>
    public string Message { get; }

    /// <inheritdoc/>
    public override string ToString() => FieldName is null ? $"{Kind} ({Event}): {Message}" : $"{Kind} ({Event} of {FieldName}): {Message}";
}
