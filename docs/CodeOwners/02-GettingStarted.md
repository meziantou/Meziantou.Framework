---
title: Getting started
description: Install the package, parse the CODEOWNERS file of a repository and choose the dialect of its host.
---

## Install the package

Add the `Meziantou.Framework.CodeOwners` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.CodeOwners" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.CodeOwners;`.

## Parse a file

Read the content of the file and pass it to [CodeOwnersFile.Parse](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.Parse), with the dialect of the host of the repository:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_ReadFile;format=md-fenced-code-block;dedent=true -->
```csharp
string content = File.ReadAllText(filePath); // For instance, .github/CODEOWNERS
CodeOwnersFile file = CodeOwnersFile.Parse(content, CodeOwnersDialect.GitHub);

// Comments and blank lines are not entries
Assert.HasCount(2, file.Entries);

CodeOwnersEntry docs = file.Entries[1];
Assert.Equal("/docs/", docs.Pattern);
Assert.Equal(["org/docs-team", "docs@example.com"], docs.Owners.Select(owner => owner.Name));
```
<!-- endref -->

- The parser takes the content, not a path: read the file yourself.
- [Entries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.Entries) holds one [CodeOwnersEntry](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry) per pattern line, in the order of the file. Comments and blank lines are not
  entries.
- Each entry has a [Pattern](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.Pattern) and its [Owners](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.Owners). A username or a team is stored without its leading `@`.

[Parse](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.Parse) throws a [CodeOwnersParseException](cref:Meziantou.Framework.CodeOwners.CodeOwnersParseException) when the file is invalid. Use [TryParse](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.TryParse) to get `false` instead: see
[Handling errors](05-HandlingErrors.md).

## Choose the dialect

The two hosts do not read the same file the same way:

- [CodeOwnersDialect.GitHub](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitHub) reads a pattern and its owners on each line. A line that starts with `[` is a pattern, as
  `[` opens a character class.
- [CodeOwnersDialect.GitLab](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitLab) adds sections: a line such as `[Docs]` or `^[Docs]` starts a section, and the owners can be
  roles such as `@@maintainer`.

The same content can give different entries:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_Dialects;format=md-fenced-code-block;dedent=true -->
```csharp
const string Content = """
    [Docs]
    *.md @docs-team
    """;

// GitHub reads "[Docs]" as a pattern, with no owners
var github = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitHub);
Assert.Equal(["[Docs]", "*.md"], github.Entries.Select(entry => entry.Pattern));

// GitLab reads it as the header of a section
var gitlab = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab);
CodeOwnersEntry entry = Assert.Single(gitlab.Entries);
Assert.Equal("*.md", entry.Pattern);
Assert.Equal("Docs", entry.Section?.Name);
```
<!-- endref -->

A file without sections and roles gives the same entries with both dialects. Pick the dialect of the host that reads the
file, so that your tool and the host agree.

## Next steps

- [Entries and owners](03-EntriesAndOwners.md): patterns, owners, unowned paths and the last-match-wins rule.
- [GitLab sections](04-GitLabSections.md): sections, default owners and roles.
