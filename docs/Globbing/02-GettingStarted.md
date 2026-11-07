---
title: Getting started
description: Install the package, check whether a path matches a pattern, and enumerate the files that match.
---

## Install the package

Add the `Meziantou.Framework.Globbing` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Globbing" />

The examples of this documentation omit the `using` directives. They need `using Meziantou.Framework.Globbing;`. They
check their results with assertions such as `Assert.True`, which show what each call returns.

## Check whether a path matches

Parse the pattern once, then test paths against it:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_IsMatch;format=md-fenced-code-block;dedent=true -->
```csharp
var glob = Glob.Parse("src/**/*.txt", GlobDialect.Standard);
Assert.True(glob.IsMatch("src/folder/file.txt"));
Assert.False(glob.IsMatch("docs/file.txt"));
```
<!-- endref -->

- [Glob.Parse](cref:Meziantou.Framework.Globbing.Glob.Parse) takes the pattern and its dialect. [GlobDialect.Standard](cref:Meziantou.Framework.Globbing.GlobDialect.Standard) is the syntax described in
  [Pattern syntax](03-PatternSyntax.md); the others follow git, MSBuild and `fnmatch`.
- `src/**/*.txt` matches the `.txt` files of `src` and of all its subdirectories. `**` matches zero or more
  directories, and `*` matches any characters of one file name.
- Paths use `/` as a separator. On Windows, `\` is a separator too. A pattern that starts with a `/` matches absolute
  paths, and the others match relative paths.

## Ignore the case

Matching is case-sensitive by default. Pass [GlobOptions.IgnoreCase](cref:Meziantou.Framework.Globbing.GlobOptions.IgnoreCase) to ignore the case:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_IsMatchIgnoreCase;format=md-fenced-code-block;dedent=true -->
```csharp
Glob glob = Glob.Parse("src/**/*.txt", GlobDialect.Standard, GlobOptions.IgnoreCase);
Assert.True(glob.IsMatch("src/abc.txt"));
Assert.True(glob.IsMatch("SRC/a/b/abc.TXT"));
Assert.False(glob.IsMatch("src/abc.png"));
```
<!-- endref -->

## Enumerate the files that match

[EnumerateFiles](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFiles) lists the files of a directory that match the pattern, and returns their full paths. The pattern is
matched against the path of each file relative to that directory:

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

The example creates the files in a temporary directory with the
<NuGetReference Id="Meziantou.Framework.TemporaryDirectory" />
package. Only the directories that can hold a match are visited: here, the enumeration does not look inside `docs`.

## Next steps

- [Pattern syntax](03-PatternSyntax.md): every wildcard, and how files, directories and hidden names are matched.
- [Dialects](04-Dialects.md): read patterns written for git, MSBuild or `fnmatch`.
- [Enumerating files](06-EnumeratingFiles.md) and [Combining patterns](07-CombiningPatterns.md).
