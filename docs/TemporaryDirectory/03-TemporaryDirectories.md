---
title: Temporary directories
description: Create files and folders in a temporary folder, keep paths inside it, pass it to other APIs and choose where it is created.
---

## Create files and folders

[TemporaryDirectory](cref:Meziantou.Framework.TemporaryDirectory) creates files and folders from paths relative to the temporary folder, and returns their
[FullPath](cref:Meziantou.Framework.FullPath):

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_Files;format=md-fenced-code-block;dedent=true -->
```csharp
using var directory = TemporaryDirectory.Create();

// The parent folders are created when needed
FullPath settings = directory.CreateTextFile("config/settings.json", "{}");
FullPath empty = directory.CreateEmptyFile("logs/app.log");
FullPath cache = directory.CreateDirectory("cache/images");

Assert.Equal(directory.FullPath / "config" / "settings.json", settings);
Assert.Equal(0, new FileInfo(empty).Length);
Assert.True(Directory.Exists(cache));

// GetFullPath and the / operator only compute a path: nothing is created
FullPath report = directory.GetFullPath("reports/today.txt");
Assert.Equal(directory / "reports" / "today.txt", report);
Assert.False(File.Exists(report));
```
<!-- endref -->

| Method | What it does |
| --- | --- |
| `CreateTextFile(relativePath, content)` | Writes a text file, replacing an existing one |
| `CreateTextFileAsync(relativePath, content, cancellationToken)` | The same, asynchronously |
| `CreateEmptyFile(relativePath)` | Creates an empty file, emptying an existing one |
| `CreateDirectory(relativePath)` | Creates a folder |
| `GetFullPath(relativePath)` | Returns the path, without creating anything |
| `/` operator | The same as [GetFullPath](cref:Meziantou.Framework.TemporaryDirectory.GetFullPath) |

- A relative path can contain several segments, separated by `/` or by the separator of the platform. The methods that
  create a file or a folder create the missing parent folders.
- [CreateTextFile](cref:Meziantou.Framework.TemporaryDirectory.CreateTextFile) and [CreateTextFileAsync](cref:Meziantou.Framework.TemporaryDirectory.CreateTextFileAsync) write the text as `File.WriteAllText` does, in UTF-8 without a byte order
  mark.

Asynchronous code can write files and dispose the folder asynchronously:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_FilesAsync;format=md-fenced-code-block;dedent=true -->
```csharp
await using var directory = TemporaryDirectory.Create();

var path = await directory.CreateTextFileAsync("data/notes.txt", "Hello");

Assert.Equal("Hello", await File.ReadAllTextAsync(path));
```
<!-- endref -->

## Paths stay inside the folder

Every method checks that the path resolves inside the temporary folder: a path with `..` segments that leave it, or a
rooted path, throws an `ArgumentException`. A path that comes from test data cannot write outside the folder by mistake:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_StayInside;format=md-fenced-code-block;dedent=true -->
```csharp
using var directory = TemporaryDirectory.Create();

// A path that resolves outside the directory is rejected
Assert.Throws<ArgumentException>(() => directory.GetFullPath("../outside.txt"));
Assert.Throws<ArgumentException>(() => directory.CreateTextFile("data/../../outside.txt", "content"));

// So is a rooted path
var rootedPath = Path.Combine(Path.GetTempPath(), "outside.txt");
Assert.Throws<ArgumentException>(() => directory / rootedPath);
```
<!-- endref -->

`..` segments that stay inside the folder, such as `data/../output.txt`, are allowed.

## Pass the folder to other APIs

[TemporaryDirectory](cref:Meziantou.Framework.TemporaryDirectory) converts implicitly to `string`, to [FullPath](cref:Meziantou.Framework.FullPath) and to `DirectoryInfo`, so you can pass the
instance itself where an API expects a folder:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_Conversions;format=md-fenced-code-block;dedent=true -->
```csharp
using var directory = TemporaryDirectory.Create();
directory.CreateEmptyFile("a.txt");
directory.CreateEmptyFile("b.txt");

// A TemporaryDirectory converts implicitly to string, FullPath and DirectoryInfo
string[] files = Directory.GetFiles(directory);
FullPath path = directory;
DirectoryInfo info = directory;

Assert.HasCount(2, files);
Assert.Equal(directory.FullPath, path);
Assert.Equal(directory.FullPath.Value, info.FullName);
```
<!-- endref -->

The [FullPath](cref:Meziantou.Framework.FullPath) property gives the path explicitly. It stays readable after the instance is disposed, although the folder
no longer exists. See [FullPath](../FullPath/01-Introduction.md) to combine, compare and inspect the paths.

## Choose where the folder is created

By default, [TemporaryDirectory.Create](cref:Meziantou.Framework.TemporaryDirectory.Create) creates the folder in a `MezTD` folder of the temporary folder of the user
(`Path.GetTempPath()`). Pass another folder to create it there instead:

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_RootDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
// Create the directory in a folder of your choice instead of the shared one
FullPath root = FullPath.GetTempPath() / "MyApp.Tests";
using var directory = TemporaryDirectory.Create(root);

Assert.Equal(root, directory.FullPath.Parent);
```
<!-- endref -->

- The root folder is created if it does not exist. It is not deleted: only the unique folder created in it is.
- The name of the unique folder is the UTC date and time (`yyyyMMdd_HHmmss`), an underscore and a GUID.
- On Linux and macOS, only the current user can access the root folder. When it already exists and other users can
  access it, its permissions are tightened. When it belongs to another user, [Create](cref:Meziantou.Framework.TemporaryDirectory.Create) throws an
  `UnauthorizedAccessException`: choose a root folder of your own.

## Deletion

Disposing the instance ([Dispose](cref:Meziantou.Framework.TemporaryDirectory.Dispose) or [DisposeAsync](cref:Meziantou.Framework.TemporaryDirectory.DisposeAsync)) deletes the folder and everything it contains:

- Read-only files are deleted: their read-only attribute is removed first.
- A file that another process holds open is retried a few times, a short time apart, before the error is reported.
- A symbolic link in the folder is deleted as a link: the file or folder it points to is not deleted.
- Disposing an instance twice does nothing. A folder created again at the same path, after the first disposal, is not
  deleted.
- After disposal, the methods that create or compute a path throw an `ObjectDisposedException`.

## Windows Explorer

On Windows, [OpenInExplorer](cref:Meziantou.Framework.TemporaryDirectory.OpenInExplorer) opens the folder in Windows Explorer, which helps to look at the files while debugging a
test.
