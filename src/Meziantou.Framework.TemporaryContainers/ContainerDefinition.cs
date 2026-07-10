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
    }

    /// <summary>Initializes a new instance of the <see cref="ContainerDefinition"/> class by deep-copying another definition.</summary>
    /// <param name="other">The definition to copy.</param>
    public ContainerDefinition(ContainerDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Image = other.Image;
        Runtime = other.Runtime;
        PullPolicy = other.PullPolicy;
        Name = other.Name;
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
    }

    /// <summary>Gets or sets the image the container is created from.</summary>
    public ImageSource Image { get; set; }

    /// <summary>Gets or sets the container runtime to use.</summary>
    public ContainerRuntime Runtime { get; set; } = ContainerRuntime.Auto;

    /// <summary>Gets or sets the image pull policy.</summary>
    public PullPolicy PullPolicy { get; set; } = PullPolicy.IfMissing;

    /// <summary>Gets or sets the container name. When <see langword="null"/>, the runtime assigns a random name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets an identifier used to reuse an existing container across runs. When set, the container is not removed on dispose.</summary>
    public string? ReuseId { get; set; }

    /// <summary>Gets or sets the container hostname.</summary>
    public string? Hostname { get; set; }

    /// <summary>Gets or sets the user the container runs as.</summary>
    public string? User { get; set; }

    /// <summary>Gets or sets the working directory inside the container.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets or sets the maximum time <see cref="TemporaryContainer.WaitUntilReadyAsync"/> waits for all wait strategies to complete.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(300);

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

    /// <summary>Creates a <see cref="TemporaryContainer"/> from a deep copy of this definition. Later changes to this definition do not affect the returned container.</summary>
    /// <returns>A new container.</returns>
    public virtual TemporaryContainer CreateContainer()
    {
        return new TemporaryContainer(new ContainerDefinition(this));
    }

    /// <summary>Creates a definition pre-configured for a Redis container (port 6379 and a readiness wait strategy).</summary>
    /// <returns>A Redis container definition using the <c>redis:8.2</c> image.</returns>
    public static RedisContainerDefinition CreateRedis()
    {
        return CreateRedis(ImageSource.FromRegistry("redis:8.2"));
    }

    /// <summary>Creates a definition pre-configured for a Redis container (port 6379 and a readiness wait strategy).</summary>
    /// <param name="image">The Redis image to use.</param>
    /// <returns>A Redis container definition.</returns>
    public static RedisContainerDefinition CreateRedis(ImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var definition = new RedisContainerDefinition(image);
        definition.Ports.Add(6379);
        definition.WaitStrategies.Add(Wait.ForLogMessage("Ready to accept connections"));
        definition.WaitStrategies.Add(Wait.ForPort(6379));
        return definition;
    }

    /// <summary>Creates a definition pre-configured for a PostgreSQL container (port 5432, a default password, and a readiness wait strategy).</summary>
    /// <returns>A PostgreSQL container definition using the <c>postgres:17</c> image.</returns>
    public static PostgreSqlContainerDefinition CreatePostgreSql()
    {
        return CreatePostgreSql(ImageSource.FromRegistry("postgres:17"));
    }

    /// <summary>Creates a definition pre-configured for a PostgreSQL container (port 5432, a default password, and a readiness wait strategy).</summary>
    /// <param name="image">The PostgreSQL image to use.</param>
    /// <returns>A PostgreSQL container definition.</returns>
    public static PostgreSqlContainerDefinition CreatePostgreSql(ImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var definition = new PostgreSqlContainerDefinition(image);
        if (!definition.Environment.Contains("POSTGRES_PASSWORD"))
            definition.Environment.Add("POSTGRES_PASSWORD", "postgres");

        definition.Ports.Add(5432);
        definition.WaitStrategies.Add(Wait.ForLogMessage("database system is ready to accept connections", occurrences: 2));
        definition.WaitStrategies.Add(Wait.ForPort(5432));
        return definition;
    }
}
