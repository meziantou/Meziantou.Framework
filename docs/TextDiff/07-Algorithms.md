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

There is no work limit: a diff of two large, unrelated texts runs to completion however long it takes. Character diffs
reach large sizes quickly, since every character is a chunk; prefer a [hierarchical diff](06-HierarchicalDiffs.md),
which compares characters only inside the chunks that changed.
