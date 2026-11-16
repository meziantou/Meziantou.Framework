namespace Meziantou.Framework.Compression.Internals.IO;

/// <summary>Reads bytes already read from a forward-only stream (to detect its format), then the rest of the stream.</summary>
internal sealed class PrefixedStream : ReadOnlyStreamBase
{
    private readonly byte[] _prefix;
    private readonly int _prefixLength;
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private int _offset;

    public PrefixedStream(byte[] prefix, int prefixLength, Stream inner, bool leaveOpen)
    {
        _prefix = prefix;
        _prefixLength = prefixLength;
        _inner = inner;
        _leaveOpen = leaveOpen;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_offset < _prefixLength)
        {
            var count = Math.Min(buffer.Length, _prefixLength - _offset);
            _prefix.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        return _inner.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset < _prefixLength)
        {
            var count = Math.Min(buffer.Length, _prefixLength - _offset);
            _prefix.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
