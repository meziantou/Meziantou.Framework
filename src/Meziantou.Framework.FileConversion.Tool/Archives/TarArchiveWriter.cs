using System.Formats.Tar;
using System.IO.Compression;
using Meziantou.Framework.Compression;

namespace Meziantou.Framework.FileConversionTool.Archives;

internal sealed class TarArchiveWriter : ArchiveWriter
{
    // The content of an entry whose stream is not seekable is buffered: in memory up to this length, in a temporary file above
    private const long MaxMemoryBufferLength = 16 * 1024 * 1024;

    private readonly Stream? _compressionStream;
    private readonly TarWriter _writer;

    public TarArchiveWriter(Stream stream, ArchiveType archiveType, ConversionSettings settings)
    {
        var level = settings.ArchiveCompressionLevel ?? CompressionLevel.Optimal;
        _compressionStream = archiveType switch
        {
            ArchiveType.Tar => null,
            ArchiveType.TarGzip => new GZipStream(stream, level, leaveOpen: true),
            ArchiveType.TarBzip2 => new BZip2Stream(stream, level, leaveOpen: true),
            ArchiveType.TarXz => new XzStream(stream, level, leaveOpen: true),
            ArchiveType.TarLzma => new LzmaStream(stream, level, leaveOpen: true),
            ArchiveType.TarLzip => new LZipStream(stream, level, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(archiveType), archiveType, "The archive format cannot be written"),
        };

        _writer = new TarWriter(_compressionStream ?? stream, TarEntryFormat.Pax, leaveOpen: true);
    }

    public override void AddDirectory(string name, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode)
    {
        var entry = CreateEntry(TarEntryType.Directory, name + "/", lastWriteTime);
        if (unixFileMode is { } mode)
        {
            entry.Mode = mode;
        }

        _writer.WriteEntry(entry);
    }

    public override void AddFile(string name, Stream content, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode)
    {
        var entry = CreateEntry(TarEntryType.RegularFile, name, lastWriteTime);
        if (unixFileMode is { } mode)
        {
            entry.Mode = mode;
        }

        // The header of an entry holds the length of its content, so the content must be seekable
        Stream? buffer = null;
        try
        {
            if (!content.CanSeek)
            {
                buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose);
                content.CopyTo(buffer);
                buffer.Position = 0;
                if (buffer.Length <= MaxMemoryBufferLength)
                {
                    var memory = new MemoryStream(capacity: (int)buffer.Length);
                    buffer.CopyTo(memory);
                    memory.Position = 0;
                    buffer.Dispose();
                    buffer = memory;
                }
            }

            entry.DataStream = buffer ?? content;
            _writer.WriteEntry(entry);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public override bool AddSymbolicLink(string name, string target, DateTimeOffset? lastWriteTime)
    {
        var entry = CreateEntry(TarEntryType.SymbolicLink, name, lastWriteTime);
        entry.LinkName = target;
        _writer.WriteEntry(entry);
        return true;
    }

    public override bool AddHardLink(string name, string target, DateTimeOffset? lastWriteTime)
    {
        var entry = CreateEntry(TarEntryType.HardLink, name, lastWriteTime);
        entry.LinkName = target;
        _writer.WriteEntry(entry);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writer.Dispose();
            _compressionStream?.Dispose();
        }
    }

    private static PaxTarEntry CreateEntry(TarEntryType entryType, string name, DateTimeOffset? lastWriteTime)
    {
        var entry = new PaxTarEntry(entryType, name);
        if (lastWriteTime is { } time)
        {
            entry.ModificationTime = time;
        }

        return entry;
    }
}
