using System.Text;

namespace Meziantou.Framework;

internal sealed class StringBuilderOutputStream : Stream
{
    private readonly StringBuilder _stringBuilder;
    private readonly object _syncObject = new();
    private Decoder? _decoder;

    public StringBuilderOutputStream(StringBuilder stringBuilder)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);
        _stringBuilder = stringBuilder;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    internal void SetEncoding(Encoding encoding)
    {
        lock (_syncObject)
        {
            _decoder = encoding.GetDecoder();
        }
    }

    public override void Flush()
    {
        FlushDecoder();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FlushDecoder();
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteCore(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteCore(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteCore(buffer.Span);
        return ValueTask.CompletedTask;
    }

    private void WriteCore(ReadOnlySpan<byte> buffer)
    {
        lock (_syncObject)
        {
            var decoder = _decoder ??= Encoding.UTF8.GetDecoder();
            Span<char> chars = stackalloc char[1024];

            while (!buffer.IsEmpty)
            {
                decoder.Convert(buffer, chars, flush: false, out var bytesUsed, out var charsUsed, out _);
                if (charsUsed > 0)
                {
                    lock (_stringBuilder)
                    {
                        _stringBuilder.Append(chars[..charsUsed]);
                    }
                }

                buffer = buffer[bytesUsed..];
            }
        }
    }

    private void FlushDecoder()
    {
        lock (_syncObject)
        {
            if (_decoder is null)
            {
                return;
            }

            Span<char> chars = stackalloc char[1024];
            while (true)
            {
                _decoder.Convert(ReadOnlySpan<byte>.Empty, chars, flush: true, out _, out var charsUsed, out var completed);
                if (charsUsed > 0)
                {
                    lock (_stringBuilder)
                    {
                        _stringBuilder.Append(chars[..charsUsed]);
                    }
                }

                if (completed)
                {
                    break;
                }
            }
        }
    }
}
