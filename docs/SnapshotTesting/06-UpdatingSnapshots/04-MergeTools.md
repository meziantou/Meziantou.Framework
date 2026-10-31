---
title: Merge tools
description: Review each snapshot change in a diff or merge tool that the test run opens, and choose which tool is used.
---

With [SnapshotUpdateStrategy.MergeTool](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeTool) or [SnapshotUpdateStrategy.MergeToolSync](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeToolSync), a failed assertion opens a merge tool
that compares each verified file with its actual file. Keep the changes you want, save the verified file, and run the
test again.

## Enable merge tools

Set the strategy for every test in a module initializer (see [Settings](../04-Settings.md#configure-every-test)), or for
a run with the `SNAPSHOTTESTING_STRATEGY` environment variable:

<!-- ref:../../../samples/Meziantou.Framework.SnapshotTesting.Samples/SettingsSnippets.cs;region=Snapshot_MergeTools;format=md-fenced-code-block;dedent=true -->
```csharp
SnapshotSettings.Default.SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeToolSync;
SnapshotSettings.Default.MergeTools = [MergeTool.BeyondCompare, MergeTool.WinMerge, MergeTool.VisualStudioCode];
```
<!-- endref -->

- [MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool) starts the tool and lets the test fail right away. The other tests keep running while you merge.
- [MergeToolSync](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeToolSync) waits for the tool to exit before the test continues. Visual Studio Code and Cursor are started with
  `--wait`, so they block until the diff is closed. Other launchers, such as those of Rider, Visual Studio or
  Kaleidoscope, may hand the files over to a running instance and exit immediately: the test then continues while the
  diff is still open. In that case, an empty verified file created for the merge of a new snapshot is only removed when
  the test process exits, if it is still empty. With [MergeToolSync](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeToolSync), the actual file is deleted once the merge tool
  saved it as the verified file.

## Which tool is started

[SnapshotSettings.MergeTools](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.MergeTools) lists the tools to try, in order; the first one that can be started is used. By default:

1. The tool named by the `DiffEngine_Tool` environment variable, the case-insensitive name of a [MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool) property
   such as [VisualStudioCode](cref:Meziantou.Framework.SnapshotTesting.MergeTool.VisualStudioCode) or `rider`.
2. The merge tool of your git configuration (`merge.tool` and `mergetool.<tool>.cmd`).
3. The diff tool of your git configuration (`diff.tool` and `difftool.<tool>.cmd`).
4. The IDE that runs the tests: Visual Studio, Visual Studio Code or Rider, found among the ancestor processes of the test
   process on Windows, Linux and macOS.
5. The first GUI diff tool installed on the machine, detected by
   <NuGetReference Id="Meziantou.Framework.DiffEngine" />. Terminal tools such
   as Vim and Neovim are skipped, as they cannot run without a terminal.

To use specific tools, set [MergeTools](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.MergeTools), as in the example above. The [MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool) class has a property for each supported
tool: [VisualStudioCode](cref:Meziantou.Framework.SnapshotTesting.MergeTool.VisualStudioCode), [VisualStudio](cref:Meziantou.Framework.SnapshotTesting.MergeTool.VisualStudio), [Rider](cref:Meziantou.Framework.SnapshotTesting.MergeTool.Rider), [BeyondCompare](cref:Meziantou.Framework.SnapshotTesting.MergeTool.BeyondCompare), [WinMerge](cref:Meziantou.Framework.SnapshotTesting.MergeTool.WinMerge), [KDiff3](cref:Meziantou.Framework.SnapshotTesting.MergeTool.KDiff3), [Meld](cref:Meziantou.Framework.SnapshotTesting.MergeTool.Meld), [P4Merge](cref:Meziantou.Framework.SnapshotTesting.MergeTool.P4Merge),
[Kaleidoscope](cref:Meziantou.Framework.SnapshotTesting.MergeTool.Kaleidoscope), [Vim](cref:Meziantou.Framework.SnapshotTesting.MergeTool.Vim), [Neovim](cref:Meziantou.Framework.SnapshotTesting.MergeTool.Neovim), and others. A terminal tool is used when you name it, in [MergeTools](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.MergeTools) or with
`DiffEngine_Tool`. To write your own, derive from [MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool) and implement [Start](cref:Meziantou.Framework.SnapshotTesting.MergeTool.Start).

## When no tool is started

The snapshot difference is reported as a regular assertion failure, and the actual files are left for review, when:

- [MergeTools](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.MergeTools) is `null` or empty;
- the `DiffEngine_Disabled` environment variable is `true` or `1`;
- [AutoDetectContinuousEnvironment](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.AutoDetectContinuousEnvironment) is enabled and the tests run on a build server, in a container, in WSL, under a
  continuous testing runner (NCrunch, ReSharper, Visual Studio Live Unit Testing) or in an LLM agent. See
  [Continuous integration](05-ContinuousIntegration.md).

When merge tools are enabled but none of them can be started, the assertion fails with the paths to compare and the
reason each tool could not start. A tool that fails to start does not prevent the next ones from being tried.

## Git tools

The `cmd` of a git `mergetool.<tool>` or `difftool.<tool>` is run like git runs it: verbatim, through `sh -c`, with the
`LOCAL`, `REMOTE`, `BASE` and `MERGED` environment variables set. `"$LOCAL"`, `${REMOTE}`, `~` or `&&` work as they do
with git, and paths may contain spaces or quotes. On Windows, the `sh.exe` of Git for Windows is used; when it cannot be
found, each placeholder is replaced by its value, quoted as a single argument.

- For a merge tool, `LOCAL` and `BASE` are a temporary copy of the verified file as it was before the merge, `REMOTE` is
  the actual file, and `MERGED` is the verified file the tool writes the result to.
- For a diff tool, `LOCAL`, `MERGED` and `BASE` are the verified file, and `REMOTE` is the actual file.

The command must not return before the merge is done (for Visual Studio Code, use `code --wait`): the temporary copy is
deleted as soon as the command exits.
