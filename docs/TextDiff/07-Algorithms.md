---
title: Algorithms
description: Choose between the Myers, Patience, Histogram and Hunt-Szymanski algorithms.
---

Several diffs can describe the same change: when a text contains repeated lines, the algorithm decides which ones are
matched. Every algorithm gives a correct diff, which rebuilds both texts; they differ in which chunks they match and in
how their cost grows. Set the algorithm in [TextDiffOptions.Algorithm](cref:Meziantou.Framework.TextDiffOptions.Algorithm):

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_Algorithm;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions
{
    Algorithm = TextDiffAlgorithm.Patience,
};

var result = TextDiff.ComputeDiff("A\nB\nA\nC", "A\nA\nB\nC", options);

Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
        new TextDiffEntry(TextDiffOperation.Delete, "B\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
        new TextDiffEntry(TextDiffOperation.Insert, "B\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "C"),
    ],
    result.Entries);
```
<!-- endref -->

## Which algorithm to use

| Algorithm | Use it when |
| --- | --- |
| [TextDiffAlgorithm.Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers) (default) | You want a minimal diff: the fewest deleted and inserted chunks. The right choice in most cases. |
| [TextDiffAlgorithm.Patience](cref:Meziantou.Framework.TextDiffAlgorithm.Patience) | People read the diff, as in a code review: readability matters more than a minimal diff. |
| [TextDiffAlgorithm.Histogram](cref:Meziantou.Framework.TextDiffAlgorithm.Histogram) | The texts are large and repetitive, and you want good practical performance. |
| [TextDiffAlgorithm.HuntSzymanski](cref:Meziantou.Framework.TextDiffAlgorithm.HuntSzymanski) | The texts are large and have relatively few chunks in common. |

Patience and Histogram first match the chunks that are rare in both texts, such as unique lines, and use them as
anchors. On source code, this tends to avoid matching unrelated blank lines or closing braces, which makes the diff
easier to follow.

## Cost

All four algorithms first set aside the common start and the common end of the texts, so their cost depends on the part
that differs: comparing two revisions of the same file is cheap whatever the algorithm.

| Algorithm | Cost | Slows down when |
| --- | --- | --- |
| [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers) | Grows with the square of the number of differences | The texts have little in common |
| [Patience](cref:Meziantou.Framework.TextDiffAlgorithm.Patience) | Anchor search, then [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers) between the anchors | No chunk is unique: it falls back to [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers) |
| [Histogram](cref:Meziantou.Framework.TextDiffAlgorithm.Histogram) | Anchor search, then [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers) between the anchors | Every shared chunk is very frequent: it falls back to [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers) |
| [HuntSzymanski](cref:Meziantou.Framework.TextDiffAlgorithm.HuntSzymanski) | Grows with the number of matching pairs of chunks | Many chunks are repeated, which multiplies the matching pairs |

Character diffs reach large sizes quickly, since every character is a chunk; prefer a
[hierarchical diff](06-HierarchicalDiffs.md), which compares characters only inside the chunks that changed.

## Limiting the work

By default, a diff runs to completion however long it takes. When the texts come from users, bound the work so that a
diff of two large, unrelated texts cannot stall your application:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_WorkLimit;format=md-fenced-code-block;dedent=true -->
```csharp
using var cancellationTokenSource = new CancellationTokenSource();
var options = new TextDiffOptions
{
    Timeout = TimeSpan.FromSeconds(1),  // Past this time, the remaining regions are reported as replaced
    MaxEditCost = 1000,                 // Past this many edits in a region, Myers settles for a non-minimal diff
};

var result = TextDiff.ComputeDiff("one\ntwo\n", "one\n2\n", options, cancellationTokenSource.Token);
Assert.True(result.HasDifferences);
```
<!-- endref -->

| Limit | When it is reached |
| --- | --- |
| [Timeout](cref:Meziantou.Framework.TextDiffOptions.Timeout) | Every region of the texts that is not diffed yet is reported as deleted and inserted as a whole. The common start and end of each region are still matched. |
| [MaxEditCost](cref:Meziantou.Framework.TextDiffOptions.MaxEditCost) | When a region needs more deleted and inserted chunks than the limit, the Myers search splits it at the furthest point it reached, as git does, and goes on with both parts. A region within the limit is still diffed exactly. |
| The `cancellationToken` argument | The diff throws an `OperationCanceledException`. |

Both options keep the diff valid: its entries still rebuild both texts. The diff may report as changed some chunks that
an unlimited search would match, so it may not be minimal.

[MaxEditCost](cref:Meziantou.Framework.TextDiffOptions.MaxEditCost) applies to [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers), and to the regions [Patience](cref:Meziantou.Framework.TextDiffAlgorithm.Patience) and [Histogram](cref:Meziantou.Framework.TextDiffAlgorithm.Histogram) hand to [Myers](cref:Meziantou.Framework.TextDiffAlgorithm.Myers).
[HuntSzymanski](cref:Meziantou.Framework.TextDiffAlgorithm.HuntSzymanski) does not use it: bound it with [Timeout](cref:Meziantou.Framework.TextDiffOptions.Timeout). For a
[hierarchical diff](06-HierarchicalDiffs.md), the timeout and the token cover all the levels.
