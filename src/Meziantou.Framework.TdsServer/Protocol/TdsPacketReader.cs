using System.Buffers.Binary;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsPacketReader
{
    public static async ValueTask<TdsPacket?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        TdsPacketType? messageType = null;
        using var payload = new MemoryStream();

        while (true)
        {
            var header = new byte[8];
            if (!await TryReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
            {
                if (payload.Length == 0)
                {
                    return null;
                }

                throw new InvalidDataException("Unexpected end of stream while reading TDS packet header.");
            }

            var packetType = (TdsPacketType)header[0];
            var status = (TdsPacketStatus)header[1];
            var packetLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
            if (packetLength < 8)
            {
                throw new InvalidDataException("Invalid TDS packet length.");
            }

            messageType ??= packetType;
            if (messageType != packetType)
            {
                throw new InvalidDataException("TDS message packets must share the same packet type.");
            }

            var currentPayloadLength = packetLength - 8;
            if (currentPayloadLength > 0)
            {
                var buffer = new byte[currentPayloadLength];
                await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
                payload.Write(buffer, 0, buffer.Length);
            }

            if ((status & TdsPacketStatus.EndOfMessage) == TdsPacketStatus.EndOfMessage)
            {
                break;
            }
        }

        return new TdsPacket
        {
            Type = messageType ?? TdsPacketType.TabularResult,
            Payload = payload.ToArray(),
        };
    }

    private static async ValueTask<bool> TryReadExactlyAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var read = await stream.ReadAsync(destination[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead > 0 ? throw new InvalidDataException("Unexpected end of stream.") : false;
            }

            totalRead += read;
        }

        return true;
    }
}
