using System.Globalization;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary PostgreSQL container. Obtain one from <see cref="PostgreSqlContainerDefinition.CreateContainer"/>.</summary>
public sealed class PostgreSqlContainer : TemporaryContainer
{
    internal PostgreSqlContainer(ContainerDefinition definition)
        : base(definition)
    {
    }

    /// <summary>Gets an Npgsql connection string for the running container, using the credentials from the definition's environment variables (<c>POSTGRES_USER</c>, <c>POSTGRES_PASSWORD</c>, <c>POSTGRES_DB</c>).</summary>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public string GetConnectionString()
    {
        var port = GetMappedPort(5432);
        var username = Definition.Environment.GetValue("POSTGRES_USER") ?? "postgres";
        var password = Definition.Environment.GetValue("POSTGRES_PASSWORD") ?? "";
        var database = Definition.Environment.GetValue("POSTGRES_DB") ?? username;
        return string.Create(CultureInfo.InvariantCulture, $"Host=127.0.0.1;Port={port};Username={username};Password={password};Database={database}");
    }
}
