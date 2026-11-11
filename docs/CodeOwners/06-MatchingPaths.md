---
title: Matching paths
description: Find the entries and the owners of a path with the matching rules of GitHub or GitLab, including GitLab sections and exclusions.
---

[GetOwners](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.GetOwners) and [FindEntries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.FindEntries) match a path against the patterns of a file the way the host of its dialect does. GitHub
and GitLab do not read the same pattern the same way, so parse the file with the dialect of the host that reads it.
The patterns are matched by the [GitHubCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitHubCodeOwners) and [GitLabCodeOwners](cref:Meziantou.Framework.Globbing.GlobDialect.GitLabCodeOwners) dialects of `Meziantou.Framework.Globbing`, which
you can also use on their own: see [Dialects](../Globbing/04-Dialects.md#codeowners).

## Find the owners of a path

[GetOwners](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.GetOwners) returns the owners of the entries that own a path, without duplicates:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwnersFile_GetOwners;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    * @maintainers
    *.md @docs-team
    /docs/internal/
    """, CodeOwnersDialect.GitHub);

Assert.Equal(["maintainers"], file.GetOwners("src/Program.cs").Select(owner => owner.Name));
Assert.Equal(["docs-team"], file.GetOwners("docs/README.md").Select(owner => owner.Name));

// The last matching entry has no owners: the path is unowned
Assert.Empty(file.GetOwners("docs/internal/notes.md"));
```
<!-- endref -->

[FindEntries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.FindEntries) returns the entries themselves, in the order of the file. Use it to read the section of an entry, or to tell a
path that no entry matches (no entries) from a path that an entry leaves unowned (an entry without owners):

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwnersFile_FindEntries;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    * @maintainers

    [Backend]
    *.cs @backend-team

    [Docs]
    *.md @docs-team
    /src/ @writers
    """, CodeOwnersDialect.GitLab);

// GitLab applies the last matching entry of each section
IReadOnlyList<CodeOwnersEntry> entries = file.FindEntries("src/Program.cs");
Assert.Equal(["*", "*.cs", "/src/"], entries.Select(entry => entry.Pattern));
Assert.Equal([null, "Backend", "Docs"], entries.Select(entry => entry.Section?.Name));
```
<!-- endref -->

| Dialect | [FindEntries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.FindEntries) returns |
| --- | --- |
| [GitHub](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitHub) | The last entry whose pattern matches the path, or nothing |
| [GitLab](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitLab) | The last matching entry of each section, except the sections where an exclusion matches the path |

To test one pattern, ignoring the other entries, use [CodeOwnersEntry.IsMatch](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.IsMatch).

## The paths

- Pass the path of a file, relative to the root of the repository, with `/` as the separator: `src/Program.cs`. A leading
  `/` is ignored, and `\` is an ordinary character, not a separator.
- The comparison is case-sensitive, as on GitHub and GitLab: `*.MD` does not match `README.md`.
- The path is not read from the disk: the file does not have to exist, and a directory is not expanded to its files.

## GitHub

GitHub follows most of the rules of `.gitignore` files:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_GitHubMatching;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    /build/logs/ @doctocat
    docs/* docs@example.com
    **/logs @octocat
    /apps/github @github-team
    """, CodeOwnersDialect.GitHub);

// A pattern that matches a directory owns everything below it
Assert.Equal("/apps/github", Assert.Single(file.FindEntries("apps/github/src/main.js")).Pattern);
Assert.Equal("**/logs", Assert.Single(file.FindEntries("deeply/nested/logs/app.log")).Pattern);

// Except a pattern that ends with '/*': it only matches the files of the directory
Assert.Equal("docs/*", Assert.Single(file.FindEntries("docs/getting-started.md")).Pattern);
Assert.Empty(file.FindEntries("docs/build-app/troubleshooting.md"));
```
<!-- endref -->

- A pattern without a `/`, such as `*.js` or `logs`, matches a name at any depth. A pattern with a `/` at the start or
  in the middle is relative to the root of the repository.
- A pattern that matches a directory owns every file below it: `/apps/github`, `apps/` and `**/logs` all match
  `apps/github/src/main.js` when it is the path of their directory. A pattern that ends with a `/` only matches a
  directory.
- A pattern whose last segment is `*`, such as `docs/*`, only matches the files directly in the directory.
- `*` and `?` do not match a `/`, and `**` matches any number of directories.
- GitHub does not support character ranges or negations: `[`, `]` and a leading `!` are ordinary characters.

## GitLab

GitLab matches the whole path of the file, with the rules of Ruby's `File.fnmatch`:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_GitLabMatching;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    /docs @docs-file-owner
    /docs/ @docs-team
    /src/** @src-team
    """, CodeOwnersDialect.GitLab);

// Without a trailing '/', the pattern only matches a file
Assert.Equal(["docs-file-owner"], file.GetOwners("docs").Select(owner => owner.Name));
Assert.Equal(["docs-team"], file.GetOwners("docs/guide/index.md").Select(owner => owner.Name));

// '/**' at the end only matches the names directly in the directory
Assert.Equal(["src-team"], file.GetOwners("src/Program.cs").Select(owner => owner.Name));
Assert.Empty(file.GetOwners("src/Api/Controller.cs"));
```
<!-- endref -->

- A pattern that does not start with a `/` matches at any depth, as if it started with `/**/`: `docs/*.md` matches
  `src/docs/README.md`.
- A pattern that ends with a `/` matches every file below the directory. Without the trailing `/`, `/docs` only matches a
  file named `docs`, not the files of a `docs` directory.
- `**/` matches any number of directories. A `**` that a `/` does not follow is a `*`: `/src/**` only matches the
  names directly in `src`.
- `[abc]`, `[a-z]`, `[!abc]` and `[^abc]` are bracket expressions. As in Ruby, a `]` always closes the expression, so
  `[]` matches nothing, and a reversed range such as `[z-a]` matches its two bounds.

The same file can give other owners with the other dialect:

| Pattern | Path | [GitHub](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitHub) | [GitLab](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitLab) |
| --- | --- | --- | --- |
| `/docs` | `docs/index.md` | Matches | No match |
| `docs/*.md` | `src/docs/index.md` | No match | Matches |
| `/src/**` | `src/Api/Program.cs` | Matches | No match |
| `/[ab].txt` | `a.txt` | No match | Matches |

## Sections

With GitLab, each section has its own last-match-wins rule, and a path collects the owners of every section where an
entry matches it. In the example of [Find the owners of a path](#find-the-owners-of-a-path), `src/Program.cs` is owned
by the entries of the default section, of `Backend` and of `Docs`.

- The entries written before the first section header form the default section.
- The sections with the same name, ignoring case, are one section: `[Docs]` and `[docs]` share their last-match-wins
  rule. [FindEntries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.FindEntries) orders the sections by their first header.

## Exclusions

In the GitLab dialect, a pattern that starts with `!` is an exclusion: its [IsExclusion](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.IsExclusion) property is `true`. A path that
matches an exclusion has no owners in its section, wherever the exclusion is in the section, and keeps the owners of
the other sections:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_GitLabExclusion;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    [Ruby]
    !/vendor/
    *.rb @ruby-team

    [Vendor]
    /vendor/ @vendor-team
    """, CodeOwnersDialect.GitLab);

// An exclusion removes its section, whatever the order of the entries
Assert.True(file.Entries[0].IsExclusion);
Assert.Equal(["ruby-team"], file.GetOwners("app/models/user.rb").Select(owner => owner.Name));
Assert.Equal(["vendor-team"], file.GetOwners("vendor/gems/gem.rb").Select(owner => owner.Name));
```
<!-- endref -->

- [FindEntries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.FindEntries) never returns an exclusion, and the owners written after an exclusion are ignored.
- The [Pattern](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.Pattern) of an exclusion keeps its `!`. [IsMatch](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.IsMatch) tests the pattern without it.
- `\!` is a pattern that starts with a literal `!`, not an exclusion. With GitHub, `!` is always an ordinary character.
