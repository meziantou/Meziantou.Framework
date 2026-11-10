using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.WebApis.Fuzz;

/// <summary>
/// The web APIs run through their bindings, as a script calls them. Each target runs a check script on the input, which
/// returns <c>ok</c> or a message starting with <c>FAIL:</c>: the APIs throw only the errors they document for invalid
/// input, and their results agree with each other (round trips, two code paths that give the same result).
/// </summary>
public static class WebApisFuzzTargets
{
    // The labels of the decoders: the option byte of an input picks one
    private static readonly string[] Labels = ["utf-8", "utf-16le", "utf-16be", "windows-1252", "iso-8859-2", "koi8-r", "macintosh", "x-mac-cyrillic", "gbk", "gb18030", "big5", "euc-jp", "iso-2022-jp", "shift_jis", "euc-kr", "x-user-defined"];

    private static readonly string[] UrlTokens = ["https://", "http://", "file:///", "data:", "blob:", "mailto:", "ws://", "//", "/", "./", "../", "?", "#", "@", ":", "[", "]", "::1", "%", "%2e", "%00", "xn--", "\\", "\t", "\n", " ", "=", "&", "+", ";", "localhost", "127.0.0.1", "0x7f.1", "Bücher", "ß", "\u3002"];

    private static readonly string[] Base64Tokens = ["=", "==", "+", "/", " ", "\t", "\n", "\f", "\r", "A", "z", "9", "AAAA", "/w=="];

    private static readonly string[] JsonTokens = ["{", "}", "[", "]", ":", ",", "\"", "null", "true", "false", "0", "-0", "1e308", "\"__proto__\"", "\"length\"", "\"constructor\""];

    private static readonly string[] ConsoleTokens = ["%s", "%d", "%i", "%f", "%o", "%O", "%c", "%%", "%", "{", "}", "[", "]", "'", "\n"];

    // After the fields it uses: the static fields are initialized in the order of the declarations
    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("javascript-webapis-url", Url) { Seeds = GetUrlSeeds, Dictionary = UrlTokens },
        new("javascript-webapis-url-search-params", UrlSearchParams) { Seeds = GetUrlSearchParamsSeeds, Dictionary = UrlTokens },
        new("javascript-webapis-base64", Base64) { Seeds = GetBase64Seeds, Dictionary = Base64Tokens },
        new("javascript-webapis-text-decoder", TextDecoder) { Seeds = GetTextDecoderSeeds },
        new("javascript-webapis-text-encoder", TextEncoder) { Seeds = GetTextEncoderSeeds },
        new("javascript-webapis-structured-clone", StructuredClone) { Seeds = GetStructuredCloneSeeds, Dictionary = JsonTokens },
        new("javascript-webapis-console", Console) { Seeds = GetConsoleSeeds, Dictionary = ConsoleTokens },
    ];

    private const string UrlScript = """
        (() => {
          const check = (base) => {
            let url;
            try {
              url = base === undefined ? new URL(input) : new URL(input, base);
            } catch (e) {
              if (!(e instanceof TypeError)) return "FAIL: the constructor throws " + e;
              if (URL.canParse(input, base)) return "FAIL: canParse accepts an URL that the constructor rejects";
              if (URL.parse(input, base) !== null) return "FAIL: parse accepts an URL that the constructor rejects";
              return "ok";
            }

            if (!URL.canParse(input, base)) return "FAIL: canParse rejects " + url.href;
            const href = url.href;
            const reparsed = new URL(href);
            if (reparsed.href !== href) return "FAIL: " + href + " is parsed as " + reparsed.href;

            // Setting a component to its value does not change the URL. An empty value is skipped: the getters return an
            // empty string for an empty component and for a missing one (e.g. "#" and no fragment)
            for (const name of ['protocol', 'username', 'password', 'host', 'hostname', 'port', 'pathname', 'search', 'hash']) {
              if (url[name] === '') continue;
              const copy = new URL(href);
              copy[name] = url[name];
              if (copy.href !== href) return "FAIL: setting " + name + " to " + url[name] + " changes " + href + " to " + copy.href;
            }

            const params = url.searchParams.toString();
            if (new URLSearchParams(url.search).toString() !== params) return "FAIL: the searchParams of " + href + " differ from its search";
            return "ok";
          };

          for (const base of [undefined, 'https://example.com/a/b?c#d', 'file:///C:/x', 'about:blank']) {
            const result = check(base);
            if (result !== "ok") return result;
          }

          // The setters accept any value
          const url = new URL('https://user:pass@example.com:8080/a/b?c=d#e');
          for (const name of ['href', 'protocol', 'username', 'password', 'host', 'hostname', 'port', 'pathname', 'search', 'hash']) {
            try {
              url[name] = input;
            } catch (e) {
              if (!(name === 'href' && e instanceof TypeError)) return "FAIL: setting " + name + " throws " + e;
            }

            if (new URL(url.href).href !== url.href) return "FAIL: after setting " + name + ", " + url.href + " is parsed differently";
          }

          return "ok";
        })()
        """;

    private const string UrlSearchParamsScript = """
        (() => {
          const params = new URLSearchParams(input);
          const text = params.toString();
          const again = new URLSearchParams(text);
          if (again.toString() !== text) return "FAIL: " + text + " is serialized as " + again.toString();
          if (again.size !== params.size) return "FAIL: the size of " + text + " changes";
          const entries = [...params];
          const fromEntries = new URLSearchParams(entries);
          if (fromEntries.toString() !== text) return "FAIL: the entries of " + text + " are serialized as " + fromEntries.toString();
          for (const [name, value] of entries) {
            if (!params.has(name) || !params.has(name, value) || !params.getAll(name).includes(value)) return "FAIL: " + name + " is missing";
          }

          params.sort();
          const sorted = [...params].map(([name]) => name);
          for (let i = 1; i < sorted.length; i++) {
            if (sorted[i - 1] > sorted[i]) return "FAIL: the names are not sorted";
          }

          const url = new URL('https://example.com/');
          url.search = input;
          if (new URLSearchParams(url.search).toString() !== url.searchParams.toString()) return "FAIL: the searchParams of a URL differ from its search";
          return "ok";
        })()
        """;

    private const string Base64Script = """
        (() => {
          let decoded;
          try {
            decoded = atob(input);
          } catch (e) {
            if (!(e instanceof DOMException) || e.name !== 'InvalidCharacterError') return "FAIL: atob throws " + e;
            decoded = null;
          }

          if (decoded !== null) {
            for (let i = 0; i < decoded.length; i++) {
              if (decoded.charCodeAt(i) > 255) return "FAIL: atob returns a character outside of Latin-1";
            }

            const encoded = btoa(decoded);
            if (atob(encoded) !== decoded) return "FAIL: the encoding of the result of atob does not decode to it";
          }

          let latin1 = true;
          for (let i = 0; i < input.length; i++) latin1 &&= input.charCodeAt(i) <= 255;
          try {
            const encoded = btoa(input);
            if (!latin1) return "FAIL: btoa accepts a character outside of Latin-1";
            if (atob(encoded) !== input) return "FAIL: btoa does not round trip";
          } catch (e) {
            if (latin1 || !(e instanceof DOMException) || e.name !== 'InvalidCharacterError') return "FAIL: btoa throws " + e;
          }

          return decoded === null ? "rejected" : "ok";
        })()
        """;

    private const string TextDecoderScript = """
        (() => {
          let expected;
          try {
            expected = new TextDecoder(label).decode(bytes);
          } catch (e) {
            return "FAIL: a non-fatal decoder throws " + e;
          }

          // Decoding the bytes in chunks gives the same text as decoding them at once
          const decoder = new TextDecoder(label);
          let streamed = '';
          for (let start = 0; start < bytes.length; start += chunkSize) {
            streamed += decoder.decode(bytes.subarray(start, start + chunkSize), { stream: true });
          }

          streamed += decoder.decode();
          if (streamed !== expected) return "FAIL: the streaming decoder gives a different text";

          // A fatal decoder gives the same text, or rejects the bytes
          try {
            const fatal = new TextDecoder(label, { fatal: true }).decode(bytes);
            if (fatal !== expected) return "FAIL: the fatal decoder gives a different text";
          } catch (e) {
            if (!(e instanceof TypeError)) return "FAIL: the fatal decoder throws " + e;
          }

          // The decoder is reset after a call that is not streamed
          if (decoder.decode(bytes) !== expected) return "FAIL: the decoder is not reset";
          return "ok";
        })()
        """;

    private const string TextEncoderScript = """
        (() => {
          const encoder = new TextEncoder();
          const bytes = encoder.encode(input);
          const text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(bytes);
          if (text !== input.toWellFormed()) return "FAIL: the UTF-8 bytes do not decode to the text";

          // encodeInto writes a prefix of the same bytes, and reports what it read and wrote
          const destination = new Uint8Array(chunkSize);
          const result = encoder.encodeInto(input, destination);
          if (result.written > destination.length || result.read > input.length) return "FAIL: encodeInto reports more than the buffers";
          const prefix = encoder.encode(input.slice(0, result.read));
          if (prefix.length !== result.written) return "FAIL: encodeInto writes " + result.written + " bytes for " + result.read + " code units";
          for (let i = 0; i < prefix.length; i++) {
            if (prefix[i] !== destination[i]) return "FAIL: encodeInto writes different bytes";
          }

          return "ok";
        })()
        """;

    private const string StructuredCloneScript = """
        (() => {
          let value;
          try {
            value = JSON.parse(input);
          } catch {
            value = input;
          }

          const clone = structuredClone(value);
          if (JSON.stringify(clone) !== JSON.stringify(value)) return "FAIL: the clone differs from the value";
          if (value !== null && typeof value === 'object' && clone === value) return "FAIL: the clone is the value";

          // A graph with cycles and shared objects keeps its shape
          const graph = { value, list: [value, value] };
          graph.self = graph;
          const graphClone = structuredClone(graph);
          if (graphClone.self !== graphClone || graphClone.list[0] !== graphClone.list[1]) return "FAIL: the shape of the graph changes";
          if (value !== null && typeof value === 'object' && graphClone.value !== graphClone.list[0]) return "FAIL: the shared object is cloned twice";

          // The other serializable types
          const map = structuredClone(new Map([[input, value]]));
          if (!map.has(input) || JSON.stringify(map.get(input)) !== JSON.stringify(value)) return "FAIL: the Map differs";
          const error = structuredClone(new TypeError(input));
          if (!(error instanceof TypeError) || error.message !== input) return "FAIL: the error differs";
          const exception = structuredClone(new DOMException(input, 'DataCloneError'));
          if (!(exception instanceof DOMException) || exception.message !== input || exception.code !== 25) return "FAIL: the DOMException differs";
          return "ok";
        })()
        """;

    private const string ConsoleScript = """
        (() => {
          const value = { input, list: [input, 1, -0, 1n, Symbol(input)], nested: { map: new Map([[input, new Set([input])]]) } };
          value.self = value;
          console.log(input, 'a', 1, value);
          console.log(input);
          console.log(input, input, input);
          console.dir(value);
          console.assert(false, input, value);
          console.count(input);
          console.countReset(input);
          console.group(input);
          console.time(input);
          console.timeLog(input, value);
          console.timeEnd(input);
          console.groupEnd();
          console.table(value);
          return "ok";
        })()
        """;

    private static void Url(ReadOnlySpan<byte> data) => RunCheck(UrlScript, FuzzText.Decode(data));

    private static void UrlSearchParams(ReadOnlySpan<byte> data) => RunCheck(UrlSearchParamsScript, FuzzText.Decode(data));

    private static void Base64(ReadOnlySpan<byte> data) => RunCheck(Base64Script, FuzzText.Decode(data));

    private static void StructuredClone(ReadOnlySpan<byte> data) => RunCheck(StructuredCloneScript, FuzzText.Decode(data));

    /// <summary>Decodes the bytes (after an option byte that chooses the label, and one that chooses the size of the chunks).</summary>
    private static void TextDecoder(ReadOnlySpan<byte> data)
    {
        var label = FuzzInput.Pick(Labels, FuzzInput.ReadByte(ref data));
        var chunkSize = 1 + (FuzzInput.ReadByte(ref data) % 16);
        var bytes = data.ToArray();
        RunCheck(TextDecoderScript, input: "", (realm, global) =>
        {
            global.SetProperty("label", label);
            global.SetProperty("chunkSize", chunkSize);
            var buffer = realm.CreateArrayBuffer(bytes);
            global.SetProperty("bytes", realm.CreateBufferView(JsBufferKind.Uint8Array, buffer, 0, bytes.Length));
        });
    }

    /// <summary>Encodes the text (after an option byte that chooses the size of the destination of <c>encodeInto</c>).</summary>
    private static void TextEncoder(ReadOnlySpan<byte> data)
    {
        var size = FuzzInput.ReadByte(ref data) % 32;
        RunCheck(TextEncoderScript, FuzzText.Decode(data), (_, global) => global.SetProperty("chunkSize", size));
    }

    private static void Console(ReadOnlySpan<byte> data)
    {
        var messages = new List<JsConsoleMessage>();
        RunCheck(ConsoleScript, FuzzText.Decode(data), configure: null, messages.Add);
        FuzzAssert.True(messages.Count > 0, "The console does not write any message");
        foreach (var message in messages)
        {
            FuzzAssert.True(message.GroupDepth is 0 or 1, $"The group depth of {message.Method} is {message.GroupDepth}");
        }
    }

    // Runs the check script in a new realm (the APIs keep state, e.g. the counters of the console), where the input is
    // the global "input"
    private static void RunCheck(string script, string input, Action<JsRealm, JsObject>? configure = null, Action<JsConsoleMessage>? consoleMessage = null)
    {
        using var engine = new JsEngine(new JsEngineOptions { TimeProvider = FixedTimeProvider.Instance });
        var realm = engine.CreateRealm();
        JsWebApis.Install(realm, new JsWebApisOptions { ConsoleMessage = consoleMessage ?? (_ => { }) });
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("input", input);
            configure?.Invoke(realm, realm.GlobalObject);
        }

        string result;
        try
        {
            result = realm.Execute(script).AsString();
        }
        catch (JsException exception)
        {
            FuzzAssert.Fail("The check throws: " + exception.Message);
            return;
        }

        FuzzAssert.False(result.StartsWith("FAIL:", StringComparison.Ordinal), $"{result} (input: {FuzzText.Escape(input)})");
    }

    private static IEnumerable<FuzzSeed> GetUrlSeeds()
    {
        string[] seeds =
        [
            "https://user:pass@example.com:8080/a/./x/../b?c=d#e", "HTTPS://EXAMPLE.com:443", "https://Bücher.example", "data:text/plain,a",
            "../x", "?q", "//other", "/x", "not a url", "http://[::1]:80/", "http://0x7f.1/", "file:///C:/a/../b", "mailto:a@b",
            "blob:https://a.com/uuid", "https://a.com/%2e%2E/x", "http://a b", "https://xn--nxasmq6b.com/", "ws://a/?x#y", "about:blank",
            "https://a.com/p q?x=1&y=a b#h i", "http:\\\\a\\b", "non-special://h:1/p", "javascript:alert(1)", "https://ß.example/",
        ];
        return seeds.Select((seed, index) => FuzzSeed.FromText($"url-{index}", seed));
    }

    private static IEnumerable<FuzzSeed> GetUrlSearchParamsSeeds()
    {
        string[] seeds = ["?a=1&b=2&a=3", "a=1&b=2", "q=a+b%26c%3D%E2%82%AC", "a b=c d", "=", "&&", "a=%zz", "%F0%9F%98%80=1", "undefined", "a=1;b=2", "\uD800=x", ""];
        return seeds.Select((seed, index) => FuzzSeed.FromText($"url-search-params-{index}", seed));
    }

    private static IEnumerable<FuzzSeed> GetBase64Seeds()
    {
        string[] seeds = ["aGVsbG8=", " aGVs\nbG8 ", "aGVsbG8", "", "a", "aGVsbG8==", "a*==", "/w==", "hello", "\u00FF\u00E9", "€", "AAAA====", "YQ", "YR=="];
        return seeds.Select((seed, index) => FuzzSeed.FromText($"base64-{index}", seed));
    }

    private static IEnumerable<FuzzSeed> GetTextDecoderSeeds()
    {
        byte[][] inputs =
        [
            [0xEF, 0xBB, 0xBF, 0x61, 0xE2, 0x82, 0xAC],
            [0x61, 0xFF],
            [0xE2, 0x82],
            Encoding.UTF8.GetBytes("Hello, wörld! 😀"),
            [0xFF, 0xFE, 0x61, 0x00, 0x3D, 0xD8, 0x00, 0xDE],
            [0x80, 0x81, 0x9F, 0xA0, 0xFF],
            [0x82, 0xA0, 0x81, 0x40, 0x5C, 0x7E],
            [0x1B, 0x24, 0x42, 0x30, 0x21, 0x1B, 0x28, 0x42, 0x61],
            [0xA4, 0xA2, 0x8F, 0xA2, 0xAF],
            [0x81, 0x30, 0x81, 0x30, 0xFE, 0x39, 0xFE, 0x39],
        ];
        var index = 0;
        foreach (var input in inputs)
        {
            for (var label = 0; label < Labels.Length; label++)
            {
                yield return new FuzzSeed($"text-decoder-{index++}", (byte[])[(byte)label, (byte)(index % 5), .. input]);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetTextEncoderSeeds()
    {
        string[] seeds = ["a€😀", "", "\uD800", "abc\uDC00def", "Hello, wörld!", "\0"];
        return seeds.SelectMany((seed, index) => new[] { 0, 1, 4, 5 }.Select(size => FuzzSeed.FromText($"text-encoder-{index}-{size}", [(byte)size], seed)));
    }

    private static IEnumerable<FuzzSeed> GetStructuredCloneSeeds()
    {
        string[] seeds = ["{\"a\":1,\"b\":[1,2,{\"c\":null}]}", "[]", "{}", "null", "\"text\"", "1e308", "-0", "{\"__proto__\":{\"x\":1}}", "[[[[[[1]]]]]]", "not json", "{\"length\":2}"];
        return seeds.Select((seed, index) => FuzzSeed.FromText($"structured-clone-{index}", seed));
    }

    private static IEnumerable<FuzzSeed> GetConsoleSeeds()
    {
        string[] seeds = ["%s is %d years and %f%%", "%cred", "%o %O", "%", "%%", "%x", "plain text", "it's\n", ""];
        return seeds.Select((seed, index) => FuzzSeed.FromText($"console-{index}", seed));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public static FixedTimeProvider Instance { get; } = new();

        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override long GetTimestamp() => 0;
    }
}
