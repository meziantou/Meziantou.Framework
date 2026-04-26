using System.Net;
using Meziantou.Framework.Tds.Handler;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds.Hosting;

internal sealed class TdsTcpConnectionHandler : ConnectionHandler
{
    private readonly TdsServerOptions _options;
    private readonly TdsAuthenticationDelegateHolder _authenticationDelegateHolder;
    private readonly TdsQueryDelegateHolder _queryDelegateHolder;
    private readonly ILogger<TdsTcpConnectionHandler> _logger;

    public TdsTcpConnectionHandler(
        TdsServerOptions options,
        TdsAuthenticationDelegateHolder authenticationDelegateHolder,
        TdsQueryDelegateHolder queryDelegateHolder,
        ILogger<TdsTcpConnectionHandler> logger)
    {
        _options = options;
        _authenticationDelegateHolder = authenticationDelegateHolder;
        _queryDelegateHolder = queryDelegateHolder;
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var processor = new TdsConnectionProcessor(_options, _authenticationDelegateHolder.Handler, _queryDelegateHolder.Handler, _logger);
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
            _logger.LogDebug(ex, "TDS connection closed with exception");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
