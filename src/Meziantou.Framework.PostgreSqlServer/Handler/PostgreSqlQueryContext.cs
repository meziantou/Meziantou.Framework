using System.Net;

namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Provides query execution context for a PostgreSQL request.</summary>
public sealed class PostgreSqlQueryContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the startup parameters associated with the connection.</summary>
    public IReadOnlyDictionary<string, string> StartupParameters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the type of query request.</summary>
    public required PostgreSqlQueryRequestType RequestType { get; init; }

    /// <summary>Gets the SQL text for simple and extended query requests.</summary>
    public string? CommandText { get; init; }

    /// <summary>Gets the statement name for extended query requests, when available.</summary>
    public string? StatementName { get; init; }

    /// <summary>Gets the portal name for extended query requests, when available.</summary>
    public string? PortalName { get; init; }

    /// <summary>Gets the decoded bound parameters.</summary>
    public IReadOnlyList<PostgreSqlQueryParameter> Parameters { get; init; } = [];
}
