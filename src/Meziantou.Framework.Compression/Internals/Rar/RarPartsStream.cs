using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The compressed data of an entry of a seekable RAR archive: its parts in the volumes, read one after the other.</summary>
internal sealed class RarPartsStream : ReadOnlyStreamBase
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The archive owns the volumes")]
    private readonly IReadOnlyList<Stream> _volumes;
    private readonly IReadOnlyList<RarDataPart> _parts;
    private int _part = -1;
    private BoundedReadStream? _current;

    public RarPartsStream(IReadOnlyList<Stream> volumes, IReadOnlyList<RarDataPart> parts)
    {
        _volumes = volumes;
        _parts = parts;
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            if (_current is not null)
            {
                var read = _current.Read(buffer);
                if (read > 0)
                    return read;

                if (_current.Remaining > 0)
                    throw new InvalidDataException("The RAR archive is truncated.");
            }

            if (_part + 1 >= _parts.Count)
                return 0;

            _part++;
            var part = _parts[_part];
            _current?.Dispose();
            _current = new BoundedReadStream(_volumes[part.Volume], part.Offset, part.Length);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _current?.Dispose();
        }

        base.Dispose(disposing);
    }
}
