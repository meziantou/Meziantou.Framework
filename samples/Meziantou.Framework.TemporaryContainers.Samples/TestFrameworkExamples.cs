using Meziantou.Extensions.Logging.Xunit.v3;
using Meziantou.Framework.TemporaryContainers.Samples;
using Xunit;

// The xUnit examples of docs/TemporaryContainers. This project is not a test project: the tests are compiled but
// never discovered nor run. The documentation shows the fixtures and the test classes that use them as top-level types,
// so they share this file.
#pragma warning disable MA0048 // File name must match type name
#region TemporaryContainers_XunitAssemblyFixtureRegistration
[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]
#endregion

#region TemporaryContainers_XunitCleanupFixtureRegistration
[assembly: AssemblyFixture(typeof(ContainerCleanupFixture))]
#endregion

namespace Meziantou.Framework.TemporaryContainers.Samples;

#region TemporaryContainers_XunitFixture
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = ContainerDefinition.CreatePostgreSql().CreateContainer();

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}
#endregion

#region TemporaryContainers_XunitClassFixture
public sealed class OrderRepositoryTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public void SaveOrder()
    {
        var connectionString = database.Container.GetConnectionString();
        Assert.StartsWith("Host=127.0.0.1;", connectionString, StringComparison.Ordinal); // open a connection and test the repository instead
    }
}
#endregion

#region TemporaryContainers_XunitAssemblyFixture
public sealed class InvoiceRepositoryTests(PostgreSqlFixture database)
{
    [Fact]
    public void SaveInvoice()
    {
        var connectionString = database.Container.GetConnectionString();
        Assert.StartsWith("Host=127.0.0.1;", connectionString, StringComparison.Ordinal); // open a connection and test the repository instead
    }
}
#endregion

public sealed class ContainerPerTestTests
{
    #region TemporaryContainers_XunitContainerPerTest
    [Fact]
    public async Task ApplyMigrations()
    {
        var definition = ContainerDefinition.CreatePostgreSql();
        definition.Logging.Logger = XUnitLogger.CreateLogger(); // the container logs go to the output of the test

        await using var container = definition.CreateContainer();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var connectionString = container.GetConnectionString();
        Assert.StartsWith("Host=127.0.0.1;", connectionString, StringComparison.Ordinal); // run the migrations against the database instead
    }
    #endregion

    #region TemporaryContainers_XunitSkipWithoutRuntime
    [Fact]
    public async Task RequiresAContainerRuntime()
    {
        Assert.SkipUnless(await ContainerRuntime.Auto.IsSupportedAsync(TestContext.Current.CancellationToken), "No container runtime is available");

        await using var container = ContainerDefinition.CreateRedis().CreateContainer();
        await container.StartAsync(TestContext.Current.CancellationToken);
    }
    #endregion
}

#region TemporaryContainers_XunitCleanupFixture
public sealed class ContainerCleanupFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        // Removes what the earlier runs left behind when they were killed
        await ContainerRuntime.Auto.CleanupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
#endregion
