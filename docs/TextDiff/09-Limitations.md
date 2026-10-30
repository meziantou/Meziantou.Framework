---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **No output format.** The result is a list of entries. Writing a unified diff, HTML or colored output is up to you;
  see [Displaying a diff](08-DisplayingADiff.md) for examples.
- **No patches or merges.** The library does not apply a diff to a text, read a patch file, or merge three versions of
  a text.
- **No move detection.** A block of text moved elsewhere is a deletion and an insertion.
- **No work limit and no cancellation.** A diff runs to completion however long it takes, and there is no
  `CancellationToken`. Diffs of large, unrelated texts can be slow, especially at the character level; see
  [Algorithms](07-Algorithms.md#cost).
- **Ordinal comparisons only.** `IgnoreCase` uses ordinal case-insensitive rules; there is no culture-aware or
  accent-insensitive comparison.
- **Characters are UTF-16 code units.** `TextChunker.Characters` splits surrogate pairs and combining sequences into
  several chunks. See [Characters](04-Chunking.md#characters).
- **Texts are strings.** Both texts are read into memory as `string`; there is no streaming or file API.
