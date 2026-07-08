using StackExchange.Redis;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class RedisContainerTests
{
    [Fact]
    public void CreateRedis_ConfiguresDefinition()
    {
        var definition = ContainerDefinition.CreateRedis();

        Assert.IsType<RegistryImage>(definition.Image);
        Assert.Equal("redis:8.2", ((RegistryImage)definition.Image).Name);
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
        await using var container = ContainerDefinition.CreateRedis().CreateContainer();
        Assert.IsType<RedisContainer>(container);
    }

    [Fact]
    public async Task StartAsync_ConnectionStringWorks()
    {
        global::Xunit.Assert.SkipUnless(ContainerRuntimeInfo.IsAvailable(), "No container runtime is available.");

        await using var container = await StartWithRetryAsync(ContainerDefinition.CreateRedis());

        var connectionString = container.GetConnectionString();
        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
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
