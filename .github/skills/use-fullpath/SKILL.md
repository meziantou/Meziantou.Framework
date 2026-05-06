---
name: use-fullpath
description: "Ensure .NET projects use `Meziantou.Framework.FullPath` for local file path manipulation instead of raw strings. Use when: reviewing or writing code that constructs, combines, compares, or passes around local file/directory paths as strings in .NET/C# projects."
---

# Use FullPath for Local Path Manipulation

Use this skill when reviewing or writing .NET code that manipulates local file or directory paths.

## Why

Raw `string` paths are a common source of bugs:

- A path can be absolute or relative (`c:\a\b` vs `.\a\b`)
- A path that looks absolute can actually be relative (`c:a` is relative)
- Paths may contain `.` or `..` segments that aren't resolved
- Trailing directory separators cause inconsistent comparisons (`c:\a` vs `c:\a\`)
- Path comparison must be case-insensitive on Windows and case-sensitive on Linux
- Reserved device names on Windows (`CON`, `PRN`, `NUL`) can cause subtle failures
- When a `FileNotFoundException` occurs, relative paths make debugging difficult
- A `string` parameter gives no signal whether a full path or a relative path is expected

The `FullPath` struct from `Meziantou.Framework.FullPath` solves these problems by guaranteeing the stored value is always a fully-resolved absolute path with normalized separators.

## Installation

```shell
# .NET 10+
dotnet package add Meziantou.Framework.FullPath

# .NET 9 and earlier
dotnet add package Meziantou.Framework.FullPath
```

The namespace is `Meziantou.Framework`.

## Core Patterns

### Creating a FullPath

```csharp
// From a string (relative paths are resolved against the current directory)
FullPath path = FullPath.FromPath("demo");

// Well-known locations
FullPath temp = FullPath.GetTempPath();
FullPath cwd  = FullPath.CurrentDirectory();
FullPath docs = FullPath.GetFolderPath(Environment.SpecialFolder.MyDocuments);
```

### Combining Paths

Use the `/` operator or `FullPath.Combine` — never `string.Concat` or string interpolation.

```csharp
FullPath root = FullPath.FromPath("/repo");

// / operator (preferred for readability)
FullPath file = root / "src" / "Program.cs";

// Combine (useful when the number of segments is dynamic)
FullPath file2 = FullPath.Combine(root, "src", "Program.cs");
```

### Comparing Paths

Comparisons are case-insensitive on Windows/macOS and case-sensitive on Linux by default. Navigation segments and trailing separators are already resolved, so equality just works.

```csharp
// == and != use the OS-default comparison
if (pathA == pathB) { /* same file */ }

// Explicit case-sensitivity control
bool equal = pathA.Equals(pathB, ignoreCase: false);
```

### Navigating the Path Tree

```csharp
FullPath file = FullPath.FromPath("/repo/src/Program.cs");

FullPath dir       = file.Parent;              // /repo/src
string   name      = file.Name;               // Program.cs
string   nameNoExt = file.NameWithoutExtension; // Program
string   ext       = file.Extension;           // .cs

FullPath renamed = file.ChangeExtension(".vb"); // /repo/src/Program.vb
```

### Checking Hierarchy

```csharp
FullPath root = FullPath.FromPath("/repo");
FullPath file = root / "src" / "Program.cs";

bool isChild = file.IsChildOf(root); // true
```

### Making a Relative Path

```csharp
FullPath root = FullPath.FromPath("/repo");
FullPath file = root / "src" / "Program.cs";

string relative = file.MakePathRelativeTo(root); // src/Program.cs (or src\Program.cs on Windows)
```

### Walking Up the Tree

```csharp
FullPath start = FullPath.FromPath("/repo/src/deep/nested");

// Find the first ancestor (or self) matching a predicate
if (start.TryFindFirstAncestorOrSelf(
        p => File.Exists(p / ".editorconfig"), out FullPath match))
{
    // match is the closest directory containing .editorconfig
}

// Shortcut: find the Git repository root
if (start.TryFindGitRepositoryRoot(out FullPath gitRoot))
{
    // gitRoot is the closest directory containing .git
}
```

### Interop with System.IO

`FullPath` has an implicit conversion to `string`, so it works directly with `File`, `Directory`, and most APIs that accept a string path.

```csharp
FullPath config = root / "appsettings.json";

// No .Value or .ToString() needed
string json = File.ReadAllText(config);
File.WriteAllText(config, json);
Directory.CreateDirectory(config.Parent);
```

When you need the raw string explicitly (e.g., for logging or interpolation):

```csharp
logger.LogInformation("Loading config from {Path}", config.Value);
```

### Ensuring the Parent Directory Exists

```csharp
FullPath output = root / "artifacts" / "report.html";
output.CreateParentDirectory(); // creates "artifacts" if it doesn't exist
File.WriteAllText(output, html);
```

### JSON Serialization

`FullPath` has a built-in `System.Text.Json` converter. It serializes to a plain JSON string and deserializes back through `FullPath.FromPath`.

```csharp
public sealed class AppSettings
{
    public FullPath OutputDirectory { get; set; }
}

// { "OutputDirectory": "C:\\repo\\output" }
var settings = JsonSerializer.Deserialize<AppSettings>(json);
```

### Using FullPath in Method Signatures

Prefer `FullPath` over `string` in APIs that expect an absolute path. This makes intent explicit at the type level.

```csharp
// ❌ Ambiguous — is this a full path or a relative fragment?
void Export(string outputPath) { }

// ✅ Clear contract — the caller must provide a resolved path
void Export(FullPath outputPath) { }
```

### Collections and Dictionaries

Use `FullPathComparer` when storing paths in sets or dictionaries to get correct OS-aware equality.

```csharp
var seen = new HashSet<FullPath>(); // uses default OS comparison
var map  = new Dictionary<FullPath, int>();

// Explicit comparer if needed
var caseSensitiveSet = new HashSet<FullPath>(FullPathComparer.CaseSensitive);
```

## What to Look For in Reviews

| Smell | Refactor to |
|---|---|
| `string filePath = Path.Combine(root, "sub", "file.txt");` | `FullPath filePath = root / "sub" / "file.txt";` |
| `string fullPath = Path.GetFullPath(relative);` | `FullPath fullPath = FullPath.FromPath(relative);` |
| `if (path1.Equals(path2, StringComparison.OrdinalIgnoreCase))` | `if (path1 == path2)` (with `FullPath`) |
| `path.StartsWith(root)` to check containment | `path.IsChildOf(root)` |
| `Path.GetDirectoryName(path)` | `path.Parent` |
| `Path.GetFileName(path)` | `path.Name` |
| `Path.GetExtension(path)` | `path.Extension` |
| `Path.ChangeExtension(path, ext)` | `path.ChangeExtension(ext)` |
| `Directory.CreateDirectory(Path.GetDirectoryName(path))` | `path.CreateParentDirectory()` |
| `void Foo(string path)` for an absolute-path parameter | `void Foo(FullPath path)` |

## Scope

This skill targets **local file-system paths** only. It does not apply to:

- URLs or URIs
- Cloud/blob storage paths
- Database connection strings
- Paths that must remain relative by design (e.g., entries inside a ZIP archive)
