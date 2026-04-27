using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Hosting;

internal sealed class TdsQueryDelegateHolder
{
    private TdsQueryDelegate? _handler;

    public TdsQueryDelegate Handler
    {
        get => _handler ?? DefaultHandler;
        set => _handler = value;
    }

    private static ValueTask<TdsQueryResult> DefaultHandler(TdsQueryContext context, CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return ValueTask.FromResult(TdsQueryResult.FromError(new TdsQueryError
        {
            Number = 50003,
            State = 1,
            Class = 16,
            Message = "No query handler configured",
        }));
    }
}
