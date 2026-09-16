namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary MongoDB container. Obtain one from <see cref="MongoDbContainerDefinition.CreateContainer"/>.</summary>
public sealed class MongoDbContainer : TemporaryContainer
{
    internal MongoDbContainer(ContainerDefinition definition)
        : base(definition)
    {
    }

    /// <summary>Gets a MongoDB connection string for the running container, using root credentials configured on the definition.</summary>
    /// <param name="enableJournaling">A value indicating whether to enable journaling (<c>j=true</c>) in the connection string.</param>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public string GetConnectionString(bool enableJournaling = false)
    {
        return BuildConnectionString(GetMappedPort(27017), enableJournaling);
    }

    /// <summary>Builds the connection string. The credentials are read when it is built: a container adopted through ReuseId was created with the credentials of the process that created it.</summary>
    internal string BuildConnectionString(int port, bool enableJournaling)
    {
        var username = Definition.Environment.GetValue("MONGO_INITDB_ROOT_USERNAME") ?? throw new InvalidOperationException("The MongoDB root username is not configured.");
        var password = Definition.Environment.GetValue("MONGO_INITDB_ROOT_PASSWORD") ?? throw new InvalidOperationException("The MongoDB root password is not configured.");
        var journaling = enableJournaling ? "true" : "false";
        return string.Create(CultureInfo.InvariantCulture, $"mongodb://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@127.0.0.1:{port}/?authSource=admin&j={journaling}");
    }
}
