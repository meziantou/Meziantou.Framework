using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Hosting;

internal sealed class TdsAuthenticationDelegateHolder
{
    private TdsAuthenticationDelegate? _handler;

    public TdsAuthenticationDelegate Handler
    {
        get => _handler ?? DefaultHandler;
        set => _handler = value;
    }

    private static ValueTask<TdsAuthenticationResult> DefaultHandler(TdsAuthenticationContext context, CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return ValueTask.FromResult(TdsAuthenticationResult.Fail("No authentication handler configured"));
    }
}
