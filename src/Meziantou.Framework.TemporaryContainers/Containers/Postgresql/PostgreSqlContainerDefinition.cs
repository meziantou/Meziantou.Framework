namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A <see cref="ContainerDefinition"/> pre-configured for PostgreSQL. Create one with <see cref="ContainerDefinitionPostgreSqlExtensions.CreatePostgreSql()"/>.</summary>
public sealed class PostgreSqlContainerDefinition : ContainerDefinition
{
    private const int DefaultPasswordLength = 24;

    internal PostgreSqlContainerDefinition(ImageSource image)
        : base(image)
    {
        // A random password rather than a well-known one: the published port is reachable by other users of the machine.
        // Generated rather than set, so a container adopted through ReuseId is reached with the password it was created with.
        SetGeneratedEnvironmentValue("POSTGRES_PASSWORD", ContainerCredentialGenerator.GenerateStrongPassword(DefaultPasswordLength));
    }

    /// <summary>Gets or sets the password of the PostgreSQL superuser. Setting this value updates <c>POSTGRES_PASSWORD</c> in <see cref="ContainerDefinition.Environment"/>.</summary>
    public string Password
    {
        get
        {
            return Environment.GetValue("POSTGRES_PASSWORD") ?? throw new InvalidOperationException("The PostgreSQL password is not configured.");
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Environment.Add("POSTGRES_PASSWORD", value);
        }
    }

    /// <summary>Creates a <see cref="PostgreSqlContainer"/> from a deep copy of this definition.</summary>
    /// <returns>A new PostgreSQL container.</returns>
    public override PostgreSqlContainer CreateContainer()
    {
        return new PostgreSqlContainer(new ContainerDefinition(this));
    }
}
