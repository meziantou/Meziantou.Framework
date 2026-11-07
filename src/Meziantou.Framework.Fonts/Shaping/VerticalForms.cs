namespace Meziantou.Framework.Fonts;

/// <summary>
/// The presentation forms of the punctuation of East Asian text in a vertical line (the "Vertical Forms" block and
/// the vertical forms of the "CJK Compatibility Forms" block of the Unicode Standard), for the fonts that have them as
/// characters and no <c>vert</c> feature.
/// </summary>
/// <remarks>
/// Each form is the one whose name is "PRESENTATION FORM FOR VERTICAL" and the name of the character, or of the
/// character its full-width form decomposes to (<c>&lt;vertical&gt;</c> and <c>&lt;wide&gt;</c> decompositions of
/// UnicodeData.txt): the full-width comma U+FF0C has the vertical comma U+FE10.
/// </remarks>
internal static class VerticalForms
{
    /// <summary>Gets the vertical presentation form of a character, or the character itself when it has none.</summary>
    public static int Get(int codePoint) => codePoint switch
    {
        0x2013 => 0xFE32, // EN DASH
        0x2014 => 0xFE31, // EM DASH
        0x2025 => 0xFE30, // TWO DOT LEADER
        0x2026 => 0xFE19, // HORIZONTAL ELLIPSIS
        0x3001 => 0xFE11, // IDEOGRAPHIC COMMA
        0x3002 => 0xFE12, // IDEOGRAPHIC FULL STOP
        0x3008 => 0xFE3F, // LEFT ANGLE BRACKET
        0x3009 => 0xFE40, // RIGHT ANGLE BRACKET
        0x300A => 0xFE3D, // LEFT DOUBLE ANGLE BRACKET
        0x300B => 0xFE3E, // RIGHT DOUBLE ANGLE BRACKET
        0x300C => 0xFE41, // LEFT CORNER BRACKET
        0x300D => 0xFE42, // RIGHT CORNER BRACKET
        0x300E => 0xFE43, // LEFT WHITE CORNER BRACKET
        0x300F => 0xFE44, // RIGHT WHITE CORNER BRACKET
        0x3010 => 0xFE3B, // LEFT BLACK LENTICULAR BRACKET
        0x3011 => 0xFE3C, // RIGHT BLACK LENTICULAR BRACKET
        0x3014 => 0xFE39, // LEFT TORTOISE SHELL BRACKET
        0x3015 => 0xFE3A, // RIGHT TORTOISE SHELL BRACKET
        0x3016 => 0xFE17, // LEFT WHITE LENTICULAR BRACKET
        0x3017 => 0xFE18, // RIGHT WHITE LENTICULAR BRACKET
        0xFE4F => 0xFE34, // WAVY LOW LINE
        0xFF01 => 0xFE15, // FULLWIDTH EXCLAMATION MARK
        0xFF08 => 0xFE35, // FULLWIDTH LEFT PARENTHESIS
        0xFF09 => 0xFE36, // FULLWIDTH RIGHT PARENTHESIS
        0xFF0C => 0xFE10, // FULLWIDTH COMMA
        0xFF1A => 0xFE13, // FULLWIDTH COLON
        0xFF1B => 0xFE14, // FULLWIDTH SEMICOLON
        0xFF1F => 0xFE16, // FULLWIDTH QUESTION MARK
        0xFF3B => 0xFE47, // FULLWIDTH LEFT SQUARE BRACKET
        0xFF3D => 0xFE48, // FULLWIDTH RIGHT SQUARE BRACKET
        0xFF3F => 0xFE33, // FULLWIDTH LOW LINE
        0xFF5B => 0xFE37, // FULLWIDTH LEFT CURLY BRACKET
        0xFF5D => 0xFE38, // FULLWIDTH RIGHT CURLY BRACKET
        _ => codePoint,
    };
}
