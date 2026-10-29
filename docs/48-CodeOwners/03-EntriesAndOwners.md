---
title: Entries and owners
description: Read the pattern and the owners of each entry, find the entry that owns a path, and write entries back.
---

## Read the owners of an entry

Each owner is a `CodeOwner`: its `Type` says how it is written, and its `Name` is the identifier without the leading
`@`:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
CodeOwnersFile file = CodeOwnersFile.Parse("* @user1 docs@example.com", CodeOwnersDialect.GitHub);
Assert.Equal("*", file.Entries[0].Pattern);
Assert.Equal(CodeOwnerType.Username, file.Entries[0].Owners[0].Type);
Assert.Equal("user1", file.Entries[0].Owners[0].Name);
Assert.Equal(CodeOwnerType.EmailAddress, file.Entries[0].Owners[1].Type);
Assert.Equal("docs@example.com", file.Entries[0].Owners[1].Name);
```
<!-- endref -->

| `CodeOwnerType` | Written as | `Name` |
| --- | --- | --- |
| `Username` | `@user` or `@org/team` | `user`, `org/team` |
| `EmailAddress` | `user@example.com` | `user@example.com` |
| `Role` | `@@developer`, `@@maintainer`, `@@owner` (GitLab only) | `developer`, `maintainer`, `owner` |

The parser does not check that a user, a team or an address exists: it only checks the syntax. A token that is neither
a username nor an email address makes the file invalid. See [Handling errors](05-HandlingErrors.md).

## Find the owners of a path

Entries are in the order of the file, and CODEOWNERS resolution is last-match-wins: a path is owned by the last entry
whose pattern matches it. The library does not match patterns, so bring your own matcher (for instance the `.gitignore`
dialect of [Globbing](../03-Globbing/04-Dialects.md)) and search the entries from the end:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_LastMatchWins;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    * @user1
    *.md @docs-team
    CHANGELOG.md
    """, CodeOwnersDialect.GitHub);

IReadOnlyList<CodeOwner> GetOwners(string path)
{
    CodeOwnersEntry? owningEntry = file.Entries.LastOrDefault(entry => Matches(entry.Pattern, path));
    return owningEntry?.Owners ?? [];
}

Assert.Equal("user1", Assert.Single(GetOwners("src/Program.cs")).Name);
Assert.Equal("docs-team", Assert.Single(GetOwners("README.md")).Name);

// An empty Owners list means the entry explicitly leaves the pattern unowned
Assert.Empty(GetOwners("CHANGELOG.md"));

// The library does not match patterns: this matcher only handles the patterns of this example
static bool Matches(string pattern, string path)
    => pattern == "*" || pattern == path || (pattern.StartsWith("*.", StringComparison.Ordinal) && path.EndsWith(pattern[1..], StringComparison.Ordinal));
```
<!-- endref -->

The matcher of this example only handles the patterns it uses. A real one must follow the pattern syntax of the host.

## Patterns without owners

An entry without owners has an empty `Owners` list: the entry explicitly leaves its pattern unowned. As resolution is
last-match-wins, the paths it matches have no owners, even when a previous entry matches them too:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_UnownedPattern;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    * @org/maintainers
    /generated/ # Nobody owns the generated files
    """, CodeOwnersDialect.GitHub);

CodeOwnersEntry generated = file.Entries[1];
Assert.Equal("/generated/", generated.Pattern);
Assert.Empty(generated.Owners);
```
<!-- endref -->

- A line that starts with `#` is a comment. A `#` after the pattern starts a comment too.
- In a GitLab section that has default owners, an entry without owners gets the default owners of the section instead.
  See [Default owners](04-GitLabSections.md#default-owners).

## Escaped patterns

A backslash escapes the next character of a pattern, so that a pattern can contain a space or start with `#`. The
`Pattern` property holds the pattern without the backslashes:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_EscapedPattern;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    /docs/User\ Guide.md @docs-team
    \#notes.txt @user1
    """, CodeOwnersDialect.GitHub);

// The backslashes are removed from the pattern
Assert.Equal("/docs/User Guide.md", file.Entries[0].Pattern);
Assert.Equal("#notes.txt", file.Entries[1].Pattern);
```
<!-- endref -->

A backslash at the end of a line does not join the next line: an entry never spans several lines.

## Write entries back

`ToString` returns a value as written in a CODEOWNERS file of the dialect it was parsed with. The pattern is escaped
again, so the text can be parsed back to the same entry:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_ToString;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    [Docs][2] @docs-team
    /docs/User\ Guide.md @writer docs@example.com
    """, CodeOwnersDialect.GitLab);

CodeOwnersEntry entry = file.Entries[0];
Assert.Equal("/docs/User Guide.md", entry.Pattern);

// ToString writes the values back in the CODEOWNERS syntax, escaping the pattern again
Assert.Equal("""/docs/User\ Guide.md @writer docs@example.com""", entry.ToString());
Assert.Equal("@writer", entry.Owners[0].ToString());
Assert.Equal("[Docs][2] @docs-team", entry.Section?.ToString());
```
<!-- endref -->

- `CodeOwnersEntry.ToString` returns the pattern and the owners. The section header is not part of the entry: write
  `CodeOwnersSection.ToString` on the line before.
- `CodeOwner.ToString` returns `@user`, `@@role` or the email address.
- Comments are not kept, and an entry that got the default owners of its section writes them on its own line.

## Compare entries

`CodeOwnersEntry`, `CodeOwner` and `CodeOwnersSection` have value equality: two entries are equal when they have the
same pattern, the same owners in the same order, and equal sections. The `==` and `!=` operators compare the same way.
