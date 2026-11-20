using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// WebSocketStream (https://whatpr.org/websockets/48.html#the-websocketstream-interface): the opened promise gives the
// ReadableStream and the WritableStream of Streams.js whose source and sink are the connection of the .NET object.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>Defines the <c>opened</c> attribute of WebSocketStream, which the generated bindings do not define.</summary>
    private void DefineWebSocketStreamMembers(TValue prototype)
    {
        var previousDefiningInterface = _definingInterface;
        _definingInterface = "WebSocketStream";
        try
        {
            // https://whatpr.org/websockets/48.html#dom-websocketstream-opened: the streams are created in the relevant realm
            // of the WebSocketStream when the promise is fulfilled (the same promise is returned each time)
            DefineAttribute(prototype, "opened", (realm, thisValue) =>
            {
                var stream = realm.Unwrap(thisValue) as WebSocketStream ?? throw realm.CreateTypeError("Illegal invocation: 'this' does not implement interface WebSocketStream");
                return realm.GetRelevantRealm(thisValue).ToPromise(stream.Opened, (relevantRealm, info) => relevantRealm.CreateWebSocketOpenInfo(stream, info));
            }, setter: null, returnsPromise: true);
        }
        finally
        {
            _definingInterface = previousDefiningInterface;
        }
    }

    /// <summary>
    /// Creates the WebSocketOpenInfo dictionary of the opened promise: the readable and the writable streams
    /// (createWebSocketStreams of Streams.js), the extensions and the subprotocol.
    /// </summary>
    private TValue CreateWebSocketOpenInfo(WebSocketStream stream, WebSocketOpenInfo info)
    {
        var adapter = Adapter;
        var source = CreateObject();
        SetMember(source, "pull", adapter.CreateFunction("pull", 0, (_, _) =>
        {
            stream.Pull();
            return adapter.Undefined;
        }));
        SetMember(source, "close", adapter.CreateFunction("close", 0, (_, _) => ToPromise(stream.CloseWritable())));
        var closeUsingReason = adapter.CreateFunction("closeUsingReason", 1, (_, args) =>
        {
            stream.CloseUsingReason(args.Length > 0 && adapter.IsObject(args[0]) ? Unwrap(args[0]) : null);
            return adapter.Undefined;
        });
        SetMember(source, "cancel", closeUsingReason);
        SetMember(source, "abort", closeUsingReason);

        // The data is a string (a text message) or a Uint8Array with a copy of the bytes of the chunk (a binary message)
        SetMember(source, "write", adapter.CreateFunction("write", 1, (_, args) =>
        {
            object data = adapter.IsString(args[0])
                ? ToUsvString(args[0])
                : ToBufferSource(args[0], 1u << (int)JsBufferKind.Uint8Array, allowShared: false, "Uint8Array").ToArray();
            return ToPromise(stream.Write(data));
        }));

        var controls = adapter.Call(adapter.Get(GetStreams(), Key("createWebSocketStreams")), adapter.Undefined, [source]);
        stream.AttachEndpoints(new WebSocketStreamEndpoints(this, controls));

        // https://webidl.spec.whatwg.org/#es-dictionary: the members are in lexicographical order
        var result = CreateObject();
        SetMember(result, "extensions", adapter.FromString(info.Extensions));
        SetMember(result, "protocol", adapter.FromString(info.Protocol));
        SetMember(result, "readable", adapter.Get(controls, Key("readable")));
        SetMember(result, "writable", adapter.Get(controls, Key("writable")));
        return result;
    }

    /// <summary>The streams of a WebSocketStream: the functions returned by createWebSocketStreams of Streams.js.</summary>
    private sealed class WebSocketStreamEndpoints(WebIdlRealm<TAdapter, TValue> realm, TValue controls) : IWebSocketStreamEndpoints
    {
        public void Enqueue(object message)
        {
            var chunk = message is string text ? realm.Adapter.FromString(text) : realm.FromBuffer(JsBufferKind.Uint8Array, (byte[])message);
            Call("enqueue", chunk);
        }

        public void Close(Exception writableError) => Call("close", realm.ToJsError(writableError));

        public void Error(Exception error) => Call("error", realm.ToJsError(error));

        private void Call(string name, TValue argument) => realm.Adapter.Call(realm.Adapter.Get(controls, realm.Key(name)), realm.Adapter.Undefined, [argument]);
    }
}
