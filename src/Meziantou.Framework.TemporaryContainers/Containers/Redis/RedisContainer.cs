using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary Redis container. Obtain one from <see cref="RedisContainerDefinition.CreateContainer"/>.</summary>
public sealed class RedisContainer : TemporaryContainer
{
    internal RedisContainer(ContainerDefinition definition)
        : base(definition)
    {
    }

    /// <summary>Gets a connection string (<c>host:port</c>) for the running container (on <see cref="TemporaryContainer.GetHostAddress"/>), suitable for StackExchange.Redis.</summary>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public string GetConnectionString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{HostAddress.FormatForUri(GetHostAddress(6379))}:{GetMappedPort(6379)}");
    }
}
