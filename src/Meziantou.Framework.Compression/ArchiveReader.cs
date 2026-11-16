using Meziantou.Framework.Compression.Internals;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Reads the entries of an archive one after the other, from a stream that does not need to be seekable (a network
/// response, a pipe). The content of the current entry can be read until the reader moves to the next entry; the content
/// that is not read is skipped.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/ArchiveFileTests.cs" ref:region="ArchiveReader_ForwardOnly" ref:dedent="true">
/// // The stream does not need to be seekable: a network response, a pipe, ...
/// using var input = File.OpenRead(path);
/// using var archiveReader = ArchiveReader.Create(input);
/// Assert.Equal(ArchiveFormat.Tar, archiveReader.Format);
/// Assert.Equal(CompressionFormat.GZip, archiveReader.CompressionFormat);
/// while (archiveReader.MoveToNextEntry())
/// {
///     var entry = archiveReader.Entry!;
///     if (entry.FullName.EndsWith(".txt", StringComparison.Ordinal))
///     {
///         // The content can be read until the reader moves to the next entry
///         using var content = archiveReader.OpenEntryStream();
///         using var copy = new MemoryStream();
///         content.CopyTo(copy);
///         Assert.Equal(entry.Length, copy.Length);
///     }
/// }
/// </code>
/// </example>
public abstract partial class ArchiveReader : IDisposable, IAsyncDisposable
{
    private ArchiveEntry? _entry;
    private bool _completed;
    private bool _disposed;

    private protected ArchiveReader()
    {
    }

    /// <summary>Gets the format of the archive.</summary>
    public abstract ArchiveFormat Format { get; }

    /// <summary>Gets the compression around the archive (the compression of a .tar.gz, .tar.xz, ...).</summary>
    public virtual CompressionFormat CompressionFormat => CompressionFormat.None;

    /// <summary>Gets the current entry, or <see langword="null"/> before the first call to <see cref="MoveToNextEntry"/> and after the last entry.</summary>
    public ArchiveEntry? Entry => _entry;

    private protected bool IsDisposed => _disposed;

    /// <summary>Moves to the next entry of the archive, skipping the content of the current entry that is not read.</summary>
    /// <returns><see langword="true"/> when there is a next entry; <see langword="false"/> at the end of the archive.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    public bool MoveToNextEntry()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            return false;

        _entry = ReadNextEntry();
        _completed = _entry is null;
        return !_completed;
    }

    /// <summary>Moves to the next entry of the archive, skipping the content of the current entry that is not read.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when there is a next entry; <see langword="false"/> at the end of the archive.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    public async ValueTask<bool> MoveToNextEntryAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            return false;

        _entry = await ReadNextEntryAsync(cancellationToken).ConfigureAwait(false);
        _completed = _entry is null;
        return !_completed;
    }

    /// <summary>Opens the content of the current entry. The stream is valid until the reader moves to the next entry.</summary>
    /// <returns>A read-only stream of the content of the current entry.</returns>
    /// <exception cref="InvalidOperationException">There is no current entry.</exception>
    public Stream OpenEntryStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_entry ?? throw new InvalidOperationException("There is no current entry: call MoveToNextEntry first.")).Open();
    }

    /// <summary>Extracts the current entry under a directory, at the path of the entry in the archive.</summary>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace an existing file; <see langword="false"/> to throw an <see cref="IOException"/> when the file exists.</param>
    /// <exception cref="InvalidOperationException">There is no current entry.</exception>
    public void WriteEntryToDirectory(string destinationDirectoryName, bool overwriteFiles = false)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = _entry ?? throw new InvalidOperationException("There is no current entry: call MoveToNextEntry first.");
        var extractor = new ArchiveExtractor(destinationDirectoryName, overwriteFiles);
        extractor.Extract(entry);
        extractor.Complete();
    }

    /// <summary>Extracts the current entry under a directory, at the path of the entry in the archive.</summary>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace an existing file; <see langword="false"/> to throw an <see cref="IOException"/> when the file exists.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the entry is extracted.</returns>
    /// <exception cref="InvalidOperationException">There is no current entry.</exception>
    public async Task WriteEntryToDirectoryAsync(string destinationDirectoryName, bool overwriteFiles = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = _entry ?? throw new InvalidOperationException("There is no current entry: call MoveToNextEntry first.");
        var extractor = new ArchiveExtractor(destinationDirectoryName, overwriteFiles);
        await extractor.ExtractAsync(entry, cancellationToken).ConfigureAwait(false);
        extractor.Complete();
    }

    /// <summary>
    /// Extracts the remaining entries of the archive under a directory. An entry whose path leaves the directory is rejected
    /// with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    public void ExtractToDirectory(string destinationDirectoryName, bool overwriteFiles = false)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        var extractor = new ArchiveExtractor(destinationDirectoryName, overwriteFiles);
        while (MoveToNextEntry())
        {
            extractor.Extract(_entry!);
        }

        extractor.Complete();
    }

    /// <summary>
    /// Extracts the remaining entries of the archive under a directory. An entry whose path leaves the directory is rejected
    /// with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when every entry is extracted.</returns>
    public async Task ExtractToDirectoryAsync(string destinationDirectoryName, bool overwriteFiles = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        var extractor = new ArchiveExtractor(destinationDirectoryName, overwriteFiles);
        while (await MoveToNextEntryAsync(cancellationToken).ConfigureAwait(false))
        {
            await extractor.ExtractAsync(_entry!, cancellationToken).ConfigureAwait(false);
        }

        extractor.Complete();
    }

    /// <summary>Releases the resources of the reader.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the reader.</summary>
    /// <returns>A task that completes when the reader is disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the reader.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Releases the resources of the reader asynchronously.</summary>
    /// <returns>A task that completes when the resources are released.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        Dispose(disposing: true);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Reads the header of the next entry, after skipping the content of the current one that is not read, and invalidates
    /// the streams opened on the current entry. Returns <see langword="null"/> at the end of the archive.
    /// </summary>
    private protected abstract ArchiveEntry? ReadNextEntry();

    private protected virtual ValueTask<ArchiveEntry?> ReadNextEntryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(ReadNextEntry());
    }
}
