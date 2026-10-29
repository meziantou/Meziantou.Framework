---
title: Running the file system
description: Start and stop a projected file system, refresh the files whose content changed, and read the on-disk state of a file.
---

## Start the file system

Create the root folder, then call `Start`:

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

- The root folder must exist: `Start` throws a `FileNotFoundException` otherwise.
- Use a new, empty folder each time you create an instance. `Start` marks the folder as the root of a new projection,
  so a folder already used by a previous instance, even one of the same class that was stopped, is rejected with a
  `COMException` (`0x8007112B`).
- Calling `Start` on a running instance does nothing.

`Start` accepts options:

<!-- ref:../../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_StartOptions;format=md-fenced-code-block;dedent=true -->
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

- `UseNegativePathCache` lets Windows remember the paths that do not exist, so it stops asking your class about them.
  Call `ClearNegativePathCache` when a new file appears in your store.
- `Notifications` subscribes to the operations that applications perform in the projection, for the whole root or for
  a subfolder (`new Notification(path, types)`). The library does not expose the notifications to your class: it
  acknowledges them and lets the operations proceed.

## Stop the file system

`Stop`, or `Dispose`, stops the projection. It waits for the requests in progress, then Windows stops calling your
class. The files that were read stay in the folder with their content, while the entries that only existed in the
projection disappear from it.

Keep the instance alive for as long as the files must be available: while it runs, Windows calls it every time an
application lists a folder or reads a new file.

## Refresh a file whose content changed

Windows keeps a local copy of each file that was read, and serves the next reads from it. When the content changes in
your store, the folder keeps showing the old content until you delete the local copy with the protected `DeleteFile`
method. The next read then asks your class again:

<!-- ref:../../../samples/Meziantou.Framework.Win32.ProjectedFileSystem.Samples/DictionaryFileSystem.cs;region=ProjectedFileSystem_Refresh;format=md-fenced-code-block;dedent=true -->
```csharp
// A file system that projects the content of a dictionary, which the application can change at any time
public sealed class DictionaryFileSystem(string rootFolder) : ProjectedFileSystemBase(rootFolder)
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public void AddOrUpdateFile(string name, byte[] content)
    {
        var isNew = _files.TryAdd(name, content);
        if (isNew)
        {
            // Windows may remember that the name did not exist: forget it
            ClearNegativePathCache();
        }
        else
        {
            _files[name] = content;

            // Remove the local copy, so Windows asks for the new content the next time the file is read.
            // It fails, and failureReason tells why, when the local copy was modified.
            if (!DeleteFile(name, PRJ_UPDATE_TYPES.PRJ_UPDATE_NONE, out var failureReason))
            {
                Console.WriteLine($"Cannot refresh '{name}': {failureReason}");
            }
        }
    }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (!string.IsNullOrEmpty(path))
            return ValueTask.FromResult(Enumerable.Empty<ProjectedFileSystemEntry>());

        var entries = _files.Select(file => ProjectedFileSystemEntry.File(file.Key, file.Value.Length)).ToArray();
        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(entries);
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (_files.TryGetValue(path, out var content))
            return ValueTask.FromResult<Stream?>(new MemoryStream(content, writable: false));

        return ValueTask.FromResult<Stream?>(null);
    }
}
```
<!-- endref -->

- `DeleteFile` returns `false`, with the reason in `failureReason`, when the local copy is in a state that the update
  flags do not allow: `PRJ_UPDATE_NONE` only deletes placeholders, whether they were read or not. Add
  `PRJ_UPDATE_ALLOW_DIRTY_METADATA`, `PRJ_UPDATE_ALLOW_DIRTY_DATA`, `PRJ_UPDATE_ALLOW_TOMBSTONE` or
  `PRJ_UPDATE_ALLOW_READ_ONLY` to delete a file that an application changed, deleted or made read-only, and lose the
  change.
- `DeleteFile` and `ClearNegativePathCache` throw an `InvalidOperationException` when the file system is not started.

## Read the state of a file

`GetOnDiskFileState` tells what Windows stores locally for a path of a running projection:

<!-- ref:../../../tests/Meziantou.Framework.Win32.ProjectedFileSystem.Tests/ProjectedFileSystemTests.cs;region=ProjectedFileSystem_GetOnDiskFileState;format=md-fenced-code-block;dedent=true -->
```csharp
var state = ProjectedFileSystemBase.GetOnDiskFileState(Path.Combine(rootPath, "file1.txt"));
Assert.True(state.HasFlag(PRJ_FILE_STATE.PRJ_FILE_STATE_HYDRATED_PLACEHOLDER)); // The content was read
```
<!-- endref -->

| `PRJ_FILE_STATE` | The local file is |
| --- | --- |
| `PRJ_FILE_STATE_PLACEHOLDER` | A placeholder whose content was not read |
| `PRJ_FILE_STATE_HYDRATED_PLACEHOLDER` | A placeholder whose content was read and is stored locally |
| `PRJ_FILE_STATE_DIRTY_PLACEHOLDER` | A placeholder whose metadata an application changed |
| `PRJ_FILE_STATE_FULL` | A file that an application created or modified: ProjFS no longer manages it |
| `PRJ_FILE_STATE_TOMBSTONE` | The marker of an item that an application deleted |
