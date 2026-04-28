using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlMessageReader
{
    public static async ValueTask<PostgreSqlStartupPacket?> ReadStartupPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthBuffer = new byte[4];
        if (!await TryReadExactlyAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (length < 8)
        {
            throw new InvalidDataException("Invalid PostgreSQL startup packet length.");
        }

        var body = new byte[length - 4];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        var requestCode = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(0, 4));
        var payload = body.AsSpan(4).ToArray();
        return new PostgreSqlStartupPacket
        {
            RequestCode = requestCode,
            Payload = payload,
        };
    }

    public static async ValueTask<PostgreSqlFrontendMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[5];
        if (!await TryReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var messageType = header[0];
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
        if (length < 4)
        {
            throw new InvalidDataException("Invalid PostgreSQL frontend message length.");
        }

        var payload = new byte[length - 4];
        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return new PostgreSqlFrontendMessage
        {
            Type = messageType,
            Payload = payload,
        };
    }

    public static Dictionary<string, string> ParseStartupParameters(ReadOnlySpan<byte> payload)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        while (index < payload.Length)
        {
            var key = ReadNullTerminatedString(payload, ref index);
            if (key.Length == 0)
            {
                break;
            }

            if (index >= payload.Length)
            {
                throw new InvalidDataException("Invalid startup packet parameters.");
            }

            var value = ReadNullTerminatedString(payload, ref index);
            result[key] = value;
        }

        return result;
    }

    public static string ReadNullTerminatedString(ReadOnlySpan<byte> payload, ref int index)
    {
        var start = index;
        while (index < payload.Length && payload[index] != 0)
        {
            index++;
        }

        if (index >= payload.Length)
        {
            throw new InvalidDataException("Invalid null-terminated string in PostgreSQL payload.");
        }

        var value = Encoding.UTF8.GetString(payload[start..index]);
        index++;
        return value;
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
