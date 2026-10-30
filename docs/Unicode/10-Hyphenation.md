---
title: Hyphenation
description: Find where a word can be hyphenated, from the hyphenation patterns of its language.
---

## Hyphenate a word

The rules of Unicode say where a line can break, but hyphenating a word needs data for its language. [Hyphenator](cref:Meziantou.Framework.Hyphenator)
applies the pattern algorithm of TeX (Franklin Liang) to patterns you provide:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/HyphenatorTests.cs;region=Hyphenator_Hyphenate;format=md-fenced-code-block;dedent=true -->
```csharp
// The patterns, as plain text: "hy3ph he2n ..." or a \patterns{...} group, then exceptions such as "as-so-ciate".
// Use Hyphenator.Load("hyph-en-us.pat.txt", "hyph-en-us.hyp.txt") to read them from files.
var hyphenator = Hyphenator.Parse("hy3ph he2n hena4 hen5at 1na n2at 1tio 2io", "as-so-ciate");

Span<int> positions = stackalloc int[32];
var count = hyphenator.Hyphenate("hyphenation", positions);
Assert.Equal<int>([2, 6], positions[..count]); // hy-phen-ation
```
<!-- endref -->

- [Hyphenate](cref:Meziantou.Framework.Hyphenator.Hyphenate) takes one word, in any case, and writes the indexes before which a hyphen can be inserted. It does not
  allocate. Use [Unicode.EnumerateWordBoundaries](cref:Meziantou.Framework.Unicode.EnumerateWordBoundaries) to find the words of a text (see
  [Text segmentation](07-TextSegmentation.md#words)).
- A break is never inside a grapheme cluster.
- The patterns match code points: pass the word in the normalization form of the patterns, usually NFC (see
  [Normalization](06-Normalization.md)).
- A hyphenator is immutable and can be used by several threads at the same time: load it once per language and
  reuse it.

## Get patterns

No pattern is included in the package. The [hyph-utf8](https://ctan.org/pkg/hyph-utf8) project publishes patterns for
many languages, each under its own license: use the plain text files (`hyph-<language>.pat.txt` and
`hyph-<language>.hyp.txt`).

- [Load](cref:Meziantou.Framework.Hyphenator.Load) and [LoadAsync](cref:Meziantou.Framework.Hyphenator.LoadAsync) read UTF-8 files or streams; [Parse](cref:Meziantou.Framework.Hyphenator.Parse) reads text or UTF-8 bytes.
- The patterns are words with digits (`hy3ph`), separated by whitespace, optionally inside a `\patterns{...}` group;
  the exceptions are hyphenated words (`as-so-ciate`), in a separate file or in a `\hyphenation{...}` group. An
  exception without a hyphen is never broken, and `%` starts a comment.
- Only the plain text form is read. A file that uses TeX macros, `\lccode` or the `^^` notation is rejected with a
  `FormatException`.

## Options

[HyphenatorOptions](cref:Meziantou.Framework.HyphenatorOptions) is given when the patterns are loaded:

| Option | Default | Description |
| --- | --- | --- |
| [LeftHyphenMin](cref:Meziantou.Framework.HyphenatorOptions.LeftHyphenMin) | 2 | The smallest number of characters before the first break of a word |
| [RightHyphenMin](cref:Meziantou.Framework.HyphenatorOptions.RightHyphenMin) | 3 | The smallest number of characters after the last break of a word |
| [MaxWordLength](cref:Meziantou.Framework.HyphenatorOptions.MaxWordLength) | 255 | A longer word has no break |
| [MaxPatternCount](cref:Meziantou.Framework.HyphenatorOptions.MaxPatternCount) | 250,000 | The greatest number of patterns, and of exceptions |
| [MaxPatternLength](cref:Meziantou.Framework.HyphenatorOptions.MaxPatternLength) | 64 | The greatest number of letters of a pattern or of an exception |
| [MaxInputLength](cref:Meziantou.Framework.HyphenatorOptions.MaxInputLength) | 8 MiB | The greatest length of the patterns, and of the exceptions |

- The right values of [LeftHyphenMin](cref:Meziantou.Framework.HyphenatorOptions.LeftHyphenMin) and [RightHyphenMin](cref:Meziantou.Framework.HyphenatorOptions.RightHyphenMin) depend on the language: they are the `\lefthyphenmin` and
  `\righthyphenmin` of TeX, and the files of hyph-utf8 give them. A character is a grapheme cluster.
- The limits protect you from pattern files from an untrusted source: a file that exceeds one is rejected with a
  `FormatException` before memory is allocated for it.

Hyphenation that changes the spelling of a word is not supported.

To hyphenate the paragraphs drawn on images, give the hyphenator to the text style of
[Meziantou.Framework.Imaging](../Imaging/08-Text/04-TextLayout.md#hyphenation-and-scripts-without-spaces).
