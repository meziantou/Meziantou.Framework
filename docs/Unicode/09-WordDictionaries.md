---
title: Word dictionaries
description: Find the words of Chinese, Japanese, Thai, Lao, Khmer and Burmese text, and wrap such text, with the word lists of the Dictionaries package.
---

Chinese, Japanese, Thai, Lao, Khmer and Burmese are written without spaces between words. Finding their words needs a
list of words: the segmentation and line breaking rules of Unicode leave it to a dictionary.
`Meziantou.Framework.Unicode.Dictionaries` provides the word lists of these languages and the engines that segment text
with them.

```bash
dotnet add package Meziantou.Framework.Unicode.Dictionaries
```

The package depends on `Meziantou.Framework.Unicode`, and its types are in the `Meziantou.Framework` namespace too.

## Wrap text

`IcuWordDictionary.CreateLineBreakDictionary` gives a `LineBreakDictionary` with the words of Thai, Lao, Khmer and
Burmese, for `Unicode.EnumerateLineBreaks`:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Dictionaries.Tests/IcuDictionaryTests.cs;region=IcuWordDictionary_LineBreaks;format=md-fenced-code-block;dedent=true -->
```csharp
// The words of Thai, Lao, Khmer and Burmese, for the line breaking algorithm
var dictionary = IcuWordDictionary.CreateLineBreakDictionary();

var text = "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u0E07\u0E48\u0E32\u0E22\u0E19\u0E34\u0E14\u0E40\u0E14\u0E35\u0E22\u0E27"; // "Thai is very easy", written without spaces
var breaks = new List<int>();
foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text, dictionary))
{
    breaks.Add(lineBreak.Index);
}

Assert.Equal([4, 7, 11, 14, 19], breaks); // before each word, and at the end of the text
```
<!-- endref -->

The dictionary also works with the text layout of [Meziantou.Framework.Imaging](../Imaging/08-Text/04-TextLayout.md#hyphenation-and-scripts-without-spaces)
(`TextLayoutOptions.LineBreakDictionary`). A `LineBreakDictionary` segments text with its own rule, described in
[Line breaking](08-LineBreaking.md#how-the-words-are-chosen): its breaks are close to the ones of the engines below, but
not always the same.

## Find the words of a run

An engine finds the words inside a run of the characters it handles (`Handles`). The positions it writes are where the
words start, other than the start of the run:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Dictionaries.Tests/IcuDictionaryTests.cs;region=IcuDictionaryBreakEngine_FindBreaks;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "ภาษาไทยง่ายนิดเดียว"; // "Thai is very easy", written without spaces
var engine = IcuDictionaryBreakEngine.Thai;

Span<int> breaks = stackalloc int[text.Length];
var count = engine.FindBreaks(text, breaks);

// The words start at 4, 7, 11 and 14: ภาษา|ไทย|ง่าย|นิด|เดียว
Assert.Equal([4, 7, 11, 14], breaks[..count].ToArray());
```
<!-- endref -->

The engines do not find the runs: apply the rules of your segmentation, such as the word or line breaking rules of
Unicode, and give each run of the characters of an engine to that engine. `IcuDictionaryBreakEngine.GetEngine` returns
the engine of a character, or `null`:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Dictionaries.Tests/IcuDictionaryTests.cs;region=IcuDictionaryBreakEngine_Segment;format=md-fenced-code-block;dedent=true -->
```csharp
static List<string> SplitWords(string text)
{
    var words = new List<string>();
    var breaks = new int[text.Length];
    var start = 0;
    while (start < text.Length)
    {
        // A run of the characters of an engine, or of the characters no engine handles
        var engine = IcuDictionaryBreakEngine.GetEngine(char.ConvertToUtf32(text, start));
        var end = start;
        while (end < text.Length && IcuDictionaryBreakEngine.GetEngine(char.ConvertToUtf32(text, end)) == engine)
        {
            end += char.IsSurrogatePair(text, end) ? 2 : 1;
        }

        var wordStart = start;
        if (engine is not null)
        {
            var count = engine.FindBreaks(text.AsSpan(start, end - start), breaks);
            foreach (var position in breaks.AsSpan(0, count))
            {
                words.Add(text[wordStart..(start + position)]);
                wordStart = start + position;
            }
        }

        words.Add(text[wordStart..end]);
        start = end;
    }

    return words;
}

Assert.Equal(["今日", "は", "良い", "天気", "です", "ね"], SplitWords("今日は良い天気ですね"));
Assert.Equal(["ພາສາ", "ລາວ", "ງ່າຍ"], SplitWords("ພາສາລາວງ່າຍ"));
```
<!-- endref -->

| Engine | Characters | How the words are chosen |
| --- | --- | --- |
| `ChineseJapanese` | Han, Hiragana and Katakana | The segmentation whose words have the smallest total cost; the run is normalized to NFKC before the words are looked up |
| `Thai`, `Lao`, `Khmer`, `Burmese` | The characters of these scripts whose line breaking class is `Complex_Context` | The longest words that are followed by other words of the list, with up to three words of lookahead; the characters that start no word are attached to the word before them |

The engines are stateless and can be used by several threads at the same time.

## Word lists

`IcuWordDictionary.Get` gives the list of a language. A list is loaded the first time it is used: an application that
only segments Thai text only loads the Thai list.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Dictionaries.Tests/IcuDictionaryTests.cs;region=IcuWordDictionary_Contains;format=md-fenced-code-block;dedent=true -->
```csharp
var thai = IcuWordDictionary.Get(IcuWordDictionaryKind.Thai);
Assert.True(thai.Contains("ภาษา"));
Assert.False(thai.Contains("ภาษาไทยง่าย"));

// The words of the Chinese and Japanese list have a cost: the lower, the more frequent
var chineseJapanese = IcuWordDictionary.Get(IcuWordDictionaryKind.ChineseJapanese);
Assert.True(chineseJapanese.TryGetValue("天気", out var cost));
Assert.True(cost > 0);
```
<!-- endref -->

The lists take about 1.5 MB in the assembly, compressed. Once loaded, a list takes from about 0.5 MB of memory (Thai)
to 3 MB (Chinese and Japanese).

## Licenses

The word lists and the engines are under the Unicode License v3, and the lists of Chinese and Japanese, Lao and Burmese
have additional notices: the `THIRD-PARTY-NOTICES.md` file of the package has them. `IcuWordDictionary.IcuVersion`
gives the version of the data.
