namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary MongoDB container. Obtain one from <see cref="MongoDbContainerDefinition.CreateContainer"/>.</summary>
public sealed class MongoDbContainer : TemporaryContainer
{
    private readonly string _username;
    private readonly string _password;
    private readonly bool _enableJournaling;

    internal MongoDbContainer(ContainerDefinition definition, string username, string password, bool enableJournaling)
        : base(definition)
    {
        _username = username;
        _password = password;
        _enableJournaling = enableJournaling;
    }

    /// <summary>Gets a MongoDB connection string for the running container, using root credentials if configured through <c>MONGO_INITDB_ROOT_USERNAME</c> and <c>MONGO_INITDB_ROOT_PASSWORD</c>.</summary>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public string GetConnectionString()
    {
        var port = GetMappedPort(27017);
        var journaling = _enableJournaling ? "true" : "false";
        return string.Create(CultureInfo.InvariantCulture, $"mongodb://{Uri.EscapeDataString(_username)}:{Uri.EscapeDataString(_password)}@127.0.0.1:{port}/?authSource=admin&j={journaling}");
    }
}
