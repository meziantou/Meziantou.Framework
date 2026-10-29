---
title: Pattern syntax
description: The wildcards of a glob pattern, and how files, directories, hidden names, escapes and invalid patterns are handled.
---

This page describes the syntax of `GlobDialect.Standard`. The other dialects change some of these rules: see
[Dialects](04-Dialects.md).

## Wildcards

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_Wildcards;format=md-fenced-code-block;dedent=true -->
```csharp
// '*' matches any characters in one path segment, but not a '/'
Assert.True(Glob.Parse("*.txt", GlobDialect.Standard).IsMatch("readme.txt"));
Assert.False(Glob.Parse("*.txt", GlobDialect.Standard).IsMatch("docs/readme.txt"));

// '?' matches one character, and brackets one character of a set or a range
Assert.True(Glob.Parse("file?.txt", GlobDialect.Standard).IsMatch("file1.txt"));
Assert.True(Glob.Parse("file[0-9].txt", GlobDialect.Standard).IsMatch("file7.txt"));
Assert.False(Glob.Parse("file[!0-9].txt", GlobDialect.Standard).IsMatch("file7.txt"));

// Braces match one of the literals
Assert.True(Glob.Parse("*.{png,jpg}", GlobDialect.Standard).IsMatch("photo.jpg"));

// '**' matches zero or more directories
Assert.True(Glob.Parse("**/*.txt", GlobDialect.Standard).IsMatch("readme.txt"));
Assert.True(Glob.Parse("**/*.txt", GlobDialect.Standard).IsMatch("docs/guides/readme.txt"));
```
<!-- endref -->

| Syntax | Matches |
| --- | --- |
| `*` | Any number of characters, including none, in one path segment. It never matches a `/`. |
| `?` | One character. |
| `[abc]` | One of the characters in the brackets. |
| `[a-z]` | One character of the range. |
| `[!abc]`, `[!a-z]` | One character that is not in the brackets. |
| `{abc,123}` | One of the literals separated by commas. |
| `**` | Zero or more directories, when it makes a whole path segment, as in `src/**/*.cs`. |
| `!pattern` | The paths that the pattern matches, to exclude them. See [Combining patterns](07-CombiningPatterns.md). |
| `\x` | The character `x` itself. |

A `**` at the end of a pattern matches at least one segment: `src/**` matches everything inside `src`, but not `src`
itself.

## Files and directories

A pattern matches files only, unless it ends with a `/`: then it matches directories only.

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_FilesAndDirectories;format=md-fenced-code-block;dedent=true -->
```csharp
// A pattern matches files only
var files = Glob.Parse("src/*", GlobDialect.Standard);
Assert.True(files.IsMatch("src", "Program.cs", PathItemType.File));
Assert.False(files.IsMatch("src", "App", PathItemType.Directory));

// A pattern that ends with a '/' matches directories only
var directories = Glob.Parse("src/*/", GlobDialect.Standard);
Assert.True(directories.IsMatch("src", "App", PathItemType.Directory));
Assert.False(directories.IsMatch("src", "Program.cs", PathItemType.File));

// A path that ends with a separator is a directory
Assert.True(directories.IsMatch("src/App/"));
```
<!-- endref -->

When you match a path, say what it is with a `PathItemType`, or end the path with a separator for a directory. A path
without a trailing separator and without a `PathItemType` is a file. [Enumerating files](06-EnumeratingFiles.md) passes
the right kind for each entry.

## Names that start with a dot

On Linux and macOS, a name that starts with a dot, such as `.git` or `.vscode`, is hidden. The wildcards `*`, `?`,
`[...]` and `**` do not match such a name, unless the pattern writes the dot or you set
`GlobOptions.MatchLeadingDot`:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_LeadingDot;format=md-fenced-code-block;dedent=true -->
```csharp
// Wildcards do not match a name that starts with a dot
Assert.False(Glob.Parse("**/*.json", GlobDialect.Standard).IsMatch(".vscode/settings.json"));

// Unless you write the dot in the pattern...
Assert.True(Glob.Parse(".vscode/*.json", GlobDialect.Standard).IsMatch(".vscode/settings.json"));

// ...or set the MatchLeadingDot option
Assert.True(Glob.Parse("**/*.json", GlobDialect.Standard, GlobOptions.MatchLeadingDot).IsMatch(".vscode/settings.json"));
```
<!-- endref -->

Only the standard dialect reads `MatchLeadingDot`: the other dialects always match a leading dot, as git, MSBuild and
`fnmatch` do.

## Ignore the case

`GlobOptions.IgnoreCase` compares the characters as `StringComparison.OrdinalIgnoreCase` does. In a range, a character
matches when it, its lowercase form or its uppercase form is in the range: `[a-z]` matches `Q`. Combine the options
with `|`: `GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot`.

## Escape a special character

A `\` makes the next character an ordinary character. Use it for a name that contains a wildcard, or that starts with a
`!`:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_Escape;format=md-fenced-code-block;dedent=true -->
```csharp
// '\' makes the next character an ordinary character
var glob = Glob.Parse(@"report\*.txt", GlobDialect.Standard);
Assert.True(glob.IsMatch("report*.txt"));
Assert.False(glob.IsMatch("report2024.txt"));

// A leading '!' negates the pattern; escape it to match a name that starts with '!'
Assert.Equal(GlobMode.Exclude, Glob.Parse("!*.log", GlobDialect.Standard).Mode);
Assert.True(Glob.Parse(@"\!readme.txt", GlobDialect.Standard).IsMatch("!readme.txt"));
```
<!-- endref -->

`\` is an escape character in a pattern, not a separator: write `src/*.cs`, not `src\*.cs`, even on Windows. The
MSBuild dialect is the exception: it reads both as separators.

## `.` and `..`

The segments `.` and `..` are resolved when the pattern is parsed: `a/./b` is `a/b`, and `a/../b` is `b`. A pattern
cannot start with `..`, and a `..` cannot follow a `**`.

## Invalid patterns

`Glob.Parse` throws an `ArgumentException` when the pattern is invalid, for instance when a bracket or a brace is not
closed, or a range is reversed (`[z-a]`). Use `Glob.TryParse` for a pattern that comes from a user or a configuration
file:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
// The bracket expression is not closed
Assert.False(Glob.TryParse("src/[abc", GlobDialect.Standard, GlobOptions.None, out var glob));
Assert.Null(glob);

// Parse throws instead
Assert.Throws<ArgumentException>("pattern", () => Glob.Parse("src/[abc", GlobDialect.Standard));
```
<!-- endref -->

`ToString` returns a pattern that parses back, with the same dialect and options, into a glob that matches the same
paths. It is not always the text you parsed.

## Patterns from untrusted sources

Matching backtracks: the cost of matching one path grows exponentially with the number of `*` in one path segment of
the pattern. Ordinary patterns are not affected, but someone can write a pattern that is slow on purpose. Limit the
length and the number of wildcards of a pattern that comes from an untrusted source before you parse it.
