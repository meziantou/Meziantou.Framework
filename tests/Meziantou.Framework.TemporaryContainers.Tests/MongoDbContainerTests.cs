using Meziantou.Xunit;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
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
        Assert.HasCount(24, password);
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
    public async Task GetConnectionString_JournalingDisabledByDefault()
    {
        SkipOnNonCompatibleEnvironments();
        await using var container = await StartWithRetryAsync(ContainerDefinition.CreateMongoDb());

        Assert.Contains("j=false", container.GetConnectionString());
    }

    [Fact]
    public async Task GetConnectionString_JournalingCanBeEnabled()
    {
        SkipOnNonCompatibleEnvironments();

        await using var container = await StartWithRetryAsync(ContainerDefinition.CreateMongoDb());

        Assert.Contains("j=true", container.GetConnectionString(enableJournaling: true));
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
        try
        {
            return await ContainerTestHelper.StartWithRetryAsync(definition.CreateContainer, XunitCancellationToken, IsIncompatibleKernelFailure);
        }
        catch (Exception ex) when (IsIncompatibleKernelFailure(ex))
        {
            global::Xunit.Assert.Skip("The MongoDB image cannot start on this kernel (SERVER-121912): " + ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Recognizes the fatal log line <c>mongod</c> writes before exiting on Linux 6.19 and newer
    /// (https://jira.mongodb.org/browse/SERVER-121912), which the readiness wait reports in its failure message.
    /// Matching the ticket rather than comparing kernel versions keeps the skip tied to the image actually refusing
    /// to run: the container's kernel is the one that decides (the host's on Linux, the runtime VM's elsewhere), and
    /// the tests start running again on their own once an image without the guard ships.
    /// </summary>
    private static bool IsIncompatibleKernelFailure(Exception exception)
    {
        return exception.ToString().Contains("SERVER-121912", StringComparison.Ordinal);
    }
}
