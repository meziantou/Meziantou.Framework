namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The changes a certification signature allows after it (ISO 32000-2, 12.8.2.2.2, the <c>/P</c> of the DocMDP
/// transform). Adding validation data (<see cref="PdfSigner.AddValidationData"/>) and document time stamps
/// (<see cref="PdfSigner.TimestampAsync"/>) is allowed at every level (ETSI EN 319 142-1, 5.4).
/// </summary>
public enum PdfCertificationLevel
{
    /// <summary>No change is allowed: another signature cannot be added (<c>/P 1</c>).</summary>
    NoChanges = 1,

    /// <summary>Filling in the forms, instantiating the page templates and signing are allowed (<c>/P 2</c>).</summary>
    FormFilling = 2,

    /// <summary>Filling in the forms, signing, and creating, changing and deleting annotations are allowed (<c>/P 3</c>).</summary>
    FormFillingAndAnnotations = 3,
}
