using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The initialization dictionary of an <see cref="EventSource"/> (https://html.spec.whatwg.org/multipage/server-sent-events.html#eventsourceinit).</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#eventsourceinit"/>
[IdlInterface("EventSourceInit", "https://html.spec.whatwg.org/multipage/server-sent-events.html#eventsourceinit")]
public sealed class EventSourceInit
{
    /// <summary>Gets whether the credentials mode of the requests is "include" (the cookies are sent to another origin). The default is false.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsourceinit-withcredentials"/>
    [IdlMember("withCredentials", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsourceinit-withcredentials")]
    public bool WithCredentials { get; init; }
}

/// <summary>
/// A connection to a server that sends events (https://html.spec.whatwg.org/multipage/server-sent-events.html#the-eventsource-interface):
/// the response of a CORS request for a <c>text/event-stream</c> resource is interpreted as its bytes arrive (see
/// <see cref="DocumentResource.RemainingContentBytes"/>), and each event is dispatched as a <see cref="MessageEvent"/>. When
/// the response ends, the connection is reestablished after the reconnection time, with the <c>Last-Event-ID</c> header.
/// </summary>
/// <remarks>
/// <para>
/// The relevant global object of the event source is the current global object when it is created (the window or the
/// worker of the script that creates it): its events are fired by tasks of its event loop, and the reconnection time is
/// measured by the timers of that event loop (<see cref="DomEventLoop.QueueTimer"/>). An event source created without
/// current global object cannot connect: its <see cref="ReadyState"/> is <see cref="Closed"/>.
/// </para>
/// <para>
/// An open connection that waits for events is not an asynchronous operation of its event loop:
/// <see cref="DomEventLoop.RunUntilIdleAsync"/> does not wait for the next event. The event sources of a document are closed
/// when the document is unloaded or destroyed, and the ones of a worker when it ends.
/// </para>
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#eventsource"/>
[IdlInterface("EventSource", "https://html.spec.whatwg.org/multipage/server-sent-events.html#eventsource")]
#pragma warning disable CA1001 // The fetch controller is disposed when the connection is closed
public sealed class EventSource : EventTarget
#pragma warning restore CA1001
{
    /// <summary>The reconnection time of a new event source (it is implementation-defined; it is 3 seconds in Chromium and WebKit).</summary>
    private static readonly TimeSpan DefaultReconnectionTime = TimeSpan.FromSeconds(3);

    /// <summary>The event sources of each global object that are not closed, for the unloading document cleanup steps.</summary>
    private static readonly ConditionalWeakTable<GlobalScope, List<EventSource>> ActiveEventSources = [];

    /// <summary>The state of an event source whose connection has not yet been established, or was closed and is being reestablished.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-connecting"/>
    [IdlMember("CONNECTING", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-connecting")]
    public const ushort Connecting = 0;

    /// <summary>The state of an event source whose connection is open: the events are dispatched as they are received.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-open"/>
    [IdlMember("OPEN", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-open")]
    public const ushort Open = 1;

    /// <summary>The state of an event source whose connection is not open and is not reestablished: it failed, or <see cref="Close"/> was called.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-closed"/>
    [IdlMember("CLOSED", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-closed")]
    public const ushort Closed = 2;

    private readonly GlobalScope? _global;
    private readonly DomEventLoop? _eventLoop;
    private readonly UrlRecord _url;
    private CancellationTokenSource? _fetchController;
    private IDisposable? _fetchGroupRecord;
    private TimeSpan _reconnectionTime = DefaultReconnectionTime;
    private string _lastEventId = "";

    /// <summary>The number of the connection attempt: the callbacks of the previous fetches are ignored.</summary>
    private int _connectionId;

    /// <summary>
    /// Creates an event source and starts to establish its connection (https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource).
    /// </summary>
    /// <param name="url">The URL of the event stream, possibly relative to the API base URL of the relevant global object.</param>
    /// <param name="eventSourceInitDict">The options, or null.</param>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the URL is not valid.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource")]
    public EventSource(string url, EventSourceInit? eventSourceInitDict = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _global = RelevantGlobal;
        _eventLoop = _global?.EventLoop;

        // Step 3: the URL is encoding-parsed relative to the settings object
        var urlRecord = _global is null ? UrlParser.Parse(url, baseUrl: null) : Html.Reflection.EncodingParseUrl(_global.EnvironmentDocument, url);
        _url = urlRecord ?? throw DomException.Syntax("The URL is not valid: " + url);
        WithCredentials = eventSourceInitDict?.WithCredentials ?? false;
        if (_global is null || _eventLoop is null)
        {
            // There is no event loop to fire the events: the connection fails silently
            ReadyState = Closed;
            return;
        }

        ActiveEventSources.GetOrCreateValue(_global).Add(this);
        StartFetch();
    }

    /// <summary>Gets the URL of the event stream, serialized (https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-url).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-url"/>
    [IdlMember("url", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-url")]
    public string Url => _url.Serialize();

    /// <summary>Gets whether the credentials mode of the requests is "include": the cookies are also sent to another origin.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-withcredentials"/>
    [IdlMember("withCredentials", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-withcredentials")]
    public bool WithCredentials { get; }

    /// <summary>
    /// Gets the state of the connection (https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-readystate):
    /// <see cref="Connecting"/>, <see cref="Open"/> or <see cref="Closed"/>.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-readystate"/>
    [IdlMember("readyState", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-readystate")]
    public ushort ReadyState { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>open</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#handler-eventsource-onopen"/>
    [IdlMember("onopen", "https://html.spec.whatwg.org/multipage/server-sent-events.html#handler-eventsource-onopen")]
    public Action<Event>? OnOpen
    {
        get => GetEventHandler("open");
        set => SetEventHandler("open", value);
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#handler-eventsource-onmessage"/>
    [IdlMember("onmessage", "https://html.spec.whatwg.org/multipage/server-sent-events.html#handler-eventsource-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#handler-eventsource-onerror"/>
    [IdlMember("onerror", "https://html.spec.whatwg.org/multipage/server-sent-events.html#handler-eventsource-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>
    /// Closes the connection (https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-close): the fetch
    /// is aborted, the events that were received and not yet dispatched are dropped, and the connection is not reestablished.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-close"/>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource-close")]
    public void Close()
    {
        AbortFetch();
        ReadyState = Closed;
        RemoveFromActiveEventSources();
    }

    /// <summary>
    /// The unloading document cleanup steps (https://html.spec.whatwg.org/multipage/document-lifecycle.html#unloading-document-cleanup-steps)
    /// and the end of a worker: forcibly closes the event sources of a global object.
    /// </summary>
    internal static void ForciblyClose(GlobalScope global)
    {
        if (!ActiveEventSources.TryGetValue(global, out var eventSources))
            return;

        ActiveEventSources.Remove(global);
        foreach (var eventSource in eventSources.ToArray())
        {
            eventSource.Close();
        }
    }

    /// <summary>The request of the event source (https://html.spec.whatwg.org/multipage/server-sent-events.html#concept-event-stream-request), for a connection attempt.</summary>
    private FetchRequest CreateRequest(Document client)
    {
        // https://html.spec.whatwg.org/multipage/urls-and-fetching.html#create-a-potential-cors-request: the mode is "cors"
        var request = new FetchRequest(_url.Clone())
        {
            Client = client,
            Origin = client.SerializeOrigin(),
            Mode = RequestMode.Cors,
            CredentialsMode = WithCredentials ? FetchCredentialsMode.Include : FetchCredentialsMode.SameOrigin,
            CacheMode = "no-store",
            StreamsBody = true,
        };

        request.HeaderList.Add(new("Accept", "text/event-stream"));

        // https://html.spec.whatwg.org/multipage/server-sent-events.html#reestablish-the-connection, step 5.3: the last event ID
        // string, encoded as UTF-8 (the values of the headers are byte sequences)
        if (_lastEventId.Length > 0)
        {
            request.HeaderList.Add(new("Last-Event-ID", FetchHeaders.IsomorphicDecode(Encoding.UTF8.GetBytes(_lastEventId))));
        }

        return request;
    }

    /// <summary>Fetches the request of the event source (https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource, step 14).</summary>
    private void StartFetch()
    {
        var global = _global!;
        var client = global.EnvironmentDocument;
        var connectionId = ++_connectionId;
        var request = CreateRequest(client);
        var controller = new CancellationTokenSource();
        _fetchController = controller;

        // The fetch is aborted with the document (https://fetch.spec.whatwg.org/#concept-fetch-group): the response is then an
        // aborted network error, which fails the connection
        _fetchGroupRecord = client.FetchGroup.Add(() =>
        {
            if (connectionId != _connectionId)
                return;

            AbortFetch();
            FailConnection();
        });

        // The response is processed by a task: the connection is never announced during the task that creates the event source
        var timing = ResourceTiming.Start(global, request, "other");
        var cancellationToken = controller.Token;
        _ = Fetching.StartFetch(request, cancellationToken).ContinueWith(
            completed => _eventLoop!.QueueTask(() => ProcessResponse(connectionId, request, Fetching.GetResult(completed), timing, cancellationToken)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>The processResponse steps of the fetch (https://html.spec.whatwg.org/multipage/server-sent-events.html#dom-eventsource, step 14).</summary>
    private void ProcessResponse(int connectionId, FetchRequest request, FetchResponse response, ResourceTiming? timing, CancellationToken cancellationToken)
    {
        // The fetch was aborted by close() or with the document
        if (connectionId != _connectionId || ReadyState == Closed)
            return;

        if (response.IsNetworkError)
        {
            timing?.Report(response);
            ReleaseFetch();

            // A network error is reestablished unless that is known to be futile: as in the browsers, only the failures of the
            // network are (the server cannot be reached); a request or a response that is blocked (the scheme of the URL, the
            // CORS check, a content security policy, mixed content, an invalid redirect...) fails the connection
            if (response.Aborted || !request.LoaderFailed)
            {
                FailConnection();
            }
            else
            {
                ReestablishConnection();
            }

            return;
        }

        var internalResponse = response.Internal;
        if (internalResponse.Status != 200 || HeaderList.ExtractMimeType(internalResponse.HeaderList)?.Essence != "text/event-stream")
        {
            timing?.Report(response);
            AbortFetch();
            FailConnection();
            return;
        }

        // The rest of a body that arrives over time is read before the body is copied
        var remainingBytes = internalResponse.Resource?.RemainingContentBytes;
        var body = internalResponse.Body ?? [];
        var origin = (internalResponse.Url ?? request.CurrentUrl).SerializeOrigin();
        AnnounceConnection();

        // https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation
        var parser = new EventStreamParser(
            _lastEventId,
            message => QueueRemoteEventTask(() =>
            {
                if (ReadyState != Closed)
                {
                    FireEvent(new MessageEvent(message.Type, new MessageEventInit { Data = message.Data, Origin = origin, LastEventId = message.LastEventId }));
                }
            }),
            reconnectionTime => _reconnectionTime = reconnectionTime);

        Write(parser, body);
        if (remainingBytes is null)
        {
            OnEndOfBody(connectionId, response, timing);
            return;
        }

        IAsyncEnumerator<ReadOnlyMemory<byte>> chunks;
        try
        {
            chunks = remainingBytes.GetAsyncEnumerator(cancellationToken);
        }
#pragma warning disable CA1031 // A failure of the response ends its body
        catch (Exception)
#pragma warning restore CA1031
        {
            OnEndOfBody(connectionId, response, timing);
            return;
        }

        ReadChunks(connectionId, parser, chunks, response, timing);
    }

    private void Write(EventStreamParser parser, ReadOnlySpan<byte> bytes)
    {
        parser.Write(bytes);

        // The last event ID string is set when an event is dispatched, including an event without data
        _lastEventId = parser.LastEventId;
    }

    /// <summary>
    /// Reads the rest of the body: the chunks that are available are interpreted at once, and the next one by a task queued
    /// when it arrives (from any thread).
    /// </summary>
    private void ReadChunks(int connectionId, EventStreamParser parser, IAsyncEnumerator<ReadOnlyMemory<byte>> chunks, FetchResponse response, ResourceTiming? timing)
    {
        while (true)
        {
            ValueTask<bool> next;
            try
            {
                next = chunks.MoveNextAsync();
            }
#pragma warning disable CA1031 // A failure of the response ends its body
            catch (Exception ex)
#pragma warning restore CA1031
            {
                next = ValueTask.FromException<bool>(ex);
            }

            if (!next.IsCompleted)
            {
                _ = next.AsTask().ContinueWith(
                    completed => _eventLoop!.QueueTask(() =>
                    {
                        if (OnChunkRead(connectionId, parser, chunks, completed.IsCompletedSuccessfully && completed.Result, response, timing))
                        {
                            ReadChunks(connectionId, parser, chunks, response, timing);
                        }
                    }),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return;
            }

            if (!OnChunkRead(connectionId, parser, chunks, next.IsCompletedSuccessfully && next.Result, response, timing))
                return;
        }
    }

    /// <summary>Interprets a chunk of the body. Returns false at the end of the body, or when the connection was closed.</summary>
    private bool OnChunkRead(int connectionId, EventStreamParser parser, IAsyncEnumerator<ReadOnlyMemory<byte>> chunks, bool hasChunk, FetchResponse response, ResourceTiming? timing)
    {
        if (connectionId == _connectionId && ReadyState != Closed && hasChunk)
        {
            Write(parser, chunks.Current.Span);
            return true;
        }

        _ = DisposeAsync(chunks);
        if (connectionId == _connectionId && ReadyState != Closed)
        {
            // The end of the body, or an error of the connection (which is reestablished as well)
            OnEndOfBody(connectionId, response, timing);
        }

        return false;

        static async Task DisposeAsync(IAsyncEnumerator<ReadOnlyMemory<byte>> chunks)
        {
            try
            {
                await chunks.DisposeAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The response is not read anymore
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    /// <summary>The processResponseEndOfBody steps of the fetch: the connection is reestablished.</summary>
    private void OnEndOfBody(int connectionId, FetchResponse response, ResourceTiming? timing)
    {
        if (connectionId != _connectionId)
            return;

        // https://fetch.spec.whatwg.org/#finalize-and-report-timing: the timing is reported at the end of the body
        timing?.Report(response);
        ReleaseFetch();
        ReestablishConnection();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/server-sent-events.html#announce-the-connection</summary>
    private void AnnounceConnection()
    {
        QueueRemoteEventTask(() =>
        {
            if (ReadyState != Closed)
            {
                ReadyState = Open;
                FireEvent(new Event("open"));
            }
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/server-sent-events.html#reestablish-the-connection</summary>
    private void ReestablishConnection()
    {
        var connectionId = _connectionId;
        QueueRemoteEventTask(() =>
        {
            if (ReadyState == Closed || connectionId != _connectionId)
                return;

            ReadyState = Connecting;
            FireEvent(new Event("error"));
            if (ReadyState != Connecting || connectionId != _connectionId)
                return;

            // The reconnection time is measured by the timers of the event loop
            _eventLoop!.QueueTimer(_reconnectionTime, () =>
            {
                if (ReadyState == Connecting && connectionId == _connectionId && _global is { CanRunScript: true })
                {
                    StartFetch();
                }
            });
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/server-sent-events.html#fail-the-connection</summary>
    private void FailConnection()
    {
        QueueRemoteEventTask(() =>
        {
            if (ReadyState != Closed)
            {
                ReadyState = Closed;
                RemoveFromActiveEventSources();
                FireEvent(new Event("error"));
            }
        });
    }

    /// <summary>Aborts the fetch of the current connection attempt, if any: its callbacks are ignored.</summary>
    private void AbortFetch()
    {
        _connectionId++;
        if (_fetchController is { } controller)
        {
            _fetchController = null;
            controller.Cancel();
            controller.Dispose();
        }

        _fetchGroupRecord?.Dispose();
        _fetchGroupRecord = null;
    }

    /// <summary>The fetch of the current connection attempt is done.</summary>
    private void ReleaseFetch()
    {
        _fetchController?.Dispose();
        _fetchController = null;
        _fetchGroupRecord?.Dispose();
        _fetchGroupRecord = null;
    }

    private void RemoveFromActiveEventSources()
    {
        if (_global is not null && ActiveEventSources.TryGetValue(_global, out var eventSources))
        {
            eventSources.Remove(this);
        }
    }

    /// <summary>
    /// Queues a task of the remote event task source (https://html.spec.whatwg.org/multipage/server-sent-events.html#remote-event-task-source),
    /// which runs only if the scripts of the relevant global object can run.
    /// </summary>
    private void QueueRemoteEventTask(Action task) => _global?.QueueGlobalTask(task);
}
