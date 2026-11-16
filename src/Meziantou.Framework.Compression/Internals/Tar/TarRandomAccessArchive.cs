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

                if (entry is null)
                    break;

                // TarReader leaves a seekable stream at the start of the content of the entry it returns
                var dataOffset = stream.Position;
                var adapter = TarEntryAdapter.Create(entry, e => TarEntryAdapter.Window(_stream, e));
                if (adapter is not null)
                {
                    adapter.DataOffset = dataOffset;
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
