using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;

namespace Meziantou.DnsProxy.Forwarding;

/// <summary>
/// Resolves upstream hostnames using the configured bootstrap DNS servers, so the proxy does not depend on the
/// machine's own resolver. Results are cached so a hostname is resolved once rather than on every connection.
/// </summary>
internal sealed class BootstrapDnsResolver
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<IPAddress> _bootstrapServers;
    private readonly TimeProvider _timeProvider;

    public BootstrapDnsResolver(IReadOnlyList<IPAddress> bootstrapServers, TimeProvider timeProvider)
    {
        _bootstrapServers = bootstrapServers;
        _timeProvider = timeProvider;
    }

    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        if (TryGetCached(host, out var cached))
        {
            return cached;
        }

        var addresses = await QueryBootstrapServersAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count > 0)
        {
            _cache[host] = new CacheEntry(addresses, _timeProvider.GetUtcNow().Add(CacheDuration));
        }

        return addresses;
    }

    /// <summary>
    /// Synchronous entry point for <see cref="DnsClientOptions.ServerAddressResolver"/>, which does not have an
    /// asynchronous overload. Cache hits never block; a miss is bounded by <see cref="TotalBudget"/>.
    /// </summary>
    public IReadOnlyList<IPAddress> Resolve(string host)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        if (TryGetCached(host, out var cached))
        {
            return cached;
        }

        using var cancellationTokenSource = new CancellationTokenSource(TotalBudget);
        try
        {
            return ResolveAsync(host, cancellationTokenSource.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return [];
        }
    }

    private bool TryGetCached(string host, out IReadOnlyList<IPAddress> addresses)
    {
        if (_cache.TryGetValue(host, out var entry) && entry.ExpiresAtUtc > _timeProvider.GetUtcNow())
        {
            addresses = entry.Addresses;
            return true;
        }

        addresses = [];
        return false;
    }

    private async Task<IReadOnlyList<IPAddress>> QueryBootstrapServersAsync(string host, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(TotalBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);

        foreach (var bootstrapServer in _bootstrapServers)
        {
            if (linked.IsCancellationRequested)
            {
                break;
            }

            var addresses = await QueryBootstrapServerAsync(bootstrapServer, host, linked.Token).ConfigureAwait(false);
            if (addresses.Count > 0)
            {
                return addresses;
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<IPAddress>> QueryBootstrapServerAsync(IPAddress bootstrapServer, string host, CancellationToken cancellationToken)
    {
        using var client = new DnsClient(bootstrapServer.ToString(), DnsClientProtocol.Udp, new DnsClientOptions
        {
            Timeout = QueryTimeout,
            EnableEdns = false,
        });

        // A and AAAA are independent; querying them together halves the latency of a cold resolution.
        var results = await Task.WhenAll(
            QueryAsync(client, host, DnsQueryType.A, cancellationToken),
            QueryAsync(client, host, DnsQueryType.AAAA, cancellationToken)).ConfigureAwait(false);

        var addresses = new List<IPAddress>();
        foreach (var result in results)
        {
            addresses.AddRange(result);
        }

        return addresses;
    }

    private static async Task<IReadOnlyList<IPAddress>> QueryAsync(DnsClient client, string host, DnsQueryType queryType, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.QueryAsync(host, queryType, cancellationToken).ConfigureAwait(false);
            return [.. response.Answers.GetIPAddresses()];
        }
        catch (Exception ex) when (ex is DnsProtocolException or OperationCanceledException or SocketException or IOException)
        {
            return [];
        }
    }

    private sealed record CacheEntry(IReadOnlyList<IPAddress> Addresses, DateTimeOffset ExpiresAtUtc);
}
