using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Scripting;

// WebTransport (https://w3c.github.io/webtransport/), implemented in JavaScript (Streams.js) because its streams are
// ReadableStream and WritableStream objects. The session is established by the DocumentLoader of the browsing context
// (WebTransportHostSession): the hooks of this file give the script the session and its streams as objects whose methods
// call the library, and the library calls the callbacks of the script from tasks of the event loop.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The interfaces of WebTransport defined by Streams.js, exposed in the secure contexts of the windows and the workers.</summary>
    internal static readonly string[] WebTransportInterfaceNames =
    [
        "WebTransport",
        "WebTransportBidirectionalStream",
        "WebTransportDatagramDuplexStream",
        "WebTransportDatagramsWritable",
        "WebTransportReceiveStream",
        "WebTransportSendGroup",
        "WebTransportSendStream",
        "WebTransportWriter",
    ];

    private void DefineWebTransportGlobals()
    {
        foreach (var name in WebTransportInterfaceNames)
        {
            var interfaceName = name;
            Adapter.DefineLazyDataProperty(Global, Key(interfaceName), () => Adapter.Get(GetStreams(), Key(interfaceName)), writable: true, enumerable: false, configurable: true);
        }
    }

    /// <summary>The hooks of WebTransport given to Streams.js (hooks.webTransport).</summary>
    private TValue CreateWebTransportHooks()
    {
        var adapter = Adapter;
        var hooks = CreateObject();
        void Define(string name, int length, JsHostFunction<TValue> function) => SetMember(hooks, name, adapter.CreateFunction(name, length, function));

        Define("toUsvString", 1, (_, args) => adapter.FromString(ToUsvString(Arg(args, 0))));

        // The URL is parsed without base URL (https://w3c.github.io/webtransport/#dom-webtransport-webtransport, step 2)
        Define("parseUrl", 1, (_, args) =>
        {
            if (UrlParser.Parse(adapter.ToString(args[0]), baseUrl: null) is not { } url)
                return adapter.Null;

            var result = CreateObject();
            SetMember(result, "href", adapter.FromString(url.Serialize(excludeFragment: true)));
            SetMember(result, "scheme", adapter.FromString(url.Scheme));
            SetMember(result, "hasFragment", adapter.FromBoolean(url.Fragment is not null));
            return result;
        });

        // The headers option (HeadersInit): the names are lowercase (HTTP/3), the forbidden request headers are dropped
        // (https://fetch.spec.whatwg.org/#forbidden-request-header), and the values are normalized
        Define("normalizeHeaders", 1, (_, args) =>
        {
            var init = Arg(args, 0);
            if (adapter.IsUndefined(init))
                return CreateArray([]);

            var headers = (Headers)Unwrap(adapter.Construct(GetInterfaceObject("Headers"), [init], GetInterfaceObject("Headers")))!;
            var pairs = new List<TValue>();
            foreach (var (name, value) in headers.List)
            {
                // The protocols are given by the protocols option (https://w3c.github.io/webtransport/#dom-webtransportoptions-headers)
                if (name.Equals("wt-available-protocols", StringComparison.OrdinalIgnoreCase))
                    throw CreateTypeError("The wt-available-protocols header is set by the protocols option");

                if (FetchHeaders.IsForbiddenRequestHeader(name, value))
                    continue;

                pairs.Add(CreateArray([adapter.FromString(name.ToLowerInvariant()), adapter.FromString(value)]));
            }

            return CreateArray(pairs);
        });

        Define("domException", 2, (_, args) => ToJsError(new DomException(adapter.ToString(args[0]), adapter.ToString(args[1]))));
        Define("webTransportError", 3, (_, args) =>
        {
            var source = adapter.ToString(args[1]) == "session" ? WebTransportErrorSource.Session : WebTransportErrorSource.Stream;
            uint? code = adapter.IsNull(args[2]) || adapter.IsUndefined(args[2]) ? null : (uint)adapter.ToNumber(args[2]);
            return ToJsError(new WebTransportError(adapter.ToString(args[0]), new WebTransportErrorOptions { Source = source, StreamErrorCode = code }));
        });
        Define("isWebTransportError", 1, (_, args) => adapter.FromBoolean(Unwrap(args[0]) is WebTransportError));
        Define("streamErrorCodeOf", 1, (_, args) => Unwrap(args[0]) is WebTransportError { StreamErrorCode: { } code } ? adapter.FromNumber(code) : adapter.Null);

        // https://w3c.github.io/webtransport/#dom-webtransport-close, step 6: the longest prefix of the reason whose UTF-8
        // encoding is at most 1024 bytes
        Define("truncateReason", 1, (_, args) =>
        {
            var reason = adapter.ToString(args[0]);
            if (Encoding.UTF8.GetByteCount(reason) <= 1024)
                return adapter.FromString(reason);

            var length = 0;
            var byteCount = 0;
            foreach (var rune in reason.EnumerateRunes())
            {
                byteCount += rune.Utf8SequenceLength;
                if (byteCount > 1024)
                    break;

                length += rune.Utf16SequenceLength;
            }

            return adapter.FromString(reason[..length]);
        });

        Define("queueTask", 1, (_, args) =>
        {
            var callback = args[0];
            GlobalScope.QueueGlobalTask(() => adapter.Call(callback, adapter.Undefined, []));
            return adapter.Undefined;
        });

        Define("connect", 2, (_, args) => ConnectWebTransport(args[0], args[1]));
        return hooks;
    }

    private static TValue Arg(ReadOnlySpan<TValue> args, int index) => index < args.Length ? args[index] : default(TAdapter).Undefined;

    /// <summary>Starts to establish the session of a WebTransport object; returns the object whose methods use the session.</summary>
    private TValue ConnectWebTransport(TValue requestValue, TValue events)
    {
        var adapter = Adapter;
        TValue Member(string name) => adapter.Get(requestValue, Key(name));
        int? OptionalInt(string name) => adapter.Get(requestValue, Key(name)) is var value && (adapter.IsNull(value) || adapter.IsUndefined(value)) ? null : (int)adapter.ToNumber(value);

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var pair in ToSequence(Member("headers"), static (realm, value) => realm.ToSequence(value, static (r, v) => r.Adapter.ToString(v))))
        {
            headers.Add(new(pair[0], pair[1]));
        }

        var hashes = new List<ReadOnlyMemory<byte>>();
        var usesCertificateHashes = false;
        foreach (var hash in ToSequence(Member("serverCertificateHashes"), static (realm, value) => (Algorithm: realm.Adapter.ToString(realm.Adapter.Get(value, realm.Key("algorithm"))), Value: realm.ToBufferSource(realm.Adapter.Get(value, realm.Key("value")), BufferSourceKinds, allowShared: false, "BufferSource").Bytes.ToArray())))
        {
            // https://w3c.github.io/webtransport/#verify-a-certificate-hash: only the SHA-256 hashes are used, but a list
            // without any still requires a match
            usesCertificateHashes = true;
            if (string.Equals(hash.Algorithm, "sha-256", StringComparison.OrdinalIgnoreCase))
            {
                hashes.Add(hash.Value);
            }
        }

        var request = new WebTransportConnectionRequest
        {
            Url = adapter.ToString(Member("url")),
            Headers = headers,
            Protocols = ToSequence(Member("protocols"), static (realm, value) => realm.Adapter.ToString(value)),
            ServerCertificateHashes = hashes,
            CongestionControl = adapter.ToString(Member("congestionControl")),
            AllowPooling = adapter.ToBoolean(Member("allowPooling")),
            RequireUnreliable = adapter.ToBoolean(Member("requireUnreliable")),
            AnticipatedConcurrentIncomingUnidirectionalStreams = OptionalInt("anticipatedUni"),
            AnticipatedConcurrentIncomingBidirectionalStreams = OptionalInt("anticipatedBidi"),
        };

        var sessionEvents = new WebTransportSessionEvents(this, events);
        var session = WebTransportHostSession.Connect(GlobalScope, request, usesCertificateHashes, sessionEvents);
        var handle = CreateWebTransportSessionHandle(session);
        sessionEvents.Handle = handle;
        return handle;
    }

    /// <summary>The object of the script whose methods use the session.</summary>
    private TValue CreateWebTransportSessionHandle(WebTransportHostSession session)
    {
        var adapter = Adapter;
        var handle = CreateObject();
        SetMember(handle, "abort", adapter.CreateFunction("abort", 0, (_, _) =>
        {
            session.Abort();
            return adapter.Undefined;
        }));
        SetMember(handle, "close", adapter.CreateFunction("close", 2, (_, args) =>
        {
            session.Close((uint)adapter.ToNumber(args[0]), adapter.ToString(args[1]));
            return adapter.Undefined;
        }));
        SetMember(handle, "maxDatagramSize", adapter.CreateFunction("maxDatagramSize", 0, (_, _) => adapter.FromNumber(session.MaxDatagramSize)));
        SetMember(handle, "openStream", adapter.CreateFunction("openStream", 3, (_, args) =>
        {
            var callback = args[2];
            session.OpenStream(adapter.ToBoolean(args[0]), adapter.ToBoolean(args[1]), (stream, error) =>
            {
                var streamHandle = stream is null ? adapter.Null : CreateWebTransportStreamHandle(stream);
                adapter.Call(callback, adapter.Undefined, [streamHandle, error is null ? adapter.Null : adapter.FromString(error)]);
            });
            return adapter.Undefined;
        }));
        SetMember(handle, "sendDatagram", adapter.CreateFunction("sendDatagram", 2, (_, args) =>
        {
            var bytes = ToBufferSource(args[0], BufferSourceKinds, allowShared: false, "BufferSource").Bytes.ToArray();
            var callback = args[1];
            session.SendDatagram(bytes, sent => adapter.Call(callback, adapter.Undefined, [adapter.FromBoolean(sent)]));
            return adapter.Undefined;
        }));
        SetMember(handle, "getStats", adapter.CreateFunction("getStats", 1, (_, args) =>
        {
            var callback = args[0];
            session.GetStatistics(statistics => adapter.Call(callback, adapter.Undefined, [statistics is null ? adapter.Null : CreateWebTransportConnectionStats(statistics)]));
            return adapter.Undefined;
        }));
        return handle;
    }

    // https://w3c.github.io/webtransport/#dictdef-webtransportconnectionstats (the datagrams member is added by the script)
    private TValue CreateWebTransportConnectionStats(WebTransportConnectionStatistics statistics)
    {
        var adapter = Adapter;
        var result = CreateObject();
        SetMember(result, "bytesSent", adapter.FromNumber(statistics.BytesSent));
        SetMember(result, "bytesSentOverhead", adapter.FromNumber(statistics.BytesSentOverhead));
        if (statistics.BytesAcknowledged is { } bytesAcknowledged)
        {
            SetMember(result, "bytesAcknowledged", adapter.FromNumber(bytesAcknowledged));
        }

        SetMember(result, "packetsSent", adapter.FromNumber(statistics.PacketsSent));
        SetMember(result, "bytesLost", adapter.FromNumber(statistics.BytesLost));
        SetMember(result, "packetsLost", adapter.FromNumber(statistics.PacketsLost));
        SetMember(result, "bytesReceived", adapter.FromNumber(statistics.BytesReceived));
        SetMember(result, "packetsReceived", adapter.FromNumber(statistics.PacketsReceived));
        SetMember(result, "smoothedRtt", adapter.FromNumber(statistics.SmoothedRtt.TotalMilliseconds));
        SetMember(result, "rttVariation", adapter.FromNumber(statistics.RttVariation.TotalMilliseconds));
        SetMember(result, "minRtt", adapter.FromNumber(statistics.MinRtt.TotalMilliseconds));
        SetMember(result, "estimatedSendRate", statistics.EstimatedSendRate is { } rate ? adapter.FromNumber(rate) : adapter.Null);
        return result;
    }

    /// <summary>The object of the script whose methods use a stream of the session.</summary>
    private TValue CreateWebTransportStreamHandle(WebTransportHostStream stream)
    {
        var adapter = Adapter;
        var handle = CreateObject();
        SetMember(handle, "read", adapter.CreateFunction("read", 1, (_, args) =>
        {
            var callback = args[0];
            stream.Read(result =>
            {
                TValue chunk;
                TValue error;
                if (result.IsReset)
                {
                    chunk = adapter.Null;
                    error = ToJsError(new WebTransportError("The server reset the stream", new WebTransportErrorOptions { Source = WebTransportErrorSource.Stream, StreamErrorCode = result.ErrorCode }));
                }
                else if (result.IsFailure)
                {
                    chunk = adapter.Null;
                    error = ToJsError(new WebTransportError("The stream failed", new WebTransportErrorOptions { Source = WebTransportErrorSource.Stream }));
                }
                else
                {
                    chunk = result.Chunk is { } bytes ? FromBuffer(JsBufferKind.Uint8Array, bytes) : adapter.Null;
                    error = adapter.Null;
                }

                adapter.Call(callback, adapter.Undefined, [chunk, error]);
            });
            return adapter.Undefined;
        }));
        SetMember(handle, "write", adapter.CreateFunction("write", 2, (_, args) =>
        {
            var bytes = ToBufferSource(args[0], BufferSourceKinds, allowShared: false, "BufferSource").Bytes.ToArray();
            var callback = args[1];
            stream.Write(bytes, endOfStream: false, result => adapter.Call(callback, adapter.Undefined, [ToWriteError(result)]));
            return adapter.Undefined;
        }));
        SetMember(handle, "finish", adapter.CreateFunction("finish", 1, (_, args) =>
        {
            var callback = args[0];
            stream.Write(ReadOnlyMemory<byte>.Empty, endOfStream: true, result => adapter.Call(callback, adapter.Undefined, [ToWriteError(result)]));
            return adapter.Undefined;
        }));
        SetMember(handle, "abortWrite", adapter.CreateFunction("abortWrite", 1, (_, args) =>
        {
            stream.AbortWrite((uint)adapter.ToNumber(args[0]));
            return adapter.Undefined;
        }));
        SetMember(handle, "abortRead", adapter.CreateFunction("abortRead", 1, (_, args) =>
        {
            stream.AbortRead((uint)adapter.ToNumber(args[0]));
            return adapter.Undefined;
        }));
        SetMember(handle, "setSendOrder", adapter.CreateFunction("setSendOrder", 1, (_, args) =>
        {
            stream.SetSendOrder((long)adapter.ToNumber(args[0]));
            return adapter.Undefined;
        }));
        SetMember(handle, "onStopSending", adapter.CreateFunction("onStopSending", 1, (_, args) =>
        {
            var callback = args[0];
            stream.OnStopSending(code => adapter.Call(callback, adapter.Undefined, [code is { } value ? adapter.FromNumber(value) : adapter.Null]));
            return adapter.Undefined;
        }));
        return handle;

        TValue ToWriteError(WebTransportHostStream.WriteResult result)
        {
            if (result.IsSuccess)
                return adapter.Null;

            return result.IsStopSending
                ? ToJsError(new WebTransportError("The server stopped reading the stream", new WebTransportErrorOptions { Source = WebTransportErrorSource.Stream, StreamErrorCode = result.ErrorCode }))
                : ToJsError(new WebTransportError("The stream failed", new WebTransportErrorOptions { Source = WebTransportErrorSource.Stream }));
        }
    }

    /// <summary>The events of a session, given to the callbacks of the script (createSessionEvents of Streams.js).</summary>
    private sealed class WebTransportSessionEvents(WebIdlRealm<TAdapter, TValue> realm, TValue events) : IWebTransportSessionEvents
    {
        public TValue? Handle { get; set; }

        public void Ready(string protocol, IReadOnlyList<KeyValuePair<string, string>> responseHeaders)
        {
            // https://w3c.github.io/webtransport/#dom-webtransport-responseheaders: an immutable Headers object, without
            // wt-protocol and the forbidden response header names
            var list = new List<KeyValuePair<string, string>>();
            foreach (var (name, value) in responseHeaders)
            {
                if (name.Equals("wt-protocol", StringComparison.OrdinalIgnoreCase) || FetchHeaders.IsForbiddenResponseHeaderName(name))
                    continue;

                list.Add(new(name.ToLowerInvariant(), value));
            }

            var headers = realm.Wrap(new Headers(list, HeadersGuard.Immutable));
            Call("ready", Handle!, realm.Adapter.FromString(protocol), headers);
        }

        public void Failed(string message) => Call("failed", realm.Adapter.FromString(message));

        public void Closed(uint closeCode, string reason) => Call("closed", realm.Adapter.FromNumber(closeCode), realm.Adapter.FromString(reason));

        public void Draining() => Call("draining");

        public void IncomingStream(WebTransportHostStream stream) => Call("incomingStream", realm.CreateWebTransportStreamHandle(stream), realm.Adapter.FromBoolean(stream.IsBidirectional));

        public void Datagram(ReadOnlyMemory<byte> datagram) => Call("datagram", realm.FromBuffer(JsBufferKind.Uint8Array, datagram.ToArray()));

        private void Call(string name, params ReadOnlySpan<TValue> arguments) => realm.Adapter.Call(realm.Adapter.Get(events, realm.Key(name)), realm.Adapter.Undefined, arguments);
    }
}
