using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql.Hosting;

internal sealed class PostgreSqlQueryDelegateHolder
{
    private PostgreSqlQueryDelegate? _handler;

    public PostgreSqlQueryDelegate Handler
    {
        get => _handler ?? DefaultHandler;
        set => _handler = value;
    }

    private static ValueTask<PostgreSqlQueryResult> DefaultHandler(PostgreSqlQueryContext context, CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return ValueTask.FromResult(PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
        {
            Code = "XX000",
            Message = "No query handler configured",
        }));
    }
}
