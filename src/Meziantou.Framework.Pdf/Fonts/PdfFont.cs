using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A font that <see cref="PdfContentBuilder"/> shows text with. A font does not belong to a document: each document that uses it gets its own font objects.</summary>
/// <remarks>
/// The metrics are in glyph space units, 1/1000 of the font size. The class cannot be derived outside the library and its
/// instances are immutable and thread-safe.
/// </remarks>
public abstract class PdfFont
{
    private static readonly PdfFont[] StandardFonts = CreateStandardFonts();

    private protected PdfFont()
    {
    }

    /// <summary>Gets the PostScript name of the font, the <c>/BaseFont</c> of its font dictionary.</summary>
    public abstract string PostScriptName { get; }

    /// <summary>Gets the height above the baseline of the tallest glyphs, in glyph space units (1/1000 em).</summary>
    public abstract double Ascent { get; }

    /// <summary>Gets the depth below the baseline of the lowest glyphs, a negative number in glyph space units.</summary>
    public abstract double Descent { get; }

    /// <summary>Gets the height of the flat capital letters, in glyph space units.</summary>
    public abstract double CapHeight { get; }

    /// <summary>Gets a standard font (ISO 32000-2, 9.6.2.2).</summary>
    /// <param name="font">The font.</param>
    /// <returns>The font.</returns>
    public static PdfFont GetStandardFont(PdfStandardFont font)
    {
        if ((uint)font >= (uint)StandardFonts.Length)
            throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown standard font");

        return StandardFonts[(int)font];
    }

    /// <summary>Indicates whether every character of a text has a glyph in the font.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the font can show the whole text.</returns>
    public abstract bool CanEncode(string text);

    /// <summary>Gets the width of a text shown with the font, in user space units, without character or word spacing.</summary>
    /// <param name="text">The text.</param>
    /// <param name="fontSize">The font size.</param>
    /// <returns>The width.</returns>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    public double GetTextWidth(string text, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        return GetTextWidthInGlyphUnits(text) * fontSize / 1000;
    }

    private protected abstract double GetTextWidthInGlyphUnits(string text);

    /// <summary>Encodes a text into the bytes of a string of a content stream.</summary>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    internal abstract byte[] Encode(string text);

    /// <summary>Gets the reference to the font dictionary of the font in a document, creating it on first use.</summary>
    internal abstract PdfReference GetReference(PdfDocument document);

    private static PdfFont[] CreateStandardFonts()
    {
        return
        [
            new StandardPdfFont("Times-Roman"),
            new StandardPdfFont("Times-Bold"),
            new StandardPdfFont("Times-Italic"),
            new StandardPdfFont("Times-BoldItalic"),
            new StandardPdfFont("Helvetica"),
            new StandardPdfFont("Helvetica-Bold"),
            new StandardPdfFont("Helvetica-Oblique"),
            new StandardPdfFont("Helvetica-BoldOblique"),
            new StandardPdfFont("Courier"),
            new StandardPdfFont("Courier-Bold"),
            new StandardPdfFont("Courier-Oblique"),
            new StandardPdfFont("Courier-BoldOblique"),
            new StandardPdfFont("Symbol"),
            new StandardPdfFont("ZapfDingbats"),
        ];
    }
}
