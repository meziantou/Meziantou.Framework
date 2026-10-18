namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The full case mappings of Unicode (https://www.unicode.org/versions/latest/core-spec/chapter-3/#G33992, default case
/// conversion) without the mappings that depend on the language or on the context, in UTF-16: the unconditional mappings of
/// SpecialCasing.txt, else the simple mappings (<see cref="UnicodeProperties.GetUppercaseMapping(int, Span{int})"/>).
/// </summary>
internal static class CaseMapping
{
    /// <summary>The maximum number of UTF-16 code units of the mapping of a code point.</summary>
    public const int MaxLength = 3;

    /// <summary>Writes the full uppercase mapping of a code point (e.g. <c>ß</c> → <c>SS</c>, <c>ﬃ</c> → <c>FFI</c>) in UTF-16; returns its length.</summary>
    public static int ToUpper(int codePoint, Span<char> destination)
    {
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        return WriteUtf16(mapping[..UnicodeProperties.GetUppercaseMapping(codePoint, mapping)], destination);
    }

    /// <summary>
    /// Writes the titlecase mapping of a code point (the first letter of a word, e.g. <c>ﬁ</c> → <c>Fi</c>, <c>ǆ</c> → <c>ǅ</c>)
    /// in UTF-16; returns its length.
    /// </summary>
    public static int ToTitle(int codePoint, Span<char> destination)
    {
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        return WriteUtf16(mapping[..UnicodeProperties.GetTitlecaseMapping(codePoint, mapping)], destination);
    }

    /// <summary>Writes the full lowercase mapping of a code point (e.g. <c>İ</c> → <c>i</c> U+0307) in UTF-16; returns its length.</summary>
    public static int ToLower(int codePoint, Span<char> destination)
    {
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        return WriteUtf16(mapping[..UnicodeProperties.GetLowercaseMapping(codePoint, mapping)], destination);
    }

    /// <summary>Whether the full uppercase mapping of a code point is not the code point itself (Changes_When_Uppercased, without the context).</summary>
    public static bool ChangesWhenUppercased(int codePoint)
    {
        if (codePoint < 0x80)
            return codePoint is >= 'a' and <= 'z';

        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        return UnicodeProperties.GetUppercaseMapping(codePoint, mapping) != 1 || mapping[0] != codePoint;
    }

    /// <summary>Whether the full lowercase mapping of a code point is not the code point itself (Changes_When_Lowercased, without the context).</summary>
    public static bool ChangesWhenLowercased(int codePoint)
    {
        if (codePoint < 0x80)
            return codePoint is >= 'A' and <= 'Z';

        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        return UnicodeProperties.GetLowercaseMapping(codePoint, mapping) != 1 || mapping[0] != codePoint;
    }

    // A code point that is not a scalar value (a lone surrogate) is its own mapping: it is written as is
    private static int WriteUtf16(ReadOnlySpan<int> codePoints, Span<char> destination)
    {
        var length = 0;
        foreach (var codePoint in codePoints)
        {
            if (codePoint < 0x10000)
            {
                destination[length++] = (char)codePoint;
            }
            else
            {
                destination[length++] = (char)(0xD7C0 + (codePoint >> 10));
                destination[length++] = (char)(0xDC00 | (codePoint & 0x3FF));
            }
        }

        return length;
    }
}
