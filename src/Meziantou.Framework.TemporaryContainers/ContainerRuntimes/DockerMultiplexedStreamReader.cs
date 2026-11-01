using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Reads the frames of a stream multiplexed by the Docker daemon: each payload is prefixed by an 8-byte header whose first byte is the stream and whose last four bytes are the big-endian payload length.</summary>
/// <remarks>The payload buffer is pooled and reused for every frame, so <see cref="CurrentPayload"/> is valid until the next call to <see cref="ReadFrameAsync"/>.</remarks>
internal sealed class DockerMultiplexedStreamReader : IDisposable
{
    private const int HeaderLength = 8;
    private const int InitialPayloadCapacity = 4096;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The caller owns the stream.")]
    private readonly Stream _stream;
    private readonly byte[] _header = new byte[HeaderLength];
    private byte[] _payload = ArrayPool<byte>.Shared.Rent(InitialPayloadCapacity);
    private int _payloadLength;

    public DockerMultiplexedStreamReader(Stream stream) => _stream = stream;

    /// <summary>Gets the stream the current frame was written to.</summary>
    public LogStream CurrentStream { get; private set; }

    /// <summary>Gets the payload of the current frame.</summary>
    public ReadOnlySpan<byte> CurrentPayload => _payload.AsSpan(0, _payloadLength);

    /// <summary>Reads the next frame that has a payload.</summary>
    /// <returns><see langword="false"/> when the stream ended between two frames.</returns>
    /// <exception cref="EndOfStreamException">The stream ended in the middle of a frame.</exception>
    public async ValueTask<bool> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!await FillHeaderAsync(cancellationToken).ConfigureAwait(false))
                return false;

            var payloadLength = BinaryPrimitives.ReadInt32BigEndian(_header.AsSpan(4));
            if (payloadLength <= 0)
                continue;

            CurrentStream = _header[0] switch
            {
                2 => LogStream.Stderr,
                _ => LogStream.Stdout,
            };

            if (_payload.Length < payloadLength)
            {
                ArrayPool<byte>.Shared.Return(_payload);
                _payload = ArrayPool<byte>.Shared.Rent(payloadLength);
            }

            _payloadLength = 0;
            await ReadPayloadAsync(_payload.AsMemory(0, payloadLength), cancellationToken).ConfigureAwait(false);
            _payloadLength = payloadLength;
            return true;
        }
    }

    private async ValueTask<bool> FillHeaderAsync(CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < HeaderLength)
        {
            var read = await _stream.ReadAsync(_header.AsMemory(offset, HeaderLength - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return offset != 0 ? throw new EndOfStreamException("Unexpected end of stream while reading Docker API logs.") : false;

            offset += read;
        }

        return true;
    }

    private async ValueTask ReadPayloadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading Docker API payload.");

            offset += read;
        }
    }

    public void Dispose()
    {
        var payload = _payload;
        _payload = [];
        _payloadLength = 0;
        if (payload.Length > 0)
            ArrayPool<byte>.Shared.Return(payload);
    }
}
