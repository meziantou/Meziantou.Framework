using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression;

/// <summary>The format of an archive or a compressed stream, as detected by <see cref="ArchiveFile.DetectFormat(Stream)"/>.</summary>
/// <param name="Format">The format of the archive, or <see cref="ArchiveFormat.Unknown"/> when the data are not an archive (a compressed file that is not a tar archive, or unknown data).</param>
/// <param name="CompressionFormat">The compression around the archive or the data (the gzip of a .tar.gz).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ArchiveFormatInfo(ArchiveFormat Format, CompressionFormat CompressionFormat)
{
    /// <summary>Gets a value indicating whether <see cref="ArchiveFile.Open(Stream, ArchiveReaderOptions?, bool)"/> can open the data.</summary>
    public bool SupportsRandomAccess => Format is not ArchiveFormat.Unknown;

    /// <summary>Gets a value indicating whether <see cref="ArchiveReader.Create(Stream, ArchiveReaderOptions?, bool)"/> can read the data (every archive format but 7z, which needs to seek).</summary>
    public bool SupportsForwardOnlyReading => Format is ArchiveFormat.Zip or ArchiveFormat.Rar or ArchiveFormat.Tar;
}
