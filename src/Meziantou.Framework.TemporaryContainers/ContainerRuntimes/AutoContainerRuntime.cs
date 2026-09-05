using System.Runtime.CompilerServices;
using System.Threading;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class AutoContainerRuntime : ContainerRuntime
{
    private ContainerRuntime? _resolvedRuntime;

    public AutoContainerRuntime()
        : base(nameof(Auto))
    {
    }

    public override async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        return await GetResolvedRuntimeOrNullAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    internal async Task<ContainerRuntime?> GetResolvedRuntimeOrNullAsync(CancellationToken cancellationToken)
    {
        // Only a success is cached, so a runtime that becomes available later is still detected.
        if (_resolvedRuntime is { } runtime)
            return runtime;

        foreach (var candidate in GetAllCandidates())
        {
            if (await candidate.IsSupportedAsync(cancellationToken).ConfigureAwait(false))
            {
                // Concurrent callers probe the candidates in the same order, so they resolve the same runtime; the
                // exchange only makes sure they all report the one that was published.
                return Interlocked.CompareExchange(ref _resolvedRuntime, candidate, comparand: null) ?? candidate;
            }
        }

        return null;
    }

    private async Task<ContainerRuntime> GetResolvedRuntimeOrThrowAsync(CancellationToken cancellationToken)
    {
        return await GetResolvedRuntimeOrNullAsync(cancellationToken).ConfigureAwait(false) ?? throw CreateUnavailableRuntimeException(this);
    }

    /// <summary>The runtime resolved by a previous operation. The container resolves the runtime before it runs anything, so the members that cannot await do not have to resolve it themselves.</summary>
    private ContainerRuntime ResolvedRuntime => _resolvedRuntime ?? throw CreateUnavailableRuntimeException(this);

    private static IEnumerable<ContainerRuntime> GetAllCandidates()
    {
        if (OperatingSystem.IsWindows())
            yield return Wslc;

        if (OperatingSystem.IsMacOS())
            yield return AppleContainer;

        yield return DockerApi;
        yield return Docker;
        yield return Podman;
    }

    internal override async Task<ContainerRuntime> GetEffectiveRuntimeAsync(CancellationToken cancellationToken)
        => await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);

    internal override bool SupportsPause => ResolvedRuntime.SupportsPause;

    internal override bool SupportsRestart => ResolvedRuntime.SupportsRestart;

    internal override async Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.EnsureCreatedAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task StartAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.StartAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task StopAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.StopAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task RestartAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.RestartAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task PauseAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.PauseAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task UnpauseAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.UnpauseAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task KillAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.KillAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.ExistsAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.InspectAsync(id, cancellationToken).ConfigureAwait(false);
    }

    internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var entry in runtime.GetLogsAsync(id, cancellationToken).ConfigureAwait(false))
            yield return entry;
    }

    internal override async Task<ExecResult> ExecAsync(string id, ExecOptions options, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.ExecAsync(id, options, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.OpenReadAsync(id, path, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.WriteFileAsync(id, path, content, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.CopyToContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.CopyFromContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
        => ResolvedRuntime.ResolvePortMap(info, definition);

    internal override async Task CreateVolumeAsync(VolumeDefinition definition, string name, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.CreateVolumeAsync(definition, name, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task DeleteVolumeAsync(string name, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        await runtime.DeleteVolumeAsync(name, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task<bool> VolumeExistsAsync(string name, CancellationToken cancellationToken)
    {
        var runtime = await GetResolvedRuntimeOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.VolumeExistsAsync(name, cancellationToken).ConfigureAwait(false);
    }
}
