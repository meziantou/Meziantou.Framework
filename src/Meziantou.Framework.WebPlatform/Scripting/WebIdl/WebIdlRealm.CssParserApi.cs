using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The CSS Parser API (https://wicg.github.io/css-parser-api/#parsing-api): the operations whose input is a CSSStringSource,
// (DOMString or ReadableStream), parse the text of a stream. The streams are implemented in JavaScript (Streams.js), so the
// bindings read the bytes of the stream, decode them, and call the generated operation with the text.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private TValue? _parseCssStream;

    /// <summary>Wraps the operations of the CSS namespace that take a CSSStringSource, so that they accept a ReadableStream.</summary>
    private void DefineCssParserStreamSources(TValue namespaceObject)
    {
        foreach (var name in (ReadOnlySpan<string>)["parseStylesheet", "parseRuleList", "parseRule", "parseDeclarationList"])
        {
            if (!IsMemberEnabled("CSS", name))
                continue;

            // The generated operation, which parses the strings
            var operation = Adapter.Get(namespaceObject, Key(name));
            if (!Adapter.IsCallable(operation))
                continue;

            var decodeAsStyleSheet = name == "parseStylesheet";
            DefineOperation(namespaceObject, name, 1, (realm, thisValue, arguments) =>
            {
                if (arguments.Length > 0 && realm.IsReadableStream(arguments[0]))
                    return realm.ParseCssStream(operation, arguments[0], arguments.Length > 1 ? arguments[1] : realm.Adapter.Undefined, decodeAsStyleSheet);

                return realm.Adapter.Call(operation, thisValue, arguments);
            }, returnsPromise: true);
        }
    }

    /// <summary>
    /// Reads all the bytes of a stream (https://streams.spec.whatwg.org/#readablestream-read-all-bytes: the chunks must be
    /// Uint8Array objects, and a locked stream is a TypeError), decodes them, then parses the text with the operation. The
    /// bytes of a style sheet are decoded as CSS Syntax does (https://drafts.csswg.org/css-syntax-3/#input-byte-stream: the
    /// byte order mark, then the @charset rule, then UTF-8); the draft does not define the decoding of the other inputs, which
    /// are decoded as UTF-8 (https://encoding.spec.whatwg.org/#utf-8-decode).
    /// </summary>
    private TValue ParseCssStream(TValue operation, TValue stream, TValue options, bool decodeAsStyleSheet)
    {
        _parseCssStream ??= EvaluateBuiltIn("""
            (function (operation, stream, options, readAllBytes, decode) {
              return new Promise((resolve, reject) => {
                readAllBytes(stream, (bytes) => {
                  try {
                    resolve(operation(decode(bytes), options));
                  } catch (e) {
                    reject(e);
                  }
                }, reject);
              });
            })
            """);

        var adapter = Adapter;
        var fetchSupport = adapter.Get(GetStreams(), Key("fetchSupport"));
        var record = adapter.Call(adapter.Get(fetchSupport, Key("getStream")), adapter.Undefined, [stream]);
        var decode = adapter.CreateFunction("decode", 1, (_, args) =>
        {
            var bytes = ToBufferSource(args[0], 1u << (int)JsBufferKind.Uint8Array, allowShared: false, "Uint8Array").Bytes.Span;
            if (decodeAsStyleSheet)
                return adapter.FromString(CssImportFetching.Decode(bytes, protocolEncoding: null, environmentEncoding: null, out WebEncodingId _));

            var withoutBom = bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;
            return adapter.FromString(WebEncodingRegistry.CreateDecoder(WebEncodingId.Utf8).DecodeToString(withoutBom));
        });

        return adapter.Call(_parseCssStream, adapter.Undefined, [operation, record, options, adapter.Get(fetchSupport, Key("readAllBytes")), decode]);
    }
}
