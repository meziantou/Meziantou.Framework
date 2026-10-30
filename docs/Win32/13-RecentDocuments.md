---
title: Recent documents
description: Add the documents your application opens to the Recent Items of Windows and to the Jump List of the application.
---

When an application opens a document, it can tell Windows, which adds the document to the Recent Items of the user and
to the Jump List of the application on the taskbar. `RecentDocuments` from the
`Meziantou.Framework.Win32.RecentDocuments` package does it.

```bash
dotnet add package Meziantou.Framework.Win32.RecentDocuments
```

The example of this page changes the recent documents of the user: it is compiled, not run, by the build of the
repository.

## Add a document

<!-- ref:../../samples/Meziantou.Framework.Win32.RecentDocumentsConsole/ReadmeExamples.cs;region=RecentDocuments_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Add a document to Recent Items and to the Jump List of the current application.
// A relative path is resolved against the current directory.
Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(@"C:\Users\Public\Documents\file.txt");

// Add a document to the Jump List of the application identified by an AppUserModelID (Windows 7+).
// Use it when the application sets an explicit AppUserModelID, or runs through "dotnet app.dll".
Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(@"C:\Users\Public\Documents\file.txt", "Company.Product.App");

// Warning: clears Recent Items and the Recent/Frequent lists of every application's Jump List, not only yours.
Meziantou.Framework.Win32.RecentDocuments.ClearRecentDocuments();
```
<!-- endref -->

- `AddToRecentDocuments(path)` adds the document to Recent Items and to the Jump List of the application of the current
  process. A relative path is resolved against the current directory.
- When the Jump List belongs to another identity than the current process, for instance when the application sets an
  explicit Application User Model ID or is started through `dotnet app.dll`, pass the AppUserModelID of the
  application as the second argument. This overload requires Windows 7, and the path must identify an existing item.
- Executable (`.exe`) files are accepted but never appear in Recent Items, and folders only appear in the Jump List of
  File Explorer.
- When the application is not registered to open the type of the file, Windows may register it for that type, for the
  current user.

`AddToRecentDocuments` throws an `ArgumentException` when the path is empty or contains a null character, and, with an
AppUserModelID, when the ID is empty, longer than 128 characters, or contains a space.

## Clear the recent documents

`ClearRecentDocuments` is not limited to your application: it empties the Recent Items of the user and the Recent and
Frequent lists of the Jump Lists of every application. It cannot be undone, so only call it when the user asks for it.
