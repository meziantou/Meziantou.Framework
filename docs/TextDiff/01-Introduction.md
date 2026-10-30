---
title: Introduction
description: Compute the differences between two texts at the line, word or character level, with configurable comparison.
---

`Meziantou.Framework.TextDiff` computes the differences between two texts. It splits both texts into chunks (lines,
words, characters, or your own unit), matches the chunks they have in common, and tells you, chunk by
chunk, what was kept, deleted and inserted.

## What you can do with it

- **Compare two texts** line by line, word by word or character by character, or with your own chunks, such as
  sentences. See [Chunking](04-Chunking.md).
- **Ignore what does not matter**: casing, the whitespace around each chunk, and `\r\n` versus `\n` line terminators.
  See [Comparison options](05-ComparisonOptions.md).
- **Refine the changes**: find the changed lines, then the changed words inside them, then the changed characters
  inside those words. See [Hierarchical diffs](06-HierarchicalDiffs.md).
- **Choose the algorithm** that suits your texts: Myers, Patience, Histogram or Hunt-Szymanski. See
  [Algorithms](07-Algorithms.md).
- **Rebuild either text** from the result, even when an option made two different chunks compare equal. See
  [Reading the result](03-ReadingTheResult.md).
- **Show the result** your way: the result is data, so you write it as a unified diff, as HTML or as anything else. See
  [Displaying a diff](08-DisplayingADiff.md).

## How it works

A diff is a list of entries. Each entry is one chunk of the texts with an operation:

| Operation | Meaning |
| --- | --- |
| `Equal` | The chunk is present in both texts. |
| `Delete` | The chunk is only present in the old text. |
| `Insert` | The chunk is only present in the new text. |

Read the entries in order: the `Equal` and `Delete` entries give the old text, the `Equal` and `Insert` entries give
the new text. A change is a `Delete` followed by an `Insert`, so a moved block of text appears as a deletion at its old
place and an insertion at its new place.

## Namespace

Every type is in the [Meziantou.Framework](cref:Meziantou.Framework.TextDiff!N:Meziantou.Framework) namespace: `TextDiff`, `TextDiffOptions`, `TextChunker`, `TextDiffResult`,
`TextDiffEntry` and the hierarchy types.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and compute your first diff.
- [Limitations](09-Limitations.md): what the library does not do.
