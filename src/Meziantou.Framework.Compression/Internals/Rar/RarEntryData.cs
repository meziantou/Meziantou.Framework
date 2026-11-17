namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>An entry of a RAR archive: the header of its first part, and the parts of its data in the volumes.</summary>
internal sealed class RarEntryData
{
    public RarEntryData(RarFileHeader header)
    {
        Header = header;
        Crc32 = header.Crc32;
        Blake2 = header.Blake2;
        IsSplitAfter = header.SplitAfter;
    }

    /// <summary>Gets the header of the first part.</summary>
    public RarFileHeader Header { get; }

    public List<RarDataPart> Parts { get; } = [];

    /// <summary>Gets or sets the CRC-32 of the last part read, which is the CRC-32 of the content for the last part.</summary>
    public uint? Crc32 { get; set; }

    /// <summary>Gets or sets the BLAKE2sp hash of the last part read.</summary>
    public byte[]? Blake2 { get; set; }

    /// <summary>Gets or sets a value indicating whether the last part read continues in the next volume.</summary>
    public bool IsSplitAfter { get; set; }

    public long CompressedLength
    {
        get
        {
            long total = 0;
            foreach (var part in Parts)
            {
                total += part.Length;
            }

            return total;
        }
    }

    /// <summary>Gets a value indicating whether the content of the entry is compressed, and so continues the solid stream of the archive.</summary>
    public bool IsCompressed => !Header.IsDirectory && Header.Method is not 0 && Header.RedirectionType is RarRedirectionType.None;

    /// <summary>Gets a value indicating whether the entry is a symbolic link of RAR 1.5-4.x, whose target is its content.</summary>
    public bool IsRar4Symlink => Header.Format is RarFormatVersion.Rar4 && Header.HostOperatingSystem is RarHostOperatingSystem.Unix && (Header.Attributes & 0xF000) == 0xA000;

    /// <summary>Records the header of the next part of the entry.</summary>
    public void AddPart(RarFileHeader header)
    {
        Crc32 = header.Crc32;
        Blake2 = header.Blake2;
        IsSplitAfter = header.SplitAfter;
    }
}
