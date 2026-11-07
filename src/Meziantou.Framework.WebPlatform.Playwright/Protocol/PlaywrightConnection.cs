using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

namespace Meziantou.Framework.WebPlatform.Playwright.Protocol;

/// <summary>
/// A connection of a Playwright client (the <c>DispatcherConnection</c> of the Playwright server): the objects of the client,
/// the dispatch of the calls of the client to them, and the messages sent to the client (responses, events, creations,
/// adoptions and disposals of objects), in order.
/// </summary>
internal sealed class PlaywrightConnection : IDisposable
{
    private readonly Dictionary<string, Dispatcher> _dispatchers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Progress> _calls = new(StringComparer.Ordinal);
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _closed = new();
    private readonly List<Task> _pendingCalls = [];
    private long _nextGuid;
    private int _isClosed;

    public PlaywrightConnection(PlaywrightServerOptions options)
    {
        Options = options;
        Root = new RootDispatcher(this);
    }

    public PlaywrightServerOptions Options { get; }

    /// <summary>Gets the name of the browser requested by the client (the <c>x-playwright-browser</c> header), or null.</summary>
    public string? RequestedBrowserName { get; init; }

    public RootDispatcher Root { get; }

    /// <summary>Gets the lock of the objects: their creation, disposal and messages are sent in order.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>Gets a token canceled when the connection is closed.</summary>
    public CancellationToken Closed => _closed.Token;

    public string CreateGuid(string prefix) => prefix + "@" + Interlocked.Increment(ref _nextGuid).ToString("x", CultureInfo.InvariantCulture);

    // The handles that the client does not dispose are disposed when there are too many of them (the "gc" of the dispatchers
    // of the Playwright server): the oldest tenth is disposed once there are more than MaxHandles
    private const int MaxHandles = 100_000;
    private readonly Queue<Dispatcher> _handles = new();

    internal void Register(Dispatcher dispatcher)
    {
        Dispatcher[]? staleHandles = null;
        lock (SyncRoot)
        {
            _dispatchers.Add(dispatcher.Guid, dispatcher);
            if (dispatcher is JSHandleDispatcher)
            {
                _handles.Enqueue(dispatcher);
                if (_handles.Count > MaxHandles)
                {
                    staleHandles = new Dispatcher[MaxHandles / 10];
                    for (var i = 0; i < staleHandles.Length; i++)
                    {
                        staleHandles[i] = _handles.Dequeue();
                    }
                }
            }
        }

        if (staleHandles is not null)
        {
            foreach (var handle in staleHandles)
            {
                handle.Dispose("gc");
            }
        }
    }

    internal void Unregister(Dispatcher dispatcher)
    {
        lock (SyncRoot)
        {
            _dispatchers.Remove(dispatcher.Guid);
        }
    }

    public Dispatcher? Find(string guid)
    {
        lock (SyncRoot)
        {
            return _dispatchers.GetValueOrDefault(guid);
        }
    }

    /// <summary>Returns the object referenced by a parameter (<c>{ "guid": ... }</c>).</summary>
    public T GetDispatcher<T>(JsonElement parameters, string name)
        where T : Dispatcher
    {
        var guid = parameters.GetGuid(name) ?? throw new PlaywrightError($"{name}: expected guid");
        return GetDispatcher<T>(guid);
    }

    public T GetDispatcher<T>(string guid)
        where T : Dispatcher
    {
        var dispatcher = Find(guid) ?? throw new PlaywrightError($"no object with guid {guid}");
        return dispatcher as T ?? throw new PlaywrightError($"object with guid {guid} has type {dispatcher.Type}");
    }

    internal void SendCreate(Dispatcher parent, string type, string guid, JsonObject initializer)
        => Send(new JsonObject { ["guid"] = parent.Guid, ["method"] = "__create__", ["params"] = new JsonObject { ["type"] = type, ["initializer"] = initializer, ["guid"] = guid } });

    internal void SendEvent(Dispatcher dispatcher, string method, JsonObject parameters)
        => Send(new JsonObject { ["guid"] = dispatcher.Guid, ["method"] = method, ["params"] = parameters });

    internal void SendAdopt(Dispatcher parent, Dispatcher child)
        => Send(new JsonObject { ["guid"] = parent.Guid, ["method"] = "__adopt__", ["params"] = new JsonObject { ["guid"] = child.Guid } });

    internal void SendDispose(Dispatcher dispatcher, string? reason)
    {
        var parameters = new JsonObject();
        if (reason is not null)
        {
            parameters["reason"] = reason;
        }

        Send(new JsonObject { ["guid"] = dispatcher.Guid, ["method"] = "__dispose__", ["params"] = parameters });
    }

    private void Send(JsonObject message)
    {
        if (!_closed.IsCancellationRequested)
        {
            _outgoing.Writer.TryWrite(message.ToJsonString());
        }
    }

    /// <summary>Runs the connection on a WebSocket, until it is closed by the client or <paramref name="cancellationToken"/> is canceled.</summary>
    public async Task RunAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), _closed);
        var writer = WriteMessagesAsync(webSocket);
        try
        {
            await ReadMessagesAsync(webSocket).ConfigureAwait(false);
        }
        finally
        {
            Close();
            await writer.ConfigureAwait(false);
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private async Task ReadMessagesAsync(WebSocket webSocket)
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            while (!_closed.IsCancellationRequested)
            {
                buffer.Clear();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(buffer.GetMemory(16 * 1024), _closed.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    buffer.Advance(result.Count);
                }
                while (!result.EndOfMessage);

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(buffer.WrittenMemory);
                }
                catch (JsonException)
                {
                    return;
                }

                Dispatch(document);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task WriteMessagesAsync(WebSocket webSocket)
    {
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            Close();
        }
    }

    /// <summary>Dispatches a message of the client: the calls run concurrently (a call can wait, e.g. for a selector).</summary>
    internal void Dispatch(JsonDocument document)
    {
        var task = DispatchAsync(document);
        lock (_pendingCalls)
        {
            _pendingCalls.RemoveAll(t => t.IsCompleted);
            _pendingCalls.Add(task);
        }
    }

    private async Task DispatchAsync(JsonDocument document)
    {
        using (document)
        {
            var message = document.RootElement;
            var id = message.TryGetProperty("id", out var idElement) ? idElement.Clone() : default;
            var guid = message.GetString("guid") ?? "";
            var method = message.GetString("method") ?? "";
            var parameters = message.TryGetProperty("params", out var parametersElement) ? parametersElement.Clone() : default;
            if (method == "__waitInfo__")
                return;

            if (method == "__abort__")
            {
                if (parameters.TryGet("id", out var abortedId) && _calls.TryGetValue(abortedId.GetRawText(), out var abortedCall))
                {
                    abortedCall.Abort(PlaywrightError.Aborted(parameters.GetString("reason")));
                }

                return;
            }

            var timeout = message.TryGet("metadata", out var metadata) ? metadata.GetDouble("timeout") ?? 0 : 0;
            var callKey = id.ValueKind == JsonValueKind.Undefined ? "" : id.GetRawText();
            using var progress = new Progress(timeout, _closed.Token);
            _calls[callKey] = progress;
            try
            {
                var dispatcher = Find(guid) ?? throw PlaywrightError.TargetClosed();
                var result = await dispatcher.InvokeAsync(method, parameters, progress).ConfigureAwait(false);
                var response = new JsonObject { ["id"] = ToNode(id) };
                if (result is not null)
                {
                    response["result"] = result;
                }

                Send(response);
            }
            catch (Exception ex)
            {
                var error = ToPlaywrightError(ex, progress);
                var response = new JsonObject { ["id"] = ToNode(id), ["error"] = SerializeError(error) };
                if (error.Details is not null)
                {
                    response["errorDetails"] = error.Details;
                }

                var log = progress.Log;
                if (log.Count > 0)
                {
                    var array = new JsonArray();
                    foreach (var line in log)
                    {
                        array.Add((JsonNode)line);
                    }

                    response["log"] = array;
                }

                Send(response);
            }
            finally
            {
                _calls.TryRemove(callKey, out _);
            }
        }
    }

    private static JsonValue? ToNode(JsonElement id) => id.ValueKind == JsonValueKind.Undefined ? null : JsonValue.Create(id);

    internal static PlaywrightError ToPlaywrightError(Exception exception, Progress progress)
    {
        return exception switch
        {
            PlaywrightError error => error,
            Dispatchers.ExpectError expectError => new PlaywrightError(expectError.Message) { Details = expectError.Details },
            OperationCanceledException when progress.IsCanceled => progress.GetCancellationError(),
            AggregateException { InnerExceptions: [var inner] } => ToPlaywrightError(inner, progress),
            _ => new PlaywrightError(exception.Message, exception),
        };
    }

    internal static JsonObject SerializeError(PlaywrightError error)
    {
        if (error.Value is not null)
            return new JsonObject { ["value"] = error.Value };

        return new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = error.Message,
                ["name"] = error.ErrorName,
                ["stack"] = error.ErrorStack ?? (error.ErrorName + ": " + error.Message),
            },
        };
    }

    /// <summary>Closes the connection: the pending calls are canceled and the objects are disposed (their browser contexts are closed).</summary>
    public void Close()
    {
        // Not _closed.IsCancellationRequested: the cancellation token of RunAsync cancels _closed without closing the
        // connection, and the writer of the messages would then wait forever for the end of the queue
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            return;

        _closed.Cancel();
        _outgoing.Writer.TryComplete();
        Root.DisposeSilently();
    }

    public void Dispose()
    {
        Close();
        _closed.Dispose();
    }
}
