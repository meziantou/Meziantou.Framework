---
title: Limitations
description: What the parser does not do, and how to work around it.
---

- **Patterns are not matched.** The library returns the patterns as written, without their escape characters: it does
  not tell which entry owns a path. Match the patterns with your own code, and apply the last-match-wins rule. See
  [Find the owners of a path](03-EntriesAndOwners.md#find-the-owners-of-a-path).
- **Owners are not checked against the host.** The parser checks the syntax of an owner, not that the user, the team or
  the email address exists, or that it has access to the repository.
- **One error per parse.** Parsing stops at the first error. See [Handling errors](05-HandlingErrors.md).
- **The file is read, not edited.** The parsed values are read-only, and comments and blank lines are not kept. Use
  `ToString` to write entries and section headers back. See [Write entries back](03-EntriesAndOwners.md#write-entries-back).
- **Sections with the same name are not merged.** Each section header gives its own [CodeOwnersSection](cref:Meziantou.Framework.CodeOwners.CodeOwnersSection). See
  [Sections with the same name](04-GitLabSections.md#sections-with-the-same-name).
- **The dialect is not detected.** Pass the dialect of the host that reads the file. See
  [Choose the dialect](02-GettingStarted.md#choose-the-dialect).
