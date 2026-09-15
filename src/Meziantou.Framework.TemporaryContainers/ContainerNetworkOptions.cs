using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Network options for a container.</summary>
public sealed class ContainerNetworkOptions
{
    private bool _isReadOnly;

    internal ContainerNetworkOptions()
    {
    }

    internal ContainerNetworkOptions(ContainerNetworkOptions other)
    {
        Network = other.Network;
        Alias = other.Alias;
    }

    /// <summary>Gets or sets the network the container connects to.</summary>
    public string? Network
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets a network alias for the container.</summary>
    public string? Alias
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    internal void MakeReadOnly() => _isReadOnly = true;
}
