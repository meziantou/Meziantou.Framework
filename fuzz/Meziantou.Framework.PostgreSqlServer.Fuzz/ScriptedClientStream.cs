namespace Meziantou.Framework.PostgreSql.Fuzz;

/// <summary>
/// The client side of a connection that answers the server: when the server has read everything the client sent, the
/// script gets what the server wrote so far and returns the next bytes to send, or <see langword="null"/> to close the
/// connection. Everything runs synchronously, so the exchange is deterministic.
/// </summary>
internal sealed class ScriptedClientStream : Stream
{
    private readonly MemoryStream _serverOutput;
    private readonly Func<int, byte[], byte[]?> _script;
    private byte[] _pending = [];
    private int _pendingOffset;
    private int _step;
    private bool _closed;

    public ScriptedClientStream(MemoryStream serverOutput, Func<int, byte[], byte[]?> script)
    {
        _serverOutput = serverOutput;
        _script = script;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (_pendingOffset == _pending.Length)
        {
            if (_closed)
                return 0;

            var next = _script(_step++, _serverOutput.ToArray());
            if (next is null)
            {
                _closed = true;
                return 0;
            }

            _pending = next;
            _pendingOffset = 0;
        }

        var read = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
        _pending.AsSpan(_pendingOffset, read).CopyTo(buffer);
        _pendingOffset += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
