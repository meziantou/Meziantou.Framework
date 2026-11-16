using Meziantou.Framework.Compression.Internals;
using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.Tar;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Opens an archive whatever its format (zip, 7z, RAR, tar, and tar compressed with gzip, bzip2, XZ, LZMA, lzip, Zstandard
/// or Unix compress), detected from its content.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/ArchiveFileTests.cs" ref:region="ArchiveFile_Open" ref:dedent="true">
/// // The format is detected from the content: zip, 7z, RAR, or tar compressed with gzip, bzip2, XZ, ...
/// using (var archive = ArchiveFile.Open(path))
/// {
///     foreach (var entry in archive.Entries)
///     {
///         if (entry.IsDirectory)
///             continue;
///
///         using var stream = entry.Open();
///         using var reader = new StreamReader(stream);
///         var text = reader.ReadToEnd();
///         Assert.NotNull(text);
///     }
///
///     var hello = archive.GetEntry("hello.txt");
///     Assert.Equal(16, hello?.Length);
/// }
///
/// ArchiveFile.ExtractToDirectory(path, destination);
/// </code>
/// </example>
public static class ArchiveFile
{
    /// <summary>Detects the format of the data of a seekable stream, from its current position, and restores the position.</summary>
    /// <param name="stream">A seekable stream.</param>
    /// <returns>The format of the archive and its compression.</returns>
    public static ArchiveFormatInfo DetectFormat(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
            throw new ArgumentException("The stream must be seekable. Use ArchiveReader.Create to read a stream that is not.", nameof(stream));

        var position = stream.Position;
        try
        {
            var header = new byte[FormatDetector.PeekLength];
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            stream.Position = position;
            var (format, compression) = FormatDetector.DetectOuter(header.AsSpan(0, read));
            if (compression is not CompressionFormat.None)
                return new(FormatDetector.IsCompressedTar(compression, stream) ? ArchiveFormat.Tar : ArchiveFormat.Unknown, compression);

            if (format is ArchiveFormat.Unknown && read >= 2 && header[0] == 'M' && header[1] == 'Z')
                return new(FormatDetector.DetectSelfExtracting(stream), CompressionFormat.None);

            return new(format, CompressionFormat.None);
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>Detects the format of a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The format of the archive and its compression.</returns>
    public static ArchiveFormatInfo DetectFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return DetectFormat(stream);
    }

    /// <summary>
    /// Opens an archive file for random access. The other volumes of a multi-volume archive are found next to it (see
    /// <see cref="ArchiveReaderOptions.VolumeResolver"/>). A compressed tar archive is decompressed to a temporary file,
    /// deleted when the archive is disposed.
    /// </summary>
    /// <param name="path">The path of the archive, or of its first volume.</param>
    /// <param name="options">The options to read the archive.</param>
    /// <returns>The archive.</returns>
    /// <exception cref="InvalidDataException">The file is not an archive of a supported format, or it is corrupted.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The archive or the reader owns the stream, and the method disposes it when it throws")]
    public static Archive Open(string path, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = DetectFormat(path);
        return info.Format switch
        {
            ArchiveFormat.Zip => ZipFile.OpenRead(path, options),
            ArchiveFormat.SevenZip => SevenZipFile.OpenRead(path, options),
            ArchiveFormat.Rar => RarFile.OpenRead(path, options),
            _ => Open(File.OpenRead(path), options, leaveOpen: false),
        };
    }

    /// <summary>
    /// Opens an archive for random access from a seekable stream. A compressed tar archive is decompressed to a temporary
    /// file, deleted when the archive is disposed.
    /// </summary>
    /// <param name="stream">A seekable stream, positioned at the start of the archive.</param>
    /// <param name="options">The options to read the archive.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the stream open when the archive is disposed.</param>
    /// <returns>The archive.</returns>
    /// <exception cref="InvalidDataException">The data are not an archive of a supported format, or they are corrupted.</exception>
    public static Archive Open(Stream stream, ArchiveReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var disposeStream = !leaveOpen;
        try
        {
            var info = DetectFormat(stream);
            Archive archive;
            switch (info)
            {
                case { Format: ArchiveFormat.Zip }:
                    archive = new ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen, options, compressionOptions: null);
                    break;
                case { Format: ArchiveFormat.SevenZip }:
                    archive = new SevenZipArchive(stream, SevenZipArchiveMode.Read, leaveOpen, options, compressionOptions: null);
                    break;
                case { Format: ArchiveFormat.Rar }:
                    archive = new RarArchive(stream, options, leaveOpen);
                    break;
                case { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.None }:
                    archive = new TarRandomAccessArchive(stream, leaveOpen, CompressionFormat.None);
                    break;
                case { Format: ArchiveFormat.Tar }:
                    archive = OpenCompressedTar(stream, info.CompressionFormat, leaveOpen, options?.MaxMemoryUsage ?? new ArchiveReaderOptions().MaxMemoryUsage);
                    break;
                case { CompressionFormat: not CompressionFormat.None }:
                    throw new InvalidDataException($"The data are a {info.CompressionFormat} stream that does not contain a tar archive. Decompress it with a stream, such as XzStream or BZip2Stream.");
                default:
                    throw new InvalidDataException("The data are not an archive of a supported format.");
            }

            disposeStream = false;
            return archive;
        }
        finally
        {
            if (disposeStream)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>Extracts an archive file of any supported format to a directory.</summary>
    /// <param name="sourceArchiveFileName">The path of the archive, or of its first volume.</param>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="options">The options to read the archive.</param>
    public static void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName, bool overwriteFiles = false, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceArchiveFileName);
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        using var archive = Open(sourceArchiveFileName, options);
        archive.ExtractToDirectory(destinationDirectoryName, overwriteFiles);
    }

    /// <summary>Extracts an archive file of any supported format to a directory.</summary>
    /// <param name="sourceArchiveFileName">The path of the archive, or of its first volume.</param>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="options">The options to read the archive.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when every entry is extracted.</returns>
    public static async Task ExtractToDirectoryAsync(string sourceArchiveFileName, string destinationDirectoryName, bool overwriteFiles = false, ArchiveReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceArchiveFileName);
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        var archive = Open(sourceArchiveFileName, options);
        await using (archive.ConfigureAwait(false))
        {
            await archive.ExtractToDirectoryAsync(destinationDirectoryName, overwriteFiles, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TarRandomAccessArchive OpenCompressedTar(Stream stream, CompressionFormat compression, bool leaveOpen, long maxMemoryUsage)
    {
        var temporary = TemporaryFileStream.Create();
        try
        {
            using (var decompressed = FormatDetector.CreateDecompressionStream(compression, stream, leaveOpen, maxMemoryUsage))
            {
                decompressed.CopyTo(temporary);
            }

            temporary.Position = 0;
            return new TarRandomAccessArchive(temporary, leaveOpen: false, compression);
        }
        catch
        {
            temporary.Dispose();
            throw;
        }
    }
}
