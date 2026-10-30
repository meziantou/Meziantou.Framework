---
title: Overview
description: What the Windows Projected File System does, what it requires, and how the package exposes it.
---

The Windows Projected File System (ProjFS) shows files and folders in a regular folder of the disk while their content
lives elsewhere: in a database, an archive, a remote service, or memory. File Explorer and every other application see
normal files. When an application lists a folder or reads a file for the first time, Windows asks your application,
the provider, for the entries or the content, and keeps a local copy of what it receives.

The `Meziantou.Framework.Win32.ProjectedFileSystem` package implements the provider side: you derive from
`ProjectedFileSystemBase`, return the entries of the folders and the content of the files, and the base class handles
the ProjFS callbacks.

```bash
dotnet add package Meziantou.Framework.Win32.ProjectedFileSystem
```

The types are in the `Meziantou.Framework.Win32.ProjectedFileSystem` namespace.

## Requirements

- Windows 10 version 1809 or later, with the optional feature "Projected File System" installed. Install it from an
  elevated PowerShell prompt:

  ```powershell
  Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart
  ```

  Without the feature, `Start` throws a `NotSupportedException`.
- A 64-bit process. The constructor of `ProjectedFileSystemBase` throws a `NotSupportedException` in a 32-bit process.
- The provider does not need administrator rights.

## How it works

1. You create an empty folder, the root, and start the file system on it.
2. When an application lists a folder under the root, Windows calls `GetEntriesAsync` of your class with the path of
   the folder, and shows the entries it returns.
3. When an application accesses a file, Windows creates a placeholder: a local file with the name and the size of the
   entry, but no content.
4. When an application reads the file, Windows calls `OpenReadAsync` and writes the content to the placeholder, which
   becomes hydrated. The next reads use the local copy, without calling your class.
5. When an application writes to a file, it becomes a full file that ProjFS no longer manages: your class is not asked
   for its content anymore.

## Pages

- [Implementing a provider](02-ImplementingAProvider.md): return the entries and the content of the files.
- [Running the file system](03-RunningTheFileSystem.md): start and stop it, refresh the files whose content changed,
  and read the state of a file.
