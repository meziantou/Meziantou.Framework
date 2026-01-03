using System.Runtime.InteropServices;

namespace HttpCaching;

internal readonly struct CacheEntrySecondaryKey : IEquatable<CacheEntrySecondaryKey>
{
    public static CacheEntrySecondaryKey MatchAll { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    public static CacheEntrySecondaryKey MatchNone { get; }

    private readonly Dictionary<string, string>? _headers;

    private CacheEntrySecondaryKey(Dictionary<string, string> headers)
    {
        _headers = headers;
    }

    public CacheEntrySecondaryKey()
    {
        _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Add(string name, string value)
    {
        if (_headers is null)
            return;

        // RFC 7230 Section 3.2.2: Combine multiple header values
        var separator = ",";
        if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "WWW-Authenticate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Proxy-Authenticate", StringComparison.OrdinalIgnoreCase))
        {
            separator = "\n";
        }

        ref var headerValue = ref CollectionsMarshal.GetValueRefOrAddDefault(_headers, name, out var exists);
        if (exists && headerValue != null)
        {
            headerValue = headerValue + separator + value;
        }
        else
        {
            headerValue = value;
        }
    }

    public bool MatchRequest(HttpRequestMessage request)
    {
        // MatchNone never matches (Vary: *)
        if (_headers is null)
            return false;

        // MatchAll (no Vary header) always matches
        if (_headers.Count is 0)
            return true;

        // Build secondary key from request and compare
        var requestKey = new CacheEntrySecondaryKey();
        foreach (var headerName in _headers.Keys)
        {
            if (request.Headers.TryGetValues(headerName, out var values))
            {
                foreach (var value in values)
                {
                    requestKey.Add(headerName, value);
                }
            }
        }

        return Equals(requestKey);
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CacheEntrySecondaryKey other && Equals(other);

    public bool Equals(CacheEntrySecondaryKey other)
    {
        if (_headers is null && other._headers is null)
            return true;

        if (_headers is null || other._headers is null)
            return false;

        if (_headers.Count != other._headers.Count)
            return false;

        foreach (var header in _headers)
        {
            if (!other._headers.TryGetValue(header.Key, out var otherValue))
                return false;

            if (!string.Equals(header.Value, otherValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        if (_headers is null)
            return 0;

        var hash = new HashCode();
        foreach (var header in _headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.Add(header.Key, StringComparer.OrdinalIgnoreCase);
            hash.Add(header.Value);
        }

        return hash.ToHashCode();
    }
}
