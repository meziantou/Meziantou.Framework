using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Reads at most a given number of bytes of a stream, and disposes it.</summary>
internal sealed class SevenZipLimitedStream : ReadOnlyStreamBase
{
    private readonly Stream _inner;
    private long _remaining;

    public SevenZipLimitedStream(Stream inner, long length)
    {
        _inner = inner;
        _remaining = length;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0 || buffer.IsEmpty)
            return 0;

        if (buffer.Length > _remaining)
        {
            buffer = buffer[..(int)_remaining];
        }

        var read = _inner.Read(buffer);
        _remaining -= read;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
