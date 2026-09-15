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

internal sealed class DnsQuicListener : BackgroundService, IAsyncDisposable
{
    /// <summary>How long shutdown waits for in-flight requests before dropping them.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly DnsServerOptions _options;
    private readonly DnsRequestProcessor _processor;
    private readonly ILogger<DnsQuicListener> _logger;
    private readonly PendingRequestTracker _pendingRequests = new();

    // Published once by StartAsync and never mutated afterwards: ExecuteAsync can still be iterating it
    // when a failed startup makes the host dispose the services underneath it.
    private (QuicListener Listener, IPEndPoint Endpoint)[] _listeners = [];

    public DnsQuicListener(DnsServerOptions options, DnsRequestProcessor processor, ILogger<DnsQuicListener> logger)
    {
        _options = options;
        _processor = processor;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!QuicListener.IsSupported)
        {
            _logger.LogWarning("QUIC is not supported on this platform. DNS over QUIC listeners will not start.");
        }
        else
        {
            // Bind up front, like the UDP listener, so that the server accepts connections as soon as the host
            // reports it started, and a port conflict surfaces as a startup failure rather than faulting the
            // background task later.
            var listeners = new List<(QuicListener Listener, IPEndPoint Endpoint)>(_options.QuicListeners.Count);
            try
            {
                foreach (var listenerOptions in _options.QuicListeners)
                {
                    var endpoint = new IPEndPoint(listenerOptions.BindAddress, listenerOptions.Port);
                    var listener = await QuicListener.ListenAsync(CreateListenerOptions(listenerOptions, endpoint), cancellationToken).ConfigureAwait(false);
                    listeners.Add((listener, endpoint));
                }
            }
            catch
            {
                foreach (var (listener, _) in listeners)
                {
                    await listener.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }

            _listeners = [.. listeners];
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>(_listeners.Length);
        foreach (var (listener, endpoint) in _listeners)
        {
            tasks.Add(RunListenerAsync(listener, endpoint, stoppingToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (!await _pendingRequests.DrainAsync(DrainTimeout).ConfigureAwait(false))
        {
            _logger.LogWarning("Some QUIC DNS requests were still running after {Timeout} and were abandoned", DrainTimeout);
        }

        await DisposeListenersAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        base.Dispose();
        await DisposeListenersAsync().ConfigureAwait(false);
    }

    public override void Dispose()
    {
        // QuicListener is only asynchronously disposable. BackgroundService.Dispose cancels the accept loops first,
        // so this only waits for the native listeners to stop.
        base.Dispose();
        DisposeListenersAsync().AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask DisposeListenersAsync()
    {
        // QuicListener.DisposeAsync is idempotent, so StopAsync and Dispose can both run this.
        foreach (var (listener, _) in _listeners)
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }
    }

    private System.Net.Quic.QuicListenerOptions CreateListenerOptions(Hosting.QuicListenerOptions listenerOptions, IPEndPoint endpoint)
    {
        return new System.Net.Quic.QuicListenerOptions
        {
            ListenEndPoint = endpoint,
            ApplicationProtocols = [new SslApplicationProtocol("doq")],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                IdleTimeout = _options.QuicIdleTimeout,
                MaxInboundBidirectionalStreams = _options.MaxConcurrentQueriesPerConnection,
                MaxInboundUnidirectionalStreams = 0,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ApplicationProtocols = [new SslApplicationProtocol("doq")],
                    ServerCertificate = listenerOptions.Certificate,
                },
            }),
        };
    }

    private async Task RunListenerAsync(QuicListener listener, IPEndPoint endpoint, CancellationToken stoppingToken)
    {
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
                catch (ObjectDisposedException)
                {
                    // The host disposed the service while the accept was pending
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
        var streams = new List<Task>();
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

                    _pendingRequests.Begin();

#pragma warning disable CA2025 // The stream is disposed inside HandleStreamAsync
                    streams.Add(HandleStreamAsync(stream, connection.RemoteEndPoint, stoppingToken));
#pragma warning restore CA2025

                    // HandleStreamAsync never throws, so completed entries carry nothing worth observing.
                    streams.RemoveAll(task => task.IsCompleted);
                }

                // Finish answering the queries already in flight before the connection is disposed.
                await Task.WhenAll(streams).ConfigureAwait(false);
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

                var responseBytes = await _processor.ProcessAsync(messageBytes, DnsServerProtocol.Quic, remoteEndPoint, DnsMessageEncoder.MaxMessageSize, stoppingToken).ConfigureAwait(false);
                if (responseBytes is null)
                    return;

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
        catch (QuicException)
        {
            // The peer reset the stream or closed the connection
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling QUIC DNS stream from {RemoteEndPoint}", remoteEndPoint);
        }
        finally
        {
            _pendingRequests.End();
        }
    }
}
