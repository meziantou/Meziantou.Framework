---
title: Enumerating files
description: List the files or the directories that match a pattern, set the enumeration options, and plug a pattern into System.IO.Enumeration.
---

## Enumerate files

[EnumerateFiles](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFiles) lists the files of a directory that match the pattern, and returns their full paths:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_EnumerateFiles;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateEmptyFile("src/a/abc.txt");
rootDirectory.CreateEmptyFile("src/a/abc.png");
rootDirectory.CreateEmptyFile("docs/readme.txt");

// Enumerate files that match the glob in the folder rootDirectory
Glob glob = Glob.Parse("src/**/*.txt", GlobDialect.Standard);
var files = new List<string>();
foreach (var file in glob.EnumerateFiles(rootDirectory.FullPath))
{
    files.Add(file);
}

Assert.Equal([Path.Combine(rootDirectory.FullPath, "src", "a", "abc.txt")], files);
```
<!-- endref -->

- The pattern is matched against the path of each file relative to the directory you pass.
- The enumeration only goes into the directories that can hold a match, as [IsPartialMatch](cref:Meziantou.Framework.Globbing.Glob.IsPartialMatch) decides. A pattern such as
  `**/*.txt` visits the whole tree; `src/*.txt` only goes into `src`.
- The results are returned as they are found, in the order of the file system. Sort them if you need a stable order.
- A pattern that only matches directories, such as a standard pattern that ends with a `/`, returns no file.

## Enumerate directories

[EnumerateFileSystemEntries](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFileSystemEntries) returns the files and the directories that match. With a pattern that ends with a `/`,
it returns directories only:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_EnumerateFileSystemEntries;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateEmptyFile("src/App/bin/App.dll");
rootDirectory.CreateEmptyFile("src/Lib/bin/Lib.dll");

// The pattern ends with a '/': it matches directories only
var glob = Glob.Parse("**/bin/", GlobDialect.Standard);
var directories = glob.EnumerateFileSystemEntries(rootDirectory.FullPath).Order(StringComparer.Ordinal).ToList();

Assert.Equal(
    [
        Path.Combine(rootDirectory.FullPath, "src", "App", "bin"),
        Path.Combine(rootDirectory.FullPath, "src", "Lib", "bin"),
    ],
    directories);
```
<!-- endref -->

## Set the enumeration options

Both methods take an optional `EnumerationOptions`. Without it, the enumeration:

- goes into subdirectories when the pattern can match below the first level,
- skips the directories it cannot access,
- skips hidden and system entries, unless the pattern can match a name that starts with a dot. On Linux and macOS,
  .NET reports such names as hidden.

The options you pass replace these defaults. Set `RecurseSubdirectories` to `true` for a pattern that goes into
subdirectories, and `AttributesToSkip` to `0` to include the hidden entries:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_EnumerateFilesWithOptions;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateEmptyFile("logs/app.log");
rootDirectory.CreateEmptyFile("logs/2024/app.log");

// The options replace the default ones: set RecurseSubdirectories to visit subdirectories
var options = new EnumerationOptions
{
    RecurseSubdirectories = true,
    MaxRecursionDepth = 1,
};

var glob = Glob.Parse("**/*.log", GlobDialect.Standard);
var files = glob.EnumerateFiles(rootDirectory.FullPath, options).ToList();

Assert.Equal([Path.Combine(rootDirectory.FullPath, "logs", "app.log")], files);
```
<!-- endref -->

`MaxRecursionDepth = 1` stops the enumeration one level below the root, so `logs/2024/app.log` is not returned.

## Use a pattern with System.IO.Enumeration

To return something else than a path, or to add your own conditions, use a `FileSystemEnumerable<T>` (namespace
`System.IO.Enumeration`) and call the [IsMatch](cref:Meziantou.Framework.Globbing.Glob.IsMatch) and [IsPartialMatch](cref:Meziantou.Framework.Globbing.Glob.IsPartialMatch) overloads that take a `FileSystemEntry`:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_FileSystemEnumerable;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateEmptyFile("docs/readme.txt");
rootDirectory.CreateEmptyFile("src/Program.cs");

var glob = Glob.Parse("docs/**/*.txt", GlobDialect.Standard);

// Return the name and the length of each file, and only visit the directories that can hold a match
var enumerable = new FileSystemEnumerable<(string Name, long Length)>(
    rootDirectory.FullPath,
    (ref FileSystemEntry entry) => (entry.FileName.ToString(), entry.Length),
    new EnumerationOptions { RecurseSubdirectories = true })
{
    ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && glob.IsMatch(ref entry),
    ShouldRecursePredicate = (ref FileSystemEntry entry) => glob.IsPartialMatch(ref entry),
};

Assert.Equal([("readme.txt", 0L)], enumerable.ToList());
```
<!-- endref -->

These overloads match the path of the entry relative to the root directory of the enumeration, and [IsMatch](cref:Meziantou.Framework.Globbing.Glob.IsMatch) tells
the pattern whether the entry is a file or a directory. To write a reusable enumerator instead, derive from [GlobFileSystemEnumerator\<T\>](cref:Meziantou.Framework.Globbing.GlobFileSystemEnumerator{T}) and override
`TransformEntry`: it already includes the entries that match and skips the directories that cannot.
