using System.Collections.Concurrent;
using System.Collections;
using System.ComponentModel.Design;

namespace Meziantou.Framework.Http;

public sealed partial class HstsDomainPolicyCollection : IEnumerable<HstsDomainPolicy>
{
    private readonly List<ConcurrentDictionary<string, HstsDomainPolicy>> _policies = new(capacity: 8);
    private readonly TimeProvider _timeProvider;

    public static HstsDomainPolicyCollection Default { get; } = new HstsDomainPolicyCollection();

    [SetsRequiredMembers]
    public HstsDomainPolicyCollection(bool includePreloadDomains = true)
        : this(timeProvider: null, includePreloadDomains)
    {
    }

    [SetsRequiredMembers]
    public HstsDomainPolicyCollection(TimeProvider? timeProvider, bool includePreloadDomains = true)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (includePreloadDomains)
        {
            Initialize(_timeProvider);
        }
    }

    public void Add(string host, TimeSpan maxAge, bool includeSubdomains)
    {
        Add(host, _timeProvider.GetUtcNow().Add(maxAge), includeSubdomains);
    }

    public void Add(string host, DateTimeOffset expiresAt, bool includeSubdomains)
    {
        var partCount = CountSegments(host);
        ConcurrentDictionary<string, HstsDomainPolicy> dictionary;
        lock (_policies)
        {
            for (var i = _policies.Count; i < partCount; i++)
            {
                _policies.Add(new ConcurrentDictionary<string, HstsDomainPolicy>(StringComparer.OrdinalIgnoreCase));
            }

            dictionary = _policies[partCount - 1];
        }

        dictionary.AddOrUpdate(host,
            (key, arg) => new HstsDomainPolicy(key, arg.expiresAt, arg.includeSubdomains),
            (key, value, arg) => new HstsDomainPolicy(key, arg.expiresAt, arg.includeSubdomains),
            factoryArgument: (expiresAt, includeSubdomains));
    }

    public bool Match(string host)
    {
        var segments = CountSegments(host);
        for (var i = 0; i < _policies.Count && i < segments; i++)
        {
            var dictionary = _policies[i];
            var lastSegments = i == segments - 1 ? host : GetLastSegments(host, i + 1);

#if NET9_0_OR_GREATER
            var lookup = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(lastSegments, out var hsts))
#else
            if (dictionary.TryGetValue(lastSegments.ToString(), out var hsts))
#endif
            {
                if (hsts.ExpiresAt < _timeProvider.GetUtcNow())
                {
                    return false;
                }

                if (!hsts.IncludeSubdomains && i != segments - 1)
                {
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    private static ReadOnlySpan<char> GetLastSegments(string host, int count)
    {
        var hostSpan = host.AsSpan();
        for (var i = 0; i < count; i++)
        {
            var start = hostSpan.LastIndexOf('.');
            hostSpan = hostSpan.Slice(0, start);
        }

        return host.AsSpan(hostSpan.Length + 1);
    }

    // internal for tests
    internal static int CountSegments(string host)
    {
        // foo.bar.com -> 3
        var count = 1;

        var index = -1;
        while (host.IndexOf('.', index + 1) is >= 0 and var newIndex)
        {
            index = newIndex;
            count++;
        }

        return count;
    }

    public IEnumerator<HstsDomainPolicy> GetEnumerator()
    {
        for (var i = 0; i < _policies.Count; i++)
        {
            var dictionary = _policies[i];
            if (dictionary is null)
                continue;

            foreach (var kvp in dictionary)
            {
                yield return kvp.Value;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
