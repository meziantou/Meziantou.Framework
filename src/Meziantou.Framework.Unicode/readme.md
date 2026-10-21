# Meziantou.Framework.Unicode

This package provides Unicode text algorithms and data that do not depend on the Unicode version of the runtime or on
ICU: character properties, case mappings, grapheme, word, sentence and line segmentation, normalization, the
bidirectional algorithm, script runs, internationalized domain names (IDNA), and confusable detection. The data is the
one of Unicode 18.0.0.
The types are in the `Meziantou.Framework` namespace.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_ReplaceConfusablesCharacters;format=md-fenced-code-block;dedent=true -->
```csharp
var input = "\u0440\u0430\u0443\u0440\u0430l"; // "раураl": Cyrillic letters that look like Latin ones
var normalized = Unicode.ReplaceConfusablesCharacters(input);

Assert.Equal("paypal", normalized);
```
<!-- endref -->

## Detecting confusable strings

To decide whether two strings look alike, use the [UTS #39](https://unicode.org/reports/tr39/)
skeleton algorithm rather than comparing the output of `ReplaceConfusablesCharacters`:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_AreConfusable;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(Unicode.AreConfusable("paypal", "\u0440\u0430\u0443\u0440\u0430l")); // Cyrillic look-alikes
Assert.True(Unicode.AreConfusable("caf\u00E9", "cafe\u0301")); // composed vs decomposed
Assert.False(Unicode.AreConfusable("paypal", "example"));
```
<!-- endref -->

`Unicode.GetConfusableSkeleton(string)` exposes the key itself, so it can be stored or indexed
to find collisions across a set of names:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_GetConfusableSkeleton;format=md-fenced-code-block;dedent=true -->
```csharp
var userName = "\u0440\u0430\u0443\u0440\u0430l";
var skeleton = Unicode.GetConfusableSkeleton(userName);

Assert.Equal(Unicode.GetConfusableSkeleton("paypal"), skeleton);
```
<!-- endref -->

> The skeleton is a comparison key, not displayable text. Do not show it to users or store it
> in place of the original string.

> These two methods use the normalization of this package (see [Normalization](#normalization)), not the one of the
> platform: the skeleton does not depend on the version of ICU and is available in globalization-invariant mode
> (`InvariantGlobalization=true`).

## Detecting mixed-script text

A homograph attack usually shows up as a string that mixes writing systems. `IsMixedScript`
implements the [UTS #39](https://unicode.org/reports/tr39/) resolved-script-set rules:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_IsMixedScript;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.False(Unicode.IsMixedScript("paypal")); // all Latin
Assert.True(Unicode.IsMixedScript("\u0440\u0430\u0443\u0440\u0430l")); // Cyrillic letters with a Latin "l"
Assert.False(Unicode.IsMixedScript("日本語です")); // Japanese legitimately mixes Han and Hiragana
Assert.False(Unicode.IsMixedScript("user123")); // digits match any script
```
<!-- endref -->

Script data is also exposed directly:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Scripts;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(UnicodeScript.Latin, UnicodeScripts.GetScript(new Rune('A')));
Assert.Equal(UnicodeScript.Cyrillic, UnicodeScripts.GetScript(new Rune(0x0410)));
Assert.Equal("Latn", UnicodeScripts.GetIso15924Code(UnicodeScript.Latin));

// Script_Extensions: the scripts a shared character is used with
Span<UnicodeScript> scripts = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
var count = UnicodeScripts.GetScriptExtensions(0x30FC, scripts);
Assert.Equal<UnicodeScript>([UnicodeScript.Hiragana, UnicodeScript.Katakana], scripts[..count]);
Assert.True(UnicodeScripts.HasScriptExtension(0x30FC, UnicodeScript.Katakana));
```
<!-- endref -->

## Character properties

`UnicodeProperties` gives the properties of a code point that text processing needs: segmentation, bidirectional
ordering, shaping, identifiers and case. Every method takes an `int` and never allocates: the value is read from a
table indexed by the code point.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Properties;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(UnicodeCategory.UppercaseLetter, UnicodeProperties.GetGeneralCategory('A'));
Assert.Equal(230, UnicodeProperties.GetCanonicalCombiningClass(0x0301));
Assert.Equal(UnicodeBidirectionalCategory.RightToLeft, UnicodeProperties.GetBidiClass(0x05D0));
Assert.True(UnicodeProperties.TryGetBidiMirroringGlyph('(', out var mirrored));
Assert.Equal(')', mirrored);
Assert.Equal(UnicodeLineBreakClass.Glue, UnicodeProperties.GetLineBreakClass(0x00A0));
Assert.Equal(UnicodeGraphemeClusterBreak.ZWJ, UnicodeProperties.GetGraphemeClusterBreak(0x200D));
Assert.Equal(UnicodeWordBreak.ExtendNumLet, UnicodeProperties.GetWordBreak('_'));
Assert.Equal(UnicodeEastAsianWidth.Wide, UnicodeProperties.GetEastAsianWidth(0x4E00));
Assert.Equal(UnicodeVerticalOrientation.TransformedRotated, UnicodeProperties.GetVerticalOrientation(0x300C));
Assert.Equal(UnicodeJoiningType.DualJoining, UnicodeProperties.GetJoiningType(0x0628));
Assert.Equal(UnicodeIndicSyllabicCategory.Virama, UnicodeProperties.GetIndicSyllabicCategory(0x094D));
Assert.True(UnicodeProperties.IsDefaultIgnorable(0x00AD));
Assert.True(UnicodeProperties.IsIdStart('a')); // UAX #31
Assert.True(UnicodeProperties.IsIdContinue('0'));
Assert.Equal('a', UnicodeProperties.ToSimpleLowercase('A'));
Assert.Equal(0x01C5, UnicodeProperties.ToSimpleTitlecase(0x01C6)); // 'ǆ' to 'ǅ'
```
<!-- endref -->

The full case mappings can map a code point to several ones. They are the unconditional mappings of
`SpecialCasing.txt`, else the simple mappings: the mappings that depend on the language (Turkish, Lithuanian) or on
the context (the final sigma) are not applied.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_CaseMappings;format=md-fenced-code-block;dedent=true -->
```csharp
Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];

var length = UnicodeProperties.GetUppercaseMapping('ß', mapping);
Assert.Equal<int>(['S', 'S'], mapping[..length]);

length = UnicodeProperties.GetTitlecaseMapping(0xFB01, mapping); // the "ﬁ" ligature
Assert.Equal<int>(['F', 'i'], mapping[..length]);

length = UnicodeProperties.GetLowercaseMapping(0x0130, mapping); // 'İ'
Assert.Equal<int>(['i', 0x0307], mapping[..length]);
```
<!-- endref -->

- The values are those of one version of the Unicode Standard, `UnicodeProperties.UnicodeVersion` (18.0.0), whatever
  the version the runtime implements. The results are the same on every platform and every framework version, and
  do not need ICU.
- An unassigned code point has the default value the standard gives to its range, for instance `RightToLeft` in the
  Hebrew block. An integer that is not a code point has the default value of the property.
- The numeric values of the enumerations (`UnicodeScript`, `UnicodeLineBreakClass`, ...) never change: a later
  version of the standard only appends values.

## Text segmentation

The boundaries are those of [UAX #29](https://www.unicode.org/reports/tr29/) and
[UAX #14](https://www.unicode.org/reports/tr14/) for Unicode 18.0.0. The enumerators are `ref struct`s over a
`ReadOnlySpan<char>` and do not allocate.

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

`EnumerateSentenceBoundaries` applies the default rules, without a list of abbreviations: `Mr. Smith` is two
sentences, while `e.g. the` (a full stop before a lowercase word), `U.S.A.` and `3.14` are not broken.

`EnumerateLineBreaks` takes two optional tailorings, which match the CSS properties `line-break` and `word-break`:

- `UnicodeLineBreakStrictness`: `Strict` (the default rules), `Normal` (a line can start with a small kana),
  `Anywhere` (a line can break between any two grapheme clusters).
- `UnicodeWordBreakBehavior`: `Normal`, `BreakAll` (a line can break between any two letters), `KeepAll` (no break
  between letters or ideographs).

Without a dictionary, Thai, Lao, Khmer and the other scripts written without spaces only break at spaces and at
zero width spaces: see [Line breaking with a dictionary](#line-breaking-with-a-dictionary). A soft hyphen (U+00AD) is
a break opportunity like any other; the caller recognizes it by the character before the break.

`Unicode.EnumerateScriptRuns` splits a text into runs of one script, for shaping and font selection
([UAX #24](https://www.unicode.org/reports/tr24/)): combining marks follow their base, spaces and punctuation follow
the text before them, `Script_Extensions` are honored, and both brackets of a pair are in the same run script.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_ScriptRuns;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "gamma (\u03B3) is";
var runs = new List<string>();
foreach (UnicodeScriptRun run in Unicode.EnumerateScriptRuns(text))
{
    runs.Add($"{text.Substring(run.Index, run.Length)}: {run.Script}");
}

Assert.Equal(["gamma (: Latin", "\u03B3: Greek", ") is: Latin"], runs);
```
<!-- endref -->

## Line breaking with a dictionary

Thai, Lao, Khmer, Myanmar and a few other scripts are written without spaces between words. Their characters have the
line breaking class `Complex_Context` (SA), and [UAX #14](https://www.unicode.org/reports/tr14/) leaves them to a
dictionary: without one, a sentence has no break opportunity. `LineBreakDictionary` segments such text into the words
of a list that you provide. No word list is included in this package: [ICU](https://github.com/unicode-org/icu)
publishes word lists for Thai, Lao, Khmer and Burmese (`thaidict.txt`, `laodict.txt`, `khmerdict.txt`,
`burmesedict.txt` in `icu4c/source/data/brkitr/dictionaries`) under the Unicode License; its `LICENSE` file has
additional notices for some of them.

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

- The word list is plain text, in UTF-8 when it is read from a file or a stream: one word per line, `#` starts a
  comment, blank lines are ignored, and so is what follows the first tab of a line (a frequency, for instance). A
  word is kept as written, without normalization or case mapping: pass the text in the normalization form of the
  list. A list that exceeds a limit of `LineBreakDictionaryOptions` (word count, word length, input length) is
  rejected with a `FormatException`, so that a list from an untrusted source cannot exhaust memory.
- A dictionary is immutable and thread-safe. `FindBreaks` and the enumeration do not allocate, and their cost is
  proportional to the length of the text.

`FindBreaks` segments the text it is given, and `Unicode.EnumerateLineBreaks` each run of the text, as follows:

- A run is a maximal sequence of consecutive `Complex_Context` characters that starts with a letter: the combining
  marks before the first letter belong to the character before them. Spaces, digits, punctuation, zero width spaces
  and the letters of the other scripts are not in a run.
- A word of the dictionary is only found between two boundaries of the run: its start, its end, and the grapheme
  cluster boundaries that are not before a combining mark (rule LB9 of UAX #14). A line never breaks inside a
  grapheme cluster, nor before a mark.
- The words that are found must not overlap. Among the ways to choose them, the one that leaves the fewest UTF-16
  code units uncovered is taken, then the one with the fewest words. When several remain, at the first position where
  two of them differ the one with the longer word at that position is taken, a word being preferred to an uncovered
  character: `ตากลม` is `ตาก` + `ลม`, not `ตา` + `กลม`, when the four words are in the list.
- A line can break before each word that is found, except at the start of the run. The characters that are in no word
  give no break: they stay with what precedes them, as the repetition mark U+0E46 or an unknown suffix do, and a run
  without any word of the list has no break.
- The breaks at the start and at the end of a run are those of UAX #14, where a `Complex_Context` character is a letter
  or a combining mark. A break of the dictionary is not mandatory.
- `UnicodeWordBreakBehavior.KeepAll` keeps the breaks of the dictionary, as `word-break: keep-all` does in CSS: they
  are between words. With `BreakAll`, or `UnicodeLineBreakStrictness.Anywhere`, a line already breaks between any two
  letters or grapheme clusters, and the dictionary is not used.
- A run longer than 1,024 UTF-16 code units is segmented piece by piece, so that the memory is bounded: the rule is
  applied to the longest part of its first 1,024 code units that ends at a boundary, the words and the uncovered
  characters that start in the first 512 code units are kept, and the rule is applied again to what follows them. A
  grapheme cluster longer than 1,024 code units is in no word.

## Hyphenation

The Unicode Standard says where a line can break, including after a soft hyphen (U+00AD), but hyphenating a word
needs data for its language. `Hyphenator` applies the pattern algorithm of TeX (Franklin Liang) to patterns that you
provide. No pattern is included in this package: the [hyph-utf8](https://ctan.org/pkg/hyph-utf8) project publishes
patterns for many languages, each under its own license.

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

- `Hyphenate` takes one word, in any case, and returns the indexes before which a hyphen can be inserted. It does
  not allocate. Use `Unicode.EnumerateWordBoundaries` to find the words of a text.
- A break is never inside a grapheme cluster, and leaves at least `LeftHyphenMin` characters before it and
  `RightHyphenMin` after it (2 and 3 by default; the right values depend on the language).
- The patterns match code points: pass the word in the normalization form of the patterns, usually NFC.
- Only the plain text form is read. A file that uses TeX macros, `\lccode` or the `^^` notation is rejected with a
  `FormatException`, as is a file that exceeds a limit of `HyphenatorOptions` (pattern count, pattern length, input
  length), so that a pattern file from an untrusted source cannot exhaust memory.
- Hyphenation that changes the spelling of a word is not supported.

## Normalization

`UnicodeNormalization` implements the four normalization forms of [UAX #15](https://www.unicode.org/reports/tr15/)
without the globalization library of the platform: the results are the same on every platform and in
globalization-invariant mode, where `string.Normalize` only handles ASCII.

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

`Normalize` returns the same instance when the text is already normalized.

## Bidirectional text

`UnicodeBidi` implements the Unicode Bidirectional Algorithm ([UAX #9](https://www.unicode.org/reports/tr9/)), in
the two steps the annex defines: levels per paragraph, then display order per line.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Bidi;format=md-fenced-code-block;dedent=true -->
```csharp
var paragraph = "car \u05D0\u05D1\u05D2!";
Span<byte> levels = stackalloc byte[paragraph.Length];
var paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, UnicodeBidiDirection.Auto, levels);

// For each line, once the paragraph is broken into lines:
UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, levels);
Span<int> visualToLogical = stackalloc int[paragraph.Length];
UnicodeBidi.GetVisualOrder(levels, visualToLogical);

Assert.Equal<int>([0, 1, 2, 3, 6, 5, 4, 7], visualToLogical);
```
<!-- endref -->

- A character whose level is odd is displayed right to left.
- `UnicodeBidi.MayRequireBidi` is a fast check: when it returns `false`, every level is 0 and the display order is
  the order of the text.
- `ResolveLevels` has an overload that takes its workspace from the caller (`GetWorkspaceLength`) and does not
  allocate.
- Mirroring (rule L4) is `UnicodeProperties.TryGetBidiMirroringGlyph`.

## Internationalized domain names

`UnicodeIdna` implements Unicode IDNA Compatibility Processing ([UTS #46](https://www.unicode.org/reports/tr46/)) and
Punycode ([RFC 3492](https://www.rfc-editor.org/rfc/rfc3492)). Unlike `System.Globalization.IdnMapping`, it does not
depend on ICU, so it gives the same results on every platform and in globalization-invariant mode.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Idna;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(UnicodeIdna.TryToAscii("B\u00FCcher.example", out var ascii));
Assert.Equal("xn--bcher-kva.example", ascii);

Assert.Equal("b\u00FCcher.example", UnicodeIdna.ToUnicode("xn--bcher-kva.example", out var hasErrors));
Assert.False(hasErrors);

// The flags of UTS #46: the defaults enable every check, with nontransitional processing
var options = new UnicodeIdnaOptions { UseStd3AsciiRules = false, VerifyDnsLength = false };
Assert.True(UnicodeIdna.TryToAscii("exa_mple.com", options, out ascii));
Assert.Equal("exa_mple.com", ascii);

Assert.True(UnicodeIdna.TryEncodePunycode("b\u00FCcher", out var encoded));
Assert.Equal("bcher-kva", encoded);
Assert.True(UnicodeIdna.TryDecodePunycode("bcher-kva", out var decoded));
Assert.Equal("b\u00FCcher", decoded);
```
<!-- endref -->

## Character information

`Unicode.GetCharacterInfo` gives the full record of a character, including its name. It is meant for inspection
tools: prefer `UnicodeProperties` to read a property of many code points.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_GetCharacterInfo;format=md-fenced-code-block;dedent=true -->
```csharp
var info = Unicode.GetCharacterInfo(new Rune('A'));

Assert.NotNull(info);
Assert.Equal("LATIN CAPITAL LETTER A", info.Value.Name);
Assert.Equal(UnicodeCategory.UppercaseLetter, info.Value.Category);
Assert.Equal(UnicodeBidirectionalCategory.LeftToRight, info.Value.BidiCategory);
Assert.Equal("Basic Latin", info.Value.Block.Name);
```
<!-- endref -->
