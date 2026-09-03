using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

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
/// <remarks>
/// <para>
/// Host names are IDNA-canonicalized to their Punycode form and then matched with an ordinal, case-insensitive
/// comparison, so an internationalized domain matches whichever of its two forms is used. The preload list stores
/// Punycode names, and <see cref="HstsClientHandler"/> looks policies up with <see cref="Uri.IdnHost"/>, which is
/// already in that form; an ASCII host name is never converted.
/// </para>
/// <para>
/// <see cref="HstsDomainPolicy.Host"/> reports the canonicalized name, which may differ from the one that was
/// passed to <see cref="Add(string, DateTimeOffset, bool)"/>.
/// </para>
/// <para>
/// The built-in preload list is a separate, immutable layer. A policy learned from a response header may widen
/// the coverage of a preloaded host but can never narrow it, expire it or delete it; only <see cref="Remove"/>
/// takes a host off the preload list, and only for this collection. Learned policies are bounded by
/// <see cref="MaxLearnedPolicies"/>.
/// </para>
/// <para>
/// Enumeration is a moment-in-time view: it may miss a policy added concurrently, it includes learned policies
/// whose <see cref="HstsDomainPolicy.ExpiresAt"/> has already passed, and it materializes the preload entries as
/// it goes. <see cref="MustUpgradeRequest(string)"/> and <see cref="TryGetPolicy"/> are the authority on whether
/// a host is covered.
/// </para>
/// </remarks>
public sealed class HstsDomainPolicyCollection : IEnumerable<HstsDomainPolicy>
{
    /// <summary>The number of learned policies a collection keeps by default.</summary>
    public const int DefaultMaxLearnedPolicies = 10_000;

    // Expired entries are dropped when their own host is looked up again, which never happens for a host the
    // process saw once, so the store is also swept every so often.
    private const int SweepInterval = 256;

    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;
    private readonly HstsPreloadList? _preload;
    private readonly int _maxLearnedPolicies;

    // Copy-on-write: the holder is never mutated once published, so readers can take a snapshot and index
    // into it without locking. Writers build a new one under _lock.
    private volatile LearnedPolicies _learned = LearnedPolicies.Empty;

    // Preloaded hosts that Remove has taken off the list for this collection. The preload data itself is
    // shared and immutable, so it is masked rather than modified. Stays null until Remove needs it.
    private volatile ConcurrentDictionary<string, bool>? _suppressedPreload;

    private int _learnedCount;
    private int _addsSinceSweep;
    private int _sweeping;

    /// <summary>Gets the default HSTS policy collection that includes preloaded domains from the Chromium HSTS preload list.</summary>
    /// <remarks>
    /// This instance is shared by the whole process, so every <see cref="HstsClientHandler"/> built without an
    /// explicit collection observes the policies learned by the others. Create a dedicated collection to isolate
    /// a client, and see <see cref="ClearLearnedPolicies"/> to return this one to its initial state.
    /// </remarks>
    public static HstsDomainPolicyCollection Default { get; } = new();

    /// <summary>Initializes a new instance of the <see cref="HstsDomainPolicyCollection"/> class.</summary>
    /// <param name="includePreloadDomains">If <see langword="true"/>, includes preloaded domains from the Chromium HSTS preload list. Default is <see langword="true"/>.</param>
    /// <param name="maxLearnedPolicies">The maximum number of policies learned from response headers that the collection keeps.</param>
    public HstsDomainPolicyCollection(bool includePreloadDomains = true, int maxLearnedPolicies = DefaultMaxLearnedPolicies)
        : this(timeProvider: null, includePreloadDomains, maxLearnedPolicies)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HstsDomainPolicyCollection"/> class with a custom time provider.</summary>
    /// <param name="timeProvider">The time provider to use for determining policy expiration. If <see langword="null"/>, uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="includePreloadDomains">If <see langword="true"/>, includes preloaded domains from the Chromium HSTS preload list. Default is <see langword="true"/>.</param>
    /// <param name="maxLearnedPolicies">The maximum number of policies learned from response headers that the collection keeps.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLearnedPolicies"/> is not positive.</exception>
    public HstsDomainPolicyCollection(TimeProvider? timeProvider, bool includePreloadDomains = true, int maxLearnedPolicies = DefaultMaxLearnedPolicies)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLearnedPolicies);

        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxLearnedPolicies = maxLearnedPolicies;

        // The preload data is immutable and shared, so including it costs a reference. An application that
        // turned the preload list off at build time has none to include.
        _preload = includePreloadDomains && HstsPreloadList.IsSupported ? HstsPreloadList.Shared : null;
    }

    /// <summary>Gets the maximum number of policies learned from response headers that this collection keeps.</summary>
    /// <remarks>Preloaded entries do not count towards this limit.</remarks>
    public int MaxLearnedPolicies => _maxLearnedPolicies;

    /// <summary>Adds or updates an HSTS policy for the specified host with a maximum age.</summary>
    /// <param name="host">The domain host name. An internationalized domain may be given in either its Unicode or its Punycode form.</param>
    /// <param name="maxAge">The duration for which the HSTS policy should be in effect.</param>
    /// <param name="includeSubdomains">If <see langword="true"/>, the policy applies to all subdomains of the host.</param>
    /// <exception cref="ArgumentException"><paramref name="host"/> is not a valid host name, or cannot be converted to its Punycode form.</exception>
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
    /// <param name="host">The domain host name. An internationalized domain may be given in either its Unicode or its Punycode form.</param>
    /// <param name="expiresAt">The date and time when the HSTS policy expires.</param>
    /// <param name="includeSubdomains">If <see langword="true"/>, the policy applies to all subdomains of the host.</param>
    /// <exception cref="ArgumentException"><paramref name="host"/> is not a valid host name, or cannot be converted to its Punycode form.</exception>
    /// <remarks>
    /// A host on the built-in preload list keeps its preloaded coverage: this policy can widen it but not narrow it.
    /// </remarks>
    public void Add(string host, DateTimeOffset expiresAt, bool includeSubdomains)
    {
        ArgumentNullException.ThrowIfNull(host);

        var canonical = CanonicalizeForStorage(host);
        var bucket = GetOrCreateBucket(CountSegments(canonical));
        var policy = new HstsDomainPolicy(canonical, expiresAt, includeSubdomains, isPreloaded: false);
        if (bucket.TryAdd(canonical, policy))
        {
            SweepLearnedPoliciesIfNeeded(Interlocked.Increment(ref _learnedCount));
        }
        else
        {
            bucket[canonical] = policy;
        }
    }

    /// <summary>Removes the HSTS policy for the specified host, including a policy from the preload list.</summary>
    /// <param name="host">The domain host name. An internationalized domain may be given in either its Unicode or its Punycode form.</param>
    /// <returns><see langword="true"/> if a policy was removed; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Removing a preloaded host masks it for this collection only; the built-in list is shared and immutable.
    /// <see cref="ClearLearnedPolicies"/> restores it.
    /// </remarks>
    public bool Remove(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!TryCanonicalize(host, out var canonical))
            return false;

        var partCount = CountSegments(canonical);
        var removed = false;

        var learned = _learned;
        if (partCount <= learned.Buckets.Length && learned.Buckets[partCount - 1].TryRemove(canonical, out _))
        {
            Interlocked.Decrement(ref _learnedCount);
            removed = true;
        }

        // Remove is explicit, so it drops preloaded entries too
        if (_preload is not null && _preload.TryGetValue(canonical, partCount, out _) && SuppressPreload(canonical))
        {
            removed = true;
        }

        return removed;
    }

    /// <summary>Drops every policy learned from a response header and restores any preloaded host that <see cref="Remove"/> took off the list.</summary>
    /// <remarks>
    /// This returns the collection to the state it was constructed in. It is the supported way to reset
    /// <see cref="Default"/>, which is shared by the whole process.
    /// </remarks>
    public void ClearLearnedPolicies()
    {
        lock (_lock)
        {
            foreach (var bucket in _learned.Buckets)
            {
                bucket.Clear();
            }

            _suppressedPreload = null;
            Volatile.Write(ref _learnedCount, 0);
            Volatile.Write(ref _addsSinceSweep, 0);
        }
    }

    /// <summary>Gets the effective HSTS policy for the specified host, if the host has one of its own.</summary>
    /// <param name="host">The domain host name. An internationalized domain may be given in either its Unicode or its Punycode form.</param>
    /// <param name="policy">When this method returns, the policy registered for that exact host name.</param>
    /// <returns><see langword="true"/> if the host has a policy of its own; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// This reports the policy registered for <paramref name="host"/> itself. A host covered only by a parent
    /// domain's <see cref="HstsDomainPolicy.IncludeSubdomains"/> has no policy of its own and returns
    /// <see langword="false"/> here while <see cref="MustUpgradeRequest(string)"/> returns <see langword="true"/>.
    /// </remarks>
    public bool TryGetPolicy(string host, [NotNullWhen(true)] out HstsDomainPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(host);

        policy = null;
        if (!TryCanonicalize(host, out var canonical))
            return false;

        policy = GetEffectivePolicy(canonical, CountSegments(canonical));
        return policy is not null;
    }

    // Removes a policy learned from a Strict-Transport-Security header. Preloaded entries live in their own
    // immutable layer, so a response header structurally cannot turn the built-in list off.
    internal bool RemoveLearnedPolicy(string host)
    {
        if (!TryCanonicalize(host, out var canonical))
            return false;

        var learned = _learned;
        var partCount = CountSegments(canonical);
        if (partCount > learned.Buckets.Length || !learned.Buckets[partCount - 1].TryRemove(canonical, out _))
            return false;

        Interlocked.Decrement(ref _learnedCount);
        return true;
    }

    /// <summary>Determines whether an HTTP request to the specified host should be upgraded to HTTPS based on HSTS policies.</summary>
    /// <param name="host">The domain host name to check. An internationalized domain may be given in either its Unicode or its Punycode form.</param>
    /// <returns><see langword="true"/> if the request should be upgraded to HTTPS; otherwise, <see langword="false"/>.</returns>
    public bool MustUpgradeRequest(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return MustUpgradeRequest(host.AsSpan());
    }

    /// <summary>Determines whether an HTTP request to the specified host should be upgraded to HTTPS based on HSTS policies.</summary>
    /// <param name="host">The domain host name to check. An internationalized domain may be given in either its Unicode or its Punycode form.</param>
    /// <returns><see langword="true"/> if the request should be upgraded to HTTPS; otherwise, <see langword="false"/>.</returns>
    public bool MustUpgradeRequest(ReadOnlySpan<char> host)
    {
        host = host.TrimEnd('.');

        // HstsClientHandler passes Uri.IdnHost, which is already ASCII, so the request path never converts and
        // never allocates. Only a caller passing an internationalized name in its Unicode form pays for it.
        if (Ascii.IsValid(host))
            return MustUpgradeRequestCore(host);

        // A name that cannot be canonicalized cannot match a policy, but a lookup must not fail the request
        return TryCanonicalize(host.ToString(), out var canonical) && MustUpgradeRequestCore(canonical);
    }

    private bool MustUpgradeRequestCore(ReadOnlySpan<char> host)
    {
        var learned = _learned;
        var preload = _preload;
        var depth = Math.Max(learned.Buckets.Length, preload?.MaxLabelCount ?? 0);

        // Only read the clock if a candidate policy is actually found
        var now = default(DateTimeOffset);
        var hasNow = false;

        // Walk the suffixes from the least to the most specific. A suffix that does not apply does not
        // rule out a match: a more specific entry (down to the host itself) may still require an upgrade.
        var enumerator = new DomainSplitReverseEnumerator(host);
        for (var i = 0; i < depth && enumerator.MoveNext(); i++)
        {
            var isExactHost = enumerator.Current == 0;
            var suffix = host[enumerator.Current..];

            if (i < learned.Buckets.Length && learned.Lookups[i].TryGetValue(suffix, out var policy))
            {
                if (!hasNow)
                {
                    now = _timeProvider.GetUtcNow();
                    hasNow = true;
                }

                if (policy.ExpiresAt < now)
                {
                    // The policy is dead, so drop it instead of keeping every host the process has ever seen.
                    // The compare-and-remove leaves a policy added concurrently for the same host in place.
                    if (learned.Buckets[i].TryRemove(new KeyValuePair<string, HstsDomainPolicy>(policy.Host, policy)))
                    {
                        Interlocked.Decrement(ref _learnedCount);
                    }
                }
                else if (policy.IncludeSubdomains || isExactHost)
                {
                    return true;
                }
            }

            if (preload is not null
                && preload.TryGetValue(suffix, i + 1, out var includeSubdomains)
                && (includeSubdomains || isExactHost)
                && !IsPreloadSuppressed(suffix))
            {
                return true;
            }
        }

        return false;
    }

    private HstsDomainPolicy? GetEffectivePolicy(string canonicalHost, int partCount)
    {
        var learned = _learned;
        HstsDomainPolicy? learnedPolicy = null;
        if (partCount <= learned.Buckets.Length)
        {
            learned.Buckets[partCount - 1].TryGetValue(canonicalHost, out learnedPolicy);
        }

        var preloadIncludeSubdomains = false;
        var isPreloaded = _preload is not null
            && _preload.TryGetValue(canonicalHost, partCount, out preloadIncludeSubdomains)
            && !IsPreloadSuppressed(canonicalHost);

        if (!isPreloaded)
            return learnedPolicy;

        // The preload entry is the floor: it never expires, and a learned policy can only widen it
        return new HstsDomainPolicy(
            canonicalHost,
            DateTimeOffset.MaxValue,
            preloadIncludeSubdomains || (learnedPolicy?.IncludeSubdomains ?? false),
            isPreloaded: true);
    }

    private bool IsPreloadSuppressed(ReadOnlySpan<char> host)
    {
        var suppressed = _suppressedPreload;
        return suppressed is not null && suppressed.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(host);
    }

    private bool SuppressPreload(string host)
    {
        var suppressed = _suppressedPreload;
        if (suppressed is null)
        {
            lock (_lock)
            {
                suppressed = _suppressedPreload ??= new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return suppressed.TryAdd(host, true);
    }

    private ConcurrentDictionary<string, HstsDomainPolicy> GetOrCreateBucket(int partCount)
    {
        var learned = _learned;
        if (partCount <= learned.Buckets.Length)
            return learned.Buckets[partCount - 1];

        lock (_lock)
        {
            learned = _learned;
            if (partCount > learned.Buckets.Length)
            {
                var buckets = new ConcurrentDictionary<string, HstsDomainPolicy>[partCount];
                Array.Copy(learned.Buckets, buckets, learned.Buckets.Length);
                for (var i = learned.Buckets.Length; i < partCount; i++)
                {
                    buckets[i] = new ConcurrentDictionary<string, HstsDomainPolicy>(StringComparer.OrdinalIgnoreCase);
                }

                // A bucket instance is never replaced once published, so a reader holding an older array keeps
                // indexing into the same dictionaries; the volatile write publishes the new holder.
                _learned = learned = new LearnedPolicies(buckets);
            }

            return learned.Buckets[partCount - 1];
        }
    }

    private void SweepLearnedPoliciesIfNeeded(int learnedCount)
    {
        if (learnedCount <= _maxLearnedPolicies && Interlocked.Increment(ref _addsSinceSweep) < SweepInterval)
            return;

        // One sweep at a time; the others simply carry on adding
        if (Interlocked.Exchange(ref _sweeping, 1) != 0)
            return;

        try
        {
            Volatile.Write(ref _addsSinceSweep, 0);
            SweepLearnedPolicies();
        }
        finally
        {
            Volatile.Write(ref _sweeping, 0);
        }
    }

    private void SweepLearnedPolicies()
    {
        var learned = _learned;
        var now = _timeProvider.GetUtcNow();
        List<HstsDomainPolicy>? live = null;

        foreach (var bucket in learned.Buckets)
        {
            foreach (var entry in bucket)
            {
                if (entry.Value.ExpiresAt < now)
                {
                    bucket.TryRemove(entry);
                }
                else
                {
                    (live ??= []).Add(entry.Value);
                }
            }
        }

        var remaining = live?.Count ?? 0;
        var overflow = remaining - _maxLearnedPolicies;
        if (overflow > 0)
        {
            // Drop the policies closest to expiring first: they protect the fewest future requests
            live!.Sort((x, y) => x.ExpiresAt.CompareTo(y.ExpiresAt));
            foreach (var policy in live)
            {
                if (overflow == 0)
                    break;

                var index = CountSegments(policy.Host) - 1;
                if (index < learned.Buckets.Length && learned.Buckets[index].TryRemove(new KeyValuePair<string, HstsDomainPolicy>(policy.Host, policy)))
                {
                    overflow--;
                    remaining--;
                }
            }
        }

        // Concurrent writers may have changed the store while it was walked, so the count is an estimate
        // corrected on every sweep rather than a running total that can drift for good.
        Volatile.Write(ref _learnedCount, remaining);
    }

    // https://datatracker.ietf.org/doc/html/rfc6797#section-10
    // Policies are stored under the IDNA-canonicalized host name. A name that cannot be canonicalized would be
    // stored in a form no lookup can produce, so it is rejected rather than kept as a policy that never matches.
    private static string CanonicalizeForStorage(string host)
    {
        if (!TryCanonicalize(host, out var canonical))
            throw new ArgumentException($"The host name '{host}' cannot be converted to its Punycode form.", nameof(host));

        // A policy stored under a name no lookup can produce would silently never match, which for this library
        // is worse than an exception: every signal says the policy was added while requests keep going out in
        // cleartext. Uri.IdnHost never yields a scheme, a port, a path, userinfo, whitespace or an empty label.
        return Uri.CheckHostName(canonical) switch
        {
            UriHostNameType.Dns or UriHostNameType.IPv4 => canonical,

            // Uri.IdnHost reports an IPv6 literal without its brackets, and that is the form a lookup uses
            UriHostNameType.IPv6 => canonical.Length > 1 && canonical[0] == '[' && canonical[^1] == ']' ? canonical[1..^1] : canonical,

            _ => throw new ArgumentException($"The host name '{host}' is not a valid host name.", nameof(host)),
        };
    }

    // The one implementation of the canonicalization used by the store and by both lookup paths. Keeping it in
    // a single place matters: if the store and a lookup ever disagreed, policies would be written under keys no
    // lookup can produce and would silently never match.
    private static bool TryCanonicalize(string host, [NotNullWhen(true)] out string? canonical)
    {
        var trimmed = TrimTrailingDots(host);
        if (Ascii.IsValid(trimmed))
        {
            canonical = trimmed;
            return true;
        }

        try
        {
            // IdnMapping does not document its instance members as thread safe, and lookups run concurrently.
            // The instance is tiny and only allocated for a non-ASCII name, which never happens on the
            // HstsClientHandler path because Uri.IdnHost is already ASCII.
            // Mapping can also turn a full-width or ideographic full stop into a '.', so trim again afterwards.
            canonical = TrimTrailingDots(new IdnMapping().GetAscii(trimmed));
            return true;
        }
        catch (ArgumentException)
        {
            canonical = null;
            return false;
        }
    }

    // A fully-qualified domain name may end with a dot; it designates the same host
    private static string TrimTrailingDots(string host)
    {
        var trimmed = host.AsSpan().TrimEnd('.');
        return trimmed.Length == host.Length ? host : trimmed.ToString();
    }

    // internal for tests
    internal static int CountSegments(ReadOnlySpan<char> host)
    {
        // foo.bar.com -> 3
        return host.Count('.') + 1;
    }

    public IEnumerator<HstsDomainPolicy> GetEnumerator()
    {
        var learned = _learned;
        var preload = _preload;

        // A host with both a learned policy and a preload entry is reported once, as the effective union
        var learnedHosts = preload is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < learned.Buckets.Length; i++)
        {
            foreach (var entry in learned.Buckets[i])
            {
                learnedHosts?.Add(entry.Key);
                yield return GetEffectivePolicy(entry.Key, i + 1) ?? entry.Value;
            }
        }

        if (preload is not null)
        {
            foreach (var (host, includeSubdomains) in preload.GetEntries())
            {
                if (learnedHosts!.Contains(host) || IsPreloadSuppressed(host))
                    continue;

                yield return new HstsDomainPolicy(host, DateTimeOffset.MaxValue, includeSubdomains, isPreloaded: true);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // The buckets and their span lookups are published together: building the AlternateLookup per probe cost
    // about 9 ns of comparer type-checking on a lookup path measured at 161 ns.
    private sealed class LearnedPolicies
    {
        public static readonly LearnedPolicies Empty = new([]);

        public LearnedPolicies(ConcurrentDictionary<string, HstsDomainPolicy>[] buckets)
        {
            Buckets = buckets;
            Lookups = new ConcurrentDictionary<string, HstsDomainPolicy>.AlternateLookup<ReadOnlySpan<char>>[buckets.Length];
            for (var i = 0; i < buckets.Length; i++)
            {
                Lookups[i] = buckets[i].GetAlternateLookup<ReadOnlySpan<char>>();
            }
        }

        public ConcurrentDictionary<string, HstsDomainPolicy>[] Buckets { get; }

        public ConcurrentDictionary<string, HstsDomainPolicy>.AlternateLookup<ReadOnlySpan<char>>[] Lookups { get; }
    }

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
