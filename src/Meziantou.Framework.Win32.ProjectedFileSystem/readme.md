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

Create a virtual file system by inheriting from `ProjectedFileSystemBase` and implementing the required abstract methods:

```csharp
using Meziantou.Framework.Win32.ProjectedFileSystem;

public class MyVirtualFileSystem : ProjectedFileSystemBase
{
    public MyVirtualFileSystem(string rootFolder) : base(rootFolder)
    {
    }

    // Return the list of files and folders for a given path
    protected override IEnumerable<ProjectedFileSystemEntry> GetEntries(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            yield return ProjectedFileSystemEntry.Directory("folder");
            yield return ProjectedFileSystemEntry.File("file1.txt", length: 100);
            yield return ProjectedFileSystemEntry.File("file2.txt", length: 200);
        }
        else if (AreFileNamesEqual(path, "folder"))
        {
            yield return ProjectedFileSystemEntry.File("nested.txt", length: 50);
        }
    }

    // Return a stream to read the file content
    protected override Stream OpenRead(string path)
    {
        if (AreFileNamesEqual(path, "file1.txt"))
        {
            return new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        }
        else if (AreFileNamesEqual(path, "file2.txt"))
        {
            return new MemoryStream(Encoding.UTF8.GetBytes("Another file content"));
        }
        else if (AreFileNamesEqual(path, "folder\\nested.txt"))
        {
            return new MemoryStream(Encoding.UTF8.GetBytes("Nested file"));
        }

        return null;
    }
}

// Start the virtual file system
var rootPath = @"C:\MyVirtualFS";
Directory.CreateDirectory(rootPath);

using var vfs = new MyVirtualFileSystem(rootPath);
vfs.Start(options: null);

// The files are now accessible through Windows Explorer or any application
var content = File.ReadAllText(Path.Combine(rootPath, "file1.txt")); // "Hello, World!"
```

### Advanced Options

Configure the virtual file system with start options:

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
            PRJ_NOTIFY_TYPES.PRE_DELETE)
    }
};

vfs.Start(options);
```

## Key Classes

### `ProjectedFileSystemBase`

The base class for creating a virtual file system. Override these methods:

- **`GetEntries(string path)`**: Returns the files and folders for a given directory path
- **`OpenRead(string path)`**: Returns a stream to read file content
- **`GetEntry(string path)`** (optional): Returns metadata for a specific file or folder

Protected helper methods:

- **`FileNameMatch(string fileName, string pattern)`**: Checks if a filename matches a pattern
- **`AreFileNamesEqual(string fileName1, string fileName2)`**: Case-insensitive file name comparison
- **`CompareFileName(string fileName1, string fileName2)`**: Compare file names with proper sorting
- **`ClearNegativePathCache()`**: Clear the negative path cache
- **`DeleteFile(string relativePath, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)`**: Delete a file from the projection

### `ProjectedFileSystemEntry`

Represents a file or folder in the virtual file system:

```csharp
// Create a file entry
var file = ProjectedFileSystemEntry.File("example.txt", length: 1024);

// Create a folder entry
var folder = ProjectedFileSystemEntry.Directory("MyFolder");
```

Properties:
- `Name`: File or folder name
- `IsDirectory`: Whether this is a directory
- `Length`: File size in bytes (0 for directories)

### `ProjectedFileSystemStartOptions`

Configuration options when starting the file system:

- **`UseNegativePathCache`**: Cache queries for non-existent paths to improve performance
- **`Notifications`**: List of notification subscriptions

### `PRJ_FILE_STATE` (Enum)

Query the on-disk state of a file:

```csharp
var state = ProjectedFileSystemBase.GetOnDiskFileState(@"C:\MyVirtualFS\file.txt");
```

States:
- `PRJ_FILE_STATE_PLACEHOLDER`: Virtual file (not yet hydrated)
- `PRJ_FILE_STATE_HYDRATED_PLACEHOLDER`: File content downloaded
- `PRJ_FILE_STATE_DIRTY_PLACEHOLDER`: Modified placeholder
- `PRJ_FILE_STATE_FULL`: Full file (no longer managed by ProjFS)
- `PRJ_FILE_STATE_TOMBSTONE`: Deleted placeholder

# Additional Resources

- [Windows Projected File System Documentation](https://docs.microsoft.com/en-us/windows/desktop/projfs/projected-file-system?WT.mc_id=DT-MVP-5003978)
- [Microsoft ProjFS Sample](https://github.com/Microsoft/Windows-classic-samples/tree/master/Samples/ProjectedFileSystem?WT.mc_id=DT-MVP-5003978)
