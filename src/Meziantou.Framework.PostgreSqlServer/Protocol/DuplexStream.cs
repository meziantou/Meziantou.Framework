using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.PostgreSql.Protocol;

/// <summary>Presents a separate read stream and write stream as one <see cref="Stream"/>.</summary>
/// <remarks>
/// Needed because the Kestrel transport exposes the connection as two half-duplex pipes, while
/// <see cref="System.Net.Security.SslStream"/> requires a single bidirectional stream.
/// </remarks>
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Input and output streams are owned and disposed by the caller.")]
internal sealed class DuplexStream : Stream
{
    private readonly Stream _readStream;
    private readonly Stream _writeStream;

    public DuplexStream(Stream readStream, Stream writeStream)
    {
        ArgumentNullException.ThrowIfNull(readStream);
        ArgumentNullException.ThrowIfNull(writeStream);

        _readStream = readStream;
        _writeStream = writeStream;
    }

    public override bool CanRead => _readStream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => _writeStream.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _writeStream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _writeStream.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _readStream.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _readStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _readStream.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _writeStream.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _writeStream.WriteAsync(buffer, cancellationToken);
    }
}
