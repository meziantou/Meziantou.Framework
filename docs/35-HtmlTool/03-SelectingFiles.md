---
title: Selecting files
description: Update one file with --single-file, or the files that a glob pattern matches with --file-pattern and --root-directory.
---

Every command takes the same three options to choose the files it updates.

| Option | Description |
| --- | --- |
| `--single-file <path>` | The file to update, relative to the current directory or absolute. |
| `--file-pattern <pattern>` | A glob pattern: every file it matches is updated. |
| `--root-directory <path>` | The folder where the pattern is evaluated. When omitted, the current directory. |

## Update one file

```bash
meziantou.html append-version --single-file dist/index.html
```

## Update the files that match a pattern

`--file-pattern` is a glob pattern, relative to `--root-directory`:

```bash
meziantou.html append-version --file-pattern "**/*.html" --root-directory dist
```

The pattern uses the standard dialect of [Meziantou.Framework.Globbing](../03-Globbing/03-PatternSyntax.md): `*`
matches any characters of a name, `**` any number of folders, `{html,htm}` one of the alternatives. The comparison is
case-sensitive, and the wildcards do not match the names that start with a dot, such as `.cache`: see
[Names that start with a dot](../03-Globbing/03-PatternSyntax.md#names-that-start-with-a-dot). Quote the pattern, so
that your shell does not expand it.

A pattern that is not valid gives the message `Glob pattern '…' is invalid` on the standard error and a non-zero exit
code, and no file is updated from the pattern. See
[Invalid patterns](../03-Globbing/03-PatternSyntax.md#invalid-patterns).

## Combine the options

`--single-file` and `--file-pattern` can be given together: the tool updates the single file first, then the files that
the pattern matches. A file that is selected both ways is updated twice. Without either option, the command updates
nothing and exits with code 0.
