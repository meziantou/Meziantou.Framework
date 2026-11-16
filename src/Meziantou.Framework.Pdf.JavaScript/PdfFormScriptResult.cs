namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>The result of <see cref="PdfFormScriptSession.SetValue(string, string?)"/>, <see cref="PdfFormScriptSession.SetValues"/> and <see cref="PdfFormScriptSession.Recalculate"/>.</summary>
public sealed class PdfFormScriptResult
{
    internal PdfFormScriptResult(bool isAccepted, IReadOnlyList<PdfFormFieldChange> changes, IReadOnlyList<PdfFormScriptDiagnostic> diagnostics)
    {
        IsAccepted = isAccepted;
        Changes = changes;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether the values were accepted: <see langword="false"/> when a keystroke or validation script rejected one (<c>event.rc = false</c>); the field then keeps its value.</summary>
    public bool IsAccepted { get; }

    /// <summary>Gets the fields that changed, in the order they first changed: the field set, then the fields calculated.</summary>
    public IReadOnlyList<PdfFormFieldChange> Changes { get; }

    /// <summary>Gets the errors, limits, blocked calls and alerts of the scripts that ran.</summary>
    public IReadOnlyList<PdfFormScriptDiagnostic> Diagnostics { get; }
}
