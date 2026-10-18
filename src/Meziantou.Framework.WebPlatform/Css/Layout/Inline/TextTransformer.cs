using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The languages whose case mappings are tailored (SpecialCasing.txt and CLDR), see <see cref="TextTransformer"/>.</summary>
internal enum CaseLanguage : byte
{
    None,

    /// <summary>Turkish and Azeri: the dotted and dotless i.</summary>
    Turkic,

    /// <summary>Lithuanian: the dot above of the soft-dotted letters that have an accent.</summary>
    Lithuanian,

    /// <summary>Greek: the uppercase letters lose their accents.</summary>
    Greek,

    /// <summary>Dutch: the digraph IJ at the start of a word.</summary>
    Dutch,
}

/// <summary>
/// The text transformations of <c>text-transform</c> (https://drafts.csswg.org/css-text-4/#text-transform-property), applied
/// character by character (with the characters around it) after the white space processing (phase I) by the inline layout
/// and by <c>innerText</c>.
/// </summary>
/// <remarks>
/// The case mappings are the full mappings of Unicode (<see cref="CaseMapping"/>: the simple mappings plus the unconditional
/// mappings of SpecialCasing.txt, e.g. <c>ß</c> → <c>SS</c>, <c>ﬁ</c> → <c>FI</c>), with the conditional mappings of
/// SpecialCasing.txt (the final sigma, the Lithuanian dot above, the Turkic dotted and dotless i) and the tailorings of CLDR
/// for the content language (<see cref="CaseLanguage"/>: the Greek uppercase letters without accent, the Dutch IJ).
/// <c>math-auto</c> maps the character of a text node made of a single character to its mathematical italic form (see
/// <see cref="ResolveMathAuto"/>).
/// </remarks>
internal static class TextTransformer
{
    /// <summary>The maximum number of characters a character (with the marks transformed with it) is transformed to.</summary>
    public const int MaxLength = 8;

    private const int CombiningDotAbove = 0x0307;

    /// <summary>
    /// Writes the transformed form of <paramref name="c"/> to <paramref name="destination"/> (at least <see cref="CaseMapping.MaxLength"/>
    /// characters) and returns the number of characters written: the transformation of a character without its context.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <param name="transform">The value of <c>text-transform</c>.</param>
    /// <param name="turkic">Whether the content language is Turkish or Azeri (see <see cref="UsesTurkicCasing(string?)"/>).</param>
    /// <param name="wordStart">Whether the character starts a word (<c>capitalize</c>): the previous character is not a word character (<see cref="IsWordCharacter(char)"/>).</param>
    /// <param name="destination">The buffer that receives the transformed characters.</param>
    public static int Transform(char c, TextTransform transform, bool turkic, bool wordStart, Span<char> destination)
        => Transform(new ReadOnlySpan<char>(in c), 0, transform, turkic ? CaseLanguage.Turkic : CaseLanguage.None, wordStart, destination, out _);

    /// <summary>
    /// Writes the transformed form of the character at <paramref name="index"/> of <paramref name="text"/> to
    /// <paramref name="destination"/> (at least <see cref="MaxLength"/> characters) and returns the number of characters
    /// written. The characters around it give the context of the conditional case mappings.
    /// </summary>
    /// <param name="text">The text (of a text node).</param>
    /// <param name="index">The index of the character: not the low surrogate of a surrogate pair.</param>
    /// <param name="transform">The value of <c>text-transform</c>.</param>
    /// <param name="language">The content language (see <see cref="GetCaseLanguage(string?)"/>).</param>
    /// <param name="wordStart">Whether the character starts a word (<c>capitalize</c>): the previous character is not a word character (<see cref="IsWordCharacter(ReadOnlySpan{char}, int)"/>).</param>
    /// <param name="destination">The buffer that receives the transformed characters.</param>
    /// <param name="consumed">
    /// The number of UTF-16 code units of <paramref name="text"/> that are transformed: a surrogate pair, or a letter with the
    /// following characters that are transformed with it (e.g. the accents of a Greek letter, the J of the Dutch IJ).
    /// </param>
    public static int Transform(ReadOnlySpan<char> text, int index, TextTransform transform, CaseLanguage language, bool wordStart, Span<char> destination, out int consumed)
    {
        var codePoint = GetCodePoint(text, index, out consumed);
        int count;
        var upper = (transform & TextTransform.Uppercase) != TextTransform.None;

        // https://drafts.csswg.org/css-text-4/#valdef-text-transform-capitalize: the first typographic letter unit of each word
        var title = !upper && (transform & TextTransform.Capitalize) != TextTransform.None && wordStart && IsLetter(codePoint);
        if (upper || title)
        {
            count = ToUpperOrTitle(text, index, codePoint, upper, language, destination, ref consumed);
        }
        else if ((transform & TextTransform.Lowercase) != TextTransform.None)
        {
            count = ToLower(text, index, codePoint, language, destination, ref consumed);
        }
        else if ((transform & TextTransform.MathAuto) != TextTransform.None && codePoint <= char.MaxValue && !char.IsSurrogate((char)codePoint))
        {
            // https://w3c.github.io/mathml-core/#the-math-auto-transform: the italic mappings (the original characters are in the BMP)
            count = new Rune(MathItalicMapping.GetItalic((char)codePoint)).EncodeToUtf16(destination);
        }
        else
        {
            count = WriteCodePoint(codePoint, destination);
        }

        if ((transform & TextTransform.FullWidth) != TextTransform.None)
        {
            for (var i = 0; i < count; i++)
            {
                destination[i] = ToFullWidth(destination[i]);
            }
        }

        if ((transform & TextTransform.FullSizeKana) != TextTransform.None)
        {
            if (count == 2 && char.IsSurrogatePair(destination[0], destination[1]))
            {
                var kana = ToFullSizeKana(char.ConvertToUtf32(destination[0], destination[1]));
                if (kana != 0)
                {
                    destination[0] = (char)kana;
                    count = 1;
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    destination[i] = ToFullSizeKana(destination[i]);
                }
            }
        }

        return count;
    }

    private static int GetCodePoint(ReadOnlySpan<char> text, int index, out int length)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        length = 1;
        return c;
    }

    private static int GetCodePointBefore(ReadOnlySpan<char> text, int index, out int length)
    {
        var c = text[index - 1];
        if (char.IsLowSurrogate(c) && index >= 2 && char.IsHighSurrogate(text[index - 2]))
        {
            length = 2;
            return char.ConvertToUtf32(text[index - 2], c);
        }

        length = 1;
        return c;
    }

    private static int WriteCodePoint(int codePoint, Span<char> destination)
    {
        if (codePoint <= char.MaxValue)
        {
            destination[0] = (char)codePoint;
            return 1;
        }

        return new Rune(codePoint).EncodeToUtf16(destination);
    }

    private static bool IsLetter(int codePoint) => UnicodeProperties.GetGeneralCategory(codePoint) <= UnicodeCategory.OtherLetter;

    // The uppercase mapping (or the titlecase mapping of the first letter of a word) with the mappings of the language
    private static int ToUpperOrTitle(ReadOnlySpan<char> text, int index, int codePoint, bool upper, CaseLanguage language, Span<char> destination, ref int consumed)
    {
        const char DottedCapitalI = (char)0x0130;
        switch (language)
        {
            case CaseLanguage.Turkic when codePoint == 'i':
                destination[0] = DottedCapitalI;
                return 1;

            case CaseLanguage.Lithuanian when IsSoftDotted(codePoint):
                // SpecialCasing.txt, "lt After_Soft_Dotted": the dot above of a soft-dotted letter (kept before its accents in
                // lowercase) is removed in uppercase and titlecase
                if (FindDotAbove(text, index + consumed) is var dot and >= 0)
                {
                    var length = upper ? CaseMapping.ToUpper(codePoint, destination) : CaseMapping.ToTitle(codePoint, destination);
                    for (var i = index + consumed; i < dot && length < MaxLength; i++)
                    {
                        destination[length++] = text[i];
                    }

                    consumed = dot + 1 - index;
                    return length;
                }

                break;

            case CaseLanguage.Greek when upper:
                if (TryGreekUpper(text, index, codePoint, destination, ref consumed, out var greekLength))
                    return greekLength;

                break;

            case CaseLanguage.Dutch when !upper && codePoint is 'i' or 'I' && index + 1 < text.Length && text[index + 1] is 'j' or 'J':
                // CLDR: the digraph IJ is capitalized as a whole at the start of a word
                destination[0] = 'I';
                destination[1] = 'J';
                consumed = 2;
                return 2;
        }

        return upper ? CaseMapping.ToUpper(codePoint, destination) : CaseMapping.ToTitle(codePoint, destination);
    }

    // The lowercase mapping with the conditional mappings of SpecialCasing.txt
    private static int ToLower(ReadOnlySpan<char> text, int index, int codePoint, CaseLanguage language, Span<char> destination, ref int consumed)
    {
        const char DottedCapitalI = (char)0x0130;
        const char DotlessSmallI = (char)0x0131;
        const int CapitalSigma = 0x03A3;
        const char FinalSigma = (char)0x03C2;
        switch (language)
        {
            case CaseLanguage.Turkic when codePoint == 'I':
                // "tr After_I": the dot above after I is removed (I with a dot above is the dotted i); "tr Not_Before_Dot"
                if (FindDotAbove(text, index + consumed) is var dot and >= 0)
                {
                    destination[0] = 'i';
                    var length = 1;
                    for (var i = index + consumed; i < dot && length < MaxLength; i++)
                    {
                        destination[length++] = text[i];
                    }

                    consumed = dot + 1 - index;
                    return length;
                }

                destination[0] = DotlessSmallI;
                return 1;

            case CaseLanguage.Turkic when codePoint == DottedCapitalI:
                destination[0] = 'i';
                return 1;

            case CaseLanguage.Lithuanian:
                // "lt More_Above": the dot above is kept on the lowercase i when an accent is above it
                switch (codePoint)
                {
                    case 'I' or 'J' or 0x012E when IsFollowedByMarkAbove(text, index + consumed):
                        var length = CaseMapping.ToLower(codePoint, destination);
                        destination[length++] = (char)CombiningDotAbove;
                        return length;

                    case 0x00CC or 0x00CD or 0x0128:
                        destination[0] = 'i';
                        destination[1] = (char)CombiningDotAbove;
                        destination[2] = codePoint switch
                        {
                            0x00CC => (char)0x0300,
                            0x00CD => (char)0x0301,
                            _ => (char)0x0303,
                        };
                        return 3;
                }

                break;
        }

        // "Final_Sigma": the sigma at the end of a word
        if (codePoint == CapitalSigma && IsAfterCasedLetter(text, index) && !IsBeforeCasedLetter(text, index + consumed))
        {
            destination[0] = FinalSigma;
            return 1;
        }

        return CaseMapping.ToLower(codePoint, destination);
    }

    /// <summary>
    /// The index of the U+0307 COMBINING DOT ABOVE that follows a letter (from <paramref name="start"/>, the end of the
    /// letter) "with no intervening character of combining class 0 or 230 (Above)", or -1.
    /// </summary>
    private static int FindDotAbove(ReadOnlySpan<char> text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == CombiningDotAbove)
                return i;

            if (UnicodeProperties.GetCanonicalCombiningClass(text[i]) is 0 or 230)
                return -1;
        }

        return -1;
    }

    /// <summary>"More_Above": the character is followed by a character of combining class 230 (Above) with no intervening character of combining class 0 or 230.</summary>
    private static bool IsFollowedByMarkAbove(ReadOnlySpan<char> text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            switch (UnicodeProperties.GetCanonicalCombiningClass(text[i]))
            {
                case 230:
                    return true;
                case 0:
                    return false;
            }
        }

        return false;
    }

    /// <summary>The characters with the Soft_Dotted property (PropList.txt) of the Latin, Greek and Cyrillic scripts.</summary>
    private static bool IsSoftDotted(int codePoint) => codePoint is 'i' or 'j' or 0x012F or 0x0249 or 0x0268 or 0x029D or 0x02B2 or 0x03F3 or 0x0456 or 0x0458
        or 0x1D62 or 0x1D96 or 0x1DA4 or 0x1DA8 or 0x1E2D or 0x1ECB or 0x2071 or 0x2148 or 0x2149 or 0x2C7C;

    private static bool IsCased(int codePoint)
        => UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter;

    /// <summary>Case_Ignorable: the marks, the format characters, the modifier letters and symbols, and the punctuation that is inside the words (Word_Break MidLetter, MidNumLet and Single_Quote).</summary>
    private static bool IsCaseIgnorable(int codePoint)
    {
        if (codePoint is '\'' or '.' or ':' or 0x00AD or 0x00B7 or 0x0387 or 0x055F or 0x05F4 or 0x2018 or 0x2019 or 0x2024 or 0x2027 or 0xFE13 or 0xFE52 or 0xFE55 or 0xFF07 or 0xFF0E or 0xFF1A)
            return true;

        return UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol;
    }

    /// <summary>Whether the character at an index is "preceded by a sequence consisting of a cased letter and then zero or more case-ignorable characters".</summary>
    private static bool IsAfterCasedLetter(ReadOnlySpan<char> text, int index)
    {
        while (index > 0)
        {
            var codePoint = GetCodePointBefore(text, index, out var length);
            if (!IsCaseIgnorable(codePoint))
                return IsCased(codePoint);

            index -= length;
        }

        return false;
    }

    /// <summary>Whether the position is "followed by a sequence consisting of zero or more case-ignorable characters and then a cased letter".</summary>
    private static bool IsBeforeCasedLetter(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            var codePoint = GetCodePoint(text, index, out var length);
            if (!IsCaseIgnorable(codePoint))
                return IsCased(codePoint);

            index += length;
        }

        return false;
    }

    [Flags]
    private enum GreekLetter : byte
    {
        None = 0,
        Vowel = 1,
        Accent = 2,
        Dialytika = 4,
        Ypogegrammeni = 8,
    }

    /// <summary>The diacritics of the Greek letters: the accents (which the uppercase letters lose), the dialytika and the ypogegrammeni.</summary>
    private static bool TryGetGreekDiacritic(int codePoint, out GreekLetter data)
    {
        data = codePoint switch
        {
            0x0300 or 0x0301 or 0x0342 or 0x0302 or 0x0303 or 0x0311 => GreekLetter.Accent,
            0x0308 => GreekLetter.Dialytika,
            0x0344 => GreekLetter.Dialytika | GreekLetter.Accent,
            0x0345 => GreekLetter.Ypogegrammeni,
            0x0304 or 0x0306 or 0x0313 or 0x0314 or 0x0343 => GreekLetter.None,
            _ => (GreekLetter)0xFF,
        };

        if (data == (GreekLetter)0xFF)
        {
            data = GreekLetter.None;
            return false;
        }

        return true;
    }

    /// <summary>The uppercase base letter (U+0391 to U+03A9) of a Greek letter and its diacritics (from its canonical decomposition), or 0.</summary>
    private static int GetGreekLetter(int codePoint, out GreekLetter data)
    {
        data = GreekLetter.None;
        if (codePoint is not ((>= 0x0370 and <= 0x03FF) or (>= 0x1F00 and <= 0x1FFF) or 0x2126))
            return 0;

        Span<int> decomposition = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength];
        var count = UnicodeNormalization.GetCanonicalDecomposition(codePoint, decomposition);
        var letter = UnicodeProperties.ToSimpleUppercase(decomposition[0]);
        if (letter is < 0x0391 or > 0x03A9)
            return 0;

        for (var i = 1; i < count; i++)
        {
            if (!TryGetGreekDiacritic(decomposition[i], out var diacritic))
                return 0;

            data |= diacritic;
        }

        if (letter is 0x0391 or 0x0395 or 0x0397 or 0x0399 or 0x039F or 0x03A5 or 0x03A9)
        {
            data |= GreekLetter.Vowel;
        }

        return letter;
    }

    /// <summary>
    /// The uppercase mapping of a Greek letter in Greek (CLDR, as ICU's GreekUpper): the accents are removed, except the tonos
    /// of the disjunctive eta; an iota or an upsilon after a vowel that loses its accent gets a dialytika; the ypogegrammeni is
    /// an iota. The combining diacritics after the letter are transformed with it.
    /// </summary>
    private static bool TryGreekUpper(ReadOnlySpan<char> text, int index, int codePoint, Span<char> destination, ref int consumed, out int length)
    {
        const int Eta = 0x0397;
        const int Iota = 0x0399;
        const int Upsilon = 0x03A5;
        length = 0;
        var upper = GetGreekLetter(codePoint, out var data);
        if (upper == 0)
            return false;

        if ((data & GreekLetter.Vowel) != GreekLetter.None && upper is Iota or Upsilon && IsAfterGreekVowelWithAccent(text, index))
        {
            data |= GreekLetter.Dialytika;
        }

        var ypogegrammeni = (data & GreekLetter.Ypogegrammeni) != GreekLetter.None ? 1 : 0;
        var next = index + consumed;
        var hasCombiningMarks = false;
        while (next < text.Length && TryGetGreekDiacritic(text[next], out var diacritic))
        {
            data |= diacritic;
            ypogegrammeni += (diacritic & GreekLetter.Ypogegrammeni) != GreekLetter.None ? 1 : 0;
            hasCombiningMarks = true;
            next++;
        }

        var addTonos = false;
        var dialytika = (data & GreekLetter.Dialytika) != GreekLetter.None;
        if (upper == Eta && (data & GreekLetter.Accent) != GreekLetter.None && ypogegrammeni == 0 && !IsAfterCasedLetter(text, index) && !IsBeforeCasedLetter(text, next))
        {
            // The disjunctive "or" keeps its tonos (the conditions of the word boundaries are the ones of the final sigma)
            if (hasCombiningMarks)
            {
                addTonos = true;
            }
            else
            {
                upper = 0x0389;
            }
        }
        else if (dialytika && upper is Iota or Upsilon)
        {
            // The precomposed forms of the vowels with a dialytika
            upper = upper == Iota ? 0x03AA : 0x03AB;
            dialytika = false;
        }

        destination[length++] = (char)upper;
        if (dialytika)
        {
            destination[length++] = (char)0x0308;
        }

        if (addTonos)
        {
            destination[length++] = (char)0x0301;
        }

        for (; ypogegrammeni > 0 && length < MaxLength; ypogegrammeni--)
        {
            destination[length++] = (char)Iota;
        }

        consumed = next - index;
        return true;
    }

    /// <summary>Whether the previous letter (with its diacritics) is a Greek vowel with an accent and without dialytika.</summary>
    private static bool IsAfterGreekVowelWithAccent(ReadOnlySpan<char> text, int index)
    {
        var data = GreekLetter.None;
        var i = index;
        while (i > 0 && TryGetGreekDiacritic(text[i - 1], out var diacritic))
        {
            data |= diacritic;
            i--;
        }

        if (i == 0 || GetGreekLetter(GetCodePointBefore(text, i, out _), out var letter) == 0)
            return false;

        data |= letter;
        return (data & (GreekLetter.Vowel | GreekLetter.Accent | GreekLetter.Dialytika)) == (GreekLetter.Vowel | GreekLetter.Accent);
    }

    /// <summary>
    /// Returns a text transformed by <c>text-transform</c> (the text of a control that is painted without inline layout): the
    /// same string when no character changes.
    /// </summary>
    public static string Transform(string text, TextTransform transform, CaseLanguage language)
    {
        if (transform == TextTransform.None || text.Length == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        Span<char> buffer = stackalloc char[MaxLength];
        var inWord = false;
        for (var index = 0; index < text.Length;)
        {
            var count = Transform(text, index, transform, language, wordStart: !inWord, buffer, out var consumed);
            builder.Append(buffer[..count]);
            inWord = IsWordCharacter(text, index);
            index += consumed;
        }

        return builder.Equals(text) ? text : builder.ToString();
    }

    /// <summary>
    /// Returns the text transformation of a text node: <c>math-auto</c> only applies to the text nodes that contain a single
    /// character (https://w3c.github.io/mathml-core/#the-math-auto-transform), ignoring the white space around it (it collapses).
    /// </summary>
    public static TextTransform ResolveMathAuto(TextTransform transform, ReadOnlySpan<char> text)
    {
        if ((transform & TextTransform.MathAuto) == TextTransform.None)
            return transform;

        text = text.Trim(" \t\n\r\f");
        var isSingleCharacter = text.Length == 1 || (text.Length == 2 && char.IsSurrogatePair(text[0], text[1]));
        return isSingleCharacter ? transform : transform & ~TextTransform.MathAuto;
    }

    /// <summary>
    /// Whether a character is part of a word for <c>capitalize</c> (https://drafts.csswg.org/css-text-4/#valdef-text-transform-capitalize):
    /// the letters and the digits, the apostrophes, the soft hyphen and the low line, which connects the parts of a word (UAX #29:
    /// ExtendNumLet), as in the browsers.
    /// </summary>
    public static bool IsWordCharacter(char c)
    {
        const char RightSingleQuotationMark = (char)0x2019;
        const char SoftHyphen = (char)0x00AD;
        return char.IsLetterOrDigit(c) || c is '\'' or '_' or RightSingleQuotationMark or SoftHyphen;
    }

    /// <summary>Whether a text ends with a character that is part of a word for <c>capitalize</c> (see <see cref="IsWordCharacter(ReadOnlySpan{char}, int)"/>).</summary>
    public static bool EndsWithWordCharacter(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return false;

        GetCodePointBefore(text, text.Length, out var length);
        return IsWordCharacter(text, text.Length - length);
    }

    /// <summary>
    /// Whether the character at an index of a text is part of a word for <c>capitalize</c>: the letters, the digits and their
    /// combining marks, the apostrophes, the soft hyphen and the connector punctuation, and the punctuation between two letters of a word (the middle dot
    /// of Catalan <c>l·l</c>, the hyphenation point).
    /// </summary>
    public static bool IsWordCharacter(ReadOnlySpan<char> text, int index)
    {
        var codePoint = GetCodePoint(text, index, out var length);
        if (codePoint <= char.MaxValue && IsWordCharacter((char)codePoint))
            return true;

        switch (UnicodeProperties.GetGeneralCategory(codePoint))
        {
            case <= UnicodeCategory.OtherLetter:
            case UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark:
            case UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber:
            case UnicodeCategory.ConnectorPunctuation:
                return true;
        }

        return codePoint is 0x00B7 or 0x0387 or 0x2027 && index > 0 && index + length < text.Length
            && IsLetter(GetCodePointBefore(text, index, out _)) && IsLetter(GetCodePoint(text, index + length, out _));
    }

    /// <summary>Returns whether the element is in Turkish or Azeri, whose case mappings of the dotted and dotless i differ (https://drafts.csswg.org/css-text/#text-transform-property).</summary>
    public static bool UsesTurkicCasing(Element? element) => GetCaseLanguage(element) == CaseLanguage.Turkic;

    /// <summary>Returns whether a content language (a BCP 47 language tag) is Turkish or Azeri.</summary>
    public static bool UsesTurkicCasing(string? language) => GetCaseLanguage(language) == CaseLanguage.Turkic;

    /// <summary>Returns the language of the case mappings of an element (its content language).</summary>
    public static CaseLanguage GetCaseLanguage(Element? element) => GetCaseLanguage(element is null ? null : HtmlElementStates.GetLanguage(element));

    /// <summary>Returns the language of the case mappings of a content language (a BCP 47 language tag).</summary>
    public static CaseLanguage GetCaseLanguage(string? language)
    {
        if (string.IsNullOrEmpty(language))
            return CaseLanguage.None;

        var primary = language.AsSpan();
        var script = ReadOnlySpan<char>.Empty;
        var dash = primary.IndexOf('-');
        if (dash >= 0)
        {
            // The script subtag (4 letters) follows the primary language subtag
            script = primary[(dash + 1)..];
            var end = script.IndexOf('-');
            script = end >= 0 ? script[..end] : script;
            primary = primary[..dash];
        }

        if (primary.Length != 2)
            return CaseLanguage.None;

        Span<char> lower = [char.ToLowerInvariant(primary[0]), char.ToLowerInvariant(primary[1])];
        var result = lower switch
        {
            "tr" or "az" => CaseLanguage.Turkic,
            "lt" => CaseLanguage.Lithuanian,
            "el" => CaseLanguage.Greek,
            "nl" => CaseLanguage.Dutch,
            _ => CaseLanguage.None,
        };

        // The rules of a language are the ones of its writing system: an explicit script subtag that is not the script of the
        // tailoring disables it (e.g. tr-Cyrl, https://drafts.csswg.org/css-text-4/#content-writing-system)
        if (result != CaseLanguage.None && script.Length == 4 && !script.Equals(result == CaseLanguage.Greek ? "Grek" : "Latn", StringComparison.OrdinalIgnoreCase))
            return CaseLanguage.None;

        return result;
    }

    // The wide forms of the halfwidth katakana (U+FF61 to U+FF9F)
    private static ReadOnlySpan<ushort> FullWidthKatakana =>
    [
        0x3002, 0x300C, 0x300D, 0x3001, 0x30FB, 0x30F2, 0x30A1, 0x30A3, 0x30A5, 0x30A7, 0x30A9, 0x30E3, 0x30E5, 0x30E7, 0x30C3, 0x30FC,
        0x30A2, 0x30A4, 0x30A6, 0x30A8, 0x30AA, 0x30AB, 0x30AD, 0x30AF, 0x30B1, 0x30B3, 0x30B5, 0x30B7, 0x30B9, 0x30BB, 0x30BD, 0x30BF,
        0x30C1, 0x30C4, 0x30C6, 0x30C8, 0x30CA, 0x30CB, 0x30CC, 0x30CD, 0x30CE, 0x30CF, 0x30D2, 0x30D5, 0x30D8, 0x30DB, 0x30DE, 0x30DF,
        0x30E0, 0x30E1, 0x30E2, 0x30E4, 0x30E6, 0x30E8, 0x30E9, 0x30EA, 0x30EB, 0x30EC, 0x30ED, 0x30EF, 0x30F3, 0x3099, 0x309A,
    ];

    /// <summary>
    /// https://drafts.csswg.org/css-text-4/#valdef-text-transform-full-width: "Puts all typographic character units in
    /// full-width form. If a character does not have a corresponding full-width form, it is left as is": the characters that
    /// are the &lt;wide&gt; or &lt;narrow&gt; compatibility decomposition of another one (the ASCII characters and the space, the
    /// halfwidth katakana, Hangul letters and symbols).
    /// </summary>
    private static char ToFullWidth(char c) => c switch
    {
        ' ' => (char)0x3000,
        >= '!' and <= '~' => (char)(c + 0xFEE0),
        (char)0x00A2 => (char)0xFFE0,
        (char)0x00A3 => (char)0xFFE1,
        (char)0x00A5 => (char)0xFFE5,
        (char)0x00A6 => (char)0xFFE4,
        (char)0x00AC => (char)0xFFE2,
        (char)0x00AF => (char)0xFFE3,
        (char)0x20A9 => (char)0xFFE6,
        (char)0x2985 => (char)0xFF5F,
        (char)0x2986 => (char)0xFF60,
        >= (char)0xFF61 and <= (char)0xFF9F => (char)FullWidthKatakana[c - 0xFF61],
        (char)0xFFA0 => (char)0x3164,
        >= (char)0xFFA1 and <= (char)0xFFBE => (char)(c - 0xFFA1 + 0x3131),
        >= (char)0xFFC2 and <= (char)0xFFC7 => (char)(c - 0xFFC2 + 0x314F),
        >= (char)0xFFCA and <= (char)0xFFCF => (char)(c - 0xFFCA + 0x3155),
        >= (char)0xFFD2 and <= (char)0xFFD7 => (char)(c - 0xFFD2 + 0x315B),
        >= (char)0xFFDA and <= (char)0xFFDC => (char)(c - 0xFFDA + 0x3161),
        (char)0xFFE8 => (char)0x2502,
        >= (char)0xFFE9 and <= (char)0xFFEC => (char)(c - 0xFFE9 + 0x2190),
        (char)0xFFED => (char)0x25A0,
        (char)0xFFEE => (char)0x25CB,
        _ => c,
    };

    /// <summary>The small kana mappings of CSS Text (https://drafts.csswg.org/css-text-4/#small-kana) of the characters outside of the BMP, or 0.</summary>
    private static int ToFullSizeKana(int codePoint) => codePoint switch
    {
        0x1B132 => 0x3053,
        0x1B150 => 0x3090,
        0x1B151 => 0x3091,
        0x1B152 => 0x3092,
        0x1B155 => 0x30B3,
        0x1B164 => 0x30F0,
        0x1B165 => 0x30F1,
        0x1B166 => 0x30F2,
        0x1B167 => 0x30F3,
        _ => 0,
    };

    /// <summary>The small kana mappings of CSS Text (https://drafts.csswg.org/css-text-4/#small-kana), for the characters of the BMP.</summary>
    private static char ToFullSizeKana(char c) => c switch
    {
        (char)0x3041 => (char)0x3042,
        (char)0x3043 => (char)0x3044,
        (char)0x3045 => (char)0x3046,
        (char)0x3047 => (char)0x3048,
        (char)0x3049 => (char)0x304A,
        (char)0x3063 => (char)0x3064,
        (char)0x3083 => (char)0x3084,
        (char)0x3085 => (char)0x3086,
        (char)0x3087 => (char)0x3088,
        (char)0x308E => (char)0x308F,
        (char)0x3095 => (char)0x304B,
        (char)0x3096 => (char)0x3051,
        (char)0x30A1 => (char)0x30A2,
        (char)0x30A3 => (char)0x30A4,
        (char)0x30A5 => (char)0x30A6,
        (char)0x30A7 => (char)0x30A8,
        (char)0x30A9 => (char)0x30AA,
        (char)0x30C3 => (char)0x30C4,
        (char)0x30E3 => (char)0x30E4,
        (char)0x30E5 => (char)0x30E6,
        (char)0x30E7 => (char)0x30E8,
        (char)0x30EE => (char)0x30EF,
        (char)0x30F5 => (char)0x30AB,
        (char)0x30F6 => (char)0x30B1,
        (char)0x31F0 => (char)0x30AF,
        (char)0x31F1 => (char)0x30B7,
        (char)0x31F2 => (char)0x30B9,
        (char)0x31F3 => (char)0x30C8,
        (char)0x31F4 => (char)0x30CC,
        (char)0x31F5 => (char)0x30CF,
        (char)0x31F6 => (char)0x30D2,
        (char)0x31F7 => (char)0x30D5,
        (char)0x31F8 => (char)0x30D8,
        (char)0x31F9 => (char)0x30DB,
        (char)0x31FA => (char)0x30E0,
        (char)0x31FB => (char)0x30E9,
        (char)0x31FC => (char)0x30EA,
        (char)0x31FD => (char)0x30EB,
        (char)0x31FE => (char)0x30EC,
        (char)0x31FF => (char)0x30ED,
        (char)0xFF67 => (char)0xFF71,
        (char)0xFF68 => (char)0xFF72,
        (char)0xFF69 => (char)0xFF73,
        (char)0xFF6A => (char)0xFF74,
        (char)0xFF6B => (char)0xFF75,
        (char)0xFF6C => (char)0xFF94,
        (char)0xFF6D => (char)0xFF95,
        (char)0xFF6E => (char)0xFF96,
        (char)0xFF6F => (char)0xFF82,
        _ => c,
    };
}
