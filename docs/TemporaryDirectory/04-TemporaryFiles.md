---
title: Temporary files
description: Create a temporary file with a generated name, a name of your choice or at a path of your choice, and delete it on disposal.
---

## Create a temporary file

[TemporaryFile.Create](cref:Meziantou.Framework.TemporaryFile.Create) creates an empty file and returns a [TemporaryFile](cref:Meziantou.Framework.TemporaryFile) that deletes it when disposed. It has three
overloads:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryFile_Create;format=md-fenced-code-block;dedent=true -->
```csharp
// Generated name under the system temp folder
using var temporaryFile = TemporaryFile.Create();
File.WriteAllText(temporaryFile.FullPath, "content");

// Choose the file name; a unique parent folder is created for it
using var namedFile = TemporaryFile.Create("custom.txt");

// Choose the full path. The file must not already exist.
using var atPath = TemporaryFile.Create(FullPath.Combine(Path.GetTempPath(), $"custom-{Guid.NewGuid():N}.txt"));
```
<!-- endref -->

| Overload | Where the file is created | What disposal deletes |
| --- | --- | --- |
| [Create()](cref:Meziantou.Framework.TemporaryFile.Create) | In a `MezTF` folder of the temporary folder, with a generated `.tmp` name | The file |
| `Create(string fileNameOrPath)` with a relative path | In a new unique folder of `MezTF`, with the name you choose | The file and the unique folder |
| `Create(string fileNameOrPath)` with a rooted path, or `Create(FullPath)` | At the path you choose | The file |

- The generated names contain the UTC date and time and a GUID, so they are unique.
- A relative path can contain folders, such as `data/input.csv`: they are created in the unique folder, and deleted
  with it.
- On Linux and macOS, only the current user can read and write the file.

## Choose the file name

Tools often expect a given file name or extension. Pass the name: the file is created in a folder of its own, so the name
cannot collide with another file, and that folder is deleted with the file:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryFile_Lifetime;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath filePath;
using (var file = TemporaryFile.Create("report.csv"))
{
    filePath = file.FullPath;

    // The file exists and is empty; a unique folder holds it, so the name can be chosen freely
    Assert.Equal("report.csv", file.FullPath.Name);
    Assert.Equal(0, new FileInfo(file).Length);

    File.WriteAllText(file, "id,name");
}

// Disposing the instance deletes the file and the folder created for it
Assert.False(File.Exists(filePath));
Assert.False(Directory.Exists(filePath.Parent));
```
<!-- endref -->

[TemporaryFile](cref:Meziantou.Framework.TemporaryFile) converts implicitly to `string`, to [FullPath](cref:Meziantou.Framework.FullPath) and to `FileInfo`, so the instance itself can be passed to
`File.WriteAllText` or to any API that takes a path.

## Create the file at a given path

Pass a full path to create the file exactly there. The file must not exist yet: an existing file is never adopted, so
disposing the instance cannot delete data it did not create:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryFile_AtPath;format=md-fenced-code-block;dedent=true -->
```csharp
// Create the file at a path of your choice. The file must not exist yet.
FullPath path = FullPath.GetTempPath() / $"output-{Guid.NewGuid():N}.json";
using (var file = TemporaryFile.Create(path))
{
    Assert.True(File.Exists(path));
    Assert.Throws<IOException>(() => TemporaryFile.Create(path));
}

Assert.False(File.Exists(path));
```
<!-- endref -->

- An existing file throws an `IOException`, an empty path or file name an `ArgumentException`.
- The missing parent folders are created. Disposal deletes the file only, not those folders.

## Several files

When you need several files, or files whose names you do not control, use a
[temporary directory](03-TemporaryDirectories.md): every file in it is deleted with it.

To create a file with a unique name that you delete yourself, use [FullPath.CreateTempFile](cref:Meziantou.Framework.FullPath.CreateTempFile): see
[Create a temporary file](../FullPath/03-CreatingPaths.md#create-a-temporary-file).
