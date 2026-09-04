using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.PostgreSql.Tests;

/// <summary>
/// A minimal PostgreSQL frontend that speaks the wire protocol directly.
/// Npgsql never sends simple queries, Close, Flush or malformed packets, so those paths need a raw client.
/// </summary>
internal sealed class RawPostgreSqlClient : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private RawPostgreSqlClient(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<RawPostgreSqlClient> ConnectAsync(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        return new RawPostgreSqlClient(client);
    }

    /// <summary>Performs a cleartext-password startup exchange and reads through to ReadyForQuery.</summary>
    public async Task AuthenticateClearTextAsync(string userName = "app", string database = "postgres", string password = "Password123!")
    {
        await SendStartupAsync(userName, database);

        var authentication = await ReadMessageAsync();
        Assert.Equal((byte)'R', authentication!.Type);

        await SendMessageAsync((byte)'p', NullTerminated(password));
        while (true)
        {
            var message = await ReadMessageAsync();
            Assert.NotNull(message);
            if (message.Type == (byte)'Z')
            {
                return;
            }

            Assert.NotEqual((byte)'E', message.Type);
        }
    }

    public async Task SendStartupAsync(string userName, string database)
    {
        var payload = new List<byte>();
        payload.AddRange(NullTerminated("user"));
        payload.AddRange(NullTerminated(userName));
        payload.AddRange(NullTerminated("database"));
        payload.AddRange(NullTerminated(database));
        payload.Add(0);

        var packet = new byte[8 + payload.Count];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, 4), packet.Length);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4, 4), 196608);
        payload.CopyTo(packet, 8);
        await _stream.WriteAsync(packet);
    }

    /// <summary>Writes a raw startup packet, including a deliberately wrong declared length.</summary>
    public async Task SendRawStartupAsync(int declaredLength, ReadOnlyMemory<byte> body)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, declaredLength);
        await _stream.WriteAsync(header);
        if (!body.IsEmpty)
        {
            await _stream.WriteAsync(body);
        }
    }

    public async Task SendMessageAsync(byte type, ReadOnlyMemory<byte> payload)
    {
        var buffer = new byte[5 + payload.Length];
        buffer[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, 4), payload.Length + 4);
        payload.Span.CopyTo(buffer.AsSpan(5));
        await _stream.WriteAsync(buffer);
    }

    /// <summary>Writes a message header whose declared length does not match the body that follows.</summary>
    public async Task SendRawMessageAsync(byte type, int declaredPayloadLength, ReadOnlyMemory<byte> body)
    {
        var header = new byte[5];
        header[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), declaredPayloadLength + 4);
        await _stream.WriteAsync(header);
        if (!body.IsEmpty)
        {
            await _stream.WriteAsync(body);
        }
    }

    public Task SendSimpleQueryAsync(string sql) => SendMessageAsync((byte)'Q', NullTerminated(sql));

    public async Task SendSslRequestAsync()
    {
        var packet = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, 4), 8);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4, 4), 80877103);
        await _stream.WriteAsync(packet);
    }

    public async Task SendCancelRequestAsync(int processId, int secretKey)
    {
        var packet = new byte[16];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, 4), 16);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4, 4), 80877102);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8, 4), processId);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12, 4), secretKey);
        await _stream.WriteAsync(packet);
    }

    public async Task<byte> ReadSingleByteAsync()
    {
        var buffer = new byte[1];
        Assert.True(await TryReadExactlyAsync(buffer));
        return buffer[0];
    }

    public Task SendParseAsync(string statementName, string query, uint[]? parameterTypeOids = null)
    {
        var payload = new List<byte>();
        payload.AddRange(NullTerminated(statementName));
        payload.AddRange(NullTerminated(query));
        payload.AddRange(Int16BigEndian((short)(parameterTypeOids?.Length ?? 0)));
        foreach (var oid in parameterTypeOids ?? [])
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, oid);
            payload.AddRange(buffer);
        }

        return SendMessageAsync((byte)'P', payload.ToArray());
    }

    public Task SendBindWithParametersAsync(string portalName, string statementName, short formatCode, string[] values)
    {
        var payload = new List<byte>();
        payload.AddRange(NullTerminated(portalName));
        payload.AddRange(NullTerminated(statementName));
        payload.AddRange(Int16BigEndian(1));
        payload.AddRange(Int16BigEndian(formatCode));
        payload.AddRange(Int16BigEndian((short)values.Length));
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            payload.AddRange(length);
            payload.AddRange(bytes);
        }

        payload.AddRange(Int16BigEndian(0));
        return SendMessageAsync((byte)'B', payload.ToArray());
    }

    public Task SendExecuteAsync(string portalName, int maxRows)
    {
        var payload = new List<byte>();
        payload.AddRange(NullTerminated(portalName));
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, maxRows);
        payload.AddRange(buffer);
        return SendMessageAsync((byte)'E', payload.ToArray());
    }

    private static byte[] Int16BigEndian(short value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        return buffer;
    }

    public Task SendBindAsync(string portalName, string statementName)
    {
        var payload = new List<byte>();
        payload.AddRange(NullTerminated(portalName));
        payload.AddRange(NullTerminated(statementName));
        payload.AddRange([0, 0]);
        payload.AddRange([0, 0]);
        payload.AddRange([0, 0]);
        return SendMessageAsync((byte)'B', payload.ToArray());
    }

    public Task SendDescribeAsync(byte target, string name)
    {
        var payload = new List<byte> { target };
        payload.AddRange(NullTerminated(name));
        return SendMessageAsync((byte)'D', payload.ToArray());
    }

    public Task SendCloseAsync(byte target, string name)
    {
        var payload = new List<byte> { target };
        payload.AddRange(NullTerminated(name));
        return SendMessageAsync((byte)'C', payload.ToArray());
    }

    public Task SendSyncAsync() => SendMessageAsync((byte)'S', ReadOnlyMemory<byte>.Empty);

    public async Task<RawMessage?> ReadMessageAsync()
    {
        var header = new byte[5];
        if (!await TryReadExactlyAsync(header))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
        var payload = new byte[length - 4];
        if (payload.Length > 0 && !await TryReadExactlyAsync(payload))
        {
            return null;
        }

        return new RawMessage(header[0], payload);
    }

    /// <summary>Reads messages until ReadyForQuery, or until the connection closes.</summary>
    public async Task<List<RawMessage>> ReadUntilReadyForQueryAsync()
    {
        var messages = new List<RawMessage>();
        while (true)
        {
            var message = await ReadMessageAsync();
            if (message is null)
            {
                return messages;
            }

            messages.Add(message);
            if (message.Type == (byte)'Z')
            {
                return messages;
            }
        }
    }

    private async Task<bool> TryReadExactlyAsync(Memory<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = await _stream.ReadAsync(destination[total..]);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private static byte[] NullTerminated(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var result = new byte[bytes.Length + 1];
        bytes.CopyTo(result, 0);
        return result;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }

    internal sealed record RawMessage(byte Type, byte[] Payload)
    {
        public string AsText() => Encoding.UTF8.GetString(Payload).TrimEnd('\0');

        /// <summary>Decodes a DataRow payload into its field values, with <see langword="null"/> for SQL NULL.</summary>
        public string?[] DataRowValues()
        {
            var fieldCount = BinaryPrimitives.ReadInt16BigEndian(Payload.AsSpan(0, 2));
            var values = new string?[fieldCount];
            var index = 2;
            for (var i = 0; i < fieldCount; i++)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(index, 4));
                index += 4;
                if (length < 0)
                {
                    values[i] = null;
                    continue;
                }

                values[i] = Encoding.UTF8.GetString(Payload.AsSpan(index, length));
                index += length;
            }

            return values;
        }

        /// <summary>Reads the fields of an ErrorResponse or NoticeResponse, keyed by their field code.</summary>
        public Dictionary<char, string> ErrorFields()
        {
            var fields = new Dictionary<char, string>();
            var index = 0;
            while (index < Payload.Length && Payload[index] != 0)
            {
                var code = (char)Payload[index++];
                var end = Array.IndexOf(Payload, (byte)0, index);
                if (end < 0)
                {
                    break;
                }

                fields[code] = Encoding.UTF8.GetString(Payload.AsSpan(index, end - index));
                index = end + 1;
            }

            return fields;
        }
    }
}
