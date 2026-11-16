using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Compression.Internals.Zip;

namespace Meziantou.Framework.Compression;

/// <summary>
/// An entry of a zip archive: an entry of the central directory of a <see cref="ZipArchive"/>, a new entry of an archive in
/// <see cref="ZipArchiveMode.Create"/> or <see cref="ZipArchiveMode.Update"/> mode, or an entry of the local headers of a
/// forward-only reader. A local header does not have the comment, the "version made by" and the external attributes of the
/// entry: a forward-only reader returns an empty comment, 0, and no attributes, permissions or link target.
/// </summary>
/// <remarks>
/// The metadata of a new entry are changed with the <c>Set</c> methods (the properties of <see cref="ArchiveEntry"/> are
/// read-only): the time before its content is opened, since the local header that precedes the content stores it; the
/// comment, the attributes and the permissions until the archive is disposed. In <see cref="ZipArchiveMode.Update"/> mode,
/// the comment, the attributes and the permissions of an existing entry can be changed too: they are stored in the central
/// directory only.
/// </remarks>
public sealed class ZipArchiveEntry : ArchiveEntry
{
    private const int MaxLinkTargetLength = 32 * 1024;
    private const uint UnixFileTypeMask = 0xF000;
    private const uint UnixDirectory = 0x4000;
    private const uint UnixRegularFile = 0x8000;
    private const uint UnixSymbolicLink = 0xA000;

    private readonly ZipEntryInfo _info;
    private readonly Func<ZipArchiveEntry, Stream>? _open;
    private readonly bool _fromCentralDirectory;
    private bool _sizesKnown;
    private bool _linkTargetRead;
    private string? _linkTarget;

    internal ZipArchiveEntry(ZipArchive? archive, ZipEntryInfo info, Func<ZipArchiveEntry, Stream> open, bool fromCentralDirectory, bool sizesKnown)
    {
        Archive = archive;
        _info = info;
        _open = open;
        _fromCentralDirectory = fromCentralDirectory;
        _sizesKnown = sizesKnown;
        OriginalComment = info.Comment;
    }

    internal ZipArchiveEntry(ZipArchive archive, ZipEntryInfo info, ZipEntrySettings settings)
    {
        Archive = archive;
        _info = info;
        _fromCentralDirectory = true;
        Settings = settings;
        State = settings.IsDirectory ? NewEntryState.Written : NewEntryState.Created;
    }

    internal enum NewEntryState
    {
        Created,
        Writing,
        Written,
    }

    internal ZipEntryInfo Info => _info;

    /// <summary>Gets the settings of a new entry, or <see langword="null"/> for an entry read from an archive.</summary>
    internal ZipEntrySettings? Settings { get; }

    internal NewEntryState State { get; set; }

    /// <summary>Gets or sets the record of a new entry, once it is written.</summary>
    internal ZipWriterRecord? Record { get; set; }

    internal bool IsNew => Settings is not null;

    /// <summary>Gets the comment the entry had in the archive, to know whether it changed.</summary>
    internal string OriginalComment { get; } = "";

    internal bool IsDeleted { get; private set; }

    /// <summary>Gets the archive of the entry, or <see langword="null"/> for an entry of a forward-only reader.</summary>
    public ZipArchive? Archive { get; }

    /// <inheritdoc/>
    public override string FullName => _info.FullName;

    /// <summary>
    /// Gets the length of the content of the entry. A forward-only reader returns -1 for a streamed entry (an entry with a
    /// data descriptor) until its content is read: the data descriptor that follows the content gives it. A new entry
    /// returns -1 until its content is written.
    /// </summary>
    public override long Length => _sizesKnown ? _info.UncompressedSize : -1;

    /// <summary>
    /// Gets the length of the content of the entry in the archive (compressed and encrypted). A forward-only reader returns
    /// -1 for a streamed entry until its content is read, and a new entry until its content is written.
    /// </summary>
    public override long CompressedLength => _sizesKnown ? _info.CompressedSize : -1;

    /// <summary>
    /// Gets the last time the entry was modified: the NTFS time (extra field 0x000A) or the Unix time (extra field 0x5455)
    /// when the entry has one, in UTC; otherwise the MS-DOS time of the header, which has no time zone and is returned as a
    /// time of the local time zone, as <see cref="System.IO.Compression.ZipArchiveEntry.LastWriteTime"/> does. It is
    /// <see langword="null"/> when the MS-DOS time is not a valid date.
    /// </summary>
    public override DateTimeOffset? LastWriteTime => _info.LastWriteTime;

    /// <summary>Gets the time the entry was created, from the NTFS extra field (or the extended timestamp of a local header), or <see langword="null"/>.</summary>
    public override DateTimeOffset? CreationTime => _info.CreationTime;

    /// <summary>Gets the last time the entry was accessed, from the NTFS extra field (or the extended timestamp of a local header), or <see langword="null"/>.</summary>
    public override DateTimeOffset? LastAccessTime => _info.LastAccessTime;

    /// <summary>Gets a value indicating whether the entry is a directory: its name ends with <c>/</c>, or its attributes say so.</summary>
    public override bool IsDirectory
    {
        get
        {
            if (_info.FullName is [.., '/'])
                return true;

            if (!_fromCentralDirectory)
                return false;

            if (_info.HasUnixAttributes)
                return ((_info.ExternalAttributes >> 16) & 0xF000) == 0x4000;

            return _info.HasDosAttributes && (_info.ExternalAttributes & (int)FileAttributes.Directory) != 0;
        }
    }

    /// <summary>
    /// Gets the target of a symbolic link: the content of an entry whose Unix mode is a symbolic link (S_IFLNK). Reading it
    /// reads the content of the entry, decoded as the name is (UTF-8 when the name is not marked as UTF-8 and the options do
    /// not set an encoding), and may throw the exceptions of <see cref="Open"/>.
    /// </summary>
    public override string? LinkTarget
    {
        get
        {
            if (!_linkTargetRead)
            {
                _linkTarget = IsSymbolicLink && !IsNew ? ReadLinkTarget() : null;
                _linkTargetRead = true;
            }

            return _linkTarget;
        }
    }

    /// <inheritdoc/>
    public override bool IsEncrypted => _info.IsEncrypted;

    /// <summary>
    /// Gets the CRC-32 of the content, or <see langword="null"/> when the archive does not store it: an AES entry of
    /// version AE-2, a streamed entry of a forward-only reader before its content is read, or a new entry before its
    /// content is written.
    /// </summary>
    public override uint? Crc32 => _sizesKnown ? _info.ExpectedCrc : null;

    /// <summary>Gets the Windows attributes of an entry created on MS-DOS or Windows (FAT, NTFS, VFAT, OS/2), or <see langword="null"/>.</summary>
    public override FileAttributes? Attributes => _fromCentralDirectory && _info.HasDosAttributes ? (FileAttributes)(_info.ExternalAttributes & 0xFFFF) : null;

    /// <summary>Gets the permissions of an entry created on Unix or macOS, or <see langword="null"/>.</summary>
    public override UnixFileMode? UnixFileMode => _fromCentralDirectory && _info.HasUnixAttributes && (_info.ExternalAttributes >> 16) != 0 ? (UnixFileMode)((_info.ExternalAttributes >> 16) & 0xFFF) : null;

    /// <summary>Gets the comment of the entry, or an empty string.</summary>
    public string Comment => _info.Comment;

    /// <summary>
    /// Gets the compression method of the entry. For an AES-encrypted entry (method 99 in the headers), it is the method of
    /// the AES extra field. A value that is not a member of <see cref="ZipCompressionMethod"/> is an unknown method.
    /// </summary>
    public ZipCompressionMethod CompressionMethod => (ZipCompressionMethod)_info.Method;

    /// <summary>Gets the encryption of the content of the entry.</summary>
    public ZipEncryption EncryptionMethod => _info.Encryption;

    /// <summary>Gets the "version made by" field of the central directory: the operating system that created the entry in the high byte, and the version of the specification in the low byte.</summary>
    public ushort VersionMadeBy => _info.VersionMadeBy;

    /// <summary>Gets the general purpose bit flags of the entry.</summary>
    public ushort GeneralPurposeFlags => _info.Flags;

    /// <summary>Gets the external attributes of the entry: MS-DOS attributes in the low 16 bits, and a Unix mode in the high 16 bits for an entry created on Unix.</summary>
    public int ExternalAttributes => _info.ExternalAttributes;

    private bool IsSymbolicLink => _fromCentralDirectory && _info.HasUnixAttributes && ((_info.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    /// <summary>
    /// Opens the content of the entry: a read-only stream for an entry of the archive, or a write-only stream for a new entry
    /// (once; disposing it completes the entry). The read stream checks the length and the CRC-32 of the content (or the
    /// authentication code of an AES entry) and throws an <see cref="InvalidDataException"/> at its end when they do not
    /// match. The streams of the entries of a <see cref="ZipArchive"/> can be read alternately, but not from several threads
    /// at once. One new entry is written at a time.
    /// </summary>
    /// <returns>A stream of the content of the entry.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The entry is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The entry is compressed or encrypted with a method that is not supported.</exception>
    /// <exception cref="InvalidOperationException">The new entry is a directory, or another new entry is being written.</exception>
    /// <exception cref="IOException">The new entry is already written.</exception>
    public override Stream Open()
    {
        EnsureNotDeleted();
        if (!IsNew)
            return _open!(this);

        if (IsDirectory)
            throw new InvalidOperationException("The entry is a directory: it has no content.");

        if (State is not NewEntryState.Created)
            throw new IOException("A new entry can be opened only once.");

        return Archive!.OpenWrite(this);
    }

    /// <summary>Deletes the entry from the archive.</summary>
    /// <exception cref="NotSupportedException">The archive is not in <see cref="ZipArchiveMode.Update"/> mode.</exception>
    /// <exception cref="InvalidOperationException">The content of the entry is being written.</exception>
    public void Delete()
    {
        EnsureNotDeleted();
        if (Archive is not { Mode: ZipArchiveMode.Update })
            throw new NotSupportedException("Entries can be deleted only in an archive opened in Update mode.");

        if (State is NewEntryState.Writing)
            throw new InvalidOperationException("The content of the entry is being written: dispose its stream first.");

        IsDeleted = true;
        Archive.Remove(this);
    }

    /// <summary>Sets the last time the entry was modified. It can be set until the content of a new entry is opened.</summary>
    /// <param name="value">The time.</param>
    /// <exception cref="NotSupportedException">The entry is not a new entry.</exception>
    /// <exception cref="InvalidOperationException">The content of the entry is opened already: its local header is written.</exception>
    public void SetLastWriteTime(DateTimeOffset value)
    {
        EnsureNotDeleted();
        if (!IsNew)
            throw new NotSupportedException("The time of an entry of an existing archive cannot be changed: it is stored in its local header, which is copied as it is.");

        if (State is not NewEntryState.Created && !Settings!.IsDirectory)
            throw new InvalidOperationException("The content of the entry is opened already: its time cannot be changed.");

        Settings!.LastWriteTime = value;
        _info.LastWriteTime = value;
    }

    /// <summary>
    /// Sets the Unix permissions of the entry. The entry is then marked as created on Unix, and the high 16 bits of its
    /// external attributes are its mode: its type (a file, a directory or a symbolic link) and its permissions.
    /// </summary>
    /// <param name="value">The permissions, or <see langword="null"/> to mark the entry as created on MS-DOS, without permissions.</param>
    public void SetUnixFileMode(UnixFileMode? value)
    {
        EnsureWritable();
        SetExternalAttributes(value, Attributes ?? GetLowAttributes());
        Archive!.MarkModified();
    }

    /// <summary>Sets the MS-DOS attributes of the entry: the low 16 bits of the external attributes. The Unix permissions are kept.</summary>
    /// <param name="value">The attributes, or <see langword="null"/> for none (<see cref="FileAttributes.Directory"/> for a directory).</param>
    public void SetAttributes(FileAttributes? value)
    {
        EnsureWritable();
        SetExternalAttributes(UnixFileMode, value);
        Archive!.MarkModified();
    }

    /// <summary>Sets the comment of the entry.</summary>
    /// <param name="value">The comment, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">The comment is longer than 65535 bytes.</exception>
    public void SetComment(string? value)
    {
        EnsureWritable();
        value ??= "";
        if (Encoding.UTF8.GetByteCount(value) > ushort.MaxValue)
            throw new ArgumentException("The comment is longer than 65535 bytes.", nameof(value));

        _info.Comment = value;
        Archive!.MarkModified();
        if (Settings is not null)
        {
            Settings.Comment = value;
        }
    }

    internal void MarkWritten(ZipWriterRecord record)
    {
        Record = record;
        State = NewEntryState.Written;
        _info.Crc = record.Crc;
        _info.CompressedSize = record.CompressedSize;
        _info.UncompressedSize = record.UncompressedSize;
        _info.AesVendorVersion = record.AesVendorVersion;
        _info.HasAesExtra = record.IsAes;
        _info.Flags = record.Flags;
        _info.Method = record.Method;
        _info.RawMethod = record.RawMethod;
        _sizesKnown = true;
    }

    internal void SetLinkTarget(string target)
    {
        _linkTarget = target;
        _linkTargetRead = true;
    }

    internal void SetSizesKnown() => _sizesKnown = true;

    /// <summary>Gets the external attributes and the "version made by" of a Unix mode and MS-DOS attributes.</summary>
    internal static (uint ExternalAttributes, ushort VersionMadeBy) GetExternalAttributes(UnixFileMode? mode, FileAttributes? attributes, bool isDirectory, bool isSymbolicLink)
    {
        var dos = attributes is { } value ? (uint)value & 0xFFFF : 0;
        if (isDirectory)
        {
            dos |= (uint)FileAttributes.Directory;
        }

        if (mode is null && !isSymbolicLink)
            return (dos, (ZipWriter.HostMsDos << 8) | ZipWriter.VersionMadeBySpecification);

        var permissions = mode is { } m ? (uint)m & 0xFFF : 0x1FF;
        var type = isSymbolicLink ? UnixSymbolicLink : isDirectory ? UnixDirectory : UnixRegularFile;
        return ((((type & UnixFileTypeMask) | permissions) << 16) | dos, (ZipWriter.HostUnix << 8) | ZipWriter.VersionMadeBySpecification);
    }

    private FileAttributes? GetLowAttributes()
    {
        var low = _info.ExternalAttributes & 0xFFFF & ~(int)FileAttributes.Directory;
        return low is 0 ? null : (FileAttributes)low;
    }

    private void SetExternalAttributes(UnixFileMode? mode, FileAttributes? attributes)
    {
        var (external, versionMadeBy) = GetExternalAttributes(mode, attributes, IsDirectory, IsSymbolicLink);
        _info.ExternalAttributes = (int)external;
        _info.VersionMadeBy = (ushort)((versionMadeBy & 0xFF00) | (_info.VersionMadeBy is 0 ? ZipWriter.VersionMadeBySpecification : _info.VersionMadeBy & 0xFF));
    }

    private void EnsureWritable()
    {
        EnsureNotDeleted();
        if (Archive is null || Archive.Mode is ZipArchiveMode.Read)
            throw new NotSupportedException("The entries of an archive opened in Read mode cannot be changed.");
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
            throw new InvalidOperationException("The entry is deleted.");
    }

    private string ReadLinkTarget()
    {
        using var stream = Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxLinkTargetLength)
                throw new InvalidDataException($"The target of the symbolic link '{FullName}' is too long.");

            buffer.Write(chunk, 0, read);
        }

        return (_info.NameEncoding ?? Encoding.UTF8).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
