namespace Meziantou.Framework.Compression;

/// <summary>The format of an archive.</summary>
public enum ArchiveFormat
{
    /// <summary>The data are not an archive of a supported format.</summary>
    Unknown,

    /// <summary>A zip archive (.zip).</summary>
    Zip,

    /// <summary>A 7z archive (.7z).</summary>
    SevenZip,

    /// <summary>A RAR archive (.rar), RAR 2.9/4.x or RAR5.</summary>
    Rar,

    /// <summary>A tar archive (.tar), possibly compressed (see <see cref="CompressionFormat"/>).</summary>
    Tar,
}
