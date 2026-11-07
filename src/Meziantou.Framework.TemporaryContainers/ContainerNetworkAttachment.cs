namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A network a container joins, with the aliases the other containers of that network reach it by.</summary>
public sealed class ContainerNetworkAttachment
{
    internal ContainerNetworkAttachment(string network, IReadOnlyList<string> aliases, TemporaryNetwork? temporaryNetwork)
    {
        Network = network;
        Aliases = aliases;
        TemporaryNetwork = temporaryNetwork;
    }

    /// <summary>Gets the name of the network.</summary>
    public string Network { get; }

    /// <summary>Gets the aliases of the container on the network.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Gets the network the library creates when the container is created, or <see langword="null"/> when the network is an existing one.</summary>
    public TemporaryNetwork? TemporaryNetwork { get; }
}
