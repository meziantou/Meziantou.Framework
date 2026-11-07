using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// The OCR-A and OCR-B fonts, for the human-readable text of barcodes (<see cref="BarcodeDrawingOptions.Font"/>).
/// </summary>
/// <remarks>
/// <para>
/// The fonts are generated from the character shapes of the standards: OCR-A from the dimensioned drawings of ANSI
/// X3.17-1981, OCR-B from the illustrations of ANSI X3.49-1975 (the American counterpart of ECMA-11 and ISO 1073-2), at
/// their Size I, with the nominal stroke width. They are monospaced, with an advance of 0.6 em, and licensed with this
/// package.
/// </para>
/// <para>
/// Both fonts map the printable ASCII characters. OCR-A maps its Hook, Chair and Fork (U+2440 to U+2442), which also
/// stand for the Tilde, the Grave Accent and the Underline, as the standard prescribes. OCR-B also maps the letters,
/// accents and symbols of its extended repertoire (Ä, Å, Æ, Ö, Ø, Ü, Ñ, IJ, å, æ, ø, ij, ß, §, ¤, £, ¥, ¨, ´).
/// </para>
/// </remarks>
public static class BarcodeFonts
{
    private static readonly Lazy<Font> OcrAFont = new(() => Load("OcrA.ttf"));
    private static readonly Lazy<Font> OcrBFont = new(() => Load("OcrB.ttf"));

    /// <summary>Gets the OCR-A font.</summary>
    public static Font OcrA => OcrAFont.Value;

    /// <summary>Gets the OCR-B font, the font usually printed under EAN and UPC barcodes.</summary>
    public static Font OcrB => OcrBFont.Value;

    private static Font Load(string fileName)
    {
        using var stream = typeof(BarcodeFonts).Assembly.GetManifestResourceStream("Meziantou.Framework.Imaging.Barcodes.Fonts." + fileName)
            ?? throw new InvalidOperationException($"The font '{fileName}' is not embedded in the assembly.");
        return Font.Load(stream);
    }
}
