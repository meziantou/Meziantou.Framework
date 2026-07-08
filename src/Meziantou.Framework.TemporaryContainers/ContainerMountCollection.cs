using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A collection of mounts attached to a container.</summary>
public sealed class ContainerMountCollection : IEnumerable<IMount>
{
    private readonly List<IMount> _mounts;

    internal ContainerMountCollection()
    {
        _mounts = [];
    }

    internal ContainerMountCollection(ContainerMountCollection other)
    {
        _mounts = [.. other._mounts];
    }

    /// <summary>Gets the number of mounts in the collection.</summary>
    public int Count => _mounts.Count;

    /// <summary>Adds a mount.</summary>
    /// <param name="mount">The mount to add.</param>
    public void Add(IMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        _mounts.Add(mount);
    }

    /// <summary>Adds a bind mount that maps a host path into the container.</summary>
    /// <param name="hostPath">The path on the host.</param>
    /// <param name="containerPath">The path inside the container.</param>
    /// <param name="readOnly">Whether the mount is read-only.</param>
    public void AddBindMount(string hostPath, string containerPath, bool readOnly = false)
    {
        _mounts.Add(new BindMount(hostPath, containerPath, readOnly));
    }

    /// <summary>Adds a named volume mount.</summary>
    /// <param name="volumeName">The volume name.</param>
    /// <param name="containerPath">The path inside the container.</param>
    public void AddVolume(string volumeName, string containerPath)
    {
        _mounts.Add(new VolumeMount(volumeName, containerPath));
    }

    /// <summary>Adds a tmpfs (in-memory) mount.</summary>
    /// <param name="containerPath">The path inside the container.</param>
    public void AddTmpfs(string containerPath)
    {
        _mounts.Add(new TmpfsMount(containerPath));
    }

    /// <summary>Returns an enumerator over the mounts.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<IMount> GetEnumerator() => _mounts.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
