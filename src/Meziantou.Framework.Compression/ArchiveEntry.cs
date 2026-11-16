using Meziantou.Framework.Compression.Internals;

namespace Meziantou.Framework.Compression;

/// <summary>An entry of an archive: a file, a directory, or a symbolic link.</summary>
public abstract class ArchiveEntry
{
    private protected ArchiveEntry()
    {
    }

    /// <summary>Gets the path of the entry in the archive, with <c>/</c> separators.</summary>
    public abstract string FullName { get; }

    /// <summary>Gets the file name of the entry: the last segment of <see cref="FullName"/>.</summary>
    public string Name
    {
        get
        {
            var fullName = FullName.TrimEnd('/');
            return fullName[(fullName.AsSpan().LastIndexOf('/') + 1)..];
        }
    }

    /// <summary>Gets the length of the content of the entry, or -1 when the archive does not tell it before the content is read.</summary>
    public abstract long Length { get; }

    /// <summary>
    /// Gets the length of the compressed content of the entry, or -1 when it is not known (the content of the entries of a
    /// solid block is compressed together).
    /// </summary>
    public abstract long CompressedLength { get; }

    /// <summary>Gets the last time the entry was modified, or <see langword="null"/> when the archive does not store it.</summary>
    public abstract DateTimeOffset? LastWriteTime { get; }

    /// <summary>Gets the time the entry was created, or <see langword="null"/> when the archive does not store it.</summary>
    public virtual DateTimeOffset? CreationTime => null;

    /// <summary>Gets the last time the entry was accessed, or <see langword="null"/> when the archive does not store it.</summary>
    public virtual DateTimeOffset? LastAccessTime => null;

    /// <summary>Gets a value indicating whether the entry is a directory.</summary>
    public abstract bool IsDirectory { get; }

    /// <summary>Gets the target of a symbolic link, or <see langword="null"/> when the entry is not a symbolic link.</summary>
    public virtual string? LinkTarget => null;

    /// <summary>
    /// Gets the path, in the archive, of the entry whose content this entry shares (a hard link of tar or RAR, a file copy of
    /// RAR), or <see langword="null"/>. Extracting the entry copies the file extracted for the target.
    /// </summary>
    public virtual string? HardLinkTarget => null;

    /// <summary>Gets a value indicating whether the content of the entry is encrypted.</summary>
    public virtual bool IsEncrypted => false;

    /// <summary>Gets the CRC-32 of the content of the entry, or <see langword="null"/> when the archive does not store it.</summary>
    public virtual uint? Crc32 => null;

    /// <summary>Gets the Windows attributes of the entry, or <see langword="null"/> when the archive does not store them.</summary>
    public virtual FileAttributes? Attributes => null;

    /// <summary>Gets the Unix permissions of the entry, or <see langword="null"/> when the archive does not store them.</summary>
    public virtual UnixFileMode? UnixFileMode => null;

    /// <summary>
    /// Opens the content of the entry. The stream checks the CRC or the hash the archive stores, and throws an
    /// <see cref="InvalidDataException"/> at its end when they do not match.
    /// </summary>
    /// <returns>A read-only stream of the content of the entry.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The entry is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The entry is compressed or encrypted with a method that is not supported.</exception>
    public abstract Stream Open();

    /// <summary>Opens the content of the entry.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A read-only stream of the content of the entry.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The entry is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The entry is compressed or encrypted with a method that is not supported.</exception>
    public virtual Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Open());
    }

    /// <summary>Extracts the content of the entry to a file, and sets its last write time.</summary>
    /// <param name="destinationFileName">The path of the file to create.</param>
    /// <param name="overwrite"><see langword="true"/> to replace an existing file; <see langword="false"/> to throw an <see cref="IOException"/> when the file exists.</param>
    public void ExtractToFile(string destinationFileName, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(destinationFileName);
        ArchiveExtractor.ExtractFile(this, Path.GetFullPath(destinationFileName), overwrite);
    }

    /// <summary>Extracts the content of the entry to a file, and sets its last write time.</summary>
    /// <param name="destinationFileName">The path of the file to create.</param>
    /// <param name="overwrite"><see langword="true"/> to replace an existing file; <see langword="false"/> to throw an <see cref="IOException"/> when the file exists.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public Task ExtractToFileAsync(string destinationFileName, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationFileName);
        return ArchiveExtractor.ExtractFileAsync(this, Path.GetFullPath(destinationFileName), overwrite, cancellationToken);
    }

    /// <inheritdoc/>
    public override string ToString() => FullName;
}
