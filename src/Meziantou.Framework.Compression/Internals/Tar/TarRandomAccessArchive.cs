using System.Formats.Tar;

namespace Meziantou.Framework.Compression.Internals.Tar;

/// <summary>A tar archive in a seekable stream: the headers are read when it is opened, and the content of each entry is a window of the stream.</summary>
internal sealed class TarRandomAccessArchive : Archive
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly List<ArchiveEntry> _entries = [];

    public TarRandomAccessArchive(Stream stream, bool leaveOpen, CompressionFormat compressionFormat)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        CompressionFormat = compressionFormat;
        try
        {
            using var reader = new TarReader(stream, leaveOpen: true);
            while (true)
            {
                TarEntry? entry;
                try
                {
                    entry = reader.GetNextEntry(copyData: false);
                }
                catch (FormatException ex)
                {
                    throw new InvalidDataException("The tar archive is corrupted.", ex);
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    // An extended attributes header larger than allowed, or a time (mtime, atime, ctime) out of the range of a date (fuzz regressions)
                    throw new InvalidDataException("The tar archive is corrupted.", ex);
                }
                catch (OverflowException ex)
                {
                    throw new InvalidDataException("The tar archive is corrupted.", ex);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new InvalidDataException("The tar archive is corrupted.", ex);
                }

                if (entry is null)
                    break;

                var adapter = TarEntryAdapter.Create(entry, e => TarEntryAdapter.Window(_stream, e));
                if (adapter is not null)
                {
                    adapter.DataOffset = GetDataOffset(stream, entry);
                    _entries.Add(adapter);
                }
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The tar archive is truncated.", ex);
        }
    }

    public override ArchiveFormat Format => ArchiveFormat.Tar;

    /// <summary>
    /// Gets the offset of the content of an entry. On a seekable stream, the content stream of <see cref="TarReader"/> reads
    /// the archive stream at its own offset, which it does not expose: reading its first byte moves the archive stream
    /// right after it.
    /// </summary>
    private static long GetDataOffset(Stream stream, TarEntry entry)
    {
        if (entry.DataStream is not { Length: > 0 } data)
            return -1;

        var position = stream.Position;
        data.Position = 0;
        if (data.ReadByte() < 0)
            throw new InvalidDataException("The tar archive is truncated.");

        var offset = stream.Position - 1;
        if (data.Length > stream.Length - offset)
            throw new InvalidDataException("The tar archive is truncated.");

        data.Position = 0;
        stream.Position = position;
        return offset;
    }

    public CompressionFormat CompressionFormat { get; }

    private protected override IReadOnlyList<ArchiveEntry> GetEntries() => _entries;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _stream.Dispose();
        }

        base.Dispose(disposing);
    }
}
