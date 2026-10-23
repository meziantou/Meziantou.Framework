using System.Collections.Concurrent;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>The metrics of the standard 14 fonts (ISO 32000-2, 9.6.2.2), read once from the vendored Adobe Core14 AFM files.</summary>
internal static class StandardFontMetrics
{
    private static readonly ConcurrentDictionary<string, AfmFontMetrics> Cache = new(StringComparer.Ordinal);

    public static AfmFontMetrics Get(string fontName) => Cache.GetOrAdd(fontName, AfmFontMetrics.Load);

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
