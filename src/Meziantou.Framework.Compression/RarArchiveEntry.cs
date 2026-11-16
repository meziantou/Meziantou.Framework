using System.Text;
using Meziantou.Framework.Compression.Internals.Rar;

namespace Meziantou.Framework.Compression;

/// <summary>
/// An entry of a RAR archive: a file, a directory, or a link. An entry whose data are split between volumes is one entry.
/// The entries of <see cref="RarArchive"/> and of the forward-only reader of RAR archives are of this type.
/// </summary>
public sealed class RarArchiveEntry : ArchiveEntry
{
    private const int MaxLinkTargetLength = 64 * 1024;

    private readonly Func<RarArchiveEntry, Stream> _open;
    private string? _linkTarget;
    private byte[]? _linkTargetBytes;

    internal RarArchiveEntry(RarEntryData data, RarArchive? archive, Func<RarArchiveEntry, Stream> open)
    {
        Data = data;
        Archive = archive;
        _open = open;
    }

    internal RarEntryData Data { get; }

    private RarFileHeader Header => Data.Header;

    /// <summary>
    /// Gets the archive of the entry, or <see langword="null"/> for an entry of the forward-only reader (its content can
    /// be read only while it is the current entry of the reader).
    /// </summary>
    public RarArchive? Archive { get; }

    /// <summary>
    /// Gets the path of the entry in the archive, with <c>/</c> separators. A version of a file archived with the
    /// <c>-ver</c> switch of RAR has the suffix <c>;N</c> where N is its version number, as in the listing of unrar.
    /// </summary>
    public override string FullName => Header.Name;

    /// <summary>Gets the length of the content of the entry: 0 for a directory, -1 when the archive does not store it. For a link of RAR5, it is the length the archive records (the length of the target), while <see cref="Open"/> returns an empty stream for a symbolic link.</summary>
    public override long Length => Header.IsDirectory ? 0 : Header.UnpackedSize;

    /// <summary>
    /// Gets the length of the compressed data of the entry, in all its volumes. In the forward-only reader, the length of
    /// the parts of an entry split between volumes is known once its content is read.
    /// </summary>
    public override long CompressedLength => Data.CompressedLength;

    /// <inheritdoc/>
    public override DateTimeOffset? LastWriteTime => Header.LastWriteTime;

    /// <inheritdoc/>
    public override DateTimeOffset? CreationTime => Header.CreationTime;

    /// <inheritdoc/>
    public override DateTimeOffset? LastAccessTime => Header.LastAccessTime;

    /// <inheritdoc/>
    public override bool IsDirectory => Header.IsDirectory && Header.RedirectionType is RarRedirectionType.None;

    /// <inheritdoc/>
    public override bool IsEncrypted => Header.IsEncrypted;

    /// <summary>
    /// Gets the CRC-32 of the content of the entry, or <see langword="null"/> when the archive does not store it (a RAR5
    /// archive may store a BLAKE2sp hash instead), when it stores a CRC that depends on the encryption key, or when the
    /// header of the last part of an entry split between volumes is not read yet (in the forward-only reader).
    /// </summary>
    public override uint? Crc32 => Header.Encryption?.UseMac is true || Data.IsSplitAfter ? null : Data.Crc32;

    /// <summary>
    /// Gets the BLAKE2sp hash (32 bytes) of the content of the entry, or an empty value when the archive does not store
    /// it, when it stores a hash that depends on the encryption key, or when the header of the last part of an entry
    /// split between volumes is not read yet (in the forward-only reader).
    /// </summary>
    public ReadOnlyMemory<byte> Blake2spHash => Header.Encryption?.UseMac is true || Data.IsSplitAfter || Data.Blake2 is null ? ReadOnlyMemory<byte>.Empty : Data.Blake2;

    /// <summary>Gets the Windows attributes of the entry, for an entry created on Windows, MS-DOS or OS/2.</summary>
    public override FileAttributes? Attributes => Header.HostOperatingSystem is RarHostOperatingSystem.Windows or RarHostOperatingSystem.MSDos or RarHostOperatingSystem.OS2 ? (FileAttributes)Header.Attributes : null;

    /// <summary>Gets the Unix permissions of the entry, for an entry created on Unix.</summary>
    public override UnixFileMode? UnixFileMode => Header.HostOperatingSystem is RarHostOperatingSystem.Unix ? (UnixFileMode)(Header.Attributes & 0xFFF) : null;

    /// <summary>Gets the operating system that created the entry, which defines the meaning of its attributes.</summary>
    public RarHostOperatingSystem HostOperatingSystem => Header.HostOperatingSystem;

    /// <summary>Gets the raw attributes of the entry: Windows attributes or a Unix mode, depending on <see cref="HostOperatingSystem"/>.</summary>
    public uint HostAttributes => Header.Attributes;

    /// <summary>Gets a value indicating whether the entry continues the compression dictionary of the previous entry (a solid archive).</summary>
    public bool IsSolid => Header.IsSolid;

    /// <summary>
    /// Gets the version of the compression algorithm: 15, 20, 26, 29 or 36 for the archives of RAR 1.5-4.x (RAR 1.5, 2.0,
    /// 2.6, 2.9), 50 for the RAR 5.0 algorithm and 70 for the RAR 7.0 algorithm.
    /// </summary>
    public int CompressionVersion => Header.UnpackVersion;

    /// <summary>Gets the compression method: 0 for stored data, 1 (fastest) to 5 (best) for compressed data.</summary>
    public int CompressionMethod => Header.Method;

    /// <summary>Gets the size of the dictionary of the compression, or 0 for a directory.</summary>
    public long DictionarySize => Header.IsDirectory ? 0 : Header.DictionarySize;

    /// <summary>
    /// Gets the target of a symbolic link (a Unix or Windows symbolic link, or a Windows junction of RAR5, a Unix symbolic
    /// link of RAR 1.5-4.x), or <see langword="null"/>. The separators are <c>/</c>; the target of a junction is absolute.
    /// For a link of RAR 1.5-4.x, the target is the content of the entry, read on the first access.
    /// </summary>
    public override string? LinkTarget
    {
        get
        {
            if (Header.RedirectionType is RarRedirectionType.UnixSymlink or RarRedirectionType.WindowsSymlink or RarRedirectionType.WindowsJunction)
                return Header.RedirectionTarget;

            if (!Data.IsRar4Symlink)
                return null;

            if (_linkTargetBytes is null)
            {
                ReadLinkTarget();
            }

            return _linkTarget;
        }
    }

    /// <summary>
    /// Gets the path of the entry whose content this entry shares: the target of a hard link or of a file copy of RAR5, or
    /// <see langword="null"/>. <see cref="Open"/> returns the content of the target in a <see cref="RarArchive"/>, and an
    /// empty stream in the forward-only reader.
    /// </summary>
    public override string? HardLinkTarget => Header.RedirectionType is RarRedirectionType.HardLink or RarRedirectionType.FileCopy ? Header.RedirectionTarget : null;

    /// <inheritdoc/>
    public override Stream Open()
    {
        // The content of a symbolic link of RAR 1.5-4.x is its target, which may be read already
        if (_linkTargetBytes is not null)
            return new MemoryStream(_linkTargetBytes, writable: false);

        return _open(this);
    }

    private void ReadLinkTarget()
    {
        if (Header.UnpackedSize > MaxLinkTargetLength)
            throw new InvalidDataException($"The target of the symbolic link '{FullName}' is too long.");

        using var stream = _open(this);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        _linkTarget = System.Text.Unicode.Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
        _linkTargetBytes = bytes;
    }
}
