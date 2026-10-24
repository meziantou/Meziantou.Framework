using System.Globalization;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The operations on strings used by the built-ins: code points, case mappings, normalization, trimming.</summary>
internal static class StringOperations
{
    /// <summary>Throws the RangeError of a string longer than <see cref="JsString.MaxLength"/>.</summary>
    public static void ThrowInvalidLength() => ThrowHelper.ThrowRangeError("Invalid string length");

    /// <summary>
    /// Appends a part of a string built by a built-in function: throws a RangeError when the string becomes too long, and
    /// counts the work, so that the limits of the running script are checked while it grows (<see cref="JsEngine.CountWork"/>).
    /// </summary>
    public static void Append(StringBuilder builder, ReadOnlySpan<char> value)
    {
        CheckLength((long)builder.Length + value.Length);
        builder.Append(value);
        JsEngine.CountCurrentWork(value.Length + 1);
    }

    /// <summary>Appends <paramref name="value"/> <paramref name="repeatCount"/> times to a string built by a built-in function (see <see cref="Append(StringBuilder, ReadOnlySpan{char})"/>).</summary>
    public static void Append(StringBuilder builder, ReadOnlySpan<char> value, long repeatCount)
    {
        CheckLength(builder.Length + (repeatCount * value.Length));
        if (value.Length == 1)
        {
            // A single allocation: the string builder can grow to the maximum length of a string
            JsEngine.EnsureCurrentCanAllocate(repeatCount * sizeof(char));
            JsEngine.CountCurrentWork(repeatCount);
            builder.Append(value[0], (int)repeatCount);
            return;
        }

        for (long i = 0; i < repeatCount; i++)
        {
            Append(builder, value);
        }
    }

    /// <summary>Throws a RangeError when a string of this length cannot be created.</summary>
    public static void CheckLength(long length)
    {
        if (length > JsString.MaxLength)
        {
            ThrowInvalidLength();
        }
    }

    /// <summary>Concatenates two strings, or throws a RangeError when the result is too long.</summary>
    public static JsString Concat(JsString left, JsString right)
    {
        if (!JsString.CanConcat(left, right))
        {
            ThrowInvalidLength();
        }

        return JsString.Concat(left, right);
    }

    /// <summary>CodePointAt(string, position): the code point at the position and its length in code units (a lone surrogate is a code point).</summary>
    public static int CodePointAt(string value, int position, out int codeUnitCount)
    {
        var first = value[position];
        if (char.IsHighSurrogate(first) && position + 1 < value.Length)
        {
            var second = value[position + 1];
            if (char.IsLowSurrogate(second))
            {
                codeUnitCount = 2;
                return char.ConvertToUtf32(first, second);
            }
        }

        codeUnitCount = 1;
        return first;
    }

    /// <summary>The code point that ends just before the position (a lone surrogate is a code point).</summary>
    private static int CodePointBefore(string value, int position, out int codeUnitCount)
    {
        var last = value[position - 1];
        if (char.IsLowSurrogate(last) && position >= 2)
        {
            var first = value[position - 2];
            if (char.IsHighSurrogate(first))
            {
                codeUnitCount = 2;
                return char.ConvertToUtf32(first, last);
            }
        }

        codeUnitCount = 1;
        return last;
    }

    /// <summary>IsStringWellFormedUnicode(string): the string has no lone surrogate.</summary>
    public static bool IsWellFormed(string value) => IndexOfLoneSurrogate(value, 0) < 0;

    /// <summary>The index of the first lone surrogate at or after the position, or -1.</summary>
    public static int IndexOfLoneSurrogate(string value, int start)
    {
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsSurrogate(c))
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>String.prototype.toWellFormed: the lone surrogates are replaced by U+FFFD.</summary>
    public static string ToWellFormed(string value)
    {
        var index = IndexOfLoneSurrogate(value, 0);
        if (index < 0)
            return value;

        var chars = value.ToCharArray();
        while (index >= 0)
        {
            chars[index] = '\uFFFD';
            index = IndexOfLoneSurrogate(value, index + 1);
        }

        return new string(chars);
    }

    /// <summary>TrimString(string, where) with the WhiteSpace and LineTerminator code points.</summary>
    public static string Trim(string value, bool start, bool end)
    {
        var first = 0;
        if (start)
        {
            while (first < value.Length && NumberParsing.IsWhiteSpaceOrLineTerminator(value[first]))
            {
                first++;
            }
        }

        var last = value.Length;
        if (end)
        {
            while (last > first && NumberParsing.IsWhiteSpaceOrLineTerminator(value[last - 1]))
            {
                last--;
            }
        }

        return first == 0 && last == value.Length ? value : value[first..last];
    }

    /// <summary>Normalizes a string (NFC, NFD, NFKC or NFKD) with the normalization of Meziantou.Framework.Unicode.</summary>
    public static string Normalize(string value, NormalizationForm form)
    {
        // The ASCII characters are in every normalization form
        var asciiPrefixLength = value.AsSpan().IndexOfAnyExceptInRange((char)0, (char)0x7F);
        if (asciiPrefixLength < 0)
            return value;

        // The compatibility decompositions can make the string 18 times longer, and the normalization works on a buffer
        // of code points (after the ASCII prefix): the memory needed by a long string is checked before it is normalized
        const int MaxExpansion = 18;
        long maxLength = value.Length;
        if ((long)value.Length * MaxExpansion * (sizeof(int) + sizeof(char)) >= JsEngine.LargeAllocationBytes)
        {
            maxLength = GetDecomposedLength(value, form);
            JsEngine.EnsureCurrentCanAllocate((maxLength * sizeof(char)) + ((maxLength - asciiPrefixLength) * sizeof(int)));
        }

        if (UnicodeNormalization.IsNormalized(value, form))
            return value;

        if (maxLength > JsString.MaxLength)
        {
            CheckLength(UnicodeNormalization.GetNormalizedLength(value, form));
        }

        return UnicodeNormalization.Normalize(value, form);
    }

    /// <summary>
    /// The length of the string in the decomposition form of <paramref name="form"/> (NFD or NFKD): the decomposition is
    /// done code point by code point, and the composition (NFC, NFKC) only makes it shorter, so it bounds the length of
    /// the normalized string.
    /// </summary>
    private static long GetDecomposedLength(string value, NormalizationForm form)
    {
        var decomposition = form is NormalizationForm.FormC or NormalizationForm.FormD ? NormalizationForm.FormD : NormalizationForm.FormKD;
        Dictionary<int, int>? lengths = null;
        long length = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsAscii(c))
            {
                length++;
                continue;
            }

            int codePoint = c;
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codePoint = char.ConvertToUtf32(c, value[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                // A lone surrogate is not changed
                length++;
                continue;
            }

            lengths ??= [];
            if (!lengths.TryGetValue(codePoint, out var codePointLength))
            {
                codePointLength = UnicodeNormalization.Normalize(char.ConvertFromUtf32(codePoint), decomposition).Length;
                lengths.Add(codePoint, codePointLength);
            }

            length += codePointLength;
        }

        return length;
    }

    /// <summary>Converts a string to lower case with the full, locale-insensitive mappings of the Unicode Character Database (including Final_Sigma).</summary>
    public static string ToLowerCase(string value)
    {
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c >= 0x80 || c is >= 'A' and <= 'Z')
                break;

            i++;
        }

        if (i == value.Length)
            return value;

        var builder = new StringBuilder(value.Length);
        builder.Append(value, 0, i);
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        while (i < value.Length)
        {
            var c = value[i];
            if (c < 0x80)
            {
                builder.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
                i++;
                continue;
            }

            var codePoint = CodePointAt(value, i, out var codeUnitCount);
            if (codePoint == 0x03A3)
            {
                // GREEK CAPITAL LETTER SIGMA: the only language-insensitive conditional mapping of SpecialCasing.txt
                builder.Append(IsFinalSigma(value, i) ? '\u03C2' : '\u03C3');
            }
            else if (char.IsSurrogate(c) && codeUnitCount == 1)
            {
                builder.Append(c);
            }
            else
            {
                var count = UnicodeProperties.GetLowercaseMapping(codePoint, mapping);
                AppendCodePoints(builder, mapping[..count]);
            }

            i += codeUnitCount;
        }

        CheckLength(builder.Length);
        return builder.ToString();
    }

    /// <summary>Converts a string to upper case with the full, locale-insensitive mappings of the Unicode Character Database.</summary>
    public static string ToUpperCase(string value)
    {
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c >= 0x80 || c is >= 'a' and <= 'z')
                break;

            i++;
        }

        if (i == value.Length)
            return value;

        var builder = new StringBuilder(value.Length);
        builder.Append(value, 0, i);
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        while (i < value.Length)
        {
            var c = value[i];
            if (c < 0x80)
            {
                builder.Append(c is >= 'a' and <= 'z' ? (char)(c - 32) : c);
                i++;
                continue;
            }

            var codePoint = CodePointAt(value, i, out var codeUnitCount);
            if (char.IsSurrogate(c) && codeUnitCount == 1)
            {
                builder.Append(c);
            }
            else
            {
                var count = UnicodeProperties.GetUppercaseMapping(codePoint, mapping);
                AppendCodePoints(builder, mapping[..count]);
                if (count > 1 && builder.Length > JsString.MaxLength)
                {
                    ThrowInvalidLength();
                }
            }

            i += codeUnitCount;
        }

        CheckLength(builder.Length);
        return builder.ToString();
    }

    private static void AppendCodePoints(StringBuilder builder, ReadOnlySpan<int> codePoints)
    {
        foreach (var codePoint in codePoints)
        {
            AppendCodePoint(builder, codePoint);
        }
    }

    /// <summary>Appends UTF16EncodeCodePoint(codePoint).</summary>
    public static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if (codePoint < 0x10000)
        {
            builder.Append((char)codePoint);
        }
        else
        {
            codePoint -= 0x10000;
            builder.Append((char)(0xD800 + (codePoint >> 10)));
            builder.Append((char)(0xDC00 + (codePoint & 0x3FF)));
        }
    }

    /// <summary>
    /// The Final_Sigma condition (Unicode 3.13): the sigma is preceded by a cased letter and zero or more case-ignorable
    /// characters, and it is not followed by zero or more case-ignorable characters and a cased letter. As in ICU, a
    /// character that is both cased and case-ignorable (e.g. U+0345) is case-ignorable.
    /// </summary>
    private static bool IsFinalSigma(string value, int index)
    {
        var position = index;
        var precededByCased = false;
        while (position > 0)
        {
            var codePoint = CodePointBefore(value, position, out var length);
            position -= length;
            if (IsCaseIgnorable(codePoint))
                continue;

            precededByCased = IsCased(codePoint);
            break;
        }

        if (!precededByCased)
            return false;

        position = index + 1;
        while (position < value.Length)
        {
            var codePoint = CodePointAt(value, position, out var length);
            position += length;
            if (IsCaseIgnorable(codePoint))
                continue;

            return !IsCased(codePoint);
        }

        return true;
    }

    /// <summary>Case_Ignorable (DerivedCoreProperties.txt): Word_Break is MidLetter, MidNumLet or Single_Quote, or the general category is Mn, Me, Cf, Lm or Sk.</summary>
    private static bool IsCaseIgnorable(int codePoint)
    {
        if (codePoint is 0x27 or 0x2E or 0x3A)
            return true;

        if (codePoint < 0x80)
            return codePoint == 0x5E || codePoint == 0x60;

        var category = UnicodeProperties.GetGeneralCategory(codePoint);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol)
            return true;

        return UnicodeProperties.GetWordBreak(codePoint) is UnicodeWordBreak.MidLetter or UnicodeWordBreak.MidNumLet or UnicodeWordBreak.SingleQuote;
    }

    /// <summary>
    /// Cased (DerivedCoreProperties.txt): Lowercase, Uppercase or Lt. Other_Lowercase and Other_Uppercase are mostly
    /// modifier letters and marks, which are case-ignorable (and so never tested as cased); the other ones are listed here.
    /// </summary>
    private static bool IsCased(int codePoint)
    {
        var category = UnicodeProperties.GetGeneralCategory(codePoint);
        if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter)
            return true;

        return codePoint is 0x00AA or 0x00BA
            or (>= 0x2160 and <= 0x217F)
            or (>= 0x24B6 and <= 0x24E9)
            or (>= 0x1F130 and <= 0x1F149)
            or (>= 0x1F150 and <= 0x1F169)
            or (>= 0x1F170 and <= 0x1F189);
    }
}
