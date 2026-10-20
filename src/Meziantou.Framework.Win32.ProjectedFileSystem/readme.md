# Meziantou.Framework.Win32.ProjectedFileSystem

A .NET wrapper for the Windows Projected File System (ProjFS) API, enabling you to create virtual file systems that appear as normal files and folders to users and applications.

## Requirements

- 64-bit process only
- Windows 10 version 1809 or later (with the "Projected File System" optional feature installed)

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart
```

## Usage

### Basic Example

Create a virtual file system by inheriting from `ProjectedFileSystemBase` (in the `Meziantou.Framework.Win32.ProjectedFileSystem`
namespace) and implementing the required abstract methods:

<!-- ref:../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/TestFileSystems/MyVirtualFileSystem.cs;region=ProjectedFileSystem_MyVirtualFileSystem;format=md-fenced-code-block;dedent=true -->
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

Start the virtual file system:

<!-- ref:../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_Start;format=md-fenced-code-block;dedent=true -->
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

### Advanced Options

Configure the virtual file system with start options:

<!-- ref:../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_StartOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ProjectedFileSystemStartOptions
{
    // Enable caching of non-existent file paths for better performance
    UseNegativePathCache = true,

    // Subscribe to file system notifications
    Notifications =
    {
        new Notification(
            PRJ_NOTIFY_TYPES.FILE_OPENED |
            PRJ_NOTIFY_TYPES.NEW_FILE_CREATED |
            PRJ_NOTIFY_TYPES.FILE_RENAMED |
            PRJ_NOTIFY_TYPES.PRE_DELETE),
    },
};

vfs.Start(options);
```
<!-- endref -->

## Key Classes

### `ProjectedFileSystemBase`

The base class for creating a virtual file system. Override these methods:

- **`GetEntriesAsync(string path)`**: Returns the files and folders for a given directory path
- **`OpenReadAsync(string path)`**: Returns a stream to read file content
- **`GetEntryAsync(string path)`** (optional): Returns metadata for a specific file or folder

Protected helper methods:

- **`FileNameMatch(string fileName, string pattern)`**: Checks if a filename matches a pattern
- **`AreFileNamesEqual(string fileName1, string fileName2)`**: Case-insensitive file name comparison
- **`CompareFileName(string fileName1, string fileName2)`**: Compare file names with proper sorting
- **`ClearNegativePathCache()`**: Clear the negative path cache
- **`DeleteFile(string relativePath, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)`**: Delete a file from the projection

### `ProjectedFileSystemEntry`

Represents a file or folder in the virtual file system:

<!-- ref:../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_Entries;format=md-fenced-code-block;dedent=true -->
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

Properties:
- `Name`: File or folder name
- `IsDirectory`: Whether this is a directory
- `Length`: File size in bytes (0 for directories)

### `ProjectedFileSystemStartOptions`

Configuration options when starting the file system:

- **`UseNegativePathCache`**: Cache queries for non-existent paths to improve performance
- **`Notifications`**: List of notification subscriptions

### `PRJ_FILE_STATE` (Enum)

Query the on-disk state of a file of the virtual file system started above:

<!-- ref:../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_GetOnDiskFileState;format=md-fenced-code-block;dedent=true -->
```csharp
var state = ProjectedFileSystemBase.GetOnDiskFileState(Path.Combine(rootPath, "file1.txt"));
Assert.True(state.HasFlag(PRJ_FILE_STATE.PRJ_FILE_STATE_HYDRATED_PLACEHOLDER)); // The content was read
```
<!-- endref -->

States:
- `PRJ_FILE_STATE_PLACEHOLDER`: Virtual file (not yet hydrated)
- `PRJ_FILE_STATE_HYDRATED_PLACEHOLDER`: File content downloaded
- `PRJ_FILE_STATE_DIRTY_PLACEHOLDER`: Modified placeholder
- `PRJ_FILE_STATE_FULL`: Full file (no longer managed by ProjFS)
- `PRJ_FILE_STATE_TOMBSTONE`: Deleted placeholder

# Additional Resources

- [Windows Projected File System Documentation](https://docs.microsoft.com/en-us/windows/desktop/projfs/projected-file-system?WT.mc_id=DT-MVP-5003978)
- [Microsoft ProjFS Sample](https://github.com/Microsoft/Windows-classic-samples/tree/master/Samples/ProjectedFileSystem?WT.mc_id=DT-MVP-5003978)
