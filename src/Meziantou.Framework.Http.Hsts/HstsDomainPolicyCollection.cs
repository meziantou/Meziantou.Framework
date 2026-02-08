using System.Collections.Concurrent;
using System.Collections;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Diagnostics;

namespace Meziantou.Framework.Http;

/// <summary>A collection of HSTS (HTTP Strict Transport Security) domain policies that determines which domains should be accessed over HTTPS.</summary>
/// <example>
/// <code>
/// // Create a collection with preloaded domains from the Chromium HSTS preload list
/// var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
///
/// // Add a custom domain policy
/// policies.Add("example.com", TimeSpan.FromDays(365), includeSubdomains: true);
///
/// // Check if a domain should be upgraded to HTTPS
/// if (policies.MustUpgradeRequest("example.com"))
/// {
///     // Upgrade HTTP to HTTPS
/// }
/// </code>
/// </example>
public sealed partial class HstsDomainPolicyCollection : IEnumerable<HstsDomainPolicy>
{
    private readonly List<ConcurrentDictionary<string, HstsDomainPolicy>> _policies = new(capacity: 8);
    private readonly TimeProvider _timeProvider;

    // Avoid recomputing the value during initialization
    private readonly DateTimeOffset _expires18weeks;
    private readonly DateTimeOffset _expires1year;

    /// <summary>Gets the default HSTS policy collection that includes preloaded domains from the Chromium HSTS preload list.</summary>
    public static HstsDomainPolicyCollection Default { get; } = new();

    /// <summary>Initializes a new instance of the <see cref="HstsDomainPolicyCollection"/> class.</summary>
    /// <param name="includePreloadDomains">If <see langword="true"/>, includes preloaded domains from the Chromium HSTS preload list. Default is <see langword="true"/>.</param>
    public HstsDomainPolicyCollection(bool includePreloadDomains = true)
        : this(timeProvider: null, includePreloadDomains)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HstsDomainPolicyCollection"/> class with a custom time provider.</summary>
    /// <param name="timeProvider">The time provider to use for determining policy expiration. If <see langword="null"/>, uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="includePreloadDomains">If <see langword="true"/>, includes preloaded domains from the Chromium HSTS preload list. Default is <see langword="true"/>.</param>
    public HstsDomainPolicyCollection(TimeProvider? timeProvider, bool includePreloadDomains = true)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (includePreloadDomains)
        {
            _expires18weeks = _timeProvider.GetUtcNow().Add(TimeSpan.FromDays(18 * 7));
            _expires1year = _timeProvider.GetUtcNow().Add(TimeSpan.FromDays(365));
            LoadPreloadDomains();
        }
    }

    private void Load(ConcurrentDictionary<string, HstsDomainPolicy> dictionary, int entryCount, string resourceName)
    {
        using var stream = typeof(HstsDomainPolicyCollection).Assembly.GetManifestResourceStream(resourceName);
        Debug.Assert(stream is not null);
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gz);
        for (var i = 0; i < entryCount; i++)
        {
            var name = reader.ReadString();
            var includeSubdomains = reader.ReadBoolean();
            var expiresIn = reader.ReadInt32();
            var expiresAt = expiresIn switch
            {
                18 * 7 * 24 * 60 * 60 => _expires18weeks,
                365 * 24 * 60 * 60 => _expires1year,
                _ => _timeProvider.GetUtcNow().AddSeconds(expiresIn),
            };
            dictionary.TryAdd(name, new(name, expiresAt, includeSubdomains));
        }
    }

    /// <summary>Adds or updates an HSTS policy for the specified host with a maximum age.</summary>
    /// <param name="host">The domain host name.</param>
    /// <param name="maxAge">The duration for which the HSTS policy should be in effect.</param>
    /// <param name="includeSubdomains">If <see langword="true"/>, the policy applies to all subdomains of the host.</param>
    public void Add(string host, TimeSpan maxAge, bool includeSubdomains)
    {
        Add(host, _timeProvider.GetUtcNow().Add(maxAge), includeSubdomains);
    }

    /// <summary>Adds or updates an HSTS policy for the specified host with an expiration date.</summary>
    /// <param name="host">The domain host name.</param>
    /// <param name="expiresAt">The date and time when the HSTS policy expires.</param>
    /// <param name="includeSubdomains">If <see langword="true"/>, the policy applies to all subdomains of the host.</param>
    public void Add(string host, DateTimeOffset expiresAt, bool includeSubdomains)
    {
        ArgumentNullException.ThrowIfNull(host);

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

    /// <summary>Determines whether an HTTP request to the specified host should be upgraded to HTTPS based on HSTS policies.</summary>
    /// <param name="host">The domain host name to check.</param>
    /// <returns><see langword="true"/> if the request should be upgraded to HTTPS; otherwise, <see langword="false"/>.</returns>
    public bool MustUpgradeRequest(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return MustUpgradeRequest(host.AsSpan());
    }

    /// <summary>Determines whether an HTTP request to the specified host should be upgraded to HTTPS based on HSTS policies.</summary>
    /// <param name="host">The domain host name to check.</param>
    /// <returns><see langword="true"/> if the request should be upgraded to HTTPS; otherwise, <see langword="false"/>.</returns>
    public bool MustUpgradeRequest(ReadOnlySpan<char> host)
    {
        var enumerator = new DomainSplitReverseEnumerator(host);
        for (var i = 0; i < _policies.Count && enumerator.MoveNext(); i++)
        {
            var dictionary = _policies[i];
            var lastSegments = host[enumerator.Current..];

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

                if (!hsts.IncludeSubdomains && enumerator.Current != 0)
                {
                    return false;
                }

                return true;
            }
        }

        return false;
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

    [StructLayout(LayoutKind.Auto)]
    private ref struct DomainSplitReverseEnumerator
    {
        private ReadOnlySpan<char> _span;

        public DomainSplitReverseEnumerator(ReadOnlySpan<char> span)
        {
            _span = span;
            Current = span.Length;
        }

        public int Current
        {
            readonly get => field == 0 ? 0 : (field + 1);
            private set;
        }

        public bool MoveNext()
        {
            var index = _span.LastIndexOf('.');
            if (index == -1)
            {
                if (_span.IsEmpty)
                    return false;

                Current = 0;
                _span = ReadOnlySpan<char>.Empty;
                return true;
            }

            Current = index;
            _span = _span.Slice(0, index);
            return true;
        }
    }
}
