---
title: Dialects
description: Choose the syntax of your patterns, the standard one or the ones of git, MSBuild, POSIX fnmatch and the CODEOWNERS files of GitHub and GitLab.
---

Every pattern is parsed in a dialect, the second argument of [Glob.Parse](cref:Meziantou.Framework.Globbing.Glob.Parse). Use [GlobDialect.Standard](cref:Meziantou.Framework.Globbing.GlobDialect.Standard) for patterns
you write, and the dialect of the tool your users know when you read their patterns: a `.gitignore` file, an MSBuild
item, a shell pattern, a CODEOWNERS file.

| Dialect | Follows | Use it for |
| --- | --- | --- |
| [Standard](cref:Meziantou.Framework.Globbing.GlobDialect.Standard) | The syntax of [Pattern syntax](03-PatternSyntax.md) | Your own patterns, and configuration files of your application |
| [Git](cref:Meziantou.Framework.Globbing.GlobDialect.Git) | gitignore(5) and git's wildmatch | `.gitignore` entries and other git patterns |
| [MSBuild](cref:Meziantou.Framework.Globbing.GlobDialect.MSBuild) | MSBuild item specs | The `Include` and `Exclude` patterns of a project file |
| [Posix](cref:Meziantou.Framework.Globbing.GlobDialect.Posix) | `fnmatch(3)` without flags | Patterns that match any string, not only paths |
| [PosixPath](cref:Meziantou.Framework.Globbing.GlobDialect.PosixPath) | `fnmatch(3)` with `FNM_PATHNAME` | Shell-like path patterns |
| [GitHubCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitHubCodeOwners) | The CODEOWNERS files of GitHub | The patterns of a GitHub CODEOWNERS file |
| [GitLabCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitLabCodeOwners) | The CODEOWNERS files of GitLab, matched with Ruby's `File.fnmatch` | The patterns of a GitLab CODEOWNERS file |

The [Git](cref:Meziantou.Framework.Globbing.GlobDialect.Git), [MSBuild](cref:Meziantou.Framework.Globbing.GlobDialect.MSBuild), [Posix](cref:Meziantou.Framework.Globbing.GlobDialect.Posix), [PosixPath](cref:Meziantou.Framework.Globbing.GlobDialect.PosixPath) and [GitLabCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitLabCodeOwners) dialects are tested against the results of git,
MSBuild, `fnmatch` and the matching code of GitLab.
They differ from these tools on purpose in a few points:

- Characters are compared as UTF-16 code units. git compares the bytes of their UTF-8 encoding, so `?` matches `é` here
  but not in git.
- A path that ends with a `/` is a directory. `fnmatch` compares strings, and does not know about directories.
- A pattern that can never match anything, such as one that holds an unknown character class, is invalid.

## Compare the dialects

| Syntax | [Standard](cref:Meziantou.Framework.Globbing.GlobDialect.Standard) | [Git](cref:Meziantou.Framework.Globbing.GlobDialect.Git) | [MSBuild](cref:Meziantou.Framework.Globbing.GlobDialect.MSBuild) | [Posix](cref:Meziantou.Framework.Globbing.GlobDialect.Posix), [PosixPath](cref:Meziantou.Framework.Globbing.GlobDialect.PosixPath) |
| --- | --- | --- | --- | --- |
| `*`, `?` | Yes | Yes | Yes | Yes, and they match a `/` in [Posix](cref:Meziantou.Framework.Globbing.GlobDialect.Posix) |
| `**` | Yes | Yes | Yes | Read as `*` |
| `[abc]`, `[a-z]`, `[!abc]` | Yes | Yes | No, plain text | Yes |
| `[^abc]` | No | Yes | No | Yes |
| `[[:alpha:]]` | No | Yes | No | Yes |
| `[[=a=]]`, `[[.a.]]` | No | No | No | Yes |
| `{abc,123}` | Yes | No, plain text | No, plain text | No, plain text |
| A leading `!` negates the pattern | Yes | Yes | No, plain text | No, plain text |
| Escape | `\x` | `\x` | `%XX` | `\x` |
| A leading `/` | Makes the pattern absolute | Anchors the pattern to the root | Makes the pattern absolute | Makes the pattern absolute in [PosixPath](cref:Meziantou.Framework.Globbing.GlobDialect.PosixPath) |
| Path separators | `/` | `/` | `/` and `\` | `/` in [PosixPath](cref:Meziantou.Framework.Globbing.GlobDialect.PosixPath), none in [Posix](cref:Meziantou.Framework.Globbing.GlobDialect.Posix) |
| Wildcards match a leading dot | With [MatchLeadingDot](cref:Meziantou.Framework.Globbing.GlobOptions.MatchLeadingDot) | Yes | Yes | Yes |

The supported character classes are `alnum`, `alpha`, `blank`, `cntrl`, `digit`, `graph`, `lower`, `print`, `punct`,
`space`, `upper` and `xdigit`. With [GlobOptions.IgnoreCase](cref:Meziantou.Framework.Globbing.GlobOptions.IgnoreCase), `upper` and `lower` match a letter of either case.

## Git

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_GitDialect;format=md-fenced-code-block;dedent=true -->
```csharp
// A pattern without a '/' matches a name at any depth
Assert.True(Glob.Parse("*.log", GlobDialect.Git).IsMatch("logs/trace.log"));

// A pattern with a '/' is relative to the root
Assert.True(Glob.Parse("/build", GlobDialect.Git).IsMatch("build"));
Assert.False(Glob.Parse("/build", GlobDialect.Git).IsMatch("src/build"));

// A pattern that ends with a '/' matches the directory and everything below it
Assert.True(Glob.Parse("bin/", GlobDialect.Git).IsMatch("src/App/bin/Debug/App.dll"));

// Character classes, and braces are ordinary characters
Assert.True(Glob.Parse("[[:digit:]]*.md", GlobDialect.Git).IsMatch("1-intro.md"));
Assert.True(Glob.Parse("{a,b}.txt", GlobDialect.Git).IsMatch("{a,b}.txt"));
```
<!-- endref -->

- A pattern without a `/` matches a name at any depth. A pattern with a `/`, at the start or in the middle, is relative
  to the root.
- A pattern matches both files and directories. A pattern that ends with a `/` matches the directory and everything
  below it.
- `.` and `..` are ordinary names, so a pattern that contains them never matches.

To read a whole `.gitignore` file, with its comments and the order of its entries, use [GlobCollection.ParseGitIgnore](cref:Meziantou.Framework.Globbing.GlobCollection.ParseGitIgnore)
instead of parsing each line: see [Reading .gitignore files](08-GitIgnore.md).

## MSBuild

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_MSBuildDialect;format=md-fenced-code-block;dedent=true -->
```csharp
// '/' and '\' are both path separators
Assert.True(Glob.Parse(@"src\**\*.cs", GlobDialect.MSBuild).IsMatch("src/Generated/Program.cs"));

// A file name made of '*.*' matches every file, even without an extension
Assert.True(Glob.Parse("**/*.*", GlobDialect.MSBuild).IsMatch("LICENSE"));

// Wildcards match names that start with a dot
Assert.True(Glob.Parse("*", GlobDialect.MSBuild).IsMatch(".editorconfig"));

// '%XX' escapes a character, and a leading '..' is kept
Assert.True(Glob.Parse("%2A.cs", GlobDialect.MSBuild).IsMatch("*.cs"));
Assert.True(Glob.Parse("../shared/*.cs", GlobDialect.MSBuild).IsMatch("../shared/Helpers.cs"));
```
<!-- endref -->

- `/` and `\` are both separators, and `%XX` escapes a character: `%2A` is a `*`, `%3F` is a `?`.
- A file name made of `*.*` matches every file, including the ones without an extension. `a*.*` still requires a dot.
- `.` and `..` are resolved, and a leading `..` is kept: `../shared/*.cs` matches paths that start with `../shared/`.
- A leading separator makes the pattern absolute: `/usr/src/*.cs` matches `/usr/src/a.cs`.
- `**` must be a whole path segment (`a**` is invalid), and a `..` cannot follow a wildcard.

## Posix and PosixPath

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_PosixDialects;format=md-fenced-code-block;dedent=true -->
```csharp
// Posix matches a plain string: '*' also matches a '/'
Assert.True(Glob.Parse("*.cs", GlobDialect.Posix).IsMatch("src/Program.cs"));

// PosixPath only matches a '/' with a '/' of the pattern
Assert.False(Glob.Parse("*.cs", GlobDialect.PosixPath).IsMatch("src/Program.cs"));
Assert.True(Glob.Parse("src/*.cs", GlobDialect.PosixPath).IsMatch("src/Program.cs"));

// Both support character classes and '^' as a negation
Assert.True(Glob.Parse("[[:upper:]]*", GlobDialect.Posix).IsMatch("Readme"));
Assert.True(Glob.Parse("[^a]*", GlobDialect.Posix).IsMatch("Readme"));
```
<!-- endref -->

- [Posix](cref:Meziantou.Framework.Globbing.GlobDialect.Posix) matches a plain string: `*`, `?` and the bracket expressions match a `/` too. It matches a file and a
  directory alike.
- [PosixPath](cref:Meziantou.Framework.Globbing.GlobDialect.PosixPath) only matches a `/` with a `/` of the pattern, and every `/` counts, including a leading one.
- A `[` that does not open a complete bracket expression is an ordinary character.
- A collating symbol can be a range bound: `[[.-.]-0]`.

## CODEOWNERS

[GitHubCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitHubCodeOwners) and [GitLabCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitLabCodeOwners) read the patterns of a CODEOWNERS file. To find the owners of a path in a whole
file, with its sections and the last-match-wins rule, use the `Meziantou.Framework.CodeOwners` package: see
[Matching paths](../CodeOwners/06-MatchingPaths.md).

GitHub follows most of the gitignore rules, and a pattern that matches a directory owns everything below it:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_GitHubCodeOwnersDialect;format=md-fenced-code-block;dedent=true -->
```csharp
// A pattern that matches a directory also matches every path below it
Assert.True(Glob.Parse("/apps/github", GlobDialect.GitHubCodeOwners).IsMatch("apps/github/src/main.js"));
Assert.True(Glob.Parse("**/logs", GlobDialect.GitHubCodeOwners).IsMatch("deeply/nested/logs/app.log"));

// Except a pattern whose last segment is '*': it only matches the names directly in the directory
Assert.True(Glob.Parse("docs/*", GlobDialect.GitHubCodeOwners).IsMatch("docs/getting-started.md"));
Assert.False(Glob.Parse("docs/*", GlobDialect.GitHubCodeOwners).IsMatch("docs/build-app/troubleshooting.md"));

// Brackets and a leading '!' are ordinary characters
Assert.True(Glob.Parse("[ab].txt", GlobDialect.GitHubCodeOwners).IsMatch("[ab].txt"));
Assert.False(Glob.Parse("[ab].txt", GlobDialect.GitHubCodeOwners).IsMatch("a.txt"));
```
<!-- endref -->

GitLab rewrites the pattern, then matches the whole path with Ruby's `File.fnmatch`:

<!-- ref:../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs;region=Glob_GitLabCodeOwnersDialect;format=md-fenced-code-block;dedent=true -->
```csharp
// A pattern without a leading '/' matches at any depth, even when it holds a '/'
Assert.True(Glob.Parse("docs/*.md", GlobDialect.GitLabCodeOwners).IsMatch("src/docs/index.md"));

// Without a trailing '/', a pattern only matches that path, not the paths below it
Assert.True(Glob.Parse("/docs", GlobDialect.GitLabCodeOwners).IsMatch("docs"));
Assert.False(Glob.Parse("/docs", GlobDialect.GitLabCodeOwners).IsMatch("docs/index.md"));
Assert.True(Glob.Parse("/docs/", GlobDialect.GitLabCodeOwners).IsMatch("docs/guide/index.md"));

// '**' only matches several directories when a '/' follows it
Assert.True(Glob.Parse("/src/**/*.cs", GlobDialect.GitLabCodeOwners).IsMatch("src/Api/Program.cs"));
Assert.False(Glob.Parse("/src/**", GlobDialect.GitLabCodeOwners).IsMatch("src/Api/Program.cs"));
```
<!-- endref -->

| Syntax | [GitHubCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitHubCodeOwners) | [GitLabCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitLabCodeOwners) |
| --- | --- | --- |
| A pattern without a leading `/` | Matches at any depth when it holds no other `/` | Matches at any depth |
| A pattern without a trailing `/`, such as `/docs` | Matches the path and everything below it | Only matches the path |
| A last segment made of `*`, such as `docs/*` | Only matches the names directly in the directory | Only matches the names directly in the directory |
| A trailing `**`, such as `/docs/**` | Matches everything below the directory | Read as `*` |
| `[abc]`, `[a-z]`, `[!abc]`, `[^abc]` | No, plain text | Yes, and a reversed range such as `[z-a]` matches its two bounds |
| `[]`, `[!]` | No, plain text | A `]` always closes the expression: `[]` matches nothing, `[!]` matches any character |
| `[[:alpha:]]` | No, plain text | No |
| A leading `!` | Plain text | Plain text: GitLab reads it as an exclusion of the file |
| A trailing `\` | Invalid | Ignored |

- Both dialects match a leading dot with a wildcard, and are case-sensitive unless [GlobOptions.IgnoreCase](cref:Meziantou.Framework.Globbing.GlobOptions.IgnoreCase) is set.
- GitHub does not publish its matcher: [GitHubCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitHubCodeOwners) follows the documentation of GitHub. A pattern that GitHub would
  report as invalid, such as one with an empty path segment, is invalid.

