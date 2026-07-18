namespace Meziantou.Framework.TemporaryContainers;

/// <summary>
/// Provides Redis factory members for <see cref="ContainerDefinition"/>.
/// </summary>
public static class ContainerDefinitionRedisExtensions
{
    extension(ContainerDefinition)
    {
        /// <summary>Creates a definition pre-configured for a Redis container (port 6379 and a readiness wait strategy).</summary>
        /// <returns>A Redis container definition using the <c>redis:8.2</c> image.</returns>
        public static RedisContainerDefinition CreateRedis()
        {
            return CreateRedis(ImageSource.FromRegistry("redis:8.2"));
        }

        /// <summary>Creates a definition pre-configured for a Redis container (port 6379 and a readiness wait strategy).</summary>
        /// <param name="image">The Redis image to use.</param>
        /// <returns>A Redis container definition.</returns>
        public static RedisContainerDefinition CreateRedis(ImageSource image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var definition = new RedisContainerDefinition(image);
            definition.Ports.Add(6379);
            definition.WaitStrategies.Add(Wait.ForLogMessage("Ready to accept connections"));
            definition.WaitStrategies.Add(Wait.ForPort(6379));
            return definition;
        }
    }
}
