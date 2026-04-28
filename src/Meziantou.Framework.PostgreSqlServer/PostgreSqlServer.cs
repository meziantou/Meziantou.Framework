using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.PostgreSql.Handler;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Framework.PostgreSql;

/// <summary>A standalone PostgreSQL server.</summary>
public sealed class PostgreSqlServer : IDisposable
{
    private readonly PostgreSqlServerOptions _options;
    private readonly PostgreSqlAuthenticationDelegate _authenticationHandler;
    private readonly PostgreSqlQueryDelegate _queryHandler;
    private readonly List<TcpListener> _listeners = [];
    private CancellationTokenSource? _cts;

    /// <summary>Initializes a new instance of the <see cref="PostgreSqlServer"/> class.</summary>
    public PostgreSqlServer(PostgreSqlServerOptions? options, PostgreSqlAuthenticationDelegate authenticationHandler, PostgreSqlQueryDelegate queryHandler)
    {
        ArgumentNullException.ThrowIfNull(authenticationHandler);
        ArgumentNullException.ThrowIfNull(queryHandler);

        _options = options ?? new PostgreSqlServerOptions();
        _authenticationHandler = authenticationHandler;
        _queryHandler = queryHandler;
    }

    /// <summary>Gets the ports currently bound by the server.</summary>
    public IReadOnlyList<int> Ports => _listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();

    /// <summary>Starts the server.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_cts is not null && _cts.IsCancellationRequested, this);
        if (_cts is not null)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = _options.GetTlsCertificate();
        var listenerOptions = _options.TcpListeners.Count > 0
            ? _options.TcpListeners
            : [new PostgreSqlTcpListenerOptions { BindAddress = IPAddress.Loopback, Port = 5432 }];

        foreach (var listenerOption in listenerOptions)
        {
            var listener = new TcpListener(listenerOption.BindAddress, listenerOption.Port);
            listener.Start();
            _listeners.Add(listener);
            _ = AcceptLoopAsync(listener, _cts.Token);
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops the server and releases resources.</summary>
    public void Dispose()
    {
        _cts?.Cancel();

        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        _listeners.Clear();
        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = ProcessClientAsync(client, cancellationToken);
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        var endpoint = client.Client.RemoteEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0);
        var processor = new PostgreSqlConnectionProcessor(_options, _authenticationHandler, _queryHandler, NullLogger.Instance);
        try
        {
            using var stream = client.GetStream();
            await processor.ProcessAsync(stream, stream, endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
