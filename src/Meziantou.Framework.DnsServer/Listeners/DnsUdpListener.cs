using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.DnsServer.Listeners;

internal sealed class DnsUdpListener : BackgroundService
{
    /// <summary>How long shutdown waits for in-flight requests before dropping them.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Number of consecutive receive failures after which the loop pauses, so a permanently broken socket cannot spin the CPU.</summary>
    private const int ConsecutiveErrorsBeforeBackoff = 10;

    /// <summary>SIO_UDP_CONNRESET, which the <see cref="IOControlCode"/> enum has no member for.</summary>
    private const int SioUdpConnectionReset = -1744830452;

    private readonly DnsServerOptions _options;
    private readonly DnsRequestProcessor _processor;
    private readonly ILogger<DnsUdpListener> _logger;
    private readonly PendingRequestTracker _pendingRequests = new();
    private readonly List<(UdpClient Client, IPEndPoint Endpoint)> _listeners = [];

    public DnsUdpListener(DnsServerOptions options, DnsRequestProcessor processor, ILogger<DnsUdpListener> logger)
    {
        _options = options;
        _processor = processor;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Bind up front so that a port conflict surfaces as a startup failure rather than faulting the
        // background task later, which would take the whole host down with it.
        try
        {
            foreach (var listenerOptions in _options.UdpListeners)
            {
                var endpoint = new IPEndPoint(listenerOptions.BindAddress, listenerOptions.Port);
                var client = new UdpClient(endpoint);

                if (OperatingSystem.IsWindows())
                {
                    // Without this, an ICMP "port unreachable" caused by a client that already closed its
                    // socket makes the next receive fail (SIO_UDP_CONNRESET).
                    client.Client.IOControl(SioUdpConnectionReset, [0, 0, 0, 0], optionOutValue: null);
                }

                _listeners.Add((client, endpoint));
            }
        }
        catch
        {
            DisposeListeners();
            throw;
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>(_listeners.Count);
        foreach (var (client, endpoint) in _listeners)
        {
            tasks.Add(RunListenerAsync(client, endpoint, stoppingToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (!await _pendingRequests.DrainAsync(DrainTimeout).ConfigureAwait(false))
        {
            _logger.LogWarning("Some UDP DNS requests were still running after {Timeout} and were abandoned", DrainTimeout);
        }

        DisposeListeners();
    }

    public override void Dispose()
    {
        DisposeListeners();
        base.Dispose();
    }

    private void DisposeListeners()
    {
        foreach (var (client, _) in _listeners)
        {
            client.Dispose();
        }

        _listeners.Clear();
    }

    private async Task RunListenerAsync(UdpClient udpClient, IPEndPoint endpoint, CancellationToken stoppingToken)
    {
        _logger.LogInformation("DNS UDP listener started on {Endpoint}", endpoint);

        var consecutiveErrors = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udpClient.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                    consecutiveErrors = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    // A datagram socket reports per-peer errors on receive. They say nothing about the
                    // socket's health, so log and carry on serving instead of taking the host down.
                    _logger.LogWarning(exception, "Error receiving a UDP DNS request on {Endpoint}", endpoint);

                    if (++consecutiveErrors >= ConsecutiveErrorsBeforeBackoff)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
                    }

                    continue;
                }

                _pendingRequests.Begin();

                // Fire and forget to handle concurrent requests
#pragma warning disable CA2025 // The udpClient outlives the task
                _ = HandleRequestAsync(udpClient, result, stoppingToken);
#pragma warning restore CA2025
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }

        _logger.LogInformation("DNS UDP listener stopped on {Endpoint}", endpoint);
    }

    private async Task HandleRequestAsync(UdpClient udpClient, UdpReceiveResult result, CancellationToken stoppingToken)
    {
        try
        {
            var responseBytes = await _processor.ProcessAsync(result.Buffer, DnsServerProtocol.Udp, result.RemoteEndPoint, _options.MaxUdpResponseSize, stoppingToken).ConfigureAwait(false);
            if (responseBytes is null)
                return;

            await udpClient.SendAsync(responseBytes, result.RemoteEndPoint, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (ObjectDisposedException)
        {
            // The listener was disposed while the response was being sent
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling UDP DNS request from {RemoteEndPoint}", result.RemoteEndPoint);
        }
        finally
        {
            _pendingRequests.End();
        }
    }
}
