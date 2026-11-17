namespace Meziantou.Framework.Pdf;

/// <summary>
/// The JavaScript actions of a form field (<c>/AA</c>, ISO 32000-2, 12.6.3, table 199): the scripts a viewer runs when a
/// value is typed, formatted, validated and calculated. The library reads them; <c>Meziantou.Framework.Pdf.JavaScript</c>
/// runs them.
/// </summary>
public sealed class PdfFieldActions
{
    internal PdfFieldActions(PdfJavaScriptAction? keystroke, PdfJavaScriptAction? format, PdfJavaScriptAction? validate, PdfJavaScriptAction? calculate)
    {
        Keystroke = keystroke;
        Format = format;
        Validate = validate;
        Calculate = calculate;
    }

    /// <summary>Gets the script run when the user types or commits a value (<c>/K</c>), or <see langword="null"/>.</summary>
    public PdfJavaScriptAction? Keystroke { get; }

    /// <summary>Gets the script that formats the value for display (<c>/F</c>), or <see langword="null"/>.</summary>
    public PdfJavaScriptAction? Format { get; }

    /// <summary>Gets the script that validates a new value (<c>/V</c>), or <see langword="null"/>.</summary>
    public PdfJavaScriptAction? Validate { get; }

    /// <summary>Gets the script that calculates the value from other fields (<c>/C</c>), or <see langword="null"/>.</summary>
    public PdfJavaScriptAction? Calculate { get; }
}
