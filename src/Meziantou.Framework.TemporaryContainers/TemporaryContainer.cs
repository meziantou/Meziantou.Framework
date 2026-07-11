namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary container created from a <see cref="ContainerDefinition"/>. Dispose the instance to remove the container (unless <see cref="ContainerDefinition.ReuseId"/> is set).</summary>
public partial class TemporaryContainer : IAsyncDisposable
{
    private readonly ContainerDefinition _definition;
    private ContainerRuntime? _runtime;
    private string? _id;
    private string? _name;
    private Dictionary<int, int>? _portMap;
    private bool _created;
    private bool _disposed;

    internal TemporaryContainer(ContainerDefinition definition)
    {
        _definition = definition;
    }

    /// <summary>Gets the container id.</summary>
    /// <exception cref="InvalidOperationException">The container has not been created yet.</exception>
    public string Id => _id ?? throw new InvalidOperationException("The container has not been created yet. Call StartAsync or EnsureCreatedAsync first.");

    /// <summary>Gets the container name.</summary>
    /// <exception cref="InvalidOperationException">The container has not been created yet.</exception>
    public string Name => _name ?? throw new InvalidOperationException("The container has not been created yet. Call StartAsync or EnsureCreatedAsync first.");

    /// <summary>Gets the definition owned by this container.</summary>
    public ContainerDefinition Definition => _definition;

    /// <summary>Gets the container runtime in use.</summary>
    public ContainerRuntime Runtime
    {
        get
        {
            EnsureRuntimeResolved();
            return _runtime!;
        }
    }

[MemberNotNull(nameof(_runtime))]
private void EnsureRuntimeResolved()
{
    if (_runtime is not null)
        return;

    _runtime = Internals.ContainerRuntimeResolver.Resolve(_definition.Runtime, _definition.Logging.Logger);
}

    /// <summary>Creates the container if it does not exist yet, without starting it. When <see cref="ContainerDefinition.ReuseId"/> is set, an existing matching container is adopted instead.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container exists.</returns>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_created)
            return;

        EnsureRuntimeResolved();

        _id = await Runtime.EnsureCreatedAsync(_definition, cancellationToken).ConfigureAwait(false);

        var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
        _name = info.Name;
        _created = true;
    }

    /// <summary>Creates the container if needed, starts it, refreshes the published-port mapping, and runs the registered wait strategies.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is started and ready.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await Runtime.StartAsync(Id, cancellationToken).ConfigureAwait(false);
        await RefreshPortsAsync(cancellationToken).ConfigureAwait(false);
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
        try
        {
            foreach (var strategy in _definition.WaitStrategies)
                await strategy.WaitAsync(this, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"The container was not ready within {_definition.StartupTimeout}."));
        }
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

    /// <summary>Removes the container unless <see cref="ContainerDefinition.ReuseId"/> is set. Cleanup is best-effort and never throws.</summary>
    /// <returns>A task that completes once cleanup finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_id is null || _definition.ReuseId is not null)
            return;

        try
        {
            await Runtime.DeleteAsync(_id, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup: ignore failures during disposal.
        }
    }

    private string RequireId()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _id ?? throw new InvalidOperationException("The container has not been created yet. Call StartAsync or EnsureCreatedAsync first.");
    }
}
