---
title: Normalization
description: Normalize text to NFC, NFD, NFKC or NFKD the same way on every platform, check whether a text is normalized, and normalize into a buffer.
---

## Normalize a text

The same text can be written with different code points: `é` is one code point, or an `e` followed by a combining
acute accent. Normalization gives one form to texts that are the same, so that you can compare them, use them as keys,
or search them. `UnicodeNormalization` implements the four forms of [UAX #15](https://www.unicode.org/reports/tr15/):

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Normalization;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("caf\u00E9", UnicodeNormalization.Normalize("cafe\u0301")); // NFC
Assert.Equal("cafe\u0301", UnicodeNormalization.Normalize("caf\u00E9", NormalizationForm.FormD));
Assert.True(UnicodeNormalization.IsNormalized("caf\u00E9"));

// Span-based
Span<char> destination = stackalloc char[16];
Assert.True(UnicodeNormalization.TryNormalize("cafe\u0301", destination, out var written));
Assert.Equal("caf\u00E9", destination[..written]);

Assert.True(UnicodeNormalization.TryComposeCanonical('e', 0x0301, out var composite));
Assert.Equal('\u00E9', composite);
```
<!-- endref -->

- `Normalize` uses NFC by default, the composed form most text is in. It returns the same instance when the text is
  already normalized.
- `IsNormalized` checks a text without allocating.
- `TryNormalize` writes into a buffer, and `GetNormalizedLength` gives the length it needs.
- `TryComposeCanonical`, `TryDecomposeCanonical` and `GetCanonicalDecomposition` work on code points.

Unlike `string.Normalize`, the results do not depend on the globalization library of the platform: they are the same
everywhere, including in globalization-invariant mode, where `string.Normalize` only handles ASCII.

## Choose a form

| Form | What it does | Use it for |
| --- | --- | --- |
| NFC (`FormC`) | Composes characters | Storing and exchanging text |
| NFD (`FormD`) | Decomposes characters | Processing letters and their accents separately |
| NFKC (`FormKC`) | Composes, and replaces the characters that differ only by their presentation | Identifiers, search keys |
| NFKD (`FormKD`) | Decomposes, and replaces the characters that differ only by their presentation | Search keys; remove the combining marks afterwards to ignore accents |

The compatibility forms (NFKC and NFKD) lose information: a ligature becomes its letters, a superscript a plain digit.
Use them for comparison keys, not to store the text a user typed:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_CompatibilityNormalization;format=md-fenced-code-block;dedent=true -->
```csharp
// NFKC also replaces the characters that differ only by their presentation
Assert.Equal("fi", UnicodeNormalization.Normalize("\uFB01", NormalizationForm.FormKC)); // the "fi" ligature
Assert.Equal("x2", UnicodeNormalization.Normalize("x\u00B2", NormalizationForm.FormKC)); // a superscript two
Assert.Equal("ABC", UnicodeNormalization.Normalize("\uFF21\uFF22\uFF23", NormalizationForm.FormKC)); // full-width letters

// NFC keeps them
Assert.Equal("\uFB01", UnicodeNormalization.Normalize("\uFB01"));
```
<!-- endref -->
