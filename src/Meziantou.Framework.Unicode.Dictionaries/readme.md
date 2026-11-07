# Meziantou.Framework.Unicode.Dictionaries

Chinese, Japanese, Thai, Lao, Khmer and Burmese are written without spaces between words. Finding their words needs a
word list: the Unicode Text Segmentation (UAX #29) and Line Breaking (UAX #14) algorithms leave this to a dictionary or
another language-specific analysis. This package provides the word lists of [ICU](https://github.com/unicode-org/icu)
and ports of the dictionary break engines of ICU (`icu4c/source/common/dictbe.cpp`), the analysis that the browsers and
`Intl.Segmenter` of JavaScript use. It depends on [Meziantou.Framework.Unicode](../Meziantou.Framework.Unicode/readme.md).
The types are in the `Meziantou.Framework` namespace.

- `IcuDictionaryBreakEngine`: the engines of Chinese and Japanese, Thai, Lao, Khmer and Burmese. An engine finds the word
  boundaries inside a run of the characters it handles.
- `IcuWordDictionary`: the word lists. A list is loaded on first use: an application that only segments Thai text only
  loads the Thai list.

The word lists take about 1.5 MB in the assembly (compressed with Brotli): about 930 KB for Chinese and Japanese, 70 KB
for Thai, 90 KB for Lao, 260 KB for Khmer and 150 KB for Burmese. Once loaded, a list takes from about 0.5 MB of memory
(Thai) to 3 MB (Chinese and Japanese). The version of ICU of the lists is `IcuWordDictionary.IcuVersion`.

## Finding the words of a run

An engine segments a run of the characters it handles (`Handles`): the positions it writes are where the words start,
other than the start of the run.

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

The engines do not find the runs: the caller applies the rules of its segmentation, such as the word or line breaking
rules of Unicode, and gives each run of the characters of an engine to that engine. `IcuDictionaryBreakEngine.GetEngine`
returns the engine of a character:

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

The engines are the ones of ICU:

- Chinese and Japanese (the Han, Hiragana and Katakana scripts): the segmentation of the run whose words have the
  smallest total cost. The run is normalized to NFKC before the words are looked up.
- Thai, Lao, Khmer and Burmese (the characters of these scripts whose line breaking class is Complex_Context): the
  longest words of the list that are followed by other words of the list, with up to three words of lookahead. The
  characters that start no word are attached to the word before them.

The engines are stateless and can be used by several threads at the same time.

## Word lists

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

`IcuWordDictionary.CreateLineBreakDictionary()` creates a `LineBreakDictionary` of Meziantou.Framework.Unicode with the
words of Thai, Lao, Khmer and Burmese, for `Unicode.EnumerateLineBreaks` or the `LineBreakDictionary` of the text layout
options of Meziantou.Framework.Imaging. `LineBreakDictionary` segments a text with its own rule: its breaks are close to
the ones of the engines of ICU, but not always the same.

## Licenses

The word lists and the engines come from ICU, under the Unicode License v3. The word lists of Chinese and Japanese, Lao
and Burmese have additional notices. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The lists are generated by `tools/Meziantou.Framework.Unicode.Dictionaries.Generator` from the data release of ICU
pinned in its `data-versions.json`.
