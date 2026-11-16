namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>Creates the script sessions of the interactive forms.</summary>
public static class PdfInteractiveFormScriptExtensions
{
    /// <summary>
    /// Creates a session that runs the scripts of a form when values are set through it. The document-level scripts run
    /// once, now. The scripts never run otherwise: setting <see cref="PdfFormField.Value"/> runs none.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="options">The limits and the callbacks, or <see langword="null"/> for <see cref="PdfFormScriptOptions.Default"/>.</param>
    /// <returns>The session. Dispose of it to release its script engine.</returns>
    /// <exception cref="PdfFormScriptException">A document-level script failed, and <see cref="PdfFormScriptOptions.ThrowOnScriptError"/> is set.</exception>
    public static PdfFormScriptSession CreateScriptSession(this PdfInteractiveForm form, PdfFormScriptOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new PdfFormScriptSession(form, options ?? PdfFormScriptOptions.Default);
    }
}
