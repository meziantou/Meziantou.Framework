using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Describes how a network should be created. Configure an instance and call <see cref="CreateNetwork"/> to obtain a <see cref="TemporaryNetwork"/>.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../samples/Meziantou.Framework.TemporaryContainers.Samples/Examples.cs" ref:region="NetworkDefinition_Example" ref:dedent="true">
/// await using var network = new NetworkDefinition().CreateNetwork();
///
/// var redisDefinition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
/// redisDefinition.Network.Add(network, "redis");
/// await using var redis = redisDefinition.CreateContainer();
/// await redis.StartAsync();
///
/// // The second container reaches the first one by its alias.
/// var clientDefinition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
/// clientDefinition.Network.Add(network);
/// clientDefinition.Command.Add("redis-cli");
/// clientDefinition.Command.Add("-h");
/// clientDefinition.Command.Add("redis");
/// clientDefinition.Command.Add("ping");
/// await using var client = clientDefinition.CreateContainer();
/// await client.StartAsync();
/// </code>
/// </example>
public sealed class NetworkDefinition
{
    private bool _isReadOnly;

    /// <summary>Initializes a new instance of the <see cref="NetworkDefinition"/> class.</summary>
    public NetworkDefinition()
    {
        Labels = new ContainerLabelCollection();
    }

    /// <summary>Initializes a new instance of the <see cref="NetworkDefinition"/> class by deep-copying another definition. The copy can be changed even when <paramref name="other"/> belongs to a network.</summary>
    /// <param name="other">The definition to copy.</param>
    public NetworkDefinition(NetworkDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Runtime = other.Runtime;
        Name = other.Name;
        Driver = other.Driver;
        Internal = other.Internal;
        ReuseId = other.ReuseId;
        Labels = new ContainerLabelCollection(other.Labels);
        Identity = other.Identity;
    }

    /// <summary>Gets or sets the container runtime to use.</summary>
    public ContainerRuntime Runtime
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    } = ContainerRuntime.Auto;

    /// <summary>Gets or sets the network name. When <see langword="null"/>, a random name is generated, or a name derived from <see cref="ReuseId"/> when that is set.</summary>
    public string? Name
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the network driver. When <see langword="null"/>, the runtime uses its default driver: <c>bridge</c>, or <c>nat</c> on a daemon that runs Windows containers.</summary>
    /// <remarks>Apple's container runtime does not support network drivers.</remarks>
    public string? Driver
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the network is isolated from the outside: its containers reach each other, but not the internet.</summary>
    public bool Internal
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets an identifier used to reuse an existing network across runs. When set, the network is not removed on dispose.</summary>
    public string? ReuseId
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets the labels.</summary>
    public ContainerLabelCollection Labels { get; }

    /// <summary>Gets a value indicating whether the definition belongs to a network, in which case it can no longer be changed.</summary>
    public bool IsReadOnly => _isReadOnly;

    /// <summary>The run recorded in the labels of the network. Defaults to the current one; the tests use it to create a network that looks like the leftover of a run that is over.</summary>
    internal SessionIdentity? Identity { get; set; }

    /// <summary>Creates a <see cref="TemporaryNetwork"/> from a deep copy of this definition. Later changes to this definition do not affect the returned network, and the copy the network owns cannot be changed.</summary>
    /// <returns>A new network.</returns>
    public TemporaryNetwork CreateNetwork()
    {
        return new TemporaryNetwork(new NetworkDefinition(this));
    }

    internal void MakeReadOnly()
    {
        _isReadOnly = true;
        Labels.MakeReadOnly();
    }
}
