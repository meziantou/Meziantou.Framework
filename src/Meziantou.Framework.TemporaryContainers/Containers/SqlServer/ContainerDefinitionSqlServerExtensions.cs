namespace Meziantou.Framework.TemporaryContainers;

/// <summary>
/// Provides SQL Server factory members for <see cref="ContainerDefinition"/>.
/// </summary>
public static class ContainerDefinitionSqlServerExtensions
{
    extension(ContainerDefinition)
    {
        /// <summary>Creates a definition pre-configured for a SQL Server container (port 1433, required environment variables, a strong random SA password, and a readiness wait strategy).</summary>
        /// <returns>A SQL Server container definition using the <c>mcr.microsoft.com/mssql/server:2025-latest</c> image.</returns>
        public static SqlServerContainerDefinition CreateSqlServer()
        {
            return CreateSqlServer(ImageSource.FromRegistry("mcr.microsoft.com/mssql/server:2025-latest"));
        }

        /// <summary>Creates a definition pre-configured for a SQL Server container (port 1433, required environment variables, a strong random SA password, and a readiness wait strategy).</summary>
        /// <param name="image">The SQL Server image to use.</param>
        /// <returns>A SQL Server container definition.</returns>
        public static SqlServerContainerDefinition CreateSqlServer(ImageSource image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var definition = new SqlServerContainerDefinition(image);
            if (!definition.Environment.Contains("ACCEPT_EULA"))
                definition.Environment.Add("ACCEPT_EULA", "Y");

            definition.Ports.Add(1433);
            definition.WaitStrategies.Add(Wait.ForLogMessage("SQL Server is now ready for client connections"));
            definition.WaitStrategies.Add(Wait.ForLogMessage("Recovery is complete."));
            definition.WaitStrategies.Add(Wait.ForPort(1433));
            return definition;
        }
    }
}
