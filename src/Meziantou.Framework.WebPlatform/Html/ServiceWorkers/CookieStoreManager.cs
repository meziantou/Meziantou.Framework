using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The cookie change subscriptions of a service worker registration (https://cookiestore.spec.whatwg.org/#cookiestoremanager),
/// <c>registration.cookies</c>: the active worker of the registration receives a <c>cookiechange</c> event
/// (<see cref="ExtendableCookieChangeEvent"/>) when a cookie that matches a subscription changes.
/// </summary>
/// <remarks>
/// The subscriptions belong to the registration: they are kept when the service worker is updated, saved with the
/// registration by the <see cref="ServiceWorkerBackend"/>, and dropped when the registration is unregistered.
/// </remarks>
/// <seealso href="https://cookiestore.spec.whatwg.org/#cookiestoremanager"/>
[IdlInterface("CookieStoreManager", "https://cookiestore.spec.whatwg.org/#cookiestoremanager")]
public sealed class CookieStoreManager
{
    private readonly ServiceWorkerRegistration _registration;

    internal CookieStoreManager(ServiceWorkerRegistration registration)
    {
        _registration = registration;
    }

    /// <summary>
    /// Adds cookie change subscriptions to the registration (https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-subscribe):
    /// the cookies whose name is the name of a subscription (any name when it has none) and that would be sent to its URL
    /// (the scope of the registration when it has none). Adding a subscription that exists does nothing.
    /// </summary>
    /// <returns>A task failed with a <see cref="TypeErrorException"/> when a URL is not valid or is not in the scope of the registration.</returns>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-subscribe"/>
    [IdlMember("subscribe", "https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-subscribe")]
    public Task Subscribe(IReadOnlyList<CookieStoreGetOptions> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        return Update(subscriptions, static (list, subscription) =>
        {
            if (!list.Contains(subscription))
            {
                list.Add(subscription);
            }
        });
    }

    /// <summary>Returns the cookie change subscriptions of the registration (https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-getsubscriptions), in the order they were added.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-getsubscriptions"/>
    [IdlMember("getSubscriptions", "https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-getsubscriptions")]
    public Task<IReadOnlyList<CookieStoreGetOptions>> GetSubscriptions()
    {
        var record = _registration.Record;
        List<CookieStoreGetOptions> result;
        lock (record.Registry.Lock)
        {
            result = [.. record.CookieChangeSubscriptions.Select(static subscription => new CookieStoreGetOptions { Name = subscription.Name, Url = subscription.Url })];
        }

        var promise = new TaskCompletionSource<IReadOnlyList<CookieStoreGetOptions>>();
        _registration.Global.QueueGlobalTask(() => promise.TrySetResult(result));
        return promise.Task;
    }

    /// <summary>
    /// Removes cookie change subscriptions from the registration (https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-unsubscribe).
    /// Removing a subscription that does not exist does nothing.
    /// </summary>
    /// <returns>A task failed with a <see cref="TypeErrorException"/> when a URL is not valid or is not in the scope of the registration.</returns>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-unsubscribe"/>
    [IdlMember("unsubscribe", "https://cookiestore.spec.whatwg.org/#dom-cookiestoremanager-unsubscribe")]
    public Task Unsubscribe(IReadOnlyList<CookieStoreGetOptions> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        return Update(subscriptions, static (list, subscription) => list.Remove(subscription));
    }

    private Task Update(IReadOnlyList<CookieStoreGetOptions> subscriptions, Action<List<CookieChangeSubscription>, CookieChangeSubscription> update)
    {
        var global = _registration.Global;
        var record = _registration.Record;
        var baseUrl = global.ApiBaseUrl;
        var parsed = new List<CookieChangeSubscription>(subscriptions.Count);
        string? error = null;
        foreach (var entry in subscriptions)
        {
            // The name is normalized as the names of the Cookie Store API, and the URL defaults to the scope of the registration
            var name = entry.Name?.Trim('\t', ' ');
            var url = entry.Url is null ? record.ScopeUrl : UrlParser.Parse(entry.Url, baseUrl);
            if (url is null)
            {
                error = "The URL of the subscription is not valid";
                break;
            }

            var serializedUrl = url.Serialize();
            if (!serializedUrl.StartsWith(record.Scope, StringComparison.Ordinal))
            {
                error = "The URL of the subscription is not in the scope of the registration";
                break;
            }

            parsed.Add(new CookieChangeSubscription(name, serializedUrl));
        }

        // The subscriptions before the invalid one are processed, as the steps that run in parallel
        bool changed;
        lock (record.Registry.Lock)
        {
            var list = record.CookieChangeSubscriptions;
            var previous = list.ToArray();
            foreach (var subscription in parsed)
            {
                update(list, subscription);
            }

            changed = !previous.SequenceEqual(list);
        }

        if (changed)
        {
            record.Registry.OnCookieChangeSubscriptionsChanged(record);
        }

        var promise = new TaskCompletionSource();
        global.QueueGlobalTask(() =>
        {
            if (error is null)
            {
                promise.TrySetResult();
            }
            else
            {
                promise.TrySetException(new TypeErrorException(error));
            }
        });
        return promise.Task;
    }
}

/// <summary>A cookie change subscription (https://cookiestore.spec.whatwg.org/#cookie-change-subscription): a cookie name (null for any name) and a serialized URL.</summary>
internal readonly record struct CookieChangeSubscription(string? Name, string Url);

/// <summary>
/// Fires the <c>cookiechange</c> events of a service worker registration (https://cookiestore.spec.whatwg.org/#process-changes):
/// the cookie jar of the registration notifies it of its changes while the registration has subscriptions.
/// </summary>
internal sealed class ServiceWorkerCookieChangeListener : ICookieChangeListener
{
    private readonly ServiceWorkerRegistrationRecord _registration;

    public ServiceWorkerCookieChangeListener(ServiceWorkerRegistrationRecord registration, CookieJar cookieJar)
    {
        _registration = registration;
        CookieJar = cookieJar;
    }

    public CookieJar CookieJar { get; }

    public void OnCookiesChanged(CookieJar cookieJar, IReadOnlyList<CookieChange> changes)
    {
        var registry = _registration.Registry;
        CookieChangeSubscription[] subscriptions;
        lock (registry.Lock)
        {
            if (_registration.IsUnregistered || _registration.Active is null || !ReferenceEquals(_registration.CookieChangeListener, this))
                return;

            subscriptions = [.. _registration.CookieChangeSubscriptions];
        }

        // https://cookiestore.spec.whatwg.org/#observable-changes: a change is observed when it matches a subscription
        var storageKey = _registration.StorageKey;
        var changed = new List<CookieListItem>();
        var deleted = new List<CookieListItem>();
        foreach (var change in changes)
        {
            var item = CookieListItem.Create(change.Cookie);
            foreach (var subscription in subscriptions)
            {
                if (!IsObserved(cookieJar, storageKey, change.Cookie, subscription))
                    continue;

                // https://cookiestore.spec.whatwg.org/#prepare-lists: the value of a deleted cookie is undefined
                if (change.Deleted)
                {
                    deleted.Add(new CookieListItem { Name = item.Name });
                }
                else
                {
                    changed.Add(item);
                }

                break;
            }
        }

        if (changed.Count == 0 && deleted.Count == 0)
            return;

        registry.FireFunctionalEvent(_registration, "cookiechange", _ => new ExtendableCookieChangeEvent("cookiechange", new ExtendableCookieChangeEventInit { Changed = changed, Deleted = deleted }));
    }

    /// <summary>
    /// Whether a subscription of a registration observes a change of a cookie (https://cookiestore.spec.whatwg.org/#observable-changes):
    /// a cookie of its name, if any, that the service workers of the registration can read at its URL.
    /// </summary>
    /// <remarks>
    /// The unpartitioned cookies of a third-party registration are not observed when the jar blocks them, as they are not
    /// read: the same check as the cookie store of a window.
    /// </remarks>
    internal static bool IsObserved(CookieJar cookieJar, StorageKey storageKey, CookieEntry cookie, CookieChangeSubscription subscription)
    {
        if (subscription.Name is { } name && name != cookie.Name)
            return false;

        if (UrlParser.Parse(subscription.Url, baseUrl: null) is not { Host: { } host } url)
            return false;

        var context = GetContext(storageKey, url);
        if (cookie.PartitionKey is null && cookieJar.IsUnpartitionedCookieAccessBlocked(context))
            return false;

        return CookieJar.IsInScope(cookie, context, host, CookieJar.GetPartitionKey(context), cookieJar.OriginBound);
    }

    /// <summary>
    /// The context of the Cookie Store API of a service worker of the registration (see <see cref="CookieContexts.ForScript"/>):
    /// same-site when its storage key is first-party.
    /// </summary>
    private static CookieRequestContext GetContext(StorageKey storageKey, UrlRecord url)
    {
        var origin = OriginRecord.FromUrl(url);
        if (storageKey.IsFirstParty)
            return new CookieRequestContext(url, IsHttp: false, CookieSameSiteStatus.Strict, origin);

        var topLevelOrigin = UrlParser.Parse(storageKey.TopLevelSite!, baseUrl: null) is { } topLevelUrl ? OriginRecord.FromUrl(topLevelUrl) : OriginRecord.CreateOpaque();
        return new CookieRequestContext(url, IsHttp: false, CookieSameSiteStatus.CrossSite, topLevelOrigin, CrossSiteAncestor: true);
    }
}

/// <summary>https://cookiestore.spec.whatwg.org/#dictdef-extendablecookiechangeeventinit</summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#dictdef-extendablecookiechangeeventinit"/>
[IdlInterface("ExtendableCookieChangeEventInit", "https://cookiestore.spec.whatwg.org/#dictdef-extendablecookiechangeeventinit")]
public sealed class ExtendableCookieChangeEventInit : ExtendableEventInit
{
    /// <summary>Gets the cookies that were created or modified.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeeventinit-changed"/>
    [IdlMember("changed", "https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeeventinit-changed")]
    public IReadOnlyList<CookieListItem>? Changed { get; init; }

    /// <summary>Gets the cookies that were deleted or expired.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeeventinit-deleted"/>
    [IdlMember("deleted", "https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeeventinit-deleted")]
    public IReadOnlyList<CookieListItem>? Deleted { get; init; }
}

/// <summary>
/// The <c>cookiechange</c> event of a service worker (https://cookiestore.spec.whatwg.org/#extendablecookiechangeevent):
/// the changes of the cookies that match the subscriptions of its registration (<see cref="CookieStoreManager"/>).
/// </summary>
/// <seealso href="https://cookiestore.spec.whatwg.org/#extendablecookiechangeevent"/>
[IdlInterface("ExtendableCookieChangeEvent", "https://cookiestore.spec.whatwg.org/#extendablecookiechangeevent")]
public sealed class ExtendableCookieChangeEvent : ExtendableEvent
{
    /// <summary>Creates a cookie change event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeevent-extendablecookiechangeevent"/>
    [IdlMember("constructor", "https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeevent-extendablecookiechangeevent")]
    public ExtendableCookieChangeEvent(string type, ExtendableCookieChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Changed = (eventInitDict?.Changed ?? []).ToArray();
        Deleted = (eventInitDict?.Deleted ?? []).ToArray();
    }

    /// <summary>Gets the created or changed cookies.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeevent-changed"/>
    [IdlMember("changed", "https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeevent-changed")]
    public IReadOnlyList<CookieListItem> Changed { get; }

    /// <summary>Gets the deleted cookies, without value.</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeevent-deleted"/>
    [IdlMember("deleted", "https://cookiestore.spec.whatwg.org/#dom-extendablecookiechangeevent-deleted")]
    public IReadOnlyList<CookieListItem> Deleted { get; }
}
