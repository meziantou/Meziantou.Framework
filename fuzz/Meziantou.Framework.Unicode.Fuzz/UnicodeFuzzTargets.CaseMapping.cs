using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    private const int CombiningDotAbove = 0x0307;

    // The language tags the option byte selects, and the language whose mappings they select: "tr", "lt" or null (no tailoring)
    private static readonly (string? Language, string? MappingLanguage)[] CaseMappingLanguages =
    [
        (null, null), ("tr", "tr"), ("az", "tr"), ("lt", "lt"), ("en", null), ("TR-tr", "tr"), ("Az_Latn", "tr"), ("tur", "tr"), ("aze", "tr"),
        ("LIT", "lt"), ("lt-LT", "lt"), ("tr-", "tr"), ("el", null), ("nl", null), ("", null), ("-tr", null), ("trx", null), ("x-lt", null), ("und", null),
    ];

    /// <summary>
    /// The input is a byte that selects the language tag, then the text. The conversions never throw; a tag selects the
    /// mappings of its language subtag only, and a conversion that changes nothing returns its argument. Uppercasing is
    /// the full uppercase mapping of each code point, except the tailorings of SpecialCasing.txt: the Turkic <c>i</c>, and
    /// the Lithuanian combining dot above after a soft-dotted letter, which is removed. Lowercasing without a tailoring
    /// is the full lowercase mapping of each code point, a capital sigma becoming the small or the final sigma. The
    /// conversions are idempotent, except uppercasing in Lithuanian: a dot above that was not after a soft-dotted letter
    /// can follow one once an other dot above is removed (<c>U+2071 U+0307 U+0307</c>), and is removed by a second
    /// conversion. An ASCII text gives the result of the invariant culture, with the dotted and the dotless I in Turkic.
    /// </summary>
    private static void CaseMapping(ReadOnlySpan<byte> data)
    {
        var (language, mappingLanguage) = FuzzInput.Pick(CaseMappingLanguages, FuzzInput.ReadByte(ref data));
        var text = FuzzText.Decode(data);

        var upper = UnicodeCaseMapping.ToUpper(text, language);
        var lower = UnicodeCaseMapping.ToLower(text, language);
        FuzzAssert.Equal(UnicodeCaseMapping.ToUpper(text, mappingLanguage), upper, $"ToUpper with the language {FuzzAssert.Format(language)} does not use the mappings of {FuzzAssert.Format(mappingLanguage)}");
        FuzzAssert.Equal(UnicodeCaseMapping.ToLower(text, mappingLanguage), lower, $"ToLower with the language {FuzzAssert.Format(language)} does not use the mappings of {FuzzAssert.Format(mappingLanguage)}");
        FuzzAssert.True(!string.Equals(upper, text, StringComparison.Ordinal) || ReferenceEquals(upper, text), "ToUpper does not return its argument when no character changes");
        FuzzAssert.True(!string.Equals(lower, text, StringComparison.Ordinal) || ReferenceEquals(lower, text), "ToLower does not return its argument when no character changes");

        FuzzAssert.Equal(ReferenceToUpper(text, mappingLanguage), upper, $"ToUpper({mappingLanguage}) is not the uppercase mapping of each code point");
        if (mappingLanguage is null)
        {
            CheckLowercaseMapping(text, lower);
        }

        var upperAgain = UnicodeCaseMapping.ToUpper(upper, mappingLanguage);
        if (mappingLanguage is not "lt" || !HasDotAboveAfterSoftDotted(upper))
        {
            FuzzAssert.Equal(upper, upperAgain, $"ToUpper({mappingLanguage}) is not idempotent");
        }

        FuzzAssert.Equal(lower, UnicodeCaseMapping.ToLower(lower, mappingLanguage), $"ToLower({mappingLanguage}) is not idempotent");

        if (Ascii.IsValid(text))
        {
            var (expectedUpper, expectedLower) = mappingLanguage is "tr"
                ? (text.Replace('i', 'İ').ToUpperInvariant(), text.Replace('I', 'ı').ToLowerInvariant())
                : (text.ToUpperInvariant(), text.ToLowerInvariant());
            FuzzAssert.Equal(expectedUpper, upper, $"ToUpper({mappingLanguage}) of an ASCII text differs from ToUpperInvariant");
            FuzzAssert.Equal(expectedLower, lower, $"ToLower({mappingLanguage}) of an ASCII text differs from ToLowerInvariant");
        }
    }

    /// <summary>The full uppercase mapping of each code point; <c>i</c> becomes U+0130 in Turkic, and a dot above after a soft-dotted letter is removed in Lithuanian.</summary>
    private static string ReferenceToUpper(string text, string? mappingLanguage)
    {
        var result = new StringBuilder();
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        for (var index = 0; index < text.Length;)
        {
            var codePoint = CodePointAt(text, index, out var length);
            if (codePoint == 'i' && mappingLanguage is "tr")
            {
                result.Append('İ');
            }
            else if (codePoint != CombiningDotAbove || mappingLanguage is not "lt" || !IsAfterSoftDotted(text, index))
            {
                foreach (var mapped in mapping[..UnicodeProperties.GetUppercaseMapping(codePoint, mapping)])
                {
                    AppendCodePoint(result, mapped);
                }
            }

            index += length;
        }

        return result.ToString();
    }

    /// <summary>Compares the result with the full lowercase mapping of each code point, where a capital sigma can become either sigma.</summary>
    private static void CheckLowercaseMapping(string text, string lower)
    {
        var expected = new StringBuilder();
        var sigmas = new HashSet<int>();
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        for (var index = 0; index < text.Length;)
        {
            var codePoint = CodePointAt(text, index, out var length);
            if (codePoint == 0x03A3)
            {
                sigmas.Add(expected.Length);
                expected.Append('σ');
            }
            else
            {
                foreach (var mapped in mapping[..UnicodeProperties.GetLowercaseMapping(codePoint, mapping)])
                {
                    AppendCodePoint(expected, mapped);
                }
            }

            index += length;
        }

        FuzzAssert.Equal(expected.Length, lower.Length, "ToLower is not the lowercase mapping of each code point");
        for (var index = 0; index < lower.Length; index++)
        {
            var isExpected = lower[index] == expected[index] || (sigmas.Contains(index) && lower[index] == 'ς');
            FuzzAssert.True(isExpected, $"ToLower is not the lowercase mapping of each code point at {index}: {FuzzAssert.Format(lower)} instead of {FuzzAssert.Format(expected.ToString())}");
        }
    }

    /// <summary>After_Soft_Dotted (SpecialCasing.txt): a soft-dotted letter comes before, with no character of combining class 0 or 230 between them.</summary>
    private static bool IsAfterSoftDotted(string text, int index)
    {
        while (index > 0)
        {
            var start = index - 1;
            if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
            {
                start--;
            }

            var codePoint = CodePointAt(text, start, out _);
            if (UnicodeProperties.IsSoftDotted(codePoint))
                return true;

            if (UnicodeProperties.GetCanonicalCombiningClass(codePoint) is 0 or 230)
                return false;

            index = start;
        }

        return false;
    }

    private static bool HasDotAboveAfterSoftDotted(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == CombiningDotAbove && IsAfterSoftDotted(text, index))
                return true;
        }

        return false;
    }

    private static IEnumerable<FuzzSeed> GetCaseMappingSeeds()
    {
        (string Name, string Text)[] texts =
        [
            ("turkic", "İstanbul Iyi ıi İ İ̖ i̇ DIYARBAKIR"),
            ("sigma", "ΣΑΣ ΟΔΥΣΣΕΥΣ σ.Σ aΣ' Σʰ ΑΣ́b"),
            ("lithuanian", "ÌÍĨ Í J̀ Į̃ i̇́ j̇ ⁱ̇̇ i̖̇"),
            ("special-casing", "ß ŉ ǰ ﬃ ΐ ᾳ ͅ ẞ և ᾀ ǅ"),
            ("supplementary", "\U00010400\U00010428 \U0001E900\U0001E922 \U0001D400"),
            ("ascii-letters", "The QUICK brown fox, 123 Iii."),
        ];

        foreach (var (name, text) in texts)
        {
            for (var language = 0; language < 4; language++)
            {
                yield return new FuzzSeed($"{name}-{language}", (byte[])[(byte)language, .. FuzzText.Encode(text)]);
            }
        }

        foreach (var (name, text) in HandWrittenTexts)
        {
            yield return new FuzzSeed($"text-{name}", (byte[])[0, .. FuzzText.Encode(text)]);
        }
    }
}
