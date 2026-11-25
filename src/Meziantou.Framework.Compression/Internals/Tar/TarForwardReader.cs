using System.Formats.Tar;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Tar;

/// <summary>Reads a tar archive forward only, from a stream that may be compressed and not seekable.</summary>
internal sealed class TarForwardReader : ArchiveReader
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly TarReader _reader;
    private TarEntry? _current;
    private int _generation;

    public TarForwardReader(Stream stream, bool leaveOpen, CompressionFormat compressionFormat)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        CompressionFormat = compressionFormat;
        _reader = new TarReader(stream, leaveOpen: true);
    }

    public override ArchiveFormat Format => ArchiveFormat.Tar;

    public override CompressionFormat CompressionFormat { get; }

    private protected override ArchiveEntry? ReadNextEntry()
    {
        while (true)
        {
            _generation++;
            try
            {
                _current = _reader.GetNextEntry(copyData: false);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("The tar archive is corrupted.", ex);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("The tar archive is truncated.", ex);
            }

            if (_current is null)
                return null;

            var generation = _generation;
            var current = _current;
            var adapter = TarEntryAdapter.Create(current, _ => Open(current, generation));
            if (adapter is not null)
                return adapter;
        }
    }

    private protected override async ValueTask<ArchiveEntry?> ReadNextEntryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            _generation++;
            try
            {
                _current = await _reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("The tar archive is corrupted.", ex);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("The tar archive is truncated.", ex);
            }

            if (_current is null)
                return null;

            var generation = _generation;
            var current = _current;
            var adapter = TarEntryAdapter.Create(current, _ => Open(current, generation));
            if (adapter is not null)
                return adapter;
        }
    }

    private Stream Open(TarEntry entry, int generation)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (generation != _generation)
            throw new InvalidOperationException("The reader moved past this entry: its content can no longer be read.");

        if (entry.DataStream is not { } data)
            return TarEntryAdapter.EmptyStream();

        // The content stream of TarReader ends with the archive stream, without an error, when the archive is truncated
        return new ChecksumVerifyingStream(new NonClosingStream(data), entry.Length, expectedCrc: null, encrypted: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
