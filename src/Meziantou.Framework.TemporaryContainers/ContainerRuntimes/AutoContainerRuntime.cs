using System.Threading;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class AutoContainerRuntime : ContainerRuntime
{
    private readonly Lock _syncObject = new();
    private readonly DockerApiRuntime _dockerApiRuntime = new();
    private ContainerRuntime? _resolvedRuntime;

    public AutoContainerRuntime()
        : base(nameof(Auto))
    {
    }

    public override bool IsSupported()
    {
        return GetResolvedRuntimeOrNull() is not null;
    }

    internal ContainerRuntime? GetResolvedRuntimeOrNull()
    {
        if (_resolvedRuntime is { } runtime)
            return runtime;

        lock (_syncObject)
        {
            if (_resolvedRuntime is { } resolved)
                return resolved;

            if (_dockerApiRuntime.IsSupported())
                return _resolvedRuntime = _dockerApiRuntime;

            foreach (var candidate in GetCliCandidates())
            {
                if (candidate.IsSupported())
                    return _resolvedRuntime = candidate;
            }

            return null;
        }
    }

    private ContainerRuntime GetResolvedRuntimeOrThrow()
    {
        return GetResolvedRuntimeOrNull() ?? throw CreateUnavailableRuntimeException(this);
    }

    private static IEnumerable<ContainerRuntime> GetCliCandidates()
    {
        yield return Docker;
        yield return Podman;

        if (OperatingSystem.IsMacOS())
            yield return AppleContainer;

        if (OperatingSystem.IsWindows())
            yield return Wslc;
    }

    internal override bool SupportsPause => GetResolvedRuntimeOrThrow().SupportsPause;

    internal override bool SupportsRestart => GetResolvedRuntimeOrThrow().SupportsRestart;

    internal override Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().EnsureCreatedAsync(definition, cancellationToken);

    internal override Task StartAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().StartAsync(id, cancellationToken);

    internal override Task StopAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().StopAsync(id, cancellationToken);

    internal override Task RestartAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().RestartAsync(id, cancellationToken);

    internal override Task PauseAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().PauseAsync(id, cancellationToken);

    internal override Task UnpauseAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().UnpauseAsync(id, cancellationToken);

    internal override Task KillAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().KillAsync(id, cancellationToken);

    internal override Task DeleteAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().DeleteAsync(id, cancellationToken);

    internal override Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().ExistsAsync(id, cancellationToken);

    internal override Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().InspectAsync(id, cancellationToken);

    internal override IAsyncEnumerable<LogEntry> GetLogsAsync(string id, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().GetLogsAsync(id, cancellationToken);

    internal override Task<ExecResult> ExecAsync(string id, ExecOptions options, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().ExecAsync(id, options, cancellationToken);

    internal override Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().OpenReadAsync(id, path, cancellationToken);

    internal override Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().WriteFileAsync(id, path, content, cancellationToken);

    internal override Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().CopyToContainerAsync(id, source, destination, cancellationToken);

    internal override Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
        => GetResolvedRuntimeOrThrow().CopyFromContainerAsync(id, source, destination, cancellationToken);

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
        => GetResolvedRuntimeOrThrow().ResolvePortMap(info, definition);
}
