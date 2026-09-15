using Meziantou.Framework.TemporaryContainers.Strategies;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>
/// Provides PostgreSQL factory members for <see cref="ContainerDefinition"/>.
/// </summary>
public static class ContainerDefinitionPostgreSqlExtensions
{
    extension(ContainerDefinition)
    {
        /// <summary>Creates a definition pre-configured for a PostgreSQL container (port 5432, a strong random password, and a readiness wait strategy).</summary>
        /// <returns>A PostgreSQL container definition using the <c>postgres:18</c> image.</returns>
        public static PostgreSqlContainerDefinition CreatePostgreSql()
        {
            return CreatePostgreSql(ImageSource.FromRegistry("postgres:18"));
        }

        /// <summary>Creates a definition pre-configured for a PostgreSQL container (port 5432, a strong random password, and a readiness wait strategy).</summary>
        /// <param name="image">The PostgreSQL image to use.</param>
        /// <returns>A PostgreSQL container definition.</returns>
        public static PostgreSqlContainerDefinition CreatePostgreSql(ImageSource image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var definition = new PostgreSqlContainerDefinition(image);
            definition.Ports.Add(5432);

            // On an empty data directory, the image starts a temporary server to run the initialization scripts, stops it,
            // then starts the real one; on a data directory that is already initialized, it only starts the real one. The
            // ready message is therefore logged once or twice. The temporary server does not listen on TCP, and
            // pg_isready only succeeds once a server accepts connections, not while it is still starting up.
            definition.WaitStrategies.Add(Wait.ForLogMessage("database system is ready to accept connections"));
            definition.WaitStrategies.Add(new CommandWaitStrategy(["pg_isready", "--host", "127.0.0.1", "--port", "5432"]));
            definition.WaitStrategies.Add(Wait.ForPort(5432));
            return definition;
        }
    }
}
