using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Tests;

/// <summary>Drives a <see cref="TdsServer"/> over a socket, without going through a client library.</summary>
internal static class RawTdsClient
{
    /// <summary>Sends an RPC payload as-is and returns the context the query handler received.</summary>
    public static async Task<TdsQueryContext> SendRpcRequestAsync(byte[] rpcPayload)
    {
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                queryContextTask.TrySetResult(context);
                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationTokenSource.Token);
        using var stream = client.GetStream();

        // PRELOGIN advertising NOT_SUPPORTED so the session stays in clear text.
        await stream.WriteAsync(CreateTdsMessage(0x12, [0x01, 0x00, 0x06, 0x00, 0x01, 0xFF, 0x02]), cancellationTokenSource.Token);
        await ReadTdsMessageAsync(stream, cancellationTokenSource.Token);

        // LOGIN7 with every variable-length field empty: the authentication callback above accepts anything.
        await stream.WriteAsync(CreateTdsMessage(0x10, new byte[94]), cancellationTokenSource.Token);
        await ReadTdsMessageAsync(stream, cancellationTokenSource.Token);

        await stream.WriteAsync(CreateTdsMessage(0x03, rpcPayload), cancellationTokenSource.Token);

        return await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
    }

    private static byte[] CreateTdsMessage(byte packetType, ReadOnlySpan<byte> payload)
    {
        var message = new byte[8 + payload.Length];
        message[0] = packetType;
        message[1] = 0x01; // end of message
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), (ushort)message.Length);
        message[6] = 1; // packet id
        payload.CopyTo(message.AsSpan(8));
        return message;
    }

    private static async Task ReadTdsMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        await stream.ReadExactlyAsync(new byte[length - 8], cancellationToken);
    }
}
