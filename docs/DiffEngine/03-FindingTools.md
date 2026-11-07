---
title: Finding tools
description: Where diff tools are searched for, how to point to an executable, swap the sides of the diff, and the supported tools.
---

## Where tools are searched for

For each tool, the executable is searched for, in this order:

1. The path in the `DiffEngine_<Tool>` environment variable, when it is set. See
   [Point to an executable](#point-to-an-executable).
2. The usual installation folders of the tool on the current operating system, such as `%ProgramFiles%\WinMerge\` on
   Windows or `/Applications/Visual Studio Code.app/Contents/Resources/app/bin/` on macOS. When several versions are
   installed side by side, such as `Beyond Compare 4` and `Beyond Compare 5`, the folder with the highest version
   number is used.
3. The folders of the `PATH` environment variable. On Windows, a name without extension is also tried with each
   extension of `PATHEXT`.

Tools are only found on Windows, Linux and macOS. The search reads the file system on each call: when you look for a
tool many times, keep the [ResolvedTool](cref:Meziantou.Framework.DiffEngine.ResolvedTool) you get.

## Point to an executable

Set `DiffEngine_<Tool>`, where `<Tool>` is the name of a [DiffTool](cref:Meziantou.Framework.DiffEngine.DiffTool) value, to the path of the executable or to the
folder that contains it. Environment variables in the value are expanded, surrounding quotes are removed, and `*`
matches any folder name:

```bash
export DiffEngine_BeyondCompare="/opt/beyondcompare/bin/bcompare"
export DiffEngine_VisualStudioCode="$HOME/tools/vscode/bin"
```

When the variable is set, the installation folders and `PATH` are not searched for that tool. If the executable cannot
be found at that path:

- [TryFindByName](cref:Meziantou.Framework.DiffEngine.DiffTools.TryFindByName) throws an `InvalidOperationException` that names the variable, so a typo is not silently ignored;
- [TryFindByExtension](cref:Meziantou.Framework.DiffEngine.DiffTools.TryFindByExtension) skips that tool and keeps looking at the others.

## Swap the sides of the diff

By default, `GetArguments(tempFile, targetFile)` puts the new content on the left and the target file on the right. Set
`DiffEngine_TargetOnLeft` to `true` to put the target file on the left:

```bash
export DiffEngine_TargetOnLeft=true
```

The variable is read each time [GetArguments](cref:Meziantou.Framework.DiffEngine.ResolvedTool.GetArguments) is called.

## Supported tools

| [DiffTool](cref:Meziantou.Framework.DiffEngine.DiffTool) | Windows | Linux | macOS | Binary extensions |
| --- | :---: | :---: | :---: | --- |
| [MsWordDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.MsWordDiff) | ✔️ | | | `.doc`, `.docx` (no text) |
| [MsExcelDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.MsExcelDiff) | ✔️ | | | `.xls`, `.xlsx` (no text) |
| [BeyondCompare](cref:Meziantou.Framework.DiffEngine.DiffTool.BeyondCompare) | ✔️ | ✔️ | ✔️ | |
| [P4Merge](cref:Meziantou.Framework.DiffEngine.DiffTool.P4Merge) | ✔️ | ✔️ | ✔️ | |
| [Kaleidoscope](cref:Meziantou.Framework.DiffEngine.DiffTool.Kaleidoscope) | | | ✔️ | |
| [DeltaWalker](cref:Meziantou.Framework.DiffEngine.DiffTool.DeltaWalker) | ✔️ | | ✔️ | |
| [WinMerge](cref:Meziantou.Framework.DiffEngine.DiffTool.WinMerge) | ✔️ | | | `.bin`, `.svg`, `.bmp`, `.gif`, `.ico`, `.jpg`, `.jpeg`, `.png`, `.tif`, `.tiff`, `.webp` |
| [TortoiseMerge](cref:Meziantou.Framework.DiffEngine.DiffTool.TortoiseMerge) | ✔️ | | | |
| [TortoiseGitMerge](cref:Meziantou.Framework.DiffEngine.DiffTool.TortoiseGitMerge) | ✔️ | | | |
| [TortoiseGitIDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.TortoiseGitIDiff) | ✔️ | | | |
| [TortoiseIDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.TortoiseIDiff) | ✔️ | | | |
| [KDiff3](cref:Meziantou.Framework.DiffEngine.DiffTool.KDiff3) | ✔️ | ✔️ | ✔️ | |
| [TkDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.TkDiff) | | | ✔️ | |
| [Guiffy](cref:Meziantou.Framework.DiffEngine.DiffTool.Guiffy) | ✔️ | | ✔️ | |
| [ExamDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.ExamDiff) | ✔️ | | | |
| [Diffinity](cref:Meziantou.Framework.DiffEngine.DiffTool.Diffinity) | ✔️ | | | |
| [Rider](cref:Meziantou.Framework.DiffEngine.DiffTool.Rider) | ✔️ | ✔️ | ✔️ | `.svg` |
| [Vim](cref:Meziantou.Framework.DiffEngine.DiffTool.Vim) | ✔️ | ✔️ | ✔️ | |
| [Neovim](cref:Meziantou.Framework.DiffEngine.DiffTool.Neovim) | ✔️ | ✔️ | ✔️ | |
| [AraxisMerge](cref:Meziantou.Framework.DiffEngine.DiffTool.AraxisMerge) | ✔️ | | ✔️ | |
| [Meld](cref:Meziantou.Framework.DiffEngine.DiffTool.Meld) | ✔️ | ✔️ | ✔️ | |
| [SublimeMerge](cref:Meziantou.Framework.DiffEngine.DiffTool.SublimeMerge) | ✔️ | ✔️ | ✔️ | |
| [VisualStudioCode](cref:Meziantou.Framework.DiffEngine.DiffTool.VisualStudioCode) | ✔️ | ✔️ | ✔️ | `.svg`, `.bin` |
| [VisualStudio](cref:Meziantou.Framework.DiffEngine.DiffTool.VisualStudio) | ✔️ | | | `.svg` |
| [Cursor](cref:Meziantou.Framework.DiffEngine.DiffTool.Cursor) | ✔️ | ✔️ | ✔️ | `.svg`, `.bin` |

Every tool supports text, except [MsWordDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.MsWordDiff) and [MsExcelDiff](cref:Meziantou.Framework.DiffEngine.DiffTool.MsExcelDiff). The table is in the order [TryFindByExtension](cref:Meziantou.Framework.DiffEngine.DiffTools.TryFindByExtension) tries
the tools.
