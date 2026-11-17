using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// The compressed input of a decompression stream. The decoders read it synchronously; <see cref="PrefetchAsync"/> reads
/// the next block of the base stream asynchronously before an asynchronous read of the decompression stream, so most of
/// the reads of the base stream are asynchronous when the caller reads asynchronously.
/// </summary>
internal sealed class PrefetchingReadStream : ReadOnlyStreamBase
{
    private const int BufferSize = 1 << 16;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The stream does not own it")]
    private readonly Stream _stream;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    private int _position;
    private int _end;
    private bool _endOfStream;

    public PrefetchingReadStream(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>Reads the next block of the base stream when nothing is buffered.</summary>
    public async ValueTask PrefetchAsync(CancellationToken cancellationToken)
    {
        if (_position < _end || _endOfStream)
            return;

        var read = await _stream.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        _position = 0;
        _end = read;
        if (read <= 0)
        {
            _endOfStream = true;
            _end = 0;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        if (_position < _end)
        {
            var count = Math.Min(buffer.Length, _end - _position);
            _buffer.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        if (_endOfStream)
            return 0;

        var read = _stream.Read(buffer);
        if (read <= 0)
        {
            _endOfStream = true;
            return 0;
        }

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        base.Dispose(disposing);
    }
}
