using System.IO.Compression;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Options of a new entry of a <see cref="ZipArchive"/>. The properties that are <see langword="null"/> use the value of the
/// <see cref="ZipCompressionOptions"/> of the archive.
/// </summary>
public sealed class ZipEntryOptions
{
    /// <summary>Gets or sets the compression method of the entry, or <see langword="null"/> for <see cref="ZipCompressionOptions.CompressionMethod"/>.</summary>
    public ZipCompressionMethod? CompressionMethod { get; set; }

    /// <summary>Gets or sets the compression level of the entry, or <see langword="null"/> for <see cref="ZipCompressionOptions.CompressionLevel"/>.</summary>
    public CompressionLevel? CompressionLevel { get; set; }

    /// <summary>
    /// Gets or sets the password of the entry, or <see langword="null"/> for <see cref="ZipCompressionOptions.Password"/>.
    /// An empty string writes the entry without encryption, even when the archive has a password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the encryption of the entry, or <see langword="null"/> for <see cref="ZipCompressionOptions.Encryption"/>.
    /// It applies only when the entry has a password; <see cref="ZipEncryption.None"/> writes the entry without encryption.
    /// </summary>
    public ZipEncryption? Encryption { get; set; }

    /// <summary>
    /// Gets or sets the last time the entry was modified, or <see langword="null"/> for the current time. It is written as
    /// an MS-DOS time, in the local time zone and with a 2-second precision (as <see cref="System.IO.Compression.ZipArchive"/>
    /// does), and as a Unix time in UTC, with a 1-second precision, in the extended timestamp extra field (0x5455).
    /// </summary>
    public DateTimeOffset? LastWriteTime { get; set; }

    /// <summary>
    /// Gets or sets the Unix permissions of the entry, or <see langword="null"/>. When it is set, the entry is marked as
    /// created on Unix, and the high 16 bits of its external attributes are its mode: the file type and the permissions.
    /// </summary>
    public UnixFileMode? UnixFileMode { get; set; }

    /// <summary>
    /// Gets or sets the MS-DOS attributes of the entry (the low 16 bits of the external attributes), or <see langword="null"/>
    /// for none, or <see cref="FileAttributes.Directory"/> for a directory.
    /// </summary>
    public FileAttributes? Attributes { get; set; }

    /// <summary>Gets or sets the comment of the entry, or <see langword="null"/>. It is written as UTF-8 when it is not ASCII.</summary>
    public string? Comment { get; set; }

    internal ZipEntryOptions Clone() => (ZipEntryOptions)MemberwiseClone();
}
