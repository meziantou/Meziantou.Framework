namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The header of a file or of a service (comment, ...).</summary>
internal sealed class RarFileHeader : RarHeader
{
    public RarFormatVersion Format { get; init; }

    public string Name { get; set; } = "";

    public long UnpackedSize { get; set; }

    public bool SplitBefore { get; set; }

    public bool SplitAfter { get; set; }

    public bool IsDirectory { get; set; }

    public bool IsSolid { get; set; }

    public RarHostOperatingSystem HostOperatingSystem { get; set; }

    public uint Attributes { get; set; }

    public DateTimeOffset? LastWriteTime { get; set; }

    public DateTimeOffset? CreationTime { get; set; }

    public DateTimeOffset? LastAccessTime { get; set; }

    public uint? Crc32 { get; set; }

    public byte[]? Blake2 { get; set; }

    /// <summary>Gets or sets the version of the compression: the UNP_VER of RAR 1.5-4.x (15, 20, 26, 29, 36), 50 or 70 for RAR5.</summary>
    public int UnpackVersion { get; set; }

    public int Method { get; set; }

    public long DictionarySize { get; set; }

    public bool IsEncrypted { get; set; }

    public RarEncryption? Encryption { get; set; }

    public RarRedirectionType RedirectionType { get; set; }

    public string? RedirectionTarget { get; set; }

    /// <summary>Gets or sets the version number of the file (-ver), or 0.</summary>
    public long FileVersion { get; set; }
}
