using Meziantou.Framework.Compression.Internals;

namespace Meziantou.Framework.Compression;

/// <summary>An archive opened for random access: its entries are known when it is opened, and can be read in any order.</summary>
public abstract class Archive : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    private protected Archive()
    {
    }

    /// <summary>Gets the format of the archive.</summary>
    public abstract ArchiveFormat Format { get; }

    /// <summary>Gets the entries of the archive, in the order of the archive.</summary>
    public IReadOnlyList<ArchiveEntry> Entries => GetEntries();

    private protected bool IsDisposed => _disposed;

    /// <summary>Gets the entry whose full name is <paramref name="entryName"/> (an ordinal comparison), or <see langword="null"/> when there is none.</summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <returns>The first entry whose <see cref="ArchiveEntry.FullName"/> is <paramref name="entryName"/>, or <see langword="null"/>.</returns>
    public ArchiveEntry? GetEntry(string entryName)
    {
        ArgumentNullException.ThrowIfNull(entryName);
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.FullName, entryName, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    /// <summary>
    /// Extracts the entries of the archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted
    /// path, or a symbolic link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    public void ExtractToDirectory(string destinationDirectoryName, bool overwriteFiles = false)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var extractor = new ArchiveExtractor(destinationDirectoryName, overwriteFiles);
        foreach (var entry in Entries)
        {
            extractor.Extract(entry);
        }

        extractor.Complete();
    }

    /// <summary>
    /// Extracts the entries of the archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted
    /// path, or a symbolic link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when every entry is extracted.</returns>
    public async Task ExtractToDirectoryAsync(string destinationDirectoryName, bool overwriteFiles = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var extractor = new ArchiveExtractor(destinationDirectoryName, overwriteFiles);
        foreach (var entry in Entries)
        {
            await extractor.ExtractAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        extractor.Complete();
    }

    /// <summary>Releases the resources of the archive.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the archive.</summary>
    /// <returns>A task that completes when the archive is disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the archive.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Releases the resources of the archive asynchronously.</summary>
    /// <returns>A task that completes when the resources are released.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        Dispose(disposing: true);
        return ValueTask.CompletedTask;
    }

    private protected abstract IReadOnlyList<ArchiveEntry> GetEntries();
}
