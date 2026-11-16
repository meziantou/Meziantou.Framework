using System.IO.Compression;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>The resolved settings of a new zip entry: the options of the archive with the overrides of the entry.</summary>
internal sealed class ZipEntrySettings
{
    public required string FullName { get; init; }

    public required bool IsDirectory { get; init; }

    public ZipCompressionMethod Method { get; set; }

    public CompressionLevel Level { get; set; }

    /// <summary>Gets or sets the password, or <see langword="null"/> when the entry is not encrypted.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the encryption: <see cref="ZipEncryption.None"/> when the entry is not encrypted.</summary>
    public ZipEncryption Encryption { get; set; }

    public DateTimeOffset LastWriteTime { get; set; }

    public ushort VersionMadeBy { get; set; }

    public uint ExternalAttributes { get; set; }

    public string Comment { get; set; } = "";

    /// <summary>Gets or sets the length of the content when it is known before it is written, or -1.</summary>
    public long KnownLength { get; set; } = -1;
}
