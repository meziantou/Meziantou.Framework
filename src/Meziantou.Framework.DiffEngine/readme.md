# Meziantou.Framework.DiffEngine

`Meziantou.Framework.DiffEngine` resolves installed diff tools and provides launch arguments for comparing two files.

This package is designed for snapshot testing scenarios and is API-compatible with the subset needed by:

- `Meziantou.Framework.SnapshotTesting`
- `Meziantou.Framework.InlineSnapshotTesting`

## Supported public API

- `DiffTool` enum
- `DiffTools.TryFindByName(DiffTool, out ResolvedTool?)`
- `DiffTools.TryFindByExtension(string, out ResolvedTool?)`
- `ResolvedTool` (`Name`, `Tool`, `ExePath`, `SupportsText`, `BinaryExtensions`, `GetArguments`)

## Example

````c#
if (DiffTools.TryFindByName(DiffTool.VisualStudioCode, out var tool))
{
    var arguments = tool.GetArguments("received.json", "verified.json");
    Console.WriteLine($"{tool.ExePath} {arguments}");
}
````

## Environment variables

- `DiffEngine_<ToolName>`: override executable detection for one tool (file path or directory).
  When the variable is set but the executable cannot be found, `TryFindByName` throws so the typo is not
  silently ignored, while `TryFindByExtension` skips that tool and keeps looking at the others.
- `DiffEngine_TargetOnLeft=true`: invert generated argument order
