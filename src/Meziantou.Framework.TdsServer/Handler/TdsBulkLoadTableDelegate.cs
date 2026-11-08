namespace Meziantou.Framework.Tds.Handler;

/// <summary>Delegate used to describe the destination table of a bulk load.</summary>
/// <returns>The columns of the table, in order, or <see langword="null"/> when the table does not exist.</returns>
public delegate ValueTask<IReadOnlyList<TdsColumn>?> TdsBulkLoadTableDelegate(TdsBulkLoadTableContext context, CancellationToken cancellationToken);
