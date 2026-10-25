using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>A request of a page (the <c>Request</c> of the Playwright server): its object of the protocol is created when needed.</summary>
internal sealed class NetworkRequest
{
    private readonly TaskCompletionSource<NetworkResponse?> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lock = new();
    private RequestDispatcher? _dispatcher;

    public NetworkRequest(BrowserContextDispatcher context, DocumentRequest documentRequest, FrameDispatcher? frame, List<KeyValuePair<string, string>> headers, string resourceType, NetworkRequest? redirectedFrom)
    {
        Context = context;
        DocumentRequest = documentRequest;
        Frame = frame;
        Url = documentRequest.Url;
        Method = documentRequest.Method;
        Headers = headers;
        PostData = documentRequest.Body?.ToArray();
        ResourceType = resourceType;
        IsNavigationRequest = documentRequest.Mode == RequestMode.Navigate && resourceType == "document";
        RedirectedFrom = redirectedFrom;
        if (redirectedFrom is not null)
        {
            redirectedFrom.RedirectedTo = this;
        }

        StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public BrowserContextDispatcher Context { get; }

    public DocumentRequest DocumentRequest { get; }

    public FrameDispatcher? Frame { get; }

    public PageDispatcher? Page => Frame?.Page;

    public string Url { get; }

    public string Method { get; }

    public List<KeyValuePair<string, string>> Headers { get; }

    public byte[]? PostData { get; }

    public string ResourceType { get; }

    public bool IsNavigationRequest { get; }

    public NetworkRequest? RedirectedFrom { get; }

    public NetworkRequest? RedirectedTo { get; private set; }

    public double StartTime { get; }

    public NetworkResponse? Response { get; private set; }

    /// <summary>Gets the error text of a failed request (e.g. <c>net::ERR_FAILED</c>), or null.</summary>
    public string? FailureText { get; private set; }

    public double ResponseEndTime { get; private set; } = -1;

    public bool HasDispatcher
    {
        get
        {
            lock (_lock)
            {
                return _dispatcher is not null;
            }
        }
    }

    /// <summary>Returns the object of the protocol of the request (created on first use, with the object of the request it redirects).</summary>
    public RequestDispatcher GetDispatcher()
    {
        RedirectedFrom?.GetDispatcher();
        lock (_lock)
        {
            return _dispatcher ??= new RequestDispatcher(this);
        }
    }

    public NetworkRequest GetFinalRequest()
    {
        var current = this;
        while (current.RedirectedTo is { } next)
        {
            current = next;
        }

        return current;
    }

    public async Task<NetworkResponse?> GetResponseAsync(Progress progress) => await progress.RaceAsync(_response.Task).ConfigureAwait(true);

    public void SetResponse(NetworkResponse response)
    {
        Response = response;
        _response.TrySetResult(response);
    }

    public void SetFailed(string failureText)
    {
        FailureText = failureText;
        ResponseEndTime = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - StartTime) / 1;
        _response.TrySetResult(null);
    }

    public void SetFinished() => ResponseEndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - StartTime;

    /// <summary>Returns the type of a resource as Playwright names it (<c>document</c>, <c>script</c>, <c>image</c>...).</summary>
    public static string GetResourceType(DocumentRequest request, bool isWebSocket)
    {
        if (isWebSocket)
            return "websocket";

        return request.Destination switch
        {
            RequestDestination.Document or RequestDestination.Iframe or RequestDestination.Frame => "document",
            RequestDestination.Style => "stylesheet",
            RequestDestination.Image => "image",
            RequestDestination.Font => "font",
            RequestDestination.Script or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker or RequestDestination.PaintWorklet => "script",
            RequestDestination.Audio or RequestDestination.Video => "media",
            RequestDestination.Track => "texttrack",
            RequestDestination.Ping or RequestDestination.Report => "ping",
            _ => request.Mode == RequestMode.Navigate ? "document" : "fetch",
        };
    }
}

/// <summary>The response of a request.</summary>
internal sealed class NetworkResponse(NetworkRequest request, int status, string statusText, IReadOnlyList<KeyValuePair<string, string>> headers, ReadOnlyMemory<byte> body)
{
    private readonly Lock _lock = new();
    private ResponseDispatcher? _dispatcher;

    public NetworkRequest Request { get; } = request;

    public string Url => Request.Url;

    public int Status { get; } = status;

    public string StatusText { get; } = statusText;

    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; } = headers;

    public ReadOnlyMemory<byte> Body { get; } = body;

    public ResponseDispatcher GetDispatcher()
    {
        var requestDispatcher = Request.GetDispatcher();
        lock (_lock)
        {
            return _dispatcher ??= new ResponseDispatcher(requestDispatcher, this);
        }
    }

    public static JsonObject CreateTiming(double startTime) => new()
    {
        ["startTime"] = startTime,
        ["domainLookupStart"] = -1,
        ["domainLookupEnd"] = -1,
        ["connectStart"] = -1,
        ["secureConnectionStart"] = -1,
        ["connectEnd"] = -1,
        ["requestStart"] = 0,
        ["responseStart"] = 0,
    };
}
