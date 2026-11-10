namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfDocument.ConvertToPdfA(PdfAConformance, PdfAConversionOptions?)"/>.</summary>
public sealed class PdfAConversionOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfAConversionOptions Default { get; } = new();

    /// <summary>
    /// Gets the ICC profile of the output intent, used when the document has no output intent, or <see langword="null"/>
    /// for sRGB. A document that paints with <c>DeviceCMYK</c> needs a CMYK profile, such as the profile of its printing
    /// condition: with an sRGB output intent, its CMYK colors have no meaning PDF/A allows.
    /// </summary>
    public ReadOnlyMemory<byte>? OutputIntentProfile { get; init; }

    /// <summary>
    /// Gets a value indicating whether the XMP metadata of the document is kept. The default is <see langword="false"/>:
    /// the metadata is rebuilt from the document information, as the properties of other schemas need extension schemas
    /// PDF/A validators check.
    /// </summary>
    public bool KeepXmpMetadata { get; init; }
}
