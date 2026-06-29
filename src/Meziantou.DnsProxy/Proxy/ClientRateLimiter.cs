using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class ClientRateLimiter : IDisposable
{
    private static readonly object NoLimiterPartitionKey = new();
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);

    private readonly PartitionedRateLimiter<IPAddress?> _limiter;

    public ClientRateLimiter(IOptions<DnsProxyOptions> options)
    {
        var maxQueriesPerWindow = options.Value.MaxDnsQueriesPerClientPerMinute;
        _limiter = PartitionedRateLimiter.Create<IPAddress?, object>(clientAddress =>
        {
            if (maxQueriesPerWindow <= 0 || clientAddress is null)
            {
                return RateLimitPartition.GetNoLimiter(NoLimiterPartitionKey);
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                (object)clientAddress,
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = maxQueriesPerWindow,
                    QueueLimit = 0,
                    Window = WindowDuration,
                });
        });
    }

    public bool TryAcquire(IPAddress? clientAddress)
    {
        using var lease = _limiter.AttemptAcquire(clientAddress);
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
