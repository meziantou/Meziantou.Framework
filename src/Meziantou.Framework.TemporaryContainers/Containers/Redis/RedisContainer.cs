namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary Redis container. Obtain one from <see cref="RedisContainerDefinition.CreateContainer"/>.</summary>
public sealed class RedisContainer : TemporaryContainer
{
    internal RedisContainer(ContainerDefinition definition)
        : base(definition)
    {
    }

    /// <summary>Gets a connection string (<c>host:port</c>) for the running container, suitable for StackExchange.Redis.</summary>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public string GetConnectionString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{GetMappedPort(6379)}");
    }
}
