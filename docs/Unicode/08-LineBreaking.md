---
title: Line breaking
description: Find where a text can wrap, wrap a text to a maximum length, and choose the rules for Chinese, Japanese and Korean text.
---

## Find the break opportunities

[Unicode.EnumerateLineBreaks](cref:Meziantou.Framework.Unicode.EnumerateLineBreaks) gives the positions where a line can break, with the rules of
[UAX #14](https://www.unicode.org/reports/tr14/). Each [UnicodeLineBreak](cref:Meziantou.Framework.UnicodeLineBreak) has the [Index](cref:Meziantou.Framework.UnicodeLineBreak.Index) of the first character of the
next line, and [IsMandatory](cref:Meziantou.Framework.UnicodeLineBreak.IsMandatory) for the breaks after a line feed and at the end of the text. A text renderer measures the
text between break opportunities, and starts a new line at the last opportunity that fits.

This example wraps a text to a maximum number of characters:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_WrapText;format=md-fenced-code-block;dedent=true -->
```csharp
static List<string> Wrap(string text, int maxLength)
{
    var lines = new List<string>();
    var lineStart = 0;
    var previousBreak = 0;
    foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text))
    {
        // The line would be too long: break at the previous opportunity
        if (lineBreak.Index - lineStart > maxLength && previousBreak > lineStart)
        {
            lines.Add(text[lineStart..previousBreak].TrimEnd());
            lineStart = previousBreak;
        }

        if (lineBreak.IsMandatory)
        {
            lines.Add(text[lineStart..lineBreak.Index].TrimEnd());
            lineStart = lineBreak.Index;
        }

        previousBreak = lineBreak.Index;
    }

    return lines;
}

Assert.Equal(["The quick brown", "fox jumps over", "the lazy dog."], Wrap("The quick brown fox jumps over the lazy dog.", 16));
Assert.Equal(["See https://", "www.meziantou.net/", "blog"], Wrap("See https://www.meziantou.net/blog", 16));
```
<!-- endref -->

- The spaces after a word belong to the line before the break: trim them when you draw the line.
- A URL is broken after its slashes, not inside its words.
- Counting `char`s is only right for a monospaced font and for Latin text: measure the width of the text instead, and
  count grapheme clusters (see [Text segmentation](07-TextSegmentation.md#grapheme-clusters)).
- A break never falls inside a grapheme cluster.
- A soft hyphen (U+00AD) is a break opportunity like any other: recognize it by the character before the break, and draw
  a hyphen at the end of the line.

## Tailor the rules

[EnumerateLineBreaks](cref:Meziantou.Framework.Unicode.EnumerateLineBreaks) takes two optional tailorings, which match the CSS properties `line-break` and `word-break`:

| Tailoring | Values |
| --- | --- |
| [UnicodeLineBreakStrictness](cref:Meziantou.Framework.UnicodeLineBreakStrictness) (`line-break`) | [Strict](cref:Meziantou.Framework.UnicodeLineBreakStrictness.Strict), the default rules; [Normal](cref:Meziantou.Framework.UnicodeLineBreakStrictness.Normal), a line can start with a small kana; [Anywhere](cref:Meziantou.Framework.UnicodeLineBreakStrictness.Anywhere), a line can break between any two grapheme clusters |
| [UnicodeWordBreakBehavior](cref:Meziantou.Framework.UnicodeWordBreakBehavior) (`word-break`) | [Normal](cref:Meziantou.Framework.UnicodeWordBreakBehavior.Normal); [BreakAll](cref:Meziantou.Framework.UnicodeWordBreakBehavior.BreakAll), a line can break between any two letters; [KeepAll](cref:Meziantou.Framework.UnicodeWordBreakBehavior.KeepAll), no break between letters or ideographs |

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_LineBreakTailorings;format=md-fenced-code-block;dedent=true -->
```csharp
static List<string> Split(string text, UnicodeLineBreakStrictness strictness = UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior wordBreak = UnicodeWordBreakBehavior.Normal)
{
    var segments = new List<string>();
    var start = 0;
    foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text, strictness, wordBreak))
    {
        segments.Add(text[start..lineBreak.Index]);
        start = lineBreak.Index;
    }

    return segments;
}

// Ideographs: a line can break between any two of them, unless KeepAll keeps the words together
var japanese = "\u65E5\u672C\u8A9E";
Assert.Equal(["\u65E5", "\u672C", "\u8A9E"], Split(japanese));
Assert.Equal(["\u65E5\u672C\u8A9E"], Split(japanese, wordBreak: UnicodeWordBreakBehavior.KeepAll));

// BreakAll: a line can break between two letters, as in a table cell that must not overflow
Assert.Equal(["a", "b", "c"], Split("abc", wordBreak: UnicodeWordBreakBehavior.BreakAll));

// A small kana starts no line with the strict rules; Normal allows it
var katakana = "\u30C1\u30E3";
Assert.Equal(["\u30C1\u30E3"], Split(katakana));
Assert.Equal(["\u30C1", "\u30E3"], Split(katakana, UnicodeLineBreakStrictness.Normal));
```
<!-- endref -->

[KeepAll](cref:Meziantou.Framework.UnicodeWordBreakBehavior.KeepAll) is common for Korean, whose words are separated by spaces. [BreakAll](cref:Meziantou.Framework.UnicodeWordBreakBehavior.BreakAll) and [Anywhere](cref:Meziantou.Framework.UnicodeLineBreakStrictness.Anywhere) suit narrow spaces where
overflowing is worse than an unusual break.

## Text written without spaces

Thai, Lao, Khmer, Burmese and a few other scripts are written without spaces between words. Their characters have the
line breaking class `Complex_Context` (SA), which UAX #14 leaves to a dictionary: without one, a sentence only breaks at
spaces and zero width spaces. [LineBreakDictionary](cref:Meziantou.Framework.LineBreakDictionary) finds the words of such text in a list of words you provide:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_LineBreakDictionary;format=md-fenced-code-block;dedent=true -->
```csharp
// The word list, as plain text: one word per line.
// Use LineBreakDictionary.Load("thaidict.txt") to read it from a file.
var dictionary = LineBreakDictionary.Parse("ภาษา\nไทย");

// The line break opportunities of a text: those of UAX #14, and those before the words of the dictionary
var breaks = new List<int>();
foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks("Hello ภาษาไทย!", dictionary))
{
    breaks.Add(lineBreak.Index);
}

Assert.Equal([6, 10, 14], breaks); // after the space, before "ไทย", the end of the text

// The words of a run of text of the script of the dictionary
Span<int> positions = stackalloc int[16];
var count = dictionary.FindBreaks("ภาษาไทย", positions);
Assert.Equal<int>([4], positions[..count]);
```
<!-- endref -->

The `Meziantou.Framework.Unicode.Dictionaries` package includes the word lists of Thai, Lao, Khmer and Burmese:
[IcuWordDictionary.CreateLineBreakDictionary()](cref:Meziantou.Framework.IcuWordDictionary.CreateLineBreakDictionary) gives a [LineBreakDictionary](cref:Meziantou.Framework.LineBreakDictionary) of them. See
[Word dictionaries](09-WordDictionaries.md).

### Word lists

- The list is plain text, in UTF-8 when it is read from a file or a stream ([Load](cref:Meziantou.Framework.LineBreakDictionary.Load), [LoadAsync](cref:Meziantou.Framework.LineBreakDictionary.LoadAsync)): one word per line,
  `#` starts a comment, blank lines are ignored, and so is what follows the first tab of a line (a frequency, for
  instance).
- A word is kept as written, without normalization or case mapping: pass the text in the normalization form of the
  list.
- A dictionary is immutable and thread-safe. [FindBreaks](cref:Meziantou.Framework.LineBreakDictionary.FindBreaks) and the enumeration do not allocate, and their cost is
  proportional to the length of the text.
- A list from an untrusted source cannot exhaust memory: a list that exceeds a limit of [LineBreakDictionaryOptions](cref:Meziantou.Framework.LineBreakDictionaryOptions) is
  rejected with a `FormatException`.

| [LineBreakDictionaryOptions](cref:Meziantou.Framework.LineBreakDictionaryOptions) | Default |
| --- | --- |
| [MaxWordCount](cref:Meziantou.Framework.LineBreakDictionaryOptions.MaxWordCount) | 500,000 words |
| [MaxWordLength](cref:Meziantou.Framework.LineBreakDictionaryOptions.MaxWordLength) | 128 UTF-16 code units |
| [MaxInputLength](cref:Meziantou.Framework.LineBreakDictionaryOptions.MaxInputLength) | 8 MiB |

### How the words are chosen

[FindBreaks](cref:Meziantou.Framework.LineBreakDictionary.FindBreaks) segments the text it is given, and [EnumerateLineBreaks](cref:Meziantou.Framework.Unicode.EnumerateLineBreaks) each run of `Complex_Context` characters of the
text:

- A run starts with a letter: the combining marks before its first letter belong to the character before them. Spaces,
  digits, punctuation, zero width spaces and the letters of other scripts are not in a run.
- A word is only found between two boundaries of the run: its start, its end, and the grapheme cluster boundaries that
  are not before a combining mark.
- The words must not overlap. The segmentation that leaves the fewest characters outside words is taken, then the one
  with the fewest words; when several remain, the one with the longest word at the first position where they differ.
- A line can break before each word found, except at the start of the run. The characters that are in no word stay with
  what precedes them: a run without any word of the list has no break.
- A break of the dictionary is never mandatory. [KeepAll](cref:Meziantou.Framework.UnicodeWordBreakBehavior.KeepAll) keeps them, as `word-break: keep-all` does in CSS. With
  [BreakAll](cref:Meziantou.Framework.UnicodeWordBreakBehavior.BreakAll) or [Anywhere](cref:Meziantou.Framework.UnicodeLineBreakStrictness.Anywhere), a line already breaks between any two letters, and the dictionary is not used.
- A run longer than 1,024 UTF-16 code units is segmented piece by piece, so that the memory used stays bounded.

## Draw wrapped text

To draw paragraphs on images, the text layout of [Meziantou.Framework.Imaging](../Imaging/08-Text/04-TextLayout.md#hyphenation-and-scripts-without-spaces)
uses these rules, and accepts a [LineBreakDictionary](cref:Meziantou.Framework.LineBreakDictionary) and a [Hyphenator](cref:Meziantou.Framework.Hyphenator).
