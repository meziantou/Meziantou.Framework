namespace Meziantou.Framework.TemporaryContainers;

/// <summary>
/// Provides MongoDB factory members for <see cref="ContainerDefinition"/>.
/// </summary>
public static class ContainerDefinitionMongoDbExtensions
{
    extension(ContainerDefinition)
    {
        /// <summary>Creates a definition pre-configured for a MongoDB container (port 27017 and a readiness wait strategy).</summary>
        /// <returns>A MongoDB container definition using the <c>mongo:8</c> image.</returns>
        public static MongoDbContainerDefinition CreateMongoDb()
        {
            return CreateMongoDb(ImageSource.FromRegistry("mongo:8"));
        }

        /// <summary>Creates a definition pre-configured for a MongoDB container (port 27017 and a readiness wait strategy).</summary>
        /// <param name="image">The MongoDB image to use.</param>
        /// <returns>A MongoDB container definition.</returns>
        public static MongoDbContainerDefinition CreateMongoDb(ImageSource image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var definition = new MongoDbContainerDefinition(image);
            definition.Ports.Add(27017);
            definition.WaitStrategies.Add(Wait.ForLogMessage("Waiting for connections"));
            definition.WaitStrategies.Add(Wait.ForPort(27017));
            return definition;
        }
    }
}
