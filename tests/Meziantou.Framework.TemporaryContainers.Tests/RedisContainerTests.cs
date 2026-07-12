using Meziantou.Xunit;
using StackExchange.Redis;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class RedisContainerTests
{
    private static void SkipOnNonCompatibleEnvironments()
    {
        if (!OperatingSystem.IsLinux() && TestEnvironment.IsOnGitHubActions())
            global::Xunit.Assert.Skip("Only runs on Linux.");
    }

    [Fact]
    public void CreateRedis_ConfiguresDefinition()
    {
        var definition = ContainerDefinition.CreateRedis();

        Assert.IsType<RegistryImage>(definition.Image);
        Assert.StartsWith("redis:", ((RegistryImage)definition.Image).Name);
        Assert.Equal(1, definition.Ports.Count);
        Assert.Equal(2, definition.WaitStrategies.Count);
    }

    [Fact]
    public void CreateRedis_WithImage_UsesProvidedImage()
    {
        var definition = ContainerDefinition.CreateRedis(new RegistryImage("redis:7"));

        Assert.Equal("redis:7", ((RegistryImage)definition.Image).Name);
    }

    [Fact]
    public async Task CreateContainer_ReturnsRedisContainer()
    {
        SkipOnNonCompatibleEnvironments();
        await using var container = ContainerDefinition.CreateRedis().CreateContainer();
        Assert.IsType<RedisContainer>(container);
    }

    [Fact]
    public async Task StartAsync_ConnectionStringWorks()
    {
        SkipOnNonCompatibleEnvironments();

        await using var container = await StartWithRetryAsync(ContainerDefinition.CreateRedis());

        var connectionString = container.GetConnectionString();
        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = connection.GetDatabase();

        await database.StringSetAsync("key", "value");
        var value = await database.StringGetAsync("key");

        Assert.Equal("value", value.ToString());
    }

    private static async Task<RedisContainer> StartWithRetryAsync(RedisContainerDefinition definition)
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
