namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Materializes a typed <see cref="IQueryable"/> built from a SQL query.</summary>
/// <param name="query">The typed query to materialize.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>The materialized rows.</returns>
public delegate ValueTask<IReadOnlyList<object?>> TdsQueryMaterializer(IQueryable query, CancellationToken cancellationToken);
