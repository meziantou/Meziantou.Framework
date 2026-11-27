using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The WebTransport handlers of <c>webtransport/handlers</c> ported to .NET: each port mirrors the functions of its file. The
/// path of the URL selects the handler, as the WebTransport server of wptserve does. wptserve executes the file of the handler
/// for each session: the global variables of a file (e.g. <c>streams_dict</c>) are the variables of a factory, so each
/// session has its own.
/// </summary>
internal static class WptWebTransportHandlers
{
    private const string Prefix = "/webtransport/handlers/";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Dictionary<string, Func<WptWebTransportHandler>> Handlers = new(StringComparer.Ordinal)
    {
        [Prefix + "abort-stream-from-server.py"] = AbortStreamFromServer,
        [Prefix + "anticipated-streams.py"] = AnticipatedStreams,
        [Prefix + "client-close.py"] = ClientClose,
        [Prefix + "custom-response.py"] = CustomResponse,
        [Prefix + "echo-request-headers.py"] = EchoRequestHeaders,
        [Prefix + "echo.py"] = Echo,
        [Prefix + "echo_datagram_length.py"] = EchoDatagramLength,
        [Prefix + "query.py"] = Query,
        [Prefix + "reset-bidi-stream-then-read.py"] = ResetBidiStreamThenRead,
        [Prefix + "sendorder.py"] = SendOrder,
        [Prefix + "server-close.py"] = ServerClose,
        [Prefix + "server-connection-close.py"] = ServerConnectionClose,
        [Prefix + "server-create-multiple-streams.py"] = ServerCreateMultipleStreams,
        [Prefix + "server-drain.py"] = ServerDrain,
        [Prefix + "server-read-then-close.py"] = ServerReadThenClose,
        [Prefix + "stop-sending-bidi-stream-then-send.py"] = StopSendingBidiStreamThenSend,
        [Prefix + "token-count.py"] = TokenCount,
    };

    /// <summary>Returns the factory of the handler of a path (<c>/webtransport/handlers/echo.py</c>), or null.</summary>
    public static Func<WptWebTransportHandler>? Get(string path) => Handlers.GetValueOrDefault(path);

    /// <summary>abort-stream-from-server.py: the server aborts the streams of the browser with the code of the query (an HTTP/3 error code).</summary>
    private static WptWebTransportHandler AbortStreamFromServer() => new()
    {
        SessionEstablished = session => session.DictForHandlers["code"] = long.Parse(session.QueryParameters["code"], CultureInfo.InvariantCulture),
        StreamDataReceived = (session, streamId, _, _) =>
        {
            var code = (long)session.DictForHandlers["code"]!;
            session.StopStream(streamId, code);
            if (!WptWebTransportSession.StreamIsUnidirectional(streamId))
            {
                session.ResetStream(streamId, code);
            }
        },
    };

    /// <summary>anticipated-streams.py: answers a JSON request with the number of streams the browser allows the server to open.</summary>
    private static WptWebTransportHandler AnticipatedStreams()
    {
        // buffered_data
        var bufferedData = new Dictionary<(long SessionId, long StreamId), List<byte>>();
        return new()
        {
            SessionEstablished = session => session.CreateBidirectionalStream(),
            StreamDataReceived = (session, streamId, data, streamEnded) =>
            {
                var key = (session.SessionId, streamId);
                if (!bufferedData.TryGetValue(key, out var buffer))
                {
                    buffer = [];
                    bufferedData[key] = buffer;
                }

                buffer.AddRange(data);
                if (!streamEnded)
                    return;

                bufferedData.Remove(key);
                JsonObject request;
                try
                {
                    // request.get(): the request must be an object
                    request = JsonNode.Parse(StrictUtf8.GetString([.. buffer])) as JsonObject ?? throw new InvalidOperationException("The request is not a JSON object");
                }
                catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
                {
                    var error = new JsonObject { ["error"] = "Invalid JSON: " + ex.Message };
                    session.SendStreamData(streamId, Encoding.UTF8.GetBytes(error.ToJsonString()), endStream: true);
                    return;
                }

                var response = new JsonObject
                {
                    ["uni_created"] = session.RemoteMaxStreamsUni,
                    ["bidi_created"] = session.RemoteMaxStreamsBidi,
                    ["uni_requested"] = request.TryGetPropertyValue("uni", out var uni) ? uni?.DeepClone() : 0,
                    ["bidi_requested"] = request.TryGetPropertyValue("bidi", out var bidi) ? bidi?.DeepClone() : 0,
                };
                session.SendStreamData(streamId, Encoding.UTF8.GetBytes(response.ToJsonString()), endStream: true);
            },
        };
    }

    /// <summary>client-close.py: records in the stash how the browser closes its streams and the session.</summary>
    private static WptWebTransportHandler ClientClose() => new()
    {
        SessionEstablished = session =>
        {
            session.DictForHandlers["token"] = session.QueryParameters["token"];
            session.CreateBidirectionalStream();
        },
        StreamReset = (session, _, errorCode) =>
        {
            var token = (string)session.DictForHandlers["token"]!;
            var data = TakeDictionary(session, token);
            data["stream-close-info"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = "reset",
                ["code"] = errorCode,
            };
            session.StashPut(token, data);
        },
        StreamDataReceived = (session, _, _, streamEnded) =>
        {
            if (!streamEnded)
                return;

            var token = (string)session.DictForHandlers["token"]!;
            var data = TakeDictionary(session, token);
            data["stream-close-info"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = "FIN",
            };
            session.StashPut(token, data);
        },
        SessionClosed = (session, closeInfo, abruptly) =>
        {
            var token = (string)session.DictForHandlers["token"]!;
            var data = TakeDictionary(session, token);
            data["session-close-info"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["abruptly"] = abruptly,
                ["close_info"] = closeInfo is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = closeInfo.CloseCode,
                    ["reason"] = closeInfo.Reason,
                },
            };
            session.StashPut(token, data);
        },
    };

    /// <summary>custom-response.py: the parameters of the query are added to the response (e.g. <c>:status=404</c>).</summary>
    private static WptWebTransportHandler CustomResponse() => new()
    {
        ConnectReceived = (requestHeaders, responseHeaders) =>
        {
            foreach (var (name, value) in requestHeaders)
            {
                if (name == ":path")
                {
                    foreach (var (key, parameter) in WptWebTransportSession.ParseQuery(WptWebTransportSession.GetQuery(value)))
                    {
                        responseHeaders.Add(new(key, parameter));
                    }

                    break;
                }
            }
        },
    };

    /// <summary>echo-request-headers.py: sends the headers of the request as a JSON object on a unidirectional stream (the values of a name in a list with <c>format=list</c>).</summary>
    private static WptWebTransportHandler EchoRequestHeaders() => new()
    {
        SessionEstablished = session =>
        {
            var asList = session.QueryParameters.TryGetValue("format", out var format) && format == "list";
            var headers = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (name, value) in session.RequestHeaders)
            {
                if (asList)
                {
                    if (!headers.TryGetValue(name, out var values))
                    {
                        values = new List<string>();
                        headers[name] = values;
                    }

                    ((List<string>)values).Add(value);
                }
                else
                {
                    headers[name] = value;
                }
            }

            var streamId = session.CreateUnidirectionalStream();
            session.SendStreamData(streamId, JsonSerializer.SerializeToUtf8Bytes(headers), endStream: true);
        },
    };

    /// <summary>echo.py: echoes the data of a bidirectional stream on the stream, of a unidirectional stream on a new unidirectional stream, and the datagrams.</summary>
    private static WptWebTransportHandler Echo()
    {
        // streams_dict
        var streams = new Dictionary<(long SessionId, long StreamId), long>();
        return new()
        {
            SessionEstablished = session => session.CreateBidirectionalStream(),
            StreamDataReceived = (session, streamId, data, streamEnded) =>
            {
                if (WptWebTransportSession.StreamIsUnidirectional(streamId))
                {
                    var key = (session.SessionId, streamId);
                    if (!streams.TryGetValue(key, out var newStreamId))
                    {
                        newStreamId = session.CreateUnidirectionalStream();
                        streams[key] = newStreamId;
                    }

                    session.SendStreamData(newStreamId, data, streamEnded);
                    if (streamEnded)
                    {
                        streams.Remove(key);
                    }

                    return;
                }

                session.SendStreamData(streamId, data, streamEnded);
            },
            SessionClosed = (session, _, _) =>
            {
                foreach (var key in streams.Keys.Where(key => key.SessionId == session.SessionId).ToArray())
                {
                    streams.Remove(key);
                }
            },
            DatagramReceived = (session, data) => session.SendDatagram(data),
        };
    }

    /// <summary>echo_datagram_length.py: answers a datagram with its length, in JSON.</summary>
    private static WptWebTransportHandler EchoDatagramLength() => new()
    {
        DatagramReceived = (session, data) => session.SendDatagram(Encoding.UTF8.GetBytes("{\"length\": " + data.Length.ToString(CultureInfo.InvariantCulture) + "}")),
    };

    /// <summary>query.py: sends the value of the stash of the token (in JSON, <c>{}</c> when there is none) on a unidirectional stream.</summary>
    private static WptWebTransportHandler Query() => new()
    {
        SessionEstablished = session =>
        {
            var token = session.QueryParameters["token"];
            var streamId = session.CreateUnidirectionalStream();

            // stash.take(key=token) or {}
            var value = session.StashTake(token);
            if (value is null or System.Collections.ICollection { Count: 0 })
            {
                value = new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            session.SendStreamData(streamId, JsonSerializer.SerializeToUtf8Bytes(value), endStream: true);
        },
    };

    /// <summary>reset-bidi-stream-then-read.py: resets the stream when it receives its first data, then records in the stash the number of bytes it received.</summary>
    private static WptWebTransportHandler ResetBidiStreamThenRead() => new()
    {
        SessionEstablished = session =>
        {
            var query = session.QueryParameters;
            session.DictForHandlers["token"] = query["token"];
            session.DictForHandlers["code"] = long.Parse(query["code"], CultureInfo.InvariantCulture);
            session.DictForHandlers["received"] = 0L;
        },
        StreamDataReceived = (session, streamId, data, streamEnded) =>
        {
            var received = (long)session.DictForHandlers["received"]!;
            if (received == 0)
            {
                session.ResetStream(streamId, (long)session.DictForHandlers["code"]!);
            }

            received += data.Length;
            session.DictForHandlers["received"] = received;
            if (streamEnded)
            {
                var token = (string)session.DictForHandlers["token"]!;
                var stashedData = TakeDictionary(session, token);
                stashedData["stream-close-info"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["source"] = "FIN",
                    ["received"] = received,
                };
                session.StashPut(token, stashedData);
            }
        },
        StreamReset = (session, _, errorCode) =>
        {
            var token = (string)session.DictForHandlers["token"]!;
            var stashedData = TakeDictionary(session, token);
            stashedData["stream-close-info"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = "reset",
                ["code"] = errorCode,
            };
            session.StashPut(token, stashedData);
        },
    };

    /// <summary>sendorder.py: records in the stash the first byte of each chunk of data it receives (<c>summary</c>), and echoes the datagrams.</summary>
    private static WptWebTransportHandler SendOrder()
    {
        // summary (the stash keeps a copy of the list)
        var summary = new List<int>();
        return new()
        {
            SessionEstablished = session =>
            {
                var token = session.QueryParameters["token"];
                session.DictForHandlers["token"] = token;
                session.StashPut(token, summary.ToList());
            },
            StreamDataReceived = (session, _, data, _) =>
            {
                if (data.Length > 0)
                {
                    summary.Add(data[0]);
                }

                var token = (string)session.DictForHandlers["token"]!;
                session.StashTake(token);
                session.StashPut(token, summary.ToList());
            },
            StreamReset = (session, _, _) =>
            {
                var token = (string)session.DictForHandlers["token"]!;
                session.StashPut(token, summary.ToList());
                summary = [];
            },
            DatagramReceived = (session, data) => session.SendDatagram(data),
        };
    }

    /// <summary>server-close.py: closes the session once it is established, with the code and the reason of the query when there is a code.</summary>
    private static WptWebTransportHandler ServerClose() => new()
    {
        SessionEstablished = session =>
        {
            var query = session.QueryParameters;
            var reason = query.TryGetValue("reason", out var value) ? value : "";
            session.Close(query.TryGetValue("code", out var code) ? new(uint.Parse(code, CultureInfo.InvariantCulture), reason) : null);
        },
    };

    /// <summary>server-connection-close.py: closes the QUIC connection when it receives data.</summary>
    private static WptWebTransportHandler ServerConnectionClose() => new()
    {
        SessionEstablished = session => session.CreateBidirectionalStream(),
        StreamDataReceived = (session, _, _, _) => session.CloseConnection(),
    };

    /// <summary>server-create-multiple-streams.py: opens <c>count</c> streams of the <c>type</c> of the query, then a <c>marker</c> unidirectional stream when <c>marker=1</c>.</summary>
    private static WptWebTransportHandler ServerCreateMultipleStreams() => new()
    {
        SessionEstablished = session =>
        {
            var query = session.QueryParameters;
            var streamType = query.TryGetValue("type", out var type) ? type : "bidi";
            var count = int.Parse(query.TryGetValue("count", out var value) ? value : "3", CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                var streamId = streamType == "unidi" ? session.CreateUnidirectionalStream() : session.CreateBidirectionalStream();
                session.SendStreamData(streamId, Encoding.UTF8.GetBytes("stream" + i.ToString(CultureInfo.InvariantCulture)), endStream: true);
            }

            if (query.TryGetValue("marker", out var marker) && marker == "1")
            {
                var streamId = session.CreateUnidirectionalStream();
                session.SendStreamData(streamId, "marker"u8.ToArray(), endStream: true);
            }
        },
    };

    /// <summary>server-drain.py: asks the browser to end the session (DRAIN_WEBTRANSPORT_SESSION) when the query contains <c>drain</c>.</summary>
    private static WptWebTransportHandler ServerDrain() => new()
    {
        SessionEstablished = session =>
        {
            if (session.Query.Contains("drain", StringComparison.Ordinal))
            {
                session.InitiateDraining();
            }
        },
    };

    /// <summary>server-read-then-close.py: closes the session (without capsule) when it receives data.</summary>
    private static WptWebTransportHandler ServerReadThenClose() => new()
    {
        SessionEstablished = session => session.CreateBidirectionalStream(),
        StreamDataReceived = (session, _, _, _) => session.Close(closeInfo: null),
    };

    /// <summary>stop-sending-bidi-stream-then-send.py: sends STOP_SENDING when it receives the first data of a stream, then sends <c>abc</c> and the end of the stream.</summary>
    private static WptWebTransportHandler StopSendingBidiStreamThenSend() => new()
    {
        SessionEstablished = session =>
        {
            session.DictForHandlers["code"] = long.Parse(session.QueryParameters["code"], CultureInfo.InvariantCulture);
            session.DictForHandlers["stopped"] = false;
        },
        StreamDataReceived = (session, streamId, _, _) =>
        {
            if ((bool)session.DictForHandlers["stopped"]!)
                return;

            session.DictForHandlers["stopped"] = true;
            session.StopStream(streamId, (long)session.DictForHandlers["code"]!);
            session.SendStreamData(streamId, "abc"u8.ToArray(), endStream: true);
        },
    };

    /// <summary>token-count.py: counts in the stash the number of times a token (the data of a chunk) is received, and answers with the count.</summary>
    private static WptWebTransportHandler TokenCount()
    {
        // streams_dict
        var streams = new Dictionary<(long SessionId, long StreamId), long>();
        return new()
        {
            SessionEstablished = session => session.CreateBidirectionalStream(),
            StreamDataReceived = (session, streamId, data, streamEnded) =>
            {
                // The keys of the stash are UUIDs (stash.take raises a ValueError for other keys)
                var token = Encoding.UTF8.GetString(data);
                if (!Guid.TryParse(token, out _))
                    throw new FormatException("badly formed hexadecimal UUID string: " + token);

                var count = (session.StashTake(token) as long? ?? 0) + 1;
                session.StashPut(token, count);
                var response = Encoding.UTF8.GetBytes(count.ToString(CultureInfo.InvariantCulture));
                if (WptWebTransportSession.StreamIsUnidirectional(streamId))
                {
                    var key = (session.SessionId, streamId);
                    if (!streams.TryGetValue(key, out var newStreamId))
                    {
                        newStreamId = session.CreateUnidirectionalStream();
                        streams[key] = newStreamId;
                    }

                    session.SendStreamData(newStreamId, response);
                    if (streamEnded)
                    {
                        streams.Remove(key);
                    }

                    return;
                }

                session.SendStreamData(streamId, response);
            },
        };
    }

    /// <summary><c>session.stash.take(key=token) or {}</c>: the dictionary of the stash of a token.</summary>
    private static Dictionary<string, object?> TakeDictionary(WptWebTransportSession session, string token)
        => session.StashTake(token) as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.Ordinal);
}
