---
title: Matching paths
description: Check whether a path matches a pattern, and whether a directory can hold a path that matches it.
---

## Match a path

`IsMatch` checks whether a path matches the pattern. Pass the whole path, or the directory and the file name
separately to avoid concatenating them:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_IsMatchDirectoryAndFileName;format=md-fenced-code-block;dedent=true -->
```csharp
var glob = Glob.Parse("src/**/*.cs", GlobDialect.Standard);

// The whole relative path
Assert.True(glob.IsMatch("src/App/Program.cs"));

// The directory and the file name, without concatenating them
Assert.True(glob.IsMatch("src/App", "Program.cs"));

// The kind of item, when the path alone does not say it
Assert.False(glob.IsMatch("src/App", "Folder.cs", PathItemType.Directory));
```
<!-- endref -->

- The overloads take a `string` or a `ReadOnlySpan<char>`.
- Without a `PathItemType`, a path that ends with a separator is a directory, and any other path is a file. A pattern
  matches files or directories as described in [Files and directories](03-PatternSyntax.md#files-and-directories).
- `IsMatch` only reads the text of the path: the file does not have to exist.

## Use relative paths

A pattern is matched against the path from the start: `src/*.cs` matches `src/Program.cs`, not
`/home/me/project/src/Program.cs`. Make the path relative to the directory the pattern is written for, for instance
with `Path.GetRelativePath`, before you match it.

In the path, `/` is a separator on every operating system, and `\` is one too on Windows. A pattern always uses `/`,
except in the MSBuild dialect, which accepts both.

## Skip the directories that cannot match

`IsPartialMatch` tells whether a directory can hold a path that matches the pattern. Use it to skip a whole directory
when you walk a tree yourself:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_IsPartialMatch;format=md-fenced-code-block;dedent=true -->
```csharp
var glob = Glob.Parse("src/**/*.cs", GlobDialect.Standard);

// A file below 'src/App' can match the pattern
Assert.True(glob.IsPartialMatch("src/App"));

// No file below 'docs' can match it
Assert.False(glob.IsPartialMatch("docs"));
```
<!-- endref -->

`IsPartialMatch` returns `true` when it cannot rule the directory out: a `true` means "look inside", not "something
matches". [Enumerating files](06-EnumeratingFiles.md) calls it for you.

## Match patterns together

`Glob` and `GlobCollection` both implement `IGlobEvaluatable`, so `IsMatch` and `IsPartialMatch` work the same on a
single pattern and on a collection of patterns. See [Combining patterns](07-CombiningPatterns.md).
