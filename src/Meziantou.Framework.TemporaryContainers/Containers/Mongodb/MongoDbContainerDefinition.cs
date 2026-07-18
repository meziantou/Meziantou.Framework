namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A <see cref="ContainerDefinition"/> pre-configured for MongoDB. Create one with <see cref="ContainerDefinitionMongoDbExtensions.CreateMongoDb()"/>.</summary>
public sealed class MongoDbContainerDefinition : ContainerDefinition
{
    private const int DefaultPasswordLength = 24;

    internal MongoDbContainerDefinition(ImageSource image)
        : base(image)
    {
        RootUsername = "root";
        RootPassword = ContainerCredentialGenerator.GenerateStrongPassword(DefaultPasswordLength);
    }

    /// <summary>Gets or sets the MongoDB root username. Setting this value updates <c>MONGO_INITDB_ROOT_USERNAME</c> in <see cref="ContainerDefinition.Environment"/>.</summary>
    public string RootUsername
    {
        get
        {
            return Environment.GetValue("MONGO_INITDB_ROOT_USERNAME") ?? throw new InvalidOperationException("The MongoDB root username is not configured.");
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Environment.Add("MONGO_INITDB_ROOT_USERNAME", value);
        }
    }

    /// <summary>Gets or sets the MongoDB root password. Setting this value updates <c>MONGO_INITDB_ROOT_PASSWORD</c> in <see cref="ContainerDefinition.Environment"/>.</summary>
    public string RootPassword
    {
        get
        {
            return Environment.GetValue("MONGO_INITDB_ROOT_PASSWORD") ?? throw new InvalidOperationException("The MongoDB root password is not configured.");
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Environment.Add("MONGO_INITDB_ROOT_PASSWORD", value);
        }
    }

    /// <summary>Creates a <see cref="MongoDbContainer"/> from a deep copy of this definition.</summary>
    /// <returns>A new MongoDB container.</returns>
    public override MongoDbContainer CreateContainer()
    {
        return new MongoDbContainer(new ContainerDefinition(this), RootUsername, RootPassword);
    }
}
