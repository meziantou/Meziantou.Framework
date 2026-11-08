using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.Tds.Tests;

/// <summary>A TDS connection driven byte by byte, to exercise exchanges no client library lets a test control.</summary>
internal sealed class RawTdsConnection : IDisposable
{
    private readonly TcpClient _client;
    private NetworkStream? _stream;
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));

    private RawTdsConnection(TcpClient client)
    {
        _client = client;
    }

    private NetworkStream Stream => _stream ?? throw new InvalidOperationException("The connection is not open.");

    /// <summary>Connects and sends a PRELOGIN that keeps the session in clear text, and asks for MARS when <paramref name="mars"/> is set.</summary>
    public static async Task<RawTdsConnection> ConnectAsync(int port, bool mars = false)
    {
        var client = new TcpClient();
        var connection = new RawTdsConnection(client);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, connection._timeout.Token);
            connection._stream = client.GetStream();

            // ENCRYPTION = NOT_SUP and MARS = 0 or 1
            await connection.SendMessageAsync(0x12, [0x01, 0x00, 0x0B, 0x00, 0x01, 0x04, 0x00, 0x0C, 0x00, 0x01, 0xFF, 0x02, mars ? (byte)1 : (byte)0]);
            _ = await connection.ReadMessageAsync();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Creates a LOGIN7 payload with empty variable-length fields, and with the SSPI data when one is given.</summary>
    public static byte[] CreateLogin7(byte[]? sspi = null)
    {
        var payload = new byte[94 + (sspi?.Length ?? 0)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)payload.Length);
        if (sspi is not null)
        {
            payload[25] = 0x80; // OptionFlags2: fIntSecurity
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(78), 94); // ibSSPI
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(80), (ushort)sspi.Length); // cbSSPI
            sspi.CopyTo(payload, 94);
        }

        return payload;
    }

    /// <summary>Creates a SQL batch payload without ALL_HEADERS.</summary>
    public static byte[] CreateSqlBatch(string commandText) => System.Text.Encoding.Unicode.GetBytes(commandText);

    /// <summary>Creates a single-packet TDS message.</summary>
    public static byte[] CreateTdsMessage(byte packetType, ReadOnlySpan<byte> payload)
    {
        var message = new byte[8 + payload.Length];
        message[0] = packetType;
        message[1] = 0x01; // end of message
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), (ushort)message.Length);
        message[6] = 1; // packet id
        payload.CopyTo(message.AsSpan(8));
        return message;
    }

    /// <summary>Returns the number of the first ERROR token of a response, or <see langword="null"/> when it has none.</summary>
    public static uint? GetErrorNumber(byte[] response)
    {
        return response.Length > 7 && response[0] == 0xAA ? BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(3)) : null;
    }

    public async Task SendMessageAsync(byte packetType, byte[] payload)
    {
        await Stream.WriteAsync(CreateTdsMessage(packetType, payload), _timeout.Token);
    }

    /// <summary>Reads a TDS message, and returns the type of its packets and its payload.</summary>
    public async Task<(byte PacketType, byte[] Payload)> ReadMessageAsync()
    {
        using var payload = new MemoryStream();
        var header = new byte[8];
        while (true)
        {
            await Stream.ReadExactlyAsync(header, _timeout.Token);
            var data = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2)) - 8];
            await Stream.ReadExactlyAsync(data, _timeout.Token);
            payload.Write(data);
            if ((header[1] & 0x01) != 0)
            {
                return (header[0], payload.ToArray());
            }
        }
    }

    /// <summary>Sends an SMP packet (MARS). A DATA packet carries <paramref name="data"/>.</summary>
    public async Task SendSmpAsync(byte flags, ushort sessionId, uint sequenceNumber, uint window, byte[]? data = null)
    {
        var packet = new byte[16 + (data?.Length ?? 0)];
        packet[0] = 0x53;
        packet[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), sequenceNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), window);
        data?.CopyTo(packet, 16);
        await Stream.WriteAsync(packet, _timeout.Token);
    }

    public async Task<(byte Flags, ushort SessionId, uint SequenceNumber, uint Window, byte[] Data)> ReadSmpAsync()
    {
        var header = new byte[16];
        await Stream.ReadExactlyAsync(header, _timeout.Token);
        Assert.Equal(0x53, header[0]);
        var data = new byte[BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) - 16];
        await Stream.ReadExactlyAsync(data, _timeout.Token);
        return (header[1], BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)), data);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client.Dispose();
        _timeout.Dispose();
    }
}
