---
title: Hierarchical diffs
description: Find the changed lines, then the changed words inside them, then the changed characters inside those words.
---

A line diff tells you which lines changed, not what changed in them. A hierarchical diff does both: it compares the
texts with a first chunker, then compares each changed chunk again with the next chunker, and so on. Pass the
chunkers to `TextDiff.ComputeHierarchyDiff`, the coarsest first:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_HierarchyDiff;format=md-fenced-code-block;dedent=true -->
```csharp
var result = TextDiff.ComputeHierarchyDiff(
    oldText: "line1\nhello world\nline3",
    newText: "line1\nhello brave world\nline3",
    chunkers: [TextChunker.Lines, TextChunker.Words, TextChunker.Characters]);

// Only the second line changed. The Words chunker refines it into words, without going further
// down to characters since every word is either equal or inserted.
Assert.HasCount(3, result.Entries);
var changedLine = result.Entries[1];
Assert.Equal(TextDiffHierarchyOperation.Replace, changedLine.Operation);
Assert.Equal("hello world\n", changedLine.OldText);
Assert.Equal("hello brave world\n", changedLine.NewText);

var insertedWords = changedLine.Children
    .Where(child => child.Operation is TextDiffHierarchyOperation.Insert)
    .Select(child => child.NewText)
    .ToArray();
Assert.Equal(["brave", " "], insertedWords);
```
<!-- endref -->

The result is a `TextDiffHierarchyResult`. Its `Entries` are the chunks of the first level (lines here), and its
`HasDifferences` tells whether the texts differ.

## The entries

A `TextDiffHierarchyEntry` has an `Operation`, an `OldText`, a `NewText` and `Children`. The operations
(`TextDiffHierarchyOperation`) are those of a [simple diff](03-ReadingTheResult.md), plus one:

| Operation | `OldText` | `NewText` | `Children` |
| --- | --- | --- | --- |
| `Equal` | The chunk in the old text | The chunk in the new text | Empty |
| `Delete` | The chunk in the old text | `null` | Empty |
| `Insert` | `null` | The chunk in the new text | Empty |
| `Replace` | The chunk in the old text | The chunk in the new text | The diff of the two chunks with the next chunker |

The children are entries too: a `Replace` child of a line holds the diff of two words with the next chunker, if there
is one. The last chunker never produces `Replace` entries, only `Equal`, `Delete` and `Insert` ones.

## How the changed chunks are paired

Between two `Equal` entries, the diff pairs the deleted and the inserted chunks in order: the first deleted line with the
first inserted line, the second with the second, and so on. Each pair becomes a `Replace` entry. When one side has
more chunks than the other, the extra chunks are `Delete` or `Insert` entries.

The pairing is positional: when a line is deleted and another one is inserted at the same place, they are compared word
by word even if they have nothing in common. Their children are then mostly deletions and insertions.

## Choose the levels

- `[TextChunker.Lines, TextChunker.Words]` shows the changed words of each changed line, like a code review tool.
- `[TextChunker.Lines, TextChunker.Words, TextChunker.Characters]` also shows the changed characters of each changed
  word, which highlights a typo inside a word.
- `[TextChunker.Words, TextChunker.Characters]` suits a text without lines, such as a title or a description.

The levels can include your own [chunkers](04-Chunking.md#write-your-own-chunker). At least two chunkers are required;
use `ComputeDiff` for one level. `ComputeHierarchyDiff` ignores `TextDiffOptions.Chunker`, but uses the algorithm and
the [comparison options](05-ComparisonOptions.md) for every level.

A hierarchical diff is also the efficient way to compare long texts character by character: the character diff only
runs on the chunks that changed, instead of on every character of the texts.

To show a hierarchical diff, see [Highlight the changed words](08-DisplayingADiff.md#highlight-the-changed-words).
