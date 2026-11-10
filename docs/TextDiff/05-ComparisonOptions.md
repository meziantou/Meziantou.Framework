---
title: Comparison options
description: Ignore the casing, the accents, the whitespace around each chunk, or the differences between line terminators.
---

By default, two chunks are equal when they are the same string. These options of [TextDiffOptions](cref:Meziantou.Framework.TextDiffOptions) relax the
comparison:

| Option | Two chunks are equal when |
| --- | --- |
| [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase) | They differ only in casing. |
| [IgnoreWhitespace](cref:Meziantou.Framework.TextDiffOptions.IgnoreWhitespace) | They differ only in the whitespace at their start or their end. |
| [IgnoreEndOfLine](cref:Meziantou.Framework.TextDiffOptions.IgnoreEndOfLine) | They differ only in their line terminators (`\r\n`, `\r` or `\n`). |
| [CompareOptions](cref:Meziantou.Framework.TextDiffOptions.CompareOptions) and [Culture](cref:Meziantou.Framework.TextDiffOptions.Culture) | They are equal under the rules of a culture, such as without their accents. |

The options combine, and they apply to [hierarchical diffs](06-HierarchicalDiffs.md) too. They change what compares
equal, not the text of the entries: an [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) entry keeps the chunk of each text in [OldText](cref:Meziantou.Framework.TextDiffEntry.OldText) and [NewText](cref:Meziantou.Framework.TextDiffEntry.NewText), so you can
still show or [rebuild](03-ReadingTheResult.md#rebuild-either-text) the exact texts.

## Casing

[IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase) compares chunks with an ordinal, case-insensitive comparison: the result is the same whatever the current
culture. To use the casing rules of a culture, set [Culture](cref:Meziantou.Framework.TextDiffOptions.Culture) too; see [Culture-aware comparison](#culture-aware-comparison).

## Whitespace

[IgnoreWhitespace](cref:Meziantou.Framework.TextDiffOptions.IgnoreWhitespace) trims each chunk before comparing it. Only the edges are trimmed: the whitespace inside a chunk still
counts.

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_IgnoreWhitespace;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { IgnoreWhitespace = true };

// The whitespace at the edges of a line is ignored...
Assert.False(TextDiff.ComputeDiff("    indented\n", "indented  \n", options).HasDifferences);

// ...but not the whitespace inside it
Assert.True(TextDiff.ComputeDiff("a  b\n", "a b\n", options).HasDifferences);
```
<!-- endref -->

To ignore the whitespace inside the lines too, compare words. [TextChunker.Words](cref:Meziantou.Framework.TextChunker.Words) puts each run of whitespace in its own
chunk, which trimming reduces to an empty chunk, so `Hello   world` and `hello world` compare equal:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_ChunkingAndComparison;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions
{
    Chunker = TextChunker.Words,        // Lines (default), Words, Characters, or custom chunker
    IgnoreCase = true,
    IgnoreWhitespace = true,
    IgnoreEndOfLine = true,
};

var result = TextDiff.ComputeDiff("Hello   world\r\n", "hello world\n", options);
Assert.False(result.HasDifferences);
```
<!-- endref -->

## Line terminators

[IgnoreEndOfLine](cref:Meziantou.Framework.TextDiffOptions.IgnoreEndOfLine) replaces every line terminator with `\n` before the texts are split into chunks. A file saved with
Windows line terminators then compares equal to the same file saved with Unix ones:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_IgnoreEndOfLine;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { IgnoreEndOfLine = true };

var result = TextDiff.ComputeDiff("one\r\ntwo\r\n", "one\ntwo\n", options);

Assert.False(result.HasDifferences);

// The entries hold the normalized text, not the original "\r\n"
Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "one\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "two\n"),
        new TextDiffEntry(TextDiffOperation.Equal, ""),
    ],
    result.Entries);
```
<!-- endref -->

Since the texts are normalized before they are split, the entries hold `\n` rather than the original terminators. Unlike
the other options, the entries no longer rebuild the original texts exactly, only their normalized versions.

## Culture-aware comparison

[CompareOptions](cref:Meziantou.Framework.TextDiffOptions.CompareOptions) compares the chunks with the `CompareInfo` of a culture, with the options you choose. `IgnoreNonSpace`
ignores the accents:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_CompareOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { CompareOptions = CompareOptions.IgnoreNonSpace };

// The accents are ignored
Assert.False(TextDiff.ComputeDiff("Caf\u00E9 cr\u00E8me\n", "Cafe creme\n", options).HasDifferences);
```
<!-- endref -->

The culture is [Culture](cref:Meziantou.Framework.TextDiffOptions.Culture), or the invariant culture when it is `null`. Set [Culture](cref:Meziantou.Framework.TextDiffOptions.Culture) for the rules of a language, such
as the Turkish casing rules:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_Culture;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { Culture = CultureInfo.GetCultureInfo("tr-TR"), IgnoreCase = true };

// In Turkish, the lowercase of "I" is the dotless "\u0131"
Assert.False(TextDiff.ComputeDiff("TITLE\n", "t\u0131tle\n", options).HasDifferences);

// The ordinal comparison of IgnoreCase alone does not know it
Assert.True(TextDiff.ComputeDiff("TITLE\n", "t\u0131tle\n", new TextDiffOptions { IgnoreCase = true }).HasDifferences);
```
<!-- endref -->

- [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase) adds `CompareOptions.IgnoreCase` to a culture-aware comparison, and [IgnoreWhitespace](cref:Meziantou.Framework.TextDiffOptions.IgnoreWhitespace) still trims the
  chunks first.
- A culture-aware comparison treats canonically equivalent strings as equal: `\u00E9` (`é`) and `e\u0301` (`e`
  followed by a combining accent) are the same chunk, even with `CompareOptions.None`, as soon as
  [Culture](cref:Meziantou.Framework.TextDiffOptions.Culture) is set.
- `IgnoreCase`, `IgnoreNonSpace`, `IgnoreSymbols`, `IgnoreKanaType` and `IgnoreWidth` are supported, in any combination.
  `Ordinal`, `OrdinalIgnoreCase` and `StringSort` throw an `ArgumentOutOfRangeException`: leave both properties unset
  for an ordinal comparison.
- The comparison uses ICU. In [invariant globalization mode](https://learn.microsoft.com/dotnet/core/runtime-config/globalization),
  it becomes ordinal, and named cultures such as `tr-TR` cannot be created.
