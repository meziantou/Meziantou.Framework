---
title: Platform-specific features
description: Reserved device names and extended-length paths on Windows, Windows known folders, and showing a file in the file manager.
---

## Reserved device names on Windows

On Windows, a path that contains a reserved device name, such as `CON`, `PRN`, `AUX`, `NUL`, `COM1` or `LPT1`, with or
without an extension, does not name a file: `NUL.txt` opens the null device. `Value`, `ToString()` and the conversion to
`string` return such paths with the `\\?\` prefix, so the file APIs open the file:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_ReservedDeviceNames;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath file = FullPath.FromPath(@"C:\logs\NUL.txt");

// Value adds the \\?\ prefix, so the file APIs open the file instead of the NUL device
Assert.Equal(@"\\?\C:\logs\NUL.txt", file.Value);
Assert.Equal(@"C:\logs\NUL.txt", file.RawValue);
```
<!-- endref -->

`RawValue` returns the path without the prefix. Use it to show the path to a user, or for an API that handles the
device names itself. On the other operating systems, `Value` and `RawValue` are the same.

## Extended-length paths on Windows

`ToWindowsExtendedPath` returns the path with the `\\?\` prefix, which lets the Windows file APIs accept paths longer
than 260 characters. A UNC path becomes `\\?\UNC\server\share\…`. Conversely, `FromPath` removes the prefix:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_WindowsExtendedPath;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(@"\\?\C:\temp\test.txt", FullPath.FromPath(@"C:\temp\test.txt").ToWindowsExtendedPath());
Assert.Equal(@"\\?\UNC\server\share\file.txt", FullPath.FromPath(@"\\server\share\file.txt").ToWindowsExtendedPath());

// FromPath removes the prefix
Assert.Equal(@"C:\temp\test.txt", FullPath.FromPath(@"\\?\C:\temp\test.txt").Value);
Assert.Equal(@"\\server\share\file.txt", FullPath.FromPath(@"\\?\UNC\server\share\file.txt").Value);
```
<!-- endref -->

- `ToWindowsExtendedPath` throws a `PlatformNotSupportedException` on the other operating systems.
- On Linux and macOS, `\` is a valid file name character: `FromPath` does not treat `\\?\` as a prefix there.

## Known folders

On Windows, `FullPath.GetKnownFolderPath` returns the path of a known folder, such as the Downloads folder, which
`Environment.SpecialFolder` does not cover:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=KnownFolder_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Get path to Downloads folder
FullPath downloadsPath = FullPath.GetKnownFolderPath(KnownFolder.Downloads);
Assert.False(downloadsPath.IsEmpty);

// Get path to Documents folder
FullPath documentsPath = FullPath.GetKnownFolderPath(KnownFolder.Documents);
Assert.False(documentsPath.IsEmpty);
```
<!-- endref -->

- The `KnownFolder` class lists the folders as static properties. Each one has a `Name`, a `FolderId`, a `FolderType`
  and a `DefaultPath`.
- The environment variables of the folder path are expanded.
- The method throws a `Win32Exception` when Windows cannot return the folder.

## Show a file in the file manager

`OpenInExplorer` opens the file manager and selects the file or the folder: File Explorer on Windows, Finder on macOS.
It throws a `PlatformNotSupportedException` on the other operating systems, and an `InvalidOperationException` on the
empty path.
