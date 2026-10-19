using System.Globalization;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The properties of a code point that the layout reads in another form than <see cref="UnicodeProperties"/> gives them
/// (the short names of the classes of UAX #9 and UAX #14, sentinels instead of <c>Try</c> methods), or that are derived from
/// several properties. The data is the one of Meziantou.Framework.Unicode, which does not depend on the OS or ICU.
/// </summary>
internal static class CharacterProperties
{
    /// <summary>The Bidi_Class property (https://www.unicode.org/reports/tr9/#Bidirectional_Character_Types).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BidiClass GetBidiClass(int codePoint) => (BidiClass)UnicodeProperties.GetBidiClass(codePoint);

    /// <summary>The Line_Break property (https://www.unicode.org/reports/tr14/#Properties), before the resolution of rule LB1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LineBreakClass GetLineBreakClass(int codePoint) => (LineBreakClass)UnicodeProperties.GetLineBreakClass(codePoint);

    /// <summary>The Bidi_Paired_Bracket property: the opening or closing bracket paired with the code point, or the code point itself when its Bidi_Paired_Bracket_Type is None.</summary>
    public static int GetBidiPairedBracket(int codePoint)
    {
        UnicodeProperties.TryGetBidiPairedBracket(codePoint, out var paired);
        return paired;
    }

    /// <summary>
    /// The Bidi_Mirroring_Glyph property (rule L4, https://www.unicode.org/reports/tr9/#L4): the code point whose glyph is the mirror
    /// image of the glyph of <paramref name="codePoint"/>, or -1 when there is none (some Bidi_Mirrored characters must be
    /// mirrored by the font, e.g. with the OpenType <c>rtlm</c> feature).
    /// </summary>
    public static int GetBidiMirroringGlyph(int codePoint) => UnicodeProperties.TryGetBidiMirroringGlyph(codePoint, out var mirrored) ? mirrored : -1;

    /// <summary>Whether the General_Category of the code point is a mark (Mn, Mc or Me).</summary>
    public static bool IsMark(int codePoint) => UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>Whether a code point has the Script property Han (https://www.unicode.org/reports/tr24/).</summary>
    public static bool IsHan(int codePoint) => codePoint >= 0x2E80 && UnicodeScripts.GetScript(codePoint) == UnicodeScript.Han;

    /// <summary>Whether the Script property of the code point is Hangul.</summary>
    public static bool IsHangul(int codePoint) => codePoint >= 0x1100 && UnicodeScripts.GetScript(codePoint) == UnicodeScript.Hangul;

    /// <summary>Whether the East_Asian_Width is Fullwidth, Wide or Halfwidth (<c>$EastAsian</c> of UAX #14).</summary>
    public static bool IsEastAsianFullwidthWideOrHalfwidth(int codePoint) => UnicodeProperties.GetEastAsianWidth(codePoint) is UnicodeEastAsianWidth.Fullwidth or UnicodeEastAsianWidth.Wide or UnicodeEastAsianWidth.Halfwidth;

    /// <summary>
    /// Whether a code point is an Emoji Presentation Participating Code Point (https://drafts.csswg.org/css-fonts-4/#emoji-presentation-participating-code-points):
    /// an emoji character (the Emoji property, https://www.unicode.org/reports/tr51/#Emoji_Properties), which can be followed by
    /// a presentation selector (as Chrome).
    /// </summary>
    public static bool IsEmojiPresentationParticipating(int codePoint) => UnicodeEmoji.IsEmoji(codePoint);

    /// <summary>Whether a code point is a variation selector (https://www.unicode.org/versions/latest/core-spec/chapter-23/#G19053): VS1 to VS256.</summary>
    public static bool IsVariationSelector(int codePoint) => codePoint is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF) or (>= 0x180B and <= 0x180D) or 0x180F;
}
