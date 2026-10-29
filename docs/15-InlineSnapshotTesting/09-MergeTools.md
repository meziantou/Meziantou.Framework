---
title: Merge tools
description: Review each inline snapshot change in a diff or merge tool, and choose which tool is used.
---

With `SnapshotUpdateStrategy.MergeTool` or `SnapshotUpdateStrategy.MergeToolSync`, a failed assertion opens a merge tool
that compares the source file of the test with a temporary copy that contains the new snapshot. Accept the change and
save the source file, or close the tool to keep the old snapshot.

- `MergeTool` starts the tool and lets the test fail right away.
- `MergeToolSync` waits for the tool to exit before the test continues. Visual Studio Code and Cursor are started with
  `--wait`, so they block until the diff is closed. Other launchers, such as those of Rider, Visual Studio or
  Kaleidoscope, may hand the files over to a running instance and exit immediately: the test then continues while the
  diff is still open, and the temporary file is only deleted when the test process exits.

## Which tool is started

`InlineSnapshotSettings.MergeTools` lists the tools to try, in order; the first one that can be started is used. By
default:

1. The tool named by the `DiffEngine_Tool` environment variable, the case-insensitive name of a `MergeTool` property
   such as `VisualStudioCode` or `rider`.
2. The merge tool of your git configuration.
3. The diff tool of your git configuration.
4. The IDE that runs the tests: Visual Studio, Visual Studio Code or Rider, found among the ancestor processes of the test
   process on Windows, Linux and macOS.
5. The first GUI diff tool installed on the machine, detected by
   [`Meziantou.Framework.DiffEngine`](https://www.nuget.org/packages/Meziantou.Framework.DiffEngine). Terminal tools such
   as Vim and Neovim are skipped, as they cannot run without a terminal.

To use specific tools, set `MergeTools`, as in the
[module initializer example](03-Settings.md#configure-every-test). The `MergeTool` class has a property for each
supported tool: `VisualStudioCode`, `VisualStudio`, `Rider`, `BeyondCompare`, `WinMerge`, `KDiff3`, `Meld`, `P4Merge`,
`Kaleidoscope`, `Vim`, `Neovim`, and others. A terminal tool is used when you name it, in `MergeTools` or with
`DiffEngine_Tool`.

## When no tool is started

The snapshot difference is reported as a regular assertion failure, and the source file is left unchanged, when:

- `MergeTools` is `null` or empty;
- the `DiffEngine_Disabled` environment variable is `true` or `1`;
- `AutoDetectContinuousEnvironment` is enabled and the tests run on a build server, in a container, in WSL, under a
  continuous testing runner (NCrunch, ReSharper, Visual Studio Live Unit Testing) or in an LLM agent. See
  [Continuous integration](10-ContinuousIntegration.md).

When merge tools are enabled but none of them can be started, the assertion fails with the paths to compare and the
reason each tool could not start. A tool that fails to start does not prevent the next ones from being tried.

## Git tools

The `cmd` of a git `mergetool.<tool>` or `difftool.<tool>` is run like git runs it: verbatim, through `sh -c`, with the
`LOCAL`, `REMOTE`, `BASE` and `MERGED` environment variables set. `"$LOCAL"`, `${REMOTE}`, `~` or `&&` work as they do
with git, and paths may contain spaces or quotes. On Windows, the `sh.exe` of Git for Windows is used; when it cannot be
found, each placeholder is replaced by its value, quoted as a single argument.

- For a merge tool, `LOCAL` and `BASE` are a temporary copy of the source file as it was before the merge, `REMOTE` is a
  temporary copy of the source file that contains the new snapshot, and `MERGED` is the source file the tool writes the
  result to.
- For a diff tool, `LOCAL`, `MERGED` and `BASE` are the source file, and `REMOTE` is a temporary copy of the source file
  that contains the new snapshot.

The command must not return before the merge is done (for Visual Studio Code, use `code --wait`): the temporary copy is
deleted as soon as the command exits.

File snapshots use the same tools: see the [merge tools of SnapshotTesting](../14-SnapshotTesting/06-UpdatingSnapshots/04-MergeTools.md).
