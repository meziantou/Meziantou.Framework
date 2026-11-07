using Meziantou.Framework.WebPlatform.Compression;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Streams Standard (https://streams.spec.whatwg.org/), implemented in JavaScript (Streams.js): the promises and the
// microtasks of the algorithms are the ones of the engine. The script is evaluated in a realm when one of its interfaces
// is first used.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The interfaces defined by Streams.js, exposed on the global object.</summary>
    internal static readonly string[] StreamInterfaceNames =
    [
        "ReadableStream",
        "ReadableStreamDefaultReader",
        "ReadableStreamBYOBReader",
        "ReadableStreamDefaultController",
        "ReadableByteStreamController",
        "ReadableStreamBYOBRequest",
        "WritableStream",
        "WritableStreamDefaultWriter",
        "WritableStreamDefaultController",
        "TransformStream",
        "TransformStreamDefaultController",
        "ByteLengthQueuingStrategy",
        "CountQueuingStrategy",
        "TextEncoderStream",
        "TextDecoderStream",
        "CompressionStream",
        "DecompressionStream",
    ];

    private static string? s_streamsScript;
    private TValue? _streams;

    private void DefineStreamGlobals()
    {
        foreach (var name in StreamInterfaceNames)
        {
            if (!IsInterfaceEnabled(name))
                continue;

            Adapter.DefineLazyDataProperty(Global, Key(name), () => Adapter.Get(GetStreams(), Key(name)), writable: true, enumerable: false, configurable: true);
        }
    }

    /// <summary>Returns the object returned by Streams.js in this realm: the interface objects and the platform hooks.</summary>
    private TValue GetStreams()
    {
        if (_streams is { } streams)
            return streams;

        return Adapter.RunInRealm(EngineRealm, () =>
        {
            if (_streams is { } existing)
                return existing;

            var script = s_streamsScript ??= LoadStreamsScript();
            var adapter = Adapter;
            var interfaces = CreateObject();
            foreach (var name in (ReadOnlySpan<string>)["AbortController", "AbortSignal", "TextEncoder", "TextDecoder"])
            {
                Adapter.Set(interfaces, Key(name), GetInterfaceObject(name));
            }

            var hooks = CreateObject();
            Adapter.Set(hooks, Key("interfaces"), interfaces);
            Adapter.Set(hooks, Key("internals"), Agent.StreamInternals ??= EvaluateBuiltIn("new WeakMap()"));
            Adapter.Set(hooks, Key("SharedArrayBuffer"), SharedArrayBufferConstructor ?? Adapter.Undefined);

            // https://dom.spec.whatwg.org/#abortsignal-add: returns a function that removes the algorithm
            Adapter.Set(hooks, Key("addAbortAlgorithm"), adapter.CreateFunction("addAbortAlgorithm", 2, (_, args) =>
            {
                var signal = (AbortSignal)Unwrap(args[0])!;
                var callback = args[1];
                void Algorithm() => adapter.Call(callback, adapter.Undefined, []);
                signal.AddAbortAlgorithm(Algorithm);
                return adapter.CreateFunction("removeAbortAlgorithm", 0, (_, _) =>
                {
                    signal.RemoveAbortAlgorithm(Algorithm);
                    return adapter.Undefined;
                });
            }));
            Adapter.Set(hooks, Key("createCompressionContext"), adapter.CreateFunction("createCompressionContext", 2, (_, args) => CreateCompressionContext(CompressionFormats.Parse(Adapter.ToString(args[0]))!.Value, Adapter.ToBoolean(args[1]))));
            Adapter.Set(hooks, Key("structuredClone"), adapter.CreateFunction("structuredClone", 1, (_, args) => StructuredDeserialize(StructuredSerialize(args[0], transferList: null))));

            var result = Adapter.Call(EvaluateBuiltIn(script), Adapter.Undefined, [hooks]);
            _streams = result;
            return result;
        });
    }

    /// <summary>
    /// Creates the compression context of a CompressionStream or a DecompressionStream (https://compression.spec.whatwg.org/):
    /// an object whose transform(chunk) and flush() functions return the output as an array of Uint8Array objects, and
    /// whose hasTrailingInput() function tells whether the end of the compressed input has been reached before the end of
    /// the last chunk. The invalid chunks and the invalid compressed data are TypeErrors.
    /// </summary>
    private TValue CreateCompressionContext(CompressionFormat format, bool decompress)
    {
        var adapter = Adapter;
#pragma warning disable CA2000 // The contexts are disposed when their stream is flushed or the input is invalid; the native state of an abandoned stream is released by its finalizer
        var compressor = decompress ? null : new Compressor(format);
        var decompressor = decompress ? Decompressor.Create(format) : null;
#pragma warning restore CA2000
        var context = CreateObject();

        SetMember(context, "transform", adapter.CreateFunction("transform", 1, (_, args) =>
        {
            var chunk = ToBufferSource(args[0], BufferSourceKinds, allowShared: false, "BufferSource");
            return ToUint8Arrays(Process(() => compressor?.Compress(chunk.Bytes.Span) ?? decompressor!.Decompress(chunk.Bytes.Span)));
        }));
        SetMember(context, "flush", adapter.CreateFunction("flush", 0, (_, _) => ToUint8Arrays(Process(() => compressor?.Finish() ?? decompressor!.Finish()))));
        SetMember(context, "hasTrailingInput", adapter.CreateFunction("hasTrailingInput", 0, (_, _) => adapter.FromBoolean(decompressor is { HasTrailingInput: true })));
        return context;

        byte[] Process(Func<byte[]> action)
        {
            try
            {
                return action();
            }
            catch (InvalidDataException ex)
            {
                decompressor?.Dispose();
                throw CreateTypeError(ex.Message);
            }
        }

        // The output is split into chunks of 64 KiB at most
        TValue ToUint8Arrays(byte[] output)
        {
            const int ChunkSize = 65536;
            var chunks = new TValue[(output.Length + ChunkSize - 1) / ChunkSize];
            for (var i = 0; i < chunks.Length; i++)
            {
                chunks[i] = FromBuffer(JsBufferKind.Uint8Array, output.AsMemory(i * ChunkSize, Math.Min(ChunkSize, output.Length - (i * ChunkSize))));
            }

            return CreateArray(chunks);
        }
    }

    /// <summary>Whether <paramref name="value"/> is a ReadableStream (of any realm of the agent).</summary>
    internal bool IsReadableStream(TValue value)
    {
        // The streams are created by Streams.js, whose records are shared by the realms of the agent
        if (!Adapter.IsObject(value) || Agent.StreamInternals is null)
            return false;

        var getStream = Adapter.Get(Adapter.Get(GetStreams(), Key("fetchSupport")), Key("getStream"));
        return !Adapter.IsUndefined(Adapter.Call(getStream, Adapter.Undefined, [value]));
    }

    /// <summary>The kinds of the BufferSource type: all the buffer source types except SharedArrayBuffer.</summary>
    private const uint BufferSourceKinds = ((1u << ((int)JsBufferKind.Float64Array + 1)) - 1) & ~(1u << (int)JsBufferKind.SharedArrayBuffer);

    private static string LoadStreamsScript()
    {
        using var stream = typeof(WebIdlRealm<TAdapter, TValue>).Assembly.GetManifestResourceStream("Meziantou.Framework.WebPlatform.Scripting.WebIdl.Streams.js")
            ?? throw new InvalidOperationException("The Streams.js resource is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Defines the streaming HTML insertion methods proposed by https://github.com/whatwg/html/pull/12758 (e.g.
    /// <c>Element.prototype.streamAppendHTMLUnsafe()</c>): they return a WritableStream whose sink is the
    /// <see cref="HtmlStreamWriter"/> of the .NET method.
    /// </summary>
    private void DefineHtmlStreamingMethods(TValue prototype, string interfaceName)
    {
        switch (interfaceName)
        {
            case "Element":
                DefineContainerMethods(Generated.Binding_Element<TAdapter, TValue>.Binding,
                    (element, options) => ((Element)element).StreamHtml(options), (element, options) => ((Element)element).StreamHtmlUnsafe(options),
                    (element, options) => ((Element)element).StreamAppendHtml(options), (element, options) => ((Element)element).StreamAppendHtmlUnsafe(options),
                    (element, options) => ((Element)element).StreamPrependHtml(options), (element, options) => ((Element)element).StreamPrependHtmlUnsafe(options));
                DefineChildNodeMethods(Generated.Binding_Element<TAdapter, TValue>.Binding,
                    (element, options) => ((Element)element).StreamBeforeHtml(options), (element, options) => ((Element)element).StreamBeforeHtmlUnsafe(options),
                    (element, options) => ((Element)element).StreamAfterHtml(options), (element, options) => ((Element)element).StreamAfterHtmlUnsafe(options),
                    (element, options) => ((Element)element).StreamReplaceWithHtml(options), (element, options) => ((Element)element).StreamReplaceWithHtmlUnsafe(options));
                break;

            case "ShadowRoot":
                DefineContainerMethods(Generated.Binding_ShadowRoot<TAdapter, TValue>.Binding,
                    (root, options) => ((ShadowRoot)root).StreamHtml(options), (root, options) => ((ShadowRoot)root).StreamHtmlUnsafe(options),
                    (root, options) => ((ShadowRoot)root).StreamAppendHtml(options), (root, options) => ((ShadowRoot)root).StreamAppendHtmlUnsafe(options),
                    (root, options) => ((ShadowRoot)root).StreamPrependHtml(options), (root, options) => ((ShadowRoot)root).StreamPrependHtmlUnsafe(options));
                break;

            case "CharacterData":
                DefineChildNodeMethods(Generated.Binding_CharacterData<TAdapter, TValue>.Binding,
                    (node, options) => ((CharacterData)node).StreamBeforeHtml(options), (node, options) => ((CharacterData)node).StreamBeforeHtmlUnsafe(options),
                    (node, options) => ((CharacterData)node).StreamAfterHtml(options), (node, options) => ((CharacterData)node).StreamAfterHtmlUnsafe(options),
                    (node, options) => ((CharacterData)node).StreamReplaceWithHtml(options), (node, options) => ((CharacterData)node).StreamReplaceWithHtmlUnsafe(options));
                break;
        }

        void DefineContainerMethods(InterfaceBinding<TAdapter, TValue> binding, SafeMethod set, UnsafeMethod setUnsafe, SafeMethod append, UnsafeMethod appendUnsafe, SafeMethod prepend, UnsafeMethod prependUnsafe)
        {
            Define(binding, "streamHTML", set, ceReactions: true);
            Define(binding, "streamAppendHTML", append, ceReactions: false);
            Define(binding, "streamPrependHTML", prepend, ceReactions: false);
            Define(binding, "streamHTMLUnsafe", setUnsafe, ceReactions: true);
            Define(binding, "streamAppendHTMLUnsafe", appendUnsafe, ceReactions: false);
            Define(binding, "streamPrependHTMLUnsafe", prependUnsafe, ceReactions: false);
        }

        void DefineChildNodeMethods(InterfaceBinding<TAdapter, TValue> binding, SafeMethod before, UnsafeMethod beforeUnsafe, SafeMethod after, UnsafeMethod afterUnsafe, SafeMethod replaceWith, UnsafeMethod replaceWithUnsafe)
        {
            Define(binding, "streamBeforeHTML", before, ceReactions: false);
            Define(binding, "streamAfterHTML", after, ceReactions: false);
            Define(binding, "streamReplaceWithHTML", replaceWith, ceReactions: true);
            Define(binding, "streamBeforeHTMLUnsafe", beforeUnsafe, ceReactions: false);
            Define(binding, "streamAfterHTMLUnsafe", afterUnsafe, ceReactions: false);
            Define(binding, "streamReplaceWithHTMLUnsafe", replaceWithUnsafe, ceReactions: true);
        }

        void Define(InterfaceBinding<TAdapter, TValue> binding, string name, Delegate method, bool ceReactions)
        {
            DefineOperation(prototype, name, 0, (realm, thisValue, arguments) =>
            {
                var target = realm.This(thisValue, binding);
                var options = arguments.Length > 0 ? arguments[0] : realm.Adapter.Undefined;
                var writer = method switch
                {
                    SafeMethod safe => safe(target, Generated.WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_SetHtmlOptions(realm, options)),
                    UnsafeMethod unsafeMethod => unsafeMethod(target, ToSetHtmlUnsafeOptionsOrTrustedHtmlParserOptions(realm, options)),
                    _ => throw new InvalidOperationException("Unexpected method"),
                };

                return realm.CreateHtmlWritableStream(writer);
            }, ceReactions);
        }
    }

    private delegate HtmlStreamWriter SafeMethod(object target, SetHtmlOptions options);

    private delegate HtmlStreamWriter UnsafeMethod(object target, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions options);

    /// <summary>Converts the options of an unsafe streaming method to the (SetHTMLUnsafeOptions or TrustedHTMLParserOptions) union.</summary>
    private static SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions ToSetHtmlUnsafeOptionsOrTrustedHtmlParserOptions(WebIdlRealm<TAdapter, TValue> realm, TValue options)
    {
        if (realm.Adapter.IsObject(options) && realm.Unwrap(options) is TrustedHtmlParserOptions trustedOptions)
            return trustedOptions;

        return Generated.WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_SetHtmlUnsafeOptions(realm, options);
    }

    /// <summary>Creates the WritableStream whose sink is <paramref name="writer"/> (createHtmlWritableStream of Streams.js).</summary>
    private TValue CreateHtmlWritableStream(HtmlStreamWriter writer)
    {
        var adapter = Adapter;
        var sink = CreateObject();
        SetMember(sink, "write", adapter.CreateFunction("write", 1, (_, args) =>
        {
            var chunk = adapter.ToString(args[0]);
            return Invoke(() => writer.Write(chunk));
        }));
        SetMember(sink, "close", adapter.CreateFunction("close", 0, (_, _) => Invoke(writer.Close)));
        SetMember(sink, "abort", adapter.CreateFunction("abort", 0, (_, _) => Invoke(writer.Abort)));
        return adapter.Call(adapter.Get(GetStreams(), Key("createHtmlWritableStream")), adapter.Undefined, [sink]);

        // The scripts and the custom element reactions run by the parser may throw: the errors of the parser are DOMExceptions
        TValue Invoke(Action action)
        {
            var previousDocument = Document.CurrentGlobalDocumentOrNull;
            Document.CurrentGlobalDocument = GlobalScope.EnvironmentDocument;
            try
            {
                action();
                return adapter.Undefined;
            }
            catch (Exception ex) when (!adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }
            finally
            {
                Document.CurrentGlobalDocumentOrNull = previousDocument;
            }
        }
    }

    /// <summary>Defines Blob.prototype.stream() and Blob.prototype.textStream() (https://w3c.github.io/FileAPI/#blob-section).</summary>
    private void DefineBlobStreamMethods(TValue prototype)
    {
        var adapter = Adapter;
        foreach (var (name, factory) in (ReadOnlySpan<(string, string)>)[("stream", "createBlobStream"), ("textStream", "createBlobTextStream")])
        {
            var factoryName = factory;
            var operationName = name;
            var function = adapter.CreateFunction(operationName, 0, (thisValue, _) =>
            {
                if (Unwrap(adapter.IsUndefined(thisValue) || adapter.IsNull(thisValue) ? Global : thisValue) is not Blob blob)
                    throw CreateTypeError("Illegal invocation: 'this' does not implement interface Blob");

                var bytes = FromBuffer(JsBufferKind.Uint8Array, blob.Data);
                return adapter.Call(adapter.Get(GetStreams(), Key(factoryName)), adapter.Undefined, [bytes]);
            });
            DefineMethod(prototype, Key(operationName), function, enumerable: true);
        }
    }
}
