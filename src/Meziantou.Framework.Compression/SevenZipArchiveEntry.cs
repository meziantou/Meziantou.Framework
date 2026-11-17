using System.Text;
using Meziantou.Framework.Compression.Internals.SevenZip;

namespace Meziantou.Framework.Compression;

/// <summary>
/// An entry of a <see cref="SevenZipArchive"/>. The times, the attributes and the Unix permissions of an entry can be changed
/// in <see cref="SevenZipArchiveMode.Create"/> and <see cref="SevenZipArchiveMode.Update"/> modes with the <c>Set</c>
/// methods (the properties of <see cref="ArchiveEntry"/> are read-only, and C# cannot add a setter to an overridden property).
/// </summary>
public sealed class SevenZipArchiveEntry : ArchiveEntry
{
    private string? _linkTarget;
    private bool _linkTargetRead;
    private bool _opened;

    internal SevenZipArchiveEntry(SevenZipArchive archive, SevenZipFileItem item, bool isNew)
    {
        Archive = archive;
        Item = item;
        IsNew = isNew;
    }

    internal SevenZipFileItem Item { get; }

    internal bool IsNew { get; }

    /// <summary>Gets the archive of the entry.</summary>
    public SevenZipArchive Archive { get; }

    /// <summary>Gets the path of the entry in the archive, with <c>/</c> separators. The name of a directory ends with <c>/</c>.</summary>
    public override string FullName => Item.IsDirectory && !Item.Name.EndsWith('/', StringComparison.Ordinal) ? Item.Name + "/" : Item.Name;

    /// <summary>Gets the length of the content of the entry. For a new entry, it is the number of bytes written to it.</summary>
    public override long Length => Item.Size;

    /// <summary>
    /// Gets the length of the packed content of the folder of the entry when the entry is the only file of its folder, 0 for
    /// an entry without content, or -1 when the folder has several files (a solid folder) or is not written yet.
    /// </summary>
    public override long CompressedLength
    {
        get
        {
            if (!Item.HasStream)
                return IsNew && _opened ? -1 : 0;

            return Item.Folder is { UnpackStreamCount: 1 } folder ? folder.TotalPackSize : -1;
        }
    }

    /// <inheritdoc/>
    public override DateTimeOffset? LastWriteTime => SevenZipArchiveFormat.ToDateTimeOffset(Item.LastWriteTime);

    /// <inheritdoc/>
    public override DateTimeOffset? CreationTime => SevenZipArchiveFormat.ToDateTimeOffset(Item.CreationTime);

    /// <inheritdoc/>
    public override DateTimeOffset? LastAccessTime => SevenZipArchiveFormat.ToDateTimeOffset(Item.LastAccessTime);

    /// <inheritdoc/>
    public override bool IsDirectory => Item.IsDirectory;

    /// <summary>Gets a value indicating whether the content of the entry is encrypted with AES-256.</summary>
    public override bool IsEncrypted
    {
        get
        {
            if (Item.Folder is { } folder)
                return folder.IsEncrypted;

            return IsNew && !Item.IsDirectory && Archive.WriterSettings is { IsEncrypted: true };
        }
    }

    /// <inheritdoc/>
    public override uint? Crc32 => Item.Crc;

    /// <summary>Gets the Windows attributes of the entry, without the bit with which 7-Zip marks the Unix permissions.</summary>
    public override FileAttributes? Attributes
    {
        get
        {
            if (Item.Attributes is not { } value)
                return null;

            return HasUnixMode(value) ? (FileAttributes)(value & 0x7FFF) : (FileAttributes)value;
        }
    }

    /// <summary>Gets the Unix permissions of the entry, which 7-Zip stores in the high 16 bits of the attributes.</summary>
    public override UnixFileMode? UnixFileMode
    {
        get
        {
            if (Item.Attributes is not { } value || !HasUnixMode(value))
                return null;

            return (UnixFileMode)((value >> 16) & 0xFFF);
        }
    }

    /// <summary>Gets the target of a symbolic link (an entry whose Unix mode is a link: its content is the target), or <see langword="null"/>.</summary>
    public override string? LinkTarget
    {
        get
        {
            if (!IsSymbolicLink)
                return null;

            if (!_linkTargetRead && !IsNew)
            {
                _linkTarget = ReadLinkTarget();
                _linkTargetRead = true;
            }

            return _linkTarget;
        }
    }

    private bool IsSymbolicLink => Item.Attributes is { } value && HasUnixMode(value) && ((value >> 16) & SevenZipConstants.UnixFileTypeMask) == SevenZipConstants.UnixSymbolicLink;

    private static bool HasUnixMode(uint attributes) => (attributes & SevenZipConstants.UnixExtensionAttribute) is not 0 && (attributes >> 16) is not 0;

    /// <summary>
    /// Opens the content of the entry: a read-only stream for an entry of the archive, or a write-only stream for a new entry
    /// (once). The read stream checks the CRC-32 of the content at its end.
    /// </summary>
    /// <returns>The content of the entry.</returns>
    /// <exception cref="InvalidOperationException">The entry is a directory, or another new entry is being written.</exception>
    /// <exception cref="IOException">The new entry is already opened.</exception>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The entry is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The entry is compressed with a method that is not supported.</exception>
    public override Stream Open()
    {
        EnsureNotDeleted();
        if (IsDirectory)
            throw new InvalidOperationException("The entry is a directory: it has no content.");

        if (!IsNew)
            return Archive.OpenRead(this);

        if (_opened)
            throw new IOException("A new entry can be opened only once.");

        var stream = Archive.OpenWrite(this);
        _opened = true;
        return stream;
    }

    /// <summary>Deletes the entry from the archive.</summary>
    /// <exception cref="NotSupportedException">The archive is not in <see cref="SevenZipArchiveMode.Update"/> mode.</exception>
    public void Delete()
    {
        EnsureNotDeleted();
        if (Archive.Mode is not SevenZipArchiveMode.Update)
            throw new NotSupportedException("Entries can be deleted only in an archive opened in Update mode.");

        IsDeleted = true;
        Archive.Remove(this);
    }

    internal bool IsDeleted { get; private set; }

    /// <summary>Sets the last time the entry was modified.</summary>
    /// <param name="value">The time, or <see langword="null"/> to not store it.</param>
    public void SetLastWriteTime(DateTimeOffset? value)
    {
        EnsureWritable();
        Item.LastWriteTime = SevenZipArchiveFormat.ToFileTime(value);
    }

    /// <summary>Sets the time the entry was created.</summary>
    /// <param name="value">The time, or <see langword="null"/> to not store it.</param>
    public void SetCreationTime(DateTimeOffset? value)
    {
        EnsureWritable();
        Item.CreationTime = SevenZipArchiveFormat.ToFileTime(value);
    }

    /// <summary>Sets the last time the entry was accessed.</summary>
    /// <param name="value">The time, or <see langword="null"/> to not store it.</param>
    public void SetLastAccessTime(DateTimeOffset? value)
    {
        EnsureWritable();
        Item.LastAccessTime = SevenZipArchiveFormat.ToFileTime(value);
    }

    /// <summary>Sets the Windows attributes of the entry (their low 16 bits). The Unix permissions are kept.</summary>
    /// <param name="value">The attributes, or <see langword="null"/> to not store them.</param>
    public void SetAttributes(FileAttributes? value)
    {
        EnsureWritable();
        var unix = Item.Attributes is { } current && HasUnixMode(current) ? current & 0xFFFF8000 : 0;
        var windows = value is { } attributes ? (uint)attributes & 0x7FFF : 0;
        if (Item.IsDirectory)
        {
            windows |= SevenZipConstants.DirectoryAttribute;
        }

        Item.Attributes = value is null && unix is 0 && !Item.IsDirectory ? null : windows | unix;
    }

    /// <summary>
    /// Sets the Unix permissions of the entry, which 7-Zip stores in the high 16 bits of the attributes with the type of the
    /// entry (a file, a directory or a symbolic link).
    /// </summary>
    /// <param name="value">The permissions, or <see langword="null"/> to not store them.</param>
    public void SetUnixFileMode(UnixFileMode? value)
    {
        EnsureWritable();
        var windows = Item.Attributes is { } current ? (HasUnixMode(current) ? current & 0x7FFF : current & 0xFFFF7FFF) : 0;
        if (value is not { } mode)
        {
            Item.Attributes = windows is 0 && !Item.IsDirectory ? null : windows;
            return;
        }

        uint type = IsSymbolicLink ? SevenZipConstants.UnixSymbolicLink : Item.IsDirectory ? SevenZipConstants.UnixDirectory : SevenZipConstants.UnixRegularFile;
        Item.Attributes = windows | SevenZipConstants.UnixExtensionAttribute | ((type | ((uint)mode & 0xFFF)) << 16);
    }

    /// <summary>Makes a new entry a symbolic link: its content is the target, and its Unix mode is a link.</summary>
    internal void SetLinkTarget(string target)
    {
        var windows = Item.Attributes is { } current && HasUnixMode(current) ? current & 0x7FFF : Item.Attributes ?? 0;
        var permissions = UnixFileMode is { } mode ? (uint)mode : 0x1FF;
        Item.Attributes = windows | SevenZipConstants.UnixExtensionAttribute | ((SevenZipConstants.UnixSymbolicLink | permissions) << 16);
        using (var stream = Open())
        {
            stream.Write(Encoding.UTF8.GetBytes(target));
        }

        _linkTarget = target;
        _linkTargetRead = true;
    }

    private string? ReadLinkTarget()
    {
        if (!Item.HasStream || Item.Size > SevenZipConstants.MaxLinkTargetLength)
            return null;

        using var stream = Open();
        var buffer = new byte[Item.Size];
        stream.ReadExactly(buffer);

        // Read to the end, so that the CRC is checked
        if (stream.ReadByte() >= 0)
            throw new InvalidDataException("The target of the symbolic link is longer than the entry.");

        return Encoding.UTF8.GetString(buffer);
    }

    private void EnsureWritable()
    {
        EnsureNotDeleted();
        if (Archive.Mode is SevenZipArchiveMode.Read)
            throw new NotSupportedException("The entries of an archive opened in Read mode cannot be modified.");

        Archive.MarkModified();
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
            throw new InvalidOperationException("The entry is deleted.");
    }
}
