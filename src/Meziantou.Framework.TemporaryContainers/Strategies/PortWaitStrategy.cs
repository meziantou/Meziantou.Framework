using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

internal sealed class PortWaitStrategy(int containerPort) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        var hostPort = container.GetMappedPort(containerPort);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, hostPort, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
