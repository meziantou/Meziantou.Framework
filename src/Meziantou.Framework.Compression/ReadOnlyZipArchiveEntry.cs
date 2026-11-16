using System.Text;
using Meziantou.Framework.Compression.Internals.Zip;

namespace Meziantou.Framework.Compression;

/// <summary>
/// An entry of a zip archive, from the central directory of a <see cref="ReadOnlyZipArchive"/>, or from the local header
/// of a forward-only reader. A local header does not have the comment, the "version made by" and the external attributes
/// of the entry: a forward-only reader returns an empty comment, 0, and no attributes, permissions or link target.
/// </summary>
public sealed class ReadOnlyZipArchiveEntry : ArchiveEntry
{
    private const int MaxLinkTargetLength = 32 * 1024;

    private readonly ZipEntryInfo _info;
    private readonly Func<ReadOnlyZipArchiveEntry, Stream> _open;
    private readonly bool _fromCentralDirectory;
    private bool _sizesKnown;
    private bool _linkTargetRead;
    private string? _linkTarget;

    internal ReadOnlyZipArchiveEntry(ZipEntryInfo info, Func<ReadOnlyZipArchiveEntry, Stream> open, bool fromCentralDirectory, bool sizesKnown)
    {
        _info = info;
        _open = open;
        _fromCentralDirectory = fromCentralDirectory;
        _sizesKnown = sizesKnown;
    }

    internal ZipEntryInfo Info => _info;

    /// <inheritdoc/>
    public override string FullName => _info.FullName;

    /// <summary>
    /// Gets the length of the content of the entry. A forward-only reader returns -1 for a streamed entry (an entry with a
    /// data descriptor) until its content is read: the data descriptor that follows the content gives it.
    /// </summary>
    public override long Length => _sizesKnown ? _info.UncompressedSize : -1;

    /// <summary>
    /// Gets the length of the content of the entry in the archive (compressed and encrypted). A forward-only reader returns
    /// -1 for a streamed entry until its content is read.
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
                _linkTarget = IsSymbolicLink ? ReadLinkTarget() : null;
                _linkTargetRead = true;
            }

            return _linkTarget;
        }
    }

    /// <inheritdoc/>
    public override bool IsEncrypted => _info.IsEncrypted;

    /// <summary>
    /// Gets the CRC-32 of the content, or <see langword="null"/> when the archive does not store it: an AES entry of
    /// version AE-2, or a streamed entry of a forward-only reader before its content is read.
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
    /// Opens the content of the entry. The stream checks the length and the CRC-32 of the content (or the authentication code
    /// of an AES entry) and throws an <see cref="InvalidDataException"/> at its end when they do not match. The streams of
    /// the entries of a <see cref="ReadOnlyZipArchive"/> can be read alternately, but not from several threads at once.
    /// </summary>
    /// <returns>A read-only stream of the content of the entry.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The entry is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The entry is compressed or encrypted with a method that is not supported.</exception>
    public override Stream Open() => _open(this);

    internal void SetSizesKnown() => _sizesKnown = true;

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
