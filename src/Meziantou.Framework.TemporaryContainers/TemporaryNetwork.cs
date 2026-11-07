using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary network created from a <see cref="NetworkDefinition"/>. Dispose the instance to remove the network.</summary>
/// <remarks>Disposing only removes the network when this instance created it and <see cref="NetworkDefinition.ReuseId"/> is not set. A network that already existed when <see cref="EnsureCreatedAsync(CancellationToken)"/> ran is adopted and left behind, so pointing a definition at an existing network never destroys it.</remarks>
public sealed class TemporaryNetwork : IAsyncDisposable
{
    // A wedged daemon must not hang the teardown of a test run.
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);

    private readonly NetworkDefinition _definition;

    // Several containers can join the network and be started at the same time, so the creation is serialized: two
    // callers probing at once would otherwise both create it.
    private readonly SemaphoreSlim _creationLock = new(1, 1);

    // Recorded in the labels of the network. A runtime that answers the creation of an existing network with a success
    // does not say who created it, and the label does.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private ContainerRuntime? _runtime;
    private bool _created;
    private bool _owned;
    private bool _disposed;

    internal TemporaryNetwork(NetworkDefinition definition)
    {
        definition.MakeReadOnly();
        _definition = definition;

        // The containers that join the network refer to it by name, so it is resolved here rather than assigned by the runtime.
        Name = definition.Name
            ?? (definition.ReuseId is { } reuseId ? ResourceNaming.GetReuseName(reuseId) : ResourceNaming.GetRandomName());
    }

    /// <summary>Gets the network name.</summary>
    public string Name { get; }

    /// <summary>Gets the definition owned by this network. It is read-only.</summary>
    public NetworkDefinition Definition => _definition;

    /// <summary>Gets the container runtime in use.</summary>
    public ContainerRuntime Runtime => _runtime ??= _definition.Runtime;

    /// <summary>Creates the network if it does not exist yet. An existing network is adopted instead, and is not removed on dispose.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the network exists.</returns>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_created)
            return;

        await _creationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_created)
                return;

            _runtime ??= _definition.Runtime;
            await _runtime.EnsureSupportedAsync(cancellationToken).ConfigureAwait(false);

            // Probing first keeps creation idempotent on the runtimes that reject an existing name, and a network that
            // already exists belongs to somebody else, so dispose must not delete it.
            if (await _runtime.NetworkExistsAsync(Name, cancellationToken).ConfigureAwait(false))
            {
                _created = true;
                return;
            }

            try
            {
                await _runtime.CreateNetworkAsync(_definition, Name, _instanceId, cancellationToken).ConfigureAwait(false);
            }
            catch (ContainerRuntimeException)
            {
                // Another process created the network between the probe and the creation, and the runtime refused the
                // name. The network is the one to use, and it is not ours to remove.
                if (!await _runtime.NetworkExistsAsync(Name, cancellationToken).ConfigureAwait(false))
                    throw;

                _created = true;
                return;
            }

            // The runtimes that answer the creation of an existing network with a success leave its labels untouched, so
            // a network another process created in the meantime carries the instance of that process.
            var labels = await _runtime.GetNetworkLabelsAsync(Name, cancellationToken).ConfigureAwait(false);
            _owned = labels is null || !labels.TryGetValue(ResourceLabels.Instance, out var instanceId) || string.Equals(instanceId, _instanceId, StringComparison.Ordinal);
            _created = true;
        }
        finally
        {
            _creationLock.Release();
        }
    }

    /// <summary>Creates the network with the runtime of the container that joins it, so both always talk to the same engine.</summary>
    internal async Task EnsureCreatedAsync(ContainerRuntime containerRuntime, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A network created in one engine is invisible to another, and the container would fail with a "network not
        // found" error that does not say why. A network that was not pinned to a runtime follows the container instead
        // of resolving one of its own.
        if (_runtime is null && _definition.Runtime == ContainerRuntime.Auto)
            _runtime = containerRuntime;

        var networkRuntime = await Runtime.GetEffectiveRuntimeAsync(cancellationToken).ConfigureAwait(false);
        if (networkRuntime != containerRuntime)
            throw new InvalidOperationException($"The network '{Name}' uses the '{networkRuntime}' runtime, but the container uses '{containerRuntime}'. Both must use the same runtime.");

        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Determines whether the network exists.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if the network exists; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime ??= _definition.Runtime;
        await _runtime.EnsureSupportedAsync(cancellationToken).ConfigureAwait(false);
        return await _runtime.NetworkExistsAsync(Name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the network.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the network is removed.</returns>
    /// <exception cref="InvalidOperationException">The network could not be removed, for instance because a container still uses it.</exception>
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime ??= _definition.Runtime;
        await _runtime.EnsureSupportedAsync(cancellationToken).ConfigureAwait(false);
        await _runtime.DeleteNetworkAsync(Name, cancellationToken).ConfigureAwait(false);

        // The runtimes report "still in use" the same way they report "already gone", so the outcome is confirmed
        // rather than inferred from an exit code.
        if (await _runtime.NetworkExistsAsync(Name, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"The network '{Name}' could not be removed. It is probably still used by a container.");

        _created = false;
        _owned = false;
    }

    /// <summary>Removes the network when this instance created it and <see cref="NetworkDefinition.ReuseId"/> is not set. Cleanup is best-effort, bounded in time, and never throws.</summary>
    /// <returns>A task that completes once cleanup finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        using var cts = new CancellationTokenSource(CleanupTimeout);
        try
        {
            if (_owned && _definition.ReuseId is null)
                await Runtime.DeleteNetworkAsync(Name, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup: ignore failures during disposal.
        }
        finally
        {
            _creationLock.Dispose();
        }
    }
}
