using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class ClientRateLimiter
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<IPAddress, ClientRateLimitWindow> _clients = new();
    private readonly Lock _evictionLock = new();
    private readonly int _maxClientEntries;
    private readonly int _maxQueriesPerWindow;
    private readonly TimeProvider _timeProvider;

    public ClientRateLimiter(IOptions<DnsProxyOptions> options, TimeProvider timeProvider)
    {
        _maxClientEntries = Math.Max(1, options.Value.MaxRateLimitClientEntries);
        _maxQueriesPerWindow = options.Value.MaxDnsQueriesPerClientPerMinute;
        _timeProvider = timeProvider;
    }

    public bool TryAcquire(IPAddress? clientAddress)
    {
        if (_maxQueriesPerWindow <= 0 || clientAddress is null)
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow();
        var window = _clients.GetOrAdd(clientAddress, CreateClientRateLimitWindow);
        var result = window.TryAcquire(now, _maxQueriesPerWindow);
        if (_clients.Count > _maxClientEntries)
        {
            TrimClientEntries(now);
        }

        return result;
    }

    private void TrimClientEntries(DateTimeOffset now)
    {
        lock (_evictionLock)
        {
            foreach (var entry in _clients)
            {
                if (entry.Value.ExpiresAtUtc <= now)
                {
                    _clients.TryRemove(entry.Key, out _);
                }
            }

            while (_clients.Count > _maxClientEntries)
            {
                KeyValuePair<IPAddress, ClientRateLimitWindow>? oldestEntry = null;
                foreach (var entry in _clients)
                {
                    if (oldestEntry is null || entry.Value.ExpiresAtUtc < oldestEntry.Value.Value.ExpiresAtUtc)
                    {
                        oldestEntry = entry;
                    }
                }

                if (oldestEntry is not { } entryToRemove)
                {
                    return;
                }

                _clients.TryRemove(entryToRemove.Key, out _);
            }
        }
    }

    private static ClientRateLimitWindow CreateClientRateLimitWindow(IPAddress _)
    {
        return new ClientRateLimitWindow();
    }

    private sealed class ClientRateLimitWindow
    {
        private readonly Lock _lock = new();
        private int _count;
        private DateTimeOffset _expiresAtUtc;

        public DateTimeOffset ExpiresAtUtc
        {
            get
            {
                lock (_lock)
                {
                    return _expiresAtUtc;
                }
            }
        }

        public bool TryAcquire(DateTimeOffset now, int maxQueriesPerWindow)
        {
            lock (_lock)
            {
                if (_expiresAtUtc <= now)
                {
                    _expiresAtUtc = now.Add(WindowDuration);
                    _count = 0;
                }

                if (_count >= maxQueriesPerWindow)
                {
                    return false;
                }

                _count++;
                return true;
            }
        }
    }
}
