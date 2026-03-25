using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Holds the DNS request handler delegate. Registered as a singleton in DI.</summary>
internal sealed class DnsRequestDelegateHolder
{
    private DnsRequestDelegate? _handler;

    public DnsRequestDelegate Handler
    {
        get => _handler ?? DefaultHandler;
        set => _handler = value;
    }

    private static ValueTask<DnsMessage> DefaultHandler(DnsRequestContext context, CancellationToken cancellationToken)
    {
        var response = context.CreateResponse();
        response.ResponseCode = DnsResponseCode.ServerFailure;

        return ValueTask.FromResult(response);
    }
}
