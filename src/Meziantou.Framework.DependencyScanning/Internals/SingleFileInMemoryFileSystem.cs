using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>A read-only file system holding the single file scanned from memory.</summary>
/// <remarks>The locations found in this file are not updatable, because there is no file to write.</remarks>
internal sealed class SingleFileInMemoryFileSystem : IFileSystem
{
    private readonly string _path;
    private readonly byte[] _content;

    public SingleFileInMemoryFileSystem(string path, byte[] content)
    {
        _path = path;
        _content = content;
    }

    public Stream OpenRead(string path)
    {
        if (path == _path)
            return new MemoryStream(_content, writable: false);

        throw new FileNotFoundException("File not found", path);
    }

    public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions)
    {
        // The only file known by this file system is the in-memory file
        var fileDirectory = Path.GetDirectoryName(_path.AsSpan());
        var searchDirectory = Path.TrimEndingDirectorySeparator(path.AsSpan());
        var isInSearchDirectory = fileDirectory.Equals(searchDirectory, StringComparison.Ordinal);
        if (!isInSearchDirectory && searchOptions is SearchOption.AllDirectories)
        {
            isInSearchDirectory = fileDirectory.Length > searchDirectory.Length
                && fileDirectory.StartsWith(searchDirectory, StringComparison.Ordinal)
                && (Path.EndsInDirectorySeparator(searchDirectory) || IsDirectorySeparator(fileDirectory[searchDirectory.Length]));
        }

        if (isInSearchDirectory && FileSystemName.MatchesWin32Expression(FileSystemName.TranslateWin32Expression(pattern), Path.GetFileName(_path.AsSpan()), ignoreCase: false))
            return [_path];

        return [];

        static bool IsDirectorySeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    public Stream OpenReadWrite(string path) => throw new NotSupportedException($"Cannot open '{path}' for writing: the dependencies were scanned from in-memory content, so their locations cannot be updated");
}
