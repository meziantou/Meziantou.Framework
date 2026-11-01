using System.Net.Sockets;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

internal sealed class PortWaitStrategy(int containerPort) : IWaitStrategy
{
    // The port forwarders of the runtimes (docker-proxy, Docker Desktop) accept a connection as soon as the port is
    // published, then close or reset it once they fail to reach the container. A connection still open after this
    // delay reached something that listens.
    internal static readonly TimeSpan DefaultProbeDuration = TimeSpan.FromMilliseconds(500);

    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        // The address of the binding, or the host of a remote daemon: a port published elsewhere than the loopback
        // address is not reachable there.
        var host = container.GetHostAddress(containerPort);
        var hostPort = container.GetMappedPort(containerPort);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsListeningAsync(host, hostPort, DefaultProbeDuration, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Determines whether a listener accepts connections on a port, rather than a forwarder that accepts them on its behalf.</summary>
    internal static async Task<bool> IsListeningAsync(string host, int port, TimeSpan probeDuration, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(probeDuration);
            try
            {
                // A service that speaks first (a banner) is up. A connection closed without a byte is the forwarder
                // giving up on a container that does not listen yet.
                var buffer = new byte[1];
                return await client.GetStream().ReadAsync(buffer, probeCts.Token).ConfigureAwait(false) > 0;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Still open, and nothing sent: a service waiting for its client to speak.
                return true;
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return false;
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"port {containerPort}");
}
