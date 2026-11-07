using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>
/// The loader of the documents and of the resources of the pages of a browser context: it reports the requests of the pages
/// (the <c>request</c>, <c>response</c>, <c>requestFinished</c> and <c>requestFailed</c> events), lets the clients intercept them
/// (<c>route()</c>), adds the extra HTTP headers and the HTTP credentials of the context, emulates the offline mode, and loads
/// the requests with <see cref="PlaywrightServerOptions.DocumentLoader"/>.
/// </summary>
internal sealed class ContextNetwork(BrowserContextDispatcher context, DocumentLoader? innerLoader) : DocumentLoader
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(BrowsingContext? BrowsingContext, string Url), NetworkRequest> _pendingRedirects = [];
    private readonly Dictionary<PageDispatcher, List<NetworkRequest>> _pageRequests = [];
    private List<UrlMatcher> _contextPatterns = [];
    private readonly Dictionary<PageDispatcher, List<UrlMatcher>> _pagePatterns = [];

    public void SetInterceptionPatterns(PageDispatcher? page, JsonElement parameters)
    {
        var patterns = parameters.GetArray("patterns").Select(pattern => UrlMatcher.Create(pattern, context.Options.BaseUrl)).ToList();
        lock (_lock)
        {
            if (page is null)
            {
                _contextPatterns = patterns;
            }
            else
            {
                _pagePatterns[page] = patterns;
            }
        }
    }

    /// <summary>Forgets the requests and the routes of a closed page.</summary>
    public void RemovePage(PageDispatcher page)
    {
        lock (_lock)
        {
            _pageRequests.Remove(page);
            _pagePatterns.Remove(page);
        }
    }

    public JsonArray GetPageRequests(PageDispatcher page)
    {
        NetworkRequest[] requests;
        lock (_lock)
        {
            requests = _pageRequests.TryGetValue(page, out var list) ? [.. list] : [];
        }

        return [.. requests.Select(request => (JsonNode)request.GetDispatcher().ToReference())];
    }

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken) => LoadCoreAsync(request, isWebSocket: false, cancellationToken);

    public override ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken) => LoadCoreAsync(request, isWebSocket: true, cancellationToken);

    private async ValueTask<DocumentResource?> LoadCoreAsync(DocumentRequest request, bool isWebSocket, CancellationToken cancellationToken)
    {
        // The state of the frames is updated on the thread of the context (this method starts on it, from a task of the event
        // loop); the rest runs on any thread: a synchronous request blocks the thread of the context
        var frame = context.FindFrame(request.BrowsingContext);
        var page = frame?.Page;
        var headers = new List<KeyValuePair<string, string>>(request.Headers);
        AddHeaders(headers, context.Options.ExtraHttpHeaders);
        if (page?.ExtraHttpHeaders is { } pageHeaders)
        {
            AddHeaders(headers, pageHeaders);
        }

        if (frame is not null && request.Mode == RequestMode.Navigate && frame.PendingReferer is { } referer)
        {
            frame.PendingReferer = null;
            SetHeader(headers, "Referer", referer);
        }

        NetworkRequest? redirectedFrom = null;
        if (request.RedirectUrls.Count > 0)
        {
            lock (_lock)
            {
                if (_pendingRedirects.Remove((request.BrowsingContext, request.Url), out var previous))
                {
                    redirectedFrom = previous;
                }
            }
        }

        var networkRequest = new NetworkRequest(context, request, frame, headers, NetworkRequest.GetResourceType(request, isWebSocket), redirectedFrom);
        if (page is not null)
        {
            lock (_lock)
            {
                if (!_pageRequests.TryGetValue(page, out var list))
                {
                    _pageRequests[page] = list = [];
                }

                list.Add(networkRequest);
                if (list.Count > 100)
                {
                    list.RemoveAt(0);
                }
            }
        }

        frame?.OnRequestStarted(networkRequest);
        try
        {
            if (redirectedFrom is not null || networkRequest.IsNavigationRequest || context.IsSubscribed("request", page))
            {
                context.SendEvent("request", CreateEvent(networkRequest, "request", networkRequest.GetDispatcher()));
            }

            var resource = await LoadWithRoutesAsync(networkRequest, request, headers, page, isWebSocket, cancellationToken).ConfigureAwait(false);
            return resource;
        }
        finally
        {
            if (frame is not null)
            {
                context.Engine.Post(_ => frame.OnRequestFinished(), state: null);
            }
        }
    }

    private async ValueTask<DocumentResource?> LoadWithRoutesAsync(NetworkRequest networkRequest, DocumentRequest request, List<KeyValuePair<string, string>> headers, PageDispatcher? page, bool isWebSocket, CancellationToken cancellationToken)
    {
        string? url = null;
        string? method = null;
        byte[]? postData = null;

        // The routes of the page, then the routes of the context (a route that falls back gives the request to the next one)
        foreach (var (scope, patterns) in GetRoutePatterns(page))
        {
            if (!patterns.Exists(pattern => pattern.IsMatch(request.Url)))
                continue;

            var route = new RouteDispatcher(scope, networkRequest);
            scope.SendEvent("route", new JsonObject { ["route"] = route.ToReference() });
            RouteDecision decision;
            using (cancellationToken.Register(route.Cancel))
            {
                decision = await route.Decision.ConfigureAwait(false);
            }

            switch (decision)
            {
                case RouteDecision.Abort abort:
                    return Fail(networkRequest, abort.ErrorText);

                case RouteDecision.Fulfill fulfill:
                    var fulfilled = new DocumentResource(fulfill.Body)
                    {
                        StatusCode = fulfill.Status,
                        StatusText = GetStatusText(fulfill.Status),
                        Headers = fulfill.Headers,
                    };
                    Complete(networkRequest, fulfilled, fulfill.Body);
                    return fulfilled;

                case RouteDecision.Continue continuation:
                    url = continuation.Url ?? url;
                    method = continuation.Method ?? method;
                    postData = continuation.PostData ?? postData;
                    if (continuation.Headers is not null)
                    {
                        headers.Clear();
                        headers.AddRange(continuation.Headers);
                    }

                    if (!continuation.IsFallback)
                        return await LoadFromNetworkAsync(networkRequest, request, headers, url, method, postData, isWebSocket, cancellationToken).ConfigureAwait(false);

                    break;
            }
        }

        return await LoadFromNetworkAsync(networkRequest, request, headers, url, method, postData, isWebSocket, cancellationToken).ConfigureAwait(false);
    }

    private List<(Dispatcher Scope, List<UrlMatcher> Patterns)> GetRoutePatterns(PageDispatcher? page)
    {
        var result = new List<(Dispatcher, List<UrlMatcher>)>(2);
        lock (_lock)
        {
            if (page is not null && _pagePatterns.TryGetValue(page, out var pagePatterns) && pagePatterns.Count > 0)
            {
                result.Add((page, pagePatterns));
            }

            if (_contextPatterns.Count > 0)
            {
                result.Add((context, _contextPatterns));
            }
        }

        return result;
    }

    private async ValueTask<DocumentResource?> LoadFromNetworkAsync(NetworkRequest networkRequest, DocumentRequest request, List<KeyValuePair<string, string>> headers, string? url, string? method, byte[]? postData, bool isWebSocket, CancellationToken cancellationToken)
    {
        if (context.Options.Offline)
            return Fail(networkRequest, "net::ERR_INTERNET_DISCONNECTED");

        if (innerLoader is null)
            return Fail(networkRequest, "net::ERR_FAILED");

        var effectiveRequest = CreateRequest(request, url ?? request.Url, method ?? request.Method, headers, postData is null ? request.Body : postData);
        DocumentResource? resource;
        try
        {
            resource = isWebSocket ? await innerLoader.ConnectWebSocketAsync(effectiveRequest, cancellationToken).ConfigureAwait(false) : await innerLoader.LoadAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);

            // The HTTP authentication: a 401 response is sent again with the credentials of the context
            if (resource is { StatusCode: 401 } && !isWebSocket && GetCredentials(effectiveRequest.Url) is { } credentials && !headers.Exists(header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)))
            {
                var authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials.Username + ":" + credentials.Password));
                SetHeader(headers, "Authorization", authorization);
                effectiveRequest = CreateRequest(request, effectiveRequest.Url, effectiveRequest.Method, headers, effectiveRequest.Body);
                resource = await innerLoader.LoadAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Fail(networkRequest, "net::ERR_ABORTED");
            throw;
        }
#pragma warning disable CA1031 // A failure of the loader is a network error of the page
        catch (Exception)
#pragma warning restore CA1031
        {
            return Fail(networkRequest, "net::ERR_FAILED");
        }

        if (resource is null)
            return Fail(networkRequest, "net::ERR_FAILED");

        if (isWebSocket)
        {
            Complete(networkRequest, resource, ReadOnlyMemory<byte>.Empty);
            return resource;
        }

        // The body is buffered, so that the clients can read it (response.body())
        var body = await ReadBodyAsync(resource, cancellationToken).ConfigureAwait(false);
        var buffered = new DocumentResource(body)
        {
            StatusCode = resource.StatusCode,
            StatusText = resource.StatusText,
            Headers = resource.Headers,
            Url = resource.Url,
            RedirectUrls = resource.RedirectUrls,
        };
        Complete(networkRequest, buffered, body);
        return buffered;
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(DocumentResource resource, CancellationToken cancellationToken)
    {
        if (resource.ContentBytes is not { } bytes)
            return Encoding.UTF8.GetBytes(resource.Content ?? "");

        if (resource.RemainingContentBytes is not { } remaining)
            return bytes;

        using var stream = new MemoryStream();
        stream.Write(bytes.Span);
        await foreach (var chunk in remaining.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            stream.Write(chunk.Span);
        }

        return stream.ToArray();
    }

    private void Complete(NetworkRequest networkRequest, DocumentResource resource, ReadOnlyMemory<byte> body)
    {
        var response = new NetworkResponse(networkRequest, resource.StatusCode, resource.StatusText ?? GetStatusText(resource.StatusCode), resource.Headers, body);
        networkRequest.SetResponse(response);
        var page = networkRequest.Page;
        if (networkRequest.HasDispatcher || context.IsSubscribed("response", page))
        {
            context.SendEvent("response", CreateEvent(networkRequest, "response", response.GetDispatcher()));
        }

        // A redirect: the next request is linked to this one
        if (resource.StatusCode is >= 300 and < 400 && GetHeader(resource.Headers, "Location") is { } location && Uri.TryCreate(new Uri(networkRequest.Url), location, out var target))
        {
            lock (_lock)
            {
                _pendingRedirects[(networkRequest.DocumentRequest.BrowsingContext, target.AbsoluteUri)] = networkRequest;
            }
        }

        networkRequest.SetFinished();
        if (networkRequest.HasDispatcher || context.IsSubscribed("requestFinished", page))
        {
            var finished = CreateEvent(networkRequest, "request", networkRequest.GetDispatcher());
            finished["response"] = response.GetDispatcher().ToReference();
            finished["responseEndTiming"] = networkRequest.ResponseEndTime;
            context.SendEvent("requestFinished", finished);
        }

        // A navigation that creates no document (a 204 or 205 response) ends with an error
        if (networkRequest is { IsNavigationRequest: true, Frame: { } frame } && resource.StatusCode is 204 or 205)
        {
            context.Engine.Post(_ => frame.OnNavigationAborted(networkRequest, "net::ERR_ABORTED"), state: null);
        }
    }

    private DocumentResource? Fail(NetworkRequest networkRequest, string errorText)
    {
        networkRequest.SetFailed(errorText);
        var page = networkRequest.Page;
        if (networkRequest.HasDispatcher || context.IsSubscribed("requestFailed", page))
        {
            var failed = CreateEvent(networkRequest, "request", networkRequest.GetDispatcher());
            failed["failureText"] = errorText;
            failed["responseEndTiming"] = networkRequest.ResponseEndTime;
            context.SendEvent("requestFailed", failed);
        }

        return null;
    }

    private static JsonObject CreateEvent(NetworkRequest request, string name, Dispatcher dispatcher)
    {
        var result = new JsonObject { [name] = dispatcher.ToReference() };
        if (request.Page is { IsDisposed: false } page)
        {
            result["page"] = page.ToReference();
        }

        return result;
    }

    private HttpCredentials? GetCredentials(string url)
    {
        foreach (var credentials in context.Options.HttpCredentials)
        {
            if (credentials.Origin is null || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && string.Equals(uri.GetLeftPart(UriPartial.Authority), credentials.Origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                return credentials;
        }

        return null;
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
    };

    private static void AddHeaders(List<KeyValuePair<string, string>> headers, List<KeyValuePair<string, string>> extraHeaders)
    {
        foreach (var (name, value) in extraHeaders)
        {
            SetHeader(headers, name, value);
        }
    }

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
}

/// <summary>A pattern of URLs of the routes of the clients (a glob, a regular expression or a URL pattern).</summary>
internal sealed partial class UrlMatcher
{
    private static readonly HashSet<char> EscapedChars = ['$', '^', '+', '.', '*', '(', ')', '|', '\\', '?', '{', '}', '[', ']'];

    private readonly Regex? _regex;
    private readonly UrlPattern? _urlPattern;
    private readonly bool _matchesAll;

    private UrlMatcher(Regex? regex, UrlPattern? urlPattern, bool matchesAll)
    {
        _regex = regex;
        _urlPattern = urlPattern;
        _matchesAll = matchesAll;
    }

    public static UrlMatcher Create(JsonElement pattern, string? baseUrl)
    {
        if (pattern.GetString("regexSource") is { } source)
            return new UrlMatcher(JavaScriptRegex.Create(source, pattern.GetString("regexFlags")), urlPattern: null, matchesAll: false);

        if (pattern.TryGet("urlPattern", out var urlPattern))
        {
            try
            {
                var input = new UrlPatternInit
                {
                    Protocol = urlPattern.GetString("protocol"),
                    Username = urlPattern.GetString("username"),
                    Password = urlPattern.GetString("password"),
                    Hostname = urlPattern.GetString("hostname"),
                    Port = urlPattern.GetString("port"),
                    Pathname = urlPattern.GetString("pathname"),
                    Search = urlPattern.GetString("search"),
                    Hash = urlPattern.GetString("hash"),
                };
                return new UrlMatcher(regex: null, new UrlPattern(input, new UrlPatternOptions { IgnoreCase = true }), matchesAll: false);
            }
#pragma warning disable CA1031 // An invalid pattern intercepts all the requests (the client checks the URL again)
            catch (Exception)
#pragma warning restore CA1031
            {
                return new UrlMatcher(regex: null, urlPattern: null, matchesAll: true);
            }
        }

        var glob = pattern.GetString("glob");
        if (string.IsNullOrEmpty(glob))
            return new UrlMatcher(regex: null, urlPattern: null, matchesAll: true);

        // A relative glob is resolved against the base URL of the context: the requests are intercepted, and the client
        // checks their URL (with its own resolution)
        if (baseUrl is not null && !glob.StartsWith("*", StringComparison.Ordinal) && !glob.Contains("://", StringComparison.Ordinal))
            return new UrlMatcher(regex: null, urlPattern: null, matchesAll: true);

        try
        {
            return new UrlMatcher(new Regex(GlobToRegexPattern(glob.Replace("\\\\?", "?", StringComparison.Ordinal)), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)), urlPattern: null, matchesAll: false);
        }
        catch (ArgumentException)
        {
            return new UrlMatcher(regex: null, urlPattern: null, matchesAll: true);
        }
    }

    public bool IsMatch(string url)
    {
        if (_matchesAll)
            return true;

        if (_regex is not null)
            return _regex.IsMatch(url);

        return _urlPattern is not null && _urlPattern.Test(url);
    }

    /// <summary>Converts a glob of the routes to a regular expression (<c>globToRegexPattern</c> of Playwright).</summary>
    public static string GlobToRegexPattern(string glob)
    {
        var tokens = new StringBuilder("^");
        var inGroup = false;
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '\\' && i + 1 < glob.Length)
            {
                var escaped = glob[++i];
                if (EscapedChars.Contains(escaped))
                {
                    tokens.Append('\\');
                }

                tokens.Append(escaped);
                continue;
            }

            if (c == '*')
            {
                var charBefore = i > 0 ? glob[i - 1] : '\0';
                var starCount = 1;
                while (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    starCount++;
                    i++;
                }

                if (starCount > 1)
                {
                    var charAfter = i + 1 < glob.Length ? glob[i + 1] : '\0';
                    if (charAfter == '/')
                    {
                        tokens.Append(charBefore == '/' ? "((.+/)|)" : "(.*/)");
                        i++;
                    }
                    else
                    {
                        tokens.Append("(.*)");
                    }
                }
                else
                {
                    tokens.Append("([^/]*)");
                }

                continue;
            }

            switch (c)
            {
                case '{':
                    if (inGroup)
                        throw new ArgumentException("nested '{' is not supported", nameof(glob));

                    inGroup = true;
                    tokens.Append('(');
                    break;

                case '}':
                    if (!inGroup)
                        throw new ArgumentException("unmatched '}'", nameof(glob));

                    inGroup = false;
                    tokens.Append(')');
                    break;

                case ',':
                    tokens.Append(inGroup ? "|" : "\\,");
                    break;

                default:
                    if (EscapedChars.Contains(c))
                    {
                        tokens.Append('\\');
                    }

                    tokens.Append(c);
                    break;
            }
        }

        if (inGroup)
            throw new ArgumentException("unmatched '{'", nameof(glob));

        return tokens.Append('$').ToString();
    }
}
