using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.PostgreSql.Handler;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Framework.PostgreSql;

/// <summary>A standalone PostgreSQL server.</summary>
public sealed class PostgreSqlServer : IDisposable, IAsyncDisposable
{
    private readonly PostgreSqlServerOptions _options;
    private readonly PostgreSqlAuthenticationDelegate _authenticationHandler;
    private readonly PostgreSqlQueryDelegate _queryHandler;
    private readonly ILogger _logger;
    private readonly List<TcpListener> _listeners = [];
    private readonly ConcurrentDictionary<Task, TcpClient> _connections = new();
    private readonly Lock _stateLock = new();
    private int _activeConnectionCount;
    private CancellationTokenSource? _cts;
    private int[] _ports = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PostgreSqlServer"/> class.</summary>
    public PostgreSqlServer(PostgreSqlServerOptions? options, PostgreSqlAuthenticationDelegate authenticationHandler, PostgreSqlQueryDelegate queryHandler)
        : this(options, authenticationHandler, queryHandler, logger: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PostgreSqlServer"/> class.</summary>
    /// <param name="options">Server configuration. When <see langword="null"/>, defaults are used.</param>
    /// <param name="authenticationHandler">Callback invoked to authenticate a client.</param>
    /// <param name="queryHandler">Callback invoked to handle a query.</param>
    /// <param name="logger">
    /// The logger used to report connection failures. When <see langword="null"/> nothing is logged, and a
    /// connection that fails to negotiate, authenticate or answer a query does so silently.
    /// </param>
    public PostgreSqlServer(PostgreSqlServerOptions? options, PostgreSqlAuthenticationDelegate authenticationHandler, PostgreSqlQueryDelegate queryHandler, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(authenticationHandler);
        ArgumentNullException.ThrowIfNull(queryHandler);

        _options = options ?? new PostgreSqlServerOptions();
        _authenticationHandler = authenticationHandler;
        _queryHandler = queryHandler;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the ports currently bound by the server.</summary>
    public IReadOnlyList<int> Ports => _ports;

    /// <summary>Starts the server.</summary>
    [SuppressMessage("Reliability", "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed", Justification = "The accept loops and connection tasks intentionally outlive StartAsync. Shutdown closes the tracked clients on purpose, to make pending socket reads fail, and both loops handle ObjectDisposedException; StopAsync awaits the connection tasks before disposal completes.")]
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cts is not null)
            {
                return Task.CompletedTask;
            }

            _ = _options.GetTlsCertificate();
            var listenerOptions = _options.TcpListeners.Count > 0
                ? _options.TcpListeners
                : [new PostgreSqlTcpListenerOptions { BindAddress = IPAddress.Loopback, Port = 5432 }];

            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                foreach (var listenerOption in listenerOptions)
                {
                    var listener = new TcpListener(listenerOption.BindAddress, listenerOption.Port);
                    listener.Start();
                    _listeners.Add(listener);
                }
            }
            catch
            {
                // Binding is all-or-nothing: the caller has no reason to dispose a server whose start threw.
                StopListeners();
                cancellationTokenSource.Dispose();
                throw;
            }

            _cts = cancellationTokenSource;
            _ports = [.. _listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port)];

            // The token is captured by value; the accept loops never touch the CancellationTokenSource itself.
            var shutdownToken = cancellationTokenSource.Token;
            foreach (var listener in _listeners)
            {
                _ = AcceptLoopAsync(listener, shutdownToken);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops accepting connections and waits for in-flight connections to complete.</summary>
    /// <param name="cancellationToken">Bounds how long to wait for in-flight connections before abandoning them.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cancellationTokenSource;
        lock (_stateLock)
        {
            cancellationTokenSource = _cts;
            StopListeners();
        }

        if (cancellationTokenSource is null)
        {
            return;
        }

        await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        // A socket read already in flight does not observe a CancellationToken, so the client is closed to
        // force it to fail; otherwise a peer that connects and sends nothing would never complete.
        foreach (var client in _connections.Values)
        {
            try
            {
                client.Close();
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            await Task.WhenAll([.. _connections.Keys]).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "A PostgreSQL connection faulted during shutdown");
        }
    }

    /// <summary>Stops the server and releases resources.</summary>
    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        StopListeners();
        _cts?.Dispose();
    }

    /// <summary>Stops the server, waiting for in-flight connections, and releases resources.</summary>
    public async ValueTask DisposeAsync()
    {
        bool alreadyDisposed;
        lock (_stateLock)
        {
            alreadyDisposed = _disposed;
        }

        if (!alreadyDisposed)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Dispose();
    }

    private void StopListeners()
    {
        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
            }
            catch (SocketException)
            {
            }
        }

        _listeners.Clear();
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
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionAborted or SocketError.ConnectionReset or SocketError.Interrupted)
            {
                // A peer that resets between the handshake and accept is routine; the listener is still usable.
                continue;
            }
            catch (Exception ex)
            {
                // The task is fire-and-forget, so without this the listener would go deaf with no trace.
                _logger.LogError(ex, "Stopped accepting PostgreSQL connections after a listener failure");
                return;
            }

            if (Interlocked.Increment(ref _activeConnectionCount) > _options.MaxConcurrentConnections)
            {
                _ = Interlocked.Decrement(ref _activeConnectionCount);
                _logger.LogWarning("Rejected a PostgreSQL connection from {RemoteEndPoint}: the limit of {MaxConcurrentConnections} concurrent connections was reached", client.Client.RemoteEndPoint, _options.MaxConcurrentConnections);
                client.Dispose();
                continue;
            }

            TrackConnection(client, cancellationToken);
        }
    }

    private void TrackConnection(TcpClient client, CancellationToken cancellationToken)
    {
        Task? connectionTask = null;
        connectionTask = Task.Run(
            async () =>
            {
                try
                {
                    await ProcessClientAsync(client, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _ = Interlocked.Decrement(ref _activeConnectionCount);
                    if (connectionTask is not null)
                    {
                        _ = _connections.TryRemove(connectionTask, out _);
                    }
                }
            },
            CancellationToken.None);

        _connections[connectionTask] = client;
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        var endpoint = client.Client.RemoteEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0);
        var processor = new PostgreSqlConnectionProcessor(_options, _authenticationHandler, _queryHandler, _logger);
        try
        {
            // Each protocol message is written in a single call, but disabling Nagle also avoids delaying the
            // last write of a response while the peer's ACK is outstanding.
            client.NoDelay = true;
            using var stream = client.GetStream();
            await processor.ProcessAsync(stream, stream, endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // This runs on a fire-and-forget task, so an escaping exception would otherwise be unobserved.
            _logger.LogDebug(ex, "PostgreSQL connection from {RemoteEndPoint} closed with exception", endpoint);
        }
    }
}
