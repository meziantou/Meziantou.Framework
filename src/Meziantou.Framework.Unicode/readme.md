# Meziantou.Framework.Unicode

This package provides Unicode text algorithms and data that do not depend on the Unicode version of the runtime or on
ICU: character properties, grapheme, word and line segmentation, normalization, the bidirectional algorithm, script
runs, and confusable detection. The data is the one of Unicode 18.0.0.

```csharp
using Meziantou.Framework;

var input = "раураl"; // Uses Cyrillic letters that look like Latin
var normalized = Unicode.ReplaceConfusablesCharacters(input);

Console.WriteLine(normalized); // "paypal"
```

## Detecting confusable strings

To decide whether two strings look alike, use the [UTS #39](https://unicode.org/reports/tr39/)
skeleton algorithm rather than comparing the output of `ReplaceConfusablesCharacters`:

```csharp
Unicode.AreConfusable("paypal", "раураl"); // true  (Cyrillic look-alikes)
Unicode.AreConfusable("café", "café");     // true  (composed vs decomposed)
Unicode.AreConfusable("paypal", "example");     // false
```

`Unicode.GetConfusableSkeleton(string)` exposes the key itself, so it can be stored or indexed
to find collisions across a set of names:

```csharp
var skeleton = Unicode.GetConfusableSkeleton(userName);
```

> The skeleton is a comparison key, not displayable text. Do not show it to users or store it
> in place of the original string.

> These two methods use the normalization of this package (see [Normalization](#normalization)), not the one of the
> platform: the skeleton does not depend on the version of ICU and is available in globalization-invariant mode
> (`InvariantGlobalization=true`).

## Detecting mixed-script text

A homograph attack usually shows up as a string that mixes writing systems. `IsMixedScript`
implements the [UTS #39](https://unicode.org/reports/tr39/) resolved-script-set rules:

```csharp
Unicode.IsMixedScript("paypal");   // false - all Latin
Unicode.IsMixedScript("раураl");   // true  - Cyrillic letters with a Latin "l"
Unicode.IsMixedScript("日本語です"); // false - Japanese legitimately mixes Han and Hiragana
Unicode.IsMixedScript("user123");  // false - digits match any script
```

Script data is also exposed directly:

```csharp
UnicodeScripts.GetScript(new Rune('A'));      // UnicodeScript.Latin
UnicodeScripts.GetScript(new Rune(0x0410));   // UnicodeScript.Cyrillic
UnicodeScripts.GetIso15924Code(UnicodeScript.Latin); // "Latn"

// Script_Extensions: the scripts a shared character is used with
Span<UnicodeScript> scripts = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
var count = UnicodeScripts.GetScriptExtensions(0x30FC, scripts); // Hiragana, Katakana
UnicodeScripts.HasScriptExtension(0x30FC, UnicodeScript.Katakana); // true
```

## Character properties

`UnicodeProperties` gives the properties of a code point that text processing needs: segmentation, bidirectional
ordering, shaping and case. Every method takes an `int`, never throws and never allocates: the value is read from a
table indexed by the code point.

```csharp
UnicodeProperties.GetGeneralCategory('A');           // UppercaseLetter
UnicodeProperties.GetCanonicalCombiningClass(0x0301); // 230
UnicodeProperties.GetBidiClass(0x05D0);              // RightToLeft
UnicodeProperties.TryGetBidiMirroringGlyph('(', out var mirrored); // true, ')'
UnicodeProperties.GetLineBreakClass(0x00A0);         // Glue
UnicodeProperties.GetGraphemeClusterBreak(0x200D);   // ZWJ
UnicodeProperties.GetWordBreak('_');                 // ExtendNumLet
UnicodeProperties.GetEastAsianWidth(0x4E00);         // Wide
UnicodeProperties.GetVerticalOrientation(0x300C);    // TransformedRotated
UnicodeProperties.GetJoiningType(0x0628);            // DualJoining
UnicodeProperties.GetIndicSyllabicCategory(0x094D);  // Virama
UnicodeProperties.IsDefaultIgnorable(0x00AD);        // true
UnicodeProperties.ToSimpleLowercase('A');            // 'a'
```

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

```csharp
// Extended grapheme clusters: what a reader perceives as one character
foreach (Range cluster in Unicode.EnumerateGraphemeClusters("e\u0301🇫🇷"))
{
    // "e\u0301", then the flag
}

// Word boundaries: words, numbers, runs of spaces, punctuation marks
foreach (Range segment in Unicode.EnumerateWordBoundaries("Hello, world!"))
{
    // "Hello", ",", " ", "world", "!"
}

// Line break opportunities
foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks("A long line\nof text"))
{
    // lineBreak.Index: the index of the first character of the next line
    // lineBreak.IsMandatory: after a line feed, and at the end of the text
}
```

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

```csharp
foreach (UnicodeScriptRun run in Unicode.EnumerateScriptRuns("gamma (γ) is"))
{
    // "gamma (" Latin, "γ" Greek, ") is" Latin
}
```

## Line breaking with a dictionary

Thai, Lao, Khmer, Myanmar and a few other scripts are written without spaces between words. Their characters have the
line breaking class `Complex_Context` (SA), and [UAX #14](https://www.unicode.org/reports/tr14/) leaves them to a
dictionary: without one, a sentence has no break opportunity. `LineBreakDictionary` segments such text into the words
of a list that you provide. No word list is included in this package: [ICU](https://github.com/unicode-org/icu)
publishes word lists for Thai, Lao, Khmer and Burmese (`thaidict.txt`, `laodict.txt`, `khmerdict.txt`,
`burmesedict.txt` in `icu4c/source/data/brkitr/dictionaries`) under the Unicode License; its `LICENSE` file has
additional notices for some of them.

```csharp
// The word list, as plain text: one word per line
var dictionary = LineBreakDictionary.Load("thaidict.txt");

// The line break opportunities of a text: those of UAX #14, and those before the words of the dictionary
foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks("Hello ภาษาไทย!", dictionary))
{
    // 6 (after the space), 10 (before "ไทย"), 14 (the end of the text)
}

// The words of a run of text of the script of the dictionary
Span<int> positions = stackalloc int[16];
var count = dictionary.FindBreaks("ภาษาไทย", positions); // 1 position: 4
```

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

```csharp
// The patterns, as plain text: "hy3ph he2n ..." or a \patterns{...} group; exceptions such as "as-so-ciate"
var hyphenator = Hyphenator.Load("hyph-en-us.pat.txt", "hyph-en-us.hyp.txt");

Span<int> positions = stackalloc int[32];
var count = hyphenator.Hyphenate("hyphenation", positions); // 2 positions: 2 and 6 (hy-phen-ation)
```

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

```csharp
UnicodeNormalization.Normalize("cafe\u0301");                            // "café" (NFC)
UnicodeNormalization.Normalize("café", NormalizationForm.FormD);         // "cafe\u0301"
UnicodeNormalization.IsNormalized("café");                               // true
UnicodeNormalization.TryNormalize(source, destination, out var written); // span-based
UnicodeNormalization.TryComposeCanonical('e', 0x0301, out var composite); // true, 'é'
```

`Normalize` returns the same instance when the text is already normalized.

## Bidirectional text

`UnicodeBidi` implements the Unicode Bidirectional Algorithm ([UAX #9](https://www.unicode.org/reports/tr9/)), in
the two steps the annex defines: levels per paragraph, then display order per line.

```csharp
var paragraph = "car \u05D0\u05D1\u05D2!";
Span<byte> levels = stackalloc byte[paragraph.Length];
var paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, UnicodeBidiDirection.Auto, levels);

// For each line, once the paragraph is broken into lines:
UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, levels);
Span<int> visualToLogical = stackalloc int[paragraph.Length];
UnicodeBidi.GetVisualOrder(levels, visualToLogical); // 0 1 2 3 6 5 4 7
```

- A character whose level is odd is displayed right to left.
- `UnicodeBidi.MayRequireBidi` is a fast check: when it returns `false`, every level is 0 and the display order is
  the order of the text.
- `ResolveLevels` has an overload that takes its workspace from the caller (`GetWorkspaceLength`) and does not
  allocate.
- Mirroring (rule L4) is `UnicodeProperties.TryGetBidiMirroringGlyph`.

## Character information

`Unicode.GetCharacterInfo` gives the full record of a character, including its name. It is meant for inspection
tools: prefer `UnicodeProperties` to read a property of many code points.

```csharp
var info = Unicode.GetCharacterInfo(new Rune('A'));
if (info is not null)
{
	Console.WriteLine(info.Value.Name); // "LATIN CAPITAL LETTER A"
	Console.WriteLine(info.Value.Category); // UppercaseLetter
	Console.WriteLine(info.Value.BidiCategory); // LeftToRight
	Console.WriteLine(info.Value.Block); // BasicLatin
}
```
