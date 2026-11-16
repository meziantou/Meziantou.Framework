using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.SevenZip;

namespace Meziantou.Framework.Compression;

/// <summary>
/// A 7z archive, read, created or updated as <see cref="System.IO.Compression.ZipArchive"/>. Reading supports every method
/// of 7-Zip but the RAR ones (LZMA, LZMA2, PPMd, BZip2, Deflate, Deflate64, the branch filters, BCJ2, Delta, AES-256),
/// solid archives, encrypted headers and multi-volume archives. Writing supports LZMA2, LZMA, PPMd, BZip2, Deflate and Copy,
/// the branch filters, solid folders and AES-256.
/// </summary>
/// <remarks>
/// <para>
/// An archive in <see cref="SevenZipArchiveMode.Create"/> mode streams the content of each entry, as it is written, into the
/// current solid folder: one entry can be written at a time. The headers are written when the archive is disposed. An archive
/// in <see cref="SevenZipArchiveMode.Update"/> mode is rewritten when it is disposed: the folders whose entries are all kept are
/// copied without being decoded, the kept entries of the other folders are recompressed.
/// </para>
/// <para>
/// Reading the entries of a solid folder in order decodes the folder once. Opening an entry before the last one opened in
/// the same folder decodes the folder again from its start.
/// </para>
/// <para>
/// The archive is read from the start of the stream. A new archive is written from the current position of the stream.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/SevenZipArchiveTests.cs" ref:region="SevenZipArchive_Create" ref:dedent="true">
/// using var stream = new MemoryStream();
/// using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true))
/// {
///     // Each entry is compressed as it is written, into the current solid folder
///     var entry = archive.CreateEntry("docs/readme.txt");
///     using var writer = new StreamWriter(entry.Open());
///     writer.Write("Hello from 7z");
/// }
///
/// stream.Position = 0;
/// using (var archive = new SevenZipArchive(stream))
/// {
///     Assert.Equal("docs/readme.txt", archive.Entries[0].FullName);
/// }
/// </code>
/// </example>
public sealed class SevenZipArchive : Archive
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly long _baseOffset;
    private readonly List<SevenZipArchiveEntry> _entries = [];
    private readonly SevenZipDecoderContext _decoderContext;
    private readonly SevenZipWriterSettings? _writerSettings;
    private readonly SevenZipDatabase? _database;
    private SevenZipFolderWriter? _writer;
    private Stream? _stagingStream;
    private SevenZipEntryWriteStream? _openWriteStream;
    private SevenZipFolderCursor? _cursor;
    private bool _modified;

    /// <summary>Initializes a new instance of the <see cref="SevenZipArchive"/> class that reads an archive.</summary>
    /// <param name="stream">The archive. It must be readable and seekable.</param>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The headers are encrypted and the password is missing or wrong.</exception>
    public SevenZipArchive(Stream stream)
        : this(stream, SevenZipArchiveMode.Read, leaveOpen: false, readerOptions: null, compressionOptions: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SevenZipArchive"/> class.</summary>
    /// <param name="stream">The archive. It must be seekable, readable to read or update an archive, and writable to create or update one.</param>
    /// <param name="mode">The mode of the archive.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the archive is disposed.</param>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    public SevenZipArchive(Stream stream, SevenZipArchiveMode mode, bool leaveOpen = false)
        : this(stream, mode, leaveOpen, readerOptions: null, compressionOptions: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SevenZipArchive"/> class.</summary>
    /// <param name="stream">The archive. It must be seekable, readable to read or update an archive, and writable to create or update one.</param>
    /// <param name="mode">The mode of the archive.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the archive is disposed.</param>
    /// <param name="readerOptions">
    /// The options to read the archive: its password, the volumes of a multi-volume archive (<see cref="ArchiveReaderOptions.VolumeResolver"/>),
    /// and the memory the decoders may allocate.
    /// </param>
    /// <param name="compressionOptions">
    /// The options to write the archive: the method, the level, the password, ... In <see cref="SevenZipArchiveMode.Update"/>
    /// mode, they apply to the new entries, to the recompressed folders, and to the headers.
    /// </param>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The headers are encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The archive uses a feature that is not supported.</exception>
    public SevenZipArchive(Stream stream, SevenZipArchiveMode mode, bool leaveOpen, ArchiveReaderOptions? readerOptions, SevenZipCompressionOptions? compressionOptions)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (mode is not (SevenZipArchiveMode.Read or SevenZipArchiveMode.Create or SevenZipArchiveMode.Update))
            throw new ArgumentOutOfRangeException(nameof(mode));

        if (!stream.CanSeek)
            throw new ArgumentException("The stream must be seekable.", nameof(stream));

        if (mode is not SevenZipArchiveMode.Create && !stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));

        if (mode is not SevenZipArchiveMode.Read && !stream.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(stream));

        Mode = mode;
        _leaveOpen = leaveOpen;
        _decoderContext = new SevenZipDecoderContext(readerOptions?.Password, readerOptions?.MaxMemoryUsage ?? new ArchiveReaderOptions().MaxMemoryUsage);
        if (mode is SevenZipArchiveMode.Read && readerOptions?.VolumeResolver is { } resolver)
        {
            stream = new ConcatenatedStream(stream, resolver, leaveFirstOpen: leaveOpen);
            _leaveOpen = false;
        }

        _stream = stream;
        try
        {
            if (mode is not SevenZipArchiveMode.Read)
            {
                _writerSettings = new SevenZipWriterSettings(compressionOptions?.Clone() ?? new SevenZipCompressionOptions());
            }

            if (mode is SevenZipArchiveMode.Create)
            {
                _baseOffset = stream.Position;
                SevenZipArchiveFormat.WriteSignaturePlaceholder(stream);
                _writer = new SevenZipFolderWriter(stream, _writerSettings!);
                return;
            }

            _database = SevenZipArchiveFormat.ReadDatabase(stream, _decoderContext);
            foreach (var item in _database.Files)
            {
                if (item.IsAnti)
                    continue;

                if (item.HasStream)
                {
                    item.Folder = _database.Folders[item.FolderIndex];
                }

                _entries.Add(new SevenZipArchiveEntry(this, item, isNew: false));
            }
        }
        catch
        {
            if (!_leaveOpen)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    /// <summary>Gets the mode of the archive.</summary>
    public SevenZipArchiveMode Mode { get; }

    /// <inheritdoc/>
    public override ArchiveFormat Format => ArchiveFormat.SevenZip;

    /// <summary>Gets the entries of the archive, in the order of the archive. The anti-items of 7-Zip (deletion markers) are not listed.</summary>
    public new IReadOnlyList<SevenZipArchiveEntry> Entries
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _entries;
        }
    }

    internal SevenZipWriterSettings? WriterSettings => _writerSettings;

    /// <summary>Gets the entry whose full name is <paramref name="entryName"/> (an ordinal comparison), or <see langword="null"/> when there is none.</summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators. The name of a directory ends with <c>/</c>.</param>
    /// <returns>The first entry whose <see cref="SevenZipArchiveEntry.FullName"/> is <paramref name="entryName"/>, or <see langword="null"/>.</returns>
    public new SevenZipArchiveEntry? GetEntry(string entryName) => (SevenZipArchiveEntry?)base.GetEntry(entryName);

    /// <summary>
    /// Creates an empty entry. A name that ends with <c>/</c> creates a directory. Write the content of a file with
    /// <see cref="SevenZipArchiveEntry.Open"/>; an entry that is not opened is an empty file.
    /// </summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <returns>The new entry, whose last write time is the current time.</returns>
    /// <exception cref="NotSupportedException">The archive is in <see cref="SevenZipArchiveMode.Read"/> mode.</exception>
    public SevenZipArchiveEntry CreateEntry(string entryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (Mode is SevenZipArchiveMode.Read)
            throw new NotSupportedException("Entries cannot be created in an archive opened in Read mode.");

        if (entryName.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("The name of an entry cannot contain a null character.", nameof(entryName));

        var isDirectory = entryName[^1] is '/';
        var name = isDirectory ? entryName.TrimEnd('/') : entryName;
        if (name.Length is 0)
            throw new ArgumentException("The name of the entry is empty.", nameof(entryName));

        var item = new SevenZipFileItem
        {
            Name = name,
            IsDirectory = isDirectory,
            LastWriteTime = SevenZipArchiveFormat.ToFileTime(DateTimeOffset.UtcNow),
            Attributes = isDirectory ? SevenZipConstants.DirectoryAttribute : null,
        };

        var entry = new SevenZipArchiveEntry(this, item, isNew: true);
        _entries.Add(entry);
        _modified = true;
        return entry;
    }

    /// <summary>
    /// Creates an entry from a file: its content, its times, its attributes and, on Unix, its permissions. A symbolic link
    /// is stored as a link (its target), as the <c>-snl</c> switch of 7-Zip.
    /// </summary>
    /// <param name="sourceFileName">The path of the file.</param>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <returns>The new entry.</returns>
    public SevenZipArchiveEntry CreateEntryFromFile(string sourceFileName, string entryName)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        var info = new FileInfo(sourceFileName);
        var entry = CreateEntry(entryName);
        SetMetadataFromFile(entry, info);
        if (info.LinkTarget is { } linkTarget)
        {
            entry.SetLinkTarget(linkTarget);
            return entry;
        }

        using var source = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);
        using var destination = entry.Open();
        source.CopyTo(destination);
        return entry;
    }

    internal async Task<SevenZipArchiveEntry> CreateEntryFromFileAsync(string sourceFileName, string entryName, CancellationToken cancellationToken)
    {
        var info = new FileInfo(sourceFileName);
        var entry = CreateEntry(entryName);
        SetMetadataFromFile(entry, info);
        if (info.LinkTarget is { } linkTarget)
        {
            entry.SetLinkTarget(linkTarget);
            return entry;
        }

        var source = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, useAsync: true);
        await using (source.ConfigureAwait(false))
        {
            var destination = entry.Open();
            await using (destination.ConfigureAwait(false))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }

        return entry;
    }

    internal static void SetMetadataFromFile(SevenZipArchiveEntry entry, FileSystemInfo info)
    {
        entry.SetLastWriteTime(new DateTimeOffset(info.LastWriteTimeUtc));
        entry.SetCreationTime(new DateTimeOffset(info.CreationTimeUtc));
        entry.SetLastAccessTime(new DateTimeOffset(info.LastAccessTimeUtc));
        entry.SetAttributes(info.Attributes & (FileAttributes)0x7FFF & ~FileAttributes.ReparsePoint);
        if (!OperatingSystem.IsWindows())
        {
            entry.SetUnixFileMode(info.UnixFileMode);
        }
    }

    internal void MarkModified()
    {
        _modified = true;
    }

    internal void Remove(SevenZipArchiveEntry entry)
    {
        _entries.Remove(entry);
        _modified = true;
    }

    /// <summary>Opens the content of an entry of the archive (not a new one).</summary>
    internal Stream OpenRead(SevenZipArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var item = entry.Item;
        if (!item.HasStream)
            return new MemoryStream([], writable: false);

        return OpenContent(_stream, item);
    }

    private SevenZipFolderCursor OpenCursor(Stream source, SevenZipFileItem item, out bool ownsCursor)
    {
        var folder = item.Folder!;
        var encrypted = folder.IsEncrypted;
        SevenZipFolderCursor? cursor = null;
        try
        {
            if (_cursor is { InUse: false } current && current.Folder == folder && current.Source == source && current.Position <= item.OffsetInFolder)
            {
                cursor = current;
                ownsCursor = false;
            }
            else if (_cursor is { InUse: true })
            {
                // Another entry of the archive is being read: this one gets its own decoder
                cursor = new SevenZipFolderCursor(source, folder, _decoderContext);
                ownsCursor = true;
            }
            else
            {
                _cursor?.Dispose();
                _cursor = null;
                cursor = new SevenZipFolderCursor(source, folder, _decoderContext);
                _cursor = cursor;
                ownsCursor = false;
            }

            cursor.SkipTo(item.OffsetInFolder);
            cursor.InUse = !ownsCursor;
            return cursor;
        }
        catch (Exception ex)
        {
            if (cursor is not null)
            {
                cursor.Dispose();
                if (_cursor == cursor)
                {
                    _cursor = null;
                }
            }

            if (ex is InvalidDataException && encrypted)
                throw new ArchivePasswordException("The content of the encrypted entry is invalid. The password may be wrong.", ex);

            throw;
        }
    }

    /// <summary>Opens the stream that writes the content of a new entry.</summary>
    internal Stream OpenWrite(SevenZipArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_openWriteStream is not null)
            throw new InvalidOperationException("Another entry of the archive is being written: dispose its stream before opening another entry.");

        if (_writer is null)
        {
            // Update mode: the new entries are compressed to a temporary file, whose folders are copied when the archive is disposed
            _stagingStream = TemporaryFileStream.Create();
            _writer = new SevenZipFolderWriter(_stagingStream, _writerSettings!);
        }

        var stream = _writer.OpenEntry(entry.Item);
        stream.Completed = () => _openWriteStream = null;
        _openWriteStream = stream;
        return stream;
    }


    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Complete();
            }
            finally
            {
                _cursor?.Dispose();
                _stagingStream?.Dispose();
                if (!_leaveOpen)
                {
                    _stream.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    private void Complete()
    {
        if (Mode is SevenZipArchiveMode.Read)
            return;

        _openWriteStream?.Dispose();
        _writer?.CloseFolder();
        if (Mode is SevenZipArchiveMode.Create)
        {
            PrepareNewItems();
            var files = SevenZipArchiveFormat.OrderFiles(_entries.Select(entry => entry.Item), _writer!.StreamFiles);
            SevenZipArchiveFormat.WriteHeaders(_stream, _baseOffset, _writerSettings!, _writer.Folders, files);
            return;
        }

        if (_modified)
        {
            Rewrite();
        }
    }

    private void PrepareNewItems()
    {
        foreach (var entry in _entries)
        {
            if (entry.IsNew && entry.Item.IsDirectory)
            {
                entry.Item.Attributes = (entry.Item.Attributes ?? 0) | SevenZipConstants.DirectoryAttribute;
            }
        }
    }

    /// <summary>Writes the updated archive to a temporary file, then copies it over the archive.</summary>
    private void Rewrite()
    {
        PrepareNewItems();
        using var temporary = TemporaryFileStream.Create();
        SevenZipArchiveFormat.WriteSignaturePlaceholder(temporary);
        var writer = new SevenZipFolderWriter(temporary, _writerSettings!);
        var outputItems = new Dictionary<SevenZipArchiveEntry, SevenZipFileItem>();

        // The original folders, then the folders of the new entries, in order
        var sources = new List<(Stream Source, SevenZipFolder Folder)>();
        foreach (var folder in _database!.Folders)
        {
            sources.Add((_stream, folder));
        }

        if (_stagingStream is not null)
        {
            foreach (var folder in _writer!.Folders)
            {
                sources.Add((_stagingStream, folder));
            }
        }

        var entriesByFolder = _entries.Where(entry => entry.Item.HasStream).ToLookup(entry => entry.Item.Folder!);
        foreach (var (source, folder) in sources)
        {
            var kept = entriesByFolder[folder].OrderBy(entry => entry.Item.OffsetInFolder).ToList();
            if (kept.Count is 0)
                continue;

            if (kept.Count == folder.UnpackStreamCount)
            {
                var items = kept.ConvertAll(entry => outputItems[entry] = entry.Item.Clone());
                writer.CopyFolder(source, folder, items);
                continue;
            }

            foreach (var entry in kept)
            {
                var item = entry.Item.Clone();
                outputItems[entry] = item;
                using var content = OpenContent(source, entry.Item);
                using var destination = writer.OpenEntry(item);
                content.CopyTo(destination);
            }
        }

        writer.CloseFolder();
        foreach (var entry in _entries)
        {
            if (!outputItems.ContainsKey(entry))
            {
                outputItems[entry] = entry.Item.Clone();
            }
        }

        var files = SevenZipArchiveFormat.OrderFiles(_entries.Select(entry => outputItems[entry]), writer.StreamFiles);
        SevenZipArchiveFormat.WriteHeaders(temporary, 0, _writerSettings!, writer.Folders, files);

        _cursor?.Dispose();
        _cursor = null;
        temporary.Position = 0;
        _stream.Position = 0;
        temporary.CopyTo(_stream);
        _stream.SetLength(temporary.Length);
        _stream.Flush();
    }

    private ChecksumVerifyingStream OpenContent(Stream source, SevenZipFileItem item)
    {
        var encrypted = item.Folder!.IsEncrypted;
        var cursor = OpenCursor(source, item, out var ownsCursor);
        return new ChecksumVerifyingStream(new SevenZipEntryReadStream(cursor, item.Size, ownsCursor, encrypted), item.Size, item.Crc, encrypted);
    }

    private protected override IReadOnlyList<ArchiveEntry> GetEntries() => Entries;
}
