using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds.Hosting;

/// <summary>Runs the SQL Server Browser responder for a TDS server hosted by Kestrel.</summary>
internal sealed class TdsSqlServerBrowserHostedService : IHostedService, IDisposable
{
    private readonly TdsSqlServerBrowserOptions _options;
    private readonly IReadOnlyList<ListenOptions> _listenOptions;
    private readonly ILogger<TdsSqlServerBrowserHostedService> _logger;
    private TdsSqlServerBrowserResponder? _responder;

    public TdsSqlServerBrowserHostedService(TdsSqlServerBrowserOptions options, IReadOnlyList<ListenOptions> listenOptions, ILogger<TdsSqlServerBrowserHostedService> logger)
    {
        _options = options;
        _listenOptions = listenOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _responder = TdsSqlServerBrowserResponder.Start(_options, GetTcpPort, _logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _responder?.Dispose();
        _responder = null;
    }

    // Kestrel replaces the endpoint of a listener with the bound one, which resolves port 0.
    // Hosted services may start before Kestrel binds, so the port is read for each request.
    private int? GetTcpPort()
    {
        foreach (var listenOptions in _listenOptions)
        {
            if (listenOptions.IPEndPoint is { Port: > 0 } endPoint)
                return endPoint.Port;
        }

        return null;
    }
}
