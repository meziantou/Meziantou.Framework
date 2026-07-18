namespace Meziantou.Framework.TemporaryContainers;

/// <summary>
/// Provides PostgreSQL factory members for <see cref="ContainerDefinition"/>.
/// </summary>
public static class ContainerDefinitionPostgreSqlExtensions
{
    extension(ContainerDefinition)
    {
        /// <summary>Creates a definition pre-configured for a PostgreSQL container (port 5432, a default password, and a readiness wait strategy).</summary>
        /// <returns>A PostgreSQL container definition using the <c>postgres:17</c> image.</returns>
        public static PostgreSqlContainerDefinition CreatePostgreSql()
        {
            return CreatePostgreSql(ImageSource.FromRegistry("postgres:17"));
        }

        /// <summary>Creates a definition pre-configured for a PostgreSQL container (port 5432, a default password, and a readiness wait strategy).</summary>
        /// <param name="image">The PostgreSQL image to use.</param>
        /// <returns>A PostgreSQL container definition.</returns>
        public static PostgreSqlContainerDefinition CreatePostgreSql(ImageSource image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var definition = new PostgreSqlContainerDefinition(image);
            if (!definition.Environment.Contains("POSTGRES_PASSWORD"))
                definition.Environment.Add("POSTGRES_PASSWORD", "postgres");

            definition.Ports.Add(5432);
            definition.WaitStrategies.Add(Wait.ForLogMessage("database system is ready to accept connections", occurrences: 2));
            definition.WaitStrategies.Add(Wait.ForPort(5432));
            return definition;
        }
    }
}
