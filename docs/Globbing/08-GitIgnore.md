---
title: Reading .gitignore files
description: Load a .gitignore file, check which paths it ignores, and list the files that git does not ignore.
---

## Check whether a path is ignored

[GlobCollection.ParseGitIgnore](cref:Meziantou.Framework.Globbing.GlobCollection.ParseGitIgnore) reads the content of a `.gitignore` file. [IsMatch](cref:Meziantou.Framework.Globbing.GlobCollection.IsMatch) then returns `true` for the paths
that git ignores:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_ParseGitIgnore;format=md-fenced-code-block;dedent=true -->
```csharp
var gitIgnore = GlobCollection.ParseGitIgnore("""
    # Build output
    bin/
    obj/
    *.log
    !important.log
    """);

// IsMatch returns true when the path is ignored
Assert.True(gitIgnore.IsMatch("src/App/bin/Debug/App.dll"));
Assert.True(gitIgnore.IsMatch("trace.log"));
Assert.False(gitIgnore.IsMatch("important.log"));
Assert.False(gitIgnore.IsMatch("src/App/Program.cs"));
```
<!-- endref -->

The content is read as git reads it:

- Empty lines and lines that start with `#` are skipped. Write `\#` for a name that starts with a `#`.
- Trailing spaces are removed, unless they are escaped with a `\`. Trailing tabs are kept.
- Lines end with a line feed; the carriage return of a CRLF is removed.
- An entry that can never match, such as one with an unclosed bracket, is skipped instead of making the whole file
  invalid.
- Each entry is parsed in the [Git dialect](04-Dialects.md#git).

## Load a file

[LoadGitIgnoreAsync](cref:Meziantou.Framework.Globbing.GlobCollection.LoadGitIgnoreAsync) reads a file, a `Stream` or a `TextReader`:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_LoadGitIgnoreAsync;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
var path = rootDirectory.CreateTextFile(".gitignore", "bin/\nobj/\n");

var gitIgnore = await GlobCollection.LoadGitIgnoreAsync(path);

Assert.True(gitIgnore.IsMatch("src/App/obj/project.assets.json"));
```
<!-- endref -->

A file or a stream is decoded as UTF-8, unless it starts with the byte order mark of UTF-16 or UTF-32.

## How git resolves a path

A collection created from gitignore content follows the rules of git, not the ones of
[a collection you build](07-CombiningPatterns.md):

- The last pattern that matches a path decides. A `!` entry re-includes the paths that the entries above it ignore.
- A path is ignored when one of its parent directories is ignored, and no entry can re-include it.

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_GitIgnoreRules;format=md-fenced-code-block;dedent=true -->
```csharp
// The last pattern that matches a path decides
var lastMatchWins = GlobCollection.ParseGitIgnore("""
    *.log
    !important.log
    """);
Assert.False(lastMatchWins.IsMatch("important.log"));

// A file cannot be re-included when its directory is ignored
var directoryIgnored = GlobCollection.ParseGitIgnore("""
    bin/
    !bin/keep.txt
    """);
Assert.True(directoryIgnored.IsMatch("bin/keep.txt"));

// Ignore the content of the directory instead of the directory itself
var contentIgnored = GlobCollection.ParseGitIgnore("""
    /bin/*
    !/bin/keep.txt
    """);
Assert.False(contentIgnored.IsMatch("bin/keep.txt"));
Assert.True(contentIgnored.IsMatch("bin/App.dll"));
```
<!-- endref -->

To keep one file of an ignored directory, ignore the content of the directory (`/bin/*`) instead of the directory
itself (`bin/`), then re-include the file.

## List the files that are not ignored

Match the entries of a `FileSystemEnumerable<T>` against the collection. Rejecting the ignored directories in
`ShouldRecursePredicate` also keeps the enumeration out of them:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_EnumerateFilesNotIgnored;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateEmptyFile("src/Program.cs");
rootDirectory.CreateEmptyFile("src/bin/App.dll");
rootDirectory.CreateEmptyFile("trace.log");

var gitIgnore = GlobCollection.ParseGitIgnore("""
    bin/
    *.log
    """);

// Skip the ignored files, and do not visit the ignored directories
var files = new FileSystemEnumerable<string>(
    rootDirectory.FullPath,
    (ref FileSystemEntry entry) => entry.ToFullPath(),
    new EnumerationOptions { RecurseSubdirectories = true })
{
    ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && !gitIgnore.IsMatch(ref entry),
    ShouldRecursePredicate = (ref FileSystemEntry entry) => !gitIgnore.IsMatch(ref entry),
};

Assert.Equal([Path.Combine(rootDirectory.FullPath, "src", "Program.cs")], files.ToList());
```
<!-- endref -->

## What is not covered

A collection holds the entries of the content you give it. Match it with paths relative to the directory of that
`.gitignore` file. It does not read the `.gitignore` files of the subdirectories, `.git/info/exclude`, or the global
excludes file of git.
