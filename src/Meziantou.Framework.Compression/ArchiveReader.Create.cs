using Meziantou.Framework.Compression.Internals;
using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.Tar;

namespace Meziantou.Framework.Compression;

public abstract partial class ArchiveReader
{
    /// <summary>
    /// Creates a forward-only reader of an archive, whose format (zip, RAR, tar, and tar compressed with gzip, bzip2, XZ, LZMA,
    /// lzip, Zstandard or Unix compress) is detected from its first bytes. The stream does not need to be seekable.
    /// </summary>
    /// <param name="stream">The stream of the archive, positioned at its start.</param>
    /// <param name="options">The options to read the archive.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the stream open when the reader is disposed.</param>
    /// <returns>The reader, before the first entry: call <see cref="MoveToNextEntry"/>.</returns>
    /// <exception cref="InvalidDataException">The data are not an archive of a supported format.</exception>
    /// <exception cref="NotSupportedException">The archive is a 7z archive, which cannot be read forward only: use <see cref="ArchiveFile.Open(Stream, ArchiveReaderOptions?, bool)"/>.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The archive or the reader owns the stream, and the method disposes it when it throws")]
    public static ArchiveReader Create(Stream stream, ArchiveReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[FormatDetector.PeekLength];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        // A seekable stream is rewound and given to the reader as is; a forward-only stream is replayed from the bytes already read
        Stream source;
        bool readerLeaveOpen;
        if (stream.CanSeek)
        {
            stream.Position -= read;
            source = stream;
            readerLeaveOpen = leaveOpen;
        }
        else
        {
            source = new PrefixedStream(header, read, stream, leaveOpen);
            readerLeaveOpen = false;
        }

        var disposeSource = !readerLeaveOpen;
        try
        {
            var (format, compression) = FormatDetector.DetectOuter(header.AsSpan(0, read));
            ArchiveReader reader;
            switch (format, compression)
            {
                case (ArchiveFormat.Zip, _):
                    reader = ZipArchive.CreateForwardReader(source, options, readerLeaveOpen);
                    break;
                case (ArchiveFormat.Rar, _):
                    reader = RarArchive.CreateForwardReader(source, options, readerLeaveOpen);
                    break;
                case (ArchiveFormat.SevenZip, _):
                    throw new NotSupportedException("A 7z archive cannot be read forward only: open it from a seekable stream with ArchiveFile.Open or SevenZipArchive.");
                case (ArchiveFormat.Tar, _):
                    reader = new TarForwardReader(source, readerLeaveOpen, CompressionFormat.None);
                    break;
                case (_, not CompressionFormat.None):
                    reader = CreateCompressedTarReader(source, compression, readerLeaveOpen, options?.MaxMemoryUsage ?? new ArchiveReaderOptions().MaxMemoryUsage);
                    break;
                default:
                    throw new InvalidDataException("The data are not an archive of a supported format.");
            }

            disposeSource = false;
            return reader;
        }
        finally
        {
            if (disposeSource)
            {
                source.Dispose();
            }
        }
    }

    /// <summary>Creates a forward-only reader of an archive file, whose format is detected from its first bytes.</summary>
    /// <param name="path">The path of the archive.</param>
    /// <param name="options">The options to read the archive.</param>
    /// <returns>The reader, before the first entry: call <see cref="MoveToNextEntry"/>.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The archive or the reader owns the stream, and the method disposes it when it throws")]
    public static ArchiveReader Create(string path, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Create(File.OpenRead(path), options, leaveOpen: false);
    }

    private static TarForwardReader CreateCompressedTarReader(Stream source, CompressionFormat compression, bool leaveOpen, long maxMemoryUsage)
    {
        var decompressed = FormatDetector.CreateDecompressionStream(compression, source, leaveOpen, maxMemoryUsage);
        var block = new byte[512];
        int read;
        try
        {
            read = decompressed.ReadAtLeast(block, block.Length, throwOnEndOfStream: false);
        }
        catch
        {
            decompressed.Dispose();
            throw;
        }

        if (!FormatDetector.IsTarHeader(block.AsSpan(0, read)))
        {
            decompressed.Dispose();
            throw new InvalidDataException($"The data are a {compression} stream that does not contain a tar archive. Decompress it with a stream, such as XzStream or BZip2Stream.");
        }

        return new TarForwardReader(new PrefixedStream(block, read, decompressed, leaveOpen: false), leaveOpen: false, compression);
    }
}
