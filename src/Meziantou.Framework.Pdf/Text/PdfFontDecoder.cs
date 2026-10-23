using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// What the text of a font dictionary is (ISO 32000-2, 9.5 to 9.10): how a string splits into codes, the width of each
/// code, and its Unicode text (the <c>/ToUnicode</c> CMap, else the glyph names of the encoding, 9.10.2).
/// </summary>
internal abstract class PdfFontDecoder
{
    private protected PdfFontDecoder(CMap? toUnicode)
    {
        ToUnicode = toUnicode;
    }

    protected CMap? ToUnicode { get; }

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

    /// <summary>Gets the text of a code, or <see langword="null"/> when it is unknown.</summary>
    public abstract string? GetText(uint code);
}
