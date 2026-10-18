using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The classes of the fullwidth punctuation of <c>text-spacing-trim</c> (https://drafts.csswg.org/css-text-4/#text-spacing-classes).</summary>
internal enum FullwidthPunctuation : byte
{
    None,
    Opening,
    Closing,
    MiddleDot,

    /// <summary>U+3000 IDEOGRAPHIC SPACE (it is never trimmed, but it trims the adjacent punctuation).</summary>
    IdeographicSpace,
}

/// <summary>
/// How the fullwidth colon and dot punctuation are classified (https://drafts.csswg.org/css-text-4/#fullwidth-ambiguous): "the
/// UA may rely on language conventions".
/// </summary>
internal enum PunctuationConvention : byte
{
    /// <summary>Japanese and Korean: the colons are middle dots, the dots are closing punctuation.</summary>
    Japanese,

    /// <summary>Simplified Chinese: the colons and the dots are closing punctuation.</summary>
    SimplifiedChinese,

    /// <summary>Traditional Chinese: the colons and the dots are middle dots (centered).</summary>
    TraditionalChinese,
}

/// <summary>
/// The character classes of the CSS Text spacing features: the fullwidth punctuation of <c>text-spacing-trim</c>, the
/// ideographs, letters and numerals of <c>text-autospace</c> (https://drafts.csswg.org/css-text-4/#inter-script-spacing), the
/// hangable punctuation of <c>hanging-punctuation</c> and the justification opportunities of the CJK scripts.
/// </summary>
internal static class TextSpacingClasses
{
    /// <summary>Returns the class of a character for <c>text-spacing-trim</c> (https://drafts.csswg.org/css-text-4/#text-spacing-classes).</summary>
    public static FullwidthPunctuation GetFullwidthPunctuation(int codePoint, PunctuationConvention convention)
    {
        if (codePoint < 0xB7)
            return FullwidthPunctuation.None;

        switch (codePoint)
        {
            case 0x3000:
                return FullwidthPunctuation.IdeographicSpace;

            // "Also includes LEFT SINGLE QUOTATION MARK (U+2018) and LEFT DOUBLE QUOTATION MARK (U+201C)"
            case 0x2018 or 0x201C:
                return FullwidthPunctuation.Opening;
            case 0x2019 or 0x201D:
                return FullwidthPunctuation.Closing;

            // MIDDLE DOT, HYPHENATION POINT, KATAKANA MIDDLE DOT
            case 0xB7 or 0x2027 or 0x30FB:
                return FullwidthPunctuation.MiddleDot;

            // The fullwidth colon punctuation
            case 0xFF1A or 0xFF1B:
                return convention == PunctuationConvention.SimplifiedChinese ? FullwidthPunctuation.Closing : FullwidthPunctuation.MiddleDot;

            // The fullwidth dot punctuation
            case 0x3001 or 0x3002 or 0xFF0C or 0xFF0E:
                return convention == PunctuationConvention.TraditionalChinese ? FullwidthPunctuation.MiddleDot : FullwidthPunctuation.Closing;
        }

        if (codePoint < 0x3000)
            return FullwidthPunctuation.None;

        // "any opening (closing) punctuation character (Unicode category Ps (Pe)) that belongs to the CJK Symbols and Punctuation
        // block (U+3000–U+303F) or is categorized as East Asian Fullwidth (F)"
        var category = UnicodeProperties.GetGeneralCategory(codePoint);
        if (category is not (UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation))
            return FullwidthPunctuation.None;

        if (codePoint > 0x303F && UnicodeProperties.GetEastAsianWidth(codePoint) != UnicodeEastAsianWidth.Fullwidth)
            return FullwidthPunctuation.None;

        return category == UnicodeCategory.OpenPunctuation ? FullwidthPunctuation.Opening : FullwidthPunctuation.Closing;
    }

    /// <summary>
    /// Whether a character is an ideograph of <c>text-autospace</c> (https://drafts.csswg.org/css-text-4/#text-autospace-property):
    /// U+3041 to U+30FF except the punctuation, the CJK strokes, the Katakana phonetic extensions, and the Han script.
    /// </summary>
    public static bool IsIdeograph(int codePoint)
    {
        if (codePoint < 0x2E80)
            return false;

        if (codePoint is >= 0x3041 and <= 0x30FF)
            return !IsPunctuation(UnicodeProperties.GetGeneralCategory(codePoint));

        return codePoint is >= 0x31C0 and <= 0x31FF || CharacterProperties.IsHan(codePoint);
    }

    /// <summary>
    /// Whether a character is a non-ideographic letter of <c>text-autospace</c>: a letter or a mark that is not an ideograph, not
    /// East Asian Wide or Fullwidth, and not upright in vertical text.
    /// </summary>
    public static bool IsNonIdeographicLetter(int codePoint, bool upright)
    {
        if (upright)
            return false;

        if (codePoint < 0x80)
            return codePoint is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

        var category = UnicodeProperties.GetGeneralCategory(codePoint);
        if (category is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
        {
            return false;
        }

        return !IsIdeograph(codePoint) && UnicodeProperties.GetEastAsianWidth(codePoint) is not (UnicodeEastAsianWidth.Wide or UnicodeEastAsianWidth.Fullwidth);
    }

    /// <summary>Whether a character is a non-ideographic numeral of <c>text-autospace</c>: a decimal digit that is not East Asian Fullwidth, and not upright in vertical text.</summary>
    public static bool IsNonIdeographicNumeral(int codePoint, bool upright)
    {
        if (upright)
            return false;

        if (codePoint < 0x80)
            return codePoint is >= '0' and <= '9';

        return UnicodeProperties.GetGeneralCategory(codePoint) == UnicodeCategory.DecimalDigitNumber && UnicodeProperties.GetEastAsianWidth(codePoint) != UnicodeEastAsianWidth.Fullwidth;
    }

    /// <summary>
    /// Whether a character may hang at the start of the first line with <c>hanging-punctuation: first</c>: an opening bracket or
    /// quote (https://drafts.csswg.org/css-text-4/#valdef-hanging-punctuation-first): Unicode categories Ps, Pf, Pi, and the quotation marks U+0027 and U+0022.
    /// </summary>
    public static bool IsOpeningBracketOrQuote(int codePoint)
        => codePoint is 0x27 or 0x22 || UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.OpenPunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation;

    /// <summary>Whether a character may hang at the end of the last line with <c>hanging-punctuation: last</c>: a closing bracket or quote (Pe, Pf, Pi, U+0027, U+0022).</summary>
    public static bool IsClosingBracketOrQuote(int codePoint)
        => codePoint is 0x27 or 0x22 || UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation;

    /// <summary>
    /// Whether a character is a stop or a comma that may hang at the end of a line with <c>hanging-punctuation: force-end |
    /// allow-end</c> (https://drafts.csswg.org/css-text-4/#hanging-punctuation-property).
    /// </summary>
    public static bool IsStopOrComma(int codePoint) => codePoint is 0x2C or 0x2E or 0x60C or 0x6D4 or 0x3001 or 0x3002 or 0xFF0C or 0xFF0E or 0xFE50 or 0xFE51 or 0xFE52 or 0xFF61 or 0xFF64;

    /// <summary>
    /// Whether the justification may expand the space after a character of a CJK script with <c>text-justify: auto</c>
    /// (https://drafts.csswg.org/css-text-4/#justify-auto): the scripts without word separators are justified between their
    /// characters ("inter-character" justification for the ideographs, the kana and the CJK and fullwidth punctuation).
    /// </summary>
    public static bool IsCjkJustificationCharacter(int codePoint)
        => codePoint >= 0x2E80 && (IsIdeograph(codePoint) || codePoint is (>= 0x3000 and <= 0x30FF) or (>= 0xFF00 and <= 0xFFEF));

    private static bool IsPunctuation(UnicodeCategory category) => category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
}
