using System.Collections.Concurrent;
using Meziantou.Framework.Win32.ProjectedFileSystem;

namespace Meziantou.Framework.Win32.ProjectedFileSystemSamples;

#region ProjectedFileSystem_Refresh
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
#endregion
