using System.Collections.Generic;
using System.IO;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal sealed class FileSystem : IFileSystem
{
    private FileSystem()
    {
    }

    public static IFileSystem Instance { get; } = new FileSystem();

    public IEnumerable<string> GetFiles(string path, string pattern)
    {
        return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly);
    }

    public Stream OpenRead(string path)
    {
        return File.Open(path, FileMode.Open, FileAccess.Read);
    }

    public Stream OpenReadWrite(string path)
    {
        return File.Open(path, FileMode.Open, FileAccess.ReadWrite);
    }
}
