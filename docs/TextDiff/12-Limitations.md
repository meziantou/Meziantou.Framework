---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **The built-in output format is the unified diff.** HTML, colored or side-by-side output is up to you; see
  [Displaying a diff](09-DisplayingADiff.md) for an example.
- **Patches are text patches in the unified format.** [UnifiedDiff.Parse](cref:Meziantou.Framework.UnifiedDiff.Parse) ignores the extended headers of git (renames, modes,
  binary patches) and does not unquote file names. A hunk applies only when its context matches exactly: there is no
  fuzz factor, only an offset search. See [Patches](10-Patches.md).
- **Merges do not refine conflicts.** [TextMerge.Merge](cref:Meziantou.Framework.TextMerge.Merge) reports a conflict for the whole changed region, even when both sides
  share lines at its edges. See [Merging](11-Merging.md).
- **Moves are detected by [ComputeDiff](cref:Meziantou.Framework.TextDiff.ComputeDiff) only.** A [hierarchical diff](06-HierarchicalDiffs.md) does not detect moves, and a moved
  block must be equal under the comparison options: a block moved and changed is a deletion and an insertion.
- **Culture-aware comparisons need ICU.** In invariant globalization mode, [CompareOptions](cref:Meziantou.Framework.TextDiffOptions.CompareOptions) and [Culture](cref:Meziantou.Framework.TextDiffOptions.Culture) compare
  ordinally. See [Culture-aware comparison](05-ComparisonOptions.md#culture-aware-comparison).
- **Texts are strings.** Both texts are read into memory as `string`; there is no streaming or file API.
