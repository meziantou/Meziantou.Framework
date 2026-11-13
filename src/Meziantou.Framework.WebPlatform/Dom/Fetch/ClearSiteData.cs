using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The <c>Clear-Site-Data</c> response header (https://w3c.github.io/webappsec-clear-site-data/): a response of a
/// potentially trustworthy URL clears the data that the user agent stores for its origin. The client hints and the
/// speculative loads are cleared by <see cref="ClientHints.ProcessClearSiteData"/> and
/// <see cref="NavigationalPrefetch.ProcessClearSiteData"/> (called here for the responses that are not the ones of a
/// navigation); this class clears the other data:
/// <list type="bullet">
/// <item><c>"cache"</c>: the stored responses of the HTTP cache of the URLs of the origin, its compression dictionaries, and the documents of the origin kept in the back/forward cache, in the partition of the request.</item>
/// <item><c>"cookies"</c>: the cookies of the registrable domain of the origin, and the compression dictionaries of the origin in the partition of the request.</item>
/// <item><c>"storage"</c>: the local storage and the session storage, the IndexedDB databases (without open connection), the caches of the Cache API and the service worker registrations of the origin.</item>
/// <item><c>"executionContexts"</c>: the documents of the origin in the tree of the browsing context of the request are reloaded.</item>
/// </list>
/// As in the browsers, a response to a request whose top-level site is another site (a third-party context) only clears
/// the data of the partition of that top-level site: the partitioned cookies, the caches of the partition, the storage
/// of the origin partitioned by that top-level site and the documents of the tree of the request.
/// </summary>
internal static class ClearSiteData
{
    [Flags]
    internal enum DataTypes
    {
        None = 0,
        Cache = 1,
        Cookies = 2,
        Storage = 4,
        ExecutionContexts = 8,
        ClientHints = 16,
    }

    /// <summary>https://w3c.github.io/webappsec-clear-site-data/#parsing: the types of the header (the unknown types are ignored).</summary>
    internal static DataTypes Parse(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (HeaderList.Get(headers, "Clear-Site-Data") is not { } value)
            return DataTypes.None;

        var types = DataTypes.None;
        foreach (var member in value.Split(','))
        {
            types |= member.Trim(' ', '\t') switch
            {
                "\"cache\"" => DataTypes.Cache | DataTypes.ClientHints,
                "\"cookies\"" => DataTypes.Cookies | DataTypes.ClientHints,
                "\"storage\"" => DataTypes.Storage,
                "\"executionContexts\"" => DataTypes.ExecutionContexts,
                "\"clientHints\"" => DataTypes.ClientHints,
                "\"*\"" => DataTypes.Cache | DataTypes.Cookies | DataTypes.Storage | DataTypes.ExecutionContexts | DataTypes.ClientHints,
                _ => DataTypes.None,
            };
        }

        return types;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-clear-site-data/#fetch-integration: the response of the network to a request whose
    /// credentials are included clears the data of its origin, after its cookies are stored (a cookie set by the response is
    /// cleared too), and before the response is used or stored in the HTTP cache. Runs on the thread of the event loop of the
    /// request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="response">The response of the network.</param>
    /// <param name="includeCredentials">Whether the request includes the credentials.</param>
    /// <param name="cachePartition">The partition of the request in the HTTP cache (see <see cref="HttpCache.GetPartition"/>), or null when it has none.</param>
    /// <returns>Whether the HTTP cache was cleared for the origin of the response.</returns>
    public static bool ProcessResponse(FetchRequest request, FetchResponse response, bool includeCredentials, string? cachePartition)
    {
        if (!includeCredentials || response.IsNetworkError || request.UseIsolatedCredentials || request.IsSpeculative)
            return false;

        var url = request.CurrentUrl;
        if (url.Scheme is not ("http" or "https") || !ReferrerPolicies.IsPotentiallyTrustworthy(url))
            return false;

        var browsingContext = request.Navigable ?? request.Client?.EnvironmentBrowsingContext ?? request.KeepaliveBrowsingContext;

        // The speculative loads (the "prefetchCache" and "prerenderCache" types): a navigation processes them with its
        // response (see BrowsingContext.ProcessNavigationResponse), the other requests here. The prefetch records belong to
        // the documents, so a worker processes them on the event loop of its browsing context.
        if (request.Mode != RequestMode.Navigate && browsingContext is not null)
        {
            if (request.Client is { WorkerGlobal: not null })
            {
                var headers = response.HeaderList.ToArray();
                browsingContext.EventLoop.QueueTask(() =>
                {
                    if (!browsingContext.IsDestroyed)
                    {
                        NavigationalPrefetch.ProcessClearSiteData(browsingContext, url, headers);
                    }
                });
            }
            else
            {
                NavigationalPrefetch.ProcessClearSiteData(browsingContext, url, response.HeaderList);
            }
        }

        var types = Parse(response.HeaderList);
        if ((types & (DataTypes.Cache | DataTypes.Cookies | DataTypes.Storage | DataTypes.ExecutionContexts)) == DataTypes.None)
            return false;

        var options = request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? browsingContext?.Options;
        if (options is null)
            return false;

        var origin = OriginRecord.FromUrl(url);
        var serializedOrigin = origin.Serialize();

        // The partition of the request: the site of its top-level origin. A response to a request of another top-level site
        // only clears the partitioned data.
        var topLevelOrigin = GetTopLevelOrigin(request);
        var firstParty = topLevelOrigin is null || (!topLevelOrigin.IsOpaque && OriginRecord.IsSameSite(topLevelOrigin, origin));
        var partitionSite = topLevelOrigin is null ? GetSite(origin) : topLevelOrigin.IsOpaque ? null : GetSite(topLevelOrigin);

        if (types.HasFlag(DataTypes.Cache))
        {
            ClearCache(request, options, browsingContext, origin, serializedOrigin, cachePartition, partitionSite);
        }

        // https://www.rfc-editor.org/rfc/rfc9842#section-9.1: the compression dictionaries are cleared with the cache and with the
        // cookies (they could be used to track the user), in the partition of the request
        if (types.HasFlag(DataTypes.Cache) || types.HasFlag(DataTypes.Cookies))
        {
            if (options.DocumentLoader is { } loader && cachePartition is not null)
            {
                CompressionDictionaries.Get(loader).ClearOrigin(serializedOrigin, cachePartition);
            }
        }

        if (types.HasFlag(DataTypes.Cookies) && (request.CookieContext is null ? request.Client?.CookieJar : request.CookieContext.CookieJar) is { } cookieJar)
        {
            var domain = PublicSuffixList.GetRegistrableDomain(origin.Host) ?? origin.Host.Serialized;
            cookieJar.RemoveForDomain(domain, includeUnpartitioned: firstParty, partitionSite);
        }

        if (browsingContext is null)
            return types.HasFlag(DataTypes.Cache);

        // The storage of the storage key of the origin in the partition of the request: the first-party storage, or the
        // storage of the origin partitioned by the top-level site (https://privacycg.github.io/storage-partitioning/), with
        // the cross-site ancestor bit of an origin embedded by its own site through a cross-site frame (A → B → A)
        if (types.HasFlag(DataTypes.Storage))
        {
            ClearStorage(browsingContext, StorageKey.ForRequest(origin, request));
        }

        // The reloaded documents are the ones of the tree of the request, which is its partition
        if (types.HasFlag(DataTypes.ExecutionContexts))
        {
            ReloadBrowsingContexts(browsingContext, origin);
        }

        return types.HasFlag(DataTypes.Cache);
    }

    /// <summary>The top-level origin of a request (the one of its cookies), or null when it has none (e.g. a top-level navigation is its own top-level origin).</summary>
    private static OriginRecord? GetTopLevelOrigin(FetchRequest request)
    {
        string? topLevelOrigin;
        if (request.Mode == RequestMode.Navigate)
        {
            if (request.Navigable is null or { Parent: null })
                return null;

            topLevelOrigin = request.CookieContext?.TopLevelOrigin;
        }
        else
        {
            topLevelOrigin = request.CookieContext is { } cookieContext ? cookieContext.TopLevelOrigin : request.Client?.TopLevelOriginForCookies;
        }

        if (topLevelOrigin is null)
            return null;

        return topLevelOrigin != "null" && UrlParser.Parse(topLevelOrigin, baseUrl: null) is { } topLevelUrl ? OriginRecord.FromUrl(topLevelUrl) : OriginRecord.CreateOpaque();
    }

    /// <summary>The site of an origin, as the keys of the partitions: <c>scheme://registrable-domain</c>.</summary>
    private static string GetSite(OriginRecord origin) => origin.Scheme + "://" + (PublicSuffixList.GetRegistrableDomain(origin.Host) ?? origin.Host.Serialized);

    /// <summary>
    /// https://w3c.github.io/webappsec-clear-site-data/#clear-cache: the stored responses of the partition of the request,
    /// and the documents of the back/forward caches whose top-level document has the top-level site of the request and whose
    /// document tree has a document of the origin (as in Chromium, the caches of the other partitions are kept).
    /// </summary>
    private static void ClearCache(FetchRequest request, BrowsingContextOptions options, BrowsingContext? browsingContext, OriginRecord origin, string serializedOrigin, string? cachePartition, string? partitionSite)
    {
        if (options.DocumentLoader is { } loader && cachePartition is not null)
        {
            HttpCache.Get(loader).ClearOrigin(serializedOrigin, cachePartition);
        }

        if (partitionSite is null || browsingContext is null)
            return;

        foreach (var topLevel in browsingContext.GetGroupTopLevelBrowsingContexts().ToArray())
        {
            if (!topLevel.IsDestroyed && !ReferenceEquals(topLevel, request.Navigable))
            {
                topLevel.EvictFromBackForwardCache(origin, partitionSite);
            }
        }
    }

    /// <summary>https://w3c.github.io/webappsec-clear-site-data/#clear-dom-accessible-storage</summary>
    private static void ClearStorage(BrowsingContext browsingContext, StorageKey storageKey)
    {
        var origin = storageKey.Serialize();
        browsingContext.LocalStorageShed.ObtainArea(storageKey).Clear();
        foreach (var topLevel in browsingContext.GetGroupTopLevelBrowsingContexts().ToArray())
        {
            topLevel.SessionStorageShed.ObtainArea(storageKey).Clear(persist: false);
        }

        // The databases without open connection are deleted (an open connection keeps its database)
        var indexedDB = browsingContext.IndexedDBBackend;
        foreach (var database in indexedDB.GetDatabases(origin))
        {
            var state = indexedDB.Coordinator.GetDatabase(origin, database.Name);
            if (state.GetOpenConnections(except: null).Count > 0)
                continue;

            state.CallBackend(backend =>
            {
                backend.DeleteDatabase(origin, database.Name);
                return true;
            });
        }

        var cacheStorage = browsingContext.CacheStorageBackend.Coordinator;
        foreach (var name in cacheStorage.Keys(origin).ToArray())
        {
            cacheStorage.Delete(origin, name);
        }

        // The service worker registrations are unregistered: the controlled clients keep their service worker until they are unloaded
        var registry = ServiceWorkerRegistry.Get(browsingContext.GroupOwner);
        var serviceWorkerStorageKey = storageKey;
        foreach (var registration in registry.GetRegistrations(serviceWorkerStorageKey))
        {
            registry.ScheduleJob(new ServiceWorkerJob
            {
                Type = ServiceWorkerJobType.Unregister,
                StorageKey = serviceWorkerStorageKey,
                ScopeUrl = registration.ScopeUrl,
            });
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-clear-site-data/#reload: the navigables of the tree of the browsing context of the
    /// request whose active document has the origin are reloaded, except the browsing context of the request (the navigable
    /// of a navigation, or the one of the client) and its ancestors: reloading them would load the response again (e.g. a
    /// page that fetches a <c>Clear-Site-Data: "*"</c> response when it loads would reload forever).
    /// </summary>
    private static void ReloadBrowsingContexts(BrowsingContext browsingContext, OriginRecord origin)
    {
        var excluded = new HashSet<BrowsingContext>();
        for (var ancestor = browsingContext; ancestor is not null; ancestor = ancestor.Parent)
        {
            excluded.Add(ancestor);
        }

        foreach (var navigable in browsingContext.Top.GetInclusiveDescendantNavigables().ToArray())
        {
            if (excluded.Contains(navigable) || navigable.IsDestroyed || !OriginRecord.IsSameOrigin(navigable.Document.Origin, origin))
                continue;

            navigable.EventLoop.QueueTask(() =>
            {
                if (!navigable.IsDestroyed)
                {
                    navigable.ReloadNavigable();
                }
            });
        }
    }
}
