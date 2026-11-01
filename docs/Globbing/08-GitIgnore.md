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

## A .gitignore file in a subdirectory

The patterns of a `.gitignore` file apply to the content of its directory, relative to that directory. Pass the
directory to [ParseGitIgnore](cref:Meziantou.Framework.Globbing.GlobCollection.ParseGitIgnore) or [LoadGitIgnoreAsync](cref:Meziantou.Framework.Globbing.GlobCollection.LoadGitIgnoreAsync), relative to the root of the repository, and match the
collection with paths relative to the root:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobCollectionTests.cs;region=GlobCollection_ParseGitIgnoreWithBaseDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
// The content of src/App/.gitignore
var gitIgnore = GlobCollection.ParseGitIgnore("""
    /generated/
    *.tmp
    """, baseDirectory: "src/App");

// Paths are relative to the root of the repository
Assert.True(gitIgnore.IsMatch("src/App/generated/Model.cs"));
Assert.True(gitIgnore.IsMatch("src/App/Data/cache.tmp"));
Assert.False(gitIgnore.IsMatch("generated/Model.cs"));
Assert.False(gitIgnore.IsMatch("src/Lib/cache.tmp"));
```
<!-- endref -->

The collection does not match the paths outside its directory, nor the directory itself.

## Use every ignore file of a repository

git reads more than one file: the `.gitignore` file of each directory, the `info/exclude` file of the repository, and
a global excludes file. [GitIgnoreMatcher](cref:Meziantou.Framework.Globbing.GitIgnoreMatcher) reads them all for a working tree. [IsIgnored](cref:Meziantou.Framework.Globbing.GitIgnoreMatcher.IsIgnored) tells whether a path is ignored, and
[EnumerateFiles](cref:Meziantou.Framework.Globbing.GitIgnoreMatcher.EnumerateFiles) lists the files that are not:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GitIgnoreMatcherTests.cs;region=GitIgnoreMatcher_EnumerateFiles;format=md-fenced-code-block;dedent=true -->
```csharp
using var rootDirectory = TemporaryDirectory.Create();
rootDirectory.CreateTextFile(".gitignore", "*.log\n");
rootDirectory.CreateTextFile("src/.gitignore", "bin/\n!important.log\n");
rootDirectory.CreateEmptyFile("src/Program.cs");
rootDirectory.CreateEmptyFile("src/bin/App.dll");
rootDirectory.CreateEmptyFile("src/trace.log");
rootDirectory.CreateEmptyFile("src/important.log");

var matcher = new GitIgnoreMatcher(rootDirectory.FullPath);

// The .gitignore file of a directory applies to its content, and takes precedence over the parent ones
Assert.True(matcher.IsIgnored("src/trace.log"));
Assert.False(matcher.IsIgnored("src/important.log"));

// Enumerate the files that are not ignored. The enumeration does not go into src/bin.
var files = matcher.EnumerateFiles()
    .Select(path => Path.GetRelativePath(rootDirectory.FullPath, path).Replace('\\', '/'))
    .Order(StringComparer.Ordinal)
    .ToList();

Assert.Equal([".gitignore", "src/.gitignore", "src/Program.cs", "src/important.log"], files);
```
<!-- endref -->

- Paths are relative to the root directory you give. [IsIgnored](cref:Meziantou.Framework.Globbing.GitIgnoreMatcher.IsIgnored) reads the file system to know whether a path is a
  directory, unless the path ends with a `/` or you pass a [PathItemType](cref:Meziantou.Framework.Globbing.PathItemType).
- The patterns follow the precedence of git: the `.gitignore` file of a directory takes precedence over the ones of
  its parent directories, then come `info/exclude` and the global excludes file. Within a file, the last pattern that
  matches decides.
- A `.gitignore` file is read the first time a path of its directory is evaluated, then kept for the lifetime of the
  matcher. The `.gitignore` files inside an ignored directory are not read, as git does not read them.
- [EnumerateFiles](cref:Meziantou.Framework.Globbing.GitIgnoreMatcher.EnumerateFiles) returns full paths. It does not go into the ignored directories nor into `.git`, includes the
  hidden files, and returns a symbolic link to a directory as a file without following it.
- Only the ignore rules are evaluated: a file that git tracks although a pattern ignores it is still reported as
  ignored.

[GitIgnoreMatcherOptions](cref:Meziantou.Framework.Globbing.GitIgnoreMatcherOptions) sets the other sources:

- [UseRepositoryExcludeFile](cref:Meziantou.Framework.Globbing.GitIgnoreMatcherOptions.UseRepositoryExcludeFile) reads `.git/info/exclude` (`true` by default). In a worktree, where `.git` is a file, the file of the
  main repository is read.
- [GlobalExcludesFile](cref:Meziantou.Framework.Globbing.GitIgnoreMatcherOptions.GlobalExcludesFile) is the path of the global excludes file. The matcher does not read the git configuration, so
  set it to the value of `core.excludesFile`. When that setting is not set, git reads `$XDG_CONFIG_HOME/git/ignore`, or
  `~/.config/git/ignore`.

## Use a collection with System.IO.Enumeration

To list the files that one collection does not ignore, match the entries of a `FileSystemEnumerable<T>` against the
collection. Rejecting the ignored directories in `ShouldRecursePredicate` also keeps the enumeration out of them:

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

[EnumerateFiles](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFiles) on the collection itself returns the opposite: the paths that it ignores, including everything below an
ignored directory.

## Performance

Matching a path only evaluates the patterns that can match its name: the entries that are a name (`node_modules`,
`bin/`) or an extension (`*.log`) are looked up directly, and the other entries are evaluated one by one. When an
enumeration goes through a directory, the directory is evaluated once for all its entries, instead of evaluating
every parent directory again for each path.
