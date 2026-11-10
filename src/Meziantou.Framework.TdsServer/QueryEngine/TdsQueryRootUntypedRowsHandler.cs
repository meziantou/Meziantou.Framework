using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

internal delegate ValueTask TdsQueryRootUntypedRowsHandler(TdsQueryContext context, IReadOnlyList<object> rows, CancellationToken cancellationToken);
