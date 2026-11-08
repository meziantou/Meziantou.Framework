namespace Meziantou.Framework.Tds.Handler;

/// <summary>Delegate used to observe or reject a transaction manager request.</summary>
/// <returns><see langword="null"/> to accept the request, or the error to send to the client.</returns>
public delegate ValueTask<TdsQueryError?> TdsTransactionDelegate(TdsTransactionContext context, CancellationToken cancellationToken);
