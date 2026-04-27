using System.Net;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Provides query execution context for a TDS request.</summary>
public sealed class TdsQueryContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the type of query request.</summary>
    public required TdsQueryRequestType RequestType { get; init; }

    /// <summary>Gets the SQL text for SQL batch requests, when available.</summary>
    public string? CommandText { get; init; }

    /// <summary>Gets the RPC procedure name for RPC requests, when available.</summary>
    public string? ProcedureName { get; init; }

    /// <summary>Gets the decoded RPC parameters.</summary>
    public IReadOnlyList<TdsQueryParameter> Parameters { get; init; } = [];
}
