namespace Meziantou.Framework;

/// <summary>Converts a text to uppercase or to lowercase, with the case mappings that depend on the context and on the language.</summary>
/// <remarks>
/// <para>
/// The conversions are the full case conversions of the Unicode Standard (section 3.13, toUppercase and toLowercase)
/// for <see cref="UnicodeProperties.UnicodeVersion"/>: the mappings of SpecialCasing.txt, else the simple mappings of
/// UnicodeData.txt, a code point being able to map to several ones. The conditional mappings of SpecialCasing.txt are
/// applied:
/// </para>
/// <list type="bullet">
/// <item><description>
/// In every language, a capital sigma (U+03A3) lowercases to the final sigma (U+03C2) at the end of a word, when it is
/// preceded by a cased letter and not followed by one, ignoring the case-ignorable characters between them (Final_Sigma);
/// else to the small sigma (U+03C3). A character that is both cased and case-ignorable, such as the modifier letter
/// U+02B0, is ignored, as ICU does.
/// </description></item>
/// <item><description>
/// In Turkish and Azerbaijani (<c>tr</c>, <c>az</c>), <c>I</c> lowercases to the dotless <c>i</c> (U+0131), the
/// dotted capital I (U+0130) to <c>i</c>, and <c>I</c> followed by a combining dot above (U+0307) to <c>i</c>;
/// <c>i</c> uppercases to the dotted capital I (U+0130).
/// </description></item>
/// <item><description>
/// In Lithuanian (<c>lt</c>), the lowercase <c>i</c> keeps its dot under an accent: <c>I</c>, <c>J</c> and the I with
/// ogonek (U+012E) followed by an accent above lowercase with a combining dot above (U+0307), and the I with grave,
/// acute or tilde (U+00CC, U+00CD, U+0128) to <c>i</c>, a dot above and the accent. Uppercasing removes the combining
/// dot above that follows a soft-dotted letter such as <c>i</c> or <c>j</c>, with no character of combining class 0 or
/// 230 between them.
/// </description></item>
/// </list>
/// <para>
/// The other languages have no tailoring in SpecialCasing.txt: the mappings of the Common Locale Data Repository, such
/// as the removal of the Greek accents when uppercasing, are not applied.
/// </para>
/// <para>
/// The conversions do not use the globalization library of the platform: the results are the same on every platform
/// and every framework version, including in globalization-invariant mode. An unpaired surrogate is copied as is.
/// </para>
/// </remarks>
/// <seealso href="https://www.unicode.org/versions/latest/core-spec/chapter-3/#G33992" />
public static class UnicodeCaseMapping
{
    private const int CombiningDotAbove = 0x0307;
    private const int CapitalSigma = 0x03A3;

    /// <summary>Converts a text to uppercase.</summary>
    /// <param name="value">The text.</param>
    /// <param name="language">
    /// The language of the text, as a BCP 47 language tag such as <c>tr</c> or <c>lt-LT</c>, or <see langword="null"/>
    /// for the mappings that do not depend on the language. Only the language subtag is read, ignoring case:
    /// <c>tr</c>, <c>az</c> and <c>lt</c> (or <c>tur</c>, <c>aze</c> and <c>lit</c>) select their mappings, and every
    /// other language uses the mappings that do not depend on the language.
    /// </param>
    /// <returns>The text in uppercase; <paramref name="value"/> itself when no character changes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToUpper(string value, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert(value, GetLanguage(language), toUpper: true);
    }

    /// <summary>Converts a text to lowercase.</summary>
    /// <param name="value">The text.</param>
    /// <param name="language">
    /// The language of the text, as a BCP 47 language tag such as <c>tr</c> or <c>lt-LT</c>, or <see langword="null"/>
    /// for the mappings that do not depend on the language. Only the language subtag is read, ignoring case:
    /// <c>tr</c>, <c>az</c> and <c>lt</c> (or <c>tur</c>, <c>aze</c> and <c>lit</c>) select their mappings, and every
    /// other language uses the mappings that do not depend on the language.
    /// </param>
    /// <returns>The text in lowercase; <paramref name="value"/> itself when no character changes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToLower(string value, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert(value, GetLanguage(language), toUpper: false);
    }

    private static CaseLanguage GetLanguage(string? language)
    {
        if (language is null)
            return CaseLanguage.Default;

        var subtag = language.AsSpan();
        var end = subtag.IndexOfAny('-', '_');
        if (end >= 0)
        {
            subtag = subtag[..end];
        }

        if (Ascii.EqualsIgnoreCase(subtag, "tr") || Ascii.EqualsIgnoreCase(subtag, "az") || Ascii.EqualsIgnoreCase(subtag, "tur") || Ascii.EqualsIgnoreCase(subtag, "aze"))
            return CaseLanguage.Turkic;

        if (Ascii.EqualsIgnoreCase(subtag, "lt") || Ascii.EqualsIgnoreCase(subtag, "lit"))
            return CaseLanguage.Lithuanian;

        return CaseLanguage.Default;
    }

    private static string Convert(string value, CaseLanguage language, bool toUpper)
    {
        // The ASCII characters that are already in the target case are copied as is, without looking them up.
        var index = 0;
        while (index < value.Length && value[index] < 0x80 && !(toUpper ? char.IsAsciiLetterLower(value[index]) : char.IsAsciiLetterUpper(value[index])))
        {
            index++;
        }

        if (index == value.Length)
            return value;

        var result = new StringBuilder(value.Length + 16);
        result.Append(value, 0, index);
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        while (index < value.Length)
        {
            var codePoint = Unicode.DecodeAt(value, index, out var length);
            var count = toUpper
                ? GetUppercaseMapping(value, index, codePoint, language, mapping)
                : GetLowercaseMapping(value, index, length, codePoint, language, mapping);
            foreach (var mapped in mapping[..count])
            {
                if (mapped < 0x10000)
                {
                    // A lone surrogate is its own mapping: it is copied as is.
                    result.Append((char)mapped);
                }
                else
                {
                    result.Append((char)(0xD7C0 + (mapped >> 10)));
                    result.Append((char)(0xDC00 | (mapped & 0x3FF)));
                }
            }

            index += length;
        }

        return result.Equals(value.AsSpan()) ? value : result.ToString();
    }

    private static int GetUppercaseMapping(ReadOnlySpan<char> text, int index, int codePoint, CaseLanguage language, Span<int> destination)
    {
        switch (codePoint)
        {
            case 'i' when language is CaseLanguage.Turkic:
                destination[0] = 0x0130;
                return 1;

            case CombiningDotAbove when language is CaseLanguage.Lithuanian && IsAfterSoftDotted(text, index):
                return 0;

            case < 0x80:
                destination[0] = char.IsAsciiLetterLower((char)codePoint) ? codePoint - 0x20 : codePoint;
                return 1;

            default:
                return UnicodeProperties.GetUppercaseMapping(codePoint, destination);
        }
    }

    private static int GetLowercaseMapping(ReadOnlySpan<char> text, int index, int length, int codePoint, CaseLanguage language, Span<int> destination)
    {
        switch (codePoint)
        {
            case CapitalSigma:
                destination[0] = IsFinalSigma(text, index, index + length) ? 0x03C2 : 0x03C3;
                return 1;

            // Turkish and Azerbaijani
            case 0x0130 when language is CaseLanguage.Turkic:
                destination[0] = 'i';
                return 1;

            case CombiningDotAbove when language is CaseLanguage.Turkic && IsAfterCapitalI(text, index):
                return 0;

            case 'I' when language is CaseLanguage.Turkic && !IsBeforeDotAbove(text, index + length):
                destination[0] = 0x0131;
                return 1;

            // Lithuanian
            case 'I' or 'J' or 0x012E when language is CaseLanguage.Lithuanian && IsBeforeMoreAbove(text, index + length):
                destination[0] = UnicodeProperties.ToSimpleLowercase(codePoint);
                destination[1] = CombiningDotAbove;
                return 2;

            case 0x00CC or 0x00CD or 0x0128 when language is CaseLanguage.Lithuanian:
                destination[0] = 'i';
                destination[1] = CombiningDotAbove;
                destination[2] = codePoint switch
                {
                    0x00CC => 0x0300, // grave
                    0x00CD => 0x0301, // acute
                    _ => 0x0303, // tilde
                };
                return 3;

            case < 0x80:
                destination[0] = char.IsAsciiLetterUpper((char)codePoint) ? codePoint + 0x20 : codePoint;
                return 1;

            default:
                return UnicodeProperties.GetLowercaseMapping(codePoint, destination);
        }
    }

    /// <summary>
    /// Final_Sigma: the character is preceded by a cased letter and zero or more case-ignorable characters, and is not
    /// followed by zero or more case-ignorable characters and a cased letter. A character that is both case-ignorable
    /// and cased is case-ignorable, as in ICU.
    /// </summary>
    private static bool IsFinalSigma(ReadOnlySpan<char> text, int start, int end)
    {
        while (start > 0)
        {
            var codePoint = Unicode.DecodeBefore(text, start, out var length);
            if (!UnicodeProperties.IsCaseIgnorable(codePoint))
                return UnicodeProperties.IsCased(codePoint) && !IsFollowedByCased(text, end);

            start -= length;
        }

        return false;

        static bool IsFollowedByCased(ReadOnlySpan<char> text, int index)
        {
            while (index < text.Length)
            {
                var codePoint = Unicode.DecodeAt(text, index, out var length);
                if (!UnicodeProperties.IsCaseIgnorable(codePoint))
                    return UnicodeProperties.IsCased(codePoint);

                index += length;
            }

            return false;
        }
    }

    /// <summary>After_Soft_Dotted: a soft-dotted character comes before, with no character of combining class 0 or 230 between them.</summary>
    private static bool IsAfterSoftDotted(ReadOnlySpan<char> text, int index)
    {
        while (index > 0)
        {
            var codePoint = Unicode.DecodeBefore(text, index, out var length);
            if (UnicodeProperties.IsSoftDotted(codePoint))
                return true;

            if (UnicodeProperties.GetCanonicalCombiningClass(codePoint) is 0 or 230)
                return false;

            index -= length;
        }

        return false;
    }

    /// <summary>After_I: a capital I comes before, with no character of combining class 0 or 230 between them.</summary>
    private static bool IsAfterCapitalI(ReadOnlySpan<char> text, int index)
    {
        while (index > 0)
        {
            var codePoint = Unicode.DecodeBefore(text, index, out var length);
            if (codePoint == 'I')
                return true;

            if (UnicodeProperties.GetCanonicalCombiningClass(codePoint) is 0 or 230)
                return false;

            index -= length;
        }

        return false;
    }

    /// <summary>More_Above: a character of combining class 230 follows, with no character of combining class 0 or 230 between them.</summary>
    private static bool IsBeforeMoreAbove(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            var combiningClass = UnicodeProperties.GetCanonicalCombiningClass(Unicode.DecodeAt(text, index, out var length));
            if (combiningClass is 230)
                return true;

            if (combiningClass is 0)
                return false;

            index += length;
        }

        return false;
    }

    /// <summary>Before_Dot: a combining dot above (U+0307) follows, with no character of combining class 0 or 230 between them.</summary>
    private static bool IsBeforeDotAbove(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            var codePoint = Unicode.DecodeAt(text, index, out var length);
            if (codePoint == CombiningDotAbove)
                return true;

            if (UnicodeProperties.GetCanonicalCombiningClass(codePoint) is 0 or 230)
                return false;

            index += length;
        }

        return false;
    }

    private enum CaseLanguage
    {
        Default,
        Turkic,
        Lithuanian,
    }
}
