using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning.Fuzz;

/// <summary>A file system holding the files of one input. Paths use <c>/</c>; a directory exists when it contains a file. Writes are kept in memory.</summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public void AddFile(string path, byte[] content) => _files[Normalize(path)] = content;

    public byte[]? GetContent(string path) => _files.GetValueOrDefault(Normalize(path));

    public Stream OpenRead(string path)
    {
        if (!_files.TryGetValue(Normalize(path), out var content))
            throw new FileNotFoundException("File not found", path);

        return new MemoryStream(content, writable: false);
    }

    public Stream OpenReadWrite(string path)
    {
        var normalizedPath = Normalize(path);
        if (!_files.TryGetValue(normalizedPath, out var content))
            throw new FileNotFoundException("File not found", path);

        return new WritableFile(this, normalizedPath, content);
    }

    public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions)
    {
        var directory = Normalize(path).TrimEnd('/');
        var result = new List<string>();
        foreach (var filePath in _files.Keys)
        {
            var separatorIndex = filePath.LastIndexOf('/', StringComparison.Ordinal);
            var fileDirectory = filePath[..Math.Max(separatorIndex, 0)];
            var isInDirectory = searchOptions is SearchOption.AllDirectories
                ? fileDirectory.Equals(directory, StringComparison.Ordinal) || fileDirectory.StartsWith(directory + "/", StringComparison.Ordinal)
                : fileDirectory.Equals(directory, StringComparison.Ordinal);

            if (isInDirectory && FileSystemName.MatchesSimpleExpression(pattern, filePath.AsSpan(separatorIndex + 1), ignoreCase: false))
            {
                result.Add(filePath);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>A file opened for writing: its content replaces the file when it is disposed.</summary>
    private sealed class WritableFile : MemoryStream
    {
        private readonly InMemoryFileSystem _fileSystem;
        private readonly string _path;

        public WritableFile(InMemoryFileSystem fileSystem, string path, byte[] content)
        {
            _fileSystem = fileSystem;
            _path = path;
            Write(content);
            Position = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileSystem._files[_path] = ToArray();
            }

            base.Dispose(disposing);
        }
    }
}
