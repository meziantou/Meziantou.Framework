using System.Text;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The full case mappings of Unicode (https://www.unicode.org/versions/latest/core-spec/chapter-3/#G33992, default case
/// conversion) without the mappings that depend on the language or on the context: the unconditional mappings of
/// SpecialCasing.txt (generated, <see cref="SpecialCasingData"/>), else the simple mappings of the invariant culture.
/// </summary>
internal static class CaseMapping
{
    /// <summary>The maximum number of UTF-16 code units of the mapping of a code point.</summary>
    public const int MaxLength = 3;

    /// <summary>Writes the full uppercase mapping of a code point (e.g. <c>ß</c> → <c>SS</c>, <c>ﬃ</c> → <c>FFI</c>) in UTF-16; returns its length.</summary>
    public static int ToUpper(int codePoint, Span<char> destination)
    {
        var index = SpecialCasingData.UppercaseCodePoints.BinarySearch(codePoint);
        if (index >= 0)
            return CopyMapping(SpecialCasingData.UppercaseStarts, SpecialCasingData.UppercaseChars, index, destination);

        // The simple mappings that the invariant culture does not have (their lowercase is another letter: i, s)
        if (codePoint is 0x0131 or 0x017F)
        {
            destination[0] = codePoint == 0x0131 ? 'I' : 'S';
            return 1;
        }

        return Rune.IsValid(codePoint) ? Rune.ToUpperInvariant(new Rune(codePoint)).EncodeToUtf16(destination) : WriteCodePoint(codePoint, destination);
    }

    /// <summary>
    /// Writes the titlecase mapping of a code point (the first letter of a word, e.g. <c>ﬁ</c> → <c>Fi</c>) in UTF-16; returns
    /// its length: the titlecase mappings of SpecialCasing.txt that differ from the uppercase mapping, else the uppercase mapping.
    /// </summary>
    public static int ToTitle(int codePoint, Span<char> destination)
    {
        var index = SpecialCasingData.TitlecaseCodePoints.BinarySearch(codePoint);
        if (index >= 0)
            return CopyMapping(SpecialCasingData.TitlecaseStarts, SpecialCasingData.TitlecaseChars, index, destination);

        // The simple titlecase mappings of UnicodeData.txt that differ from the uppercase mappings: the Latin digraphs
        // (U+01C4 to U+01CC, U+01F1 to U+01F3) have a titlecase form, and the Georgian letters (Mkhedruli) are their own
        // titlecase (their uppercase is Mtavruli)
        switch (codePoint)
        {
            case >= 0x01C4 and <= 0x01CC:
                destination[0] = (char)(0x01C5 + ((codePoint - 0x01C4) / 3 * 3));
                return 1;

            case >= 0x01F1 and <= 0x01F3:
                destination[0] = (char)0x01F2;
                return 1;

            case (>= 0x10D0 and <= 0x10FA) or (>= 0x10FD and <= 0x10FF):
                destination[0] = (char)codePoint;
                return 1;
        }

        return ToUpper(codePoint, destination);
    }

    /// <summary>Writes the full lowercase mapping of a code point (e.g. <c>İ</c> → <c>i̇</c>) in UTF-16; returns its length.</summary>
    public static int ToLower(int codePoint, Span<char> destination)
    {
        var index = SpecialCasingData.LowercaseCodePoints.BinarySearch(codePoint);
        if (index >= 0)
            return CopyMapping(SpecialCasingData.LowercaseStarts, SpecialCasingData.LowercaseChars, index, destination);

        return Rune.IsValid(codePoint) ? Rune.ToLowerInvariant(new Rune(codePoint)).EncodeToUtf16(destination) : WriteCodePoint(codePoint, destination);
    }

    /// <summary>Whether the full uppercase mapping of a code point is not the code point itself (Changes_When_Uppercased, without the context).</summary>
    public static bool ChangesWhenUppercased(int codePoint)
    {
        if (codePoint < 0x80)
            return codePoint is >= 'a' and <= 'z';

        if (SpecialCasingData.UppercaseCodePoints.BinarySearch(codePoint) >= 0 || codePoint is 0x0131 or 0x017F)
            return true;

        return Rune.IsValid(codePoint) && Rune.ToUpperInvariant(new Rune(codePoint)).Value != codePoint;
    }

    /// <summary>Whether the full lowercase mapping of a code point is not the code point itself (Changes_When_Lowercased, without the context).</summary>
    public static bool ChangesWhenLowercased(int codePoint)
    {
        if (codePoint < 0x80)
            return codePoint is >= 'A' and <= 'Z';

        if (SpecialCasingData.LowercaseCodePoints.BinarySearch(codePoint) >= 0)
            return true;

        return Rune.IsValid(codePoint) && Rune.ToLowerInvariant(new Rune(codePoint)).Value != codePoint;
    }

    private static int CopyMapping(ReadOnlySpan<ushort> starts, ReadOnlySpan<ushort> chars, int index, Span<char> destination)
    {
        var start = starts[index];
        var length = starts[index + 1] - start;
        for (var i = 0; i < length; i++)
        {
            destination[i] = (char)chars[start + i];
        }

        return length;
    }

    private static int WriteCodePoint(int codePoint, Span<char> destination)
    {
        destination[0] = (char)codePoint;
        return 1;
    }
}
