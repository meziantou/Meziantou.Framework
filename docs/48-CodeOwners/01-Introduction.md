---
title: Introduction
description: A parser for CODEOWNERS files that returns their entries, owners and GitLab sections, and reports where a file is invalid.
---

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)
files, the files of GitHub and GitLab repositories that say who owns which files. Use it to build tools on top of the
ownership rules of a repository: list the owners of a team, check that every owner is a known user, or request
reviewers from your own automation.

## What you can do with it

- **Parse a file** and read its entries, in the order of the file. See [Getting started](02-GettingStarted.md).
- **Read the patterns and the owners** of each entry: usernames, teams, email addresses and GitLab roles. See
  [Entries and owners](03-EntriesAndOwners.md).
- **Read the GitLab sections**, with their default owners, their required number of reviewers and whether they are
  optional. See [GitLab sections](04-GitLabSections.md).
- **Know why a file is invalid**, with the line and the position of the error. See
  [Handling errors](05-HandlingErrors.md).

## Principles

- **The dialect is explicit.** GitHub and GitLab do not accept the same syntax, so every parse takes a
  `CodeOwnersDialect`. See [Choose the dialect](02-GettingStarted.md#choose-the-dialect).
- **A file is valid or it is not.** Parsing stops at the first error: you get the error, never a partially parsed
  file.
- **Values are copied out of the content.** A parsed file does not reference the string or the span it was parsed
  from.
- **Read-only.** The entries, the owners and the sections cannot be modified.
- **The library parses, it does not match.** It does not tell which entry owns a path: see
  [Limitations](06-Limitations.md).

## Namespace

All the types are in the `Meziantou.Framework.CodeOwners` namespace:

| Type | Contains |
| --- | --- |
| `CodeOwnersFile` | `Parse`, `TryParse` and the `Entries` of a file |
| `CodeOwnersDialect` | `GitHub` or `GitLab` |
| `CodeOwnersEntry` | A pattern, its owners and its section |
| `CodeOwner` | An owner and its `CodeOwnerType` |
| `CodeOwnersSection` | A GitLab section |
| `CodeOwnersParseError`, `CodeOwnersParseErrorKind`, `CodeOwnersParseException` | Why and where a file is invalid |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and parse your first file.
