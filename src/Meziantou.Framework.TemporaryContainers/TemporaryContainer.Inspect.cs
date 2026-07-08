namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Inspects the container and returns a snapshot of its current state.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The container information.</returns>
    public async Task<ContainerInfo> InspectAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        var result = await Cli.RunBufferedAsync(Adapter.BuildInspectArguments(id), cancellationToken).ConfigureAwait(false);
        return Adapter.ParseInspect(result.StandardOutput);
    }

    private async Task RefreshPortsAsync(CancellationToken cancellationToken)
    {
        var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
        var portMap = Adapter.ResolvePortMap(info, _definition);

        var map = new Dictionary<int, int>(portMap.Count);
        foreach (var (containerPort, hostPort) in portMap)
            map[containerPort] = hostPort;

        _portMap = map;
    }
}
