using System.Text;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A cookie store as specified by the user agent requirements of RFC 6265bis
/// (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-user-agent-requirements): the cookies of the
/// <c>Set-Cookie</c> headers of the responses and of the scripts (<c>document.cookie</c>, <c>cookieStore</c>), sent in
/// the <c>Cookie</c> header of the requests (see <see cref="DocumentRequest.GetCookieHeader"/>).
/// </summary>
/// <remarks>
/// <para>
/// The jar implements the storage model (the <c>Domain</c>, <c>Path</c>, <c>Expires</c>, <c>Max-Age</c>, <c>Secure</c>,
/// <c>HttpOnly</c>, <c>SameSite</c> and <c>Partitioned</c> attributes, the <c>__Secure-</c>, <c>__Host-</c>,
/// <c>__Http-</c> and <c>__Host-Http-</c> prefixes, the public suffixes and the size limits) and the retrieval model (the
/// "same-site" and "cross-site" requests, schemefully). The cookies with the <c>Partitioned</c> attribute are keyed by the
/// site of the top-level document (CHIPS); the other cookies are not partitioned, and the third-party cookies are not blocked.
/// </para>
/// <para>
/// The names and the values of the cookies are byte sequences, represented as strings whose characters are the bytes
/// (isomorphic strings, as the values of the HTTP headers): a script stores the UTF-8 bytes of its strings. The expiry
/// times are measured by the clock given to the constructor (e.g. <see cref="BrowsingContextOptions.TimeProvider"/>).
/// The jar is thread-safe: it can be shared by several browsing contexts.
/// </para>
/// <para>
/// The cookies are kept in memory, and the persistent cookies (the cookies with an expiry time) are persisted by the
/// <see cref="CookiePersistence"/> given to the constructor, if any.
/// </para>
/// </remarks>
public sealed class CookieJar
{
    /// <summary>https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-cookie-lifetime-limits: 400 days.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(400);

    /// <summary>The maximum size of the name and the value of a cookie, in bytes.</summary>
    internal const int MaxNameValuePairSize = 4096;

    /// <summary>The maximum size of the value of an attribute, in bytes.</summary>
    internal const int MaxAttributeValueSize = 1024;

    /// <summary>The number of cookies of a domain above which the excess cookies are removed (as Chromium).</summary>
    private const int MaxCookiesPerDomain = 180;

    /// <summary>The number of cookies above which the excess cookies are removed (as Chromium).</summary>
    private const int MaxCookies = 3300;

    /// <summary>The age of the persisted last access time of a cookie above which a retrieval persists it again (it is only used to evict the excess cookies).</summary>
    private static readonly TimeSpan AccessTimePersistenceInterval = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private readonly List<CookieEntry> _cookies = [];
    private readonly TimeProvider _timeProvider;
    private long _creationSequence;

    /// <summary>Creates an empty cookie jar, whose expiry times are measured by the system clock.</summary>
    public CookieJar()
        : this(timeProvider: null)
    {
    }

    /// <summary>Creates an empty cookie jar.</summary>
    /// <param name="timeProvider">The clock that measures the creation and expiry times of the cookies, or null for <see cref="TimeProvider.System"/>.</param>
    public CookieJar(TimeProvider? timeProvider)
        : this(persistence: null, timeProvider)
    {
    }

    /// <summary>
    /// Creates a cookie jar whose persistent cookies are persisted: the jar contains the cookies of
    /// <see cref="CookiePersistence.LoadCookies"/> that are not expired, and reports its changes to the persistence.
    /// </summary>
    /// <param name="persistence">The persistent storage of the cookies, or null to keep them in memory only.</param>
    /// <param name="timeProvider">The clock that measures the creation and expiry times of the cookies, or null for <see cref="TimeProvider.System"/>.</param>
    public CookieJar(CookiePersistence? persistence, TimeProvider? timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Persistence = persistence;
        if (persistence is not null)
        {
            Load(persistence);
        }
    }

    /// <summary>Gets the persistent storage of the cookies, or null when they are kept in memory only.</summary>
    public CookiePersistence? Persistence { get; }

    /// <summary>The cookie stores notified (outside of the lock of the jar) when cookies are created, changed or removed (https://cookiestore.spec.whatwg.org/#process-changes).</summary>
    private readonly List<WeakReference<ICookieChangeListener>> _listeners = [];

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <summary>Returns the cookies of the jar that are not expired, in the order of their creation.</summary>
    public IReadOnlyList<StoredCookie> GetCookies()
    {
        List<CookieChange>? changes = null;
        StoredCookie[] result;
        lock (_lock)
        {
            EvictExpiredCookies(ref changes);
            PersistChanges(changes);
            result = [.. _cookies.OrderBy(cookie => cookie.CreationSequence).Select(cookie => cookie.ToStoredCookie())];
        }

        RaiseChanged(changes);
        return result;
    }

    /// <summary>
    /// Returns the cookies that would be sent to <paramref name="url"/> by a request that is same-site with it, including the
    /// HttpOnly cookies (the associated cookies of WebDriver, https://w3c.github.io/webdriver/#dfn-associated-cookies), in
    /// the order of the <c>Cookie</c> header. The partitioned cookies are the ones of the site of the URL.
    /// </summary>
    /// <param name="url">An absolute URL.</param>
    public IReadOnlyList<StoredCookie> GetCookies(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (UrlParser.Parse(url, baseUrl: null) is not { } parsed)
            throw new ArgumentException($"'{url}' is not a valid URL", nameof(url));

        var context = new CookieRequestContext(parsed, IsHttp: true, SameSite: CookieSameSiteStatus.Strict, TopLevelOrigin: OriginRecord.FromUrl(parsed));
        return [.. Retrieve(context, updateAccessTime: false).Select(cookie => cookie.ToStoredCookie())];
    }

    /// <summary>
    /// Stores the cookie of a <c>Set-Cookie</c> header received from <paramref name="url"/> in a response to a same-site
    /// request (e.g. to restore cookies). Returns false when the cookie is ignored (an invalid cookie, or a cookie that the
    /// URL cannot set).
    /// </summary>
    /// <param name="url">The absolute URL of the response.</param>
    /// <param name="setCookieHeader">The value of the <c>Set-Cookie</c> header (an isomorphic string: each character is a byte).</param>
    public bool SetCookie(string url, string setCookieHeader)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(setCookieHeader);
        if (UrlParser.Parse(url, baseUrl: null) is not { } parsed)
            throw new ArgumentException($"'{url}' is not a valid URL", nameof(url));

        var context = new CookieRequestContext(parsed, IsHttp: true, SameSite: CookieSameSiteStatus.Strict, TopLevelOrigin: OriginRecord.FromUrl(parsed));
        return ReceiveSetCookieString(context, setCookieHeader);
    }

    /// <summary>
    /// Copies the cookies of another jar into this one, replacing the cookies with the same name, domain, path and partition:
    /// the cookies of the isolated partition of a cross-site navigational prefetch, when a navigation uses its response
    /// (https://wicg.github.io/nav-speculation/prefetch.html#copy-prefetch-cookies).
    /// </summary>
    internal void CopyFrom(CookieJar source)
    {
        var cookies = source.GetCookies();
        List<CookieChange>? changes = null;
        lock (_lock)
        {
            EvictExpiredCookies(ref changes);
            foreach (var stored in cookies)
            {
                var cookie = CookieEntry.FromStoredCookie(stored);
                cookie.ExpiryTime = stored.Expires is { } expiry ? LimitExpiryTime(expiry) : null;
                var oldIndex = _cookies.FindIndex(existing => existing.Name == cookie.Name && existing.Domain == cookie.Domain && existing.HostOnly == cookie.HostOnly && existing.Path == cookie.Path && existing.PartitionKey == cookie.PartitionKey);
                if (oldIndex >= 0)
                {
                    _cookies.RemoveAt(oldIndex);
                }

                cookie.CreationSequence = ++_creationSequence;
                _cookies.Add(cookie);
                (changes ??= []).Add(new CookieChange(cookie, Deleted: false));
                RemoveExcessCookies(cookie.Domain, ref changes);
            }

            PersistChanges(changes);
        }

        RaiseChanged(changes);
    }

    /// <summary>Removes a cookie (with the same name, domain, path and partition). Returns false when the jar does not contain it.</summary>
    public bool Remove(StoredCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        List<CookieChange>? changes = null;
        var removed = false;
        lock (_lock)
        {
            var index = _cookies.FindIndex(entry => entry.Name == cookie.Name && entry.Domain == cookie.Domain && entry.Path == cookie.Path && entry.HostOnly == cookie.HostOnly && entry.PartitionKey == cookie.PartitionKey);
            if (index >= 0)
            {
                (changes ??= []).Add(new CookieChange(_cookies[index], Deleted: true));
                _cookies.RemoveAt(index);
                removed = true;
                PersistChanges(changes);
            }
        }

        RaiseChanged(changes);
        return removed;
    }

    /// <summary>Removes all the cookies, and the persisted cookies of <see cref="Persistence"/>.</summary>
    public void Clear()
    {
        List<CookieChange>? changes = null;
        lock (_lock)
        {
            Persistence?.ClearAll();
            foreach (var cookie in _cookies)
            {
                (changes ??= []).Add(new CookieChange(cookie, Deleted: true));
            }

            _cookies.Clear();
        }

        RaiseChanged(changes);
    }

    /// <summary>
    /// https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-retrieval-algorithm: the cookie-string of a
    /// retrieval, or null when there is no cookie. The last access time of the cookies is updated.
    /// </summary>
    internal string? GetCookieString(CookieRequestContext context)
    {
        var cookies = Retrieve(context, updateAccessTime: true);
        if (cookies.Count == 0)
            return null;

        var result = new StringBuilder();
        foreach (var cookie in cookies)
        {
            if (result.Length > 0)
            {
                result.Append("; ");
            }

            if (cookie.Name.Length > 0)
            {
                result.Append(cookie.Name).Append('=');
            }

            result.Append(cookie.Value);
        }

        return result.ToString();
    }

    /// <summary>The cookie-list of a retrieval (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-retrieval-algorithm), sorted.</summary>
    internal List<CookieEntry> Retrieve(CookieRequestContext context, bool updateAccessTime)
    {
        List<CookieChange>? changes = null;
        List<CookieEntry> result = [];
        if (context.Url.Host is not { } host || host.Kind == UrlHostKind.Empty)
            return result;

        lock (_lock)
        {
            EvictExpiredCookies(ref changes);
            var partitionKey = GetPartitionKey(context);
            foreach (var cookie in _cookies)
            {
                if (IsInScope(cookie, context, host, partitionKey))
                {
                    result.Add(cookie);
                }
            }

            // Step 4: the cookies with longer paths first, then the cookies created earlier
            result.Sort((a, b) => a.Path.Length != b.Path.Length ? b.Path.Length.CompareTo(a.Path.Length) : a.CreationSequence.CompareTo(b.CreationSequence));
            PersistChanges(changes);
            if (updateAccessTime)
            {
                var now = Now;
                foreach (var cookie in result)
                {
                    cookie.LastAccessTime = now;
                    if (cookie.IsPersisted && cookie.ExpiryTime is not null && now - cookie.PersistedAccessTime >= AccessTimePersistenceInterval)
                    {
                        Persistence!.SetCookie(cookie.ToStoredCookie());
                        cookie.PersistedAccessTime = now;
                    }
                }
            }
        }

        RaiseChanged(changes);
        return result;
    }

    /// <summary>Whether a cookie meets the requirements of step 3 of the retrieval algorithm (the observable cookies of a URL).</summary>
    internal static bool IsInScope(CookieEntry cookie, CookieRequestContext context, UrlHost host, CookiePartitionKey? partitionKey)
    {
        if (cookie.HostOnly ? cookie.Domain != host.Serialized : !DomainMatches(host, cookie.Domain))
            return false;

        if (cookie.PartitionKey is not null && cookie.PartitionKey != partitionKey)
            return false;

        if (!PathMatches(context.Url.SerializePath(), cookie.Path))
            return false;

        if (cookie.SecureOnly && !IsSecure(context.Url))
            return false;

        if (cookie.HttpOnly && !context.IsHttp)
            return false;

        if (cookie.SameSite != CookieSameSite.None && !IsSentInSameSiteContext(cookie, context))
            return false;

        return true;
    }

    /// <summary>The SameSite enforcement of the retrieval (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-retrieval-algorithm, step 3).</summary>
    private static bool IsSentInSameSiteContext(CookieEntry cookie, CookieRequestContext context) => context.SameSite switch
    {
        CookieSameSiteStatus.Strict => true,

        // A cross-site top-level navigation with a safe method (or a same-site request initiated by a cross-site document)
        // sends the Lax cookies (the cookies without SameSite attribute are Lax by default)
        CookieSameSiteStatus.Lax => context.IsHttp && cookie.SameSite is CookieSameSite.Lax or null,
        _ => false,
    };

    /// <summary>
    /// The steps to receive a set-cookie-string (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-set-cookie-header-field)
    /// from the URL of a retrieval context. Returns false when the cookie is ignored.
    /// </summary>
    internal bool ReceiveSetCookieString(CookieRequestContext context, string setCookieString)
    {
        if (ParseSetCookieString(setCookieString, context.Url) is not { } parsed)
            return false;

        return ReceiveCookie(context, parsed.Name, parsed.Value, parsed.Attributes);
    }

    /// <summary>
    /// The storage model (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-storage-model): the
    /// steps when the user agent "receives a cookie". Returns false when the cookie is ignored.
    /// </summary>
    internal bool ReceiveCookie(CookieRequestContext context, string name, string value, CookieAttributes attributes)
    {
        var url = context.Url;
        if (url.Host is not { } requestHost || requestHost.Kind == UrlHostKind.Empty)
            return false;

        // Steps 2-4
        if (name.Length == 0 && value.Length == 0)
            return false;

        if (ContainsControlCharacter(name) || ContainsControlCharacter(value))
            return false;

        if (name.Length + value.Length > MaxNameValuePairSize)
            return false;

        // Step 5
        var now = Now;
        var cookie = new CookieEntry
        {
            Name = name,
            Value = value,
            CreationTime = now,
            LastAccessTime = now,
        };

        // Step 6: Max-Age has precedence over Expires
        if (attributes.MaxAgeExpiry is { } maxAgeExpiry)
        {
            cookie.ExpiryTime = maxAgeExpiry;
        }
        else if (attributes.Expires is { } expires)
        {
            cookie.ExpiryTime = expires;
        }

        // Steps 7-10
        var domainAttribute = attributes.Domain ?? "";
        if (!System.Text.Ascii.IsValid(domainAttribute))
            return false;

        if (domainAttribute.Length > 0 && IsPublicSuffix(domainAttribute))
        {
            if (domainAttribute != requestHost.Serialized)
                return false;

            domainAttribute = "";
        }

        if (domainAttribute.Length > 0)
        {
            if (!DomainMatches(requestHost, domainAttribute))
                return false;

            cookie.HostOnly = false;
            cookie.Domain = domainAttribute;
        }
        else
        {
            cookie.HostOnly = true;
            cookie.Domain = requestHost.Serialized;
        }

        // Step 11
        cookie.Path = attributes.Path ?? GetDefaultPath(url);

        // Steps 12-13
        cookie.SecureOnly = attributes.Secure;
        var isSecure = IsSecure(url);
        if (!isSecure && cookie.SecureOnly)
            return false;

        // Steps 14-15
        cookie.HttpOnly = attributes.HttpOnly;
        if (!context.IsHttp && cookie.HttpOnly)
            return false;

        // Step 17
        cookie.SameSite = attributes.SameSite;

        // Step 18: a cookie that is not SameSite=None can only be set by a same-site request or a top-level navigation
        if (cookie.SameSite != CookieSameSite.None && context.SameSite == CookieSameSiteStatus.CrossSite && !context.IsTopLevelNavigation)
            return false;

        // Step 19
        if (cookie.SameSite == CookieSameSite.None && !cookie.SecureOnly)
            return false;

        // The Partitioned attribute (CHIPS, https://github.com/privacycg/CHIPS): the cookie is keyed by the top-level site, and must be Secure
        if (attributes.Partitioned)
        {
            if (!cookie.SecureOnly)
                return false;

            cookie.PartitionKey = GetPartitionKey(context);
        }

        // Steps 20-22: the prefixes, matched case-insensitively
        if (!CheckPrefixes(cookie, attributes))
            return false;

        List<CookieChange>? changes = null;
        bool stored;
        lock (_lock)
        {
            stored = StoreCookie(cookie, context, isSecure, now, ref changes);
            PersistChanges(changes);
        }

        RaiseChanged(changes);
        return stored;
    }

    /// <summary>Steps 16 and 23-24 of the storage model: stores the cookie in the jar (in the lock of the jar), or returns false when it is ignored.</summary>
    private bool StoreCookie(CookieEntry cookie, CookieRequestContext context, bool isSecure, DateTimeOffset now, ref List<CookieChange>? changes)
    {
        EvictExpiredCookies(ref changes);

        // Step 16: a non-secure cookie cannot overlay a secure cookie
        if (!cookie.SecureOnly && !isSecure)
        {
            foreach (var existing in _cookies)
            {
                if (existing.Name == cookie.Name && existing.SecureOnly && (DomainMatches(existing.Domain, cookie.Domain) || DomainMatches(cookie.Domain, existing.Domain)) && PathMatches(cookie.Path, existing.Path))
                    return false;
            }
        }

        // Step 23
        var oldIndex = _cookies.FindIndex(existing => existing.Name == cookie.Name && existing.Domain == cookie.Domain && existing.HostOnly == cookie.HostOnly && existing.Path == cookie.Path && existing.PartitionKey == cookie.PartitionKey);
        CookieEntry? oldCookie = null;
        if (oldIndex >= 0)
        {
            oldCookie = _cookies[oldIndex];
            if (!context.IsHttp && oldCookie.HttpOnly)
                return false;

            cookie.CreationTime = oldCookie.CreationTime;
            cookie.CreationSequence = oldCookie.CreationSequence;
            _cookies.RemoveAt(oldIndex);

            // The persisted cookie is replaced by the change of the new cookie (if any), except by a session cookie, which is not persisted
            if (oldCookie.IsPersisted)
            {
                if (cookie.ExpiryTime is null)
                {
                    Persistence!.RemoveCookie(oldCookie.ToStoredCookie());
                }
                else
                {
                    cookie.IsPersisted = true;
                }
            }
        }
        else
        {
            cookie.CreationSequence = ++_creationSequence;
        }

        // Step 24. A cookie that expires immediately deletes the cookie it replaces (https://cookiestore.spec.whatwg.org/#cookie-change)
        if (cookie.ExpiryTime is { } expiry && expiry <= now)
        {
            if (oldCookie is not null)
            {
                (changes ??= []).Add(new CookieChange(oldCookie, Deleted: true));
            }
        }
        else
        {
            _cookies.Add(cookie);

            // A cookie replaced by an identical cookie does not change
            if (oldCookie is null || !oldCookie.HasSameValueAndAttributes(cookie))
            {
                (changes ??= []).Add(new CookieChange(cookie, Deleted: false));
            }

            RemoveExcessCookies(cookie.Domain, ref changes);
        }

        return true;
    }

    /// <summary>The cookie prefixes (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-storage-model, steps 20-22).</summary>
    private static bool CheckPrefixes(CookieEntry cookie, CookieAttributes attributes)
    {
        var name = cookie.Name;
        if (StartsWithIgnoreCase(name, "__Secure-") && !cookie.SecureOnly)
            return false;

        // __Host-Http- is __Host- and __Http-
        var isHost = StartsWithIgnoreCase(name, "__Host-");
        var isHttp = StartsWithIgnoreCase(name, "__Http-") || StartsWithIgnoreCase(name, "__Host-Http-");
        // The Path attribute must be "/": a default path of "/" (e.g. an empty Path attribute) is not enough
        // (https://httpwg.org/http-extensions/draft-ietf-httpbis-layered-cookies.html#name-the-__host-prefix)
        if (isHost && !(cookie.SecureOnly && cookie.HostOnly && attributes.ExplicitPath && cookie.Path == "/"))
            return false;

        if (isHttp && !(cookie.SecureOnly && cookie.HttpOnly))
            return false;

        if (name.Length == 0 && IsPrefixedValue(cookie.Value))
            return false;

        return true;
    }

    /// <summary>Whether the value of a cookie without name starts with a cookie prefix (it would be serialized as a prefixed cookie).</summary>
    internal static bool IsPrefixedValue(string value)
        => StartsWithIgnoreCase(value, "__Secure-") || StartsWithIgnoreCase(value, "__Host-") || StartsWithIgnoreCase(value, "__Http-") || StartsWithIgnoreCase(value, "__Host-Http-");

    private static bool StartsWithIgnoreCase(string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The cookies are expired once their expiry time is reached: they are evicted (and are deleted for the change events).</summary>
    private void EvictExpiredCookies(ref List<CookieChange>? changes)
    {
        var now = Now;
        for (var i = _cookies.Count - 1; i >= 0; i--)
        {
            if (_cookies[i].ExpiryTime is { } expiry && expiry <= now)
            {
                (changes ??= []).Add(new CookieChange(_cookies[i], Deleted: true));
                _cookies.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Removes the excess cookies (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-storage-model):
    /// the non-secure cookies of the domain then its other cookies, then all the cookies, the least recently accessed first.
    /// </summary>
    private void RemoveExcessCookies(string domain, ref List<CookieChange>? changes)
    {
        var domainCookies = _cookies.Where(cookie => cookie.Domain == domain).ToList();
        if (domainCookies.Count > MaxCookiesPerDomain)
        {
            foreach (var cookie in domainCookies.OrderBy(cookie => cookie.SecureOnly).ThenBy(cookie => cookie.LastAccessTime).ThenBy(cookie => cookie.CreationSequence).Take(domainCookies.Count - MaxCookiesPerDomain))
            {
                _cookies.Remove(cookie);
                (changes ??= []).Add(new CookieChange(cookie, Deleted: true));
            }
        }

        if (_cookies.Count > MaxCookies)
        {
            foreach (var cookie in _cookies.OrderBy(cookie => cookie.LastAccessTime).ThenBy(cookie => cookie.CreationSequence).Take(_cookies.Count - MaxCookies).ToList())
            {
                _cookies.Remove(cookie);
                (changes ??= []).Add(new CookieChange(cookie, Deleted: true));
            }
        }
    }

    /// <summary>
    /// Reports the changes of the jar to <see cref="Persistence"/>, in the lock of the jar (so that the persistence receives
    /// the changes in order): the persistent cookies that are created or changed, and the persisted cookies that are removed.
    /// </summary>
    private void PersistChanges(List<CookieChange>? changes)
    {
        if (Persistence is not { } persistence || changes is null)
            return;

        foreach (var (cookie, deleted) in changes)
        {
            if (deleted)
            {
                if (cookie.IsPersisted)
                {
                    persistence.RemoveCookie(cookie.ToStoredCookie());
                    cookie.IsPersisted = false;
                }
            }
            else if (cookie.ExpiryTime is not null)
            {
                persistence.SetCookie(cookie.ToStoredCookie());
                cookie.IsPersisted = true;
                cookie.PersistedAccessTime = cookie.LastAccessTime;
            }
        }
    }

    /// <summary>
    /// Adds the persisted cookies to the empty jar: the invalid and expired cookies are ignored (and the expired ones are
    /// removed from the persistence), the expiry times are limited to 400 days, a cookie replaces the previous cookie with
    /// the same name, domain, host-only flag, path and partition key, and the excess cookies are removed.
    /// </summary>
    private void Load(CookiePersistence persistence)
    {
        var now = Now;
        var cookies = new Dictionary<(string Name, string Domain, bool HostOnly, string Path, CookiePartitionKey? PartitionKey), CookieEntry>();
        foreach (var stored in persistence.LoadCookies())
        {
            if (stored is null || stored.Name is null || stored.Value is null || string.IsNullOrEmpty(stored.Domain) || stored.Path is null || !stored.Path.StartsWith('/', StringComparison.Ordinal))
                continue;

            if (stored.Expires is { } expires && expires <= now)
            {
                persistence.RemoveCookie(stored);
                continue;
            }

            var cookie = CookieEntry.FromStoredCookie(stored);
            cookie.ExpiryTime = stored.Expires is { } expiry ? LimitExpiryTime(expiry) : null;
            cookie.IsPersisted = true;
            cookie.PersistedAccessTime = cookie.LastAccessTime;
            cookies[(cookie.Name, cookie.Domain, cookie.HostOnly, cookie.Path, cookie.PartitionKey)] = cookie;
        }

        List<CookieChange>? changes = null;
        lock (_lock)
        {
            // The order of creation (the order of the Cookie header for the paths of the same length)
            foreach (var cookie in cookies.Values.OrderBy(cookie => cookie.CreationTime))
            {
                cookie.CreationSequence = ++_creationSequence;
                _cookies.Add(cookie);
            }

            foreach (var domain in cookies.Values.Select(cookie => cookie.Domain).Distinct(StringComparer.Ordinal).ToList())
            {
                RemoveExcessCookies(domain, ref changes);
            }

            PersistChanges(changes);
        }
    }

    /// <summary>Notifies a cookie store of the changes of the jar, until it is garbage collected.</summary>
    internal void AddListener(ICookieChangeListener listener)
    {
        lock (_lock)
        {
            _listeners.Add(new WeakReference<ICookieChangeListener>(listener));
        }
    }

    private void RaiseChanged(List<CookieChange>? changes)
    {
        if (changes is not { Count: > 0 })
            return;

        List<ICookieChangeListener> listeners = [];
        lock (_lock)
        {
            for (var i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i].TryGetTarget(out var listener))
                {
                    listeners.Add(listener);
                }
                else
                {
                    _listeners.RemoveAt(i);
                }
            }
        }

        for (var i = listeners.Count - 1; i >= 0; i--)
        {
            listeners[i].OnCookiesChanged(this, changes);
        }
    }

    /// <summary>
    /// The partition key of the partitioned cookies of a context (https://github.com/privacycg/CHIPS): the site of the
    /// top-level origin, and the cross-site ancestor bit (a cookie partitioned by a top-level site is not available to a
    /// same-site iframe that has a cross-site ancestor).
    /// </summary>
    internal static CookiePartitionKey? GetPartitionKey(CookieRequestContext context)
    {
        var topLevel = context.TopLevelOrigin ?? OriginRecord.FromUrl(context.Url);
        if (topLevel.IsOpaque)
            return null;

        return new CookiePartitionKey
        {
            TopLevelSite = topLevel.Scheme + "://" + (PublicSuffixList.GetRegistrableDomain(topLevel.Host) ?? topLevel.Host.Serialized),
            HasCrossSiteAncestor = context.CrossSiteAncestor,
        };
    }

    /// <summary>Whether the URL denotes a "secure" connection: a potentially trustworthy origin (https, wss, localhost).</summary>
    internal static bool IsSecure(UrlRecord url) => ReferrerPolicies.IsOriginPotentiallyTrustworthy(url);

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (c is (>= '\x00' and <= '\x08') or (>= '\x0A' and <= '\x1F') or '\x7F')
                return true;
        }

        return false;
    }

    /// <summary>Whether a domain attribute is a public suffix (the user agent rejects the public suffixes).</summary>
    private static bool IsPublicSuffix(string domain)
    {
        if (HostParser.Parse(domain) is not { Kind: UrlHostKind.Domain } host)
            return false;

        return PublicSuffixList.GetPublicSuffix(host) == host.Serialized;
    }

    /// <summary>https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-domain-matching</summary>
    internal static bool DomainMatches(UrlHost host, string domain)
    {
        if (host.Serialized == domain)
            return true;

        return host.Kind == UrlHostKind.Domain && IsDomainSuffix(host.Serialized, domain);
    }

    /// <summary>The domain matching of two cookie domains (the domain of a cookie is a host name unless it is an IP address).</summary>
    private static bool DomainMatches(string value, string domain)
    {
        if (value == domain)
            return true;

        return HostParser.Parse(value) is { Kind: UrlHostKind.Domain } && IsDomainSuffix(value, domain);
    }

    private static bool IsDomainSuffix(string value, string domain)
        => domain.Length > 0 && value.Length > domain.Length && value.EndsWith(domain, StringComparison.Ordinal) && value[value.Length - domain.Length - 1] == '.';

    /// <summary>https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-paths-and-path-match</summary>
    internal static bool PathMatches(string requestPath, string cookiePath)
    {
        if (requestPath == cookiePath)
            return true;

        if (!requestPath.StartsWith(cookiePath, StringComparison.Ordinal))
            return false;

        return cookiePath.EndsWith('/', StringComparison.Ordinal) || requestPath[cookiePath.Length] == '/';
    }

    /// <summary>The default-path of a cookie (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-paths-and-path-match).</summary>
    internal static string GetDefaultPath(UrlRecord url)
    {
        var path = url.HasOpaquePath ? url.OpaquePath! : url.SerializePath();
        if (path.Length == 0 || path[0] != '/')
            return "/";

        var lastSlash = path.LastIndexOf('/', StringComparison.Ordinal);
        return lastSlash == 0 ? "/" : path[..lastSlash];
    }

    /// <summary>
    /// Parses a set-cookie-string (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-set-cookie-header-field):
    /// its name, its value and its attributes, or null when it is ignored.
    /// </summary>
    internal (string Name, string Value, CookieAttributes Attributes)? ParseSetCookieString(string setCookieString, UrlRecord url)
    {
        // Step 1
        if (ContainsControlCharacter(setCookieString))
            return null;

        // Step 2
        var semicolon = setCookieString.IndexOf(';', StringComparison.Ordinal);
        var nameValuePair = semicolon < 0 ? setCookieString : setCookieString[..semicolon];
        var unparsedAttributes = semicolon < 0 ? "" : setCookieString[semicolon..];

        // Steps 3-4
        string name;
        string value;
        var equals = nameValuePair.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0)
        {
            name = "";
            value = TrimWhitespace(nameValuePair);
        }
        else
        {
            name = TrimWhitespace(nameValuePair[..equals]);
            value = TrimWhitespace(nameValuePair[(equals + 1)..]);
        }

        // Step 5
        if (name.Length + value.Length > MaxNameValuePairSize)
            return null;

        var attributes = new CookieAttributes();
        while (unparsedAttributes.Length > 0)
        {
            // Steps 2-3
            unparsedAttributes = unparsedAttributes[1..];
            var next = unparsedAttributes.IndexOf(';', StringComparison.Ordinal);
            var cookieAv = next < 0 ? unparsedAttributes : unparsedAttributes[..next];
            unparsedAttributes = next < 0 ? "" : unparsedAttributes[next..];

            // Steps 4-5
            var avEquals = cookieAv.IndexOf('=', StringComparison.Ordinal);
            var attributeName = TrimWhitespace(avEquals < 0 ? cookieAv : cookieAv[..avEquals]);
            var attributeValue = avEquals < 0 ? "" : TrimWhitespace(cookieAv[(avEquals + 1)..]);

            // Step 6
            if (attributeValue.Length > MaxAttributeValueSize)
                continue;

            ProcessAttribute(ref attributes, attributeName, attributeValue, url);
        }

        return (name, value, attributes);
    }

    /// <summary>Processes an attribute of a set-cookie-string (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-expires-attribute and the next sections).</summary>
    private void ProcessAttribute(ref CookieAttributes attributes, string name, string value, UrlRecord url)
    {
        if (InfraAscii.EqualsIgnoreAsciiCase(name, "Expires"))
        {
            if (ParseCookieDate(value) is { } expiryTime)
            {
                attributes.Expires = LimitExpiryTime(expiryTime);
            }
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "Max-Age"))
        {
            if (ParseMaxAge(value) is { } deltaSeconds)
            {
                attributes.MaxAgeExpiry = GetMaxAgeExpiryTime(deltaSeconds);
            }
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "Domain"))
        {
            // A Domain attribute that is a single dot is invalid (it does not domain-match any host), as in the browsers: it is
            // not the empty domain of a host-only cookie
            var domain = value.StartsWith('.', StringComparison.Ordinal) && value.Length > 1 ? value[1..] : value;
            attributes.Domain = InfraAscii.ToAsciiLowercase(domain);
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "Path"))
        {
            var isExplicit = value.Length > 0 && value[0] == '/';
            attributes.Path = isExplicit ? value : GetDefaultPath(url);
            attributes.ExplicitPath = isExplicit;
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "Secure"))
        {
            attributes.Secure = true;
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "HttpOnly"))
        {
            attributes.HttpOnly = true;
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "SameSite"))
        {
            // An unknown value is the default enforcement
            attributes.SameSite = InfraAscii.EqualsIgnoreAsciiCase(value, "None") ? CookieSameSite.None
                : InfraAscii.EqualsIgnoreAsciiCase(value, "Strict") ? CookieSameSite.Strict
                : InfraAscii.EqualsIgnoreAsciiCase(value, "Lax") ? CookieSameSite.Lax
                : null;
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(name, "Partitioned"))
        {
            attributes.Partitioned = true;
        }
    }

    /// <summary>The expiry time of a Max-Age attribute (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-max-age-attribute, steps 5-7).</summary>
    internal DateTimeOffset GetMaxAgeExpiryTime(long deltaSeconds)
    {
        if (deltaSeconds <= 0)
            return DateTimeOffset.MinValue;

        return deltaSeconds >= (long)MaxAge.TotalSeconds ? Now + MaxAge : Now.AddSeconds(deltaSeconds);
    }

    /// <summary>The expiry time of an Expires attribute, limited to 400 days in the future (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-cookie-lifetime-limits).</summary>
    internal DateTimeOffset LimitExpiryTime(DateTimeOffset expiryTime)
    {
        var limit = Now + MaxAge;
        return expiryTime > limit ? limit : expiryTime;
    }

    /// <summary>The value of a Max-Age attribute (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-max-age-attribute, steps 1-4).</summary>
    private static long? ParseMaxAge(string value)
    {
        if (value.Length == 0)
            return null;

        var negative = value[0] == '-';
        var digits = negative ? value.AsSpan(1) : value.AsSpan();
        if (digits.Length == 0)
            return null;

        long result = 0;
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
                return null;

            // A large value is limited to the maximum age anyway
            result = result > long.MaxValue / 20 ? long.MaxValue / 20 : (result * 10) + (c - '0');
        }

        return negative ? -result : result;
    }

    /// <summary>Parses a cookie-date (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-dates), or returns null.</summary>
    internal static DateTimeOffset? ParseCookieDate(string value)
    {
        int? hour = null, minute = null, second = null, dayOfMonth = null, month = null, year = null;
        var index = 0;
        while (index < value.Length)
        {
            // Skip the delimiters, then read a date-token
            while (index < value.Length && IsDelimiter(value[index]))
            {
                index++;
            }

            var start = index;
            while (index < value.Length && !IsDelimiter(value[index]))
            {
                index++;
            }

            if (start == index)
                break;

            var token = value.AsSpan(start, index - start);
            if (hour is null && TryParseTime(token, out var h, out var m, out var s))
            {
                (hour, minute, second) = (h, m, s);
            }
            else if (dayOfMonth is null && TryParseDigits(token, 1, 2, out var day))
            {
                dayOfMonth = day;
            }
            else if (month is null && TryParseMonth(token, out var parsedMonth))
            {
                month = parsedMonth;
            }
            else if (year is null && TryParseDigits(token, 2, 4, out var parsedYear))
            {
                year = parsedYear;
            }
        }

        if (year is >= 70 and <= 99)
        {
            year += 1900;
        }
        else if (year is >= 0 and <= 69)
        {
            year += 2000;
        }

        if (hour is null || dayOfMonth is null || month is null || year is null)
            return null;

        if (dayOfMonth is < 1 or > 31 || year < 1601 || hour > 23 || minute > 59 || second > 59)
            return null;

        if (dayOfMonth > DateTime.DaysInMonth(year.Value, month.Value))
            return null;

        return new DateTimeOffset(year.Value, month.Value, dayOfMonth.Value, hour.Value, minute!.Value, second!.Value, TimeSpan.Zero);

        static bool IsDelimiter(char c) => c is '\x09' or (>= '\x20' and <= '\x2F') or (>= '\x3B' and <= '\x40') or (>= '\x5B' and <= '\x60') or (>= '\x7B' and <= '\x7E');

        // 1*{min}DIGIT [ non-digit *OCTET ] with at most {max} digits
        static bool TryParseDigits(ReadOnlySpan<char> token, int min, int max, out int result)
        {
            result = 0;
            var count = 0;
            while (count < token.Length && char.IsAsciiDigit(token[count]))
            {
                count++;
            }

            if (count < min || count > max)
                return false;

            result = int.Parse(token[..count], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        // time-field ":" time-field ":" time-field [ non-digit *OCTET ]
        static bool TryParseTime(ReadOnlySpan<char> token, out int hour, out int minute, out int second)
        {
            hour = minute = second = 0;
            var fields = new int[3];
            var position = 0;
            for (var i = 0; i < 3; i++)
            {
                var start = position;
                while (position < token.Length && position - start < 2 && char.IsAsciiDigit(token[position]))
                {
                    position++;
                }

                if (position == start)
                    return false;

                fields[i] = int.Parse(token[start..position], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
                if (i < 2)
                {
                    if (position >= token.Length || token[position] != ':')
                        return false;

                    position++;
                }
            }

            if (position < token.Length && char.IsAsciiDigit(token[position]))
                return false;

            (hour, minute, second) = (fields[0], fields[1], fields[2]);
            return true;
        }

        static bool TryParseMonth(ReadOnlySpan<char> token, out int month)
        {
            month = 0;
            if (token.Length < 3)
                return false;

            ReadOnlySpan<string> months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
            for (var i = 0; i < months.Length; i++)
            {
                if (token[..3].Equals(months[i], StringComparison.OrdinalIgnoreCase))
                {
                    month = i + 1;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Removes the leading and trailing WSP characters (space and horizontal tab).</summary>
    internal static string TrimWhitespace(string value) => value.Trim(' ', '\t');
}

/// <summary>
/// The same-site flag of a cookie (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-samesite-attribute),
/// when it is not "Default", and the <c>CookieSameSite</c> enumeration of the Cookie Store API (https://cookiestore.spec.whatwg.org/#enumdef-cookiesamesite).
/// </summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#enumdef-cookiesamesite"/>
[IdlInterface("CookieSameSite", "https://cookiestore.spec.whatwg.org/#enumdef-cookiesamesite")]
public enum CookieSameSite
{
    /// <summary>The cookie is only sent with the same-site requests (<c>SameSite=Strict</c>).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiesamesite-strict"/>
    [IdlMember("strict", "https://cookiestore.spec.whatwg.org/#dom-cookiesamesite-strict")]
    Strict,

    /// <summary>The cookie is sent with the same-site requests and the cross-site top-level navigations with a safe method (<c>SameSite=Lax</c>).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiesamesite-lax"/>
    [IdlMember("lax", "https://cookiestore.spec.whatwg.org/#dom-cookiesamesite-lax")]
    Lax,

    /// <summary>The cookie is sent with the cross-site requests (<c>SameSite=None</c>).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiesamesite-none"/>
    [IdlMember("none", "https://cookiestore.spec.whatwg.org/#dom-cookiesamesite-none")]
    None,
}

/// <summary>A cookie of a <see cref="CookieJar"/> (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-storage-model).</summary>
public sealed class StoredCookie
{
    /// <summary>Creates a cookie (e.g. a cookie loaded by a <see cref="CookiePersistence"/>).</summary>
    public StoredCookie()
    {
    }

    /// <summary>Gets the name of the cookie (an isomorphic string: each character is a byte).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the value of the cookie (an isomorphic string: each character is a byte).</summary>
    public required string Value { get; init; }

    /// <summary>Gets the domain of the cookie: the host of the URL that set it, or the domain of its <c>Domain</c> attribute (see <see cref="HostOnly"/>).</summary>
    public required string Domain { get; init; }

    /// <summary>Gets the path of the cookie.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the expiry time of the cookie, or null for a session cookie (without <c>Expires</c> or <c>Max-Age</c> attribute).</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>Gets the creation time of the cookie (the time of the cookie it replaced, if any).</summary>
    public DateTimeOffset CreationTime { get; init; }

    /// <summary>Gets whether the cookie is only sent to the host of <see cref="Domain"/> (it has no <c>Domain</c> attribute).</summary>
    public bool HostOnly { get; init; }

    /// <summary>Gets whether the cookie is only sent over secure connections (the <c>Secure</c> attribute).</summary>
    public bool Secure { get; init; }

    /// <summary>Gets whether the cookie is hidden from the scripts (the <c>HttpOnly</c> attribute).</summary>
    public bool HttpOnly { get; init; }

    /// <summary>Gets the same-site flag of the cookie, or null when it has no valid <c>SameSite</c> attribute (the cookie is then <see cref="CookieSameSite.Lax"/> by default).</summary>
    public CookieSameSite? SameSite { get; init; }

    /// <summary>Gets the partition key of a partitioned cookie (a cookie with the <c>Partitioned</c> attribute), or null.</summary>
    public CookiePartitionKey? PartitionKey { get; init; }

    /// <summary>
    /// Gets the last time the cookie was sent (or created), which orders the eviction of the excess cookies. The jar updates
    /// the persisted access time at most once a minute. The default value is the creation time.
    /// </summary>
    public DateTimeOffset LastAccessTime { get; init; }
}

/// <summary>A cookie of a <see cref="CookieJar"/>, with its mutable last access time.</summary>
internal sealed class CookieEntry
{
    public string Name { get; init; } = "";

    public string Value { get; init; } = "";

    public string Domain { get; set; } = "";

    public string Path { get; set; } = "/";

    public DateTimeOffset? ExpiryTime { get; set; }

    public DateTimeOffset CreationTime { get; set; }

    /// <summary>The order of creation of the cookies, which have the same creation time when they are created in the same tick.</summary>
    public long CreationSequence { get; set; }

    public DateTimeOffset LastAccessTime { get; set; }

    public bool HostOnly { get; set; }

    public bool SecureOnly { get; set; }

    public bool HttpOnly { get; set; }

    public CookieSameSite? SameSite { get; set; }

    public CookiePartitionKey? PartitionKey { get; set; }

    /// <summary>Whether the cookie is stored by the persistence of the jar (it was loaded, or its last change was persisted).</summary>
    public bool IsPersisted { get; set; }

    /// <summary>The last access time stored by the persistence of the jar.</summary>
    public DateTimeOffset PersistedAccessTime { get; set; }

    public bool HasSameValueAndAttributes(CookieEntry other)
        => Value == other.Value && ExpiryTime == other.ExpiryTime && SecureOnly == other.SecureOnly && HttpOnly == other.HttpOnly && SameSite == other.SameSite;

    public StoredCookie ToStoredCookie() => new()
    {
        Name = Name,
        Value = Value,
        Domain = Domain,
        Path = Path,
        Expires = ExpiryTime,
        CreationTime = CreationTime,
        HostOnly = HostOnly,
        Secure = SecureOnly,
        HttpOnly = HttpOnly,
        SameSite = SameSite,
        PartitionKey = PartitionKey,
        LastAccessTime = LastAccessTime,
    };

    /// <summary>Creates a cookie from a persisted cookie (without expiry time and creation sequence).</summary>
    public static CookieEntry FromStoredCookie(StoredCookie cookie) => new()
    {
        Name = cookie.Name,
        Value = cookie.Value,
        Domain = cookie.Domain,
        Path = cookie.Path,
        CreationTime = cookie.CreationTime,
        LastAccessTime = cookie.LastAccessTime == default ? cookie.CreationTime : cookie.LastAccessTime,
        HostOnly = cookie.HostOnly,
        SecureOnly = cookie.Secure,
        HttpOnly = cookie.HttpOnly,
        SameSite = cookie.SameSite,
        PartitionKey = cookie.PartitionKey,
    };
}

/// <summary>The processed attributes of a cookie (the cookie-attribute-list, the last attribute of each name wins).</summary>
internal struct CookieAttributes
{
    public DateTimeOffset? Expires;
    public DateTimeOffset? MaxAgeExpiry;
    public string? Domain;
    public string? Path;

    /// <summary>Whether the path is the value of the last Path attribute (not the default path of an empty or invalid value).</summary>
    public bool ExplicitPath;
    public bool Secure;
    public bool HttpOnly;
    public CookieSameSite? SameSite;
    public bool Partitioned;
}

/// <summary>A change of the cookies of a jar (https://cookiestore.spec.whatwg.org/#cookie-change).</summary>
internal readonly record struct CookieChange(CookieEntry Cookie, bool Deleted);

/// <summary>The same-site status of a retrieval (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-same-site-and-cross-site-requ).</summary>
internal enum CookieSameSiteStatus
{
    /// <summary>A same-site request initiated by a same-site document: all the cookies are sent.</summary>
    Strict,

    /// <summary>
    /// A cross-site top-level navigation, or a same-site request initiated by a cross-site document, with a safe method: the
    /// Lax cookies are sent (and the request can set the cookies).
    /// </summary>
    Lax,

    /// <summary>The same as <see cref="Lax"/> with an unsafe method: the Lax cookies are not sent, but the request can set the cookies.</summary>
    LaxUnsafe,

    /// <summary>A cross-site request: only the SameSite=None cookies are sent and set.</summary>
    CrossSite,
}

/// <summary>
/// A retrieval or a reception of cookies: the URL, whether it is an HTTP request or a "non-HTTP" API (a script), its
/// same-site status, whether it is a top-level navigation, and the top-level origin (the partition key of the partitioned cookies).
/// </summary>
/// The partitioned cookies are keyed by the site of the top-level origin and whether the context has a cross-site ancestor
/// (or is a cross-site request).
internal readonly record struct CookieRequestContext(UrlRecord Url, bool IsHttp, CookieSameSiteStatus SameSite, OriginRecord? TopLevelOrigin, bool IsTopLevelNavigation = false, bool CrossSiteAncestor = false);
