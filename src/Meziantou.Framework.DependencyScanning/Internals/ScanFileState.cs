using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>The mutable state shared by the copies of a <see cref="ScanFileContext"/>: the content stream, opened on first access, and whether the dependency callback failed.</summary>
internal sealed class ScanFileState
{
    private readonly IFileSystem _fileSystem;
    private readonly string _fullPath;
    private Stream? _content;
    private ExceptionDispatchInfo? _openException;

    public ScanFileState(IFileSystem fileSystem, string fullPath)
    {
        _fileSystem = fileSystem;
        _fullPath = fullPath;
    }

    /// <summary>Gets whether the callback that receives the dependencies threw, in which case the scan must stop rather than skip the file.</summary>
    public bool DependencyFoundCallbackFailed { get; set; }

    public Stream GetContent()
    {
        if (_content is not null)
            return _content;

        _openException?.Throw();

        try
        {
            _content = OpenSeekableStream();
            return _content;
        }
        catch (Exception ex)
        {
            _openException = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }

    private Stream OpenSeekableStream()
    {
        var stream = _fileSystem.OpenRead(_fullPath);
        if (stream.CanSeek)
            return stream;

        // Scanners read the content several times from its start, so a stream that cannot seek is read into memory
        try
        {
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }
        finally
        {
            stream.Dispose();
        }
    }

    public void ResetContent()
    {
        _content?.Seek(0, SeekOrigin.Begin);
    }

    public ValueTask DisposeAsync()
    {
        return _content?.DisposeAsync() ?? default;
    }
}
