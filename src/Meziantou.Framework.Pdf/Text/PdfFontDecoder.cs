using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// What the text of a font dictionary is (ISO 32000-2, 9.5 to 9.10): how a string splits into codes, the width of each
/// code, and its Unicode text (the <c>/ToUnicode</c> CMap, else the glyph names of the encoding, 9.10.2).
/// </summary>
/// <remarks>
/// The code <see cref="ReadCode"/> returns identifies the code for the other methods: the byte of a simple font, a value
/// that also says the length of the code for a composite font (see <see cref="CompositeFontDecoder"/>).
/// </remarks>
internal abstract class PdfFontDecoder
{
    // The ascent and descent of a font without metrics, in thousandths of the font size
    private const double DefaultAscent = 0.8;
    private const double DefaultDescent = -0.2;

    private protected PdfFontDecoder(PdfDictionary font, PdfDictionary? descriptor, CMap? toUnicode)
    {
        ToUnicode = toUnicode;
        FontName = font.GetName(PdfNames.BaseFont)?.Value;
        var ascent = descriptor?.GetNumber(PdfNames.Ascent, 0) ?? 0;
        var descent = descriptor?.GetNumber(PdfNames.Descent, 0) ?? 0;
        if (font.GetName(PdfNames.Subtype) == PdfNames.Type3 || ascent <= descent || ascent <= 0)
        {
            // The metrics of a Type 3 font are in its glyph space, not in thousandths of the font size (9.6.4)
            Ascent = DefaultAscent;
            Descent = DefaultDescent;
        }
        else
        {
            Ascent = Math.Min(ascent, 4000) / 1000;
            Descent = Math.Clamp(descent, -4000, 0) / 1000;
        }
    }

    protected CMap? ToUnicode { get; }

    /// <summary>Gets the PostScript name of the font (<c>/BaseFont</c>), or <see langword="null"/>.</summary>
    public string? FontName { get; }

    /// <summary>Gets the height of the font above the baseline, a fraction of the font size (<c>/Ascent</c> of the font descriptor).</summary>
    public double Ascent { get; }

    /// <summary>Gets the depth of the font below the baseline, a negative fraction of the font size (<c>/Descent</c> of the font descriptor).</summary>
    public double Descent { get; }

    /// <summary>Gets a value indicating whether the font is written vertically (Identity-V).</summary>
    public virtual bool IsVertical => false;

    public static PdfFontDecoder Create(PdfDictionary? font)
    {
        if (font is null)
            return new SimpleFontDecoder(new PdfDictionary(), toUnicode: null);

        CMap? toUnicode = null;
        if (font.Get<PdfStream>(PdfNames.ToUnicode) is { } toUnicodeStream)
        {
            try
            {
                toUnicode = CMap.Parse(toUnicodeStream);
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
                // A damaged ToUnicode CMap: the encoding gives the text
            }
        }

        if (font.GetName(PdfNames.Subtype) == PdfNames.Type0)
            return new CompositeFontDecoder(font, toUnicode);

        return new SimpleFontDecoder(font, toUnicode);
    }

    /// <summary>Reads the next code of a string.</summary>
    public abstract (uint Code, int Length) ReadCode(ReadOnlySpan<byte> data);

    /// <summary>Gets the horizontal displacement of a code, in text space units (glyph width / 1000 for most fonts).</summary>
    public abstract double GetWidth(uint code);

    /// <summary>
    /// Gets the metrics of a code in vertical writing (9.7.4.3), in text space units: the vertical displacement (negative
    /// downward), and the position vector from the origin of the glyph in horizontal writing to its origin in vertical writing.
    /// </summary>
    public virtual (double Advance, double OriginX, double OriginY) GetVerticalMetrics(uint code) => (-1, GetWidth(code) / 2, 0.88);

    /// <summary>Gets the text of a code, or <see langword="null"/> when it is unknown.</summary>
    public abstract string? GetText(uint code);
}
