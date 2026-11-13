using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Meziantou.Framework.WebPlatform.DevTools.Browser;

namespace Meziantou.Framework.WebPlatform.DevTools.Protocol;

/// <summary>
/// A connection of a client: it reads the commands, gives them to their session, and sends the responses and the events in
/// the order they are produced. Each connection has its own browser (its browser contexts, targets and sessions).
/// </summary>
internal sealed class CdpConnection : IDisposable
{
    private readonly Dictionary<string, CdpSession> _sessions = new(StringComparer.Ordinal);
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _closed = new();
    private readonly List<Task> _pendingCommands = [];
    private int _isClosed;

    public CdpConnection(DevToolsServerOptions options)
    {
        Options = options;
        Browser = new DevToolsBrowser(this);
        RootSession = new BrowserSession(this, Browser);
    }

    public DevToolsServerOptions Options { get; }

    public DevToolsBrowser Browser { get; }

    public BrowserSession RootSession { get; }

    /// <summary>Gets a token canceled when the connection is closed.</summary>
    public CancellationToken Closed => _closed.Token;

    public bool IsClosed => Volatile.Read(ref _isClosed) != 0;

    /// <summary>Creates an identifier as Chromium does for the targets, the browser contexts and the sessions (32 hexadecimal digits).</summary>
    public static string CreateId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public void AddSession(CdpSession session)
    {
        lock (_sessions)
        {
            _sessions.Add(session.Id!, session);
        }
    }

    public void RemoveSession(CdpSession session)
    {
        lock (_sessions)
        {
            _sessions.Remove(session.Id!);
        }

        session.MarkDetached();
    }

    public CdpSession? FindSession(string? sessionId)
    {
        if (sessionId is null)
            return RootSession;

        lock (_sessions)
        {
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

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

                Dispatch(buffer.WrittenMemory);
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

    /// <summary>Dispatches a message of the client: the commands run concurrently (e.g. an evaluation that awaits a promise).</summary>
    internal void Dispatch(ReadOnlyMemory<byte> utf8)
    {
        if (!CdpMessage.TryParse(utf8, out var message, out var id, out var errorCode, out var errorMessage))
        {
            SendError(id, sessionId: null, errorCode, errorMessage!);
            return;
        }

        var session = FindSession(message.SessionId);
        if (session is null)
        {
            SendError(message.Id, message.SessionId, ProtocolException.ServerError, "Session with given id not found.");
            return;
        }

        var task = DispatchAsync(session, message);
        lock (_pendingCommands)
        {
            _pendingCommands.RemoveAll(t => t.IsCompleted);
            _pendingCommands.Add(task);
        }
    }

    private static Task DispatchAsync(CdpSession session, CdpMessage message)
    {
        // The command starts synchronously: the commands of a session start in order
        return session.DispatchAsync(message);
    }

    /// <summary>Sends the response of a command: its result, or the error of its exception.</summary>
    public void Complete(CdpMessage message, JsonObject? result) => SendResponse(message.Id, message.SessionId, result);

    public void Fail(CdpMessage message, Exception exception)
    {
        switch (exception)
        {
            case ProtocolException protocolException:
                SendError(message.Id, message.SessionId, protocolException.Code, protocolException.Message);
                break;

            case OperationCanceledException when IsClosed:
                break;

            default:
                SendError(message.Id, message.SessionId, ProtocolException.ServerError, exception.Message);
                break;
        }
    }

    public void SendResponse(long id, string? sessionId, JsonObject? result)
    {
        var message = new JsonObject { ["id"] = id, ["result"] = result ?? [] };
        if (sessionId is not null)
        {
            message["sessionId"] = sessionId;
        }

        Send(message);
    }

    public void SendError(long? id, string? sessionId, int code, string errorMessage)
    {
        var message = new JsonObject();
        if (id is not null)
        {
            message["id"] = id.Value;
        }

        message["error"] = new JsonObject { ["code"] = code, ["message"] = errorMessage };
        if (sessionId is not null)
        {
            message["sessionId"] = sessionId;
        }

        Send(message);
    }

    public void SendEvent(string? sessionId, string method, JsonObject parameters)
    {
        var message = new JsonObject { ["method"] = method, ["params"] = parameters };
        if (sessionId is not null)
        {
            message["sessionId"] = sessionId;
        }

        Send(message);
    }

    private void Send(JsonObject message)
    {
        if (!IsClosed)
        {
            _outgoing.Writer.TryWrite(message.ToJsonString());
        }
    }

    /// <summary>Closes the connection: its browser contexts are closed.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            return;

        _outgoing.Writer.TryComplete();
        _closed.Cancel();
        Browser.Shutdown();
    }

    public void Dispose()
    {
        Close();
        _closed.Dispose();
    }
}
