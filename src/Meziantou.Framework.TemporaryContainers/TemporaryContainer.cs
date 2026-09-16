using System.Text;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary container created from a <see cref="ContainerDefinition"/>. Dispose the instance to remove the container (unless <see cref="ContainerDefinition.ReuseId"/> is set).</summary>
public partial class TemporaryContainer : IAsyncDisposable
{
    // A wedged daemon must not hang the teardown of a test run: the cleanup done by a dispose has a deadline of its own,
    // which nothing the caller does can cancel.
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);

    // Describing a startup failure must not take as long as the startup it describes.
    private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(10);

    private const int MaxReportedLogLines = 20;
    private const int MaxHostPortConflictAttempts = 3;

    private readonly ContainerDefinition _definition;
    private readonly bool _hasGeneratedName;
    private ContainerRuntime? _runtime;
    private string? _id;
    private string? _name;
    private string? _builtImage;
    private Dictionary<int, int>? _portMap;
    private DateTimeOffset? _startedAt;
    private int _logEntriesToSkip;
    private CancellationTokenSource? _forwardLogsCancellationTokenSource;
    private Task? _forwardLogsTask;
    private bool _created;
    private bool _disposed;

    internal TemporaryContainer(ContainerDefinition definition)
    {
        // A container the runtime created while the request failed (a cancellation that arrives while the daemon creates
        // it) has no id anyone knows, so it is named up front: the name is what finds it and removes it.
        if (definition.Name is null && definition.ReuseId is null)
        {
            definition.AssignGeneratedName(ResourceNaming.GetRandomName());
            _hasGeneratedName = true;
        }

        definition.MakeReadOnly();
        _definition = definition;
    }

    /// <summary>Gets the container id.</summary>
    /// <exception cref="InvalidOperationException">The container has not been created yet.</exception>
    public string Id => _id ?? throw new InvalidOperationException("The container has not been created yet. Call StartAsync or EnsureCreatedAsync first.");

    /// <summary>Gets the container name.</summary>
    /// <exception cref="InvalidOperationException">The container has not been created yet.</exception>
    public string Name => _name ?? throw new InvalidOperationException("The container has not been created yet. Call StartAsync or EnsureCreatedAsync first.");

    /// <summary>Gets the definition owned by this container. It is read-only: the container reads it at every step of its life.</summary>
    public ContainerDefinition Definition => _definition;

    /// <summary>Gets the container runtime in use.</summary>
    public ContainerRuntime Runtime => _runtime ??= _definition.Runtime;

    /// <summary>Resolves the runtime and checks that it can actually be used, which may run a command or open a connection.</summary>
    private async Task EnsureRuntimeSupportedAsync(CancellationToken cancellationToken)
    {
        _runtime ??= _definition.Runtime;
        await _runtime.EnsureSupportedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates the container if it does not exist yet, without starting it. When <see cref="ContainerDefinition.ReuseId"/> is set, an existing matching container is adopted instead.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container exists.</returns>
    /// <exception cref="InvalidOperationException">The container found through <see cref="ContainerDefinition.ReuseId"/> was created from a different definition.</exception>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_created)
            return;

        await EnsureRuntimeSupportedAsync(cancellationToken).ConfigureAwait(false);
        await EnsureOwnedVolumesCreatedAsync(cancellationToken).ConfigureAwait(false);

        // A previous attempt may have created the container and failed afterwards: that container is the one to use,
        // rather than a second one whose id would replace the first and leave it behind.
        _id ??= await CreateContainerAsync(cancellationToken).ConfigureAwait(false);

        var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (_definition.ReuseId is not null)
            AdoptReusedContainer(info);

        _name = info.Name;
        if (_definition.Image is DockerfileImage && info.Image is { } image && image.Contains(ResourceNaming.BuiltImagePrefix, StringComparison.Ordinal))
            _builtImage = image;

        _created = true;

        if (_definition.ReuseId is not null && info.State is ContainerState.Running)
        {
            _startedAt = info.StartedAt;
            StartForwardingLogs();
        }
    }

    private async Task<string> CreateContainerAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Runtime.EnsureCreatedAsync(_definition, cancellationToken).ConfigureAwait(false);
        }
        catch when (_hasGeneratedName && _definition.Name is { } name)
        {
            await DeleteQuietlyAsync(name).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Checks that the container found through the reuse identifier was created from this definition, and reads back the credentials it was created with.</summary>
    private void AdoptReusedContainer(ContainerInfo info)
    {
        // A container created by an earlier version of the library has no hash, and is adopted as it always was.
        if (info.Labels.TryGetValue(ResourceLabels.ConfigurationHash, out var hash) && !string.Equals(hash, _definition.ConfigurationHash, StringComparison.Ordinal))
        {
            _id = null;
            throw new InvalidOperationException($"The container '{info.Name}' reused through '{_definition.ReuseId}' was created from a different definition, so it cannot serve this one. Remove it (ContainerRuntime.CleanupAsync with IncludeReusedResources) or use another ReuseId.");
        }

        // The credentials the database helpers generate differ from one process to the next, and the container only
        // knows the ones of the process that created it.
        _definition.AdoptGeneratedEnvironmentValues(info.Environment);
    }

    /// <summary>Creates the container if needed, starts it, refreshes the published-port mapping, and runs the registered wait strategies.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is started and ready.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var attempt = 1; ; attempt++)
        {
            await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await PrepareLogsForStartAsync(restarting: false, cancellationToken).ConfigureAwait(false);
            try
            {
                await Runtime.StartAsync(Id, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (attempt < MaxHostPortConflictAttempts && _definition.ReuseId is null && _definition.Ports.Any(static port => port.HostPort is null) && Runtime.IsHostPortConflict(ex))
            {
                // The runtime picked the random host ports when it created the container, and another process took one
                // of them before the container started. A new container gets new ones.
                await DeleteCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var previousStart = _startedAt;
        await RefreshStateAsync(cancellationToken).ConfigureAwait(false);

        // A container that actually started again (rather than one that was already running) begins a new run: the pump
        // of the earlier run, which may not have noticed yet that the container exited, is replaced.
        if (_startedAt != previousStart)
            await StopForwardingLogsAsync().ConfigureAwait(false);

        StartForwardingLogs();
        await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the registered wait strategies, bounded by <see cref="ContainerDefinition.StartupTimeout"/>.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once every wait strategy has completed.</returns>
    /// <exception cref="TimeoutException">The container was not ready within <see cref="ContainerDefinition.StartupTimeout"/>.</exception>
    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_definition.WaitStrategies.Count == 0)
            return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_definition.StartupTimeout);
        foreach (var strategy in _definition.WaitStrategies)
        {
            try
            {
                await strategy.WaitAsync(this, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Only the startup timeout is reported as one: a strategy that gives up on its own (an HTTP client with a
                // timeout of its own, for instance) reports its own failure.
                throw new TimeoutException(await BuildStartupTimeoutMessageAsync(strategy).ConfigureAwait(false), ex);
            }
        }
    }

    /// <summary>Describes a startup that timed out with what explains it: the strategy that did not complete, the state of the container, and what it printed.</summary>
    private async Task<string> BuildStartupTimeoutMessageAsync(IWaitStrategy strategy)
    {
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The container was not ready within {_definition.StartupTimeout}: the wait strategy '{strategy}' did not complete.");

        using var cts = new CancellationTokenSource(DiagnosticsTimeout);
        try
        {
            var info = await InspectAsync(cts.Token).ConfigureAwait(false);
            message.Append(CultureInfo.InvariantCulture, $" The container is {info.State}");
            if (!string.IsNullOrWhiteSpace(info.Status))
                message.Append(CultureInfo.InvariantCulture, $" ({info.Status})");

            message.Append('.');

            var tail = new Queue<string>(MaxReportedLogLines);
            await foreach (var entry in GetCurrentRunLogsAsync(follow: false, cts.Token).ConfigureAwait(false))
            {
                if (tail.Count == MaxReportedLogLines)
                    tail.Dequeue();

                tail.Enqueue(entry.Stream is LogStream.Stderr ? "[stderr] " + entry.Message : entry.Message);
            }

            if (tail.Count == 0)
            {
                message.Append(" The container did not write anything to its log streams.");
            }
            else
            {
                message.Append(CultureInfo.InvariantCulture, $" Last {tail.Count} log line(s):");
                foreach (var line in tail)
                    message.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}  {line}");
            }
        }
        catch (Exception ex)
        {
            // The runtime did not answer: the timeout is still the failure to report.
            message.Append(" The state of the container could not be read: ").Append(ex.Message);
        }

        return message.ToString();
    }

    /// <summary>Gets the host port mapped to the specified container port.</summary>
    /// <param name="containerPort">The container port.</param>
    /// <returns>The host port.</returns>
    /// <exception cref="InvalidOperationException">The port mapping is not available (the container has not been started) or the port is not published.</exception>
    public int GetMappedPort(int containerPort)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_portMap is null)
            throw new InvalidOperationException("The port mapping is not available. Call StartAsync first.");

        if (_portMap.TryGetValue(containerPort, out var hostPort))
            return hostPort;

        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Container port {containerPort} is not published."));
    }

    /// <summary>Removes the container, and the image built for it, unless <see cref="ContainerDefinition.ReuseId"/> is set. Cleanup is best-effort, bounded in time, and never throws.</summary>
    /// <returns>A task that completes once cleanup finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        using var cts = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await StopForwardingLogsAsync().ConfigureAwait(false);

            if (_id is not null && _definition.ReuseId is null)
            {
                await Runtime.DeleteAsync(_id, cts.Token).ConfigureAwait(false);
                await DeleteBuiltImageAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort cleanup: ignore failures during disposal.
        }
    }

    /// <summary>Removes a container known by name only, without ever throwing: it only runs on a path that already failed.</summary>
    private async Task DeleteQuietlyAsync(string name)
    {
        using var cts = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await Runtime.DeleteAsync(name, cts.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    /// <summary>Removes the image built from a Dockerfile for this container. The container is removed first, since a runtime refuses to remove an image a container still uses.</summary>
    private async Task DeleteBuiltImageAsync(CancellationToken cancellationToken)
    {
        if (_builtImage is not { } image || !Runtime.SupportsImageCleanup)
            return;

        await Runtime.DeleteImageAsync(image, cancellationToken).ConfigureAwait(false);
        _builtImage = null;
    }

    /// <summary>Creates the volumes added through <see cref="ContainerMountCollection.AddVolume(TemporaryVolume, string, bool)"/>, so a mount never silently resolves to an empty volume the runtime auto-created.</summary>
    private async Task EnsureOwnedVolumesCreatedAsync(CancellationToken cancellationToken)
    {
        ContainerRuntime? effectiveRuntime = null;
        foreach (var mount in _definition.Mounts)
        {
            if (mount is not OwnedVolumeMount owned)
                continue;

            effectiveRuntime ??= await Runtime.GetEffectiveRuntimeAsync(cancellationToken).ConfigureAwait(false);
            await owned.Volume.EnsureCreatedAsync(effectiveRuntime, cancellationToken).ConfigureAwait(false);
        }
    }

    private string RequireId()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _id ?? throw new InvalidOperationException("The container has not been created yet. Call StartAsync or EnsureCreatedAsync first.");
    }
}
