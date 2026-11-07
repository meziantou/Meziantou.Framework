---
title: Introduction
description: A .NET library to match paths against glob patterns and to enumerate the files that match them.
---

`Meziantou.Framework.Globbing` matches paths against glob patterns such as `src/**/*.cs`, and enumerates the files and
directories that match them. It reads the patterns of several tools: its own syntax, `.gitignore` files, MSBuild item
specs and POSIX `fnmatch`.

## What you can do with it

- **Test a path**: check whether a path, or a directory and a file name, matches a pattern, without touching the file
  system. See [Matching paths](05-MatchingPaths.md).
- **Enumerate files**: list the files or the directories of a folder that match a pattern. Only the directories that
  can hold a match are visited. See [Enumerating files](06-EnumeratingFiles.md).
- **Combine patterns**: include some paths and exclude others with a [GlobCollection](cref:Meziantou.Framework.Globbing.GlobCollection). See
  [Combining patterns](07-CombiningPatterns.md).
- **Read `.gitignore` files**: load the content of a `.gitignore` file and check which paths git ignores. See
  [Reading .gitignore files](08-GitIgnore.md).
- **Use the syntax your users know**: parse a pattern as git, MSBuild or `fnmatch` would. See
  [Dialects](04-Dialects.md).

## Principles

- **A pattern is parsed once.** [Glob.Parse](cref:Meziantou.Framework.Globbing.Glob.Parse) validates the pattern and returns a [Glob](cref:Meziantou.Framework.Globbing.Glob) that you can reuse for as many
  paths as you want. An invalid pattern throws, or makes [Glob.TryParse](cref:Meziantou.Framework.Globbing.Glob.TryParse) return `false`, instead of
  silently matching nothing.
- **The dialect is explicit.** Every call to [Parse](cref:Meziantou.Framework.Globbing.Glob.Parse) names the dialect of the pattern. The same characters can mean
  different things in two dialects: `{a,b}` is a choice in the standard syntax and plain text in a `.gitignore` file.
- **Matching is a string operation.** [IsMatch](cref:Meziantou.Framework.Globbing.Glob.IsMatch) reads the path you give it; it never checks whether the file exists.
- **Enumeration is built on `System.IO.Enumeration`.** The results are full paths, and the same `EnumerationOptions`
  apply.

## Namespaces

Every type is in the [Meziantou.Framework.Globbing](cref:Meziantou.Framework.Globbing) namespace: [Glob](cref:Meziantou.Framework.Globbing.Glob), [GlobCollection](cref:Meziantou.Framework.Globbing.GlobCollection), [GlobDialect](cref:Meziantou.Framework.Globbing.GlobDialect), [GlobOptions](cref:Meziantou.Framework.Globbing.GlobOptions)
and the `IsMatch` and [EnumerateFiles](cref:Meziantou.Framework.Globbing.GlobExtensions.EnumerateFiles) extension methods.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, match a path and enumerate files.
- [Pattern syntax](03-PatternSyntax.md): the wildcards, and how files, directories and hidden names are matched.
- [Dialects](04-Dialects.md): choose between the standard syntax and the ones of git, MSBuild and `fnmatch`.
