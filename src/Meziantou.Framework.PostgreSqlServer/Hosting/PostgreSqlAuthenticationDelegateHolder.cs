using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql.Hosting;

internal sealed class PostgreSqlAuthenticationDelegateHolder
{
    private PostgreSqlAuthenticationDelegate? _handler;

    public PostgreSqlAuthenticationDelegate Handler
    {
        get => _handler ?? DefaultHandler;
        set => _handler = value;
    }

    private static ValueTask<PostgreSqlAuthenticationResult> DefaultHandler(PostgreSqlAuthenticationContext context, CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return ValueTask.FromResult(PostgreSqlAuthenticationResult.Fail("No authentication handler configured"));
    }
}
