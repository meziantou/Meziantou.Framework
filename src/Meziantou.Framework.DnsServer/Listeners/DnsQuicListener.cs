#if NET9_0_OR_GREATER
#pragma warning disable CA1416 // QUIC platform compatibility is validated at runtime via QuicListener.IsSupported
using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.DnsServer.Listeners;

internal sealed class DnsQuicListener : BackgroundService
{
    private readonly DnsServerOptions _options;
    private readonly DnsRequestDelegateHolder _handlerHolder;
    private readonly ILogger<DnsQuicListener> _logger;

    public DnsQuicListener(DnsServerOptions options, DnsRequestDelegateHolder handlerHolder, ILogger<DnsQuicListener> logger)
    {
        _options = options;
        _handlerHolder = handlerHolder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!QuicListener.IsSupported)
        {
            _logger.LogWarning("QUIC is not supported on this platform. DNS over QUIC listeners will not start.");
            return;
        }

        var tasks = new List<Task>();
        foreach (var listener in _options.QuicListeners)
        {
            tasks.Add(RunListenerAsync(listener, stoppingToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunListenerAsync(Hosting.QuicListenerOptions listenerOptions, CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(listenerOptions.BindAddress, listenerOptions.Port);

        await using var listener = await QuicListener.ListenAsync(new System.Net.Quic.QuicListenerOptions
        {
            ListenEndPoint = endpoint,
            ApplicationProtocols = [new SslApplicationProtocol("doq")],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ApplicationProtocols = [new SslApplicationProtocol("doq")],
                    ServerCertificate = listenerOptions.Certificate,
                },
            }),
        }, stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("DNS QUIC listener started on {Endpoint}", endpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                QuicConnection connection;
                try
                {
                    connection = await listener.AcceptConnectionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleConnectionAsync(connection, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }

        _logger.LogInformation("DNS QUIC listener stopped on {Endpoint}", endpoint);
    }

    private async Task HandleConnectionAsync(QuicConnection connection, CancellationToken stoppingToken)
    {
        try
        {
            await using (connection)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    QuicStream stream;
                    try
                    {
                        stream = await connection.AcceptInboundStreamAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (QuicException)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

#pragma warning disable CA2025 // The stream is disposed inside HandleStreamAsync
                    _ = HandleStreamAsync(stream, connection.RemoteEndPoint, stoppingToken);
#pragma warning restore CA2025
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling QUIC DNS connection from {RemoteEndPoint}", connection.RemoteEndPoint);
        }
    }

    private async Task HandleStreamAsync(QuicStream stream, EndPoint remoteEndPoint, CancellationToken stoppingToken)
    {
        try
        {
            await using (stream)
            {
                // Read 2-byte length prefix (RFC 9250)
                var lengthBytes = new byte[2];
                await stream.ReadExactlyAsync(lengthBytes, stoppingToken).ConfigureAwait(false);
                var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);

                var messageBytes = new byte[messageLength];
                await stream.ReadExactlyAsync(messageBytes, stoppingToken).ConfigureAwait(false);

                var query = DnsMessageEncoder.DecodeQuery(messageBytes);
                var context = new DnsRequestContext(query, DnsServerProtocol.Quic, remoteEndPoint);
                var response = await _handlerHolder.Handler(context, stoppingToken).ConfigureAwait(false);
                var responseBytes = DnsMessageEncoder.EncodeResponse(response);

                // Write 2-byte length prefix + response (RFC 9250)
                BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, (ushort)responseBytes.Length);
                await stream.WriteAsync(lengthBytes, stoppingToken).ConfigureAwait(false);
                await stream.WriteAsync(responseBytes, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling QUIC DNS stream from {RemoteEndPoint}", remoteEndPoint);
        }
    }
}
#endif
