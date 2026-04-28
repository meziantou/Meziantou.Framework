using System.Net;
using Meziantou.Framework.PostgreSql.Handler;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.PostgreSql.Hosting;

internal sealed class PostgreSqlTcpConnectionHandler : ConnectionHandler
{
    private readonly PostgreSqlServerOptions _options;
    private readonly PostgreSqlAuthenticationDelegateHolder _authenticationDelegateHolder;
    private readonly PostgreSqlQueryDelegateHolder _queryDelegateHolder;
    private readonly ILogger<PostgreSqlTcpConnectionHandler> _logger;

    public PostgreSqlTcpConnectionHandler(
        PostgreSqlServerOptions options,
        PostgreSqlAuthenticationDelegateHolder authenticationDelegateHolder,
        PostgreSqlQueryDelegateHolder queryDelegateHolder,
        ILogger<PostgreSqlTcpConnectionHandler> logger)
    {
        _options = options;
        _authenticationDelegateHolder = authenticationDelegateHolder;
        _queryDelegateHolder = queryDelegateHolder;
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var processor = new PostgreSqlConnectionProcessor(_options, _authenticationDelegateHolder.Handler, _queryDelegateHolder.Handler, _logger);
        try
        {
            var input = connection.Transport.Input.AsStream();
            var output = connection.Transport.Output.AsStream();
            await processor.ProcessAsync(input, output, connection.RemoteEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0), connection.ConnectionClosed).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PostgreSQL connection closed with exception");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
