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
tool many times, keep the `ResolvedTool` you get.

## Point to an executable

Set `DiffEngine_<Tool>`, where `<Tool>` is the name of a `DiffTool` value, to the path of the executable or to the
folder that contains it. Environment variables in the value are expanded, surrounding quotes are removed, and `*`
matches any folder name:

```bash
export DiffEngine_BeyondCompare="/opt/beyondcompare/bin/bcompare"
export DiffEngine_VisualStudioCode="$HOME/tools/vscode/bin"
```

When the variable is set, the installation folders and `PATH` are not searched for that tool. If the executable cannot
be found at that path:

- `TryFindByName` throws an `InvalidOperationException` that names the variable, so a typo is not silently ignored;
- `TryFindByExtension` skips that tool and keeps looking at the others.

## Swap the sides of the diff

By default, `GetArguments(tempFile, targetFile)` puts the new content on the left and the target file on the right. Set
`DiffEngine_TargetOnLeft` to `true` to put the target file on the left:

```bash
export DiffEngine_TargetOnLeft=true
```

The variable is read each time `GetArguments` is called.

## Supported tools

| `DiffTool` | Windows | Linux | macOS | Binary extensions |
| --- | :---: | :---: | :---: | --- |
| `MsWordDiff` | ✔️ | | | `.doc`, `.docx` (no text) |
| `MsExcelDiff` | ✔️ | | | `.xls`, `.xlsx` (no text) |
| `BeyondCompare` | ✔️ | ✔️ | ✔️ | |
| `P4Merge` | ✔️ | ✔️ | ✔️ | |
| `Kaleidoscope` | | | ✔️ | |
| `DeltaWalker` | ✔️ | | ✔️ | |
| `WinMerge` | ✔️ | | | `.bin`, `.svg`, `.bmp`, `.gif`, `.ico`, `.jpg`, `.jpeg`, `.png`, `.tif`, `.tiff`, `.webp` |
| `TortoiseMerge` | ✔️ | | | |
| `TortoiseGitMerge` | ✔️ | | | |
| `TortoiseGitIDiff` | ✔️ | | | |
| `TortoiseIDiff` | ✔️ | | | |
| `KDiff3` | ✔️ | ✔️ | ✔️ | |
| `TkDiff` | | | ✔️ | |
| `Guiffy` | ✔️ | | ✔️ | |
| `ExamDiff` | ✔️ | | | |
| `Diffinity` | ✔️ | | | |
| `Rider` | ✔️ | ✔️ | ✔️ | `.svg` |
| `Vim` | ✔️ | ✔️ | ✔️ | |
| `Neovim` | ✔️ | ✔️ | ✔️ | |
| `AraxisMerge` | ✔️ | | ✔️ | |
| `Meld` | ✔️ | ✔️ | ✔️ | |
| `SublimeMerge` | ✔️ | ✔️ | ✔️ | |
| `VisualStudioCode` | ✔️ | ✔️ | ✔️ | `.svg`, `.bin` |
| `VisualStudio` | ✔️ | | | `.svg` |
| `Cursor` | ✔️ | ✔️ | ✔️ | `.svg`, `.bin` |

Every tool supports text, except `MsWordDiff` and `MsExcelDiff`. The table is in the order `TryFindByExtension` tries
the tools.
