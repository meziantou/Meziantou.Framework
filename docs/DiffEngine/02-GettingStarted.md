---
title: Getting started
description: Install the package, find a diff tool, get its arguments and start it.
---

## Install the package

```bash
dotnet add package Meziantou.Framework.DiffEngine
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.DiffEngine;`, and
`using System.Diagnostics;` to start a process.

## Find a tool by name

`DiffTools.TryFindByName` looks for one tool, and returns it as a `ResolvedTool` when it is installed:

<!-- ref:../../tests/Meziantou.Framework.DiffEngine.Tests/DiffToolsTests.cs;region=DiffTools_TryFindByName;format=md-fenced-code-block;dedent=true -->
```csharp
if (DiffTools.TryFindByName(DiffTool.VisualStudioCode, out var tool))
{
    var arguments = tool.GetArguments("received.json", "verified.json");
    Assert.Equal("--diff received.json verified.json", arguments);

    // Start the tool with Process.Start(tool.ExePath, arguments)
}
```
<!-- endref -->

`GetArguments(tempFile, targetFile)` takes the file with the new content first (here `received.json`), and the file to
compare it with, usually the one to update (`verified.json`). It returns the arguments in the syntax of the tool, here
`--diff` for Visual Studio Code. Paths are quoted when they contain spaces or quotes.

`ResolvedTool` describes the tool that was found:

| Property | Description |
| --- | --- |
| `Tool` | The `DiffTool` value |
| `Name` | The name of the `DiffTool` value, such as `VisualStudioCode` |
| `ExePath` | The full path of the executable to start |
| `SupportsText` | Whether the tool compares text files |
| `BinaryExtensions` | The extensions of the binary files the tool compares, such as `.png` or `.docx` |

## Find a tool for a file

`DiffTools.TryFindByExtension` looks at every tool installed on the machine and returns the first one that lists the
extension in its `BinaryExtensions`, or else the first one that supports text. Then start the executable with the
arguments:

<!-- ref:../../samples/Meziantou.Framework.DiffEngine.Samples/Program.cs;region=DiffTools_StartTool;format=md-fenced-code-block;dedent=true -->
```csharp
// A tool that supports this binary extension, or else the first tool that supports text
if (!DiffTools.TryFindByExtension(Path.GetExtension(verifiedFile), out var tool))
{
    Console.WriteLine("No diff tool found");
    return;
}

// The received file holds the new content; the verified file is the one to update
var arguments = tool.GetArguments(receivedFile, verifiedFile);
using var process = Process.Start(tool.ExePath, arguments);
```
<!-- endref -->

- The extension can be written with or without its leading dot (`".png"` or `"png"`), in any case. An empty extension,
  as `Path.GetExtension` returns for a file without one, selects a text tool.
- The tools are tried in the order of the `DiffTool` enumeration. To prefer a tool, look for it with `TryFindByName`
  first.
- `TryFindByExtension` can return a terminal tool, `Vim` or `Neovim`, which needs a console to run in. Check `Tool`
  before starting it from a process that has none, such as a test run.
- Many tools hand the files over to a running instance and exit immediately: the process ending does not mean the user
  closed the diff.

## Next steps

- [Finding tools](03-FindingTools.md): how the executables are found, and how to override them.
