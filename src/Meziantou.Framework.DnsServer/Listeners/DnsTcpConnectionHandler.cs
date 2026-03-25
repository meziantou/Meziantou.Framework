using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.DnsServer.Listeners;

internal sealed class DnsTcpConnectionHandler : ConnectionHandler
{
    private readonly DnsRequestDelegateHolder _handlerHolder;
    private readonly DnsServerProtocol _protocol;
    private readonly ILogger<DnsTcpConnectionHandler> _logger;

    public DnsTcpConnectionHandler(DnsRequestDelegateHolder handlerHolder, DnsServerProtocol protocol, ILogger<DnsTcpConnectionHandler> logger)
    {
        _handlerHolder = handlerHolder;
        _protocol = protocol;
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var input = connection.Transport.Input;
        var output = connection.Transport.Output;

        try
        {
            while (true)
            {
                var result = await input.ReadAsync(connection.ConnectionClosed).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryReadDnsMessage(ref buffer, out var messageBytes))
                {
                    await HandleMessageAsync(messageBytes, connection, output).ConfigureAwait(false);
                }

                input.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling TCP DNS connection from {RemoteEndPoint}", connection.RemoteEndPoint);
        }
    }

    private static bool TryReadDnsMessage(ref ReadOnlySequence<byte> buffer, out byte[] messageBytes)
    {
        messageBytes = [];

        // Need at least 2 bytes for the length prefix (RFC 7766)
        if (buffer.Length < 2)
            return false;

        Span<byte> lengthBytes = stackalloc byte[2];
        buffer.Slice(0, 2).CopyTo(lengthBytes);
        var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);

        if (buffer.Length < 2 + messageLength)
            return false;

        var messageSlice = buffer.Slice(2, messageLength);
        messageBytes = new byte[messageLength];
        messageSlice.CopyTo(messageBytes);

        buffer = buffer.Slice(2 + messageLength);
        return true;
    }

    private async Task HandleMessageAsync(byte[] messageBytes, ConnectionContext connection, PipeWriter output)
    {
        try
        {
            var query = DnsMessageEncoder.DecodeQuery(messageBytes);
            var context = new DnsRequestContext(query, _protocol, connection.RemoteEndPoint!);
            var response = await _handlerHolder.Handler(context, connection.ConnectionClosed).ConfigureAwait(false);
            var responseBytes = DnsMessageEncoder.EncodeResponse(response);

            // Write 2-byte length prefix + response (RFC 7766)
            var memory = output.GetMemory(2 + responseBytes.Length);
            BinaryPrimitives.WriteUInt16BigEndian(memory.Span, (ushort)responseBytes.Length);
            responseBytes.CopyTo(memory[2..]);
            output.Advance(2 + responseBytes.Length);

            await output.FlushAsync(connection.ConnectionClosed).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling TCP DNS message from {RemoteEndPoint}", connection.RemoteEndPoint);
        }
    }
}
