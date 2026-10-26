using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Request and Response interfaces and fetch() of the Fetch Standard (https://fetch.spec.whatwg.org/#fetch-api),
// implemented in JavaScript (Fetch.js) because their bodies are streams. The script is evaluated in a realm when one of
// them is first used. The fetch algorithm (Dom/Fetch/Fetching.cs) and the Headers interface are implemented in .NET.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The members of the global object defined by Fetch.js.</summary>
    internal static readonly string[] FetchGlobalNames = ["Request", "Response", "fetch"];

    private static string? s_fetchScript;
    private TValue? _fetch;

    private void DefineFetchGlobals()
    {
        foreach (var name in FetchGlobalNames)
        {
            // fetch() is an operation of the global object: it is enumerable (https://webidl.spec.whatwg.org/#es-operations).
            // It is a member of WindowOrWorkerGlobalScope: an own property of a window (Window is a [Global] interface), and
            // a property of the interface prototype object of WorkerGlobalScope for a worker.
            var isOperation = name == "fetch";
            var target = isOperation && Window is null ? GetPrototype("WorkerGlobalScope") : Global;
            Adapter.DefineLazyDataProperty(target, Key(name), () => Adapter.Get(GetFetch(), Key(name)), writable: true, enumerable: isOperation, configurable: true);
        }

        // https://fetch.spec.whatwg.org/#fetch-method: [Exposed=Window] interface FetchLaterResult, and the [SecureContext]
        // fetchLater() operation of Window
        if (Window is not null && Features.FetchLaterEnabled)
        {
            Adapter.DefineLazyDataProperty(Global, Key("FetchLaterResult"), () => Adapter.Get(GetFetch(), Key("FetchLaterResult")), writable: true, enumerable: false, configurable: true);
            if (IsSecureContext)
            {
                Adapter.DefineLazyDataProperty(Global, Key("fetchLater"), () => Adapter.Get(GetFetch(), Key("fetchLater")), writable: true, enumerable: true, configurable: true);
            }
        }
    }

    /// <summary>Returns the object returned by Fetch.js in this realm: the interface objects and fetch().</summary>
    private TValue GetFetch()
    {
        if (_fetch is { } fetch)
            return fetch;

        return Adapter.RunInRealm(EngineRealm, () =>
        {
            if (_fetch is { } existing)
                return existing;

            var script = s_fetchScript ??= LoadEmbeddedScript("Meziantou.Framework.WebPlatform.Scripting.WebIdl.Fetch.js");
            var adapter = Adapter;
            var streams = GetStreams();
            var interfaces = CreateObject();
            foreach (var name in (ReadOnlySpan<string>)["Headers", "AbortSignal", "Blob", "FormData", "URLSearchParams", "TextDecoder", "TextEncoder", "DOMException"])
            {
                Adapter.Set(interfaces, Key(name), GetInterfaceObject(name));
            }

            var hooks = CreateObject();
            Adapter.Set(hooks, Key("interfaces"), interfaces);
            Adapter.Set(hooks, Key("streams"), Adapter.Get(streams, Key("fetchSupport")));
            Adapter.Set(hooks, Key("internals"), Agent.FetchInternals ??= EvaluateBuiltIn("new WeakMap()"));
            Adapter.Set(hooks, Key("SharedArrayBuffer"), SharedArrayBufferConstructor ?? Adapter.Undefined);

            // The .NET exceptions (e.g. the TypeError of FetchBodyParsing) are thrown as JavaScript exceptions
            void Define(string name, int length, JsHostFunction<TValue> function) => Adapter.Set(hooks, Key(name), adapter.CreateFunction(name, length, (thisValue, arguments) =>
            {
                try
                {
                    return function(thisValue, arguments);
                }
                catch (Exception ex) when (!adapter.TryGetThrownValue(ex, out TValue? _))
                {
                    throw ToJsException(ex);
                }
            }));

            // Whether the this value of fetch() is a window, with the security check of the members of a Window (a cross-origin
            // window throws a "SecurityError" DOMException)
            Define("isWindow", 1, (_, args) =>
            {
                if (!Adapter.IsObject(args[0]) || Unwrap(args[0]) is not Window window)
                    return adapter.FromBoolean(false);

                CheckCrossOriginAccess(window);
                return adapter.FromBoolean(true);
            });
            Define("isAbortSignal", 1, (_, args) => adapter.FromBoolean(Adapter.IsObject(args[0]) && Unwrap(args[0]) is AbortSignal));
            Define("isHeaders", 1, (_, args) => adapter.FromBoolean(Adapter.IsObject(args[0]) && Unwrap(args[0]) is Headers));
            Define("isBlob", 1, (_, args) => adapter.FromBoolean(Adapter.IsObject(args[0]) && Unwrap(args[0]) is Blob));
            Define("isFormData", 1, (_, args) => adapter.FromBoolean(Adapter.IsObject(args[0]) && Unwrap(args[0]) is FormData));
            Define("isURLSearchParams", 1, (_, args) => adapter.FromBoolean(Adapter.IsObject(args[0]) && Unwrap(args[0]) is UrlSearchParams));
            Define("blobBytes", 1, (_, args) => FromBuffer(JsBufferKind.Uint8Array, ((Blob)Unwrap(args[0])!).Data));
            Define("createBlob", 2, (_, args) => Wrap(new Blob([GetBytes(args[0])], new BlobPropertyBag { Type = Adapter.ToString(args[1]) })));
            Define("serializeFormData", 1, (_, args) =>
            {
                var (bytes, contentType) = XmlHttpRequest.ExtractBody(Unwrap(args[0])!);
                return CreateArray([FromBuffer(JsBufferKind.Uint8Array, bytes), Adapter.FromString(contentType!)]);
            });
            Define("parseFormData", 2, (_, args) => Wrap(FetchBodyParsing.ParseFormData(GetBytes(args[0]), Adapter.IsString(args[1]) ? Adapter.ToString(args[1]) : null)));
            Define("utf8Encode", 1, (_, args) => FromBuffer(JsBufferKind.Uint8Array, System.Text.Encoding.UTF8.GetBytes(Adapter.ToString(args[0]))));

            // The headers of the requests and the responses
            Define("createHeaders", 2, (_, args) => Wrap(new Headers(ToHeaderList(args[0]), ParseGuard(Adapter.ToString(args[1])))));
            Define("headerList", 1, (_, args) => FromHeaderList(((Headers)Unwrap(args[0])!).List));
            Define("clearHeaders", 1, (_, args) =>
            {
                ((Headers)Unwrap(args[0])!).List.Clear();
                return adapter.Undefined;
            });
            Define("setGuard", 2, (_, args) =>
            {
                ((Headers)Unwrap(args[0])!).Guard = ParseGuard(Adapter.ToString(args[1]));
                return adapter.Undefined;
            });
            Define("getGuard", 1, (_, args) => Adapter.FromString(FormatGuard(((Headers)Unwrap(args[0])!).Guard)));
            Define("hasHeader", 2, (_, args) => Adapter.FromBoolean(FetchHeaders.Contains(((Headers)Unwrap(args[0])!).List, Adapter.ToString(args[1]))));
            Define("extractMimeType", 1, (_, args) => HeaderList.ExtractMimeType(((Headers)Unwrap(args[0])!).List) is { } mimeType ? Adapter.FromString(mimeType.ToString()) : Adapter.Null);

            // The environment settings object of the realm (https://html.spec.whatwg.org/multipage/webappapis.html#environment-settings-object)
            Define("parseUrl", 1, (_, args) =>
            {
                if (UrlParser.Parse(Adapter.ToString(args[0]), GlobalScope.ApiBaseUrl) is not { } url)
                    return adapter.Null;

                var result = CreateObject();
                SetMember(result, "href", Adapter.FromString(url.Serialize()));
                SetMember(result, "origin", Adapter.FromString(url.SerializeOrigin()));
                SetMember(result, "includesCredentials", Adapter.FromBoolean(url.IncludesCredentials));
                return result;
            });
            // https://url.spec.whatwg.org/#concept-url-blob-entry: the blob of a blob: URL, resolved when the URL is parsed
            Define("resolveBlobUrl", 1, (_, args) => UrlParser.Parse(Adapter.ToString(args[0]), baseUrl: null) is { Scheme: "blob" } blobUrl && BlobUrlStore.Get(GlobalScope.EnvironmentDocument).Resolve(blobUrl) is { } entry ? Wrap(entry.Object) : adapter.Null);
            Define("origin", 0, (_, _) => Adapter.FromString(GlobalScope.Origin));
            Define("queueTask", 1, (_, args) =>
            {
                var callback = args[0];
                GlobalScope.QueueGlobalTask(() => adapter.Call(callback, adapter.Undefined, []));
                return adapter.Undefined;
            });

            // https://dom.spec.whatwg.org/#abortsignal-add
            Define("addAbortAlgorithm", 2, (_, args) =>
            {
                var signal = (AbortSignal)Unwrap(args[0])!;
                var callback = args[1];
                signal.AddAbortAlgorithm(() => adapter.Call(callback, adapter.Undefined, []));
                return adapter.Undefined;
            });

            Define("fetch", 2, (_, args) => Fetch(args[0], args[1]));
            Define("fetchLater", 2, (_, args) => FetchLater(args[0], args[1]));

            var result = Adapter.Call(EvaluateBuiltIn(script), Adapter.Undefined, [hooks]);
            _fetch = result;
            return result;
        });
    }

    /// <summary>
    /// Fetches the request serialized by Fetch.js (https://fetch.spec.whatwg.org/#fetch-method, step 12): the callback is
    /// called from a task with the response. Returns the function that aborts the fetch.
    /// </summary>
    private TValue Fetch(TValue serialized, TValue callback)
    {
        var document = GlobalScope.EnvironmentDocument;
        var request = CreateFetchRequest(serialized, document);

#pragma warning disable CA2000 // The controller is disposed when the fetch completes
        var controller = new CancellationTokenSource();
#pragma warning restore CA2000
        return StartFetch(request, document, controller, callback);
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#dom-window-fetchlater, from the check of the document: queues the deferred fetch of the
    /// request serialized by Fetch.js, and returns an object with the <c>isActivated()</c> and <c>abort()</c> functions.
    /// </summary>
    private TValue FetchLater(TValue serialized, TValue activateAfterValue)
    {
        var adapter = Adapter;
        if (Window is not { } window || !window.Document.IsFullyActive)
            throw new TypeErrorException("The document is not fully active");

        var document = window.Document;
        var request = CreateFetchRequest(serialized, document);
        var url = request.Url;
        if (url.Scheme is not ("http" or "https"))
            throw new TypeErrorException("The scheme of the URL of a deferred request must be http or https");

        if (!ReferrerPolicies.IsPotentiallyTrustworthy(url))
            throw new TypeErrorException("The URL of a deferred request must be potentially trustworthy");

        var quota = DeferredFetch.GetAvailableQuota(document, OriginRecord.FromUrl(url));
        var requested = DeferredFetch.ComputeTotalRequestLength(request, document);
        if (quota < requested)
            throw new QuotaExceededError("The deferred request exceeds the quota of the deferred fetches of the document", quota, requested);

        var activated = false;
        double? activateAfter = adapter.IsNull(activateAfterValue) || adapter.IsUndefined(activateAfterValue) ? null : adapter.ToNumber(activateAfterValue);
        var record = DeferredFetch.Queue(request, document, activateAfter, () => activated = true);
        var result = CreateObject();
        SetMember(result, "isActivated", adapter.CreateFunction("isActivated", 0, (_, _) => adapter.FromBoolean(activated)));
        SetMember(result, "abort", adapter.CreateFunction("abort", 0, (_, _) =>
        {
            // "Set deferredRecord's invoke state to "aborted"": a request that was sent is not affected
            if (record.InvokeState == DeferredFetchInvokeState.Pending)
            {
                record.InvokeState = DeferredFetchInvokeState.Aborted;
                document.FetchGroup.DeferredFetchRecords.Remove(record);
            }

            return adapter.Undefined;
        }));
        return result;
    }

    /// <summary>Creates the request of the fetch algorithm from a request serialized by Fetch.js.</summary>
    private FetchRequest CreateFetchRequest(TValue serialized, Document document)
    {
        var urlList = new List<UrlRecord>();
        foreach (var item in ToList(Adapter.Get(serialized, Key("urlList"))))
        {
            urlList.Add(UrlParser.Parse(Adapter.ToString(item), baseUrl: null)!);
        }

        var body = Adapter.Get(serialized, Key("body"));
        var request = new FetchRequest(urlList[0])
        {
            Method = Adapter.ToString(Adapter.Get(serialized, Key("method"))),
            UrlList = urlList,
            HeaderList = ToHeaderList(Adapter.Get(serialized, Key("headers"))),
            UnsafeRequest = true,
            Body = Adapter.IsNull(body) ? null : GetBytes(body),
            BodyHasSource = Adapter.ToBoolean(Adapter.Get(serialized, Key("bodyHasSource"))),
            Client = document,
            // The origin of a request created from the request of a fetch event (e.g. a navigation) is kept, else it is the origin of the client
            Origin = Adapter.Get(serialized, Key("origin")) is var origin && Adapter.IsString(origin) && Adapter.ToString(origin) is var originString && originString != "client" ? originString : document.SerializeOrigin(),
            Referrer = Adapter.ToString(Adapter.Get(serialized, Key("referrer"))),
            ReferrerPolicy = Adapter.ToString(Adapter.Get(serialized, Key("referrerPolicy"))),
            Mode = Adapter.ToString(Adapter.Get(serialized, Key("mode"))) switch
            {
                "same-origin" => RequestMode.SameOrigin,
                "no-cors" => RequestMode.NoCors,
                "navigate" => RequestMode.Navigate,
                _ => RequestMode.Cors,
            },
            UseCorsPreflight = Adapter.ToBoolean(Adapter.Get(serialized, Key("useCorsPreflight"))),
            CredentialsMode = Adapter.ToString(Adapter.Get(serialized, Key("credentials"))) switch
            {
                "omit" => FetchCredentialsMode.Omit,
                "include" => FetchCredentialsMode.Include,
                _ => FetchCredentialsMode.SameOrigin,
            },
            CacheMode = Adapter.ToString(Adapter.Get(serialized, Key("cache"))),
            Keepalive = Adapter.ToBoolean(Adapter.Get(serialized, Key("keepalive"))),
            Destination = ParseDestination(Adapter.Get(serialized, Key("destination"))),

            // https://fetch.spec.whatwg.org/#fetch-method: the requests of a service worker are not handled by service workers
            ServiceWorkersMode = GlobalScope is ServiceWorkerGlobalScope ? ServiceWorkersMode.None : ServiceWorkersMode.All,
            Integrity = Adapter.ToString(Adapter.Get(serialized, Key("integrity"))),
            BlobUrlEntry = Unwrap(Adapter.Get(serialized, Key("blob"))) is Blob blob ? new BlobUrlEntry(blob, document) : null,
            RedirectMode = Adapter.ToString(Adapter.Get(serialized, Key("redirect"))) switch
            {
                "error" => FetchRedirectMode.Error,
                "manual" => FetchRedirectMode.Manual,
                _ => FetchRedirectMode.Follow,
            },
        };

        // The body of the response is a ReadableStream that is read as the body arrives, except when the complete body is
        // checked: the integrity metadata, and the hashes of the scripts reported to the content security policies (the
        // request of a fetch event fetched again by its service worker keeps its destination)
        request.StreamsBody = request.Integrity.Length == 0 && request.Destination is not (RequestDestination.Script or RequestDestination.PaintWorklet or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker);

        // The request of a fetch event fetched again by its service worker: the redirects of a navigation, and its cookies
        if (Adapter.Get(serialized, Key("redirectChain")) is var redirectChain && Adapter.IsObject(redirectChain))
        {
            foreach (var item in ToList(redirectChain))
            {
                if (UrlParser.Parse(Adapter.ToString(item), baseUrl: null) is { } redirectUrl)
                {
                    request.NavigationRedirectUrls.Add(redirectUrl);
                }
            }
        }

        if (request.Mode == RequestMode.Navigate && FromHostValue<FetchEventNavigationContext>(Adapter.Get(serialized, Key("navigation"))) is { } navigation)
        {
            request.CookieContext = navigation.CookieContext;
            request.NavigationDestination = navigation.Destination;
        }

        return request;
    }

    private TValue StartFetch(FetchRequest request, Document document, CancellationTokenSource controller, TValue callback)
    {
        var adapter = Adapter;

        // The fetch is aborted with the document (https://fetch.spec.whatwg.org/#concept-fetch-group), unless it is a keepalive
        // request: the response is an aborted network error, and the response of the loader is ignored. A body that is read
        // as it arrives is aborted too: its stream is errored with an "AbortError" DOMException.
        var done = false;
        var abortedByFetchGroup = false;
        var finished = false;
        FetchBodyReader? bodyReader = null;
        Action? bodyEnded = null;
        IDisposable? fetchGroupRecord = null;

        // The fetch is done (https://fetch.spec.whatwg.org/#done-flag): its response is received and its body is read (or
        // canceled). The controller is not disposed while a chunk may still be read with its token.
        void Finish(bool disposeController)
        {
            if (finished)
                return;

            finished = true;
            fetchGroupRecord?.Dispose();
            if (disposeController)
            {
                controller.Dispose();

                // The end of a body read as it arrives
                bodyEnded?.Invoke();
            }
        }

        void Complete(FetchResponse response)
        {
            // The tasks queued before the abort are discarded
            if (done || (abortedByFetchGroup && !response.Aborted))
                return;

            done = true;

            // The body of an opaque response is hidden from the scripts: the body of its internal response is read in the
            // background (e.g. to give the response to a client of a service worker), and the abort of the fetch stops it
            var isOpaque = response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect;
            bodyReader = finished || response.IsNetworkError ? null : FetchBodyReader.Create(isOpaque ? response.Internal : response, controller.Token);
            if (bodyReader is null)
            {
                Finish(disposeController: true);
            }

            var reader = bodyReader is null ? null : CreateBodyReader(bodyReader, document, () => controller.IsCancellationRequested, Finish, controller.Cancel);
            adapter.Call(callback, adapter.Undefined, [isOpaque ? FromFetchResponse(response, internalBodyReader: reader) : FromFetchResponse(response, reader)]);
        }

        fetchGroupRecord = document.FetchGroup.Add(() =>
        {
            abortedByFetchGroup = true;
            controller.Cancel();
            bodyReader?.Cancel();
            document.QueueGlobalTask(() => Complete(FetchResponse.NetworkError(aborted: true)));
        }, request);

        var timing = ResourceTiming.Start(GlobalScope, request, "fetch");
        var task = Fetching.FetchAsync(request, controller.Token);
        _ = task.ContinueWith(
            completed => document.QueueGlobalTask(() =>
            {
                var report = !done && !abortedByFetchGroup;
                Complete(completed.Result);

                // The timing of a body read as it arrives is reported at the end of the body (https://fetch.spec.whatwg.org/#fetch-finale)
                if (report && bodyReader is null)
                {
                    timing?.Report(completed.Result);
                }
                else if (report && timing is not null)
                {
                    var response = completed.Result;
                    var reader = bodyReader;
                    bodyEnded = () => timing.ReportEndOfBody(response, reader!.Length);
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // https://fetch.spec.whatwg.org/#fetch-controller-abort: the fetch, or the reading of its body, is aborted
        return adapter.CreateFunction("abort", 0, (_, _) =>
        {
            if (!finished)
            {
                controller.Cancel();
                bodyReader?.Cancel();
                if (done)
                {
                    Finish(disposeController: false);
                }
            }

            return adapter.Undefined;
        });
    }

    /// <summary>
    /// The object from which Fetch.js pulls the chunks of a body that arrives over time (see <see cref="FetchBodyReader"/>):
    /// <c>read(callback)</c> calls the callback from a task with the next chunk (a <c>Uint8Array</c>), with null at the end of
    /// the body, or with <c>(undefined, error, aborted)</c> when the body fails; <c>cancel()</c> stops reading the body.
    /// </summary>
    private TValue CreateBodyReader(FetchBodyReader reader, Document document, Func<bool> isAborted, Action<bool> finish, Action terminate)
    {
        var adapter = Adapter;
        var result = CreateObject();
        SetMember(result, "read", adapter.CreateFunction("read", 1, (thisValue, args) =>
        {
            var callback = args[0];
            Task<ReadOnlyMemory<byte>?> read;
            try
            {
                read = reader.ReadAsync();
            }
#pragma warning disable CA1031 // A failure of the body errors the stream
            catch (Exception ex)
#pragma warning restore CA1031
            {
                read = Task.FromException<ReadOnlyMemory<byte>?>(ex);
            }

            // The chunk is given from a task, even when it is already received (the body is read in parallel)
            var eventLoop = document.EnvironmentEventLoop;
            eventLoop?.BeginOperation();
            _ = read.ContinueWith(
                completed =>
                {
                    document.QueueGlobalTask(() =>
                    {
                        if (completed.IsCompletedSuccessfully && completed.Result is { } chunk)
                        {
                            adapter.Call(callback, adapter.Undefined, [FromBuffer(JsBufferKind.Uint8Array, chunk.ToArray())]);
                            return;
                        }

                        var aborted = isAborted();
                        finish(completed.IsCompletedSuccessfully);
                        adapter.Call(callback, adapter.Undefined, completed.IsCompletedSuccessfully ? [adapter.Null] : [adapter.Undefined, adapter.FromBoolean(true), adapter.FromBoolean(aborted)]);
                    });
                    eventLoop?.EndOperation();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return adapter.Undefined;
        }));
        // https://fetch.spec.whatwg.org/#concept-fetch: the cancel algorithm of the stream of the body terminates the fetch
        SetMember(result, "cancel", adapter.CreateFunction("cancel", 0, (_, _) =>
        {
            terminate();
            reader.Cancel();
            finish(false);
            return adapter.Undefined;
        }));
        return result;
    }

    /// <summary>
    /// Converts a response of the fetch algorithm to the object read by Fetch.js. The chunks of a body that arrives over time
    /// are pulled from <paramref name="bodyReader"/> (see <see cref="CreateBodyReader"/>), and the ones of the body of the
    /// internal response of an opaque response from <paramref name="internalBodyReader"/>.
    /// </summary>
    private TValue FromFetchResponse(FetchResponse response, TValue? bodyReader = null, TValue? internalBodyReader = null)
    {
        var result = CreateObject();
        SetMember(result, "type", Adapter.FromString(response.Type switch
        {
            FetchResponseType.Basic => "basic",
            FetchResponseType.Cors => "cors",
            FetchResponseType.Error => "error",
            FetchResponseType.Opaque => "opaque",
            FetchResponseType.OpaqueRedirect => "opaqueredirect",
            _ => "default",
        }));
        SetMember(result, "aborted", Adapter.FromBoolean(response.Aborted));
        SetMember(result, "status", Adapter.FromNumber(response.Status));
        SetMember(result, "statusText", Adapter.FromString(response.StatusMessage));
        SetMember(result, "headers", FromHeaderList(response.HeaderList));
        if (bodyReader is not null)
        {
            SetMember(result, "body", Adapter.Null);
            SetMember(result, "bodyReader", bodyReader);
        }
        else
        {
            SetMember(result, "body", response.Body is { } body ? FromBuffer(JsBufferKind.Uint8Array, body) : Adapter.Null);
            SetMember(result, "bodyReader", Adapter.Null);
        }

        SetMember(result, "urlList", CreateArray(response.UrlList.Select(url => Adapter.FromString(url.Serialize())).ToArray()));

        // The data of the internal response of an opaque response used by the cross-origin resource policy check of the Cache API
        SetMember(result, "requestIncludesCredentials", Adapter.FromBoolean(response.Internal.RequestIncludesCredentials));
        SetMember(result, "crossOriginResourcePolicy", HeaderList.Get(response.Internal.HeaderList, "Cross-Origin-Resource-Policy") is { } policy ? Adapter.FromString(policy) : Adapter.Null);

        // The internal response of an opaque response, given back to a client when a service worker answers its request with it
        SetMember(result, "internal", response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect ? FromInternalResponse(response.Internal, streamed: internalBodyReader is not null) : Adapter.Null);
        SetMember(result, "internalBodyReader", internalBodyReader ?? Adapter.Null);

        // The headers of the internal response of a CORS response that the filtered response hides (e.g. Content-Range,
        // which a media element needs when a service worker answers its range request with the response)
        SetMember(result, "internalHeaders", response.Type == FetchResponseType.Cors && response.Internal.HeaderList.Count != response.HeaderList.Count ? FromHeaderList(response.Internal.HeaderList) : Adapter.Null);
        return result;
    }

    /// <summary>The internal response of an opaque response; the body of a body read as it arrives is set by Fetch.js at its end.</summary>
    private TValue FromInternalResponse(FetchResponse response, bool streamed = false)
    {
        var result = CreateObject();
        SetMember(result, "status", Adapter.FromNumber(response.Status));
        SetMember(result, "statusText", Adapter.FromString(response.StatusMessage));
        SetMember(result, "headers", FromHeaderList(response.HeaderList));
        SetMember(result, "body", !streamed && response.Body is { } body ? FromBuffer(JsBufferKind.Uint8Array, body) : Adapter.Null);
        SetMember(result, "urlList", CreateArray(response.UrlList.Select(url => Adapter.FromString(url.Serialize())).ToArray()));
        return result;
    }

    /// <summary>The destination of a request serialized by Fetch.js (https://fetch.spec.whatwg.org/#concept-request-destination).</summary>
    private RequestDestination ParseDestination(TValue value) => Adapter.IsString(value) ? Adapter.ToString(value) switch
    {
        "document" => RequestDestination.Document,
        "iframe" => RequestDestination.Iframe,
        "frame" => RequestDestination.Frame,
        "embed" => RequestDestination.Embed,
        "object" => RequestDestination.Object,
        "script" => RequestDestination.Script,
        "style" => RequestDestination.Style,
        "image" => RequestDestination.Image,
        "font" => RequestDestination.Font,
        "json" => RequestDestination.Json,
        "text" => RequestDestination.Text,
        "worker" => RequestDestination.Worker,
        "sharedworker" => RequestDestination.SharedWorker,
        "serviceworker" => RequestDestination.ServiceWorker,
        "audio" => RequestDestination.Audio,
        "video" => RequestDestination.Video,
        "track" => RequestDestination.Track,
        "paintworklet" => RequestDestination.PaintWorklet,
        _ => RequestDestination.Empty,
    } : RequestDestination.Empty;

    private static HeadersGuard ParseGuard(string guard) => guard switch
    {
        "request" => HeadersGuard.Request,
        "request-no-cors" => HeadersGuard.RequestNoCors,
        "response" => HeadersGuard.Response,
        "immutable" => HeadersGuard.Immutable,
        _ => HeadersGuard.None,
    };

    private static string FormatGuard(HeadersGuard guard) => guard switch
    {
        HeadersGuard.Request => "request",
        HeadersGuard.RequestNoCors => "request-no-cors",
        HeadersGuard.Response => "response",
        HeadersGuard.Immutable => "immutable",
        _ => "none",
    };

    /// <summary>Converts an array of [name, value] arrays to a header list.</summary>
    private List<KeyValuePair<string, string>> ToHeaderList(TValue array)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var pair in ToList(array))
        {
            list.Add(new(Adapter.ToString(Adapter.Get(pair, Key("0"))), Adapter.ToString(Adapter.Get(pair, Key("1")))));
        }

        return list;
    }

    private TValue FromHeaderList(List<KeyValuePair<string, string>> list)
    {
        var pairs = new TValue[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            pairs[i] = CreateArray([Adapter.FromString(list[i].Key), Adapter.FromString(list[i].Value)]);
        }

        return CreateArray(pairs);
    }

    /// <summary>The elements of an array created by Fetch.js.</summary>
    private List<TValue> ToList(TValue array)
    {
        var length = (int)Adapter.ToNumber(Adapter.Get(array, Key("length")));
        var items = new List<TValue>(length);
        for (var i = 0; i < length; i++)
        {
            items.Add(Adapter.Get(array, Key(i.ToString(CultureInfo.InvariantCulture))));
        }

        return items;
    }

    /// <summary>The bytes of a Uint8Array created by Fetch.js.</summary>
    private byte[] GetBytes(TValue value) => TryToBufferSource(value, 1u << (int)JsBufferKind.Uint8Array, allowShared: false)?.Bytes.ToArray() ?? [];

    private static string LoadEmbeddedScript(string name)
    {
        using var stream = typeof(WebIdlRealm<TAdapter, TValue>).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The {name} resource is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
