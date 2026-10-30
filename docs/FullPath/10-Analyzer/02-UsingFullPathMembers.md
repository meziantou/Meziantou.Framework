---
title: Using FullPath members
description: The rules that replace Path, Directory and Environment methods with their FullPath equivalents, and the differences to know before you apply their fixes.
---

These rules report a call to a `Path`, `Directory` or `Environment` method that receives a `FullPath` or that has a
`FullPath` equivalent. They are all Info rules, and all of them but `MFFP0023` have a code fix.

| Rule | Reported | Fixed to |
| --- | --- | --- |
| `MFFP0001` | `Path.GetFullPath(fullPath)` | `fullPath` |
| `MFFP0002` | `left / right`, where `right` is a `FullPath` | `right` |
| `MFFP0003` | `Path.GetFullPath(value, basePath)`, where `basePath` is a `FullPath` | `basePath / value` |
| `MFFP0004` | `Path.Combine(fullPath, "value1", "value2")` | `fullPath / "value1" / "value2"` |
| `MFFP0005` | `Path.GetFileName(fullPath)` | `fullPath.Name` |
| `MFFP0006` | `Path.GetFileNameWithoutExtension(fullPath)` | `fullPath.NameWithoutExtension` |
| `MFFP0007` | `Path.GetExtension(fullPath)` | `fullPath.Extension` |
| `MFFP0008` | `Path.GetDirectoryName(fullPath)` | `fullPath.Parent` |
| `MFFP0009` | `Path.ChangeExtension(fullPath, extension)` | `fullPath.WithExtension(extension)` |
| `MFFP0010` | `Path.GetRelativePath(root, fullPath)`, where both are `FullPath` | `fullPath.MakePathRelativeTo(root)` |
| `MFFP0019` | `FullPath.FromPath(fullPath)` | `fullPath` |
| `MFFP0019` | `FullPath.FromPath(Path.GetFullPath(path))` | `FullPath.FromPath(path)` |
| `MFFP0019` | `FullPath.FromPath(Path.Combine(path1, path2))` | `FullPath.Combine(path1, path2)` |
| `MFFP0020` | `FullPath.FromPath(info.FullName)` | `FullPath.FromFileSystemInfo(info)` |
| `MFFP0022` | `Directory.CreateDirectory(fullPath.Parent);` | `fullPath.CreateParentDirectory();` |
| `MFFP0023` | `Directory.GetParent(fullPath)` | No fix: use `fullPath.Parent` |
| `MFFP0024` | `Path.GetTempPath()` | `FullPath.GetTempPath()` |
| `MFFP0024` | `Environment.GetFolderPath(folder)` | `FullPath.GetFolderPath(folder)` |

The sections below give the details of each rule: the cases it does not report, the cases where its fix is not
offered, and the differences of behavior between the original code and the fixed code.

## MFFP0001: Use FullPath directly instead of Path.GetFullPath

A `FullPath` is already a full path: `Path.GetFullPath(fullPath)` returns its string unchanged. The rule also reports
the overload with a base path, whose base is then ignored.

- The fix keeps the argument as written: `Path.GetFullPath(fullPath.Value)` becomes `fullPath.Value`.
- It is not offered when the result is used as the receiver of a member, such as `Path.GetFullPath(fullPath).Length`,
  or initializes a `var` variable: the type of the expression would change.
- `Path.GetFullPath` throws on the empty path; the fixed code returns it.

## MFFP0002: Use the right FullPath operand directly

`left / right` returns `right` when `right` is a `FullPath`: a full path is rooted, and the `/` operator, like
`Path.Combine`, ignores the left operand when the right one is rooted. The fix keeps the right operand only.

## MFFP0003: Use '/' with a FullPath base instead of Path.GetFullPath

`Path.GetFullPath(value, basePath)` resolves `value` against a `FullPath` base: `basePath / value` does the same and
returns a `FullPath`. The fix is not offered when `value` is a `FullPath` too: `MFFP0001` reports that case.

## MFFP0004: Use '/' operator instead of Path.Combine

The rule reports `Path.Combine` when any of its arguments is a `FullPath`, including the overloads with five arguments
or more. It does not report `Path.Join`, which concatenates its arguments even when one of them is rooted, unlike the
`/` operator.

- The fix starts from the last `FullPath` argument and appends the following arguments with `/`. The arguments before
  it are dropped: `Path.Combine` ignores them, because a `FullPath` is rooted. `Path.Combine("value1", "value2",
  fullPath)` becomes `fullPath`.
- It is not offered when a dropped argument may have side effects, such as a method call.
- The fixed expression is a `FullPath`. Where a `string` is required, it converts implicitly.

## MFFP0005, MFFP0006, MFFP0007: name and extension

`Path.GetFileName`, `Path.GetFileNameWithoutExtension` and `Path.GetExtension` with a `FullPath` argument are replaced
with the `Name`, `NameWithoutExtension` and `Extension` properties, which return the same strings. A complex argument
is put in parentheses: `Path.GetFileName(condition ? a : b)` becomes `(condition ? a : b).Name`. See
[Names, extensions and parents](../04-NamesAndExtensions.md).

## MFFP0008: Use FullPath.Parent instead of Path.GetDirectoryName

- `Path.GetDirectoryName` returns `null` for a root folder; `Parent` returns the empty path, which converts to an empty
  string. Code that checks the result for `null` must check `IsEmpty` instead.
- The fix is not offered when the result is used as the receiver of a member, such as
  `Path.GetDirectoryName(fullPath).Length`, or initializes a `var` variable.

## MFFP0009: Use FullPath.WithExtension instead of Path.ChangeExtension

`fullPath.WithExtension(extension)` changes the extension like `Path.ChangeExtension`, and returns a `FullPath`.

## MFFP0010: Use FullPath.MakePathRelativeTo instead of Path.GetRelativePath

The rule only reports calls where both arguments are `FullPath` values. Note the order of the arguments:
`Path.GetRelativePath(relativeTo, path)` becomes `path.MakePathRelativeTo(relativeTo)`. On an empty path, `MakePathRelativeTo` throws an
`InvalidOperationException` where `Path.GetRelativePath` throws an `ArgumentException`; on an empty folder, it returns
the full path where `Path.GetRelativePath` throws.

## MFFP0019: Simplify the FullPath.FromPath call

The rule reports three calls whose `FromPath` is redundant:

- `FullPath.FromPath(fullPath)` converts a `FullPath` to a string and back. The fix keeps the `FullPath`.
- `FullPath.FromPath(Path.GetFullPath(path))`: `FromPath` already resolves the path. On an empty string,
  `Path.GetFullPath` throws, while the fixed code returns the empty path.
- `FullPath.FromPath(Path.Combine(...))`: `FullPath.Combine` takes the same arguments. `Path.Join` is not reported.

## MFFP0020: Use FullPath.FromFileSystemInfo instead of FullPath.FromPath

`FullPath.FromPath(info.FullName)`, where `info` is a `FileInfo`, a `DirectoryInfo` or another `FileSystemInfo`,
becomes `FullPath.FromFileSystemInfo(info)`. When `info` is `null`, the original code throws a
`NullReferenceException`, while the fixed code returns the empty path.

## MFFP0022: Use FullPath.CreateParentDirectory instead of Directory.CreateDirectory

The rule reports `Directory.CreateDirectory(fullPath.Parent)` when its result is not used. It does not report the call
when the returned `DirectoryInfo` is used, or with the overload that takes a `UnixFileMode`: `CreateParentDirectory`
returns nothing and takes no options.

## MFFP0023: Use FullPath.Parent instead of Directory.GetParent

`Directory.GetParent(fullPath)` returns a `DirectoryInfo`, where `fullPath.Parent` returns a `FullPath`. The rule has
no code fix: replace the call by hand, and use `FullPath` where the code used the `DirectoryInfo`.

## MFFP0024: Use the FullPath equivalent of Path.GetTempPath or Environment.GetFolderPath

The rule reports every call to `Path.GetTempPath()` and to both overloads of `Environment.GetFolderPath`. The fix
calls `FullPath.GetTempPath()` or `FullPath.GetFolderPath` with the same arguments.

- `Path.GetTempPath()` ends with a directory separator; `FullPath.GetTempPath()` does not. Code that concatenates
  strings to the result must combine paths instead.
- For a folder that does not exist on the platform, `FullPath.GetFolderPath` returns the empty path, which converts to
  the same empty string.
