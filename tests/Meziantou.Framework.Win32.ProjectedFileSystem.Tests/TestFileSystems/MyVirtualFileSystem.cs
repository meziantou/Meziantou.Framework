namespace Meziantou.Framework.Win32.ProjectedFileSystem;

// Example of the readme (src/Meziantou.Framework.Win32.ProjectedFileSystem/readme.md)
#region ProjectedFileSystem_MyVirtualFileSystem
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
#endregion
