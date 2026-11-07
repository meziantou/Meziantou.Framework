---
title: Comparison options
description: Ignore the casing, the whitespace around each chunk, or the differences between line terminators.
---

By default, two chunks are equal when they are the same string. Three options of [TextDiffOptions](cref:Meziantou.Framework.TextDiffOptions) relax the
comparison:

| Option | Two chunks are equal when |
| --- | --- |
| [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase) | They differ only in casing. |
| [IgnoreWhitespace](cref:Meziantou.Framework.TextDiffOptions.IgnoreWhitespace) | They differ only in the whitespace at their start or their end. |
| [IgnoreEndOfLine](cref:Meziantou.Framework.TextDiffOptions.IgnoreEndOfLine) | They differ only in their line terminators (`\r\n`, `\r` or `\n`). |

The options combine, and they apply to [hierarchical diffs](06-HierarchicalDiffs.md) too. They change what compares
equal, not the text of the entries: an [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) entry keeps the chunk of each text in [OldText](cref:Meziantou.Framework.TextDiffEntry.OldText) and [NewText](cref:Meziantou.Framework.TextDiffEntry.NewText), so you can
still show or [rebuild](03-ReadingTheResult.md#rebuild-either-text) the exact texts.

## Casing

[IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase) compares chunks with an ordinal, case-insensitive comparison: the result is the same whatever the current
culture.

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
