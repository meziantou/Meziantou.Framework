namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Network options for a container.</summary>
public sealed class ContainerNetworkOptions
{
    internal ContainerNetworkOptions()
    {
    }

    internal ContainerNetworkOptions(ContainerNetworkOptions other)
    {
        Network = other.Network;
        Alias = other.Alias;
    }

    /// <summary>Gets or sets the network the container connects to.</summary>
    public string? Network { get; set; }

    /// <summary>Gets or sets a network alias for the container.</summary>
    public string? Alias { get; set; }
}
