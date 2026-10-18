using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The Unicode properties used by the shaping, with the adjustments of HarfBuzz (hb-unicode.hh and hb-unicode.cc, MIT license):
/// the default ignorable characters that stay visible, the modified combining classes that reorder the marks of some scripts,
/// the fallback of the spaces and the vertical forms of the punctuation.
/// </summary>
internal static class ShapingUnicode
{
    /// <summary>
    /// The modified combining class of each canonical combining class (HarfBuzz's <c>_hb_modified_combining_class</c>): the
    /// fixed-position classes of Hebrew, Arabic, Syriac, Telugu, Thai, Lao and Tibetan are reordered so that the marks are
    /// sorted in the order the fonts expect.
    /// </summary>
    private static ReadOnlySpan<byte> ModifiedCombiningClasses =>
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9,

        // Hebrew (10-26)
        22, 15, 16, 17, 23, 18, 19, 20, 21, 14, 24, 12, 25, 13, 10, 11, 26,

        // Arabic (27-35)
        28, 29, 30, 31, 32, 33, 27, 34, 35,

        // Syriac (36)
        36,

        37, 38, 39,
        40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59,
        60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79,
        80, 81, 82, 83,

        // Telugu (84, 91)
        4, 85, 86, 87, 88, 89, 90, 5, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102,

        // Thai (103, 107)
        3, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117,

        // Lao (118, 122)
        118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128,

        // Tibetan (129, 130, 132)
        129, 132, 131, 131, 133, 134, 135, 136, 137, 138, 139,

        140, 141, 142, 143, 144, 145, 146, 147, 148, 149,
        150, 151, 152, 153, 154, 155, 156, 157, 158, 159,
        160, 161, 162, 163, 164, 165, 166, 167, 168, 169,
        170, 171, 172, 173, 174, 175, 176, 177, 178, 179,
        180, 181, 182, 183, 184, 185, 186, 187, 188, 189,
        190, 191, 192, 193, 194, 195, 196, 197, 198, 199,
        200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213,
        214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234,
        235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255,
    ];

    /// <summary>The modified combining class of a code point (HarfBuzz's <c>modified_combining_class</c>).</summary>
    public static int GetModifiedCombiningClass(int codePoint) => codePoint switch
    {
        // Reorder SAKOT after the tone marks, PADMA after the vowel marks, TSA -PHRU before U+0F74
        0x1A60 or 0x0FC6 => 254,
        0x0F39 => 127,
        _ => ModifiedCombiningClasses[UnicodeProperties.GetCanonicalCombiningClass(codePoint)],
    };

    /// <summary>
    /// Whether a code point is a default ignorable character for the shaping: Default_Ignorable_Code_Point, except the Hangul
    /// fillers U+115F, U+1160, U+3164 and U+FFA0 and the shorthand format controls U+1BCA0-1BCA3, which have glyphs (as HarfBuzz).
    /// </summary>
    public static bool IsDefaultIgnorable(int codePoint) => UnicodeProperties.IsDefaultIgnorable(codePoint) && codePoint is not (0x115F or 0x1160 or 0x3164 or 0xFFA0 or (>= 0x1BCA0 and <= 0x1BCA3));

    /// <summary>Whether a code point is a variation selector (the Mongolian free variation selectors are handled by the Arabic shaper).</summary>
    public static bool IsVariationSelector(int codePoint) => codePoint is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>The fallback of a space character that the font does not support (HarfBuzz's <c>space_fallback_type</c>).</summary>
    public static SpaceFallback GetSpaceFallback(int codePoint) => codePoint switch
    {
        0x0020 or 0x00A0 => SpaceFallback.Space,
        0x2000 or 0x2002 => SpaceFallback.Em2,
        0x2001 or 0x2003 or 0x3000 => SpaceFallback.Em,
        0x2004 => SpaceFallback.Em3,
        0x2005 => SpaceFallback.Em4,
        0x2006 => SpaceFallback.Em6,
        0x2007 => SpaceFallback.Figure,
        0x2008 => SpaceFallback.Punctuation,
        0x2009 => SpaceFallback.Em5,
        0x200A => SpaceFallback.Em16,
        0x202F => SpaceFallback.Narrow,
        0x205F => SpaceFallback.FourEm18,
        _ => SpaceFallback.NotSpace,
    };

    /// <summary>The vertical presentation form of a punctuation for the fonts without <c>vert</c> feature (HarfBuzz's <c>vertical_char_for</c>), or the code point itself.</summary>
    public static int GetVerticalCharacter(int codePoint) => codePoint switch
    {
        0x2013 => 0xFE32,
        0x2014 => 0xFE31,
        0x2025 => 0xFE30,
        0x2026 => 0xFE19,
        0x3001 => 0xFE11,
        0x3002 => 0xFE12,
        0x3008 => 0xFE3F,
        0x3009 => 0xFE40,
        0x300A => 0xFE3D,
        0x300B => 0xFE3E,
        0x300C => 0xFE41,
        0x300D => 0xFE42,
        0x300E => 0xFE43,
        0x300F => 0xFE44,
        0x3010 => 0xFE3B,
        0x3011 => 0xFE3C,
        0x3014 => 0xFE39,
        0x3015 => 0xFE3A,
        0x3016 => 0xFE17,
        0x3017 => 0xFE18,
        0xFE4F => 0xFE34,
        0xFF01 => 0xFE15,
        0xFF08 => 0xFE35,
        0xFF09 => 0xFE36,
        0xFF0C => 0xFE10,
        0xFF1A => 0xFE13,
        0xFF1B => 0xFE14,
        0xFF1F => 0xFE16,
        0xFF3B => 0xFE47,
        0xFF3D => 0xFE48,
        0xFF3F => 0xFE33,
        0xFF5B => 0xFE37,
        0xFF5D => 0xFE38,
        _ => codePoint,
    };
}
