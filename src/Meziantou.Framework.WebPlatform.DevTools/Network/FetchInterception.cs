using System.Globalization;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;

namespace Meziantou.Framework.WebPlatform.DevTools.Network;

/// <summary>
/// The interception of the requests of a session (the <c>Fetch</c> domain): the requests that match its patterns are paused
/// (<c>Fetch.requestPaused</c>) until the client continues, fulfills or fails them. The decisions of the client are given on
/// any thread: a request can block the thread of the context (a synchronous <c>XMLHttpRequest</c>).
/// </summary>
internal sealed class FetchInterception : IDisposable
{
    private static long s_nextId;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<FetchDecision>> _pending = new(StringComparer.Ordinal);
    private readonly List<(string UrlPattern, string? ResourceType)> _patterns;
    private bool _disposed;

    public FetchInterception(JsonElement parameters)
    {
        _patterns = [];
        var hasPatterns = parameters.TryGet("patterns", out var patterns) && patterns.ValueKind == JsonValueKind.Array;
        if (hasPatterns)
        {
            foreach (var pattern in patterns.EnumerateArray())
            {
                // The response stage is not supported: the requests are paused before they are sent only
                if (pattern.GetString("requestStage") is "Response")
                    continue;

                _patterns.Add((pattern.GetString("urlPattern") ?? "*", pattern.GetString("resourceType")));
            }
        }
        else
        {
            _patterns.Add(("*", null));
        }

        HandleAuthRequests = parameters.GetBoolean("handleAuthRequests", defaultValue: false);
    }

    public bool HandleAuthRequests { get; }

    public bool IsMatch(string url, string resourceType)
    {
        foreach (var (urlPattern, patternResourceType) in _patterns)
        {
            if (patternResourceType is not null && patternResourceType != resourceType)
                continue;

            if (UrlGlob.IsMatch(urlPattern, url))
                return true;
        }

        return false;
    }

    /// <summary>Creates a paused request: its identifier, and the task of the decision of the client.</summary>
    public (string InterceptionId, Task<FetchDecision> Decision) Pause()
    {
        var id = "interception-job-" + Interlocked.Increment(ref s_nextId).ToString(CultureInfo.InvariantCulture) + ".0";
        var completion = new TaskCompletionSource<FetchDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The interception can be disabled on another thread: a request paused once it is disabled continues at once
        lock (_lock)
        {
            if (!_disposed)
            {
                _pending.Add(id, completion);
                return (id, completion.Task);
            }
        }

        completion.TrySetResult(CreateContinue());
        return (id, completion.Task);
    }

    private static FetchDecision.Continue CreateContinue() => new(Url: null, Method: null, PostData: null, Headers: null);

    /// <summary>Gives the decision of the client for a paused request (<c>Fetch.continueRequest</c>, <c>Fetch.fulfillRequest</c>...).</summary>
    public void Resolve(string method, JsonElement parameters)
    {
        var requestId = parameters.GetRequiredString("requestId");
        TaskCompletionSource<FetchDecision>? completion;
        lock (_lock)
        {
            if (!_pending.Remove(requestId, out completion))
                throw new ProtocolException("Invalid InterceptionId.");
        }

        FetchDecision decision;
        try
        {
            decision = method switch
            {
                "Fetch.continueRequest" or "Fetch.continueResponse" => new FetchDecision.Continue(
                    parameters.GetString("url"),
                    parameters.GetString("method"),
                    parameters.GetString("postData") is { } postData ? Convert.FromBase64String(postData) : null,
                    parameters.GetHeaderEntries("headers")),
                "Fetch.fulfillRequest" => new FetchDecision.Fulfill(
                    parameters.GetInt32("responseCode") ?? 200,
                    parameters.GetString("responsePhrase"),
                    parameters.GetHeaderEntries("responseHeaders") ?? [],
                    parameters.GetString("body") is { } body ? Convert.FromBase64String(body) : []),
                "Fetch.failRequest" => new FetchDecision.Fail(GetErrorText(parameters.GetString("errorReason"))),
                "Fetch.continueWithAuth" => new FetchDecision.Auth(
                    parameters.TryGet("authChallengeResponse", out var response) ? response.GetString("response") ?? "Default" : "Default",
                    response.GetString("username"),
                    response.GetString("password")),
                _ => throw ProtocolException.NotFound(method),
            };
        }
        catch (FormatException)
        {
            // The request stays paused (or continues when the interception was disabled meanwhile)
            lock (_lock)
            {
                if (!_disposed)
                {
                    _pending[requestId] = completion;
                    completion = null;
                }
            }

            completion?.TrySetResult(CreateContinue());
            throw ProtocolException.InvalidParameters("Invalid base64 string");
        }

        completion.TrySetResult(decision);
    }

    /// <summary>The error of a failed request (<c>Network.ErrorReason</c>) as the error text of the network stack.</summary>
    public static string GetErrorText(string? errorReason) => errorReason switch
    {
        "Aborted" => "net::ERR_ABORTED",
        "TimedOut" => "net::ERR_TIMED_OUT",
        "AccessDenied" => "net::ERR_ACCESS_DENIED",
        "ConnectionClosed" => "net::ERR_CONNECTION_CLOSED",
        "ConnectionReset" => "net::ERR_CONNECTION_RESET",
        "ConnectionRefused" => "net::ERR_CONNECTION_REFUSED",
        "ConnectionAborted" => "net::ERR_CONNECTION_ABORTED",
        "ConnectionFailed" => "net::ERR_CONNECTION_FAILED",
        "NameNotResolved" => "net::ERR_NAME_NOT_RESOLVED",
        "InternetDisconnected" => "net::ERR_INTERNET_DISCONNECTED",
        "AddressUnreachable" => "net::ERR_ADDRESS_UNREACHABLE",
        "BlockedByClient" => "net::ERR_BLOCKED_BY_CLIENT",
        "BlockedByResponse" => "net::ERR_BLOCKED_BY_RESPONSE",
        _ => "net::ERR_FAILED",
    };

    /// <summary>The interception is disabled: the paused requests continue.</summary>
    public void Dispose()
    {
        TaskCompletionSource<FetchDecision>[] pending;
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var completion in pending)
        {
            completion.TrySetResult(CreateContinue());
        }
    }
}

/// <summary>The decision of a client for a paused request.</summary>
internal abstract record FetchDecision
{
    public sealed record Continue(string? Url, string? Method, byte[]? PostData, List<KeyValuePair<string, string>>? Headers) : FetchDecision;

    public sealed record Fulfill(int Status, string? StatusText, List<KeyValuePair<string, string>> Headers, byte[] Body) : FetchDecision;

    public sealed record Fail(string ErrorText) : FetchDecision;

    /// <summary>The answer to an authentication challenge (<c>Default</c>, <c>CancelAuth</c> or <c>ProvideCredentials</c>).</summary>
    public sealed record Auth(string Response, string? Username, string? Password) : FetchDecision;
}
