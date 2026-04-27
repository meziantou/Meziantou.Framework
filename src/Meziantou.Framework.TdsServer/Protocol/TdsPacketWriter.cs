using System.Buffers.Binary;

namespace Meziantou.Framework.Tds.Protocol;

internal sealed class TdsPacketWriter
{
    private readonly Stream _stream;
    private readonly int _packetSize;
    private byte _packetId = 1;

    public TdsPacketWriter(Stream stream, int packetSize)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
        _packetSize = Math.Max(packetSize, 512);
    }

    public async ValueTask WriteAsync(TdsPacketType packetType, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var maxPayloadPerPacket = _packetSize - 8;
        if (maxPayloadPerPacket <= 0)
        {
            throw new InvalidOperationException("Packet size is too small.");
        }

        if (payload.Length == 0)
        {
            await WritePacketAsync(packetType, TdsPacketStatus.EndOfMessage, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = 0;
        while (offset < payload.Length)
        {
            var count = Math.Min(maxPayloadPerPacket, payload.Length - offset);
            var status = (offset + count) >= payload.Length ? TdsPacketStatus.EndOfMessage : TdsPacketStatus.None;
            await WritePacketAsync(packetType, status, payload.Slice(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;
        }
    }

    private async ValueTask WritePacketAsync(TdsPacketType packetType, TdsPacketStatus status, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        header[0] = (byte)packetType;
        header[1] = (byte)status;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2, 2), (ushort)(payload.Length + 8));
        header[4] = 0;
        header[5] = 0;
        header[6] = _packetId++;
        header[7] = 0;

        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
