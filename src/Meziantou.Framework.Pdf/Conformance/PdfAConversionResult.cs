namespace Meziantou.Framework.Pdf;

/// <summary>What <see cref="PdfDocument.ConvertToPdfA(PdfAConformance, PdfAConversionOptions?)"/> changed, and what it could not fix.</summary>
public sealed class PdfAConversionResult
{
    internal PdfAConversionResult(IReadOnlyList<string> changes, IReadOnlyList<string> violations)
    {
        Changes = changes;
        Violations = violations;
    }

    /// <summary>Gets the changes made to the document, such as "Removed the JavaScript action of the document".</summary>
    public IReadOnlyList<string> Changes { get; }

    /// <summary>Gets the requirements of the conformance level that the document does not meet and that the conversion could not fix.</summary>
    public IReadOnlyList<string> Violations { get; }

    /// <summary>Gets a value indicating whether the document meets the requirements the library checks: <see cref="PdfDocument.PdfAConformance"/> is set, and the document is saved as PDF/A.</summary>
    public bool Succeeded => Violations.Count == 0;
}
