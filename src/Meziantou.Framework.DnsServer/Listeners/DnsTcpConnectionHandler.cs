using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.DnsServer.Listeners;

internal sealed class DnsTcpConnectionHandler : ConnectionHandler
{
    private readonly DnsRequestProcessor _processor;
    private readonly DnsServerOptions _options;
    private readonly ILogger<DnsTcpConnectionHandler> _logger;

    public DnsTcpConnectionHandler(DnsRequestProcessor processor, DnsServerOptions options, ILogger<DnsTcpConnectionHandler> logger)
    {
        _processor = processor;
        _options = options;
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var input = connection.Transport.Input;
        var output = connection.Transport.Output;

        // The same handler serves the plaintext and the TLS endpoints, so the transport is identified
        // from the connection itself rather than from how the handler was registered.
        var protocol = connection.Features.Get<ITlsHandshakeFeature>() is not null
            ? DnsServerProtocol.Tls
            : DnsServerProtocol.Tcp;

        var idleTimeout = _options.TcpIdleTimeout;
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);

        // Disposed below, once every in-flight query has finished using them.
#pragma warning disable CA2025 // The queries are awaited before these are disposed
        var writeLock = new SemaphoreSlim(1, 1);
        var slots = new SemaphoreSlim(_options.MaxConcurrentQueriesPerConnection);

        var pending = new List<Task>();

        try
        {
            idleCts.CancelAfter(idleTimeout);

            while (true)
            {
                var result = await input.ReadAsync(idleCts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;
                var receivedQuery = false;

                while (TryReadDnsMessage(ref buffer, out var messageBytes))
                {
                    receivedQuery = true;

                    // Cap how many queries one connection can have running at once.
                    await slots.WaitAsync(connection.ConnectionClosed).ConfigureAwait(false);

                    pending.Add(HandleMessageAsync(messageBytes, protocol, connection, output, writeLock, slots));
                }

                input.AdvanceTo(buffer.Start, buffer.End);

                // HandleMessageAsync never throws, so completed entries carry nothing worth observing.
                pending.RemoveAll(task => task.IsCompleted);

                // The clock measures the time since the last *complete* query, so a client that dribbles
                // out bytes to hold the connection open still runs out of time.
                if (receivedQuery)
                {
                    idleCts.CancelAfter(idleTimeout);
                }

                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (OperationCanceledException) when (idleCts.IsCancellationRequested)
        {
            _logger.LogDebug("Closing an idle DNS connection from {RemoteEndPoint} after {IdleTimeout}", connection.RemoteEndPoint, idleTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling TCP DNS connection from {RemoteEndPoint}", connection.RemoteEndPoint);
        }

        // Let the in-flight queries finish writing before the transport goes away.
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Dispose();
            slots.Dispose();
        }
#pragma warning restore CA2025
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

    private async Task HandleMessageAsync(byte[] messageBytes, DnsServerProtocol protocol, ConnectionContext connection, PipeWriter output, SemaphoreSlim writeLock, SemaphoreSlim slots)
    {
        try
        {
            var responseBytes = await _processor.ProcessAsync(messageBytes, protocol, connection.RemoteEndPoint!, DnsMessageEncoder.MaxMessageSize, connection.ConnectionClosed).ConfigureAwait(false);
            if (responseBytes is null)
                return;

            // Responses may complete out of order (RFC 7766 6.2.1.1), so one writer at a time.
            await writeLock.WaitAsync(connection.ConnectionClosed).ConfigureAwait(false);
            try
            {
                // Write 2-byte length prefix + response (RFC 7766)
                var memory = output.GetMemory(2 + responseBytes.Length);
                BinaryPrimitives.WriteUInt16BigEndian(memory.Span, (ushort)responseBytes.Length);
                responseBytes.CopyTo(memory[2..]);
                output.Advance(2 + responseBytes.Length);

                await output.FlushAsync(connection.ConnectionClosed).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (ObjectDisposedException)
        {
            // The connection was torn down while the response was being written
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling TCP DNS message from {RemoteEndPoint}", connection.RemoteEndPoint);
        }
        finally
        {
            slots.Release();
        }
    }
}
