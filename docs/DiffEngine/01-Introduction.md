---
title: Introduction
description: Find an installed diff tool, by name or by file extension, and get the arguments that compare two files.
---

`Meziantou.Framework.DiffEngine` finds the diff and merge tools installed on the machine, such as Visual Studio Code,
Rider, Beyond Compare, WinMerge or Meld, and builds the command line that opens two files side by side in them. It does
not compare files itself: it tells you which executable to start, and with which arguments.

The snapshot testing libraries use it to open a merge tool when a snapshot changes. See
[Merge tools](../SnapshotTesting/06-UpdatingSnapshots/04-MergeTools.md) for file snapshots and
[Merge tools](../InlineSnapshotTesting/09-MergeTools.md) for inline snapshots: you do not need this package directly
to use them.

## What you can do with it

- **Find a tool by name** and get the arguments for two files. See [Getting started](02-GettingStarted.md).
- **Find a tool for a file type**: one that supports the extension, or else one that supports text. See
  [Getting started](02-GettingStarted.md#find-a-tool-for-a-file).
- **Control the detection** with environment variables: point to an executable, or swap the sides of the diff. See
  [Finding tools](03-FindingTools.md).

## Namespace

[DiffTools](cref:Meziantou.Framework.DiffEngine.DiffTools), [DiffTool](cref:Meziantou.Framework.DiffEngine.DiffTool) and [ResolvedTool](cref:Meziantou.Framework.DiffEngine.ResolvedTool) are in the [Meziantou.Framework.DiffEngine](cref:Meziantou.Framework.DiffEngine) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and open a diff.
- [Finding tools](03-FindingTools.md): where tools are searched for, and the supported tools.
