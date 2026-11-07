using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Request and Response objects of the service workers (https://w3c.github.io/ServiceWorker/#fetchevent): the request of a
// fetch event is a Request object created from a request of the fetch algorithm, and the Response object given to
// respondWith() is read as a response of the fetch algorithm (the objects are implemented in JavaScript, see Fetch.js).
public sealed partial class WebIdlRealm<TAdapter, TValue> : IServiceWorkerFetchOperations
{
    private TValue CacheSupport => Adapter.Get(GetFetch(), Key("cacheSupport"));

    /// <summary>
    /// Converts a value to a <c>Request</c> or a <c>Response</c> object (https://webidl.spec.whatwg.org/#js-interface): the
    /// objects are implemented in JavaScript, so the .NET member gets the value of the engine.
    /// </summary>
    internal object ToFetchObject(TValue value, string interfaceName)
    {
        if (!Adapter.IsObject(value))
            throw CreateTypeError($"The value is not of type '{interfaceName}'");

        var record = Adapter.RunInRealm(EngineRealm, () => Adapter.Call(Adapter.Get(CacheSupport, Key(interfaceName == "Request" ? "getRequestRecord" : "getResponseRecord")), Adapter.Undefined, [value]));
        if (Adapter.IsUndefined(record))
            throw CreateTypeError($"The value is not of type '{interfaceName}'");

        return value;
    }

    private object? _hostValue;

    /// <summary>
    /// Wraps a .NET value that Fetch.js keeps in the record of a request or a response (the records are not exposed to the
    /// scripts): a function that gives the value back to <see cref="FromHostValue"/>.
    /// </summary>
    private TValue ToHostValue(object value) => Adapter.CreateFunction("", 0, (_, _) =>
    {
        _hostValue = value;
        return Adapter.Undefined;
    });

    private T? FromHostValue<T>(TValue hostValue)
        where T : class
    {
        if (!Adapter.IsCallable(hostValue))
            return null;

        _hostValue = null;
        Adapter.Call(hostValue, Adapter.Undefined, []);
        var value = _hostValue as T;
        _hostValue = null;
        return value;
    }

    object IServiceWorkerFetchOperations.CreateRequest(FetchRequest request, AbortSignal abortSignal) => Adapter.RunInRealm(EngineRealm, () =>
    {
        var serialized = CreateObject();
        SetMember(serialized, "method", Adapter.FromString(request.Method));
        // The URL of the Request object is the current URL of the request; the URLs of the redirects of a navigation and
        // the context of its cookies are kept with it (see FetchRequest.NavigationRedirectUrls)
        SetMember(serialized, "urlList", CreateArray([Adapter.FromString(request.CurrentUrl.Serialize())]));
        SetMember(serialized, "redirectChain", CreateArray([.. request.UrlList.Take(request.UrlList.Count - 1).Select(url => Adapter.FromString(url.Serialize()))]));
        SetMember(serialized, "navigation", request.Mode == RequestMode.Navigate ? ToHostValue(new FetchEventNavigationContext(request.CookieContext, request.Destination)) : Adapter.Null);
        SetMember(serialized, "headers", FromHeaderList(request.HeaderList));
        SetMember(serialized, "mode", Adapter.FromString(request.Mode switch
        {
            RequestMode.SameOrigin => "same-origin",
            RequestMode.NoCors => "no-cors",
            RequestMode.Navigate => "navigate",
            RequestMode.WebSocket => "websocket",
            _ => "cors",
        }));
        SetMember(serialized, "credentials", Adapter.FromString(request.CredentialsMode switch
        {
            FetchCredentialsMode.Omit => "omit",
            FetchCredentialsMode.Include => "include",
            _ => "same-origin",
        }));
        SetMember(serialized, "cache", Adapter.FromString(request.CacheMode));
        SetMember(serialized, "redirect", Adapter.FromString(request.RedirectMode switch
        {
            FetchRedirectMode.Error => "error",
            FetchRedirectMode.Manual => "manual",
            _ => "follow",
        }));
        SetMember(serialized, "integrity", Adapter.FromString(request.Integrity));
        SetMember(serialized, "referrer", Adapter.FromString(request.Referrer));
        SetMember(serialized, "referrerPolicy", Adapter.FromString(request.ReferrerPolicy));
        SetMember(serialized, "origin", Adapter.FromString(request.Origin));
        var destination = FetchMetadata.GetDestinationName(request.Destination);
        SetMember(serialized, "destination", Adapter.FromString(destination == "empty" ? "" : destination));
        SetMember(serialized, "keepalive", Adapter.FromBoolean(request.Keepalive));
        SetMember(serialized, "reloadNavigation", Adapter.FromBoolean(request.IsReloadNavigation));
        SetMember(serialized, "historyNavigation", Adapter.FromBoolean(request.IsHistoryNavigation));
        SetMember(serialized, "body", request.Body is { } body ? FromBuffer(JsBufferKind.Uint8Array, body) : Adapter.Null);
        return (object)Adapter.Call(Adapter.Get(CacheSupport, Key("createFetchEventRequest")), Adapter.Undefined, [serialized, Wrap(abortSignal)]);
    });

    object IServiceWorkerFetchOperations.CreateResponse(FetchResponse response) => Adapter.RunInRealm(EngineRealm, () =>
        (object)Adapter.Call(Adapter.Get(CacheSupport, Key("createResponseObject")), Adapter.Undefined, [FromFetchResponse(response)]));

    void IServiceWorkerFetchOperations.ReadResponse(object value, bool streamBody, Action<FetchResponse?> callback)
    {
        var adapter = Adapter;
        if (!streamBody)
        {
            var onResponse = adapter.CreateFunction("", 1, (_, arguments) =>
            {
                var serialized = arguments.Length > 0 ? arguments[0] : adapter.Undefined;
                callback(adapter.IsObject(serialized) ? ToFetchResponse(serialized) : null);
                return adapter.Undefined;
            });

            Adapter.RunInRealm(EngineRealm, () => Adapter.Call(Adapter.Get(CacheSupport, Key("readFetchEventResponse")), Adapter.Undefined, [(TValue)value, onResponse]));
            return;
        }

        // The body is read on the thread of the service worker, and its chunks are given to the client as they are read. The
        // client that stops reading the body (e.g. an aborted fetch) cancels the stream of the body.
        var global = GlobalScope;
        TValue? cancelReading = null;
        var body = new FetchBodyChannel(() => global.QueueGlobalTask(() =>
        {
            if (cancelReading is not null)
            {
                Adapter.RunInRealm(EngineRealm, () => Adapter.Call(cancelReading, Adapter.Undefined, []));
            }
        }));
        var onStreamedResponse = adapter.CreateFunction("", 1, (_, arguments) =>
        {
            var serialized = arguments.Length > 0 ? arguments[0] : adapter.Undefined;
            callback(adapter.IsObject(serialized) ? ToFetchResponse(serialized, adapter.ToBoolean(Adapter.Get(serialized, Key("streamed"))) ? body : null) : null);
            return adapter.Undefined;
        });
        var onChunk = adapter.CreateFunction("", 1, (_, arguments) =>
        {
            body.Write(GetBytes(arguments[0]));
            return adapter.Undefined;
        });
        var onEnd = adapter.CreateFunction("", 0, (_, _) =>
        {
            body.Complete();
            return adapter.Undefined;
        });
        var onError = adapter.CreateFunction("", 0, (_, _) =>
        {
            body.Fail(new IOException("The body of the response of the service worker is errored"));
            return adapter.Undefined;
        });

        Adapter.RunInRealm(EngineRealm, () =>
        {
            var cancel = Adapter.Call(Adapter.Get(CacheSupport, Key("readFetchEventResponseStreamed")), Adapter.Undefined, [(TValue)value, onStreamedResponse, onChunk, onEnd, onError]);
            cancelReading = Adapter.IsCallable(cancel) ? cancel : null;
            return Adapter.Undefined;
        });
    }

    /// <summary>
    /// Converts a response serialized by Fetch.js to a response of the fetch algorithm: the response keeps its type (the
    /// response given by a service worker is already filtered), and an opaque response gets its internal response.
    /// </summary>
    private FetchResponse ToFetchResponse(TValue serialized, FetchBodyChannel? streamedBody = null)
    {
        var type = Adapter.ToString(Adapter.Get(serialized, Key("type")));
        if (type == "error")
            return FetchResponse.NetworkError();

        if (type is "opaque" or "opaqueredirect")
        {
            var internalValue = Adapter.Get(serialized, Key("internal"));
            var internalResponse = Adapter.IsObject(internalValue) ? CreateResponse(internalValue) : FetchResponse.Create(0, "", [], body: null);
            internalResponse.RequestIncludesCredentials = Adapter.ToBoolean(Adapter.Get(serialized, Key("requestIncludesCredentials")));
            return type == "opaque" ? internalResponse.ToOpaque() : internalResponse.ToOpaqueRedirect();
        }

        var response = CreateResponse(serialized);
        response.RequestIncludesCredentials = Adapter.ToBoolean(Adapter.Get(serialized, Key("requestIncludesCredentials")));
        switch (type)
        {
            case "basic":
                return response.ToBasic();
            case "cors":
                // The headers were already filtered: the CORS filter keeps them. The internal response has the headers of
                // the response of the network, which the filtered response hides.
                response.CorsExposedHeaderNames = [.. response.HeaderList.Select(header => header.Key)];
                if (Adapter.Get(serialized, Key("internalHeaders")) is var internalHeaders && Adapter.IsObject(internalHeaders))
                {
                    response.HeaderList = ToHeaderList(internalHeaders);
                }

                return response.ToCors();
            default:
                return response;
        }

        FetchResponse CreateResponse(TValue value)
        {
            var body = Adapter.Get(value, Key("body"));
            var status = (int)Adapter.ToNumber(Adapter.Get(value, Key("status")));
            var statusText = Adapter.ToString(Adapter.Get(value, Key("statusText")));
            var headers = ToHeaderList(Adapter.Get(value, Key("headers")));
            var result = streamedBody is not null
                ? FetchResponse.Create(status, statusText, headers, new DocumentResource(ReadOnlyMemory<byte>.Empty) { StatusCode = status, Headers = headers, RemainingContentBytes = streamedBody })
                : FetchResponse.Create(status, statusText, headers, Adapter.IsNull(body) || Adapter.IsUndefined(body) ? null : GetBytes(body));
            foreach (var url in ToList(Adapter.Get(value, Key("urlList"))))
            {
                if (UrlParser.Parse(Adapter.ToString(url), baseUrl: null) is { } parsedUrl)
                {
                    result.UrlList.Add(parsedUrl);
                }
            }

            return result;
        }
    }
}
