using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// A WebTransport handler of wptserve (a <c>webtransport/handlers/*.py</c> file): the callbacks that the server calls, in the
/// order of the events of the session (https://github.com/web-platform-tests/wpt/blob/master/tools/webtransport/h3/webtransport_h3_server.py,
/// <c>WebTransportEventHandler</c>). A callback that is not defined is not called. The error codes are the HTTP/3 error codes.
/// </summary>
internal sealed class WptWebTransportHandler
{
    /// <summary>
    /// <c>connect_received(request_headers, response_headers)</c>: called with the headers of the CONNECT request, before the
    /// response is sent. The handler may add headers to the response, including <c>:status</c>.
    /// </summary>
    public Action<IReadOnlyList<KeyValuePair<string, string>>, List<KeyValuePair<string, string>>>? ConnectReceived { get; init; }

    /// <summary><c>session_established(session)</c>: called once the response is sent, when its status is <c>200</c>.</summary>
    public Action<WptWebTransportSession>? SessionEstablished { get; init; }

    /// <summary><c>stream_data_received(session, stream_id, data, stream_ended)</c></summary>
    public Action<WptWebTransportSession, long, byte[], bool>? StreamDataReceived { get; init; }

    /// <summary><c>stream_reset(session, stream_id, error_code)</c>: the browser sent RESET_STREAM.</summary>
    public Action<WptWebTransportSession, long, long>? StreamReset { get; init; }

    /// <summary><c>datagram_received(session, data)</c></summary>
    public Action<WptWebTransportSession, byte[]>? DatagramReceived { get; init; }

    /// <summary>
    /// <c>session_closed(session, close_info, abruptly)</c>: the browser closed the session with a code and a reason, or it
    /// ended abruptly (<c>close_info</c> is <c>None</c>). Called once.
    /// </summary>
    public Action<WptWebTransportSession, WebTransportConnectionCloseInfo?, bool>? SessionClosed { get; init; }
}
