using Meziantou.Xunit;
using Microsoft.Data.SqlClient;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class SqlServerContainerTests
{
    private static void SkipOnNonCompatibleEnvironments()
    {
        if (!OperatingSystem.IsLinux() && TestEnvironment.IsOnGitHubActions())
            global::Xunit.Assert.Skip("Only runs on Linux.");
    }

    [Fact]
    public void CreateSqlServer_ConfiguresDefinition()
    {
        var definition = ContainerDefinition.CreateSqlServer();
        var password = definition.SaPassword;

        Assert.StartsWith("mcr.microsoft.com/mssql/server:", ((RegistryImage)definition.Image).Name);
        Assert.Equal("Y", definition.Environment.GetValue("ACCEPT_EULA"));
        Assert.Equal(password, definition.Environment.GetValue("MSSQL_SA_PASSWORD"));
        Assert.Equal(password, definition.Environment.GetValue("SA_PASSWORD"));
        Assert.Equal(24, password.Length);
        Assert.Contains(password, static c => char.IsUpper(c));
        Assert.Contains(password, static c => char.IsLower(c));
        Assert.Contains(password, static c => char.IsDigit(c));
        Assert.Contains(password, static c => !char.IsLetterOrDigit(c));
        Assert.Equal(1, definition.Ports.Count);
        Assert.Equal(3, definition.WaitStrategies.Count);
    }

    [Fact]
    public void CreateSqlServer_SaPassword_UpdatesEnvironmentVariables()
    {
        var definition = ContainerDefinition.CreateSqlServer();
        definition.SaPassword = "Abcdef1!Abcdef1!";

        Assert.Equal("Abcdef1!Abcdef1!", definition.Environment.GetValue("MSSQL_SA_PASSWORD"));
        Assert.Equal("Abcdef1!Abcdef1!", definition.Environment.GetValue("SA_PASSWORD"));
    }

    [Fact]
    public void CreateSqlServer_WithImage_UsesProvidedImage()
    {
        var definition = ContainerDefinition.CreateSqlServer(new RegistryImage("mcr.microsoft.com/mssql/server:2022-latest"));

        Assert.Equal("mcr.microsoft.com/mssql/server:2022-latest", ((RegistryImage)definition.Image).Name);
    }

    [Fact]
    public async Task CreateContainer_ReturnsSqlServerContainer()
    {
        SkipOnNonCompatibleEnvironments();
        await using var container = ContainerDefinition.CreateSqlServer().CreateContainer();
        Assert.IsType<SqlServerContainer>(container);
    }

    [Fact]
    public async Task StartAsync_ConnectionStringWorks()
    {
        SkipOnNonCompatibleEnvironments();

        await using var container = await StartWithRetryAsync(ContainerDefinition.CreateSqlServer());

        await using var connection = await OpenConnectionWithRetryAsync(container.GetConnectionString());

        await using var command = new SqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync(XunitCancellationToken);
        Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
    }

    private static async Task<SqlServerContainer> StartWithRetryAsync(SqlServerContainerDefinition definition)
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

    private static async Task<SqlConnection> OpenConnectionWithRetryAsync(string connectionString)
    {
        const int MaxRetries = 5;
        for (var i = 0; ; i++)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(XunitCancellationToken);
                return connection;
            }
            catch (SqlException) when (i < MaxRetries)
            {
                await connection.DisposeAsync();
                await Task.Delay(1000, XunitCancellationToken);
            }
        }
    }
}
