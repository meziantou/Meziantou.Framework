---
title: Detecting moves
description: Tag the blocks of text deleted at one place and inserted at another as moves.
---

A diff reports a block of lines moved elsewhere as a deletion and an insertion. Set
[DetectMoves](cref:Meziantou.Framework.TextDiffOptions.DetectMoves) to link the two: the entries of both places get the same [MoveId](cref:Meziantou.Framework.TextDiffEntry.MoveId):

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_DetectMoves;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { DetectMoves = true };

var result = TextDiff.ComputeDiff("a\nb\nc\nd\n", "a\nc\nd\nb\n", options);

// The deleted "b" and the inserted "b" share move 0
Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "a\n"),
        new TextDiffEntry(TextDiffOperation.Delete, "b\n", null, moveId: 0),
        new TextDiffEntry(TextDiffOperation.Equal, "c\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "d\n"),
        new TextDiffEntry(TextDiffOperation.Insert, null, "b\n", moveId: 0),
        new TextDiffEntry(TextDiffOperation.Equal, ""),
    ],
    result.Entries);
```
<!-- endref -->

- The operations do not change: a moved block is still [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) entries where it was and [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert) entries where it is
  now, so the entries [rebuild both texts](03-ReadingTheResult.md#rebuild-either-text) as usual. Code that ignores
  [MoveId](cref:Meziantou.Framework.TextDiffEntry.MoveId) keeps working.
- A block of several chunks shares one identifier. Identifiers start at 0 and follow the order of the deleted blocks.
- Use [MoveId](cref:Meziantou.Framework.TextDiffEntry.MoveId) to display moves differently, such as in another color, or to link the two places.

## What counts as a move

- The deleted and the inserted chunks must be equal under the [comparison options](05-ComparisonOptions.md): with
  [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase), a block moved and recased is a move. A block moved and edited is not.
- The deleted blocks are matched in order: for each one, the longest insertion that matches from its start is taken,
  and the first one wins a tie. A deletion that comes earlier can claim an insertion that a longer, later deletion
  would also match.
- A block that holds only whitespace, such as an empty line, is never a move: it would link unrelated places.
- A block deleted and inserted at the same place is a change, not a move. This happens when a
  [work limit](08-Algorithms.md#limiting-the-work) replaces a region as a whole.
- The chunker sets the granularity: with [TextChunker.Words](cref:Meziantou.Framework.TextChunker.Words), a moved word is a move.

Move detection runs after the diff, on its changed entries; [ComputeHierarchyDiff](cref:Meziantou.Framework.TextDiff.ComputeHierarchyDiff) does not detect moves.
