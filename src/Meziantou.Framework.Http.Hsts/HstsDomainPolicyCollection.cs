using System.Collections.Concurrent;
using System.Collections;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Diagnostics.CodeAnalysis;

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
    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;

    // Copy-on-write: the array is never mutated once published, so readers can take a
    // snapshot and index into it without locking. Writers build a new array under _lock.
    private volatile ConcurrentDictionary<string, HstsDomainPolicy>[] _policies = [];

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
            LoadPreloadDomains();
        }
    }

    private static void Load(ConcurrentDictionary<string, HstsDomainPolicy> dictionary, int entryCount, string resourceName)
    {
        // The resource and the entry count come from the generated file: a mismatch means the package was
        // built from an inconsistent tree, so say which resource is at fault instead of failing inside the
        // decompression stream.
        using var stream = typeof(HstsDomainPolicyCollection).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{resourceName}' is missing from the assembly.");

        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gz);
        for (var i = 0; i < entryCount; i++)
        {
            string name;
            bool includeSubdomains;
            try
            {
                name = reader.ReadString();
                includeSubdomains = reader.ReadBoolean();

                // The duration in the source data is the max-age the domain must serve to qualify for the
                // preload list, not a lifetime for the entry itself. The list is compiled into the assembly,
                // so its entries stay valid until the package is updated.
                _ = reader.ReadInt32();
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
                throw new InvalidOperationException($"The embedded resource '{resourceName}' does not contain the expected {entryCount} entries; reading entry {i} failed.", ex);
            }

            dictionary.TryAdd(name, new(name, DateTimeOffset.MaxValue, includeSubdomains, isPreloaded: true));
        }
    }

    /// <summary>Adds or updates an HSTS policy for the specified host with a maximum age.</summary>
    /// <param name="host">The domain host name.</param>
    /// <param name="maxAge">The duration for which the HSTS policy should be in effect.</param>
    /// <param name="includeSubdomains">If <see langword="true"/>, the policy applies to all subdomains of the host.</param>
    public void Add(string host, TimeSpan maxAge, bool includeSubdomains)
    {
        // A max-age that would push the expiration date out of range saturates instead of throwing
        var now = _timeProvider.GetUtcNow();
        var expiresAt =
            maxAge >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue :
            maxAge <= DateTimeOffset.MinValue - now ? DateTimeOffset.MinValue :
            now.Add(maxAge);

        Add(host, expiresAt, includeSubdomains);
    }

    /// <summary>Adds or updates an HSTS policy for the specified host with an expiration date.</summary>
    /// <param name="host">The domain host name.</param>
    /// <param name="expiresAt">The date and time when the HSTS policy expires.</param>
    /// <param name="includeSubdomains">If <see langword="true"/>, the policy applies to all subdomains of the host.</param>
    public void Add(string host, DateTimeOffset expiresAt, bool includeSubdomains)
    {
        ArgumentNullException.ThrowIfNull(host);

        host = NormalizeHost(host);
        if (HasEmptyLabel(host))
            throw new ArgumentException($"The host name '{host}' contains an empty label.", nameof(host));

        var partCount = CountSegments(host);
        ConcurrentDictionary<string, HstsDomainPolicy> dictionary;
        lock (_lock)
        {
            var policies = _policies;
            if (policies.Length < partCount)
            {
                var newPolicies = new ConcurrentDictionary<string, HstsDomainPolicy>[partCount];
                Array.Copy(policies, newPolicies, policies.Length);
                for (var i = policies.Length; i < partCount; i++)
                {
                    newPolicies[i] = new ConcurrentDictionary<string, HstsDomainPolicy>(StringComparer.OrdinalIgnoreCase);
                }

                // Publish the fully-initialized array; the volatile write makes the element stores visible first
                _policies = policies = newPolicies;
            }

            dictionary = policies[partCount - 1];
        }

        dictionary.AddOrUpdate(host,
            (key, arg) => new HstsDomainPolicy(key, arg.expiresAt, arg.includeSubdomains, isPreloaded: false),
            // A host stays preloaded once it is on the built-in list, whatever policy replaces it
            (key, value, arg) => new HstsDomainPolicy(key, arg.expiresAt, arg.includeSubdomains, value.IsPreloaded),
            factoryArgument: (expiresAt, includeSubdomains));
    }

    /// <summary>Removes the HSTS policy for the specified host, including a policy from the preload list.</summary>
    /// <param name="host">The domain host name.</param>
    /// <returns><see langword="true"/> if a policy was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return TryGetDictionary(host, out var dictionary, out var key) && dictionary.TryRemove(key, out _);
    }

    // Removes a policy learned from a Strict-Transport-Security header. Preloaded entries are kept: like
    // browsers, the built-in list cannot be turned off by a response header.
    internal bool RemoveLearnedPolicy(string host)
    {
        if (!TryGetDictionary(host, out var dictionary, out var key))
            return false;

        if (!dictionary.TryGetValue(key, out var policy) || policy.IsPreloaded)
            return false;

        return dictionary.TryRemove(new KeyValuePair<string, HstsDomainPolicy>(key, policy));
    }

    private bool TryGetDictionary(string host, [NotNullWhen(true)] out ConcurrentDictionary<string, HstsDomainPolicy>? dictionary, [NotNullWhen(true)] out string? key)
    {
        key = NormalizeHost(host);
        var partCount = CountSegments(key);
        var policies = _policies;
        if (partCount > policies.Length)
        {
            dictionary = null;
            key = null;
            return false;
        }

        dictionary = policies[partCount - 1];
        return true;
    }

    // An empty label counts as a segment, so the policy would go to a bucket the suffix walk never reads for
    // that host and would silently never match. Such a host name is not valid anyway: Uri rejects it.
    private static bool HasEmptyLabel(string host)
        => host.Length == 0 || host[0] == '.' || host.Contains("..", StringComparison.Ordinal);

    // A fully-qualified domain name may end with a dot; it designates the same host
    private static string NormalizeHost(string host)
    {
        var trimmed = host.AsSpan().TrimEnd('.');
        return trimmed.Length == host.Length ? host : trimmed.ToString();
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
        host = host.TrimEnd('.');
        var policies = _policies;
        var now = _timeProvider.GetUtcNow();

        // Walk the suffixes from the least to the most specific. A suffix that does not apply does not
        // rule out a match: a more specific entry (down to the host itself) may still require an upgrade.
        var enumerator = new DomainSplitReverseEnumerator(host);
        for (var i = 0; i < policies.Length && enumerator.MoveNext(); i++)
        {
            var dictionary = policies[i];
            var lastSegments = host[enumerator.Current..];

            var lookup = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(lastSegments, out var hsts))
            {
                if (hsts.ExpiresAt < now)
                {
                    // The policy is dead, so drop it instead of keeping every host the process has ever seen.
                    // The compare-and-remove leaves a policy added concurrently for the same host in place.
                    dictionary.TryRemove(new KeyValuePair<string, HstsDomainPolicy>(hsts.Host, hsts));
                    continue;
                }

                if (!hsts.IncludeSubdomains && enumerator.Current != 0)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    // internal for tests
    internal static int CountSegments(ReadOnlySpan<char> host)
    {
        // foo.bar.com -> 3
        return host.Count('.') + 1;
    }

    public IEnumerator<HstsDomainPolicy> GetEnumerator()
    {
        var policies = _policies;
        for (var i = 0; i < policies.Length; i++)
        {
            var dictionary = policies[i];
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
