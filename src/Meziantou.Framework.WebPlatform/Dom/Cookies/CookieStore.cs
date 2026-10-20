using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://cookiestore.spec.whatwg.org/#dictdef-cookiestoregetoptions</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#dictdef-cookiestoregetoptions"/>
[IdlInterface("CookieStoreGetOptions", "https://cookiestore.spec.whatwg.org/#dictdef-cookiestoregetoptions")]
public sealed class CookieStoreGetOptions
{
    /// <summary>Gets the name of the cookies, or null when the member is not present.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoregetoptions-name"/>
    [IdlMember("name", "https://cookiestore.spec.whatwg.org/#dom-cookiestoregetoptions-name")]
    public string? Name { get; init; }

    /// <summary>Gets the URL of the cookies (the creation URL of the document, possibly without its fragment), or null when the member is not present.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoregetoptions-url"/>
    [IdlMember("url", "https://cookiestore.spec.whatwg.org/#dom-cookiestoregetoptions-url")]
    public string? Url { get; init; }
}

/// <summary>https://cookiestore.spec.whatwg.org/#dictdef-cookieinit</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#dictdef-cookieinit"/>
[IdlInterface("CookieInit", "https://cookiestore.spec.whatwg.org/#dictdef-cookieinit")]
public sealed class CookieInit
{
    /// <summary>Gets the name of the cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-name"/>
    [IdlMember("name", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-name")]
    public required string Name { get; init; }

    /// <summary>Gets the value of the cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-value"/>
    [IdlMember("value", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-value")]
    public required string Value { get; init; }

    /// <summary>Gets the expiry time, in milliseconds since the Unix epoch, or null.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-expires"/>
    [IdlMember("expires", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-expires")]
    public double? Expires { get; init; }

    /// <summary>Gets the domain of the cookie, or null for a host-only cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-domain"/>
    [IdlMember("domain", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-domain")]
    public string? Domain { get; init; }

    /// <summary>Gets the path of the cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-path"/>
    [IdlMember("path", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-path")]
    public string Path { get; init; } = "/";

    /// <summary>Gets the <c>SameSite</c> policy of the cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-samesite"/>
    [IdlMember("sameSite", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-samesite")]
    public CookieSameSite SameSite { get; init; } = CookieSameSite.Strict;

    /// <summary>Gets a value indicating whether the cookie is a partitioned cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-partitioned"/>
    [IdlMember("partitioned", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-partitioned")]
    public bool Partitioned { get; init; }

    /// <summary>Gets the maximum age, in seconds, or null.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookieinit-maxage"/>
    [IdlMember("maxAge", "https://cookiestore.spec.whatwg.org/#dom-cookieinit-maxage")]
    public long? MaxAge { get; init; }
}

/// <summary>https://cookiestore.spec.whatwg.org/#dictdef-cookiestoredeleteoptions</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#dictdef-cookiestoredeleteoptions"/>
[IdlInterface("CookieStoreDeleteOptions", "https://cookiestore.spec.whatwg.org/#dictdef-cookiestoredeleteoptions")]
public sealed class CookieStoreDeleteOptions
{
    /// <summary>Gets the name of the cookie to delete.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-name"/>
    [IdlMember("name", "https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-name")]
    public required string Name { get; init; }

    /// <summary>Gets the domain of the cookie to delete, or null for a host-only cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-domain"/>
    [IdlMember("domain", "https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-domain")]
    public string? Domain { get; init; }

    /// <summary>Gets the path of the cookie to delete.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-path"/>
    [IdlMember("path", "https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-path")]
    public string Path { get; init; } = "/";

    /// <summary>Gets a value indicating whether the cookie to delete is a partitioned cookie.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-partitioned"/>
    [IdlMember("partitioned", "https://cookiestore.spec.whatwg.org/#dom-cookiestoredeleteoptions-partitioned")]
    public bool Partitioned { get; init; }
}

/// <summary>A cookie returned by the Cookie Store API (https://cookiestore.spec.whatwg.org/#dictdef-cookielistitem).</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#dictdef-cookielistitem"/>
[IdlInterface("CookieListItem", "https://cookiestore.spec.whatwg.org/#dictdef-cookielistitem")]
public sealed class CookieListItem
{
    /// <summary>Gets the name of the cookie (decoded from UTF-8), or null when the member is not present.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookielistitem-name"/>
    [IdlMember("name", "https://cookiestore.spec.whatwg.org/#dom-cookielistitem-name")]
    public string? Name { get; init; }

    /// <summary>Gets the value of the cookie (decoded from UTF-8), or null when the member is not present (a deleted cookie of a change event).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookielistitem-value"/>
    [IdlMember("value", "https://cookiestore.spec.whatwg.org/#dom-cookielistitem-value")]
    public string? Value { get; init; }

    /// <summary>https://cookiestore.spec.whatwg.org/#create-a-cookielistitem</summary>
    internal static CookieListItem Create(CookieEntry cookie)
        => new() { Name = InfraString.Utf8DecodeWithoutBom(cookie.Name), Value = InfraString.Utf8DecodeWithoutBom(cookie.Value) };
}

/// <summary>
/// The Cookie Store API of a window (https://cookiestore.spec.whatwg.org/#cookiestore): reads and writes the script-visible
/// cookies of <see cref="BrowsingContextOptions.CookieJar"/> for the creation URL of the document (which
/// <c>history.pushState()</c> does not change), and fires <c>change</c> events when they change.
/// </summary>
/// <remarks>
/// The cookies set by this API are Secure. A document without cookie jar has no cookies: the cookies it sets are ignored.
/// The change events are fired for the changes of the jar observable from the document (the cookies of other documents,
/// of the responses, and of <c>document.cookie</c>).
/// </remarks>
/// <seealso href="https://cookiestore.spec.whatwg.org/#cookiestore"/>
[IdlInterface("CookieStore", "https://cookiestore.spec.whatwg.org/#cookiestore")]
public sealed class CookieStore : EventTarget, ICookieChangeListener
{
    internal CookieStore(Window window)
    {
        Window = window;
        window.Options.CookieJar?.AddListener(this);
    }

    internal Window Window { get; }

    internal override Window? RelevantGlobal => Window;

    /// <summary>The event handler of the change events (https://cookiestore.spec.whatwg.org/#dom-cookiestore-onchange).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-onchange"/>
    [IdlMember("onchange", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>Returns the first script-visible cookie with the name (https://cookiestore.spec.whatwg.org/#dom-cookiestore-get), or null.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-get"/>
    [IdlMember("get", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-get")]
    public Task<CookieListItem?> Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (GetCreationUrl() is not { } url)
            return Task.FromException<CookieListItem?>(OpaqueOriginError());

        return Resolve<CookieListItem?>(() => QueryCookies(url, name).FirstOrDefault());
    }

    /// <summary>Returns the first script-visible cookie matching the options (https://cookiestore.spec.whatwg.org/#dom-cookiestore-get-options), or null.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-get-options"/>
    [IdlMember("get", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-get-options")]
    public Task<CookieListItem?> Get(CookieStoreGetOptions? options = null)
    {
        if (GetCreationUrl() is not { } url)
            return Task.FromException<CookieListItem?>(OpaqueOriginError());

        if (options is null || (options.Name is null && options.Url is null))
            return Task.FromException<CookieListItem?>(new TypeErrorException("The options must have a name or a URL"));

        if (GetOptionsUrl(url, options) is not { } optionsUrl)
            return Task.FromException<CookieListItem?>(new TypeErrorException("The URL must be the URL of the document"));

        return Resolve<CookieListItem?>(() => QueryCookies(optionsUrl, options.Name).FirstOrDefault());
    }

    /// <summary>Returns the script-visible cookies with the name (https://cookiestore.spec.whatwg.org/#dom-cookiestore-getall).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-getall"/>
    [IdlMember("getAll", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-getall")]
    public Task<IReadOnlyList<CookieListItem>> GetAll(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (GetCreationUrl() is not { } url)
            return Task.FromException<IReadOnlyList<CookieListItem>>(OpaqueOriginError());

        return Resolve<IReadOnlyList<CookieListItem>>(() => QueryCookies(url, name));
    }

    /// <summary>Returns the script-visible cookies matching the options (https://cookiestore.spec.whatwg.org/#dom-cookiestore-getall-options).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-getall-options"/>
    [IdlMember("getAll", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-getall-options")]
    public Task<IReadOnlyList<CookieListItem>> GetAll(CookieStoreGetOptions? options = null)
    {
        if (GetCreationUrl() is not { } url)
            return Task.FromException<IReadOnlyList<CookieListItem>>(OpaqueOriginError());

        if (options is not null && GetOptionsUrl(url, options) is not { } optionsUrl)
            return Task.FromException<IReadOnlyList<CookieListItem>>(new TypeErrorException("The URL must be the URL of the document"));

        var queryUrl = options is null ? url : GetOptionsUrl(url, options)!;
        return Resolve<IReadOnlyList<CookieListItem>>(() => QueryCookies(queryUrl, options?.Name));
    }

    /// <summary>Sets a cookie for the path <c>/</c>, Secure and SameSite=Strict (https://cookiestore.spec.whatwg.org/#dom-cookiestore-set).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-set"/>
    [IdlMember("set", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-set")]
    public Task Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (GetCreationUrl() is not { } url)
            return Task.FromException(OpaqueOriginError());

        return SetCookie(url, name, value, expires: null, domain: null, "/", CookieSameSite.Strict, partitioned: false, maxAge: null);
    }

    /// <summary>Sets a cookie (https://cookiestore.spec.whatwg.org/#dom-cookiestore-set-options).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-set-options"/>
    [IdlMember("set", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-set-options")]
    public Task Set(CookieInit options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (GetCreationUrl() is not { } url)
            return Task.FromException(OpaqueOriginError());

        return SetCookie(url, options.Name, options.Value, options.Expires, options.Domain, options.Path, options.SameSite, options.Partitioned, options.MaxAge);
    }

    /// <summary>Deletes the cookie with the name and the path <c>/</c> (https://cookiestore.spec.whatwg.org/#dom-cookiestore-delete).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-delete"/>
    [IdlMember("delete", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-delete")]
    public Task Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (GetCreationUrl() is not { } url)
            return Task.FromException(OpaqueOriginError());

        // The specification passes true for partitioned, but the browsers delete the cookie that is not partitioned (as delete({ name }))
        return DeleteCookie(url, name, domain: null, "/", partitioned: false);
    }

    /// <summary>Deletes a cookie (https://cookiestore.spec.whatwg.org/#dom-cookiestore-delete-options).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestore-delete-options"/>
    [IdlMember("delete", "https://cookiestore.spec.whatwg.org/#dom-cookiestore-delete-options")]
    public Task Delete(CookieStoreDeleteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (GetCreationUrl() is not { } url)
            return Task.FromException(OpaqueOriginError());

        return DeleteCookie(url, options.Name, options.Domain, options.Path, options.Partitioned);
    }

    private static DomException OpaqueOriginError() => new("The document has an opaque origin", "SecurityError");

    /// <summary>The creation URL of the relevant settings object, or null when its origin is opaque.</summary>
    private UrlRecord? GetCreationUrl()
    {
        var document = Window.Document;
        return document.Origin.IsOpaque ? null : document.GetCookieUrl(creationUrl: true);
    }

    /// <summary>
    /// The URL of the options of get() and getAll(): the URL parsed with the API base URL, which must be the creation URL
    /// (excluding the fragments). Returns the creation URL when the options have no URL, and null on failure.
    /// </summary>
    private UrlRecord? GetOptionsUrl(UrlRecord url, CookieStoreGetOptions options)
    {
        if (options.Url is not { } optionsUrl)
            return url;

        var parsed = UrlParser.Parse(optionsUrl, Window.Document.DocumentBaseUrl);
        if (parsed is null || parsed.Serialize(excludeFragment: true) != url.Serialize(excludeFragment: true))
            return null;

        if (parsed.SerializeOrigin() != url.SerializeOrigin())
            return null;

        return parsed;
    }

    /// <summary>Resolves the promise from a task: the cookies are read "in parallel".</summary>
    private Task<T> Resolve<T>(Func<T> result)
    {
        var value = result();
        var completion = new TaskCompletionSource<T>();
        Window.Document.QueueGlobalTask(() => completion.TrySetResult(value));
        return completion.Task;
    }

    private Task Complete(bool success)
    {
        var completion = new TaskCompletionSource();
        Window.Document.QueueGlobalTask(() =>
        {
            if (success)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(new TypeErrorException("The cookie is not valid"));
            }
        });
        return completion.Task;
    }

    private CookieRequestContext GetContext(UrlRecord url) => CookieContexts.ForScript(Window.Document, url);

    /// <summary>https://cookiestore.spec.whatwg.org/#query-cookies</summary>
    private List<CookieListItem> QueryCookies(UrlRecord url, string? name)
    {
        var list = new List<CookieListItem>();
        if (Window.Document.GetCookieJar() is not { } cookieJar)
            return list;

        var normalizedName = name is null ? null : Normalize(name);
        foreach (var cookie in cookieJar.Retrieve(GetContext(url), updateAccessTime: true))
        {
            var item = CookieListItem.Create(cookie);
            if (normalizedName is not null && item.Name != normalizedName)
                continue;

            list.Add(item);
        }

        return list;
    }

    /// <summary>https://cookiestore.spec.whatwg.org/#normalize-a-cookie-name-or-value</summary>
    private static string Normalize(string value) => value.Trim('\t', ' ');

    /// <summary>https://cookiestore.spec.whatwg.org/#set-a-cookie</summary>
    private Task SetCookie(UrlRecord url, string name, string value, double? expires, string? domain, string path, CookieSameSite sameSite, bool partitioned, long? maxAge)
    {
        return Complete(TrySetCookie(url, name, value, expires, domain, path, sameSite, partitioned, maxAge));
    }

    private bool TrySetCookie(UrlRecord url, string name, string value, double? expires, string? domain, string path, CookieSameSite sameSite, bool partitioned, long? maxAge)
    {
        // Steps 1-5
        name = Normalize(name);
        value = Normalize(value);
        if (ContainsForbiddenCharacter(name) || ContainsForbiddenCharacter(value))
            return false;

        if (name.Contains('=', StringComparison.Ordinal))
            return false;

        if (name.Length == 0)
        {
            if (value.Contains('=', StringComparison.Ordinal) || value.Length == 0 || CookieJar.IsPrefixedValue(value))
                return false;
        }

        if (name.StartsWith("__Host-Http-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("__Http-", StringComparison.OrdinalIgnoreCase))
            return false;

        // Steps 6-8
        var encodedName = InfraString.Utf8EncodeToIsomorphic(name);
        var encodedValue = InfraString.Utf8EncodeToIsomorphic(value);
        if (encodedName.Length + encodedValue.Length > CookieJar.MaxNameValuePairSize)
            return false;

        // Steps 9-11
        if (url.Host is not { } host)
            return false;

        var attributes = new CookieAttributes();
        if (domain is not null)
        {
            if (domain.StartsWith('.', StringComparison.Ordinal))
                return false;

            if (name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Document.IsRegistrableDomainSuffixOfOrIsEqualTo(domain, host))
                return false;

            if (HostParser.Parse(domain) is not { } parsedDomain)
                return false;

            var encodedDomain = InfraString.Utf8EncodeToIsomorphic(parsedDomain.Serialized);
            if (encodedDomain.Length > CookieJar.MaxAttributeValueSize)
                return false;

            attributes.Domain = encodedDomain;
        }

        if (Window.Document.GetCookieJar() is not { } cookieJar)
            return true;

        // Steps 12-13: the expiry time is serialized as a cookie-date (with a resolution of one second)
        if (expires is { } expiresValue)
        {
            if (maxAge is not null)
                return false;

            if (double.IsFinite(expiresValue) && GetCookieDate(expiresValue) is { } expiryTime)
            {
                attributes.Expires = cookieJar.LimitExpiryTime(expiryTime);
            }
        }
        else if (maxAge is { } maxAgeValue)
        {
            attributes.MaxAgeExpiry = cookieJar.GetMaxAgeExpiryTime(maxAgeValue);
        }

        // Steps 14-18
        if (path.Length == 0)
        {
            path = CookieJar.GetDefaultPath(url);
        }

        if (!path.StartsWith('/', StringComparison.Ordinal))
            return false;

        if (path != "/" && name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase))
            return false;

        var encodedPath = InfraString.Utf8EncodeToIsomorphic(path);
        if (encodedPath.Length > CookieJar.MaxAttributeValueSize)
            return false;

        attributes.Path = encodedPath;
        attributes.ExplicitPath = true;

        // Steps 19-21
        attributes.Secure = true;
        attributes.SameSite = sameSite;
        attributes.Partitioned = partitioned;

        // Step 22: the storage failures are not reported
        cookieJar.ReceiveCookie(GetContext(url), encodedName, encodedValue, attributes);
        return true;
    }

    /// <summary>The date of a DOMHighResTimeStamp (https://cookiestore.spec.whatwg.org/#date-serialize), rounded to the second as a cookie-date, or null when it cannot be represented as a cookie-date.</summary>
    private static DateTimeOffset? GetCookieDate(double milliseconds)
    {
        var seconds = Math.Round(milliseconds / 1000, MidpointRounding.AwayFromZero);
        if (seconds < -11644473600 || seconds > 253402300799)
            return null;

        return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }

    /// <summary>A name or value cannot contain U+003B (;), a C0 control character except U+0009 TAB, or U+007F DELETE.</summary>
    private static bool ContainsForbiddenCharacter(string value)
    {
        foreach (var c in value)
        {
            if (c is ';' or '\x7F' || (c <= '\x1F' && c != '\t'))
                return true;
        }

        return false;
    }

    /// <summary>https://cookiestore.spec.whatwg.org/#delete-a-cookie</summary>
    private Task DeleteCookie(UrlRecord url, string name, string? domain, string path, bool partitioned)
    {
        name = Normalize(name);

        // A cookie without name has a value
        var value = name.Length == 0 ? "deleted" : "";
        return SetCookie(url, name, value, expires: null, domain, path, CookieSameSite.Strict, partitioned, maxAge: 0);
    }

    /// <summary>
    /// The steps to process cookie changes (https://cookiestore.spec.whatwg.org/#process-changes) for the window: the
    /// observable changes for its creation URL are fired as a change event from a task.
    /// </summary>
    void ICookieChangeListener.OnCookiesChanged(CookieJar cookieJar, IReadOnlyList<CookieChange> changes)
    {
        var document = Window.Document;
        document.QueueGlobalTask(() =>
        {
            if (!ReferenceEquals(Window.Document.GetCookieJar(), cookieJar) || GetCreationUrl() is not { } url || url.Host is not { } host)
                return;

            // https://cookiestore.spec.whatwg.org/#observable-changes
            var context = GetContext(url);
            var partitionKey = CookieJar.GetPartitionKey(context);
            var changed = new List<CookieListItem>();
            var deleted = new List<CookieListItem>();
            foreach (var change in changes)
            {
                if (!CookieJar.IsInScope(change.Cookie, context, host, partitionKey))
                    continue;

                // https://cookiestore.spec.whatwg.org/#prepare-lists: the value of a deleted cookie is undefined
                var item = CookieListItem.Create(change.Cookie);
                if (change.Deleted)
                {
                    deleted.Add(new CookieListItem { Name = item.Name });
                }
                else
                {
                    changed.Add(item);
                }
            }

            if (changed.Count == 0 && deleted.Count == 0)
                return;

            // https://cookiestore.spec.whatwg.org/#fire-a-change-event
            FireEvent(new CookieChangeEvent("change", new CookieChangeEventInit { Changed = changed, Deleted = deleted }));
        });
    }
}

/// <summary>https://cookiestore.spec.whatwg.org/#dictdef-cookiechangeeventinit</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#dictdef-cookiechangeeventinit"/>
[IdlInterface("CookieChangeEventInit", "https://cookiestore.spec.whatwg.org/#dictdef-cookiechangeeventinit")]
public sealed class CookieChangeEventInit : EventInit
{
    /// <summary>Gets the cookies that were created or modified.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiechangeeventinit-changed"/>
    [IdlMember("changed", "https://cookiestore.spec.whatwg.org/#dom-cookiechangeeventinit-changed")]
    public IReadOnlyList<CookieListItem>? Changed { get; init; }

    /// <summary>Gets the cookies that were deleted or expired.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiechangeeventinit-deleted"/>
    [IdlMember("deleted", "https://cookiestore.spec.whatwg.org/#dom-cookiechangeeventinit-deleted")]
    public IReadOnlyList<CookieListItem>? Deleted { get; init; }
}

/// <summary>The change events of the Cookie Store API (https://cookiestore.spec.whatwg.org/#cookiechangeevent), fired at <c>window.cookieStore</c>.</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#cookiechangeevent"/>
[IdlInterface("CookieChangeEvent", "https://cookiestore.spec.whatwg.org/#cookiechangeevent")]
public sealed class CookieChangeEvent : Event
{
    /// <summary>Creates a cookie change event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-cookiechangeevent"/>
    [IdlMember("constructor", "https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-cookiechangeevent")]
    public CookieChangeEvent(string type, CookieChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Changed = (eventInitDict?.Changed ?? []).ToArray();
        Deleted = (eventInitDict?.Deleted ?? []).ToArray();
    }

    /// <summary>Gets the created or changed cookies (https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-changed).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-changed"/>
    [IdlMember("changed", "https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-changed")]
    public IReadOnlyList<CookieListItem> Changed { get; }

    /// <summary>Gets the deleted cookies, without value (https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-deleted).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-deleted"/>
    [IdlMember("deleted", "https://cookiestore.spec.whatwg.org/#dom-cookiechangeevent-deleted")]
    public IReadOnlyList<CookieListItem> Deleted { get; }
}

/// <summary>A cookie store notified of the changes of a <see cref="CookieJar"/> (the jar keeps a weak reference to it).</summary>
internal interface ICookieChangeListener
{
    void OnCookiesChanged(CookieJar cookieJar, IReadOnlyList<CookieChange> changes);
}
