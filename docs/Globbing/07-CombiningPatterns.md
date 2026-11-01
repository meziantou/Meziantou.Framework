---
title: Combining patterns
description: Include some paths and exclude others with a GlobCollection, and enumerate the files that the collection matches.
---

## Include and exclude paths

A [GlobCollection](cref:Meziantou.Framework.Globbing.GlobCollection) matches a path when at least one of its patterns includes it and none excludes it. A pattern that
starts with a `!` is an exclude pattern:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_IncludeExclude;format=md-fenced-code-block;dedent=true -->
```csharp
GlobCollection globs =
[
    Glob.Parse("**/*.cs", GlobDialect.Standard),
    Glob.Parse("!**/obj/**", GlobDialect.Standard),
];

Assert.True(globs.IsMatch("src/Program.cs"));
Assert.False(globs.IsMatch("src/obj/Debug/AssemblyInfo.cs"));
Assert.False(globs.IsMatch("readme.md"));
```
<!-- endref -->

- Create the collection with a collection expression, as above, or with `new GlobCollection(glob1, glob2)`.
- The [Mode](cref:Meziantou.Framework.Globbing.Glob.Mode) property of a [Glob](cref:Meziantou.Framework.Globbing.Glob) tells whether it includes ([GlobMode.Include](cref:Meziantou.Framework.Globbing.GlobMode.Include)) or excludes ([GlobMode.Exclude](cref:Meziantou.Framework.Globbing.GlobMode.Exclude))
  paths. On its own, an exclude pattern still matches the paths it describes: the exclusion happens in the collection.
- A collection can hold other collections, since both implement [IGlobEvaluatable](cref:Meziantou.Framework.Globbing.IGlobEvaluatable).

## Exclude patterns always win

The order of the patterns does not matter: a path that matches an exclude pattern is excluded, even when an include
pattern written after it matches the path too.

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_ExcludeWins;format=md-fenced-code-block;dedent=true -->
```csharp
// The order does not matter: a path that matches an exclude pattern is excluded
GlobCollection globs =
[
    Glob.Parse("!*.log", GlobDialect.Standard),
    Glob.Parse("*", GlobDialect.Standard),
    Glob.Parse("important.log", GlobDialect.Standard),
];

Assert.True(globs.IsMatch("readme.md"));
Assert.False(globs.IsMatch("important.log"));
```
<!-- endref -->

To re-include a path, as `!` does in a `.gitignore` file, use a collection created from gitignore content: its last
matching pattern decides. See [Reading .gitignore files](08-GitIgnore.md).

## Exclude the content of a directory

In the standard dialect, a pattern that ends with a `/` matches the directory only. Excluding it does not exclude the
files it holds: exclude `dir/**` instead.

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_ExcludeDirectoryContent;format=md-fenced-code-block;dedent=true -->
```csharp
// '!obj/' excludes the directory itself, not the files it holds
GlobCollection directoryOnly = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!obj/", GlobDialect.Standard)];
Assert.True(directoryOnly.IsMatch("obj/project.assets.json"));

// '!obj/**' excludes everything below the directory
GlobCollection content = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!obj/**", GlobDialect.Standard)];
Assert.False(content.IsMatch("obj/project.assets.json"));
```
<!-- endref -->

`obj/` and `obj/**` only match at the root. Write `**/obj/**` to exclude every `obj` directory, at any depth.

## Enumerate the files of a collection

[EnumerateFiles](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFiles) and [EnumerateFileSystemEntries](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFileSystemEntries) work on a collection as on a single pattern:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_EnumerateFilesWithExclusions;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateEmptyFile("src/App/Program.cs");
rootDirectory.CreateEmptyFile("src/App/obj/Debug/AssemblyInfo.cs");
rootDirectory.CreateEmptyFile("tests/AppTests.cs");

GlobCollection globs =
[
    Glob.Parse("src/**/*.cs", GlobDialect.Standard),
    Glob.Parse("!**/obj/**", GlobDialect.Standard),
];

var files = globs.EnumerateFiles(rootDirectory.FullPath).ToList();
Assert.Equal([Path.Combine(rootDirectory.FullPath, "src", "App", "Program.cs")], files);
```
<!-- endref -->

The enumeration does not go into a directory when an exclude pattern matches everything below it, such as
`!**/node_modules/**` or `!**/bin/**/*`, so a large excluded tree is not read. A pattern that only excludes some of the
files of a directory, such as `!**/*.log`, does not stop the enumeration from reading the directory.

In the standard dialect, `**` does not match a name that starts with a dot, so `!**/node_modules/**` does not exclude
`node_modules/.bin/tool`. The directory is still skipped when the include patterns cannot match such a name either,
which is the case unless they use [GlobOptions.MatchLeadingDot](cref:Meziantou.Framework.Globbing.GlobOptions.MatchLeadingDot).

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_ExcludedDirectoriesAreNotVisited;format=md-fenced-code-block;dedent=true -->
```csharp
GlobCollection globs =
[
    Glob.Parse("**/*", GlobDialect.Standard),
    Glob.Parse("!**/node_modules/**", GlobDialect.Standard),
];

// The exclude pattern matches the whole content of node_modules: the enumeration does not go into it
Assert.False(globs.IsPartialMatch("src/node_modules"));
Assert.True(globs.IsPartialMatch("src/app"));
```
<!-- endref -->
