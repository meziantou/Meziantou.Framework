---
title: Introduction
description: A parser for CODEOWNERS files that returns their entries, owners and GitLab sections, finds the owners of a path, and reports where a file is invalid.
---

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)
files, the files of GitHub and GitLab repositories that say who owns which files. Use it to build tools on top of the
ownership rules of a repository: find the owners of the files of a pull request, list the files nobody owns, check that
every owner is a known user, or request reviewers from your own automation.

## What you can do with it

- **Parse a file** and read its entries, in the order of the file. See [Getting started](02-GettingStarted.md).
- **Read the patterns and the owners** of each entry: usernames, teams, email addresses and GitLab roles. See
  [Entries and owners](03-EntriesAndOwners.md).
- **Find the owners of a path**, with the matching rules of GitHub or GitLab. See [Matching paths](06-MatchingPaths.md).
- **Read the GitLab sections**, with their default owners, their required number of reviewers and whether they are
  optional. See [GitLab sections](04-GitLabSections.md).
- **Know why a file is invalid**, with the line and the position of the error. See
  [Handling errors](05-HandlingErrors.md).

## Principles

- **The dialect is explicit.** GitHub and GitLab do not accept the same syntax, so every parse takes a
  [CodeOwnersDialect](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect). See [Choose the dialect](02-GettingStarted.md#choose-the-dialect).
- **A file is valid or it is not.** Parsing stops at the first error: you get the error, never a partially parsed
  file.
- **Values are copied out of the content.** A parsed file does not reference the string or the span it was parsed
  from.
- **Read-only.** The entries, the owners and the sections cannot be modified.
- **Matching follows the host.** GitHub and GitLab do not read the same pattern the same way, so a path is matched
  with the rules of the dialect of the file. See [Matching paths](06-MatchingPaths.md).

## Namespace

All the types are in the [Meziantou.Framework.CodeOwners](cref:Meziantou.Framework.CodeOwners) namespace:

| Type | Contains |
| --- | --- |
| [CodeOwnersFile](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile) | [Parse](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.Parse), [TryParse](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.TryParse), the [Entries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.Entries) of a file, and the owners of a path: [GetOwners](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.GetOwners), [FindEntries](cref:Meziantou.Framework.CodeOwners.CodeOwnersFile.FindEntries) |
| [CodeOwnersDialect](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect) | [GitHub](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitHub) or [GitLab](cref:Meziantou.Framework.CodeOwners.CodeOwnersDialect.GitLab) |
| [CodeOwnersEntry](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry) | A pattern, its owners and its section, and whether the pattern matches a path ([IsMatch](cref:Meziantou.Framework.CodeOwners.CodeOwnersEntry.IsMatch)) |
| [CodeOwner](cref:Meziantou.Framework.CodeOwners.CodeOwner) | An owner and its [CodeOwnerType](cref:Meziantou.Framework.CodeOwners.CodeOwnerType) |
| [CodeOwnersSection](cref:Meziantou.Framework.CodeOwners.CodeOwnersSection) | A GitLab section |
| [CodeOwnersParseError](cref:Meziantou.Framework.CodeOwners.CodeOwnersParseError), [CodeOwnersParseErrorKind](cref:Meziantou.Framework.CodeOwners.CodeOwnersParseErrorKind), [CodeOwnersParseException](cref:Meziantou.Framework.CodeOwners.CodeOwnersParseException) | Why and where a file is invalid |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and parse your first file.
