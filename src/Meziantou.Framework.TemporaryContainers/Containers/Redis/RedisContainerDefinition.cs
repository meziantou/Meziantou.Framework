namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A <see cref="ContainerDefinition"/> pre-configured for Redis. Create one with <see cref="ContainerDefinitionRedisExtensions.CreateRedis()"/>.</summary>
public sealed class RedisContainerDefinition : ContainerDefinition
{
    internal RedisContainerDefinition(ImageSource image)
        : base(image)
    {
    }

    /// <summary>Creates a <see cref="RedisContainer"/> from a deep copy of this definition.</summary>
    /// <returns>A new Redis container.</returns>
    public override RedisContainer CreateContainer()
    {
        return new RedisContainer(new ContainerDefinition(this));
    }
}
