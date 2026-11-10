using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Handles the rows of a data modification statement that targets a query root.</summary>
/// <typeparam name="T">The type of the rows of the query root.</typeparam>
/// <param name="context">The context of the request.</param>
/// <param name="rows">The rows inserted, updated or deleted by the statement.</param>
/// <param name="cancellationToken">The token that is canceled when the request is canceled.</param>
public delegate ValueTask TdsQueryRootRowsHandler<T>(TdsQueryContext context, IReadOnlyList<T> rows, CancellationToken cancellationToken);
