---
title: GitLab sections
description: Read the sections of a GitLab CODEOWNERS file, their default owners, their reviewer count and roles.
---

GitLab groups the entries of a CODEOWNERS file in sections. Parse the file with `CodeOwnersDialect.GitLab` to read
them; with `CodeOwnersDialect.GitHub`, a section header is a pattern.

## Read the sections

Each entry has the `Section` it belongs to, or `null` for the entries before the first section header:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_SectionDefaultOwners;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    [Backend][2] @backend-team
    *.cs
    /src/Api/ @api-team

    ^[Docs] @@maintainers
    *.md
    """, CodeOwnersDialect.GitLab);

// A pattern without owners gets the default owners of its section
Assert.Equal("backend-team", file.Entries[0].Owners[0].Name);
Assert.Equal("api-team", file.Entries[1].Owners[0].Name);

CodeOwnersSection backend = file.Entries[0].Section!;
Assert.Equal("Backend", backend.Name);
Assert.Equal(2, backend.RequiredReviewerCount);
Assert.True(backend.IsMandatory);

// "^" makes the section optional
CodeOwnersEntry markdown = file.Entries[2];
Assert.True(markdown.IsOptional);
Assert.Equal(CodeOwnerType.Role, markdown.Owners[0].Type);
```
<!-- endref -->

The header of a section can hold:

| Syntax | Property |
| --- | --- |
| `[Name]` | `Name` |
| `^[Name]` | `IsOptional` is `true`, and `IsMandatory` is `false`: the owners are asked for a review, but their approval is not required |
| `[Name][2]` | `RequiredReviewerCount` is `2`. It is `1` when the header declares no count |
| `[Name] @owner other@example.com` | `DefaultOwners`, and `HasDefaultOwners` is `true` |

`CodeOwnersEntry.IsOptional` is a shortcut for the `IsOptional` of the section of the entry. An optional section keeps
the count written in its header, even though no approval is required.

## Default owners

The owners written after the name of a section are its default owners. An entry of the section that declares no owners
gets them, and an entry that declares owners keeps its own. In the example, `*.cs` is owned by `backend-team`, and
`/src/Api/` by `api-team` only.

Default owners are separated from the header by a space or a tab. A token that starts with `[` or `#` ends the list.

## Roles

In the GitLab dialect, an owner can be a role: `@@developer`, `@@maintainer` or `@@owner`, singular or plural. Its
`Type` is `CodeOwnerType.Role`, and its `Name` is the role as written, without `@@`:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_Role;format=md-fenced-code-block;dedent=true -->
```csharp
CodeOwnersFile file = CodeOwnersFile.Parse("*.md @@maintainers", CodeOwnersDialect.GitLab);
Assert.Equal(CodeOwnerType.Role, file.Entries[0].Owners[0].Type);
Assert.Equal("maintainers", file.Entries[0].Owners[0].Name);
```
<!-- endref -->

Any other word after `@@` makes the file invalid, and so does `@@` in the GitHub dialect.

## Sections with the same name

Each header creates its own `CodeOwnersSection`, even when an earlier header has the same name, and the entries keep the
section they were written in. Group the entries by `Section.Name` to see all the entries of a name together.
