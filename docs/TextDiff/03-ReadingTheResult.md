---
title: Reading the result
description: Read the entries of a diff, check whether two texts differ, and rebuild the old or the new text.
---

`TextDiff.ComputeDiff` returns a `TextDiffResult`:

- `HasDifferences` is `true` when the texts differ under the comparison options that were used. Two texts that differ
  only in casing have no differences with `IgnoreCase`.
- `Entries` holds the chunks of both texts, in order. Each `TextDiffEntry` has an `Operation` (`Equal`, `Delete` or
  `Insert`) and the text of the chunk.
- `ToString()` counts the entries of each operation, which is handy in logs.

## The order of the entries

The entries follow both texts from start to end. Between two `Equal` entries, the deleted chunks come first, then the
inserted chunks: a changed line is a `Delete` entry followed by an `Insert` entry, never the other way around.

## The text of an entry

An entry has three text properties:

| Property | `Equal` | `Delete` | `Insert` |
| --- | --- | --- | --- |
| `OldText` | The chunk in the old text | The chunk in the old text | `null` |
| `NewText` | The chunk in the new text | `null` | The chunk in the new text |
| `Text` | The chunk in the old text | The chunk in the old text | The chunk in the new text |

`Text` is never `null`, which makes it the simplest property to display. Without comparison options, the two sides of
an `Equal` entry are the same string. With `IgnoreCase` or `IgnoreWhitespace`, two different chunks can compare equal:
`OldText` and `NewText` then differ, and `Text` is the chunk of the old text.

## Rebuild either text

Concatenate `OldText` for the `Equal` and `Delete` entries to get the old text, and `NewText` for the `Equal` and
`Insert` entries to get the new text. This works whatever the comparison options:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_RebuildSides;format=md-fenced-code-block;dedent=true -->
```csharp
var result = TextDiff.ComputeDiff("Hello\nWorld\n", "hello\nWORLD\nagain\n", new TextDiffOptions { IgnoreCase = true });

var oldText = string.Concat(result.Entries
    .Where(e => e.Operation is TextDiffOperation.Equal or TextDiffOperation.Delete)
    .Select(e => e.OldText));

var newText = string.Concat(result.Entries
    .Where(e => e.Operation is TextDiffOperation.Equal or TextDiffOperation.Insert)
    .Select(e => e.NewText));

Assert.Equal("Hello\nWorld\n", oldText);
Assert.Equal("hello\nWORLD\nagain\n", newText);
```
<!-- endref -->

The texts are rebuilt exactly because the built-in chunkers keep every character, line terminators included. A
[custom chunker](04-Chunking.md#write-your-own-chunker) must do the same. `IgnoreEndOfLine` is the exception: it
normalizes the line terminators before the texts are split, so the entries hold `\n` instead of the original
terminators. See [Comparison options](05-ComparisonOptions.md#line-terminators).

## Compare entries

`TextDiffEntry` is a value type with value equality: two entries are equal when they have the same operation, the same
`OldText` and the same `NewText`. This is what lets the examples of this documentation compare `Entries` with a list of
expected entries, created with the `TextDiffEntry(operation, text)` constructor.
