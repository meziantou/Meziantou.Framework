using System.Net;
using System.Security.Claims;

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

    /// <summary>
    /// Gets a value indicating whether every parameter sent by the client was decoded.
    /// This is <see langword="false"/> when <see cref="Parameters"/> is truncated because the client sent a
    /// parameter whose type the server cannot decode, or because the payload was malformed. Requests with
    /// incomplete parameters should be rejected rather than acted on, because the missing values are silent.
    /// </summary>
    public bool HasCompleteParameters { get; init; } = true;

    /// <summary>Gets the authenticated user context associated with this query request.</summary>
    public ClaimsPrincipal? UserContext { get; init; }
}
