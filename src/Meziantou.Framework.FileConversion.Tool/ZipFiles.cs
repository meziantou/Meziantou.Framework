using System.IO.Compression;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>Reads the ZIP archives of EPUB books with limits on the size of what they contain.</summary>
internal static class ZipFiles
{
    /// <summary>The maximum number of entries of an archive.</summary>
    public const int MaxEntryCount = 100_000;

    /// <summary>The maximum decompressed size of an entry: 256 MiB.</summary>
    public const int MaxEntryLength = 256 * 1024 * 1024;

    public static ZipArchive OpenRead(FullPath path)
    {
        var stream = File.OpenRead(path);
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Reads the decompressed content of an entry.</summary>
    /// <exception cref="InvalidDataException">The entry is larger than <paramref name="maxLength"/>, or its data is not valid.</exception>
    public static byte[] ReadEntry(ZipArchiveEntry entry, int maxLength = MaxEntryLength)
    {
        using var stream = entry.Open();
        return ArchiveFiles.ReadBounded(stream, entry.FullName, entry.Length, maxLength);
    }
}
