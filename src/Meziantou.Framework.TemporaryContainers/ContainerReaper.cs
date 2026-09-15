using System.Diagnostics;
using System.Net.Sockets;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A watchdog container that removes the containers, images and volumes of the current session when this process goes away, including when it is killed and cannot run its own cleanup.</summary>
/// <remarks>Dispose the instance to stop the watchdog. A clean disposal removes the watchdog before it can remove anything, so the resources the process still owns are left to their own disposal.</remarks>
public sealed class ContainerReaper : IAsyncDisposable
{
    private const int ReaperPort = 8080;

    // The watchdog answers as soon as it is reachable; the timeout only matters when it never becomes reachable.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);

    private readonly TemporaryContainer _container;
    private readonly int _hostPort;
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly Task _monitor;
    private TcpClient _client;
    private bool _disposed;

    private ContainerReaper(TemporaryContainer container, int hostPort, TcpClient client, string sessionId)
    {
        _container = container;
        _hostPort = hostPort;
        _client = client;
        SessionId = sessionId;
        _monitor = MonitorConnectionAsync(_monitorCancellation.Token);
    }

    /// <summary>Gets the session the watchdog removes the resources of.</summary>
    public string SessionId { get; }

    /// <summary>Gets the id of the watchdog container.</summary>
    public string ContainerId => _container.Id;

    internal static async Task<ContainerReaper> StartAsync(ContainerRuntime runtime, ContainerReaperOptions options, string socketPath, CancellationToken cancellationToken)
    {
        var definition = new ContainerDefinition(options.Image)
        {
            Runtime = runtime,

            // The watchdog watches the session; being part of it would make it remove itself in the middle of its own
            // sweep. It still carries the other library labels, so a leftover watchdog is collected by a later cleanup.
            SessionOwned = false,
        };

        definition.Environment.Add("RYUK_PORT", ReaperPort.ToString(CultureInfo.InvariantCulture));
        definition.Environment.Add("RYUK_CONNECTION_TIMEOUT", FormatDuration(options.ConnectionTimeout));
        definition.Environment.Add("RYUK_RECONNECTION_TIMEOUT", FormatDuration(options.ReconnectionTimeout));
        definition.Mounts.AddBindMount(socketPath, "/var/run/docker.sock");

        // The watchdog reads its filters from whoever connects, and removes what they match with the daemon socket, so
        // its port is only published on the loopback address.
        definition.Ports.Add(new ContainerPort(ReaperPort));
        definition.WaitStrategies.Add(Wait.ForPort(ReaperPort));

        var container = definition.CreateContainer();
        try
        {
            await container.StartAsync(cancellationToken).ConfigureAwait(false);

            var hostPort = container.GetMappedPort(ReaperPort);
            var client = await ConnectAsync(hostPort, cancellationToken).ConfigureAwait(false);
            return new ContainerReaper(container, hostPort, client, SessionIdentity.Current.SessionId);
        }
        catch
        {
            await container.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Connects to the watchdog and registers the session, retrying until it answers.</summary>
    /// <remarks>The port forwarder of the runtime accepts connections as soon as the port is published, and resets them until the container is actually reachable, so a single attempt says nothing about the watchdog being up.</remarks>
    private static async Task<TcpClient> ConnectAsync(int port, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
                await SendFilterAsync(client, cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException && elapsed.Elapsed < HandshakeTimeout)
            {
                client.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    /// <summary>Registers the resources to remove. The watchdog reads one URL-encoded query string per line and answers each one with an acknowledgement.</summary>
    internal static async Task SendFilterAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(BuildFilter(SessionIdentity.Current.SessionId)), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read == 0)
            throw new IOException("The reaper closed the connection without answering.");

        var response = Encoding.UTF8.GetString(buffer, 0, read).Trim();
        if (!string.Equals(response, "ACK", StringComparison.Ordinal))
            throw new InvalidOperationException($"The reaper did not acknowledge the filter. It answered '{response}'.");
    }

    /// <summary>Builds the filter line that selects the resources of a session: <c>label=&lt;name&gt;=&lt;value&gt;</c>, with the label URL-encoded as a single query value.</summary>
    internal static string BuildFilter(string sessionId)
        => "label=" + Uri.EscapeDataString(ResourceLabels.SessionId + "=" + sessionId) + "\n";

    /// <summary>Watches the connection to the watchdog. The watchdog removes the resources of the session once the connection is gone for longer than its reconnection timeout, so a connection dropped while this process is alive (a restart of the port forwarder, a laptop that went to sleep) is opened again at once.</summary>
    private async Task MonitorConnectionAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // The watchdog sends nothing after its acknowledgement, so a read only returns when the connection ends.
                if (await _client.GetStream().ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
                    continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
            }

            try
            {
                var client = await ConnectAsync(_hostPort, cancellationToken).ConfigureAwait(false);
                var previous = _client;
                _client = client;
                previous.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                // The watchdog is gone for good (its container was removed): there is nothing left to reconnect to.
                return;
            }
        }
    }

    /// <summary>Formats a duration the way Go parses it, which is what the watchdog expects. Sub-second values are written in milliseconds so they do not end up as "0s".</summary>
    internal static string FormatDuration(TimeSpan value)
        => value.Ticks % TimeSpan.TicksPerSecond == 0
            ? ((long)value.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s"
            : ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";

    /// <summary>Simulates the death of this process for the watchdog, by closing the connection without reconnecting. Only the tests use it.</summary>
    internal async Task AbandonConnectionAsync()
    {
        await _monitorCancellation.CancelAsync().ConfigureAwait(false);
        await _monitor.ConfigureAwait(false);
        _client.Dispose();
    }

    /// <summary>Stops the watchdog. The watchdog container is removed first, so it cannot remove the resources that are still in use.</summary>
    /// <returns>A task that completes once the watchdog is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // The monitor is stopped first, and the connection is kept open until the watchdog is gone: the watchdog must
        // neither see this process leave nor a new connection while it is being removed.
        await _monitorCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await _monitor.ConfigureAwait(false);
        }
        catch
        {
            // The monitor only ever reconnects: a failure there cannot matter once the watchdog is stopped.
        }

        try
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup: ignore failures during disposal.
        }

        _client.Dispose();
        _monitorCancellation.Dispose();
    }
}
