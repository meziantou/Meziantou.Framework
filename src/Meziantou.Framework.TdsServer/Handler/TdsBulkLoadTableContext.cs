using System.Net;
using System.Security.Claims;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Provides context for the request that asks for the columns of the destination table of a bulk load.</summary>
public sealed class TdsBulkLoadTableContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the authenticated user context associated with the connection.</summary>
    public ClaimsPrincipal? UserContext { get; init; }

    /// <summary>Gets the name of the destination table, without its schema and quotes, such as <c>Orders</c> for <c>[dbo].[Orders]</c>.</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the schema of the destination table, such as <c>dbo</c>, or <see langword="null"/> when the client did not specify one.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Gets the database of the destination table, or <see langword="null"/> when the client did not specify one.</summary>
    public string? DatabaseName { get; init; }
}
