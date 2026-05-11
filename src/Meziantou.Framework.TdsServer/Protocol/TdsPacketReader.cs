using System.Buffers;
using System.Buffers.Binary;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsPacketReader
{
    public static async ValueTask<TdsPacket?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        TdsPacketType? messageType = null;
        byte[]? payload = null;
        var payloadLength = 0;
        var header = ArrayPool<byte>.Shared.Rent(8);

        try
        {
            while (true)
            {
                if (!await TryReadExactlyAsync(stream, header.AsMemory(0, 8), cancellationToken).ConfigureAwait(false))
                {
                    if (payloadLength == 0)
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
                    EnsurePayloadCapacity(ref payload, payloadLength + currentPayloadLength, payloadLength);
                    await stream.ReadExactlyAsync(payload.AsMemory(payloadLength, currentPayloadLength), cancellationToken).ConfigureAwait(false);
                    payloadLength += currentPayloadLength;
                }

                if ((status & TdsPacketStatus.EndOfMessage) == TdsPacketStatus.EndOfMessage)
                {
                    break;
                }
            }

            return new TdsPacket
            {
                Type = messageType ?? TdsPacketType.TabularResult,
                Payload = payloadLength == 0 ? [] : payload![..payloadLength].ToArray(),
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
            if (payload is not null)
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }
    }

    private static void EnsurePayloadCapacity(ref byte[]? payload, int requiredLength, int existingLength)
    {
        if (payload is null)
        {
            payload = ArrayPool<byte>.Shared.Rent(requiredLength);
            return;
        }

        if (payload.Length >= requiredLength)
            return;

        var newPayload = ArrayPool<byte>.Shared.Rent(requiredLength);
        payload.AsSpan(0, existingLength).CopyTo(newPayload);
        ArrayPool<byte>.Shared.Return(payload);
        payload = newPayload;
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
