using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A collection of ports published by a container.</summary>
public sealed class ContainerPortCollection : IEnumerable<ContainerPort>
{
    private readonly List<ContainerPort> _ports;

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
    public void Add(int containerPort)
    {
        _ports.Add(new ContainerPort(containerPort));
    }

    /// <summary>Publishes a container port on a specific host port.</summary>
    /// <param name="hostPort">The host port to bind.</param>
    /// <param name="containerPort">The container port to publish.</param>
    public void Add(int hostPort, int containerPort)
    {
        _ports.Add(new ContainerPort(hostPort, containerPort));
    }

    /// <summary>Adds a port mapping.</summary>
    /// <param name="port">The port mapping.</param>
    public void Add(ContainerPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        _ports.Add(port);
    }

    /// <summary>Removes all mappings that publish the specified container port.</summary>
    /// <param name="containerPort">The container port to remove.</param>
    /// <returns><see langword="true"/> if at least one mapping was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(int containerPort)
    {
        return _ports.RemoveAll(p => p.Port == containerPort) > 0;
    }

    /// <summary>Returns an enumerator over the port mappings.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<ContainerPort> GetEnumerator() => _ports.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
