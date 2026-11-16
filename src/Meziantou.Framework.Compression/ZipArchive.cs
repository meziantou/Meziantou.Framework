using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.Zip;

namespace Meziantou.Framework.Compression;

/// <summary>
/// A zip archive, read, created or updated as <see cref="System.IO.Compression.ZipArchive"/>, with the compression methods
/// and the encryptions that it does not support. In <see cref="ZipArchiveMode.Read"/> mode, the entries are read from the
/// central directory when the archive is opened, and their content can be read in any order.
/// </summary>
/// <remarks>
/// <para>
/// Reading supports the compression methods of <see cref="ZipCompressionMethod"/> that are not marked as not supported, the
/// traditional PKWARE encryption (ZipCrypto) and the WinZip AES encryption, zip64 archives, archives with data prepended
/// (self-extracting archives), and split archives: the Info-ZIP <c>.z01</c>, <c>.z02</c>, ..., <c>.zip</c> volumes and the
/// <c>.zip.001</c>, <c>.zip.002</c>, ... parts of 7-Zip.
/// </para>
/// <para>
/// Writing supports the Stored, Deflate, BZip2, LZMA, PPMd, XZ and Zstandard (on .NET 11) methods, the WinZip AES (128, 192
/// and 256 bits) and ZipCrypto encryptions, and zip64. In <see cref="ZipArchiveMode.Create"/> mode, the stream does not need to
/// be seekable: each entry is written when its content is written, one entry at a time. On a seekable stream, the local
/// header of an entry is rewritten with its sizes once its content is written; otherwise, a data descriptor follows the
/// content. The central directory is written when the archive is disposed. In <see cref="ZipArchiveMode.Update"/> mode, the
/// archive is rewritten when it is disposed, if it is modified: the existing entries are copied without being decoded (their
/// password is not needed), and the new entries are written to a temporary file until then.
/// </para>
/// <para>
/// The names that are not marked as UTF-8 (general purpose flag bit 11) nor have an Info-ZIP Unicode path extra field are
/// decoded with <see cref="ArchiveReaderOptions.EntryNameEncoding"/>, or with the code page 437 when it is <see langword="null"/>.
/// The backslashes of the names of the entries created on MS-DOS or Windows are replaced with <c>/</c>. The names and the
/// comments written are ASCII, or UTF-8 with the flag bit 11. A password is encoded in UTF-8.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveWriteTests.cs" ref:region="ZipArchive_Create" ref:dedent="true">
/// using var stream = new MemoryStream();
/// using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
/// {
///     // One entry is written at a time; the stream does not need to be seekable
///     var entry = archive.CreateEntry("docs/readme.txt", new ZipEntryOptions { CompressionMethod = ZipCompressionMethod.Lzma });
///     using var writer = new StreamWriter(entry.Open());
///     writer.Write("Hello from zip");
/// }
///
/// stream.Position = 0;
/// using (var archive = new ZipArchive(stream))
/// {
///     var entry = archive.GetEntry("docs/readme.txt")!;
///     Assert.Equal(ZipCompressionMethod.Lzma, entry.CompressionMethod);
///     using var reader = new StreamReader(entry.Open());
///     Assert.Equal("Hello from zip", reader.ReadToEnd());
/// }
/// </code>
/// </example>
public sealed class ZipArchive : Archive
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const uint ArchiveExtraDataSignature = 0x08064B50;
    private const int EndOfCentralDirectoryLength = 22;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EndOfCentralDirectoryLength = 56;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly ArchiveReaderOptions _options;
    private readonly ZipCompressionOptions _compressionOptions;
    private readonly List<ZipArchiveEntry> _entries = [];
    private readonly List<ZipArchiveEntry> _originalEntries = [];
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "It is _stream, which is disposed")]
    private readonly ConcatenatedStream? _volumes;
    private ZipWriter? _writer;
    private Stream? _stagingStream;
    private string? _comment = "";
    private bool _modified;
    private bool _closed;

    /// <summary>Initializes a new instance of the <see cref="ZipArchive"/> class that reads an archive.</summary>
    /// <param name="stream">The archive. It must be readable and seekable.</param>
    /// <exception cref="ArgumentException">The stream is not readable or not seekable.</exception>
    /// <exception cref="InvalidDataException">The stream is not a zip archive, or the archive is corrupted.</exception>
    /// <exception cref="NotSupportedException">The central directory of the archive is encrypted.</exception>
    public ZipArchive(Stream stream)
        : this(stream, ZipArchiveMode.Read, leaveOpen: false, readerOptions: null, compressionOptions: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZipArchive"/> class.</summary>
    /// <param name="stream">
    /// The archive: readable and seekable to read it, writable to create it, and readable, writable and seekable to update it.
    /// </param>
    /// <param name="mode">The mode of the archive.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the archive is disposed.</param>
    /// <exception cref="ArgumentException">The stream does not support the operations of the mode.</exception>
    /// <exception cref="InvalidDataException">The stream is not a zip archive, or the archive is corrupted.</exception>
    /// <exception cref="NotSupportedException">The central directory of the archive is encrypted.</exception>
    public ZipArchive(Stream stream, ZipArchiveMode mode, bool leaveOpen = false)
        : this(stream, mode, leaveOpen, readerOptions: null, compressionOptions: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZipArchive"/> class.</summary>
    /// <param name="stream">
    /// The archive: readable and seekable to read it, writable to create it, and readable, writable and seekable to update it.
    /// To read a split archive, it is the first volume (the <c>.z01</c> file of an Info-ZIP split archive, the <c>.zip.001</c>
    /// file of 7-Zip), and <see cref="ArchiveReaderOptions.VolumeResolver"/> returns the next ones (the <c>.zip</c> file is the
    /// last volume of an Info-ZIP split archive).
    /// </param>
    /// <param name="mode">The mode of the archive.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the archive is disposed.</param>
    /// <param name="readerOptions">The options to read the archive, or <see langword="null"/> for the default options.</param>
    /// <param name="compressionOptions">The options of the new entries, or <see langword="null"/> for the default options.</param>
    /// <exception cref="ArgumentException">The stream does not support the operations of the mode.</exception>
    /// <exception cref="InvalidDataException">The stream is not a zip archive, or the archive is corrupted.</exception>
    /// <exception cref="NotSupportedException">The central directory of the archive is encrypted.</exception>
    public ZipArchive(Stream stream, ZipArchiveMode mode, bool leaveOpen, ArchiveReaderOptions? readerOptions, ZipCompressionOptions? compressionOptions)
        : this(stream, mode, leaveOpen, readerOptions, compressionOptions, mode is ZipArchiveMode.Read ? readerOptions?.VolumeResolver : null)
    {
    }

    internal ZipArchive(Stream stream, ZipArchiveMode mode, bool leaveOpen, ArchiveReaderOptions? readerOptions, ZipCompressionOptions? compressionOptions, Func<int, Stream?>? volumeResolver)
    {
        ArgumentNullException.ThrowIfNull(stream);
        switch (mode)
        {
            case ZipArchiveMode.Read:
                if (!stream.CanRead || !stream.CanSeek)
                    throw new ArgumentException("The stream must be readable and seekable. Use a forward-only reader for a stream that is not seekable.", nameof(stream));

                break;

            case ZipArchiveMode.Create:
                if (!stream.CanWrite)
                    throw new ArgumentException("The stream must be writable.", nameof(stream));

                break;

            case ZipArchiveMode.Update:
                if (!stream.CanRead || !stream.CanWrite || !stream.CanSeek)
                    throw new ArgumentException("The stream must be readable, writable and seekable.", nameof(stream));

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
        _options = readerOptions ?? new ArchiveReaderOptions();
        _compressionOptions = compressionOptions?.Clone() ?? new ZipCompressionOptions();
        if (volumeResolver is not null)
        {
            _volumes = new ConcatenatedStream(stream, volumeResolver, leaveFirstOpen: leaveOpen);
            _stream = _volumes;
            _ownsStream = true;
        }
        else
        {
            _stream = stream;
            _ownsStream = !leaveOpen;
        }

        try
        {
            if (mode is ZipArchiveMode.Create)
            {
                _writer = new ZipWriter(_stream, _compressionOptions.Zip64);
            }
            else if (mode is ZipArchiveMode.Read || _stream.Length > 0)
            {
                ReadCentralDirectory();
            }
            else
            {
                // Update mode on an empty stream: a new archive
                _modified = true;
            }
        }
        catch
        {
            if (_ownsStream)
            {
                _stream.Dispose();
            }

            throw;
        }
    }

    /// <summary>Gets the mode of the archive.</summary>
    public ZipArchiveMode Mode { get; }

    /// <inheritdoc/>
    public override ArchiveFormat Format => ArchiveFormat.Zip;

    /// <summary>Gets the entries of the archive, in the order of the central directory (and of their creation for the new entries).</summary>
    public new IReadOnlyList<ZipArchiveEntry> Entries
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _entries;
        }
    }

    /// <summary>
    /// Gets or sets the comment of the archive. It can be set in <see cref="ZipArchiveMode.Create"/> and
    /// <see cref="ZipArchiveMode.Update"/> modes, until the archive is disposed. The comment of the end of central directory
    /// record has no encoding flag: it is written in the code page 437, the encoding the readers assume, or in UTF-8 when it
    /// has a character that code page 437 does not have.
    /// </summary>
    /// <exception cref="NotSupportedException">The archive is in <see cref="ZipArchiveMode.Read"/> mode.</exception>
    /// <exception cref="ArgumentException">The comment is longer than 65535 bytes.</exception>
    public string? Comment
    {
        get => _comment;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (Mode is ZipArchiveMode.Read)
                throw new NotSupportedException("The comment of an archive opened in Read mode cannot be changed.");

            if (ZipWriter.EncodeArchiveComment(value).Length > ushort.MaxValue)
                throw new ArgumentException("The comment is longer than 65535 bytes.", nameof(value));

            _comment = value;
            _modified = true;
        }
    }

    /// <summary>Gets the entry whose full name is <paramref name="entryName"/> (an ordinal comparison), or <see langword="null"/> when there is none.</summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <returns>The first entry whose <see cref="ArchiveEntry.FullName"/> is <paramref name="entryName"/>, or <see langword="null"/>.</returns>
    public new ZipArchiveEntry? GetEntry(string entryName) => (ZipArchiveEntry?)base.GetEntry(entryName);

    /// <summary>
    /// Creates an entry. A name that ends with <c>/</c> creates a directory, written at once. Write the content of a file with
    /// <see cref="ZipArchiveEntry.Open"/>; an entry that is not opened is an empty file. The options of the entry are
    /// resolved when it is created: changing <paramref name="options"/> afterwards has no effect.
    /// </summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <param name="options">The options of the entry, which override the <see cref="ZipCompressionOptions"/> of the archive.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="NotSupportedException">
    /// The archive is in <see cref="ZipArchiveMode.Read"/> mode, or the compression method cannot be written (Deflate64, the
    /// legacy methods, and Zstandard before .NET 11).
    /// </exception>
    /// <exception cref="InvalidOperationException">The content of another entry is being written.</exception>
    public ZipArchiveEntry CreateEntry(string entryName, ZipEntryOptions? options = null)
    {
        return CreateEntryCore(entryName, options, knownLength: -1, isSymbolicLink: false);
    }

    /// <summary>
    /// Creates an entry that is a symbolic link: its Unix mode is a link (S_IFLNK, <c>rwxrwxrwx</c> unless
    /// <see cref="ZipEntryOptions.UnixFileMode"/> is set), and its content, stored, is the target in UTF-8, as Info-ZIP writes it.
    /// </summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <param name="target">The target of the link.</param>
    /// <param name="options">The options of the entry. The compression method is ignored.</param>
    /// <returns>The new entry.</returns>
    public ZipArchiveEntry CreateSymbolicLinkEntry(string entryName, string target, ZipEntryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        var entry = CreateEntryCore(entryName, options, knownLength: -1, isSymbolicLink: true);
        using (var stream = entry.Open())
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(target));
        }

        entry.SetLinkTarget(target);
        return entry;
    }

    /// <summary>
    /// Creates an entry from a file: its content, its last write time and, on Unix, its permissions (on Windows, its
    /// attributes). A symbolic link is stored as a link (its target). The options override the metadata of the file.
    /// </summary>
    /// <param name="sourceFileName">The path of the file.</param>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <param name="options">The options of the entry.</param>
    /// <returns>The new entry, written.</returns>
    public ZipArchiveEntry CreateEntryFromFile(string sourceFileName, string entryName, ZipEntryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        var info = new FileInfo(sourceFileName);
        var entryOptions = GetFileOptions(info, options);
        if (info.LinkTarget is { } linkTarget)
            return CreateSymbolicLinkEntry(entryName, linkTarget, entryOptions);

        using var source = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);
        var entry = CreateEntryCore(entryName, entryOptions, source.Length, isSymbolicLink: false);
        using var destination = entry.Open();
        source.CopyTo(destination);
        return entry;
    }

    /// <summary>
    /// Creates an entry from a file: its content, its last write time and, on Unix, its permissions (on Windows, its
    /// attributes). A symbolic link is stored as a link (its target). The options override the metadata of the file.
    /// </summary>
    /// <param name="sourceFileName">The path of the file.</param>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <param name="options">The options of the entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The new entry, written.</returns>
    public async Task<ZipArchiveEntry> CreateEntryFromFileAsync(string sourceFileName, string entryName, ZipEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        var info = new FileInfo(sourceFileName);
        var entryOptions = GetFileOptions(info, options);
        if (info.LinkTarget is { } linkTarget)
        {
            var link = CreateEntryCore(entryName, entryOptions, knownLength: -1, isSymbolicLink: true);
            var linkStream = link.Open();
            await using (linkStream.ConfigureAwait(false))
            {
                await linkStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(linkTarget), cancellationToken).ConfigureAwait(false);
            }

            link.SetLinkTarget(linkTarget);
            return link;
        }

        var source = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, useAsync: true);
        await using (source.ConfigureAwait(false))
        {
            var entry = CreateEntryCore(entryName, entryOptions, source.Length, isSymbolicLink: false);
            var destination = entry.Open();
            await using (destination.ConfigureAwait(false))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            return entry;
        }
    }

    /// <summary>Creates a reader that reads the local headers of a zip archive one after the other, from a stream that does not need to be seekable.</summary>
    internal static ArchiveReader CreateForwardReader(Stream stream, ArchiveReaderOptions? options, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new ZipForwardReader(stream, options, leaveOpen);
    }

    internal static ZipEntryOptions GetFileOptions(FileSystemInfo info, ZipEntryOptions? options)
    {
        var result = options?.Clone() ?? new ZipEntryOptions();
        result.LastWriteTime ??= new DateTimeOffset(info.LastWriteTimeUtc);
        if (OperatingSystem.IsWindows())
        {
            result.Attributes ??= info.Attributes & (FileAttributes)0x7FFF & ~FileAttributes.ReparsePoint;
        }
        else
        {
            result.UnixFileMode ??= info.UnixFileMode;
        }

        return result;
    }

    internal void MarkModified() => _modified = true;

    internal void Remove(ZipArchiveEntry entry)
    {
        _entries.Remove(entry);
        _modified = true;
    }

    /// <summary>Opens the stream that writes the content of a new entry.</summary>
    internal Stream OpenWrite(ZipArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return OpenWriteCore(entry);
    }

    private ZipWriter.EntryWriteStream OpenWriteCore(ZipArchiveEntry entry)
    {
        var writer = GetWriter();
        if (writer.OpenStream is not null)
            throw new InvalidOperationException("Another entry of the archive is being written: dispose its stream before opening another entry.");

        var stream = writer.OpenEntry(GetSettings(entry));
        stream.Completed = entry.MarkWritten;
        entry.State = ZipArchiveEntry.NewEntryState.Writing;
        return stream;
    }

    private ZipArchiveEntry CreateEntryCore(string entryName, ZipEntryOptions? options, long knownLength, bool isSymbolicLink)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (Mode is ZipArchiveMode.Read)
            throw new NotSupportedException("Entries cannot be created in an archive opened in Read mode.");

        if (entryName.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("The name of an entry cannot contain a null character.", nameof(entryName));

        if (_writer?.OpenStream is not null)
            throw new InvalidOperationException("Another entry of the archive is being written: dispose its stream before creating another entry.");

        var isDirectory = entryName[^1] is '/';
        if (isDirectory && isSymbolicLink)
            throw new ArgumentException("The name of a symbolic link cannot end with '/'.", nameof(entryName));

        var defaults = _compressionOptions;
        var password = options?.Password ?? defaults.Password;
        var encryption = options?.Encryption ?? defaults.Encryption;
        if (string.IsNullOrEmpty(password) || encryption is ZipEncryption.None || isDirectory)
        {
            password = null;
            encryption = ZipEncryption.None;
        }

        var (externalAttributes, versionMadeBy) = ZipArchiveEntry.GetExternalAttributes(options?.UnixFileMode, options?.Attributes, isDirectory, isSymbolicLink);
        var settings = new ZipEntrySettings
        {
            FullName = entryName,
            IsDirectory = isDirectory,
            Method = isDirectory || isSymbolicLink ? ZipCompressionMethod.Stored : options?.CompressionMethod ?? defaults.CompressionMethod,
            Level = options?.CompressionLevel ?? defaults.CompressionLevel,
            Password = password,
            Encryption = encryption,
            LastWriteTime = options?.LastWriteTime ?? DateTimeOffset.UtcNow,
            ExternalAttributes = externalAttributes,
            VersionMadeBy = versionMadeBy,
            Comment = options?.Comment ?? "",
            KnownLength = knownLength,
        };

        ZipWriter.Validate(settings);
        if (System.Text.Encoding.UTF8.GetByteCount(entryName) > ushort.MaxValue)
            throw new ArgumentException("The name of the entry is longer than 65535 bytes.", nameof(entryName));

        if (System.Text.Encoding.UTF8.GetByteCount(settings.Comment) > ushort.MaxValue)
            throw new ArgumentException("The comment of the entry is longer than 65535 bytes.", nameof(options));

        var isAes = encryption is ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256;
        var info = new ZipEntryInfo
        {
            FullName = entryName,
            Comment = settings.Comment,
            VersionMadeBy = versionMadeBy,
            ExternalAttributes = (int)externalAttributes,
            Flags = encryption is ZipEncryption.None ? (ushort)0 : ZipEntryInfo.FlagEncrypted,
            Method = (ushort)settings.Method,
            RawMethod = isAes ? ZipEntryInfo.AesMethod : (ushort)settings.Method,
            Encryption = encryption,
            HasAesExtra = isAes,
            LastWriteTime = settings.LastWriteTime,
            NameEncoding = System.Text.Encoding.UTF8,
        };

        var entry = new ZipArchiveEntry(this, info, settings);
        if (isDirectory)
        {
            var writer = GetWriter();
            var stream = writer.OpenEntry(settings);
            stream.Completed = entry.MarkWritten;
            stream.Dispose();
        }

        _entries.Add(entry);
        _modified = true;
        return entry;
    }

    private static ZipEntrySettings GetSettings(ZipArchiveEntry entry)
    {
        var settings = entry.Settings!;
        settings.ExternalAttributes = (uint)entry.Info.ExternalAttributes;
        settings.VersionMadeBy = entry.Info.VersionMadeBy;
        settings.Comment = entry.Info.Comment;
        return settings;
    }

    private ZipWriter GetWriter()
    {
        if (_writer is null)
        {
            // Update mode: the new entries are written to a temporary file, and copied when the archive is disposed
            _stagingStream = TemporaryFileStream.Create();
            _writer = new ZipWriter(_stagingStream, _compressionOptions.Zip64);
        }

        return _writer;
    }


    private protected override IReadOnlyList<ArchiveEntry> GetEntries() => Entries;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            try
            {
                CompleteAsync(async: false, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                _stagingStream?.Dispose();
                if (_ownsStream)
                {
                    _stream.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        _closed = true;
        try
        {
            await CompleteAsync(async: true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (_stagingStream is not null)
            {
                await _stagingStream.DisposeAsync().ConfigureAwait(false);
            }

            if (_ownsStream)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsyncCore().ConfigureAwait(false);
        }
    }

    private async ValueTask CompleteAsync(bool async, CancellationToken cancellationToken)
    {
        if (Mode is ZipArchiveMode.Read)
            return;

        // The entry being written, then the entries that are not opened: empty files
        if (_writer?.OpenStream is { } openStream)
        {
            if (async)
            {
                await openStream.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                openStream.Dispose();
            }
        }

        foreach (var entry in _entries)
        {
            if (entry.IsNew && entry.State is ZipArchiveEntry.NewEntryState.Created)
            {
                var stream = OpenWriteCore(entry);
                if (async)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    stream.Dispose();
                }
            }
        }

        var comment = ZipWriter.EncodeArchiveComment(_comment);
        if (Mode is ZipArchiveMode.Create)
        {
            var records = _entries.ConvertAll(GetRecord);
            if (async)
            {
                await _writer!.WriteCentralDirectoryAsync(records, comment, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _writer!.WriteCentralDirectory(records, comment);
            }

            return;
        }

        if (_modified)
        {
            Rewrite(comment);
        }
    }

    /// <summary>Gets the record of a new entry, with the comment and the attributes set after its content.</summary>
    private static ZipWriterRecord GetRecord(ZipArchiveEntry entry)
    {
        var record = entry.Record!;
        var info = entry.Info;
        var flags = record.Flags;
        record.Comment = ZipWriter.EncodeComment(info.Comment, ref flags);
        record.Flags = flags;
        record.ExternalAttributes = (uint)info.ExternalAttributes;
        record.VersionMadeBy = info.VersionMadeBy;
        return record;
    }

    /// <summary>Gets the record of an entry of the archive, from its central directory record.</summary>
    private ZipWriterRecord GetExistingRecord(ZipArchiveEntry entry)
    {
        var info = entry.Info;
        var raw = info.RawCentralRecord;
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(28));
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(30));
        var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(32));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(8));
        var extra = raw.AsSpan(ZipEntryInfo.CentralHeaderLength + nameLength, extraLength);
        var rawComment = raw.AsSpan(ZipEntryInfo.CentralHeaderLength + nameLength + extraLength, commentLength);
        var commentChanged = !string.Equals(entry.Info.Comment, entry.OriginalComment, StringComparison.Ordinal);
        var originalFlags = flags;
        var comment = commentChanged ? ZipWriter.EncodeComment(info.Comment, ref flags) : rawComment.ToArray();
        var name = raw.AsSpan(ZipEntryInfo.CentralHeaderLength, nameLength).ToArray();

        // A comment that needs UTF-8 sets the UTF-8 flag, which applies to the name too, and makes the readers ignore the
        // Unicode path extra field: a name whose bytes are not its UTF-8 encoding (code page 437, another encoding, or a
        // name given by the extra field) is written again as UTF-8, in the local header too
        var rewriteLocalName = false;
        if ((flags & ZipEntryInfo.FlagUtf8) != 0 && (originalFlags & ZipEntryInfo.FlagUtf8) is 0)
        {
            var utf8Name = Encoding.UTF8.GetBytes(info.FullName);
            if (!utf8Name.AsSpan().SequenceEqual(name))
            {
                name = utf8Name;
                rewriteLocalName = true;
            }
        }

        return new ZipWriterRecord
        {
            RewriteLocalName = rewriteLocalName,
            Name = name,
            Comment = comment,
            VersionMadeBy = info.VersionMadeBy,
            VersionNeeded = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(6)),
            Flags = flags,
            RawMethod = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(10)),
            Method = info.Method,
            DosTime = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(12)),
            DosDate = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(14)),
            UnixTime = null,
            ExternalAttributes = (uint)info.ExternalAttributes,
            InternalAttributes = info.InternalAttributes,
            Encryption = ZipEncryption.None,
            LocalZip64 = false,
            AlwaysZip64 = _compressionOptions.Zip64 is Zip64Mode.Always,
            Crc = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(16)),
            CompressedSize = info.CompressedSize,
            UncompressedSize = info.UncompressedSize,
            PreservedCentralExtra = ZipWriter.RemoveZip64Extra(extra),
        };
    }

    /// <summary>Writes the updated archive to a temporary file, then copies it over the archive.</summary>
    private void Rewrite(byte[] comment)
    {
        using var temporary = TemporaryFileStream.Create();

        // The data before the first entry (the stub of a self-extracting archive) are kept
        var prefix = long.MaxValue;
        foreach (var entry in _originalEntries)
        {
            prefix = Math.Min(prefix, entry.Info.LocalHeaderOffset);
        }

        if (prefix is > 0 and < long.MaxValue)
        {
            _stream.Position = 0;
            CopyBytes(_stream, temporary, prefix);
        }

        var writer = new ZipWriter(temporary, _compressionOptions.Zip64);
        var records = new List<ZipWriterRecord>(_entries.Count);
        foreach (var entry in _entries)
        {
            ZipWriterRecord record;
            if (entry.IsNew)
            {
                record = GetRecord(entry);
                writer.CopyEntry(_stagingStream!, record);
            }
            else
            {
                record = GetExistingRecord(entry);
                writer.CopyExistingEntry(_stream, entry.Info, record);
            }

            records.Add(record);
        }

        writer.WriteCentralDirectory(records, comment);

        temporary.Position = 0;
        _stream.Position = 0;
        temporary.CopyTo(_stream);
        _stream.SetLength(temporary.Length);
        _stream.Flush();
    }

    private static void CopyBytes(Stream source, Stream destination, long length)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read is 0)
                throw new InvalidDataException("The zip archive is truncated.");

            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    private void ReadCentralDirectory()
    {
        var length = _stream.Length;
        if (length < EndOfCentralDirectoryLength)
            throw new InvalidDataException("The stream is not a zip archive: it is too short.");

        // The end of central directory record is followed by a comment of at most 65535 bytes
        var tailLength = (int)Math.Min(length, EndOfCentralDirectoryLength + ushort.MaxValue);
        var tailStart = length - tailLength;
        var tail = ReadAt(tailStart, tailLength);
        var eocdIndex = FindEndOfCentralDirectory(tail, tailStart, length);
        var eocd = tail.AsSpan(eocdIndex);
        var eocdPosition = tailStart + eocdIndex;

        uint diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(eocd[4..]);
        uint centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd[6..]);
        long entryCount = BinaryPrimitives.ReadUInt16LittleEndian(eocd[10..]);
        long centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..]);
        long centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..]);
        var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(eocd[20..]);
        var comment = eocd[EndOfCentralDirectoryLength..];
        _comment = DecodeComment(comment[..Math.Min(commentLength, comment.Length)]);

        // The central directory ends where the zip64 end of central directory record, or the end of central directory record, starts
        var centralDirectoryEnd = eocdPosition;
        if (eocdPosition >= Zip64LocatorLength)
        {
            var locator = ReadAt(eocdPosition - Zip64LocatorLength, Zip64LocatorLength);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) == Zip64LocatorSignature)
            {
                var zip64Disk = BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(4));
                var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
                var record = FindZip64EndOfCentralDirectory(zip64Disk, zip64Offset, eocdPosition - Zip64LocatorLength, diskNumber, out var recordPosition);
                diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));
                centralDirectoryDisk = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20));
                entryCount = ToInt64(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32)));
                centralDirectorySize = ToInt64(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(40)));
                centralDirectoryOffset = ToInt64(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(48)));
                centralDirectoryEnd = recordPosition;
            }
        }

        var multiVolume = CheckVolumes(diskNumber);
        if (centralDirectorySize > centralDirectoryEnd)
            throw new InvalidDataException("The size of the central directory of the zip archive is invalid.");

        // The central directory may not be at its offset when data is prepended to the archive (a self-extracting archive):
        // the offsets are then shifted by the difference, as Info-ZIP does
        var expectedPosition = GetVolumeStart(centralDirectoryDisk, multiVolume) + centralDirectoryOffset;
        var actualPosition = centralDirectoryEnd - centralDirectorySize;
        var delta = 0L;
        if (expectedPosition != actualPosition && !multiVolume && !HasCentralHeaderAt(expectedPosition, centralDirectorySize, centralDirectoryEnd))
        {
            delta = actualPosition - expectedPosition;
            expectedPosition = actualPosition;
        }

        if (expectedPosition < 0 || expectedPosition > centralDirectoryEnd - centralDirectorySize)
            throw new InvalidDataException("The offset of the central directory of the zip archive is invalid.");

        if (centralDirectorySize > Array.MaxLength)
            throw new NotSupportedException("The central directory of the zip archive is too large.");

        // Each record has at least 46 bytes: a larger count is invalid, and must not size an allocation
        if (entryCount > centralDirectorySize / ZipEntryInfo.CentralHeaderLength)
            throw new InvalidDataException("The number of entries of the zip archive is larger than its central directory.");

        var centralDirectory = ReadAt(expectedPosition, (int)centralDirectorySize);
        if (centralDirectory.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(centralDirectory) == ArchiveExtraDataSignature)
            throw new NotSupportedException("The central directory of the zip archive is encrypted, which is not supported.");

        _entries.Capacity = (int)entryCount;
        var data = centralDirectory.AsSpan();
        while (data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == ZipEntryInfo.CentralHeaderSignature)
        {
            var info = ZipEntryInfo.ParseCentral(data, _options, out var recordLength);
            data = data[recordLength..];

            var offset = GetVolumeStart(info.DiskNumber, multiVolume) + info.LocalHeaderOffset + delta;
            if (offset < 0 || offset > length - ZipEntryInfo.LocalHeaderLength)
                throw new InvalidDataException($"The offset of the local header of the zip entry '{info.FullName}' is invalid.");

            info.LocalHeaderOffset = offset;
            var entry = new ZipArchiveEntry(this, info, OpenEntry, fromCentralDirectory: true, sizesKnown: true);
            _entries.Add(entry);
            _originalEntries.Add(entry);
        }

        if (_entries.Count < entryCount)
            throw new InvalidDataException("The central directory of the zip archive has fewer entries than its end record says.");
    }

    private static int FindEndOfCentralDirectory(byte[] tail, long tailStart, long length)
    {
        // The record whose comment ends at the end of the stream; otherwise, the last one (trailing data, or a truncated comment)
        int? fallback = null;
        for (var i = tail.Length - EndOfCentralDirectoryLength; i >= 0; i--)
        {
            if (tail[i] != 'P' || BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != EndOfCentralDirectorySignature)
                continue;

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20));
            if (tailStart + i + EndOfCentralDirectoryLength + commentLength == length)
                return i;

            fallback ??= i;
        }

        return fallback ?? throw new InvalidDataException("The stream is not a zip archive: the end of central directory record is not found.");
    }

    private byte[] FindZip64EndOfCentralDirectory(uint disk, ulong offset, long locatorPosition, uint lastDisk, out long position)
    {
        // At the offset of the locator; otherwise right before the locator (data prepended to the archive)
        var multiVolume = lastDisk > 0 && _volumes is not null;
        if (offset <= long.MaxValue && (!multiVolume || disk < _volumes!.VolumeCount))
        {
            var candidate = (multiVolume ? _volumes!.GetVolumeStart((int)disk) : 0) + (long)offset;
            if (candidate >= 0 && candidate <= locatorPosition - Zip64EndOfCentralDirectoryLength)
            {
                var record = ReadAt(candidate, Zip64EndOfCentralDirectoryLength);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record) == Zip64EndOfCentralDirectorySignature)
                {
                    position = candidate;
                    return record;
                }
            }
        }

        if (locatorPosition >= Zip64EndOfCentralDirectoryLength)
        {
            position = locatorPosition - Zip64EndOfCentralDirectoryLength;
            var record = ReadAt(position, Zip64EndOfCentralDirectoryLength);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) == Zip64EndOfCentralDirectorySignature)
                return record;
        }

        throw new InvalidDataException("The zip64 end of central directory record of the zip archive is not found.");
    }

    private bool CheckVolumes(uint lastDisk)
    {
        if (lastDisk is 0)
            return false; // A single volume, or the parts of an archive split by 7-Zip, whose offsets are global

        var volumeCount = _volumes?.VolumeCount ?? 1;
        if (volumeCount is 1)
            throw new InvalidDataException($"The zip archive is the last volume of a split archive of {(long)lastDisk + 1} volumes: open the first volume (.z01) with a volume resolver, or open the archive from its path.");

        if (lastDisk + 1 != volumeCount)
            throw new InvalidDataException($"The split zip archive has {(long)lastDisk + 1} volumes, but {volumeCount} volumes are found.");

        return true;
    }

    private long GetVolumeStart(uint disk, bool multiVolume)
    {
        if (!multiVolume)
            return 0;

        if (disk >= _volumes!.VolumeCount)
            throw new InvalidDataException("A disk number of the split zip archive is invalid.");

        return _volumes.GetVolumeStart((int)disk);
    }

    private bool HasCentralHeaderAt(long position, long centralDirectorySize, long centralDirectoryEnd)
    {
        if (centralDirectorySize is 0 || position < 0 || position > centralDirectoryEnd - 4)
            return false;

        return BinaryPrimitives.ReadUInt32LittleEndian(ReadAt(position, 4)) == ZipEntryInfo.CentralHeaderSignature;
    }

    private string DecodeComment(ReadOnlySpan<byte> comment)
    {
        if (comment.IsEmpty)
            return "";

        return _options.EntryNameEncoding is { } encoding ? encoding.GetString(comment) : Cp437.GetString(comment);
    }

    private byte[] ReadAt(long position, int count)
    {
        var buffer = new byte[count];
        _stream.Position = position;
        _stream.ReadExactlyOrThrow(buffer);
        return buffer;
    }

    private static long ToInt64(ulong value)
    {
        if (value > long.MaxValue)
            throw new InvalidDataException("A size or an offset of the zip64 end of central directory record is invalid.");

        return (long)value;
    }

    private Stream OpenEntry(ZipArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var info = entry.Info;
        ZipEntryOpener.ThrowIfNotSupported(info);

        var header = ReadAt(info.LocalHeaderOffset, ZipEntryInfo.LocalHeaderLength);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ZipEntryInfo.LocalHeaderSignature)
            throw new InvalidDataException($"The local header of the zip entry '{info.FullName}' is not found.");

        var dataStart = info.LocalHeaderOffset + ZipEntryInfo.LocalHeaderLength + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        if (info.CompressedSize > _stream.Length - dataStart)
            throw new InvalidDataException($"The content of the zip entry '{info.FullName}' is truncated.");

        var raw = new BoundedReadStream(_stream, dataStart, info.CompressedSize);
        return ZipEntryOpener.Open(info, raw, info.UncompressedSize, _options, onEnd: null, isValid: null);
    }
}
