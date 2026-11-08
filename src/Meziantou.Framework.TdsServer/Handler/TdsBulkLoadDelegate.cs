namespace Meziantou.Framework.Tds.Handler;

/// <summary>Delegate used to receive the rows of a bulk load (<c>SqlBulkCopy</c>).</summary>
public delegate ValueTask<TdsBulkLoadResult> TdsBulkLoadDelegate(TdsBulkLoadContext context, CancellationToken cancellationToken);
