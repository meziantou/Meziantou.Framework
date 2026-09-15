using System.Collections;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A collection of ports published by a container. A container port is published on a single host port.</summary>
public sealed class ContainerPortCollection : IEnumerable<ContainerPort>
{
    private readonly List<ContainerPort> _ports;
    private bool _isReadOnly;

    internal ContainerPortCollection()
    {
        _ports = [];
    }

    internal ContainerPortCollection(ContainerPortCollection other)
    {
        _ports = [.. other._ports];
    }

    /// <summary>Gets the number of ports in the collection.</summary>
    public int Count => _ports.Count;

    /// <summary>Publishes a container port on a random host port.</summary>
    /// <param name="containerPort">The container port to publish.</param>
    /// <exception cref="ArgumentException">The container port is already published.</exception>
    public void Add(int containerPort)
    {
        AddCore(new ContainerPort(containerPort));
    }

    /// <summary>Publishes a container port on a specific host port.</summary>
    /// <param name="hostPort">The host port to bind.</param>
    /// <param name="containerPort">The container port to publish.</param>
    /// <exception cref="ArgumentException">The container port is already published.</exception>
    public void Add(int hostPort, int containerPort)
    {
        AddCore(new ContainerPort(hostPort, containerPort));
    }

    /// <summary>Adds a port mapping.</summary>
    /// <param name="port">The port mapping.</param>
    /// <exception cref="ArgumentException">The container port is already published.</exception>
    public void Add(ContainerPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        AddCore(port);
    }

    /// <summary>Removes the mapping that publishes the specified container port.</summary>
    /// <param name="containerPort">The container port to remove.</param>
    /// <returns><see langword="true"/> if the mapping was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(int containerPort)
    {
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        return _ports.RemoveAll(p => p.Port == containerPort) > 0;
    }

    /// <summary>Returns an enumerator over the port mappings.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<ContainerPort> GetEnumerator() => _ports.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void MakeReadOnly() => _isReadOnly = true;

    private void AddCore(ContainerPort port)
    {
        DefinitionReadOnly.ThrowIf(_isReadOnly);

        // The runtimes disagree on a container port published twice (one publishes both, one keeps the last, one keeps
        // the first), and GetMappedPort could only report one of them anyway.
        if (_ports.Exists(existing => existing.Port == port.Port))
            throw new ArgumentException($"The container port {port.Port.ToString(CultureInfo.InvariantCulture)} is already published. Remove the existing mapping first.", nameof(port));

        _ports.Add(port);
    }
}
