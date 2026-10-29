---
title: Text segmentation
description: Split a text into grapheme clusters, words and sentences, and cut a text without breaking a character.
---

The boundaries are those of [UAX #29](https://www.unicode.org/reports/tr29/) for Unicode 18.0.0. The enumerators are
`ref struct`s over a `ReadOnlySpan<char>`: they do not allocate, and each segment is a `Range` of the text.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Segmentation;format=md-fenced-code-block;dedent=true -->
```csharp
// Extended grapheme clusters: what a reader perceives as one character
var text = "e\u0301\U0001F1EB\U0001F1F7"; // "e" and a combining acute accent, then the French flag
var clusters = new List<string>();
foreach (Range cluster in Unicode.EnumerateGraphemeClusters(text))
{
    clusters.Add(text[cluster]);
}

Assert.Equal(["e\u0301", "\U0001F1EB\U0001F1F7"], clusters);

// Word boundaries: words, numbers, runs of spaces, punctuation marks
text = "Hello, world!";
var segments = new List<string>();
foreach (Range segment in Unicode.EnumerateWordBoundaries(text))
{
    segments.Add(text[segment]);
}

Assert.Equal(["Hello", ",", " ", "world", "!"], segments);

// The kind of each segment: whether it is a word (isWordLike in Intl.Segmenter)
text = "I have 2 cats.";
var words = new List<string>();
var enumerator = Unicode.EnumerateWordBoundaries(text);
while (enumerator.MoveNext())
{
    if (enumerator.CurrentKind is not UnicodeWordKind.None) // Number, Letter, Kana or Ideographic
    {
        words.Add(text[enumerator.Current]);
    }
}

Assert.Equal(["I", "have", "2", "cats"], words);

// Sentences: the spaces after the terminal punctuation belong to the sentence
text = "It costs $3.50, i.e. not much. Really? Yes!";
var sentences = new List<string>();
foreach (Range sentence in Unicode.EnumerateSentenceBoundaries(text))
{
    sentences.Add(text[sentence]);
}

Assert.Equal(["It costs $3.50, i.e. not much. ", "Really? ", "Yes!"], sentences);

// Line break opportunities
text = "A long line\nof text";
var lines = new List<string>();
var lineStart = 0;
foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text))
{
    // lineBreak.Index: the index of the first character of the next line
    // lineBreak.IsMandatory: after a line feed, and at the end of the text
    lines.Add(text[lineStart..lineBreak.Index] + (lineBreak.IsMandatory ? " (mandatory)" : ""));
    lineStart = lineBreak.Index;
}

Assert.Equal(["A ", "long ", "line\n (mandatory)", "of ", "text (mandatory)"], lines);
```
<!-- endref -->

The last part of this example, the line breaks, is described in [Line breaking](08-LineBreaking.md).

## Grapheme clusters

A grapheme cluster is what a reader perceives as one character: a letter and its combining marks, a Hangul syllable, an
emoji sequence, a flag. Count, cut and reverse text by grapheme clusters, never by `char`: a `char` can be half of an
emoji, or an accent without its letter.

- `Unicode.EnumerateGraphemeClusters` enumerates the clusters of a text.
- `Unicode.GetGraphemeClusterLength` gives the length of the first cluster of a text, in UTF-16 code units.

For instance, to shorten a text to a maximum length without cutting a character:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_TruncateText;format=md-fenced-code-block;dedent=true -->
```csharp
static string Truncate(string text, int maxLength)
{
    // Keep whole grapheme clusters: an accent or an emoji is never cut in half
    var length = 0;
    foreach (Range cluster in Unicode.EnumerateGraphemeClusters(text))
    {
        var end = cluster.End.GetOffset(text.Length);
        if (end > maxLength)
            break;

        length = end;
    }

    return text[..length];
}

Assert.Equal("Caf", Truncate("Cafe\u0301 au lait", 4)); // not "Cafe" without its accent
Assert.Equal("Go ", Truncate("Go \U0001F1EB\U0001F1F7", 5)); // not half of the flag
```
<!-- endref -->

## Words

`Unicode.EnumerateWordBoundaries` gives the segments between word boundaries: words, numbers, runs of spaces and
punctuation marks. `CurrentKind` tells whether a segment is a word:

- `None` for spaces, punctuation, symbols and emoji;
- else `Number`, `Letter`, `Kana` or `Ideographic`, from the last character of the segment that is not a combining mark
  or a format character. A low line takes the kind of the character before it: `a_` is a letter word, `1_` a number.

Chinese, Japanese, Thai, Lao, Khmer and Burmese are written without spaces between words: the default rules do not
find their words. The `Meziantou.Framework.Unicode.Dictionaries` package finds them with word lists: see
[Word dictionaries](09-WordDictionaries.md).

## Sentences

`Unicode.EnumerateSentenceBoundaries` applies the default rules: the spaces after the terminal punctuation belong to
the sentence. There is no list of abbreviations: `Mr. Smith` is two sentences, while `e.g. the` (a full stop before a
lowercase word), `U.S.A.` and `3.14` are not broken.
