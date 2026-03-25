using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.DnsServer.Listeners;

internal sealed class DnsUdpListener : BackgroundService
{
    private readonly DnsServerOptions _options;
    private readonly DnsRequestDelegateHolder _handlerHolder;
    private readonly ILogger<DnsUdpListener> _logger;

    public DnsUdpListener(DnsServerOptions options, DnsRequestDelegateHolder handlerHolder, ILogger<DnsUdpListener> logger)
    {
        _options = options;
        _handlerHolder = handlerHolder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        foreach (var listener in _options.UdpListeners)
        {
            tasks.Add(RunListenerAsync(listener, stoppingToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunListenerAsync(UdpListenerOptions listenerOptions, CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(listenerOptions.BindAddress, listenerOptions.Port);
        using var udpClient = new UdpClient(endpoint);

        _logger.LogInformation("DNS UDP listener started on {Endpoint}", endpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udpClient.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

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
            var query = DnsMessageEncoder.DecodeQuery(result.Buffer);
            var context = new DnsRequestContext(query, DnsServerProtocol.Udp, result.RemoteEndPoint);
            var response = await _handlerHolder.Handler(context, stoppingToken).ConfigureAwait(false);

            var responseBytes = DnsMessageEncoder.EncodeResponse(response);

            // UDP has a maximum payload size; truncate if needed
            var maxSize = query.EdnsOptions?.UdpPayloadSize ?? 512;
            if (responseBytes.Length > maxSize)
            {
                response.IsTruncated = true;
                response.Answers.Clear();
                response.Authorities.Clear();
                response.AdditionalRecords.Clear();
                responseBytes = DnsMessageEncoder.EncodeResponse(response);
            }

            await udpClient.SendAsync(responseBytes, result.RemoteEndPoint, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling UDP DNS request from {RemoteEndPoint}", result.RemoteEndPoint);
        }
    }
}
