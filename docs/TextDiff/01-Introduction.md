---
title: Introduction
description: Compute the differences between two texts at the line, word or character level, with configurable comparison.
---

`Meziantou.Framework.TextDiff` computes the differences between two texts. It splits both texts into chunks (lines,
words, characters, or your own unit), matches the chunks they have in common, and tells you, chunk by
chunk, what was kept, deleted and inserted.

## What you can do with it

- **Compare two texts** line by line, word by word, character by character or grapheme by grapheme, or with your own
  chunks, such as sentences. See [Chunking](04-Chunking.md).
- **Ignore what does not matter**: casing, accents, the whitespace around each chunk, and `\r\n` versus `\n` line
  terminators, with ordinal or culture-aware rules. See [Comparison options](05-ComparisonOptions.md).
- **Refine the changes**: find the changed lines, then the changed words inside them, then the changed characters
  inside those words. See [Hierarchical diffs](06-HierarchicalDiffs.md).
- **Find moved blocks**: a block deleted at one place and inserted at another is tagged as a move. See
  [Detecting moves](07-DetectingMoves.md).
- **Choose the algorithm** that suits your texts: Myers, Patience, Histogram or Hunt-Szymanski. See
  [Algorithms](08-Algorithms.md).
- **Rebuild either text** from the result, even when an option made two different chunks compare equal. See
  [Reading the result](03-ReadingTheResult.md).
- **Show the result** your way: write it as a unified diff, or as HTML or anything else from the entries. See
  [Displaying a diff](09-DisplayingADiff.md).
- **Read and apply patches** in the unified format of `diff -u` and `git diff`. See [Patches](10-Patches.md).
- **Merge two versions** of a text changed from a common base, with conflict markers. See [Merging](11-Merging.md).

## How it works

A diff is a list of entries. Each entry is one chunk of the texts with an operation:

| Operation | Meaning |
| --- | --- |
| [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) | The chunk is present in both texts. |
| [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) | The chunk is only present in the old text. |
| [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert) | The chunk is only present in the new text. |

Read the entries in order: the [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) and [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) entries give the old text, the [Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) and [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert) entries give
the new text. A change is a [Delete](cref:Meziantou.Framework.TextDiffOperation.Delete) followed by an [Insert](cref:Meziantou.Framework.TextDiffOperation.Insert), so a moved block of text appears as a deletion at its old
place and an insertion at its new place; [move detection](07-DetectingMoves.md) links the two.

## Namespace

Every type is in the [Meziantou.Framework](cref:Meziantou.Framework.TextDiff!N:Meziantou.Framework) namespace: [TextDiff](cref:Meziantou.Framework.TextDiff), [TextDiffOptions](cref:Meziantou.Framework.TextDiffOptions), [TextChunker](cref:Meziantou.Framework.TextChunker), [TextDiffResult](cref:Meziantou.Framework.TextDiffResult),
[TextDiffEntry](cref:Meziantou.Framework.TextDiffEntry), the hierarchy types, [UnifiedDiff](cref:Meziantou.Framework.UnifiedDiff) and [TextMerge](cref:Meziantou.Framework.TextMerge).

## Next steps

- [Getting started](02-GettingStarted.md): install the package and compute your first diff.
- [Limitations](12-Limitations.md): what the library does not do.
