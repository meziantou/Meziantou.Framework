using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// The parser reports an invalid script or module with a <see cref="JsSyntaxErrorException"/> whose position lies in the
/// source, and parses the same source the same way twice. The scripts and the modules that run may throw a
/// <see cref="JsException"/> (an uncaught JavaScript error) or be stopped by a limit (<see cref="JsTerminationException"/>),
/// nothing else: any other exception is an internal error of the engine. A regular expression either is rejected with a
/// SyntaxError or matches without any other error.
/// </summary>
public static class JavaScriptEngineFuzzTargets
{
    // Separates the parts of the inputs that hold several texts: JavaScript source and regular expressions accept no raw NUL here
    private const byte Separator = 0;

    private static readonly string[] Tokens =
    [
        "var ", "let ", "const ", "function ", "function* ", "async ", "await ", "yield ", "class ", "extends ", "super", "new ", "this", "return ", "throw ", "try ", "catch ", "finally ", "if ", "else ", "for ", "while ", "do ", "switch ", "case ", "default:", "break", "continue", "label:", "with ", "debugger",
        "typeof ", "void ", "delete ", "in ", "of ", "instanceof ", "import ", "export ", "from ", "as ", "static ", "get ", "set ", "target", "new.target", "import.meta", "import(", "using ", "await using ",
        "=>", "...", "?.", "??", "??=", "||=", "&&=", "**", "**=", ">>>", ">>>=", "<<=", "++", "--", "===", "!==", "`", "${", "}", "#x", "#x in ", "@", "\\u{41}", "\\u0041", "\\x41", "0b1", "0o7", "0xF", "1_0", "1n", ".5", "1e3", "<!--", "-->", "/*", "*/", "//", "#!",
        "(", ")", "[", "]", "{", "}", ";", ",", ".", ":", "?", "=", "+", "-", "*", "/", "%", "&", "|", "^", "~", "!", "<", ">", "\"", "'", "\\", "\n", "\r\n", "\u2028", "\u2029", " ", "\t",
        "Symbol.iterator", "Symbol.asyncIterator", "Promise.resolve()", "new Proxy({}, {})", "Reflect.ownKeys(", "JSON.stringify(", "JSON.parse(", "Object.defineProperty(", "Array.from(", "new Map()", "new Set()", "new WeakRef({})", "new Date(", "Temporal.Now.instant()", "new Intl.DateTimeFormat(", "new ArrayBuffer(", "new Uint8Array(", "new DataView(", "Atomics.", "BigInt(", "eval(", "new Function(", "globalThis", "arguments", "async function* ",
        "'use strict';", "import './a.js';", "import x from './a.js';", "import * as ns from './a.js';", "export * from './a.js';", "export { x } from './a.js';", "export default ", "export const x = 1;",
    ];

    private static readonly string[] RegExpTokens =
    [
        "^", "$", ".", "*", "+", "?", "|", "(", ")", "(?:", "(?=", "(?!", "(?<=", "(?<!", "(?<n>", "\\k<n>", "(?i:", "(?-i:", "(?<n>a)|(?<n>b)", "[", "]", "[^", "-", "{", "}", "{2}", "{1,}", "{2,3}", "*?", "+?", "??", "{1,2}?", "\\", "\\b", "\\B", "\\d", "\\D", "\\w", "\\W", "\\s", "\\S", "\\1", "\\0", "\\cA", "\\u{1F600}", "\\uD83D\\uDE00", "\\x41", "\\p{L}", "\\P{Lu}", "\\p{Script=Greek}", "\\p{RGI_Emoji}", "\\q{ab|c}", "[\\p{L}--[a-z]]", "[a-z&&[^b]]", "&&", "--",
        "\u00DF", "\u017F", "\u212A", "\u0130", "\uD83D", "\uDE00", "\uD83D\uDE00",
    ];

    private static readonly string[] RegExpFlags = ["", "g", "i", "m", "s", "u", "v", "y", "d", "gi", "gu", "iu", "gy", "gv", "iv", "ms", "dgimsuy", "dgimsvy"];

    // The imports of the fuzzed modules resolve to these modules; any other specifier fails
    private static readonly Dictionary<string, string> ModuleSources = new(StringComparer.Ordinal)
    {
        ["a.js"] = "export const x = 1; export default function f() { return x; } export * from './b.js';",
        ["b.js"] = "export let y = 2; export function g() { return y; }",
        ["json.json"] = "{\"a\":[1,2,3]}",
    };

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("javascript-parse", Parse) { Seeds = GetScriptSeeds, Dictionary = Tokens },
        new("javascript-execute", Execute) { Seeds = GetScriptSeeds, Dictionary = Tokens },
        new("javascript-module", ExecuteModule) { Seeds = GetModuleSeeds, Dictionary = Tokens },
        new("javascript-regexp", RegExpMatch) { Seeds = GetRegExpSeeds, Dictionary = RegExpTokens },
    ];

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);

        var isScript = JsPreparedScript.TryParse(code, url: null, lineOffset: 0, columnOffset: 0, out var script, out var error);
        if (isScript)
        {
            FuzzAssert.True(script is not null, "TryParse returned true without a script");
            FuzzAssert.True(JsPreparedScript.TryParse(code, url: null, lineOffset: 0, columnOffset: 0, out _, out _), "A script that parsed does not parse the second time");
        }
        else
        {
            FuzzAssert.True(error is not null, "TryParse returned false without an error");
            CheckPosition(code, error);
            FuzzAssert.False(JsPreparedScript.TryParse(code, url: null, lineOffset: 0, columnOffset: 0, out _, out _), "A script that was rejected parses the second time");
        }

        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        try
        {
            _ = realm.ParseModule(code, "module.js");
        }
        catch (JsSyntaxErrorException exception)
        {
            CheckPosition(code, exception);
        }
        catch (JsException)
        {
            // Some early errors are reported as a SyntaxError of the realm
        }
    }

    private static void Execute(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);
        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        Run(() => realm.Execute(code, "script.js"));
        Run(engine.RunJobs);
    }

    private static void ExecuteModule(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);
        using var engine = CreateEngine(new DictionaryModuleLoader());
        var realm = engine.CreateRealm();
        Run(() =>
        {
            var module = realm.ParseModule(code, "main.js");
            module.LoadRequestedModules();
            engine.RunJobs();

            // A module whose graph failed to load stays New: linking it is a misuse of the API
            if (module.Status is not JsModuleStatus.Unlinked)
                return;

            module.Link();
            module.Evaluate();
            engine.RunJobs();
            _ = module.GetNamespace();
        });
        Run(engine.RunJobs);
    }

    // The input is a pattern, the flags (an index into RegExpFlags), and the subject, separated by NUL
    private static void RegExpMatch(ReadOnlySpan<byte> data)
    {
        var pattern = FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));
        var flags = FuzzInput.Pick(RegExpFlags, FuzzInput.ReadByte(ref data));
        var subject = FuzzText.Decode(data);

        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("pattern", pattern);
            realm.GlobalObject.SetProperty("flags", flags);
            realm.GlobalObject.SetProperty("subject", subject);
        }

        try
        {
            // An invalid pattern must be a SyntaxError. Any other error, thrown by the match, is a bug
            realm.Execute(
                """
                let re;
                try { re = new RegExp(pattern, flags); }
                catch (e) { if (!(e instanceof SyntaxError)) throw e; }
                if (re) {
                    re.exec(subject);
                    re.test(subject);
                    subject.replace(re, "[$&|$1|$<n>]");
                    subject.split(re);
                    subject.search(re);
                    const global = new RegExp(pattern, flags.includes("g") ? flags : flags + "g");
                    for (const match of subject.matchAll(global)) { match.index; }
                    subject.replaceAll(global, (m) => m);
                }
                """,
                "regexp.js");
        }
        catch (JsTerminationException)
        {
            // A match that exceeds a limit (catastrophic backtracking) stops the script
        }
        catch (JsException exception)
        {
            FuzzAssert.Fail("The regular expression threw an error that is not a SyntaxError of the pattern: " + exception.Message);
        }
    }

    private static JsEngine CreateEngine(JsModuleLoader? loader)
    {
        return new JsEngine(new JsEngineOptions
        {
            // The statement limit bounds the work deterministically; the timeout is only a safety net, as is the memory limit
            MaxStatements = 100_000,
            Timeout = TimeSpan.FromSeconds(10),
            RegExpTimeout = TimeSpan.FromSeconds(1),
            MemoryLimit = 128 * 1024 * 1024,
            RecursionLimit = 1_000,
            TimeZone = "UTC",
            ModuleLoader = loader,
        });
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (JsException)
        {
            // An uncaught JavaScript error
        }
        catch (JsSyntaxErrorException)
        {
            // An invalid script or module
        }
        catch (JsTerminationException)
        {
            // A limit stopped the script
        }
    }

    private static void CheckPosition(string code, JsSyntaxErrorException error)
    {
        FuzzAssert.True(error.Offset >= 0 && error.Offset <= code.Length, $"The offset {error.Offset} of the syntax error is outside the source (length {code.Length})");
        FuzzAssert.True(error.Line >= 0 && error.Column >= 0, $"The position {error.Line}:{error.Column} of the syntax error is negative");
    }

    private static IEnumerable<FuzzSeed> GetScriptSeeds()
    {
        foreach (var (name, code) in JavaScriptSeeds.Scripts)
        {
            yield return FuzzSeed.FromText(name, code);
        }
    }

    private static IEnumerable<FuzzSeed> GetModuleSeeds()
    {
        foreach (var (name, code) in JavaScriptSeeds.Modules)
        {
            yield return FuzzSeed.FromText(name, code);
        }
    }

    private static IEnumerable<FuzzSeed> GetRegExpSeeds()
    {
        using var stream = FuzzResources.Open(typeof(JavaScriptEngineFuzzTargets).Assembly, "RegExpV8.jsonl");
        using var reader = new StreamReader(stream);
        var index = 0;
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            // The corpus holds 3000 random cases: a few hundred are enough to start from
            if (index++ % 8 != 0)
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var pattern = DecodeUnits(root.GetProperty("p").GetString()!);
            var flags = root.GetProperty("f").GetString()!;
            var subject = "";
            if (root.TryGetProperty("t", out var tests) && tests.GetArrayLength() > 0)
            {
                subject = DecodeUnits(tests[0].GetProperty("s").GetString()!);
            }

            var flagsIndex = Math.Max(0, Array.IndexOf(RegExpFlags, flags));
            yield return new FuzzSeed($"v8-{index}", (byte[])[.. FuzzText.Encode(pattern), Separator, (byte)flagsIndex, .. FuzzText.Encode(subject)]);
        }
    }

    // In the corpus, %XXXX is a UTF-16 code unit: the JSON readers reject the lone surrogates
    private static string DecodeUnits(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 5 <= value.Length && ushort.TryParse(value.AsSpan(i + 1, 4), System.Globalization.NumberStyles.AllowHexSpecifier, provider: null, out var unit))
            {
                builder.Append((char)unit);
                i += 4;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

    private sealed class DictionaryModuleLoader : JsModuleLoader
    {
        private readonly Dictionary<string, JsModule> _modules = new(StringComparer.Ordinal);

        protected override void LoadImportedModule(JsModuleLoadRequest request)
        {
            var name = request.Specifier.StartsWith("./", StringComparison.Ordinal) ? request.Specifier[2..] : request.Specifier;
            if (!ModuleSources.TryGetValue(name, out var source))
            {
                request.Fail("Cannot find the module '" + request.Specifier + "'");
                return;
            }

            if (!_modules.TryGetValue(name, out var module))
            {
                module = name.EndsWith(".json", StringComparison.Ordinal) ? request.Realm.CreateJsonModule(source, name) : request.Realm.ParseModule(source, name);
                _modules[name] = module;
            }

            request.Complete(module);
        }
    }
}
