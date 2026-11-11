---
title: Limitations
description: What the parser does not do, and how to work around it.
---

- **Paths are not read from the repository.** Matching compares the path you pass with the patterns: it does not check
  that the file exists, and it does not list the files a pattern matches. Pass the paths of files, not of directories.
  See [The paths](06-MatchingPaths.md#the-paths).
- **Unsupported GitHub syntax is not reported.** GitHub skips the lines it cannot read. The library reads `[`, `]` and
  a leading `!` as ordinary characters instead. See [GitHub](06-MatchingPaths.md#github).
- **Owners are not checked against the host.** The parser checks the syntax of an owner, not that the user, the team or
  the email address exists, or that it has access to the repository.
- **One error per parse.** Parsing stops at the first error. See [Handling errors](05-HandlingErrors.md).
- **The file is read, not edited.** The parsed values are read-only, and comments and blank lines are not kept. Use
  `ToString` to write entries and section headers back. See [Write entries back](03-EntriesAndOwners.md#write-entries-back).
- **Sections with the same name are not merged.** Each section header gives its own [CodeOwnersSection](cref:Meziantou.Framework.CodeOwners.CodeOwnersSection). See
  [Sections with the same name](04-GitLabSections.md#sections-with-the-same-name).
- **The dialect is not detected.** Pass the dialect of the host that reads the file. See
  [Choose the dialect](02-GettingStarted.md#choose-the-dialect).
