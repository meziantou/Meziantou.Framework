using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>A request (<c>Request</c>): its parent is its page (or its context).</summary>
internal sealed class RequestDispatcher(NetworkRequest request) : Dispatcher((Dispatcher?)request.Page ?? request.Context, "Request", "request", CreateInitializer(request))
{
    public NetworkRequest Request { get; } = request;

    private static JsonObject CreateInitializer(NetworkRequest request)
    {
        var initializer = new JsonObject
        {
            ["url"] = request.Url,
            ["resourceType"] = request.ResourceType,
            ["method"] = request.Method,
            ["headers"] = JsonExtensions.ToNameValues(request.Headers),
            ["isNavigationRequest"] = request.IsNavigationRequest,
        };
        if (request.Frame is { IsDisposed: false } frame)
        {
            initializer["frame"] = frame.ToReference();
        }

        if (request.PostData is { } postData)
        {
            initializer["postData"] = Convert.ToBase64String(postData);
        }

        if (request.RedirectedFrom is { } redirectedFrom)
        {
            initializer["redirectedFrom"] = redirectedFrom.GetDispatcher().ToReference();
        }

        return initializer;
    }

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "response":
                var response = await Request.GetResponseAsync(progress).ConfigureAwait(false);
                var result = new JsonObject();
                if (response is not null)
                {
                    result["response"] = response.GetDispatcher().ToReference();
                }

                return result;

            case "rawRequestHeaders":
                return new JsonObject { ["headers"] = JsonExtensions.ToNameValues(Request.Headers) };

            default:
                return await base.InvokeAsync(method, parameters, progress).ConfigureAwait(false);
        }
    }
}

/// <summary>The response of a request (<c>Response</c>).</summary>
internal sealed class ResponseDispatcher(RequestDispatcher request, NetworkResponse response) : Dispatcher(request, "Response", "response", new JsonObject
{
    ["request"] = request.ToReference(),
    ["url"] = response.Url,
    ["status"] = response.Status,
    ["statusText"] = response.StatusText,
    ["headers"] = JsonExtensions.ToNameValues(response.Headers),
    ["timing"] = NetworkResponse.CreateTiming(response.Request.StartTime),
    ["fromServiceWorker"] = false,
})
{
    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        JsonNode? result = method switch
        {
            "body" => new JsonObject { ["binary"] = Convert.ToBase64String(response.Body.Span) },
            "securityDetails" => new JsonObject(),
            "serverAddr" => new JsonObject(),
            "rawResponseHeaders" => new JsonObject { ["headers"] = JsonExtensions.ToNameValues(response.Headers) },
            "httpVersion" => new JsonObject { ["value"] = "HTTP/1.1" },
            "sizes" => new JsonObject
            {
                ["sizes"] = new JsonObject
                {
                    ["requestBodySize"] = response.Request.PostData?.Length ?? 0,
                    ["requestHeadersSize"] = GetHeadersSize(response.Request.Headers),
                    ["responseBodySize"] = response.Body.Length,
                    ["responseHeadersSize"] = GetHeadersSize(response.Headers),
                },
            },
            _ => throw new PlaywrightError($"Response.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform"),
        };
        return ValueTask.FromResult<JsonNode?>(result);

        static int GetHeadersSize(IEnumerable<KeyValuePair<string, string>> headers) => headers.Sum(header => Encoding.UTF8.GetByteCount(header.Key) + Encoding.UTF8.GetByteCount(header.Value) + 4);
    }
}

/// <summary>The decision of a route for an intercepted request.</summary>
internal abstract record RouteDecision
{
    /// <summary>The request continues to the network (or to the next route when <see cref="IsFallback"/> is true), with overrides.</summary>
    public sealed record Continue(string? Url, string? Method, List<KeyValuePair<string, string>>? Headers, byte[]? PostData, bool IsFallback) : RouteDecision;

    /// <summary>The request gets a response without network.</summary>
    public sealed record Fulfill(int Status, List<KeyValuePair<string, string>> Headers, byte[] Body) : RouteDecision;

    /// <summary>The request fails.</summary>
    public sealed record Abort(string ErrorText) : RouteDecision;
}

/// <summary>An intercepted request (<c>Route</c>): the client continues, fulfills or aborts it.</summary>
internal sealed class RouteDispatcher(Dispatcher parent, NetworkRequest request) : Dispatcher(parent, "Route", "route", new JsonObject { ["request"] = request.GetDispatcher().ToReference() })
{
    private readonly TaskCompletionSource<RouteDecision> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<RouteDecision> Decision => _decision.Task;

    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        RouteDecision decision = method switch
        {
            "continue" => new RouteDecision.Continue(
                parameters.GetString("url"),
                parameters.GetString("method"),
                parameters.TryGet("headers", out _) ? parameters.GetNameValues("headers") : null,
                parameters.GetBinary("postData"),
                parameters.GetBoolean("isFallback", defaultValue: false)),
            "fulfill" => CreateFulfill(parameters),
            "abort" => new RouteDecision.Abort(GetErrorText(parameters.GetString("errorCode"))),
            "redirectNavigationRequest" => throw new PlaywrightError("Route.redirectNavigationRequest is not supported by the Playwright server of Meziantou.Framework.WebPlatform"),
            _ => throw new PlaywrightError($"Route.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform"),
        };

        if (!_decision.TrySetResult(decision))
            throw new PlaywrightError("Route is already handled!");

        Dispose();
        return ValueTask.FromResult<JsonNode?>(null);
    }

    private RouteDecision.Fulfill CreateFulfill(JsonElement parameters)
    {
        byte[] body;
        if (parameters.GetString("fetchResponseUid") is { } fetchResponseUid && request.Context.RequestContext.GetFetchResponse(fetchResponseUid) is { } fetchResponse)
        {
            var status = parameters.GetInt32("status") ?? fetchResponse.Status;
            var headers = parameters.TryGet("headers", out _) ? parameters.GetNameValues("headers") : [.. fetchResponse.Headers];
            return new RouteDecision.Fulfill(status, headers, parameters.GetString("body") is { } overriddenBody ? Encoding.UTF8.GetBytes(overriddenBody) : fetchResponse.Body);
        }

        var text = parameters.GetString("body") ?? "";
        body = parameters.GetBoolean("isBase64", defaultValue: false) ? Convert.FromBase64String(text) : Encoding.UTF8.GetBytes(text);
        return new RouteDecision.Fulfill(parameters.GetInt32("status") ?? 200, parameters.GetNameValues("headers"), body);
    }

    /// <summary>Cancels the route (the page or the context is closed): the request fails.</summary>
    public void Cancel() => _decision.TrySetResult(new RouteDecision.Abort("net::ERR_ABORTED"));

    /// <summary>The error text of Chromium for an error code of <c>route.abort()</c>.</summary>
    public static string GetErrorText(string? errorCode) => errorCode switch
    {
        null or "failed" => "net::ERR_FAILED",
        "aborted" => "net::ERR_ABORTED",
        "accessdenied" => "net::ERR_ACCESS_DENIED",
        "addressunreachable" => "net::ERR_ADDRESS_UNREACHABLE",
        "blockedbyclient" => "net::ERR_BLOCKED_BY_CLIENT",
        "blockedbyresponse" => "net::ERR_BLOCKED_BY_RESPONSE",
        "connectionaborted" => "net::ERR_CONNECTION_ABORTED",
        "connectionclosed" => "net::ERR_CONNECTION_CLOSED",
        "connectionfailed" => "net::ERR_CONNECTION_FAILED",
        "connectionrefused" => "net::ERR_CONNECTION_REFUSED",
        "connectionreset" => "net::ERR_CONNECTION_RESET",
        "internetdisconnected" => "net::ERR_INTERNET_DISCONNECTED",
        "namenotresolved" => "net::ERR_NAME_NOT_RESOLVED",
        "timedout" => "net::ERR_TIMED_OUT",
        _ => throw new PlaywrightError("Invalid error code: " + errorCode),
    };
}
