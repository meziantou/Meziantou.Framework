---
title: Reading files
description: Open an Outlook data file, find a folder and enumerate the items of every folder.
---

## Open a file

[PstFile.Open](cref:Meziantou.Framework.Mail.PstFile.Open) opens a file from a path, a stream or bytes. The objects it
returns read the file when they are accessed, so a large file opens immediately, and the file must stay open while they are
used. [GetFolder](cref:Meziantou.Framework.Mail.PstFile.GetFolder) finds a folder from its path, the display names of the
folders from the top of the hierarchy:

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstFile_Open;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
Assert.Equal(PstFormat.Unicode, file.Format);
Assert.Equal("Outlook Data File", file.Store.DisplayName);

var folder = file.GetFolder("Inbox/tmp");
Assert.NotNull(folder);
Assert.Equal(4, folder.ContentCount);
```
<!-- endref -->

## Enumerate the items

[EnumerateItems](cref:Meziantou.Framework.Mail.PstFolder.EnumerateItems) reads the items of a folder one at a time, and
[Folders](cref:Meziantou.Framework.Mail.PstFolder.Folders) returns its subfolders.
[RootFolder](cref:Meziantou.Framework.Mail.PstFile.RootFolder) is the root of the file, which contains the top of the folder
hierarchy (`Top of Personal Folders`) and the search folders:

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstFolder_EnumerateItems;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var subjects = new List<string>();
void Visit(PstFolder folder)
{
    foreach (var item in folder.EnumerateItems())
    {
        subjects.Add($"{folder.Path}: {item.Subject}");
    }

    foreach (var child in folder.Folders)
    {
        Visit(child);
    }
}

Visit(file.RootFolder);
Assert.Contains("Top of Outlook data file/Inbox/tmp: original email", subjects);
```
<!-- endref -->

The items are [PstMessage](cref:Meziantou.Framework.Mail.PstMessage) objects, or one of the derived types for the items
Outlook types: see [Contacts, calendar and tasks](04-ContactsCalendarTasks.md).

A [PstFile](cref:Meziantou.Framework.Mail.PstFile) is not thread-safe: use one instance per thread.
