using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Inspects the container and returns a snapshot of its current state.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The container information.</returns>
    public async Task<ContainerInfo> InspectAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        return await Runtime.InspectAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads what a start changed: the host ports, and the time the container started, which tells its current logs from the ones of an earlier run.</summary>
    private async Task RefreshStateAsync(CancellationToken cancellationToken)
    {
        var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
        var portMap = Runtime.ResolvePortMap(info, _definition);

        var daemonHost = portMap.Count > 0 ? await Runtime.GetRemoteDaemonHostAsync(cancellationToken).ConfigureAwait(false) : null;

        var map = new Dictionary<int, int>(portMap.Count);
        var hostAddresses = new Dictionary<int, string>(portMap.Count);
        foreach (var (containerPort, hostPort) in portMap)
        {
            map[containerPort] = hostPort;

            // A runtime that does not report the address of a binding created it on the address of the definition.
            var bindingAddress = info.PortHostIps.TryGetValue(containerPort, out var reported) ? reported : FindDefinedHostIp(containerPort);
            hostAddresses[containerPort] = HostAddress.Resolve(bindingAddress, daemonHost);
        }

        _portMap = map;
        _portHostAddresses = hostAddresses;
        _startedAt = info.StartedAt;
    }

    private string? FindDefinedHostIp(int containerPort)
    {
        foreach (var port in _definition.Ports)
        {
            if (port.Port == containerPort)
                return port.HostIp;
        }

        return null;
    }
}
