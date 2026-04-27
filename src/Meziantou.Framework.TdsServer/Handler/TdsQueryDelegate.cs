namespace Meziantou.Framework.Tds.Handler;

/// <summary>Delegate used to process a SQL batch or RPC request.</summary>
public delegate ValueTask<TdsQueryResult> TdsQueryDelegate(TdsQueryContext context, CancellationToken cancellationToken);
