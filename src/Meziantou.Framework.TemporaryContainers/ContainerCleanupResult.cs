namespace Meziantou.Framework.TemporaryContainers;

/// <summary>What a cleanup removed, and what it could not remove.</summary>
public sealed class ContainerCleanupResult
{
    internal ContainerCleanupResult(IReadOnlyList<string> removedContainers, IReadOnlyList<string> removedVolumes, IReadOnlyList<Exception> errors)
    {
        RemovedContainers = removedContainers;
        RemovedVolumes = removedVolumes;
        Errors = errors;
    }

    /// <summary>Gets the ids of the removed containers.</summary>
    public IReadOnlyList<string> RemovedContainers { get; }

    /// <summary>Gets the names of the removed volumes.</summary>
    public IReadOnlyList<string> RemovedVolumes { get; }

    /// <summary>Gets the failures met while removing the resources. A cleanup does not stop on the first failure, so a resource that cannot be removed does not keep the others alive.</summary>
    public IReadOnlyList<Exception> Errors { get; }

    /// <summary>Gets the total number of removed resources.</summary>
    public int RemovedCount => RemovedContainers.Count + RemovedVolumes.Count;
}
