---
title: Reading the result
description: Read the entries of a diff, check whether two texts differ, and rebuild the old or the new text.
---

[TextDiff.ComputeDiff](cref:Meziantou.Framework.TextDiff.ComputeDiff) returns a [TextDiffResult](cref:Meziantou.Framework.TextDiffResult):

- [HasDifferences](cref:Meziantou.Framework.TextDiffResult.HasDifferences) is `true` when the texts differ under the comparison options that were used. Two texts that differ
  only in casing have no differences with [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase).
- [Entries](cref:Meziantou.Framework.TextDiffResult.Entries) holds the chunks of both texts, in order. Each [TextDiffEntry](cref:Meziantou.Framework.TextDiffEntry) has an [Operation](cref:Meziantou.Framework.TextDiffEntry.Operation) ([Equal](cref:Meziantou.Framework.TextDiffOperation.Equal), [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) or
  [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert)) and the text of the chunk.
- `ToString()` counts the entries of each operation, which is handy in logs.

## The order of the entries

The entries follow both texts from start to end. Between two [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) entries, the deleted chunks come first, then the
inserted chunks: a changed line is a [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) entry followed by an [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert) entry, never the other way around.

## The text of an entry

An entry has three text properties:

| Property | [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) | [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) | [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert) |
| --- | --- | --- | --- |
| [OldText](cref:Meziantou.Framework.TextDiffEntry.OldText) | The chunk in the old text | The chunk in the old text | `null` |
| [NewText](cref:Meziantou.Framework.TextDiffEntry.NewText) | The chunk in the new text | `null` | The chunk in the new text |
| [Text](cref:Meziantou.Framework.TextDiffEntry.Text) | The chunk in the old text | The chunk in the old text | The chunk in the new text |

[Text](cref:Meziantou.Framework.TextDiffEntry.Text) is never `null`, which makes it the simplest property to display. Without comparison options, the two sides of
an [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) entry are the same string. With [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase) or [IgnoreWhitespace](cref:Meziantou.Framework.TextDiffOptions.IgnoreWhitespace), two different chunks can compare equal:
[OldText](cref:Meziantou.Framework.TextDiffEntry.OldText) and [NewText](cref:Meziantou.Framework.TextDiffEntry.NewText) then differ, and [Text](cref:Meziantou.Framework.TextDiffEntry.Text) is the chunk of the old text.

## Rebuild either text

Concatenate [OldText](cref:Meziantou.Framework.TextDiffEntry.OldText) for the [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) and [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) entries to get the old text, and [NewText](cref:Meziantou.Framework.TextDiffEntry.NewText) for the [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) and
[Insert](cref:Meziantou.Framework.TextDiffOperation.Insert) entries to get the new text. This works whatever the comparison options:

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
[custom chunker](04-Chunking.md#write-your-own-chunker) must do the same. [IgnoreEndOfLine](cref:Meziantou.Framework.TextDiffOptions.IgnoreEndOfLine) is the exception: it
normalizes the line terminators before the texts are split, so the entries hold `\n` instead of the original
terminators. See [Comparison options](05-ComparisonOptions.md#line-terminators).

## Compare entries

[TextDiffEntry](cref:Meziantou.Framework.TextDiffEntry) is a value type with value equality: two entries are equal when they have the same operation, the same
[OldText](cref:Meziantou.Framework.TextDiffEntry.OldText) and the same [NewText](cref:Meziantou.Framework.TextDiffEntry.NewText). This is what lets the examples of this documentation compare [Entries](cref:Meziantou.Framework.TextDiffResult.Entries) with a list of
expected entries, created with the `TextDiffEntry(operation, text)` constructor.
