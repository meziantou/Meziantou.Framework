using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Cache API (https://w3c.github.io/ServiceWorker/#cache-objects), implemented in JavaScript (CacheStorage.js) because
// it stores the Request and Response objects of Fetch.js. The caches are stored by the CacheStorageBackend of the browsing
// context (BrowsingContextOptions.CacheStorage); the matching of the requests and the batch operations are implemented in
// .NET (Dom/CacheStorage/CacheStorageCoordinator.cs) and given to the script as hooks.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private static string? s_cacheStorageScript;
    private TValue? _cacheStorage;

    /// <summary>
    /// Defines the caches attribute of the global object (https://w3c.github.io/ServiceWorker/#self-caches: a [SecureContext]
    /// member of the WindowOrWorkerGlobalScope mixin) and the interface objects Cache and CacheStorage.
    /// </summary>
    private void DefineCacheStorageGlobals()
    {
        foreach (var name in (ReadOnlySpan<string>)["Cache", "CacheStorage"])
        {
            var interfaceName = name;
            Adapter.DefineLazyDataProperty(Global, Key(interfaceName), () => Adapter.Get(GetCacheStorage(), Key(interfaceName)), writable: true, enumerable: false, configurable: true);
        }

        // [SecureContext, SameObject] readonly attribute CacheStorage caches: the members of Window ([Global]) are on the
        // global object, the ones of WorkerGlobalScope on its prototype
        DefineAttribute(Window is null ? GetPrototype("WorkerGlobalScope") : Global, "caches", static (realm, thisValue) =>
        {
            if (realm.Unwrap(realm.ThisOrGlobal(thisValue)) is not GlobalScope global)
                throw realm.CreateTypeError("Illegal invocation: 'this' does not implement interface WindowOrWorkerGlobalScope");

            var targetRealm = ReferenceEquals(global, realm.GlobalScope) ? realm : realm.Agent.TryGetRealm(global) ?? realm;
            return targetRealm.Adapter.Get(targetRealm.GetCacheStorage(), targetRealm.Key("caches"));
        }, setter: null);
    }

    /// <summary>Returns the object returned by CacheStorage.js in this realm: the interface objects and the CacheStorage object.</summary>
    private TValue GetCacheStorage()
    {
        if (_cacheStorage is { } cacheStorage)
            return cacheStorage;

        return Adapter.RunInRealm(EngineRealm, () =>
        {
            if (_cacheStorage is { } existing)
                return existing;

            var script = s_cacheStorageScript ??= LoadEmbeddedScript("Meziantou.Framework.WebPlatform.Scripting.WebIdl.CacheStorage.js");
            var adapter = Adapter;
            var fetch = GetFetch();
            var interfaces = CreateObject();
            Adapter.Set(interfaces, Key("AbortController"), GetInterfaceObject("AbortController"));
            var abortSignal = GetInterfaceObject("AbortSignal");
            var abortSignalAny = Adapter.Get(abortSignal, Key("any"));

            var hooks = CreateObject();
            Adapter.Set(hooks, Key("interfaces"), interfaces);
            Adapter.Set(hooks, Key("fetchSupport"), Adapter.Get(fetch, Key("cacheSupport")));
            Adapter.Set(hooks, Key("fetchSupportRequest"), Adapter.Get(fetch, Key("Request")));

            // The .NET exceptions (e.g. the InvalidStateError of a batch) are thrown as JavaScript exceptions
            void Define(string name, int length, JsHostFunction<TValue> function) => Adapter.Set(hooks, Key(name), adapter.CreateFunction(name, length, (thisValue, arguments) =>
            {
                try
                {
                    return function(thisValue, arguments);
                }
                catch (Exception ex) when (!adapter.TryGetThrownValue(ex, out TValue? _))
                {
                    throw ToJsException(ex is DomException or TypeErrorException ? ex : new DomException("The cache storage failed: " + ex.Message, "UnknownError"));
                }
            }));

            Define("queueTask", 1, (_, args) =>
            {
                var callback = args[0];
                GlobalScope.QueueGlobalTask(() => adapter.Call(callback, adapter.Undefined, []));
                return adapter.Undefined;
            });
            Define("anySignal", 1, (_, args) => adapter.Call(abortSignalAny, abortSignal, [args[0]]));
            Define("headerList", 1, (_, args) => FromHeaderList(((Headers)Unwrap(args[0])!).List));

            // CacheStorage (https://w3c.github.io/ServiceWorker/#cachestorage-interface)
            Define("storageKeys", 0, (_, _) =>
            {
                var (coordinator, origin) = ObtainCacheStorage();
                return CreateArray([.. coordinator.Keys(origin).Select(Adapter.FromString)]);
            });
            Define("storageHas", 1, (_, args) =>
            {
                var (coordinator, origin) = ObtainCacheStorage();
                return Adapter.FromBoolean(coordinator.Has(origin, Adapter.ToString(args[0])));
            });
            Define("storageOpen", 1, (_, args) =>
            {
                var (coordinator, origin) = ObtainCacheStorage();
                return Adapter.FromNumber(coordinator.Open(origin, Adapter.ToString(args[0])));
            });
            Define("storageDelete", 1, (_, args) =>
            {
                var (coordinator, origin) = ObtainCacheStorage();
                return Adapter.FromBoolean(coordinator.Delete(origin, Adapter.ToString(args[0])));
            });

            // https://w3c.github.io/ServiceWorker/#cache-storage-match: the first matching response of the caches (or of the named cache)
            Define("storageMatch", 5, (_, args) =>
            {
                var (coordinator, origin) = ObtainCacheStorage();
                var query = ToCachedRequest(args[0]);
                var options = ToCacheQueryOptions(args[2], args[3], args[4]);
                IEnumerable<string> names = Adapter.IsUndefined(args[1]) ? coordinator.Keys(origin) : coordinator.Has(origin, Adapter.ToString(args[1])) ? [Adapter.ToString(args[1])] : [];
                foreach (var name in names)
                {
                    var handle = coordinator.GetHandle(coordinator.Open(origin, name));
                    if (CacheStorageCoordinator.QueryCache(handle, query, options) is [var entry, ..])
                    {
                        CheckCrossOriginResourcePolicy(entry);
                        return FromCachedResponse(entry.Response);
                    }
                }

                return Adapter.Null;
            });

            // Cache (https://w3c.github.io/ServiceWorker/#cache-interface): the handle is the id of a CacheHandle
            Define("queryResponses", 6, (_, args) =>
            {
                var entries = QueryEntries(args[0], args[1], ToCacheQueryOptions(args[2], args[3], args[4]));
                var single = Adapter.ToBoolean(args[5]);
                var matched = entries.Take(single ? 1 : int.MaxValue).ToList();
                foreach (var entry in matched)
                {
                    CheckCrossOriginResourcePolicy(entry);
                }

                return CreateArray([.. matched.Select(entry => FromCachedResponse(entry.Response))]);
            });
            Define("queryRequests", 5, (_, args) => CreateArray([.. QueryEntries(args[0], args[1], ToCacheQueryOptions(args[2], args[3], args[4])).Select(entry => FromCachedRequest(entry.Request))]));
            Define("batch", 2, (_, args) =>
            {
                var handle = GetCacheHandle(args[0]);
                var operations = new List<CacheOperation>();
                foreach (var operation in ToList(args[1]))
                {
                    var response = Adapter.Get(operation, Key("response"));
                    var options = Adapter.Get(operation, Key("options"));
                    operations.Add(new CacheOperation(
                        ToCachedRequest(Adapter.Get(operation, Key("request"))),
                        Adapter.IsNull(response) ? null : ToCachedResponse(response),
                        Adapter.IsNull(options) ? default : ToCacheQueryOptions(Adapter.Get(options, Key("ignoreSearch")), Adapter.Get(options, Key("ignoreMethod")), Adapter.Get(options, Key("ignoreVary")))));
                }

                var results = CacheStorageCoordinator.BatchCacheOperations(handle, operations);
                return CreateArray([.. results.Select(result => Adapter.FromNumber(result))]);
            });

            var result = Adapter.Call(EvaluateBuiltIn(script), Adapter.Undefined, [hooks]);
            _cacheStorage = result;
            return result;
        });
    }

    // https://w3c.github.io/ServiceWorker/#cache-storage: the caches of the storage key of the global object
    private (CacheStorageCoordinator Coordinator, string Origin) ObtainCacheStorage()
    {
        var (backend, origin) = StorageEndpoints.Resolve(GlobalScope, GlobalScope.Options.CacheStorage, static context => context.CacheStorageBackend, static () => new InMemoryCacheStorageBackend())
            ?? throw new DomException("The origin of the document is opaque", "SecurityError");
        return (backend.Coordinator, origin);
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#cache-matchall (step 4.4.1): an opaque response blocked by the cross-origin resource
    /// policy check of the global object rejects the promise with a TypeError.
    /// </summary>
    private void CheckCrossOriginResourcePolicy(CacheEntry entry)
    {
        // The URL list of an opaque filtered response is empty: the URL is the one of the request
        var response = entry.Response;
        if (response.Type != "opaque" || Urls.UrlParser.Parse(response.UrlList.Count > 0 ? response.UrlList[^1] : entry.Request.Url, baseUrl: null) is not { } url)
            return;

        var document = GlobalScope.EnvironmentDocument;
        List<KeyValuePair<string, string>> headers = response.CrossOriginResourcePolicy is { } policy ? [new("Cross-Origin-Resource-Policy", policy)] : [];
        if (Dom.CrossOriginResourcePolicy.IsBlocked(document, document.SubresourceEmbedderPolicy, "", url, url, headers, forNavigation: false, response.RequestIncludesCredentials))
            throw new TypeErrorException("The response is blocked by the cross-origin resource policy");
    }

    private CacheHandle GetCacheHandle(TValue handle) => ObtainCacheStorage().Coordinator.GetHandle((long)Adapter.ToNumber(handle));

    // https://w3c.github.io/ServiceWorker/#query-cache, or all the entries without request
    private IReadOnlyList<CacheEntry> QueryEntries(TValue handleValue, TValue request, CacheQueryOptions options)
    {
        var handle = GetCacheHandle(handleValue);
        return Adapter.IsNull(request) ? handle.GetEntries(urlWithoutQuery: null) : CacheStorageCoordinator.QueryCache(handle, ToCachedRequest(request), options);
    }

    private CacheQueryOptions ToCacheQueryOptions(TValue ignoreSearch, TValue ignoreMethod, TValue ignoreVary)
        => new(Adapter.ToBoolean(ignoreSearch), Adapter.ToBoolean(ignoreMethod), Adapter.ToBoolean(ignoreVary));

    private string GetString(TValue obj, string name) => Adapter.ToString(Adapter.Get(obj, Key(name)));

    private CachedRequest ToCachedRequest(TValue serialized) => new()
    {
        Method = GetString(serialized, "method"),
        Url = GetString(serialized, "url"),
        Headers = ToHeaderList(Adapter.Get(serialized, Key("headers"))),
        Mode = GetString(serialized, "mode"),
        Credentials = GetString(serialized, "credentials"),
        Cache = GetString(serialized, "cache"),
        Redirect = GetString(serialized, "redirect"),
        Integrity = GetString(serialized, "integrity"),
        Referrer = GetString(serialized, "referrer"),
        ReferrerPolicy = GetString(serialized, "referrerPolicy"),
    };

    private TValue FromCachedRequest(CachedRequest request)
    {
        var result = CreateObject();
        SetMember(result, "method", Adapter.FromString(request.Method));
        SetMember(result, "url", Adapter.FromString(request.Url));
        SetMember(result, "headers", FromHeaderList([.. request.Headers]));
        SetMember(result, "mode", Adapter.FromString(request.Mode));
        SetMember(result, "credentials", Adapter.FromString(request.Credentials));
        SetMember(result, "cache", Adapter.FromString(request.Cache));
        SetMember(result, "redirect", Adapter.FromString(request.Redirect));
        SetMember(result, "integrity", Adapter.FromString(request.Integrity));
        SetMember(result, "referrer", Adapter.FromString(request.Referrer));
        SetMember(result, "referrerPolicy", Adapter.FromString(request.ReferrerPolicy));
        return result;
    }

    private CachedResponse ToCachedResponse(TValue serialized)
    {
        // An opaque response is stored with its internal response (e.g. the body of a cross-origin script that a service worker
        // gives to a page): the scripts only see the filtered response
        var internalResponse = Adapter.Get(serialized, Key("internal"));
        var source = Adapter.IsObject(internalResponse) ? internalResponse : serialized;
        var body = Adapter.Get(source, Key("body"));
        return new CachedResponse
        {
            Type = GetString(serialized, "type"),
            Status = (int)Adapter.ToNumber(Adapter.Get(source, Key("status"))),
            StatusText = GetString(source, "statusText"),
            Headers = ToHeaderList(Adapter.Get(source, Key("headers"))),
            UrlList = [.. ToList(Adapter.Get(source, Key("urlList"))).Select(Adapter.ToString)],
            RequestIncludesCredentials = Adapter.ToBoolean(Adapter.Get(serialized, Key("requestIncludesCredentials"))),
            CrossOriginResourcePolicy = Adapter.Get(serialized, Key("crossOriginResourcePolicy")) is var policy && Adapter.IsString(policy) ? Adapter.AsString(policy) : null,
            Body = Adapter.IsNull(body) || Adapter.IsUndefined(body) ? null : GetBytes(body),
        };
    }

    private TValue FromCachedResponse(CachedResponse response)
    {
        if (response.Type is "opaque" or "opaqueredirect")
        {
            // The filtered response, and its internal response (https://fetch.spec.whatwg.org/#concept-filtered-response-opaque)
            var opaque = CreateObject();
            SetMember(opaque, "type", Adapter.FromString(response.Type));
            SetMember(opaque, "status", Adapter.FromNumber(0));
            SetMember(opaque, "statusText", Adapter.FromString(""));
            SetMember(opaque, "headers", CreateArray([]));
            SetMember(opaque, "urlList", CreateArray([]));
            SetMember(opaque, "requestIncludesCredentials", Adapter.FromBoolean(response.RequestIncludesCredentials));
            SetMember(opaque, "crossOriginResourcePolicy", response.CrossOriginResourcePolicy is { } opaquePolicy ? Adapter.FromString(opaquePolicy) : Adapter.Null);
            SetMember(opaque, "body", Adapter.Null);
            SetMember(opaque, "internal", FromCachedResponse(new CachedResponse
            {
                Status = response.Status,
                StatusText = response.StatusText,
                Headers = response.Headers,
                UrlList = response.UrlList,
                Body = response.Body,
            }));
            return opaque;
        }

        var result = CreateObject();
        SetMember(result, "type", Adapter.FromString(response.Type));
        SetMember(result, "status", Adapter.FromNumber(response.Status));
        SetMember(result, "statusText", Adapter.FromString(response.StatusText));
        SetMember(result, "headers", FromHeaderList([.. response.Headers]));
        SetMember(result, "urlList", CreateArray([.. response.UrlList.Select(Adapter.FromString)]));
        SetMember(result, "requestIncludesCredentials", Adapter.FromBoolean(response.RequestIncludesCredentials));
        SetMember(result, "crossOriginResourcePolicy", response.CrossOriginResourcePolicy is { } policy ? Adapter.FromString(policy) : Adapter.Null);
        SetMember(result, "body", response.Body is { } body ? FromBuffer(JsBufferKind.Uint8Array, body) : Adapter.Null);
        return result;
    }
}
