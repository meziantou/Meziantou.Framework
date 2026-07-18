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

        return string.Create(CultureInfo.InvariantCulture, $"Server=127.0.0.1,{port};Database=master;User Id=sa;Pwd={password};Encrypt=True;TrustServerCertificate=True;Connection Timeout=30");
    }
}
