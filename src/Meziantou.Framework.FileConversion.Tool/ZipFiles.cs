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
    /// <exception cref="InvalidDataException">The entry is larger than <paramref name="maxLength"/>, its data is not valid, or it is encrypted (the container of an EPUB book cannot be encrypted).</exception>
    public static byte[] ReadEntry(ZipArchiveEntry entry, int maxLength = MaxEntryLength)
    {
        Stream stream;
        try
        {
            stream = entry.Open();
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException($"The entry '{entry.FullName}' of the book is encrypted or uses a compression method that is not supported", ex);
        }

        using (stream)
        {
            return ArchiveFiles.ReadBounded(stream, entry.FullName, entry.Length, maxLength);
        }
    }
}
