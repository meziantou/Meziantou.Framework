---
title: Case conversion
description: Convert a text to uppercase or lowercase with the rules of Turkish, Azerbaijani, Lithuanian and the Greek final sigma, the same way on every platform.
---

## Convert a text

[UnicodeCaseMapping.ToUpper](cref:Meziantou.Framework.UnicodeCaseMapping.ToUpper) and [ToLower](cref:Meziantou.Framework.UnicodeCaseMapping.ToLower) convert a whole text with the full case mappings of the standard, a
character being able to become several ones:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_CaseMappingOfText;format=md-fenced-code-block;dedent=true -->
```csharp
// The mappings that do not depend on the language, with the final sigma of Greek
Assert.Equal("STRASSE", UnicodeCaseMapping.ToUpper("Stra\u00DFe"));
Assert.Equal("\u03BF\u03B4\u03CC\u03C2", UnicodeCaseMapping.ToLower("\u039F\u0394\u038C\u03A3"));

// Turkish and Azerbaijani: the dotless and the dotted I
Assert.Equal("\u0131i", UnicodeCaseMapping.ToLower("I\u0130", "tr"));
Assert.Equal("\u0130I", UnicodeCaseMapping.ToUpper("i\u0131", "tr-TR"));

// Lithuanian: the i keeps its dot under an accent
Assert.Equal("i\u0307\u0300", UnicodeCaseMapping.ToLower("\u00CC", "lt"));
```
<!-- endref -->

- The second parameter is the language of the text, as a BCP 47 language tag (`tr`, `lt-LT`); only the language subtag
  is read, ignoring case. `null` selects the mappings that do not depend on the language.
- In every language, a Greek capital sigma becomes the final sigma at the end of a word, and the small sigma
  elsewhere.
- In Turkish and Azerbaijani (`tr`, `az`), `I` and `i` are distinct letters from the dotless `ı` and the dotted `İ`.
- In Lithuanian (`lt`), a lowercase `i` keeps its dot under an accent.
- The other languages have no rule of their own in the standard: they use the mappings that do not depend on the
  language.

## Why not `string.ToUpper`

`string.ToUpper(CultureInfo)` depends on the globalization library of the operating system and on its version.
[UnicodeCaseMapping](cref:Meziantou.Framework.UnicodeCaseMapping) does not: it gives the same result on every platform and every version of .NET, including in
globalization-invariant mode, for the version of Unicode of the package. An unpaired surrogate is copied as it is.

The rules of the Common Locale Data Repository that the standard does not include, such as removing the accents of
Greek when uppercasing, are not applied.

## Compare without case

To compare two texts without case, do not convert both to lowercase: case folding is made for comparison. Fold each
code point with [UnicodeProperties.ToSimpleCaseFold](cref:Meziantou.Framework.UnicodeProperties.ToSimpleCaseFold), after [normalizing](06-Normalization.md) the texts. See
[Character properties](03-CharacterProperties.md#map-the-case-of-a-code-point).
