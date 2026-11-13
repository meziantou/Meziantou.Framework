using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Browser;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Targets;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Network;

/// <summary>
/// The loader of the documents and of the resources of the pages of a browser context: it reports the requests of the pages
/// (the <c>Network</c> domain), lets the clients intercept them (the <c>Fetch</c> domain), adds the extra HTTP headers of the
/// sessions, emulates the offline mode, and loads the requests with <see cref="DevToolsServerOptions.DocumentLoader"/>.
/// </summary>
/// <remarks>
/// A request starts on the thread of the context (from a task of the event loop); the rest runs on any thread: a synchronous
/// request blocks the thread of the context, so the state of the frames is updated with <see cref="Engine.EngineThread.Post"/>.
/// </remarks>
internal sealed class ContextLoader(BrowserContextHost context, DocumentLoader? innerLoader) : DocumentLoader
{
    private const int MaxResponseBodies = 200;

    private readonly Lock _lock = new();
    private readonly Dictionary<(BrowsingContext? BrowsingContext, string Url), PendingRedirect> _pendingRedirects = [];
    private readonly Dictionary<PageTarget, Dictionary<string, (byte[] Body, byte[]? PostData)>> _bodies = [];
    private long _nextRequestId;

    /// <summary>Forgets the responses of a closed page.</summary>
    public void RemovePage(PageTarget page)
    {
        lock (_lock)
        {
            _bodies.Remove(page);
        }
    }

    /// <summary>Returns the body of a response of a page (<c>Network.getResponseBody</c>).</summary>
    public byte[]? GetResponseBody(PageTarget page, string requestId)
    {
        lock (_lock)
        {
            return _bodies.TryGetValue(page, out var bodies) && bodies.TryGetValue(requestId, out var entry) ? entry.Body : null;
        }
    }

    /// <summary>Returns the body of a request of a page (<c>Network.getRequestPostData</c>).</summary>
    public byte[]? GetRequestPostData(PageTarget page, string requestId)
    {
        lock (_lock)
        {
            return _bodies.TryGetValue(page, out var bodies) && bodies.TryGetValue(requestId, out var entry) ? entry.PostData : null;
        }
    }

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken) => Load(request, isWebSocket: false, cancellationToken);

    public override ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken) => Load(request, isWebSocket: true, cancellationToken);

    private ValueTask<DocumentResource?> Load(DocumentRequest request, bool isWebSocket, CancellationToken cancellationToken)
    {
        // The requests of a page that waits for the client (a popup the client has not set up yet) wait until it runs. A
        // synchronous request blocks the thread of the context: it does not wait.
        if (!request.IsSynchronous && context.FindFrame(request.BrowsingContext)?.Target is { IsPaused: true } target)
            return LoadAfterResumeAsync(target, request, isWebSocket, cancellationToken);

        return LoadCoreAsync(request, isWebSocket, cancellationToken);
    }

    private async ValueTask<DocumentResource?> LoadAfterResumeAsync(PageTarget target, DocumentRequest request, bool isWebSocket, CancellationToken cancellationToken)
    {
        await target.Resumed.WaitAsync(cancellationToken).ConfigureAwait(false);

        // The request starts on the thread of the context, as the other requests
        return await context.Engine.InvokeAsync(() => LoadCoreAsync(request, isWebSocket, cancellationToken).AsTask()).ConfigureAwait(false);
    }

    private async ValueTask<DocumentResource?> LoadCoreAsync(DocumentRequest request, bool isWebSocket, CancellationToken cancellationToken)
    {
        var frame = context.FindFrame(request.BrowsingContext);
        var target = frame?.Target;
        var resourceType = GetResourceType(request, isWebSocket);
        var isNavigation = frame is not null && request.Mode == RequestMode.Navigate && resourceType == "Document";

        var headers = new List<KeyValuePair<string, string>>(request.Headers);
        if (target is not null)
        {
            foreach (var (name, value) in target.GetExtraHeaders())
            {
                SetHeader(headers, name, value);
            }
        }

        if (isNavigation && frame!.PendingReferrer is { } referrer)
        {
            frame.PendingReferrer = null;
            SetHeader(headers, "Referer", referrer);
        }

        PendingRedirect? redirect = null;
        if (request.RedirectUrls.Count > 0)
        {
            lock (_lock)
            {
                _pendingRedirects.Remove((request.BrowsingContext, request.Url), out redirect);
            }
        }

        var requestId = redirect?.RequestId ?? (isNavigation ? CdpConnection.CreateId() : "R" + Interlocked.Increment(ref _nextRequestId).ToString(CultureInfo.InvariantCulture));
        var info = new RequestInfo(requestId, request, frame, headers, resourceType, isNavigation);
        if (isNavigation)
        {
            frame!.PendingLoaderId = requestId;
            frame.PendingNavigationError = null;
            if (!frame.IsClientNavigation && redirect is null)
            {
                target!.Broadcast(session => session.PageEnabled, "Page.frameRequestedNavigation", new JsonObject
                {
                    ["frameId"] = frame.FrameId,
                    ["reason"] = "scriptInitiated",
                    ["url"] = request.Url,
                    ["disposition"] = "currentTab",
                });
            }

            if (redirect is null)
            {
                target!.Broadcast(session => session.PageEnabled, "Page.frameStartedLoading", new JsonObject { ["frameId"] = frame.FrameId });
            }
        }

        if (frame is not null)
        {
            target!.OnRequestStarted(frame);
        }

        try
        {
            if (target is not null)
            {
                if (isWebSocket)
                {
                    target.Broadcast(session => session.NetworkEnabled, "Network.webSocketCreated", new JsonObject { ["requestId"] = requestId, ["url"] = request.Url, ["initiator"] = new JsonObject { ["type"] = "script" } });
                    target.Broadcast(session => session.NetworkEnabled, "Network.webSocketWillSendHandshakeRequest", new JsonObject
                    {
                        ["requestId"] = requestId,
                        ["timestamp"] = PageTarget.Timestamp,
                        ["wallTime"] = PageTarget.WallTime,
                        ["request"] = new JsonObject { ["headers"] = JsonExtensions.ToHeaderObject(headers) },
                    });
                }
                else
                {
                    var willBeSent = CreateRequestWillBeSent(info);
                    if (redirect is not null)
                    {
                        willBeSent["redirectResponse"] = redirect.Response;
                        willBeSent["redirectHasExtraInfo"] = false;
                    }

                    target.Broadcast(session => session.NetworkEnabled, "Network.requestWillBeSent", willBeSent);
                }
            }

            return await LoadWithInterceptionAsync(info, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (frame is not null)
            {
                context.Engine.Post(_ => target!.OnRequestFinished(frame), state: null);
            }
        }
    }

    private async ValueTask<DocumentResource?> LoadWithInterceptionAsync(RequestInfo info, CancellationToken cancellationToken)
    {
        var url = info.Request.Url;
        var method = info.Request.Method;
        var postData = info.Request.Body;
        if (info.Target is { } target && !info.IsWebSocket && !url.StartsWith("data:", StringComparison.Ordinal) && FindInterception(target, url, info.ResourceType) is { } interception)
        {
            var (session, fetch) = interception;
            var (interceptionId, decisionTask) = fetch.Pause();
            var paused = new JsonObject
            {
                ["requestId"] = interceptionId,
                ["request"] = CreateRequestPayload(info),
                ["frameId"] = info.Frame?.FrameId ?? target.TargetId,
                ["resourceType"] = info.ResourceType,
                ["networkId"] = info.RequestId,
            };
            session.SendEvent("Fetch.requestPaused", paused);
            FetchDecision decision;
            try
            {
                decision = await decisionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Fail(info, "net::ERR_ABORTED");
                throw;
            }

            switch (decision)
            {
                case FetchDecision.Fail fail:
                    return Fail(info, fail.ErrorText);

                case FetchDecision.Fulfill fulfill:
                    var fulfilled = new DocumentResource(fulfill.Body)
                    {
                        StatusCode = fulfill.Status,
                        StatusText = fulfill.StatusText is { Length: > 0 } statusText ? statusText : GetStatusText(fulfill.Status),
                        Headers = fulfill.Headers,
                        Url = url,
                    };
                    Complete(info, fulfilled, fulfill.Body);
                    return fulfilled;

                case FetchDecision.Continue continuation:
                    url = continuation.Url ?? url;
                    method = continuation.Method ?? method;
                    postData = continuation.PostData ?? postData;
                    if (continuation.Headers is not null)
                    {
                        info.Headers.Clear();
                        info.Headers.AddRange(continuation.Headers);
                    }

                    break;
            }

            return await LoadFromNetworkAsync(info, url, method, postData, interception, cancellationToken).ConfigureAwait(false);
        }

        return await LoadFromNetworkAsync(info, url, method, postData, interception: null, cancellationToken).ConfigureAwait(false);
    }

    private static (PageSession Session, FetchInterception Fetch)? FindInterception(PageTarget target, string url, string resourceType)
    {
        foreach (var session in target.GetSessions())
        {
            if (session.Fetch is { } fetch && fetch.IsMatch(url, resourceType))
                return (session, fetch);
        }

        return null;
    }

    private async ValueTask<DocumentResource?> LoadFromNetworkAsync(RequestInfo info, string url, string method, ReadOnlyMemory<byte>? postData, (PageSession Session, FetchInterception Fetch)? interception, CancellationToken cancellationToken)
    {
        if (info.Target?.IsOffline() == true)
            return Fail(info, "net::ERR_INTERNET_DISCONNECTED");

        if (innerLoader is null)
            return Fail(info, "net::ERR_FAILED");

        var effectiveRequest = CreateRequest(info.Request, url, method, info.Headers, postData);
        DocumentResource? resource;
        try
        {
            resource = info.IsWebSocket ? await innerLoader.ConnectWebSocketAsync(effectiveRequest, cancellationToken).ConfigureAwait(false) : await innerLoader.LoadAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);

            // The HTTP authentication: the client answers the challenge of a 401 response (Fetch.authRequired)
            var attempts = 0;
            while (resource is { StatusCode: 401 } && !info.IsWebSocket && interception is { Fetch.HandleAuthRequests: true } auth && attempts++ < 3)
            {
                var challenge = GetHeader(resource.Headers, "WWW-Authenticate") ?? "Basic";
                var (authId, authTask) = auth.Fetch.Pause();
                auth.Session.SendEvent("Fetch.authRequired", new JsonObject
                {
                    ["requestId"] = authId,
                    ["request"] = CreateRequestPayload(info),
                    ["frameId"] = info.Frame?.FrameId ?? info.Target!.TargetId,
                    ["resourceType"] = info.ResourceType,
                    ["authChallenge"] = new JsonObject
                    {
                        ["source"] = "Server",
                        ["origin"] = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : url,
                        ["scheme"] = challenge.Split(' ', 2)[0].ToLowerInvariant(),
                        ["realm"] = "",
                    },
                });
                if (await authTask.WaitAsync(cancellationToken).ConfigureAwait(false) is not FetchDecision.Auth { Response: "ProvideCredentials" } credentials)
                    break;

                var authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes((credentials.Username ?? "") + ":" + (credentials.Password ?? "")));
                SetHeader(info.Headers, "Authorization", authorization);
                effectiveRequest = CreateRequest(info.Request, url, method, info.Headers, postData);
                resource = await innerLoader.LoadAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Fail(info, "net::ERR_ABORTED");
            throw;
        }
#pragma warning disable CA1031 // A failure of the loader is a network error of the page
        catch (Exception)
#pragma warning restore CA1031
        {
            return Fail(info, "net::ERR_FAILED");
        }

        if (resource is null)
            return Fail(info, "net::ERR_FAILED");

        if (info.IsWebSocket)
        {
            info.Target?.Broadcast(session => session.NetworkEnabled, "Network.webSocketHandshakeResponseReceived", new JsonObject
            {
                ["requestId"] = info.RequestId,
                ["timestamp"] = PageTarget.Timestamp,
                ["response"] = new JsonObject
                {
                    ["status"] = resource.StatusCode is 0 or 200 ? 101 : resource.StatusCode,
                    ["statusText"] = resource.StatusText ?? "Switching Protocols",
                    ["headers"] = JsonExtensions.ToHeaderObject(resource.Headers),
                },
            });
            return resource;
        }

        // The body is buffered, so that the clients can read it (Network.getResponseBody)
        var body = await ReadBodyAsync(resource, cancellationToken).ConfigureAwait(false);
        var buffered = new DocumentResource(body)
        {
            StatusCode = resource.StatusCode,
            StatusText = resource.StatusText,
            Headers = resource.Headers,
            Url = resource.Url,
            RedirectUrls = resource.RedirectUrls,
        };
        Complete(info, buffered, body);
        return buffered;
    }

    private static async Task<byte[]> ReadBodyAsync(DocumentResource resource, CancellationToken cancellationToken)
    {
        if (resource.ContentBytes is not { } bytes)
            return Encoding.UTF8.GetBytes(resource.Content ?? "");

        if (resource.RemainingContentBytes is not { } remaining)
            return bytes.ToArray();

        using var stream = new MemoryStream();
        stream.Write(bytes.Span);
        await foreach (var chunk in remaining.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            stream.Write(chunk.Span);
        }

        return stream.ToArray();
    }

    private void Complete(RequestInfo info, DocumentResource resource, ReadOnlyMemory<byte> body)
    {
        var target = info.Target;
        if (target is null)
            return;

        var response = CreateResponsePayload(info, resource, body.Length);

        // A redirect followed by the library: the next request continues this one (its requestWillBeSent event has the response)
        if (resource.StatusCode is >= 300 and < 400 && !info.Request.FollowRedirects && GetHeader(resource.Headers, "Location") is { } location && Uri.TryCreate(new Uri(info.Request.Url), location, out var redirectTarget))
        {
            var key = (info.Request.BrowsingContext, redirectTarget.AbsoluteUri);
            var pending = new PendingRedirect(info.RequestId, response);
            lock (_lock)
            {
                _pendingRedirects[key] = pending;
            }

            // A redirect that is not followed (e.g. the redirect mode "manual") completes the request
            _ = CompleteUnfollowedRedirectAsync(info, key, pending, body.Length);
            return;
        }

        StoreBody(target, info.RequestId, body.ToArray(), info.Request.Body?.ToArray());
        target.Broadcast(session => session.NetworkEnabled, "Network.responseReceived", new JsonObject
        {
            ["requestId"] = info.RequestId,
            ["loaderId"] = info.LoaderId,
            ["timestamp"] = PageTarget.Timestamp,
            ["type"] = info.ResourceType,
            ["response"] = response,
            ["hasExtraInfo"] = false,
            ["frameId"] = info.Frame?.FrameId ?? target.TargetId,
        });
        target.Broadcast(session => session.NetworkEnabled, "Network.loadingFinished", new JsonObject
        {
            ["requestId"] = info.RequestId,
            ["timestamp"] = PageTarget.Timestamp,
            ["encodedDataLength"] = body.Length,
        });

        // A navigation that creates no document (a 204 or 205 response) ends with an error
        if (info is { IsNavigation: true, Frame: { } frame } && resource.StatusCode is 204 or 205)
        {
            context.Engine.Post(_ =>
            {
                if (frame.PendingLoaderId == info.RequestId)
                {
                    frame.PendingLoaderId = null;
                    frame.IsClientNavigation = false;
                    frame.CompleteNavigation(new Targets.NavigationResult(info.RequestId, "net::ERR_ABORTED"));
                }
            }, state: null);
        }
    }

    private async Task CompleteUnfollowedRedirectAsync(RequestInfo info, (BrowsingContext? BrowsingContext, string Url) key, PendingRedirect pending, int length)
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        lock (_lock)
        {
            if (!_pendingRedirects.TryGetValue(key, out var current) || !ReferenceEquals(current, pending))
                return;

            _pendingRedirects.Remove(key);
        }

        var target = info.Target!;
        target.Broadcast(session => session.NetworkEnabled, "Network.responseReceived", new JsonObject
        {
            ["requestId"] = info.RequestId,
            ["loaderId"] = info.LoaderId,
            ["timestamp"] = PageTarget.Timestamp,
            ["type"] = info.ResourceType,
            ["response"] = pending.Response.DeepClone(),
            ["hasExtraInfo"] = false,
            ["frameId"] = info.Frame?.FrameId ?? target.TargetId,
        });
        target.Broadcast(session => session.NetworkEnabled, "Network.loadingFinished", new JsonObject { ["requestId"] = info.RequestId, ["timestamp"] = PageTarget.Timestamp, ["encodedDataLength"] = length });
    }

    private void StoreBody(PageTarget target, string requestId, byte[] body, byte[]? postData)
    {
        lock (_lock)
        {
            if (!_bodies.TryGetValue(target, out var bodies))
            {
                bodies = new Dictionary<string, (byte[], byte[]?)>(StringComparer.Ordinal);
                _bodies[target] = bodies;
            }

            if (bodies.Count >= MaxResponseBodies)
            {
                bodies.Remove(bodies.Keys.First());
            }

            bodies[requestId] = (body, postData);
        }
    }

    private DocumentResource? Fail(RequestInfo info, string errorText)
    {
        info.Target?.Broadcast(session => session.NetworkEnabled, "Network.loadingFailed", new JsonObject
        {
            ["requestId"] = info.RequestId,
            ["timestamp"] = PageTarget.Timestamp,
            ["type"] = info.ResourceType,
            ["errorText"] = errorText,
            ["canceled"] = errorText == "net::ERR_ABORTED",
        });

        if (info is { IsNavigation: true, Frame: { } frame })
        {
            context.Engine.Post(_ =>
            {
                if (frame.PendingLoaderId == info.RequestId)
                {
                    frame.PendingNavigationError = errorText;

                    // An aborted navigation creates no document: the client waits for nothing else
                    if (errorText == "net::ERR_ABORTED")
                    {
                        frame.PendingLoaderId = null;
                        frame.IsClientNavigation = false;
                        frame.CompleteNavigation(new Targets.NavigationResult(info.RequestId, errorText));
                    }
                }
            }, state: null);
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Payloads
    // ----------------------------------------------------------------------------------------------------------------

    private static JsonObject CreateRequestWillBeSent(RequestInfo info) => new()
    {
        ["requestId"] = info.RequestId,
        ["loaderId"] = info.LoaderId,
        ["documentURL"] = info.Frame?.Url ?? info.Request.Url,
        ["request"] = CreateRequestPayload(info),
        ["timestamp"] = PageTarget.Timestamp,
        ["wallTime"] = PageTarget.WallTime,
        ["initiator"] = new JsonObject { ["type"] = info.IsNavigation ? "other" : "script" },
        ["redirectHasExtraInfo"] = false,
        ["type"] = info.ResourceType,
        ["frameId"] = info.Frame?.FrameId ?? info.Target?.TargetId ?? "",
        ["hasUserGesture"] = info.Request.UserActivation,
    };

    private static JsonObject CreateRequestPayload(RequestInfo info)
    {
        var url = info.Request.Url;
        string? fragment = null;
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = url[hash..];
            url = url[..hash];
        }

        var payload = new JsonObject
        {
            ["url"] = url,
            ["method"] = info.Request.Method,
            ["headers"] = JsonExtensions.ToHeaderObject(info.Headers),
            ["mixedContentType"] = "none",
            ["initialPriority"] = info.IsNavigation ? "VeryHigh" : "High",
            ["referrerPolicy"] = info.Request.ReferrerPolicy is { Length: > 0 } referrerPolicy ? referrerPolicy : "strict-origin-when-cross-origin",
        };
        if (fragment is not null)
        {
            payload["urlFragment"] = fragment;
        }

        if (info.Request.Body is { } body)
        {
            payload["hasPostData"] = true;
            payload["postData"] = Encoding.UTF8.GetString(body.Span);
            payload["postDataEntries"] = new JsonArray(new JsonObject { ["bytes"] = Convert.ToBase64String(body.Span) });
        }

        return payload;
    }

    private static JsonObject CreateResponsePayload(RequestInfo info, DocumentResource resource, int length)
    {
        var contentType = GetHeader(resource.Headers, "Content-Type") ?? "";
        var mimeType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        var charset = "";
        var charsetIndex = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (charsetIndex >= 0)
        {
            charset = contentType[(charsetIndex + "charset=".Length)..].Split(';')[0].Trim().Trim('"');
        }

        var url = resource.Url ?? info.Request.Url;
        return new JsonObject
        {
            ["url"] = url,
            ["status"] = resource.StatusCode,
            ["statusText"] = resource.StatusText ?? GetStatusText(resource.StatusCode),
            ["headers"] = JsonExtensions.ToHeaderObject(resource.Headers),
            ["mimeType"] = mimeType,
            ["charset"] = charset,
            ["connectionReused"] = false,
            ["connectionId"] = 0,
            ["remoteIPAddress"] = "",
            ["remotePort"] = 0,
            ["fromDiskCache"] = false,
            ["fromServiceWorker"] = false,
            ["fromPrefetchCache"] = false,
            ["encodedDataLength"] = length,
            ["responseTime"] = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["protocol"] = "http/1.1",
            ["securityState"] = url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? "secure" : "neutral",
        };
    }

    /// <summary>Returns the type of a resource as the protocol names it (<c>Document</c>, <c>Script</c>, <c>Image</c>...).</summary>
    public static string GetResourceType(DocumentRequest request, bool isWebSocket)
    {
        if (isWebSocket)
            return "WebSocket";

        return request.Destination switch
        {
            RequestDestination.Document or RequestDestination.Iframe or RequestDestination.Frame => "Document",
            RequestDestination.Style => "Stylesheet",
            RequestDestination.Image => "Image",
            RequestDestination.Font => "Font",
            RequestDestination.Script or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker or RequestDestination.PaintWorklet or RequestDestination.AudioWorklet => "Script",
            RequestDestination.Audio or RequestDestination.Video => "Media",
            RequestDestination.Track => "TextTrack",
            RequestDestination.Ping => "Ping",
            RequestDestination.Report => "CSPViolationReport",
            _ => request.Mode == RequestMode.Navigate ? "Document" : "Fetch",
        };
    }

    private static DocumentRequest CreateRequest(DocumentRequest request, string url, string method, List<KeyValuePair<string, string>> headers, ReadOnlyMemory<byte>? body) => new(url)
    {
        CookieJar = request.CookieJar,
        SiteForCookies = request.SiteForCookies,
        TopLevelOrigin = request.TopLevelOrigin,
        RedirectUrls = request.RedirectUrls,
        Destination = request.Destination,
        Initiator = request.Initiator,
        BrowsingContext = request.BrowsingContext,
        Origin = request.Origin,
        Referrer = request.Referrer,
        ReferrerPolicy = request.ReferrerPolicy,
        Mode = request.Mode,
        IsSpeculative = request.IsSpeculative,
        UserActivation = request.UserActivation,
        Method = method,
        Body = body,
        BodyContentType = request.BodyContentType,
        Headers = [.. headers],
        IncludeCredentials = request.IncludeCredentials,
        FollowRedirects = request.FollowRedirects,
        IsSynchronous = request.IsSynchronous,
        Integrity = request.Integrity,
        InterimResponseHandler = request.ReportInterimResponse,
    };

    private static void SetHeader(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        headers.RemoveAll(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        headers.Add(new(name, value));
    }

    private static string? GetHeader(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var (key, value) in headers)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static string GetStatusText(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        301 => "Moved Permanently",
        302 => "Found",
        303 => "See Other",
        304 => "Not Modified",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "",
    };

    /// <summary>A request of a page: its identifier, and the identifier of the document it loads (a navigation) or of the document of its frame.</summary>
    private sealed class RequestInfo(string requestId, DocumentRequest request, FrameState? frame, List<KeyValuePair<string, string>> headers, string resourceType, bool isNavigation)
    {
        public string RequestId { get; } = requestId;

        public DocumentRequest Request { get; } = request;

        public FrameState? Frame { get; } = frame;

        public PageTarget? Target => Frame?.Target;

        public List<KeyValuePair<string, string>> Headers { get; } = headers;

        public string ResourceType { get; } = resourceType;

        public bool IsNavigation { get; } = isNavigation;

        public bool IsWebSocket => ResourceType == "WebSocket";

        public string LoaderId { get; } = isNavigation ? requestId : frame?.LoaderId ?? "";
    }

    private sealed record PendingRedirect(string RequestId, JsonObject Response);
}
