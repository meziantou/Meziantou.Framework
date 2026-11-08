using System.Data;
using System.Net;
using System.Security.Claims;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Provides context for a transaction manager request.</summary>
public sealed class TdsTransactionContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the authenticated user context associated with the connection.</summary>
    public ClaimsPrincipal? UserContext { get; init; }

    /// <summary>Gets the type of request.</summary>
    public required TdsTransactionRequestType RequestType { get; init; }

    /// <summary>
    /// Gets the identifier of the transaction. For <see cref="TdsTransactionRequestType.Begin"/>, it is the identifier
    /// the transaction gets when the handler accepts the request. Queries running in the transaction report it in
    /// <see cref="TdsQueryContext.TransactionId"/>.
    /// </summary>
    public required long TransactionId { get; init; }

    /// <summary>Gets the name of the transaction sent by the client, if any.</summary>
    public string? TransactionName { get; init; }

    /// <summary>
    /// Gets the name of the savepoint to create (<see cref="TdsTransactionRequestType.Save"/>) or to roll back to
    /// (<see cref="TdsTransactionRequestType.Rollback"/>). It is <see langword="null"/> when a rollback ends the
    /// whole transaction.
    /// </summary>
    public string? SavepointName { get; init; }

    /// <summary>Gets the isolation level requested by <see cref="TdsTransactionRequestType.Begin"/>.</summary>
    public IsolationLevel IsolationLevel { get; init; } = IsolationLevel.Unspecified;

    /// <summary>
    /// Gets a value indicating whether the server ends the transaction because the client disconnected. The
    /// request type is then <see cref="TdsTransactionRequestType.Rollback"/>, and an error returned by the handler
    /// is ignored.
    /// </summary>
    public bool IsConnectionClosing { get; init; }
}
