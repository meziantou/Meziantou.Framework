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

        var map = new Dictionary<int, int>(portMap.Count);
        foreach (var (containerPort, hostPort) in portMap)
            map[containerPort] = hostPort;

        _portMap = map;
        _startedAt = info.StartedAt;
    }
}
