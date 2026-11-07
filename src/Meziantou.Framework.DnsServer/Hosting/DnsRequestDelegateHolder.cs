using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Holds the DNS request handler delegate. Registered as a singleton in DI.</summary>
internal sealed class DnsRequestDelegateHolder
{
    private DnsRequestDelegate? _handler;

    public DnsRequestDelegate Handler => _handler ?? DefaultHandler;

    /// <summary>Registers the handler. Only the first call succeeds, so a late or duplicate registration fails loudly instead of racing.</summary>
    public void SetHandler(DnsRequestDelegate handler)
    {
        if (Interlocked.CompareExchange(ref _handler, handler, comparand: null) is not null)
            throw new InvalidOperationException("A DNS request handler is already registered. MapDnsHandler must be called exactly once, before the application starts.");
    }

    private static ValueTask<DnsMessage> DefaultHandler(DnsRequestContext context, CancellationToken cancellationToken)
    {
        var response = context.CreateResponse();
        response.ResponseCode = DnsResponseCode.ServerFailure;

        return ValueTask.FromResult(response);
    }
}
