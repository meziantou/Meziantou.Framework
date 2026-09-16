using Meziantou.Xunit;
using Npgsql;

namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class PostgreSqlContainerTests
{
    private static void SkipOnNonCompatibleEnvironments()
    {
        if (!OperatingSystem.IsLinux() && TestEnvironment.IsOnGitHubActions())
            global::Xunit.Assert.Skip("Only runs on Linux.");
    }

    [Fact]
    public void CreatePostgreSql_ConfiguresDefinition()
    {
        var definition = ContainerDefinition.CreatePostgreSql();

        Assert.StartsWith("postgres:", ((RegistryImage)definition.Image).Name);

        // A well-known password on a published port lets anyone who reaches the port in.
        var password = definition.Password;
        Assert.NotEqual("postgres", password);
        Assert.HasCount(24, password);
        Assert.Equal(password, definition.Environment.GetValue("POSTGRES_PASSWORD"));
        Assert.NotEqual(password, ContainerDefinition.CreatePostgreSql().Password);
        Assert.Equal(1, definition.Ports.Count);
        Assert.Equal(3, definition.WaitStrategies.Count);
    }

    [Fact]
    public void CreatePostgreSql_Password_UpdatesEnvironmentVariable()
    {
        var definition = ContainerDefinition.CreatePostgreSql();
        definition.Password = "pa;ss=word";

        Assert.Equal("pa;ss=word", definition.Environment.GetValue("POSTGRES_PASSWORD"));
    }

    [Fact]
    public void CreatePostgreSql_WithImage_UsesProvidedImage()
    {
        var definition = ContainerDefinition.CreatePostgreSql(new RegistryImage("postgres:16"));

        Assert.Equal("postgres:16", ((RegistryImage)definition.Image).Name);
    }

    [Fact]
    public async Task CreateContainer_ReturnsPostgreSqlContainer()
    {
        SkipOnNonCompatibleEnvironments();
        await using var container = ContainerDefinition.CreatePostgreSql().CreateContainer();
        Assert.IsType<PostgreSqlContainer>(container);
    }

    [Fact]
    public async Task StartAsync_ConnectionStringWorks()
    {
        SkipOnNonCompatibleEnvironments();

        var definition = ContainerDefinition.CreatePostgreSql();
        definition.Environment.Add("POSTGRES_DB", "testdb");
        await using var container = await StartWithRetryAsync(definition);

        var connectionString = container.GetConnectionString();
        Assert.Contains("Database=testdb", connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(XunitCancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync(XunitCancellationToken);

        Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task StartAsync_ConnectionStringWorksWhenThePasswordContainsASeparator()
    {
        SkipOnNonCompatibleEnvironments();

        var definition = ContainerDefinition.CreatePostgreSql();
        definition.Environment.Add("POSTGRES_PASSWORD", "pa;ss=word");
        await using var container = await StartWithRetryAsync(definition);

        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(XunitCancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync(XunitCancellationToken);

        Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
    }

    private static Task<PostgreSqlContainer> StartWithRetryAsync(PostgreSqlContainerDefinition definition)
    {
        return ContainerTestHelper.StartWithRetryAsync(definition.CreateContainer, XunitCancellationToken);
    }
}
