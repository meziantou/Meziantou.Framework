using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Network options for a container.</summary>
/// <remarks>A container joins <see cref="Network"/> first, when it is set, then every network added with <see cref="Add(string, string[])"/> or <see cref="Add(TemporaryNetwork, string[])"/>, in order.</remarks>
public sealed class ContainerNetworkOptions
{
    private readonly List<ContainerNetworkAttachment> _attachments;
    private bool _isReadOnly;

    internal ContainerNetworkOptions()
    {
        _attachments = [];
    }

    internal ContainerNetworkOptions(ContainerNetworkOptions other)
    {
        Network = other.Network;
        Alias = other.Alias;
        _attachments = [.. other._attachments];
    }

    /// <summary>Gets or sets the network the container connects to first. It can also be a network mode, such as <c>host</c> or <c>none</c>.</summary>
    public string? Network
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets a network alias for the container on <see cref="Network"/>, or on the default network when <see cref="Network"/> is not set.</summary>
    public string? Alias
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets the networks added with <see cref="Add(string, string[])"/> and <see cref="Add(TemporaryNetwork, string[])"/>.</summary>
    public IReadOnlyList<ContainerNetworkAttachment> Attachments => _attachments;

    /// <summary>Connects the container to an existing network.</summary>
    /// <param name="network">The name of the network.</param>
    /// <param name="aliases">The aliases the other containers of the network reach the container by.</param>
    /// <exception cref="ArgumentException">The container already joins the network.</exception>
    public void Add(string network, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);
        AddCore(network, aliases, temporaryNetwork: null);
    }

    /// <summary>Connects the container to a <see cref="TemporaryNetwork"/>. The network is created, if needed, when the container is created.</summary>
    /// <param name="network">The network.</param>
    /// <param name="aliases">The aliases the other containers of the network reach the container by.</param>
    /// <exception cref="ArgumentException">The container already joins the network.</exception>
    public void Add(TemporaryNetwork network, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(network);
        AddCore(network.Name, aliases, network);
    }

    /// <summary>Disconnects the container from a network added with <see cref="Add(string, string[])"/> or <see cref="Add(TemporaryNetwork, string[])"/>.</summary>
    /// <param name="network">The name of the network.</param>
    /// <returns><see langword="true"/> if the network was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string network)
    {
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        return _attachments.RemoveAll(attachment => string.Equals(attachment.Network, network, StringComparison.Ordinal)) > 0;
    }

    internal void MakeReadOnly() => _isReadOnly = true;

    /// <summary>Splits the networks between the one the container is created on and the ones it is connected to afterwards. The runtimes cannot create a container on several networks at once, or only recent versions of them can.</summary>
    internal (NetworkEndpoint? Primary, IReadOnlyList<ContainerNetworkAttachment> Additional) GetNetworkPlan()
    {
        if (Network is not null || Alias is not null)
            return (new NetworkEndpoint(Network, Alias is null ? [] : [Alias]), _attachments);

        if (_attachments.Count == 0)
            return (null, []);

        return (new NetworkEndpoint(_attachments[0].Network, _attachments[0].Aliases), _attachments.GetRange(1, _attachments.Count - 1));
    }

    private void AddCore(string network, string[] aliases, TemporaryNetwork? temporaryNetwork)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        DefinitionReadOnly.ThrowIf(_isReadOnly);

        foreach (var alias in aliases)
            ArgumentException.ThrowIfNullOrWhiteSpace(alias, nameof(aliases));

        // The runtimes reject a second connection to the same network; the aliases of both belong in one call.
        if (string.Equals(network, Network, StringComparison.Ordinal) || _attachments.Exists(attachment => string.Equals(attachment.Network, network, StringComparison.Ordinal)))
            throw new ArgumentException($"The container already joins the network '{network}'. Pass all its aliases in a single call.", nameof(network));

        _attachments.Add(new ContainerNetworkAttachment(network, [.. aliases], temporaryNetwork));
    }
}

