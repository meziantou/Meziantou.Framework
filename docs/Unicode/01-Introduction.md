---
title: Introduction
description: Unicode algorithms and character data for .NET that do not depend on the platform, the runtime version or globalization settings.
---

`Meziantou.Framework.Unicode` implements the algorithms of the Unicode Standard that text processing needs, with the
data of Unicode 18.0.0 embedded in the assembly. The results do not depend on the operating system, on the version of
.NET, or on its globalization settings: they are the same on Windows, Linux and macOS, and in applications that run in
globalization-invariant mode (`InvariantGlobalization=true`), where `string.Normalize` only handles ASCII and the case
rules of a language are not available.

## What you can do with it

- **Read the properties of characters**: general category, scripts, blocks, line breaking and segmentation classes,
  bidirectional class, emoji, case mappings, names. See [Character properties](03-CharacterProperties.md) and
  [Scripts](04-Scripts.md).
- **Convert the case** of a text with the rules of Turkish, Azerbaijani, Lithuanian and the Greek final sigma. See
  [Case conversion](05-CaseConversion.md).
- **Normalize text** to NFC, NFD, NFKC or NFKD. See [Normalization](06-Normalization.md).
- **Split text** into what readers see as characters, into words and into sentences. See
  [Text segmentation](07-TextSegmentation.md).
- **Find where lines can break**, including in Thai, Lao, Khmer and Burmese with word lists, and **hyphenate** words.
  See [Line breaking](08-LineBreaking.md), [Word dictionaries](09-WordDictionaries.md) and
  [Hyphenation](10-Hyphenation.md).
- **Order bidirectional text** for display, such as Arabic or Hebrew mixed with Latin text. See
  [Bidirectional text](11-BidirectionalText.md).
- **Convert internationalized domain names** to and from their ASCII form. See
  [Internationalized domain names](12-InternationalizedDomainNames.md).
- **Detect spoofing**: names that look alike, and names that mix scripts. See
  [Look-alike characters](13-LookAlikeCharacters.md).

## Packages

| Package | Contains |
| --- | --- |
| `Meziantou.Framework.Unicode` | Everything above |
| `Meziantou.Framework.Unicode.Dictionaries` | The word lists of Chinese, Japanese, Thai, Lao, Khmer and Burmese, and the engines that find the words of these languages |

The types are in the `Meziantou.Framework` namespace.

## Principles

- **One version of Unicode.** Every result follows Unicode 18.0.0 (`UnicodeProperties.UnicodeVersion`), whatever the
  version the runtime implements.
- **No allocation on hot paths.** The properties are read from tables indexed by the code point, and the enumerators of
  segments and line breaks are `ref struct`s over a `ReadOnlySpan<char>`.
- **Untrusted data is bounded.** The word lists and hyphenation patterns you load are checked against limits, so that a
  file from an untrusted source cannot exhaust memory.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and split a text into characters.
