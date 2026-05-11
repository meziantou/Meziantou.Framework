using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

internal static class TdsQueryContextExtensions
{
    internal static IQueryable ResolveQuery(this TdsQueryContext context, TdsQueryRoot queryRoot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(queryRoot);

        var query = queryRoot.GetQuery(context);
        return query ?? throw new InvalidOperationException($"The query root '{queryRoot.Name}' returned null.");
    }
}
