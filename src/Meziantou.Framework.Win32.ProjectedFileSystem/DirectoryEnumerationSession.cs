namespace Meziantou.Framework.Win32.ProjectedFileSystem;

internal sealed class DirectoryEnumerationSession : IDisposable
{
    private IEnumerator<ProjectedFileSystemEntry>? _enumerator;
    private ProjectedFileSystemEntry? _current;

    public DirectoryEnumerationSession(IEnumerable<ProjectedFileSystemEntry> entries)
    {
        Entries = entries;
    }

    public ProjectedFileSystemEntry? GetNextEntry()
    {
        if (_current is not null)
        {
            var current = _current;
            _current = null;
            return current;
        }

        _enumerator ??= Entries.GetEnumerator();
        if (_enumerator.MoveNext())
        {
            return _enumerator.Current;
        }

        return null;
    }

    public void Reenqueue()
    {
        if (_enumerator is null)
            throw new InvalidOperationException("No item dequeued");

        _current = _enumerator.Current;
    }

    public IEnumerable<ProjectedFileSystemEntry> Entries { get; }

    public void Dispose()
    {
        Reset();
    }

    internal void Reset()
    {
        _current = null;
        _enumerator?.Dispose();
    }
}
