using System.Data.Common;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A temporary SQL Server container. Obtain one from <see cref="SqlServerContainerDefinition.CreateContainer"/>.</summary>
public sealed class SqlServerContainer : TemporaryContainer
{
    internal SqlServerContainer(ContainerDefinition definition)
        : base(definition)
    {
    }

    /// <summary>Gets a SQL Server connection string for the running container, using credentials from the definition's environment variables (<c>MSSQL_SA_PASSWORD</c>).</summary>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public string GetConnectionString()
    {
        var port = GetMappedPort(1433);
        var password = Definition.Environment.GetValue("MSSQL_SA_PASSWORD") ?? Definition.Environment.GetValue("SA_PASSWORD");
        if (password is null)
            throw new InvalidOperationException("The SQL Server SA password is not configured.");

        // The password is caller-supplied, so it goes through the builder: a value containing ';' would otherwise
        // end the entry and have the rest of it parsed as further keywords.
        var builder = new DbConnectionStringBuilder
        {
            ["Server"] = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1,{port}"),
            ["Database"] = "master",
            ["User Id"] = "sa",
            ["Pwd"] = password,
            ["Encrypt"] = "True",
            ["TrustServerCertificate"] = "True",
            ["Connection Timeout"] = "30",
        };

        return builder.ConnectionString;
    }
}
