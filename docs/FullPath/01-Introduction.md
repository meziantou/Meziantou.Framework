---
title: Introduction
description: A path type that is always absolute and normalized, compares paths the way the operating system does, and works with every API that takes a string.
---

`Meziantou.Framework.FullPath` provides the [FullPath](cref:Meziantou.Framework.FullPath) type: the absolute path of a file or a folder. It replaces the
strings that applications usually pass around to represent paths.

A string can hold a relative path, which silently depends on the current directory, a path with a trailing separator or
with `..` segments, or a path that two parts of the code compare with a different case sensitivity. A [FullPath](cref:Meziantou.Framework.FullPath) cannot:
it is resolved and normalized when it is created, and it compares paths the same way everywhere.

## What you can do with it

- **Create paths** from strings, from `FileInfo` and `DirectoryInfo`, from the current directory, the temporary folder
  and the special folders, and combine them with the `/` operator. See [Creating paths](03-CreatingPaths.md).
- **Read and change the parts of a path**: the name, the extension, the parent folder. See
  [Names, extensions and parents](04-NamesAndExtensions.md).
- **Compare paths** with the case sensitivity of the operating system or the one you choose, check that a path is inside
  a folder, and compute relative paths. See [Comparing paths](05-ComparingPaths.md).
- **Find a parent folder**, such as the root of the Git repository. See [Finding ancestors](06-FindingAncestors.md).
- **Resolve symbolic links** and get the canonical path of a file. See [Symbolic links](07-SymbolicLinks.md).
- **Serialize paths to JSON**, including with the `System.Text.Json` source generator. See
  [Serialization](08-Serialization.md).
- **Use the Windows-specific features**: reserved device names, extended-length paths, known folders. See
  [Platform-specific features](09-PlatformSpecificFeatures.md).
- **Find the string paths to convert**: the package includes an analyzer and code fixes. See
  [Analyzer](10-Analyzer/01-Overview.md).

## Principles

- **Always absolute.** [FullPath.FromPath](cref:Meziantou.Framework.FullPath.FromPath) resolves a relative path against the current directory once, when the path is
  created, so later changes of the current directory do not change it.
- **Normalized.** `.` and `..` segments are resolved and the trailing separator is removed, so two spellings of the same
  path give equal values.
- **A string when you need one.** [FullPath](cref:Meziantou.Framework.FullPath) converts implicitly to `string`, so you can pass it to `File`,
  `Directory`, `FileStream` and any other API that takes a path.
- **Compared like the operating system does.** Comparisons are ordinal. They ignore case on Windows, macOS, iOS and
  tvOS, and are case-sensitive elsewhere, unless you choose otherwise.
- **An explicit empty value.** `default(FullPath)` is [FullPath.Empty](cref:Meziantou.Framework.FullPath.Empty), the absence of a path. It is never the current
  directory.
- **Immutable.** [FullPath](cref:Meziantou.Framework.FullPath) is a read-only struct: every method that changes a path returns a new value.

[FullPath](cref:Meziantou.Framework.FullPath) only computes paths: creating, combining or comparing paths never accesses the file system. The methods
that do access it say so, such as [CreateParentDirectory](cref:Meziantou.Framework.FullPath.CreateParentDirectory), [CreateTempFile](cref:Meziantou.Framework.FullPath.CreateTempFile) or the symbolic link methods.

## Namespace

All the types are in the [Meziantou.Framework](cref:Meziantou.Framework.FullPath!N:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| [FullPath](cref:Meziantou.Framework.FullPath) | The path, its operators and its methods |
| [FullPathComparer](cref:Meziantou.Framework.FullPathComparer) | The case-sensitive, case-insensitive and default comparers |
| [FullPathJsonConverter](cref:Meziantou.Framework.FullPathJsonConverter) | The `System.Text.Json` converter, applied automatically |
| [SymbolicLinkResolutionMode](cref:Meziantou.Framework.SymbolicLinkResolutionMode) | How far to follow symbolic links |
| [KnownFolder](cref:Meziantou.Framework.KnownFolder) | The Windows known folders |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and write your first program.
- [Analyzer](10-Analyzer/01-Overview.md): convert existing code that uses strings.
