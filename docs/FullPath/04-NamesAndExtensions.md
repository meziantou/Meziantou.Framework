---
title: Names, extensions and parents
description: Read the name, the extension and the parent folder of a path, rename a file or change its extension.
---

## Read the parts of a path

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_NamesAndExtensions;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath file = FullPath.GetTempPath() / "reports" / "summary.tar.gz";

Assert.Equal("summary.tar.gz", file.Name);
Assert.Equal("summary.tar", file.NameWithoutExtension);
Assert.Equal(".gz", file.Extension);
Assert.Equal(FullPath.GetTempPath() / "reports", file.Parent);
```
<!-- endref -->

| Member | Returns | Equivalent string API |
| --- | --- | --- |
| [Name](cref:Meziantou.Framework.FullPath.Name) | The file or folder name, with its extension | `Path.GetFileName` |
| [NameWithoutExtension](cref:Meziantou.Framework.FullPath.NameWithoutExtension) | The name without its last extension | `Path.GetFileNameWithoutExtension` |
| [Extension](cref:Meziantou.Framework.FullPath.Extension) | The last extension, with its leading dot, or an empty string | `Path.GetExtension` |
| [Parent](cref:Meziantou.Framework.FullPath.Parent) | The folder that contains the path | `Path.GetDirectoryName` |

[Name](cref:Meziantou.Framework.FullPath.Name), [NameWithoutExtension](cref:Meziantou.Framework.FullPath.NameWithoutExtension) and [Extension](cref:Meziantou.Framework.FullPath.Extension) return a `string`; [Parent](cref:Meziantou.Framework.FullPath.Parent) returns a [FullPath](cref:Meziantou.Framework.FullPath). As the path has no
trailing separator, the name of a folder is its last segment.

## The parent of a root folder

A root folder, such as `/` or `C:\`, has no parent: [Parent](cref:Meziantou.Framework.FullPath.Parent) returns the empty path, where `Path.GetDirectoryName`
returns `null`:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_ParentOfRoot;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath root = FullPath.FromPath(Path.GetPathRoot(Environment.CurrentDirectory)!);

// A root folder (/ or C:\) has no parent
Assert.True(root.Parent.IsEmpty);
```
<!-- endref -->

A loop that walks up the folders stops when `Parent.IsEmpty` is `true`. To find the first folder that matches a
condition, use the methods of [Finding ancestors](06-FindingAncestors.md).

## Change the extension or the name

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_ChangeNameAndExtension;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath file = FullPath.GetTempPath() / "reports" / "summary.tar.gz";

// Replace the last extension, all the extensions, or a number of them
Assert.Equal("summary.tar.zip", file.WithExtension(".zip").Name);
Assert.Equal("summary.zip", file.WithExtension(".zip", replaceAllTrailingExtensions: true).Name);
Assert.Equal("summary.zip", file.WithExtension("zip", extensionCount: 2).Name); // the leading dot is optional

// null removes the extension
Assert.Equal("summary.tar", file.WithExtension(null).Name);

// Rename the file and keep its folder
Assert.Equal(FullPath.GetTempPath() / "reports" / "readme.md", file.WithName("readme.md"));
Assert.Equal("details.gz", file.WithNameWithoutExtension("details").Name);
```
<!-- endref -->

- `WithExtension(extension)` replaces the last extension, like `Path.ChangeExtension`. The leading dot of the new
  extension is optional, and `null` removes the extension.
- `WithExtension(extension, replaceAllTrailingExtensions: true)` replaces all the extensions, such as `.tar.gz`.
  `WithExtension(extension, extensionCount)` replaces the given number of extensions; the count must be greater than 0.
- [WithName](cref:Meziantou.Framework.FullPath.WithName) replaces the name and keeps the folder. [WithNameWithoutExtension](cref:Meziantou.Framework.FullPath.WithNameWithoutExtension) replaces the name and keeps the
  extension.
- These methods return a new path: they do not rename the file on disk. Pass both paths to `File.Move` to rename it.

## Create the parent folder

[CreateParentDirectory](cref:Meziantou.Framework.FullPath.CreateParentDirectory) creates the folder that contains the path, and its own parents, when they do not exist. Call
it before you create a file in a folder that may not exist yet. It does nothing on the empty path and on a root folder.
