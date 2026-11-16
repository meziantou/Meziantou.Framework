using Meziantou.Framework.Compression;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>Opens the archives (zip, 7z, RAR, tar...) and reads their entries with limits on the size of what they contain.</summary>
internal static class ArchiveFiles
{
    /// <summary>Opens an archive, whatever its format. The other volumes of a multi-volume archive are found next to it.</summary>
    /// <exception cref="InvalidDataException">The file is not an archive of a supported format, or it is corrupted.</exception>
    public static Archive Open(FullPath path, ConversionSettings settings)
    {
        return ArchiveFile.Open(path, new ArchiveReaderOptions { Password = settings.ArchivePassword });
    }

    /// <summary>Reads the decompressed content of an entry.</summary>
    /// <exception cref="InvalidDataException">The entry is larger than <paramref name="maxLength"/>, or its data is not valid.</exception>
    public static byte[] ReadEntry(ArchiveEntry entry, int maxLength = ZipFiles.MaxEntryLength)
    {
        using var stream = entry.Open();
        return ReadBounded(stream, entry.FullName, entry.Length, maxLength);
    }

    /// <summary>Reads a stream up to a limit: the length an archive declares is not trusted.</summary>
    /// <exception cref="InvalidDataException">The stream is longer than <paramref name="maxLength"/>.</exception>
    public static byte[] ReadBounded(Stream stream, string name, long declaredLength, int maxLength)
    {
        using var buffer = new MemoryStream(capacity: (int)Math.Clamp(declaredLength, 0, 1024 * 1024));
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > maxLength)
                throw new InvalidDataException($"The entry '{name}' of the archive is larger than {maxLength.ToString(CultureInfo.InvariantCulture)} bytes");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Writes a file through a new temporary file next to it, so that a failed conversion does not leave a partial file.
    /// The file replaces the output path: an existing link at this path is replaced, not followed, so the file it points
    /// to is not changed.
    /// </summary>
    public static void WriteAtomically(FullPath path, Action<Stream> write)
    {
        // A unique name, created exclusively: an existing file is never truncated, and parallel conversions do not collide
        FullPath temporaryPath;
        FileStream stream;
        while (true)
        {
            temporaryPath = path.Parent / ("." + path.Name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException) when (File.Exists(temporaryPath))
            {
            }
        }

        var moved = false;
        try
        {
            using (stream)
            {
                write(stream);
            }

            File.Move(temporaryPath, path, overwrite: true);
            moved = true;
        }
        finally
        {
            // Only the file this call created
            if (!moved)
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
