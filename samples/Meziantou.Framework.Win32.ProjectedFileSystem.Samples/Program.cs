using System.Runtime.Versioning;
using Meziantou.Framework.Win32.ProjectedFileSystem;

[assembly: SupportedOSPlatform("windows10.0.17763")]

// The example is a complete program: the file system type follows the top-level statements
#pragma warning disable CA1050, MA0047, MA0048

#region ProjectedFileSystemBase_Usage
var rootPath = @"C:\MyVirtualFS";
Directory.CreateDirectory(rootPath);
using var vfs = new MyVirtualFileSystem(rootPath);
vfs.Start(options: null);

public class MyVirtualFileSystem(string rootFolder) : ProjectedFileSystemBase(rootFolder)
{
    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(
            [
                ProjectedFileSystemEntry.File("file.txt", length: 13),
                ProjectedFileSystemEntry.Directory("folder"),
            ]);
        }

        return ValueTask.FromResult(Enumerable.Empty<ProjectedFileSystemEntry>());
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, "file.txt"))
            return ValueTask.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!")));

        return ValueTask.FromResult<Stream?>(null);
    }
}
#endregion
