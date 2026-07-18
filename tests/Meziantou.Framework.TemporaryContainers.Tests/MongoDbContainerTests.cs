using Meziantou.Xunit;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class MongoDbContainerTests
{
    private static void SkipOnNonCompatibleEnvironments()
    {
        if (!OperatingSystem.IsLinux() && TestEnvironment.IsOnGitHubActions())
            global::Xunit.Assert.Skip("Only runs on Linux.");
    }

    [Fact]
    public void CreateMongoDb_ConfiguresDefinition()
    {
        var definition = ContainerDefinition.CreateMongoDb();
        var password = definition.RootPassword;

        Assert.StartsWith("mongo:", ((RegistryImage)definition.Image).Name);
        Assert.Equal("root", definition.RootUsername);
        Assert.Equal("root", definition.Environment.GetValue("MONGO_INITDB_ROOT_USERNAME"));
        Assert.Equal(password, definition.Environment.GetValue("MONGO_INITDB_ROOT_PASSWORD"));
        Assert.Equal(24, password.Length);
        Assert.Contains(password, static c => char.IsUpper(c));
        Assert.Contains(password, static c => char.IsLower(c));
        Assert.Contains(password, static c => char.IsDigit(c));
        Assert.Contains(password, static c => !char.IsLetterOrDigit(c));
        Assert.Equal(1, definition.Ports.Count);
        Assert.Equal(2, definition.WaitStrategies.Count);
    }

    [Fact]
    public void CreateMongoDb_RootCredentials_UpdatesEnvironmentVariables()
    {
        var definition = ContainerDefinition.CreateMongoDb();
        definition.RootUsername = "custom-root";
        definition.RootPassword = "Abcdef1!Abcdef1!";

        Assert.Equal("custom-root", definition.Environment.GetValue("MONGO_INITDB_ROOT_USERNAME"));
        Assert.Equal("Abcdef1!Abcdef1!", definition.Environment.GetValue("MONGO_INITDB_ROOT_PASSWORD"));
    }

    [Fact]
    public void CreateMongoDb_WithImage_UsesProvidedImage()
    {
        var definition = ContainerDefinition.CreateMongoDb(new RegistryImage("mongo:7"));

        Assert.Equal("mongo:7", ((RegistryImage)definition.Image).Name);
    }

    [Fact]
    public async Task CreateContainer_ReturnsMongoDbContainer()
    {
        SkipOnNonCompatibleEnvironments();
        await using var container = ContainerDefinition.CreateMongoDb().CreateContainer();
        Assert.IsType<MongoDbContainer>(container);
    }

    [Fact]
    public async Task StartAsync_ConnectionStringWorks()
    {
        SkipOnNonCompatibleEnvironments();

        await using var container = await StartWithRetryAsync(ContainerDefinition.CreateMongoDb());

        using var client = new MongoClient(container.GetConnectionString());
        var database = client.GetDatabase("testdb");
        var collection = database.GetCollection<BsonDocument>("items");
        await collection.InsertOneAsync(new BsonDocument("value", 1), cancellationToken: XunitCancellationToken);
        var count = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: XunitCancellationToken);
        Assert.Equal(1, count);
    }

    private static async Task<MongoDbContainer> StartWithRetryAsync(MongoDbContainerDefinition definition)
    {
        const int MaxRetries = 3;
        for (var i = 0; ; i++)
        {
            var container = definition.CreateContainer();
            try
            {
                await container.StartAsync(XunitCancellationToken);
                return container;
            }
            catch when (i < MaxRetries)
            {
                await container.DisposeAsync();
                await Task.Delay(1000, XunitCancellationToken);
            }
        }
    }
}
