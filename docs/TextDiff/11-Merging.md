---
title: Merging
description: Merge two versions of a text changed from a common base, and resolve the conflicts.
---

[TextMerge.Merge](cref:Meziantou.Framework.TextMerge.Merge) merges two versions of a text, "ours" and "theirs", that were both changed from a common base, as
`diff3` and `git merge` do:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextMerge_Merge;format=md-fenced-code-block;dedent=true -->
```csharp
var baseText = "red\ngreen\nblue\n";
var oursText = "red\nGREEN\nblue\n";            // Changes a line
var theirsText = "red\ngreen\nblue\nyellow\n";   // Adds a line

var result = TextMerge.Merge(baseText, oursText, theirsText);

Assert.False(result.HasConflicts);
Assert.Equal("red\nGREEN\nblue\nyellow\n", result.Text);
```
<!-- endref -->

Both versions are compared to the base. A region changed in one version only takes that change, and a region changed the
same way in both takes it once. A region changed differently in both is a conflict.

## Conflicts

[TextMergeResult.Text](cref:Meziantou.Framework.TextMergeResult.Text) writes each conflict between markers, as git does, and [HasConflicts](cref:Meziantou.Framework.TextMergeResult.HasConflicts) tells whether there is one. The
[Chunks](cref:Meziantou.Framework.TextMergeResult.Chunks) give the text of each region in the three versions, to resolve the conflicts in code:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextMerge_Conflict;format=md-fenced-code-block;dedent=true -->
```csharp
var baseText = "title: Draft\nbody\n";
var oursText = "title: Final\nbody\n";
var theirsText = "title: Review\nbody\n";

var result = TextMerge.Merge(baseText, oursText, theirsText, new TextMergeOptions
{
    ConflictStyle = TextMergeConflictStyle.Diff3,
    OursLabel = "main",
    TheirsLabel = "feature",
});

Assert.True(result.HasConflicts);
Assert.Equal(
    "<<<<<<< main\n" +
    "title: Final\n" +
    "||||||| base\n" +
    "title: Draft\n" +
    "=======\n" +
    "title: Review\n" +
    ">>>>>>> feature\n" +
    "body\n",
    result.Text);

// Each region gives the text of the three versions, to resolve the conflict in code
var conflict = result.Chunks.Single(chunk => chunk.Kind is TextMergeChunkKind.Conflict);
Assert.Equal("title: Draft\n", conflict.BaseText);
Assert.Equal("title: Final\n", conflict.OursText);
Assert.Equal("title: Review\n", conflict.TheirsText);
Assert.Null(conflict.Text);
```
<!-- endref -->

- [ConflictStyle](cref:Meziantou.Framework.TextMergeOptions.ConflictStyle) chooses the markers: [Merge](cref:Meziantou.Framework.TextMergeConflictStyle.Merge) writes both versions, [Diff3](cref:Meziantou.Framework.TextMergeConflictStyle.Diff3) also writes the base after a
  `|||||||` marker, like `merge.conflictStyle=diff3` in git.
- [OursLabel](cref:Meziantou.Framework.TextMergeOptions.OursLabel), [BaseLabel](cref:Meziantou.Framework.TextMergeOptions.BaseLabel) and [TheirsLabel](cref:Meziantou.Framework.TextMergeOptions.TheirsLabel) are written after the markers. An empty label writes the marker alone.
- A marker always starts a line: a line feed is added after a version whose last line has none.
- Two insertions at the same place are a conflict, even when they do not overlap, since the merge cannot choose their
  order. So is a line deleted in one version and changed in the other.

## The regions

Each [TextMergeChunk](cref:Meziantou.Framework.TextMergeChunk) has a [Kind](cref:Meziantou.Framework.TextMergeChunk.Kind):

| Kind | Merged [Text](cref:Meziantou.Framework.TextMergeChunk.Text) |
| --- | --- |
| [Unchanged](cref:Meziantou.Framework.TextMergeChunkKind.Unchanged) | The text of ours |
| [ChangedInOurs](cref:Meziantou.Framework.TextMergeChunkKind.ChangedInOurs) | The text of ours |
| [ChangedInTheirs](cref:Meziantou.Framework.TextMergeChunkKind.ChangedInTheirs) | The text of theirs |
| [ChangedInBoth](cref:Meziantou.Framework.TextMergeChunkKind.ChangedInBoth) | The text of ours, the same change as theirs |
| [Conflict](cref:Meziantou.Framework.TextMergeChunkKind.Conflict) | `null` |

The [BaseText](cref:Meziantou.Framework.TextMergeChunk.BaseText), [OursText](cref:Meziantou.Framework.TextMergeChunk.OursText) and [TheirsText](cref:Meziantou.Framework.TextMergeChunk.TheirsText) of the regions concatenate back to the three texts.

## Options

[DiffOptions](cref:Meziantou.Framework.TextMergeOptions.DiffOptions) sets the options of the two diffs: the [chunker](04-Chunking.md), the [algorithm](08-Algorithms.md), the
[comparison options](05-ComparisonOptions.md) and the work limits. Lines are the default; words merge two edits of the
same line that touch different words.

With a comparison option such as [IgnoreCase](cref:Meziantou.Framework.TextDiffOptions.IgnoreCase), a region whose differences are ignored is unchanged and takes the text
of ours. With [IgnoreEndOfLine](cref:Meziantou.Framework.TextDiffOptions.IgnoreEndOfLine), the three texts are normalized first, so the merged text has `\n` line terminators.
