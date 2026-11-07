---
title: Implementing a provider
description: Derive from ProjectedFileSystemBase, return the entries of each folder and the content of each file.
---

## Derive from ProjectedFileSystemBase

A provider overrides two methods: [GetEntriesAsync](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.GetEntriesAsync) returns the files and folders of a folder, and [OpenReadAsync](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.OpenReadAsync)
returns the content of a file:

<!-- ref:../../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/TestFileSystems/MyVirtualFileSystem.cs;region=ProjectedFileSystem_MyVirtualFileSystem;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class MyVirtualFileSystem : ProjectedFileSystemBase
{
    public MyVirtualFileSystem(string rootFolder) : base(rootFolder)
    {
    }

    // Return the list of files and folders for a given path.
    // The length of a file must be the exact size of the content returned by OpenReadAsync.
    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(
            [
                ProjectedFileSystemEntry.Directory("folder"),
                ProjectedFileSystemEntry.File("file1.txt", length: 13),
                ProjectedFileSystemEntry.File("file2.txt", length: 20),
            ]);
        }

        if (AreFileNamesEqual(path, "folder"))
        {
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(
            [
                ProjectedFileSystemEntry.File("nested.txt", length: 11),
            ]);
        }

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    // Return a stream to read the file content
    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, "file1.txt"))
        {
            return ValueTask.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!")));
        }
        else if (AreFileNamesEqual(path, "file2.txt"))
        {
            return ValueTask.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("Another file content")));
        }
        else if (AreFileNamesEqual(path, "folder\\nested.txt"))
        {
            return ValueTask.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("Nested file")));
        }

        return ValueTask.FromResult<Stream?>(null);
    }
}
```
<!-- endref -->

Start it on an empty folder, and the files appear:

<!-- ref:../../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_Start;format=md-fenced-code-block;dedent=true -->
```csharp
// Start the virtual file system
var rootPath = Path.Combine(Path.GetTempPath(), "MyVirtualFS", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(rootPath);

using var vfs = new MyVirtualFileSystem(rootPath);
vfs.Start(options: null);

// The files are now accessible through Windows Explorer or any application
var content = File.ReadAllText(Path.Combine(rootPath, "file1.txt"));
Assert.Equal("Hello, World!", content);
Assert.Equal("Nested file", File.ReadAllText(Path.Combine(rootPath, "folder", "nested.txt")));
```
<!-- endref -->

## Paths

The methods receive paths relative to the root folder, with `\` as the separator: `""` is the root itself,
`folder\nested.txt` a file in a subfolder. Windows compares file names without regard to case. Compare the names with
the protected helpers of the base class, which follow the rules of Windows:

| Helper | Description |
| --- | --- |
| `AreFileNamesEqual(name1, name2)` | Whether two names are equal, ignoring case |
| `CompareFileName(name1, name2)` | Compares two names in the order Windows sorts them |
| `FileNameMatch(name, pattern)` | Whether a name matches a pattern with the `*` and `?` wildcards |

## Entries

`ProjectedFileSystemEntry.File(name, length)` describes a file and `ProjectedFileSystemEntry.Directory(name)` a folder:

<!-- ref:../../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_Entries;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a file entry
var file = ProjectedFileSystemEntry.File("example.txt", length: 1024);
Assert.False(file.IsDirectory);
Assert.Equal(1024, file.Length);

// Create a folder entry
var folder = ProjectedFileSystemEntry.Directory("MyFolder");
Assert.True(folder.IsDirectory);
Assert.Equal("MyFolder", folder.Name);
```
<!-- endref -->

- The length of a file must be the exact size of the content that [OpenReadAsync](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.OpenReadAsync) returns: Windows creates the file
  with this size. When the stream is shorter, reading the file fails with an `IOException` instead of returning
  truncated content.
- Return the entries in any order: the base class sorts them and applies the search pattern of the application that
  lists the folder.
- Return an empty list for a folder that has no entries or does not exist.

## Content

[OpenReadAsync](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.OpenReadAsync) returns a new stream positioned at the start of the content, or `null` when the file does not exist.
The base class disposes the stream.

Windows can ask for the content of a file in several ranges: [OpenReadAsync](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.OpenReadAsync) is then called for each range, and the
base class moves to the start of the range. The stream does not need to be seekable: the base class then reads and
skips the bytes before the range, which is slower for large files. The data is copied in chunks of [BufferSize](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.BufferSize) bytes,
4,096 by default, which a derived class can change.

Both methods return a `ValueTask`, so they can be asynchronous, for instance to query a database or a remote service.

## Get the entry of a path

When Windows needs the metadata of a single file or folder, the base class calls the virtual `GetEntryAsync(path)`
method. Its default implementation lists the parent folder with [GetEntriesAsync](cref:Meziantou.Framework.Win32.ProjectedFileSystem.ProjectedFileSystemBase.GetEntriesAsync) and looks for the name. Override it
when your store can find an entry directly, for instance in a large folder. It returns `null` when the path does not
exist.
