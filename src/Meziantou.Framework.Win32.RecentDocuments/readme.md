# Meziantou.Framework.Win32.RecentDocuments

`Meziantou.Framework.Win32.RecentDocuments` is a wrapper for manipulating recent documents in Windows.

```c#
// Add a document to Recent Items and to the Jump List of the current application.
// A relative path is resolved against the current directory.
Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(@"C:\Users\Public\Documents\file.txt");

// Add a document to the Jump List of the application identified by an AppUserModelID (Windows 7+).
// Use it when the application sets an explicit AppUserModelID, or runs through "dotnet app.dll".
Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(@"C:\Users\Public\Documents\file.txt", "Company.Product.App");

// Warning: clears Recent Items and the Recent/Frequent lists of every application's Jump List, not only yours.
Meziantou.Framework.Win32.RecentDocuments.ClearRecentDocuments();
```

Executable (`.exe`) files are accepted but never appear in Recent Items, and folders only appear in the File Explorer Jump List.
