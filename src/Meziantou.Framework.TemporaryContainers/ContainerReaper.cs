using System.Diagnostics;
using System.Net.Sockets;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A watchdog container that removes the containers and volumes of the current session when this process goes away, including when it is killed and cannot run its own cleanup.</summary>
/// <remarks>Dispose the instance to stop the watchdog. A clean disposal removes the watchdog before it can remove anything, so the resources the process still owns are left to their own disposal.</remarks>
public sealed class ContainerReaper : IAsyncDisposable
{
    private const int ReaperPort = 8080;

    // The watchdog answers as soon as it is reachable; the timeout only matters when it never becomes reachable.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);

    private readonly TemporaryContainer _container;
    private readonly TcpClient _client;
    private bool _disposed;

    private ContainerReaper(TemporaryContainer container, TcpClient client, string sessionId)
    {
        _container = container;
        _client = client;
        SessionId = sessionId;
    }

    /// <summary>Gets the session the watchdog removes the resources of.</summary>
    public string SessionId { get; }

    /// <summary>Gets the id of the watchdog container.</summary>
    public string ContainerId => _container.Id;

    internal static async Task<ContainerReaper> StartAsync(ContainerRuntime runtime, ContainerReaperOptions options, CancellationToken cancellationToken)
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
        definition.Mounts.AddBindMount(options.SocketPath ?? GetDefaultSocketPath(), "/var/run/docker.sock");
        definition.Ports.Add(new ContainerPort(ReaperPort));
        definition.WaitStrategies.Add(Wait.ForPort(ReaperPort));

        var container = definition.CreateContainer();
        try
        {
            await container.StartAsync(cancellationToken).ConfigureAwait(false);

            var client = await ConnectAsync(container.GetMappedPort(ReaperPort), cancellationToken).ConfigureAwait(false);
            return new ContainerReaper(container, client, SessionIdentity.Current.SessionId);
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
    private static async Task SendFilterAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var filter = "label=" + Uri.EscapeDataString(ResourceLabels.SessionId + "=" + SessionIdentity.Current.SessionId) + "\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(filter), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read == 0)
            throw new IOException("The reaper closed the connection without answering.");

        var response = Encoding.UTF8.GetString(buffer, 0, read).Trim();
        if (!string.Equals(response, "ACK", StringComparison.Ordinal))
            throw new InvalidOperationException($"The reaper did not acknowledge the filter. It answered '{response}'.");
    }

    private static string GetDefaultSocketPath()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (dockerHost is not null && dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            var path = dockerHost["unix://".Length..];
            if (!string.IsNullOrWhiteSpace(path))
                return Uri.UnescapeDataString(path);
        }

        // Docker Desktop for Windows exposes the Linux socket at the same path; the leading slash keeps the CLI from
        // reading it as a Windows path.
        return OperatingSystem.IsWindows() ? "//var/run/docker.sock" : "/var/run/docker.sock";
    }

    /// <summary>Formats a duration the way Go parses it, which is what the watchdog expects. Sub-second values are written in milliseconds so they do not end up as "0s".</summary>
    private static string FormatDuration(TimeSpan value)
        => value.Ticks % TimeSpan.TicksPerSecond == 0
            ? ((long)value.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s"
            : ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";

    /// <summary>Stops the watchdog. The watchdog container is removed first, so it cannot remove the resources that are still in use.</summary>
    /// <returns>A task that completes once the watchdog is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup: ignore failures during disposal.
        }

        _client.Dispose();
    }
}
