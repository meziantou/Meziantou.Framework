using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Describes how a container should be created. Configure an instance and call <see cref="CreateContainer"/> to obtain a runnable <see cref="TemporaryContainer"/>.</summary>
/// <example>
/// <code>
/// var definition = new ContainerDefinition(new RegistryImage("redis:8"));
/// definition.Environment.Add("ALLOW_EMPTY_PASSWORD", "yes");
/// definition.Ports.Add(new ContainerPort(6379));
/// definition.WaitStrategies.Add(Wait.ForPort(6379));
///
/// await using var container = definition.CreateContainer();
/// await container.StartAsync();
/// </code>
/// </example>
public class ContainerDefinition
{
    private readonly Dictionary<string, string> _generatedEnvironmentValues;
    private string? _name;
    private bool _hasGeneratedName;
    private bool _isReadOnly;

    /// <summary>Initializes a new instance of the <see cref="ContainerDefinition"/> class.</summary>
    /// <param name="image">The image the container is created from.</param>
    public ContainerDefinition(ImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image = image;
        Entrypoint = new ContainerCommandCollection();
        Command = new ContainerCommandCollection();
        Environment = new ContainerEnvironmentCollection();
        Labels = new ContainerLabelCollection();
        Ports = new ContainerPortCollection();
        Mounts = new ContainerMountCollection();
        WaitStrategies = new ContainerWaitStrategyCollection();
        Network = new ContainerNetworkOptions();
        Resources = new ContainerResourceOptions();
        Logging = new ContainerLoggingOptions();
        _generatedEnvironmentValues = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Initializes a new instance of the <see cref="ContainerDefinition"/> class by deep-copying another definition. The copy can be changed even when <paramref name="other"/> belongs to a container.</summary>
    /// <param name="other">The definition to copy.</param>
    public ContainerDefinition(ContainerDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Image = other.Image;
        Runtime = other.Runtime;
        PullPolicy = other.PullPolicy;

        // The name a container was given by the library belongs to that container: a copy that kept it would create a
        // second container with the name of the first.
        _name = other._hasGeneratedName ? null : other._name;
        ReuseId = other.ReuseId;
        Hostname = other.Hostname;
        User = other.User;
        WorkingDirectory = other.WorkingDirectory;
        StartupTimeout = other.StartupTimeout;
        Entrypoint = new ContainerCommandCollection(other.Entrypoint);
        Command = new ContainerCommandCollection(other.Command);
        Environment = new ContainerEnvironmentCollection(other.Environment);
        Labels = new ContainerLabelCollection(other.Labels);
        Ports = new ContainerPortCollection(other.Ports);
        Mounts = new ContainerMountCollection(other.Mounts);
        WaitStrategies = new ContainerWaitStrategyCollection(other.WaitStrategies);
        Network = new ContainerNetworkOptions(other.Network);
        Resources = new ContainerResourceOptions(other.Resources);
        Logging = new ContainerLoggingOptions(other.Logging);
        SessionOwned = other.SessionOwned;
        Identity = other.Identity;
        _generatedEnvironmentValues = new Dictionary<string, string>(other._generatedEnvironmentValues, StringComparer.Ordinal);
    }

    /// <summary>Gets or sets the image the container is created from.</summary>
    public ImageSource Image
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
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

    /// <summary>Gets or sets the image pull policy.</summary>
    public PullPolicy PullPolicy
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    } = PullPolicy.IfMissing;

    /// <summary>Gets or sets the container name. When <see langword="null"/>, the container is named after <see cref="ReuseId"/> when it is set, and gets a random name otherwise.</summary>
    public string? Name
    {
        get => _name;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            _name = value;
        }
    }

    /// <summary>Gets or sets an identifier used to reuse an existing container across runs. When set, the container is not removed on dispose.</summary>
    /// <remarks>A container is only adopted when it was created from the same definition. The credentials generated by the database helpers are the exception: every process generates its own, so the ones the container was created with are read back from it.</remarks>
    public string? ReuseId
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the container hostname.</summary>
    public string? Hostname
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the user the container runs as.</summary>
    public string? User
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the working directory inside the container.</summary>
    public string? WorkingDirectory
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the maximum time <see cref="TemporaryContainer.WaitUntilReadyAsync"/> waits for all wait strategies to complete.</summary>
    public TimeSpan StartupTimeout
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    } = TimeSpan.FromSeconds(300);

    /// <summary>Gets the entrypoint override tokens.</summary>
    public ContainerCommandCollection Entrypoint { get; }

    /// <summary>Gets the command tokens passed to the container.</summary>
    public ContainerCommandCollection Command { get; }

    /// <summary>Gets the environment variables.</summary>
    public ContainerEnvironmentCollection Environment { get; }

    /// <summary>Gets the labels.</summary>
    public ContainerLabelCollection Labels { get; }

    /// <summary>Gets the published ports.</summary>
    public ContainerPortCollection Ports { get; }

    /// <summary>Gets the mounts.</summary>
    public ContainerMountCollection Mounts { get; }

    /// <summary>Gets the wait strategies run when the container starts.</summary>
    public ContainerWaitStrategyCollection WaitStrategies { get; }

    /// <summary>Gets the network options.</summary>
    public ContainerNetworkOptions Network { get; }

    /// <summary>Gets the resource limits.</summary>
    public ContainerResourceOptions Resources { get; }

    /// <summary>Gets the logging options.</summary>
    public ContainerLoggingOptions Logging { get; }

    /// <summary>Gets a value indicating whether the definition belongs to a container, in which case it can no longer be changed.</summary>
    public bool IsReadOnly => _isReadOnly;

    /// <summary>Whether the container belongs to the current run. The reaper container is the only one that does not: it must outlive the session it watches, so it is not labelled with it and does not remove itself.</summary>
    internal bool SessionOwned { get; set; } = true;

    /// <summary>The run recorded in the labels of the container. Defaults to the current one; the tests use it to create a container that looks like the leftover of a run that is over.</summary>
    internal SessionIdentity? Identity { get; set; }

    /// <summary>The hash of the configuration, computed once the definition can no longer change.</summary>
    internal string? ConfigurationHash { get; private set; }

    /// <summary>The host ports the runtime picked for the ports that asked for a random one, for the runtimes that cannot pick them on their own.</summary>
    internal Dictionary<int, int> AllocatedHostPorts { get; } = [];

    /// <summary>Creates a <see cref="TemporaryContainer"/> from a deep copy of this definition. Later changes to this definition do not affect the returned container, and the copy the container owns cannot be changed.</summary>
    /// <returns>A new container.</returns>
    public virtual TemporaryContainer CreateContainer()
    {
        return new TemporaryContainer(new ContainerDefinition(this));
    }

    /// <summary>Sets an environment variable to a credential generated by the library. See <see cref="ReuseId"/> for why that matters.</summary>
    internal void SetGeneratedEnvironmentValue(string name, string value)
    {
        Environment.Add(name, value);
        _generatedEnvironmentValues[name] = value;
    }

    /// <summary>Determines whether a variable still holds the value the library generated for it, rather than one the caller set.</summary>
    internal bool IsGeneratedEnvironmentValue(string name, string value)
        => _generatedEnvironmentValues.TryGetValue(name, out var generated) && string.Equals(generated, value, StringComparison.Ordinal);

    /// <summary>Replaces the generated credentials with the ones an adopted container was created with.</summary>
    internal void AdoptGeneratedEnvironmentValues(IReadOnlyDictionary<string, string> containerEnvironment)
    {
        foreach (var (name, generated) in _generatedEnvironmentValues.ToArray())
        {
            if (!string.Equals(Environment.GetValue(name), generated, StringComparison.Ordinal))
                continue;

            if (containerEnvironment.TryGetValue(name, out var value))
            {
                Environment.SetValueCore(name, value);
                _generatedEnvironmentValues[name] = value;
            }
        }
    }

    /// <summary>Names the container of a definition that names nothing, before the definition becomes read-only.</summary>
    internal void AssignGeneratedName(string name)
    {
        _name = name;
        _hasGeneratedName = true;
    }

    /// <summary>Freezes the definition a container owns.</summary>
    internal void MakeReadOnly()
    {
        _isReadOnly = true;
        Entrypoint.MakeReadOnly();
        Command.MakeReadOnly();
        Environment.MakeReadOnly();
        Labels.MakeReadOnly();
        Ports.MakeReadOnly();
        Mounts.MakeReadOnly();
        WaitStrategies.MakeReadOnly();
        Network.MakeReadOnly();
        Resources.MakeReadOnly();
        Logging.MakeReadOnly();
        ConfigurationHash = ContainerConfigurationHash.Compute(this);
    }
}
