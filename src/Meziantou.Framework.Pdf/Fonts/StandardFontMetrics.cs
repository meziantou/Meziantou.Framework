using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>The metrics of the standard 14 fonts (ISO 32000-2, 9.6.2.2): the Adobe Core14 AFM files of Meziantou.Framework.Fonts.</summary>
internal static class StandardFontMetrics
{
    /// <summary>Gets the metrics of a standard font from its name (a name <see cref="GetStandardName"/> returns).</summary>
    public static AfmFontMetrics Get(string fontName) => AfmFontMetrics.GetStandardFont(fontName) ?? throw new ArgumentException("'" + fontName + "' is not the name of a standard font", nameof(fontName));

    /// <summary>Gets the advance width of a glyph of a standard font in 1/1000 em, or <see langword="null"/> when the font has no such glyph.</summary>
    public static double? GetWidth(AfmFontMetrics metrics, string glyphName) => metrics.GetCharacter(glyphName)?.Width?.X;

    /// <summary>Gets the height of the flat capital letters, in 1/1000 em: the top of the font box when the file does not give it.</summary>
    public static double GetCapHeight(AfmFontMetrics metrics) => metrics.CapHeight ?? metrics.FontBBox?.Top ?? 0;

    /// <summary>Gets the ascender, in 1/1000 em: the top of the font box when the file does not give it (Symbol, ZapfDingbats).</summary>
    public static double GetAscender(AfmFontMetrics metrics) => metrics.Ascender ?? metrics.FontBBox?.Top ?? 0;

    /// <summary>Gets the descender, in 1/1000 em: the bottom of the font box when the file does not give it (Symbol, ZapfDingbats).</summary>
    public static double GetDescender(AfmFontMetrics metrics) => metrics.Descender ?? metrics.FontBBox?.Bottom ?? 0;

    /// <summary>Gets a value indicating whether a font has its own encoding (<c>FontSpecific</c>: Symbol and ZapfDingbats).</summary>
    public static bool IsFontSpecific(AfmFontMetrics metrics) => string.Equals(metrics.EncodingScheme, "FontSpecific", StringComparison.Ordinal);

    /// <summary>Gets the name of the standard font a <c>/BaseFont</c> designates, with the alternative names readers accept (Arial, TimesNewRoman, ...).</summary>
    public static string? GetStandardName(string baseFont)
    {
        // A subset prefix ("ABCDEF+") does not change the font
        if (baseFont.Length > 7 && baseFont[6] == '+')
        {
            baseFont = baseFont[7..];
        }

        return baseFont switch
        {
            "Courier" or "CourierNew" or "CourierNewPSMT" => "Courier",
            "Courier-Bold" or "CourierNew,Bold" or "CourierNew-Bold" or "CourierNewPS-BoldMT" => "Courier-Bold",
            "Courier-Oblique" or "CourierNew,Italic" or "CourierNew-Italic" or "CourierNewPS-ItalicMT" => "Courier-Oblique",
            "Courier-BoldOblique" or "CourierNew,BoldItalic" or "CourierNew-BoldItalic" or "CourierNewPS-BoldItalicMT" => "Courier-BoldOblique",
            "Helvetica" or "Arial" or "ArialMT" => "Helvetica",
            "Helvetica-Bold" or "Arial,Bold" or "Arial-Bold" or "Arial-BoldMT" => "Helvetica-Bold",
            "Helvetica-Oblique" or "Arial,Italic" or "Arial-Italic" or "Arial-ItalicMT" => "Helvetica-Oblique",
            "Helvetica-BoldOblique" or "Arial,BoldItalic" or "Arial-BoldItalic" or "Arial-BoldItalicMT" => "Helvetica-BoldOblique",
            "Times-Roman" or "TimesNewRoman" or "TimesNewRomanPSMT" or "Times New Roman" => "Times-Roman",
            "Times-Bold" or "TimesNewRoman,Bold" or "TimesNewRoman-Bold" or "TimesNewRomanPS-BoldMT" => "Times-Bold",
            "Times-Italic" or "TimesNewRoman,Italic" or "TimesNewRoman-Italic" or "TimesNewRomanPS-ItalicMT" => "Times-Italic",
            "Times-BoldItalic" or "TimesNewRoman,BoldItalic" or "TimesNewRoman-BoldItalic" or "TimesNewRomanPS-BoldItalicMT" => "Times-BoldItalic",
            "Symbol" => "Symbol",
            "ZapfDingbats" => "ZapfDingbats",
            _ => null,
        };
    }
}
