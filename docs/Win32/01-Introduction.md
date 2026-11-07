---
title: Introduction
description: The Meziantou.Framework.Win32 packages wrap Windows APIs that .NET does not expose, one package per feature.
---

The `Meziantou.Framework.Win32.*` packages give .NET applications access to Windows features that the base class
library does not cover. Each package wraps one Windows API behind a small, typed .NET API: you never declare a P/Invoke
or a COM interface yourself.

The packages are **Windows-only**. They compile in any .NET project, but their methods only work when the
application runs on Windows. See [Getting started](02-GettingStarted.md) for how to call them from a cross-platform
application.

## What you can do with them

- **Inspect and adjust the security context of a process**: elevation, groups, integrity level and privileges. See
  [Access tokens](03-AccessTokens.md).
- **Scan content for malware** with the antimalware product installed on the machine before using it. See
  [Antimalware scanning](04-AntimalwareScanning.md).
- **Find the files that changed on a volume** without scanning it, from the NTFS change journal. See
  [Change journal](05-ChangeJournal.md).
- **Store passwords and tokens** in the Windows Credential Manager, and ask the user for credentials. See
  [Credential Manager](06-CredentialManager.md).
- **Let the user pick a folder** with the modern Windows folder dialog. See [Folder dialog](07-FolderDialog.md).
- **Manage a group of processes as a unit**: terminate the child processes when the application exits, and limit their
  memory, CPU and IO. See [Job objects](08-JobObjects.md).
- **Store machine secrets** in the Local Security Authority. See [LSA private data](09-LsaPrivateData.md).
- **Tell whether a file was downloaded from the Internet**, and mark the files you download. See
  [Mark of the Web](10-MarkOfTheWeb.md).
- **Classify files** as text, image, audio, video and so on, from their extension. See
  [Perceived types](11-PerceivedTypes.md).
- **Show virtual files and folders** whose content your application provides on demand. See
  [Projected file system](12-ProjectedFileSystem/01-Overview.md).
- **Add documents to Recent Items and Jump Lists**. See [Recent documents](13-RecentDocuments.md).
- **Find which processes lock a file**, and close and restart them. See [Restart Manager](14-RestartManager.md).

## Packages

| Package | Main types | Page |
| --- | --- | --- |
| `Meziantou.Framework.Win32.AccessToken` | [AccessToken](cref:Meziantou.Framework.Win32.AccessToken), [SecurityIdentifier](cref:Meziantou.Framework.Win32.SecurityIdentifier), [Privileges](cref:Meziantou.Framework.Win32.Privileges) | [Access tokens](03-AccessTokens.md) |
| `Meziantou.Framework.Win32.Amsi` | [AmsiContext](cref:Meziantou.Framework.Win32.AmsiContext), [AmsiSession](cref:Meziantou.Framework.Win32.AmsiSession), [AmsiResult](cref:Meziantou.Framework.Win32.AmsiResult) | [Antimalware scanning](04-AntimalwareScanning.md) |
| `Meziantou.Framework.Win32.ChangeJournal` | [ChangeJournal](cref:Meziantou.Framework.Win32.ChangeJournal), [ChangeJournalEntry](cref:Meziantou.Framework.Win32.ChangeJournalEntry), [Usn](cref:Meziantou.Framework.Win32.Usn) | [Change journal](05-ChangeJournal.md) |
| `Meziantou.Framework.Win32.CredentialManager` | [CredentialManager](cref:Meziantou.Framework.Win32.CredentialManager), [Credential](cref:Meziantou.Framework.Win32.Credential) | [Credential Manager](06-CredentialManager.md) |
| `Meziantou.Framework.Win32.Dialogs` | [OpenFolderDialog](cref:Meziantou.Framework.Win32.OpenFolderDialog) | [Folder dialog](07-FolderDialog.md) |
| `Meziantou.Framework.Win32.Jobs` | [JobObject](cref:Meziantou.Framework.Win32.JobObject), [JobObjectLimits](cref:Meziantou.Framework.Win32.JobObjectLimits) | [Job objects](08-JobObjects.md) |
| `Meziantou.Framework.Win32.Lsa` | [LsaPrivateData](cref:Meziantou.Framework.Win32.LsaPrivateData) | [LSA private data](09-LsaPrivateData.md) |
| `Meziantou.Framework.Win32.MarkOfTheWeb` | [MarkOfTheWeb](cref:Meziantou.Framework.Win32.MarkOfTheWeb), [ZoneIdentifier](cref:Meziantou.Framework.Win32.ZoneIdentifier), [UrlZone](cref:Meziantou.Framework.Win32.UrlZone) | [Mark of the Web](10-MarkOfTheWeb.md) |
| `Meziantou.Framework.Win32.PerceivedType` | [Perceived](cref:Meziantou.Framework.Win32.Perceived), [PerceivedType](cref:Meziantou.Framework.Win32.PerceivedType) | [Perceived types](11-PerceivedTypes.md) |
| `Meziantou.Framework.Win32.ProjectedFileSystem` | [ProjectedFileSystemBase](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase), [ProjectedFileSystemEntry](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemEntry) | [Projected file system](12-ProjectedFileSystem/01-Overview.md) |
| `Meziantou.Framework.Win32.RecentDocuments` | [RecentDocuments](cref:Meziantou.Framework.Win32.RecentDocuments) | [Recent documents](13-RecentDocuments.md) |
| `Meziantou.Framework.Win32.RestartManager` | [RestartManager](cref:Meziantou.Framework.Win32.RestartManager), [RestartManagerProcessInfo](cref:Meziantou.Framework.Win32.RestartManagerProcessInfo) | [Restart Manager](14-RestartManager.md) |

Each package is independent: install only the ones you need. WPF applications can also use the
[WPF](../WPF/01-Introduction.md) package, which provides commands, markup extensions and converters.

## Next steps

- [Getting started](02-GettingStarted.md): install a package and call it safely from an application that also runs on
  Linux or macOS.
