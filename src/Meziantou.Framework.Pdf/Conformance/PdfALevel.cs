namespace Meziantou.Framework.Pdf.Conformance;

/// <summary>The part of ISO 19005 and the conformance level of a <see cref="PdfAConformance"/> value.</summary>
internal static class PdfALevel
{
    /// <summary>Gets the part of ISO 19005 (1 to 4), or 0 for <see cref="PdfAConformance.None"/>.</summary>
    public static int GetPart(PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA1B or PdfAConformance.PdfA1A => 1,
        PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA2A => 2,
        PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A => 3,
        PdfAConformance.PdfA4 or PdfAConformance.PdfA4E or PdfAConformance.PdfA4F => 4,
        _ => 0,
    };

    /// <summary>Gets the value of <c>pdfaid:conformance</c>, or <see langword="null"/> for PDF/A-4, which has none.</summary>
    public static string? GetConformance(PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A => "A",
        PdfAConformance.PdfA1B or PdfAConformance.PdfA2B or PdfAConformance.PdfA3B => "B",
        PdfAConformance.PdfA2U or PdfAConformance.PdfA3U => "U",
        PdfAConformance.PdfA4E => "E",
        PdfAConformance.PdfA4F => "F",
        _ => null,
    };

    /// <summary>Gets a value indicating whether the level needs a tagged document (the level A).</summary>
    public static bool IsTagged(PdfAConformance conformance) => conformance is PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A;
}
