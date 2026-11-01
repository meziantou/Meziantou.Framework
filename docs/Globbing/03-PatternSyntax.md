---
title: Pattern syntax
description: The wildcards of a glob pattern, and how files, directories, hidden names, escapes and invalid patterns are handled.
---

This page describes the syntax of [GlobDialect.Standard](cref:Meziantou.Framework.Globbing.GlobDialect.Standard). The other dialects change some of these rules: see
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

// Braces match one of the alternatives
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
| `{abc,*.cs}` | One of the alternatives separated by commas. See [Braces](#braces). |
| `**` | Zero or more directories, when it makes a whole path segment, as in `src/**/*.cs`. |
| `!pattern` | The paths that the pattern matches, to exclude them. See [Combining patterns](07-CombiningPatterns.md). |
| `\x` | The character `x` itself. |

A `**` at the end of a pattern matches at least one segment: `src/**` matches everything inside `src`, but not `src`
itself.

## Braces

An alternative of a brace expression is a pattern of its own: it can hold `*`, `?`, `[...]` and other braces. It can
also hold a `/` or a `**`: the pattern is then read once per alternative, as if you had written several patterns.

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_Braces;format=md-fenced-code-block;dedent=true -->
```csharp
// An alternative can hold wildcards and other braces
var projects = Glob.Parse("*.{cs,*proj,{props,targets}}", GlobDialect.Standard);
Assert.True(projects.IsMatch("App.csproj"));
Assert.True(projects.IsMatch("Directory.Build.props"));
Assert.False(projects.IsMatch("readme.md"));

// An alternative can hold a '/' or a '**'
var sources = Glob.Parse("{src/**,tests}/*.cs", GlobDialect.Standard);
Assert.True(sources.IsMatch("src/App/Program.cs"));
Assert.True(sources.IsMatch("tests/AppTests.cs"));
Assert.False(sources.IsMatch("tests/unit/AppTests.cs"));
```
<!-- endref -->

Inside the braces, escape a `,`, a `}` or a wildcard with a `\` to match it as an ordinary character: `{a\,b,c}` matches
`a,b` or `c`. A `**` is a recursive wildcard when it makes a whole path segment of the pattern as written or of one
alternative, as in `{**/bin,obj}`. An alternative cannot make a path segment empty: `src/{,a}` matches `src/a` only.
Write `src{,/a}` to match `src` too.

A brace expression that holds a `/` or a `**` expands into one pattern per alternative, and expansions multiply:
[Glob.Parse](cref:Meziantou.Framework.Globbing.Glob.Parse) rejects a pattern that expands into more than 1,024 patterns.

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

When you match a path, say what it is with a [PathItemType](cref:Meziantou.Framework.Globbing.PathItemType), or end the path with a separator for a directory. A path
without a trailing separator and without a [PathItemType](cref:Meziantou.Framework.Globbing.PathItemType) is a file. [Enumerating files](06-EnumeratingFiles.md) passes
the right kind for each entry.

## Absolute paths

A pattern that starts with a `/` is absolute: it matches absolute paths only, and a pattern without it matches
relative paths only.

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_AbsolutePath;format=md-fenced-code-block;dedent=true -->
```csharp
var glob = Glob.Parse("/home/*.txt", GlobDialect.Standard);
Assert.True(glob.IsMatch("/home/notes.txt"));
Assert.False(glob.IsMatch("home/notes.txt"));
```
<!-- endref -->

[Enumerating files](06-EnumeratingFiles.md) with an absolute pattern lists the files of the directory the pattern
names, wherever the directory you enumerate is.

## Names that start with a dot

On Linux and macOS, a name that starts with a dot, such as `.git` or `.vscode`, is hidden. The wildcards `*`, `?`,
`[...]` and `**` do not match such a name, unless the pattern writes the dot or you set
[GlobOptions.MatchLeadingDot](cref:Meziantou.Framework.Globbing.GlobOptions.MatchLeadingDot):

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

Only the standard dialect reads [MatchLeadingDot](cref:Meziantou.Framework.Globbing.GlobOptions.MatchLeadingDot): the other dialects always match a leading dot, as git, MSBuild and
`fnmatch` do.

## Ignore the case

[GlobOptions.IgnoreCase](cref:Meziantou.Framework.Globbing.GlobOptions.IgnoreCase) compares the characters as `StringComparison.OrdinalIgnoreCase` does. In a range, a character
matches when it is equal, ignoring the case, to one of the characters of the range: `[a-z]` matches `Q`. As
`StringComparison.OrdinalIgnoreCase` does, a non-ASCII character is never equal to an ASCII one: `[r-s]` does not match
`ſ` (U+017F), and `[j-k]` does not match the Kelvin sign `K` (U+212A). Combine the options with `|`:
`GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot`.

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

## Build a pattern from a name

To use a file or folder name in a pattern, escape it with [Glob.Escape](cref:Meziantou.Framework.Globbing.Glob.Escape) instead of concatenating it as is. A name
that holds a `*`, a `[` or a leading `!` would otherwise change the meaning of the pattern:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_EscapeText;format=md-fenced-code-block;dedent=true -->
```csharp
// Build a pattern from a folder name that holds special characters
var pattern = Glob.Escape("Reports [2024]", GlobDialect.Standard) + "/**/*.pdf";
Assert.Equal(@"Reports \[2024]/**/*.pdf", pattern);

var glob = Glob.Parse(pattern, GlobDialect.Standard);
Assert.True(glob.IsMatch("Reports [2024]/Q1/summary.pdf"));
Assert.False(glob.IsMatch("Reports 2/Q1/summary.pdf"));

// Each dialect has its own escaping rules
Assert.Equal("100%25 done.txt", Glob.Escape("100% done.txt", GlobDialect.MSBuild));
Assert.Equal(@"\#notes.txt", Glob.Escape("#notes.txt", GlobDialect.Git));
```
<!-- endref -->

- The result matches exactly the text, and can be combined with other parts of a pattern.
- Each dialect has its own rules: a `\` before the character, or `%XX` for MSBuild, which also escapes the characters
  that have a meaning in a project file (`;`, `$`, `@`, `'`, `(` and `)`). The Git dialect also escapes a leading `#`
  and a trailing space, so the result can be written in a `.gitignore` file.
- A path separator stays a separator: `/`, and `\` on Windows. The text `src/file*.cs` gives the pattern `src/file\*.cs`.
- A Git pattern without a `/` matches the name at any depth. Prepend a `/` to the result to anchor it to the root.

## `.` and `..`

The segments `.` and `..` are resolved when the pattern is parsed: `a/./b` is `a/b`, and `a/../b` is `b`. A pattern
cannot start with `..`, and a `..` cannot follow a `**`.

## Invalid patterns

[Glob.Parse](cref:Meziantou.Framework.Globbing.Glob.Parse) throws an `ArgumentException` when the pattern is invalid, for instance when a bracket or a brace is not
closed, or a range is reversed (`[z-a]`). Use [Glob.TryParse](cref:Meziantou.Framework.Globbing.Glob.TryParse) for a pattern that comes from a user or a configuration
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

The cost of matching one path grows polynomially with the length of the pattern and of the path, whatever the number
of wildcards, so a pattern cannot be made exponentially slow on purpose. Still limit the length of a pattern that comes
from an untrusted source, as a long pattern matched against long paths costs more.
