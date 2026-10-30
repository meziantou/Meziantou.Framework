---
title: Creating paths
description: Create a FullPath from a string, a FileSystemInfo or a well-known folder, combine paths, and create temporary files.
---

## From a string

[FullPath.FromPath](cref:Meziantou.Framework.FullPath.FromPath) converts a string to a [FullPath](cref:Meziantou.Framework.FullPath). A relative path is resolved against the current directory, and
the result is normalized:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_Normalization;format=md-fenced-code-block;dedent=true -->
```csharp
// A relative path is resolved against the current directory
FullPath data = FullPath.FromPath("data");
Assert.Equal(FullPath.CurrentDirectory() / "data", data);

// "." and ".." segments are resolved, and the trailing separator is removed
FullPath logs = FullPath.FromPath("data/./../logs/");
Assert.Equal(FullPath.CurrentDirectory() / "logs", logs);
Assert.Equal("logs", logs.Name);
```
<!-- endref -->

Convert a string as soon as it enters the application, for instance a command-line argument: the path is then resolved
against the current directory of that moment, and later changes of `Environment.CurrentDirectory` do not affect it.

[FromPath](cref:Meziantou.Framework.FullPath.FromPath) validates the path like `Path.GetFullPath` does: an invalid path, such as one that contains a null
character, throws an `ArgumentException`.

## Combine paths

The `/` operator appends a relative path to a [FullPath](cref:Meziantou.Framework.FullPath). [FullPath.Combine](cref:Meziantou.Framework.FullPath.Combine) does the same with several segments, and
the `+` operator appends text to the last segment:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_Combine;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath root = FullPath.GetTempPath() / "app";

// The / operator and Combine are equivalent
Assert.Equal(FullPath.Combine(root, "logs", "today.log"), root / "logs" / "today.log");

// The right operand can hold several segments and ".."
Assert.Equal(root / "data" / "input.csv", root / "logs/../data/input.csv");

// The + operator appends text to the last segment
Assert.Equal(root / "logs.old", root / "logs" + ".old");

// Like Path.Combine, a rooted right operand replaces the left one
string absolutePath = Environment.CurrentDirectory;
Assert.Equal(FullPath.FromPath(absolutePath), root / absolutePath);
```
<!-- endref -->

- The result is normalized like the result of [FromPath](cref:Meziantou.Framework.FullPath.FromPath): the right operand can contain `..` segments.
- Like `Path.Combine`, the `/` operator returns the right operand when it is a rooted path, and ignores the left one.
  When the right operand comes from a user, check the result with [IsChildOf](cref:Meziantou.Framework.FullPath.IsChildOf): see
  [Keep a path inside a folder](05-ComparingPaths.md#keep-a-path-inside-a-folder).
- [FullPath.Combine](cref:Meziantou.Framework.FullPath.Combine) also accepts a string as the first segment.

## From a FileInfo or a DirectoryInfo

[FullPath.FromFileSystemInfo](cref:Meziantou.Framework.FullPath.FromFileSystemInfo) takes a `FileInfo`, a `DirectoryInfo` or any `FileSystemInfo`. It returns the empty
path for `null`.

## Well-known folders

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_WellKnownFolders;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath temp = FullPath.GetTempPath();
FullPath current = FullPath.CurrentDirectory();
FullPath home = FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile);
FullPath fromInfo = FullPath.FromFileSystemInfo(new DirectoryInfo("output"));

Assert.False(temp.IsEmpty);
Assert.False(current.IsEmpty);
Assert.False(home.IsEmpty);
Assert.Equal(current / "output", fromInfo);
```
<!-- endref -->

| Method | Equivalent string API |
| --- | --- |
| [FullPath.CurrentDirectory()](cref:Meziantou.Framework.FullPath.CurrentDirectory) | `Environment.CurrentDirectory` |
| [FullPath.GetTempPath()](cref:Meziantou.Framework.FullPath.GetTempPath) | `Path.GetTempPath()` |
| `FullPath.GetFolderPath(folder)` and `FullPath.GetFolderPath(folder, option)` | `Environment.GetFolderPath` |
| `FullPath.GetKnownFolderPath(knownFolder)` | `SHGetKnownFolderPath`, on Windows only: see [Known folders](09-PlatformSpecificFeatures.md#known-folders) |

`Environment.GetFolderPath` returns an empty string for a folder that does not exist on the current platform, so
[FullPath.GetFolderPath](cref:Meziantou.Framework.FullPath.GetFolderPath) returns the empty path: check [IsEmpty](cref:Meziantou.Framework.FullPath.IsEmpty) before you use the result. Unlike `Path.GetTempPath()`,
[FullPath.GetTempPath()](cref:Meziantou.Framework.FullPath.GetTempPath) has no trailing separator.

## Create a temporary file

[FullPath.CreateTempFile](cref:Meziantou.Framework.FullPath.CreateTempFile) creates an empty file with a unique name and returns its path:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_CreateTempFile;format=md-fenced-code-block;dedent=true -->
```csharp
// Creates an empty file with a unique name in the temporary folder
FullPath report = FullPath.CreateTempFile(prefix: "report-", suffix: ".csv");

Assert.True(File.Exists(report));
Assert.Equal(FullPath.GetTempPath(), report.Parent);
Assert.StartsWith("report-", report.Name);
Assert.Equal(".csv", report.Extension);
```
<!-- endref -->

- The name is the prefix, a random GUID and the suffix. The suffix is `.tmp` by default; pass `null` for none.
- Another overload takes the folder in which to create the file. The folder is created if it does not exist; when it is
  `null` or empty, the temporary folder is used.
- The file is created only if no file has the same name. On Linux and macOS, only the current user can read and write
  it.
- A prefix or a suffix that contains a character that is not valid in a file name, such as a directory separator,
  throws an `ArgumentException`. When no unique name is found after 10 attempts, the method throws an `IOException`.
- The file is not deleted for you: delete it when you no longer need it.

## The empty path

[FullPath.Empty](cref:Meziantou.Framework.FullPath.Empty), which is also `default(FullPath)`, represents the absence of a path:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_Empty;format=md-fenced-code-block;dedent=true -->
```csharp
// An empty string gives the empty path, not the current directory
FullPath path = FullPath.FromPath("");
Assert.True(path.IsEmpty);
Assert.Equal(FullPath.Empty, path);
Assert.Equal(default(FullPath), path);
Assert.Equal("", path.Value);

// A null FileSystemInfo gives the empty path too
Assert.True(FullPath.FromFileSystemInfo(null).IsEmpty);
```
<!-- endref -->

- An empty string gives the empty path, not the current directory. Use [FullPath.CurrentDirectory()](cref:Meziantou.Framework.FullPath.CurrentDirectory) or
  `FullPath.FromPath(".")` for the current directory.
- [Name](cref:Meziantou.Framework.FullPath.Name), [Extension](cref:Meziantou.Framework.FullPath.Extension), `Value` and the conversion to `string` return an empty string; [Parent](cref:Meziantou.Framework.FullPath.Parent) and [WithExtension](cref:Meziantou.Framework.FullPath.WithExtension)
  return the empty path.
- Combining the empty path with a relative path gives a path in the current directory. Check [IsEmpty](cref:Meziantou.Framework.FullPath.IsEmpty) before you
  combine a path that can be empty.
- [MakePathRelativeTo](cref:Meziantou.Framework.FullPath.MakePathRelativeTo) and [IsChildOf](cref:Meziantou.Framework.FullPath.IsChildOf) throw an `InvalidOperationException` on an empty path.
