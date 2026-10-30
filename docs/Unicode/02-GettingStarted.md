---
title: Getting started
description: Install the package, count the characters a reader sees, and normalize a text.
---

## Install the package

Add the `Meziantou.Framework.Unicode` package to a project:

```bash
dotnet add package Meziantou.Framework.Unicode
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`, and
`using System.Text;` or `using System.Globalization;` for a few types of .NET (`Rune`, `NormalizationForm`,
`UnicodeCategory`).

## Split a text into characters

A `string` is a sequence of UTF-16 code units: an accented letter can be two of them, and an emoji four or more. What a
reader sees as one character is a grapheme cluster:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "e\u0301\U0001F44D\U0001F3FD"; // "e" and a combining acute accent, then a thumbs up with a skin tone
Assert.HasCount(6, text); // 6 UTF-16 code units, not characters

// What a reader sees as characters: the grapheme clusters
var characters = new List<string>();
foreach (Range cluster in Unicode.EnumerateGraphemeClusters(text))
{
    characters.Add(text[cluster]);
}

Assert.Equal(["e\u0301", "\U0001F44D\U0001F3FD"], characters);

// The composed form of the text, the same on every platform
Assert.Equal("\u00E9\U0001F44D\U0001F3FD", UnicodeNormalization.Normalize(text));
```
<!-- endref -->

- [Unicode.EnumerateGraphemeClusters](cref:Meziantou.Framework.Unicode.EnumerateGraphemeClusters) gives the range of each cluster in the text. It does not allocate.
- [UnicodeNormalization.Normalize](cref:Meziantou.Framework.UnicodeNormalization.Normalize) composes the letter and its accent into one code point. It gives the same result on
  every platform, including in globalization-invariant mode.

## Next steps

- [Character properties](03-CharacterProperties.md): what the standard says about each code point.
- [Text segmentation](07-TextSegmentation.md): characters, words and sentences.
- [Line breaking](08-LineBreaking.md): where a text can wrap.
- [Look-alike characters](13-LookAlikeCharacters.md): detect user names that imitate others.
