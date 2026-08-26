// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCharClass.cs
//         src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCharClass.Tables.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCharClass.cs
//
// Changes: only the names are kept. The character sets they stand for are a matching concern and are not here.

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The category and block names the .NET engine accepts inside <c>\p{…}</c>.</summary>
/// <remarks>
/// A name the engine does not know is a parse error, so the set has to be known to report the same thing. Only the
/// names are needed: this library never computes the characters a category stands for.
/// </remarks>
internal static class NetUnicodeCategoryNames
{
    /// <summary>Every accepted name, in ordinal order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "C", "Cc", "Cf", "Cn", "Co", "Cs", "IsAlphabeticPresentationForms", "IsArabic", "IsArabicExtended-A",
        "IsArabicExtended-B", "IsArabicPresentationForms-A", "IsArabicPresentationForms-B", "IsArabicSupplement", "IsArmenian",
        "IsArrows", "IsBalinese", "IsBamum", "IsBasicLatin", "IsBatak", "IsBengali", "IsBlockElements", "IsBopomofo",
        "IsBopomofoExtended", "IsBoxDrawing", "IsBraillePatterns", "IsBuginese", "IsBuhid", "IsCJKCompatibility",
        "IsCJKCompatibilityForms", "IsCJKCompatibilityIdeographs", "IsCJKRadicalsSupplement", "IsCJKStrokes",
        "IsCJKSymbolsandPunctuation", "IsCJKUnifiedIdeographs", "IsCJKUnifiedIdeographsExtensionA", "IsCham", "IsCherokee",
        "IsCherokeeSupplement", "IsCombiningDiacriticalMarks", "IsCombiningDiacriticalMarksExtended",
        "IsCombiningDiacriticalMarksSupplement", "IsCombiningDiacriticalMarksforSymbols", "IsCombiningHalfMarks",
        "IsCombiningMarksforSymbols", "IsCommonIndicNumberForms", "IsControlPictures", "IsCoptic", "IsCurrencySymbols",
        "IsCyrillic", "IsCyrillicExtended-A", "IsCyrillicExtended-B", "IsCyrillicExtended-C", "IsCyrillicSupplement",
        "IsDevanagari", "IsDevanagariExtended", "IsDingbats", "IsEnclosedAlphanumerics", "IsEnclosedCJKLettersandMonths",
        "IsEthiopic", "IsEthiopicExtended", "IsEthiopicExtended-A", "IsEthiopicSupplement", "IsGeneralPunctuation",
        "IsGeometricShapes", "IsGeorgian", "IsGeorgianExtended", "IsGeorgianSupplement", "IsGlagolitic", "IsGreek",
        "IsGreekExtended", "IsGreekandCoptic", "IsGujarati", "IsGurmukhi", "IsHalfwidthandFullwidthForms",
        "IsHangulCompatibilityJamo", "IsHangulJamo", "IsHangulJamoExtended-A", "IsHangulJamoExtended-B", "IsHangulSyllables",
        "IsHanunoo", "IsHebrew", "IsHighPrivateUseSurrogates", "IsHighSurrogates", "IsHiragana", "IsIPAExtensions",
        "IsIdeographicDescriptionCharacters", "IsJavanese", "IsKanbun", "IsKangxiRadicals", "IsKannada", "IsKatakana",
        "IsKatakanaPhoneticExtensions", "IsKayahLi", "IsKhmer", "IsKhmerSymbols", "IsLao", "IsLatin-1Supplement",
        "IsLatinExtended-A", "IsLatinExtended-B", "IsLatinExtended-C", "IsLatinExtended-D", "IsLatinExtended-E",
        "IsLatinExtendedAdditional", "IsLepcha", "IsLetterlikeSymbols", "IsLimbu", "IsLisu", "IsLowSurrogates", "IsMalayalam",
        "IsMandaic", "IsMathematicalOperators", "IsMeeteiMayek", "IsMeeteiMayekExtensions",
        "IsMiscellaneousMathematicalSymbols-A", "IsMiscellaneousMathematicalSymbols-B", "IsMiscellaneousSymbols",
        "IsMiscellaneousSymbolsandArrows", "IsMiscellaneousTechnical", "IsModifierToneLetters", "IsMongolian", "IsMyanmar",
        "IsMyanmarExtended-A", "IsMyanmarExtended-B", "IsNKo", "IsNewTaiLue", "IsNumberForms", "IsOgham", "IsOlChiki",
        "IsOpticalCharacterRecognition", "IsOriya", "IsPhags-pa", "IsPhoneticExtensions", "IsPhoneticExtensionsSupplement",
        "IsPrivateUse", "IsPrivateUseArea", "IsRejang", "IsRunic", "IsSamaritan", "IsSaurashtra", "IsSinhala",
        "IsSmallFormVariants", "IsSpacingModifierLetters", "IsSpecials", "IsSundanese", "IsSundaneseSupplement",
        "IsSuperscriptsandSubscripts", "IsSupplementalArrows-A", "IsSupplementalArrows-B", "IsSupplementalMathematicalOperators",
        "IsSupplementalPunctuation", "IsSylotiNagri", "IsSyriac", "IsSyriacSupplement", "IsTagalog", "IsTagbanwa", "IsTaiLe",
        "IsTaiTham", "IsTaiViet", "IsTamil", "IsTelugu", "IsThaana", "IsThai", "IsTibetan", "IsTifinagh",
        "IsUnifiedCanadianAboriginalSyllabics", "IsUnifiedCanadianAboriginalSyllabicsExtended", "IsVai", "IsVariationSelectors",
        "IsVedicExtensions", "IsVerticalForms", "IsYiRadicals", "IsYiSyllables", "IsYijingHexagramSymbols", "L", "Ll", "Lm",
        "Lo", "Lt", "Lu", "M", "Mc", "Me", "Mn", "N", "Nd", "Nl", "No", "P", "Pc", "Pd", "Pe", "Pf", "Pi", "Po", "Ps", "S", "Sc",
        "Sk", "Sm", "So", "Z", "Zl", "Zp", "Zs"
    ];

    private static readonly HashSet<string> Lookup = new(All, StringComparer.Ordinal);

    public static bool IsDefined(string name) => Lookup.Contains(name);
}
