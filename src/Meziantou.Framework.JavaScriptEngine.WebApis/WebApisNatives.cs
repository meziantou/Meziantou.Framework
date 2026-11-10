using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.JavaScriptEngine.WebApis;

/// <summary>
/// The .NET functions called by the bindings (<see cref="WebApisScript"/>). The arguments are already converted by the
/// bindings (e.g. strings), and the objects of the APIs (a URL, a decoder...) are handles: objects without properties whose
/// target is the .NET state.
/// </summary>
internal static class WebApisNatives
{
    // The internal slots in the order of the "slots" object of the structured clone script
    private static readonly JsInternalSlot[] StructuredCloneSlots =
    [
        JsInternalSlot.BooleanData,
        JsInternalSlot.NumberData,
        JsInternalSlot.BigIntData,
        JsInternalSlot.StringData,
        JsInternalSlot.SymbolData,
        JsInternalSlot.DateValue,
        JsInternalSlot.ArrayBufferData,
        JsInternalSlot.SharedArrayBufferData,
        JsInternalSlot.DataView,
        JsInternalSlot.MapData,
        JsInternalSlot.SetData,
        JsInternalSlot.WeakMapData,
        JsInternalSlot.WeakSetData,
        JsInternalSlot.RegExpMatcher,
        JsInternalSlot.ErrorData,
    ];

    public static JsObject Create(JsWebApiHost host)
    {
        var realm = host.Realm;
        var engine = realm.Engine;
        var natives = realm.CreateObject(prototype: null);

        void Define(string name, int length, JsNativeFunction function) => natives.DefineProperty(name, JsPropertyDescriptor.Data(realm.CreateFunction(name, length, function), writable: false, enumerable: false, configurable: false));

        // Base64
        Define("btoa", 1, (_, args) => ToNullableString(ForgivingBase64.Btoa(Arg(args, 0).AsString())));
        Define("atob", 1, (_, args) => ToNullableString(ForgivingBase64.Atob(Arg(args, 0).AsString())));

        // Microtasks and timers
        Define("queueMicrotask", 1, (_, args) =>
        {
            var callback = Arg(args, 0);
            engine.EnqueueJob(() => engine.Call(callback, JsValue.Undefined), realm);
            return JsValue.Undefined;
        });
        Define("setTimer", 4, (_, args) =>
        {
            var arguments = Arg(args, 2).TryGetObject() is JsArray array ? array.ToArray() : [];
            return host.SetTimer(Arg(args, 0), Arg(args, 1), arguments, JsEngine.ToBoolean(Arg(args, 3)));
        });
        Define("clearTimer", 1, (_, args) =>
        {
            host.ClearTimer(Arg(args, 0));
            return JsValue.Undefined;
        });

        // Console
        Define("print", 4, (_, args) =>
        {
            var level = Arg(args, 1).AsString() switch
            {
                "info" => JsConsoleMessageLevel.Info,
                "warn" => JsConsoleMessageLevel.Warning,
                "error" => JsConsoleMessageLevel.Error,
                "debug" => JsConsoleMessageLevel.Debug,
                _ => JsConsoleMessageLevel.Log,
            };
            var message = new JsConsoleMessage(Arg(args, 0).AsString(), level, Arg(args, 2).AsString(), (int)Arg(args, 3).AsNumber());
            if (host.Options.ConsoleMessage is { } handler)
            {
                handler(message);
            }
            else
            {
                WriteToConsole(message);
            }

            return JsValue.Undefined;
        });
        Define("now", 0, (_, _) =>
        {
            var timeProvider = engine.Options.TimeProvider;
            return timeProvider.GetTimestamp() * 1000.0 / timeProvider.TimestampFrequency;
        });
        Define("stackTrace", 0, (_, _) => RemoveInternalFrames(engine.CaptureStackTrace()));

        // The kind of an object, read from its internal slots: the console shows an object without running code of the
        // script (no getter, no proxy trap, no Symbol.toStringTag accessor)
        Define("kindOf", 1, (_, args) => Arg(args, 0).TryGetObject() is { } obj ? GetKind(engine, obj) : "primitive");
        Define("primitiveValue", 1, (_, args) => Arg(args, 0).TryGetObject() is { } obj && obj.TryGetPrimitiveValue(out var value) ? value : JsValue.Undefined);
        Define("promiseState", 1, (_, args) =>
        {
            if (Arg(args, 0).TryGetObject() is not { } obj || !obj.TryGetPromiseState(out var state, out var result, out bool _))
                return JsValue.Undefined;

            var name = state switch
            {
                JsPromiseState.Fulfilled => "fulfilled",
                JsPromiseState.Rejected => "rejected",
                _ => "pending",
            };
            return realm.CreateArray([name, result]);
        });

        // Structured clone
        Define("isBufferSource", 1, (_, args) => JsBinaryData.TryGetBufferSource(Arg(args, 0), out JsBufferKind _, out bool _, out Memory<byte> _));
        Define("hasInternalSlot", 2, (_, args) =>
        {
            var slot = (int)Arg(args, 1).AsNumber();
            return Arg(args, 0).TryGetObject() is { } obj && (uint)slot < (uint)StructuredCloneSlots.Length && obj.HasInternalSlot(StructuredCloneSlots[slot]);
        });
        Define("setErrorStack", 2, (_, args) => Arg(args, 0).TryGetObject() is { } obj && obj.TrySetErrorStack(Arg(args, 1).AsString()));
        Define("shareBuffer", 1, (_, args) =>
        {
            var block = JsBinaryData.GetSharedDataBlock(Arg(args, 0)) ?? throw realm.CreateException(JsErrorType.TypeError, "The value is not a SharedArrayBuffer");
            return CreateHandle(block);
        });
        Define("receiveBuffer", 3, (_, args) => realm.CreateSharedArrayBuffer(GetHandle<JsSharedDataBlock>(Arg(args, 0))));

        // URL
        Define("urlParse", 2, (_, args) =>
        {
            var baseUrl = Arg(args, 1);
            var url = UrlApiObject.ApiParse(ToUsvString(Arg(args, 0)), baseUrl.IsUndefined ? null : ToUsvString(baseUrl));
            return url is null ? JsValue.Null : CreateHandle(new UrlApiObject(url));
        });
        Define("urlGet", 2, (_, args) =>
        {
            var url = GetHandle<UrlApiObject>(Arg(args, 0));
            return Arg(args, 1).AsString() switch
            {
                "href" => url.Href,
                "origin" => url.Origin,
                "protocol" => url.Protocol,
                "username" => url.Username,
                "password" => url.Password,
                "host" => url.Host,
                "hostname" => url.Hostname,
                "port" => url.Port,
                "pathname" => url.Pathname,
                "search" => url.Search,
                "hash" => url.Hash,
                var name => throw new ArgumentOutOfRangeException(nameof(args), name, "Unknown URL property"),
            };
        });
        Define("urlSet", 3, (_, args) =>
        {
            var url = GetHandle<UrlApiObject>(Arg(args, 0));
            var value = ToUsvString(Arg(args, 2));
            switch (Arg(args, 1).AsString())
            {
                case "href":
                    return url.TrySetHref(value);
                case "protocol":
                    url.Protocol = value;
                    break;
                case "username":
                    url.Username = value;
                    break;
                case "password":
                    url.Password = value;
                    break;
                case "host":
                    url.Host = value;
                    break;
                case "hostname":
                    url.Hostname = value;
                    break;
                case "port":
                    url.Port = value;
                    break;
                case "pathname":
                    url.Pathname = value;
                    break;
                case "search":
                    url.Search = value;
                    break;
                case "hash":
                    url.Hash = value;
                    break;
                case var name:
                    throw new ArgumentOutOfRangeException(nameof(args), name, "Unknown URL property");
            }

            return true;
        });
        Define("urlSearchParams", 1, (_, args) => CreateHandle(GetHandle<UrlApiObject>(Arg(args, 0)).SearchParams));

        // URLSearchParams
        Define("searchParamsCreate", 1, (_, args) => CreateHandle(UrlApiSearchParams.FromQuery(Arg(args, 0).AsString())));
        Define("searchParamsAppendInitial", 3, (_, args) =>
        {
            GetHandle<UrlApiSearchParams>(Arg(args, 0)).AppendInitial(Arg(args, 1).AsString(), Arg(args, 2).AsString());
            return JsValue.Undefined;
        });
        Define("searchParamsSize", 1, (_, args) => GetHandle<UrlApiSearchParams>(Arg(args, 0)).Count);
        Define("searchParamsAppend", 3, (_, args) =>
        {
            GetHandle<UrlApiSearchParams>(Arg(args, 0)).Append(Arg(args, 1).AsString(), Arg(args, 2).AsString());
            return JsValue.Undefined;
        });
        Define("searchParamsDelete", 3, (_, args) =>
        {
            GetHandle<UrlApiSearchParams>(Arg(args, 0)).Delete(Arg(args, 1).AsString(), ToNullableString(Arg(args, 2)));
            return JsValue.Undefined;
        });
        Define("searchParamsGet", 2, (_, args) => ToNullableString(GetHandle<UrlApiSearchParams>(Arg(args, 0)).Get(Arg(args, 1).AsString())));
        Define("searchParamsGetAll", 2, (_, args) =>
        {
            var values = GetHandle<UrlApiSearchParams>(Arg(args, 0)).GetAll(Arg(args, 1).AsString());
            var items = new JsValue[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                items[i] = values[i];
            }

            return realm.CreateArray(items);
        });
        Define("searchParamsHas", 3, (_, args) => GetHandle<UrlApiSearchParams>(Arg(args, 0)).Has(Arg(args, 1).AsString(), ToNullableString(Arg(args, 2))));
        Define("searchParamsSet", 3, (_, args) =>
        {
            GetHandle<UrlApiSearchParams>(Arg(args, 0)).Set(Arg(args, 1).AsString(), Arg(args, 2).AsString());
            return JsValue.Undefined;
        });
        Define("searchParamsSort", 1, (_, args) =>
        {
            GetHandle<UrlApiSearchParams>(Arg(args, 0)).Sort();
            return JsValue.Undefined;
        });
        Define("searchParamsEntry", 2, (_, args) =>
        {
            var list = GetHandle<UrlApiSearchParams>(Arg(args, 0));
            var index = Arg(args, 1).AsNumber();
            if (index < 0 || index >= list.Count)
                return JsValue.Undefined;

            var entry = list[(int)index];
            return realm.CreateArray([entry.Key, entry.Value]);
        });
        Define("searchParamsToString", 1, (_, args) => GetHandle<UrlApiSearchParams>(Arg(args, 0)).ToString());

        // TextEncoder and TextDecoder
        Define("encode", 1, (_, args) =>
        {
            var bytes = TextEncoderCore.Encode(Arg(args, 0).AsString());
            var buffer = realm.CreateArrayBuffer(bytes);
            return realm.CreateBufferView(JsBufferKind.Uint8Array, buffer, 0, bytes.Length);
        });
        Define("encodeInto", 2, (_, args) =>
        {
            if (!JsBinaryData.TryGetBufferSource(Arg(args, 1), out JsBufferKind _, out bool _, out var destination))
                throw realm.CreateException(JsErrorType.TypeError, "The destination is not a Uint8Array");

            TextEncoderCore.EncodeInto(Arg(args, 0).AsString(), destination.Span, out var read, out var written);
            var result = realm.CreateObject();
            result.SetProperty("read", (double)read);
            result.SetProperty("written", (double)written);
            return result;
        });
        Define("decoderCreate", 3, (_, args) =>
        {
            var decoder = TextDecoderCore.TryCreate(Arg(args, 0).AsString(), JsEngine.ToBoolean(Arg(args, 1)), JsEngine.ToBoolean(Arg(args, 2)));
            return decoder is null ? JsValue.Null : CreateHandle(decoder);
        });
        Define("decoderGet", 2, (_, args) =>
        {
            var decoder = GetHandle<TextDecoderCore>(Arg(args, 0));
            return Arg(args, 1).AsString() switch
            {
                "encoding" => decoder.Encoding,
                "fatal" => decoder.Fatal,
                "ignoreBOM" => decoder.IgnoreBom,
                var name => throw new ArgumentOutOfRangeException(nameof(args), name, "Unknown TextDecoder property"),
            };
        });
        Define("decode", 3, (_, args) =>
        {
            var decoder = GetHandle<TextDecoderCore>(Arg(args, 0));
            var input = Arg(args, 1);
            Memory<byte> bytes = default;
            if (!input.IsUndefined && !JsBinaryData.TryGetBufferSource(input, out JsBufferKind _, out bool _, out bytes))
                throw realm.CreateException(JsErrorType.TypeError, "The input is not a buffer source");

            return decoder.TryDecode(bytes.Span, JsEngine.ToBoolean(Arg(args, 2)), out var result) ? result : JsValue.Null;
        });

        return natives;
    }

    private static string GetKind(JsEngine engine, JsObject obj)
    {
        if (obj.HasInternalSlot(JsInternalSlot.ProxyHandler))
            return "proxy";

        if (obj.HasInternalSlot(JsInternalSlot.ErrorData))
            return "error";

        if (obj.HasInternalSlot(JsInternalSlot.DateValue))
            return "date";

        if (obj.HasInternalSlot(JsInternalSlot.RegExpMatcher))
            return "regexp";

        if (obj.HasInternalSlot(JsInternalSlot.MapData))
            return "map";

        if (obj.HasInternalSlot(JsInternalSlot.SetData))
            return "set";

        if (obj.HasInternalSlot(JsInternalSlot.WeakMapData))
            return "weakmap";

        if (obj.HasInternalSlot(JsInternalSlot.WeakSetData))
            return "weakset";

        if (obj.HasInternalSlot(JsInternalSlot.TypedArrayName))
            return "typedarray";

        if (obj.HasInternalSlot(JsInternalSlot.ArrayBufferData))
            return "arraybuffer";

        if (obj.HasInternalSlot(JsInternalSlot.SharedArrayBufferData))
            return "sharedarraybuffer";

        if (obj.HasInternalSlot(JsInternalSlot.DataView))
            return "dataview";

        if (obj.HasInternalSlot(JsInternalSlot.PromiseState))
            return "promise";

        if (obj.HasInternalSlot(JsInternalSlot.BooleanData))
            return "Boolean";

        if (obj.HasInternalSlot(JsInternalSlot.NumberData))
            return "Number";

        if (obj.HasInternalSlot(JsInternalSlot.StringData))
            return "String";

        if (obj.HasInternalSlot(JsInternalSlot.SymbolData))
            return "Symbol";

        if (obj.HasInternalSlot(JsInternalSlot.BigIntData))
            return "BigInt";

        if (obj.IsCallable)
            return "function";

        // Not a proxy: IsArray runs no code of the script
        return engine.IsArray(obj) ? "array" : "object";
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => index < arguments.Length ? arguments[index] : JsValue.Undefined;

    private static JsValue ToNullableString(string? value) => value is null ? JsValue.Null : value;

    private static string? ToNullableString(JsValue value) => value.IsUndefined ? null : value.AsString();

    /// <summary>The conversion to the WebIDL <c>USVString</c> type: the lone surrogates are replaced by U+FFFD.</summary>
    private static string ToUsvString(JsValue value) => InfraString.ToScalarValueString(value.AsString());

    private static JsHostObject CreateHandle(object target) => new(prototype: null, target);

    private static T GetHandle<T>(JsValue value)
        where T : class
    {
        return (T)((JsHostObject)value.AsObject()).Target!;
    }

    /// <summary>Removes the frames of the bindings from a stack trace (e.g. the frame of <c>console.trace</c>).</summary>
    private static string RemoveInternalFrames(string stackTrace)
    {
        var lines = stackTrace.Split('\n');
        return string.Join('\n', lines.Where(line => !line.Contains(JsWebApis.ScriptUrl, StringComparison.Ordinal)));
    }

    private static void WriteToConsole(JsConsoleMessage message)
    {
        var writer = message.Level is JsConsoleMessageLevel.Warning or JsConsoleMessageLevel.Error ? Console.Error : Console.Out;
        var text = message.Text;
        if (message.GroupDepth > 0)
        {
            var indentation = new string(' ', message.GroupDepth * 2);
            text = indentation + text.Replace("\n", "\n" + indentation, StringComparison.Ordinal);
        }

        writer.WriteLine(text);
    }
}
