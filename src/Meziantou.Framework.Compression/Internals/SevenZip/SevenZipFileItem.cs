namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>A file of the archive, as stored in the headers.</summary>
internal sealed class SevenZipFileItem
{
    public string Name { get; set; } = "";

    public bool HasStream { get; set; }

    public bool IsDirectory { get; set; }

    public bool IsAnti { get; set; }

    public long Size { get; set; }

    public uint? Crc { get; set; }

    public long? CreationTime { get; set; }

    public long? LastAccessTime { get; set; }

    public long? LastWriteTime { get; set; }

    public uint? Attributes { get; set; }

    /// <summary>Gets or sets the folder of the content, or -1 for a file without content.</summary>
    public int FolderIndex { get; set; } = -1;

    /// <summary>Gets or sets the offset of the content in the output of the folder.</summary>
    public long OffsetInFolder { get; set; }

    /// <summary>Gets or sets the folder of the content (not stored: it locates the folder of the file).</summary>
    public SevenZipFolder? Folder { get; set; }

    public SevenZipFileItem Clone() => (SevenZipFileItem)MemberwiseClone();
}
