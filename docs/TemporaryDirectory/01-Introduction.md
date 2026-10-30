---
title: Introduction
description: Disposable temporary folders and files, with unique names, private permissions and reliable deletion.
---

`Meziantou.Framework.TemporaryDirectory` provides two types: `TemporaryDirectory`, a unique empty folder, and
`TemporaryFile`, a unique file. Both are deleted when you dispose them, so a `using` statement is enough to clean up
after a test, a build step or any work that needs scratch space on disk.

## What you can do with it

- **Create a scratch folder** and the files and subfolders you need inside it, with paths that cannot escape it. See
  [Temporary directories](03-TemporaryDirectories.md).
- **Create a single temporary file**, with a generated name, a name of your choice or at a path of your choice. See
  [Temporary files](04-TemporaryFiles.md).
- **Pass them to any API**: both types convert implicitly to `string` and to
  [`FullPath`](../FullPath/01-Introduction.md), and to `DirectoryInfo` or `FileInfo`.

## Principles

- **Unique names.** Every folder and file name contains a timestamp and a GUID, so two instances never share a path, even
  when tests run in parallel.
- **Private.** On Linux and macOS, the folders are created so that only the current user can access them, and the files
  so that only the current user can read and write them.
- **Deleted when disposed.** Disposing deletes the folder and everything it contains, read-only files included. Disposing
  twice does nothing, so it cannot delete a folder that something else created at the same path afterward.
- **Absolute paths.** Paths are [`FullPath`](../FullPath/01-Introduction.md) values: absolute and normalized.

## Namespace

`TemporaryDirectory` and `TemporaryFile` are in the `Meziantou.Framework` namespace, as `FullPath` is.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and create your first temporary folder.
