using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

public partial class ContainerRuntime
{
    /// <summary>Removes the containers and volumes this library left behind, for instance by a run that was interrupted before it could dispose them.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The removed resources.</returns>
    /// <exception cref="InvalidOperationException">The runtime is not available.</exception>
    public Task<ContainerCleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
        => CleanupAsync(new ContainerCleanupOptions(), cancellationToken);

    /// <summary>Removes the containers and volumes this library left behind, for instance by a run that was interrupted before it could dispose them.</summary>
    /// <param name="options">What to remove. By default, only the resources whose creating process is gone.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The removed resources.</returns>
    /// <exception cref="InvalidOperationException">The runtime is not available.</exception>
    /// <remarks>The resources of the current process are never removed, whatever the scope: a cleanup at the beginning of a test run cannot break the containers that run is about to use.</remarks>
    public async Task<ContainerCleanupResult> CleanupAsync(ContainerCleanupOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await EnsureSupportedAsync(cancellationToken).ConfigureAwait(false);
        var runtime = await GetEffectiveRuntimeAsync(cancellationToken).ConfigureAwait(false);

        var removedContainers = new List<string>();
        var removedVolumes = new List<string>();
        var errors = new List<Exception>();
        var now = DateTimeOffset.UtcNow;

        if (options.IncludeContainers)
        {
            foreach (var container in await ListSafeAsync(runtime.ListManagedContainersAsync, errors, cancellationToken).ConfigureAwait(false))
            {
                if (!ShouldRemove(container, options, now))
                    continue;

                try
                {
                    await runtime.DeleteAsync(container.Id, cancellationToken).ConfigureAwait(false);
                    removedContainers.Add(container.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(ex);
                }
            }
        }

        // Volumes come last: a volume a container still references cannot be removed, and the containers that
        // referenced these volumes have just been removed.
        if (options.IncludeVolumes && runtime.SupportsVolumes)
        {
            foreach (var volume in await ListSafeAsync(runtime.ListManagedVolumesAsync, errors, cancellationToken).ConfigureAwait(false))
            {
                if (!ShouldRemove(volume, options, now))
                    continue;

                try
                {
                    await runtime.DeleteVolumeAsync(volume.Id, cancellationToken).ConfigureAwait(false);

                    // The runtimes report "still in use" the same way they report "already gone", so the outcome is
                    // confirmed rather than inferred from an exit code.
                    if (await runtime.VolumeExistsAsync(volume.Id, cancellationToken).ConfigureAwait(false))
                        errors.Add(new InvalidOperationException($"The volume '{volume.Id}' could not be removed. It is probably still used by a container."));
                    else
                        removedVolumes.Add(volume.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(ex);
                }
            }
        }

        return new ContainerCleanupResult(removedContainers, removedVolumes, errors);
    }

    /// <summary>Lists the containers created by this library, whichever process created them.</summary>
    internal virtual Task<IReadOnlyList<ManagedResource>> ListManagedContainersAsync(CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Lists the volumes created by this library, whichever process created them.</summary>
    internal virtual Task<IReadOnlyList<ManagedResource>> ListManagedVolumesAsync(CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual bool SupportsVolumes => true;

    /// <summary>Runs a listing without letting a failure on one kind of resource abort the whole cleanup.</summary>
    private static async Task<IReadOnlyList<ManagedResource>> ListSafeAsync(Func<CancellationToken, Task<IReadOnlyList<ManagedResource>>> list, List<Exception> errors, CancellationToken cancellationToken)
    {
        try
        {
            return await list(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(ex);
            return [];
        }
    }

    internal static bool ShouldRemove(ManagedResource resource, ContainerCleanupOptions options, DateTimeOffset now)
    {
        var labels = resource.Labels;
        if (!labels.ContainsKey(ResourceLabels.Managed))
            return false;

        // The containers of the current run are what the caller is about to use.
        if (labels.TryGetValue(ResourceLabels.SessionId, out var sessionId) && string.Equals(sessionId, SessionIdentity.Current.SessionId, StringComparison.Ordinal))
            return false;

        if (!options.IncludeReusedResources && labels.ContainsKey(ResourceLabels.ReuseId))
            return false;

        if (options.MinimumAge > TimeSpan.Zero)
        {
            if (!labels.TryGetValue(ResourceLabels.CreatedAt, out var createdAtText) ||
                !long.TryParse(createdAtText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var createdAtMilliseconds))
            {
                // An age that cannot be read is not an age above the threshold.
                return false;
            }

            if (now - DateTimeOffset.FromUnixTimeMilliseconds(createdAtMilliseconds) < options.MinimumAge)
                return false;
        }

        return options.Scope is ContainerCleanupScope.All || ResourceOwnership.IsOwnerGone(labels);
    }
}
