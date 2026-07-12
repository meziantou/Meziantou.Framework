using Meziantou.Xunit;
using Npgsql;

namespace Meziantou.Framework.TemporaryContainers.Tests;

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
        Assert.Equal("postgres", definition.Environment.GetValue("POSTGRES_PASSWORD"));
        Assert.Equal(1, definition.Ports.Count);
        Assert.Equal(2, definition.WaitStrategies.Count);
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

    private static async Task<PostgreSqlContainer> StartWithRetryAsync(PostgreSqlContainerDefinition definition)
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
