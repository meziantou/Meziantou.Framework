using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// The parser reports an invalid script or module with a <see cref="JsSyntaxErrorException"/> whose position lies in the
/// source, and parses the same source the same way twice. The scripts and the modules that run may throw a
/// <see cref="JsException"/> (an uncaught JavaScript error) or be stopped by a limit (<see cref="JsTerminationException"/>),
/// nothing else: any other exception is an internal error of the engine. A regular expression either is rejected with a
/// SyntaxError or matches without any other error. A script gives the same result run as a string, as a prepared script and
/// (for its syntax errors) as an indirect eval; the line and column offsets of a source only shift its error positions. A
/// JSON module exports the value of JSON.parse, and the text and bytes modules export their content unchanged.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    // Separates the parts of the inputs that hold several texts: JavaScript source and regular expressions accept no raw NUL here
    private const byte Separator = 0;

    private const string JsonTestSuitePrefix = "JSONTestSuite.";

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

    private static readonly string[] JsonTokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\", "\\u", "\\u0000", "\\uD800", "\\uDC00", "\\n", "\\/", "true", "false", "null", "-", "+", ".", "e", "E", "e+", "e-", "0", "-0", "1e309", "5e-324", "9007199254740993", " ", "\t", "\n", "\r", "\u2028", "\uFEFF",
        "\"__proto__\"", "\"constructor\"", "\"length\"", "\"0\"", "\"4294967295\"", "\"-0\"", "[[[[[[[[", "]]]]]]]]",
    ];

    private static readonly string[] EngineOptionsTokens =
    [
        "\0", "UTC", "Etc/UTC", "Etc/GMT+5", "Etc/GMT-14", "GMT", "Z", "+05:30", "-00:00", "+14:00", "Europe/Paris", "America/New_York", "Asia/Kolkata", "Australia/Lord_Howe", "Pacific/Chatham", "America/Argentina/Buenos_Aires", "Asia/Calcutta", "US/Eastern", "EST5EDT", "Romance Standard Time", "Pacific Standard Time", "/", "..", "europe/paris",
        "en-US", "fr-FR", "de", "ja-JP-u-ca-japanese", "zh-Hant-TW", "ar-EG-u-nu-arab", "und", "root", "i-klingon", "x-private", "en-u-hc-h23", "-u-", "-t-", "-x-", "en_US", "C", "POSIX", "tr", "az-Latn", "sr-Cyrl-RS", "en-GB-oed", "art-lojban",
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
        new("javascript-json-module", JsonModule) { Seeds = GetJsonSeeds, Dictionary = JsonTokens },
        new("javascript-engine-options", EngineOptions) { Seeds = GetEngineOptionsSeeds, Dictionary = EngineOptionsTokens },
        new("javascript-string-parsers", StringParsers) { Seeds = GetStringParserSeeds, Dictionary = StringParserTokens },
    ];

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);

        // The source is also parsed as a part of a document: the offsets come from the input, which keeps its shape (a source text)
        var lineOffset = data.IsEmpty ? 1 : data[0] + 1;
        var columnOffset = data.IsEmpty ? 1 : (data[^1] * 7) + 1;

        var isScript = JsPreparedScript.TryParse(code, url: null, lineOffset: 0, columnOffset: 0, out var script, out var error);
        var isShiftedScript = JsPreparedScript.TryParse(code, "page.html", lineOffset, columnOffset, out _, out var shiftedError);
        FuzzAssert.Equal(isScript, isShiftedScript, "The offsets of the source change whether the script is valid");
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

            FuzzAssert.True(shiftedError is not null, "TryParse returned false without an error");
            CheckPosition(code, shiftedError);
            FuzzAssert.Equal(error.Message, shiftedError.Message, "The offsets of the source change the syntax error");
            FuzzAssert.Equal(error.Offset, shiftedError.Offset, "The offsets of the source change the offset of the syntax error");
            FuzzAssert.Equal("page.html", shiftedError.Url, "The syntax error does not have the URL of the script");
            var (expectedLine, expectedColumn) = ShiftPosition(error.Line, error.Column, lineOffset, columnOffset);
            FuzzAssert.Equal(expectedLine, shiftedError.Line, $"The line of the syntax error is not shifted by the line offset {lineOffset}");
            FuzzAssert.Equal(expectedColumn, shiftedError.Column, $"The column of the syntax error is not shifted by the column offset {columnOffset}");
        }

        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        var moduleError = ParseModuleError(realm, code, lineOffset: 0, columnOffset: 0);
        var shiftedModuleError = ParseModuleError(realm, code, lineOffset, columnOffset);
        FuzzAssert.Equal(moduleError is null, shiftedModuleError is null, "The offsets of the source change whether the module is valid");
        if (moduleError is not null && shiftedModuleError is not null)
        {
            FuzzAssert.Equal(moduleError.Message, shiftedModuleError.Message, "The offsets of the source change the syntax error of the module");
            FuzzAssert.Equal(moduleError.Location is null, shiftedModuleError.Location is null, "The offsets of the source change whether the syntax error of the module has a location");
            if (moduleError.Location is { } location && shiftedModuleError.Location is { } shiftedLocation)
            {
                FuzzAssert.True(location.Line > 0 && location.Column > 0, $"The location {location} of the syntax error of the module is not 1-based");
                var (expectedLine, expectedColumn) = ShiftPosition(location.Line, location.Column, lineOffset, columnOffset);
                FuzzAssert.Equal(new JsSourceLocation("module.js", expectedLine, expectedColumn), shiftedLocation, $"The location of the syntax error of the module is not shifted by the offsets {lineOffset}:{columnOffset}");
            }
        }
    }

    // The line offset shifts every line; the column offset only shifts the first line. A position-less error (line 0) stays position-less
    private static (int Line, int Column) ShiftPosition(int line, int column, int lineOffset, int columnOffset)
    {
        if (line is 0)
            return (0, column);

        return (line + lineOffset, line is 1 ? column + columnOffset : column);
    }

    // ParseModule reports an invalid module as a SyntaxError of the realm, located at the error
    private static JsException? ParseModuleError(JsRealm realm, string code, int lineOffset, int columnOffset)
    {
        try
        {
            _ = realm.ParseModule(code, "module.js", hostDefined: null, lineOffset, columnOffset);
            return null;
        }
        catch (JsException exception)
        {
            FuzzAssert.True(exception.Message.StartsWith("SyntaxError", StringComparison.Ordinal), "ParseModule threw an error that is not a SyntaxError: " + exception.Message);
            return exception;
        }
    }

    // The script runs as a string, as a prepared script in two engines (the compiled code is shared), and as an indirect eval
    private static void Execute(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);
        var expected = RunScript(realm => realm.Execute(code, "script.js"));
        if (JsPreparedScript.TryParse(code, "script.js", lineOffset: 0, columnOffset: 0, out var script, out var error))
        {
            CheckSameOutcome(expected, RunScript(realm => realm.Execute(script)), "The prepared script and the script run differently");
            CheckSameOutcome(expected, RunScript(realm => realm.Execute(script)), "The prepared script runs differently in a second engine");
        }
        else
        {
            var location = error.Line > 0 ? new JsSourceLocation(error.Url, error.Line, error.Column) : (JsSourceLocation?)null;
            FuzzAssert.Equal(new ScriptOutcome(ScriptOutcomeKind.Exception, FormatSyntaxError(error), location), expected, "Execute does not report the syntax error of TryParse");
        }

        var evaluated = RunScript(realm => realm.Evaluate(code));
        if (error is not null)
        {
            // The code of an indirect eval is parsed as a script too: its syntax errors are the same, without the location
            FuzzAssert.Equal(ScriptOutcomeKind.Exception, evaluated.Kind, "Evaluate accepts a script that TryParse rejects: " + error.Message);
            FuzzAssert.Equal(FormatSyntaxError(error), evaluated.Text, "Evaluate does not report the syntax error of TryParse");
        }
    }

    private static string FormatSyntaxError(JsSyntaxErrorException error) => string.IsNullOrEmpty(error.Message) ? "SyntaxError" : "SyntaxError: " + error.Message;

    // Runs a script in a fresh engine, where the clock and Math.random are fixed so that two runs give the same result
    private static ScriptOutcome RunScript(Func<JsRealm, JsValue> run)
    {
        using var engine = CreateEngine(loader: null, FixedTimeProvider.Instance);
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            var math = realm.GlobalObject.GetProperty("Math").AsObject();
            math.SetProperty("random", realm.CreateFunction("random", 0, (_, _) => 0.5));
        }

        ScriptOutcome outcome;
        try
        {
            var value = run(realm);
            outcome = new ScriptOutcome(ScriptOutcomeKind.Value, value.Kind + ":" + value.ToString(), Location: null);
        }
        catch (JsException exception)
        {
            outcome = new ScriptOutcome(ScriptOutcomeKind.Exception, exception.Message, exception.Location);
        }
        catch (JsTerminationException exception)
        {
            outcome = new ScriptOutcome(ScriptOutcomeKind.Terminated, exception.Reason.ToString(), Location: null);
        }

        Run(engine.RunJobs);
        return outcome;
    }

    private static void CheckSameOutcome(ScriptOutcome expected, ScriptOutcome actual, string message)
    {
        // The limits other than the statement count (time, memory) do not stop two runs at the same point
        if (IsNondeterministicTermination(expected) || IsNondeterministicTermination(actual))
            return;

        FuzzAssert.Equal(expected, actual, message);

        static bool IsNondeterministicTermination(ScriptOutcome outcome) => outcome.Kind is ScriptOutcomeKind.Terminated && outcome.Text != nameof(JsTerminationReason.StatementLimit);
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

    // The input is a JSON text: the default export of its JSON module is the value JSON.parse returns, and its text and bytes
    // modules export the text and the bytes unchanged
    private static void JsonModule(ReadOnlySpan<byte> data)
    {
        var json = FuzzText.Decode(data);
        var loader = new SyntheticModuleLoader();
        using var engine = CreateEngine(loader);
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("expectedText", json);
            realm.GlobalObject.SetProperty("expectedBytes", realm.CreateArrayBuffer(data));
        }

        JsException? parseError = null;
        try
        {
            realm.Execute("globalThis.parsed = JSON.parse(expectedText);", "parse.js");
        }
        catch (JsException exception)
        {
            parseError = exception;
        }
        catch (JsTerminationException)
        {
            return;
        }

        JsSyntheticModule? jsonModule = null;
        try
        {
            jsonModule = realm.CreateJsonModule(json, "data.json");
            FuzzAssert.True(parseError is null, "CreateJsonModule accepts a text that JSON.parse rejects: " + parseError?.Message);
        }
        catch (JsException exception)
        {
            FuzzAssert.True(parseError is not null, "CreateJsonModule rejects a text that JSON.parse accepts: " + exception.Message);
            FuzzAssert.Equal(parseError.Message, exception.Message, "CreateJsonModule and JSON.parse report different errors");
        }

        loader.Modules["./data.json"] = jsonModule;
        loader.Modules["./data.txt"] = realm.CreateTextModule(json, "data.txt");
        loader.Modules["./data.bin"] = realm.CreateBytesModule(data, "data.bin");

        // The values are compared without recursion: the depth of the JSON value is only bounded by the statement limit
        var main = (jsonModule is null ? "const data = undefined;\n" : "import data from './data.json' with { type: 'json' };\n") +
            """
            import text from './data.txt' with { type: 'text' };
            import bytes from './data.bin' with { type: 'bytes' };
            function check() {
                if (text !== expectedText) return 'the text module does not export the text';
                if (!(bytes instanceof Uint8Array)) return 'the bytes module does not export a Uint8Array';
                const expected = new Uint8Array(expectedBytes);
                if (bytes.length !== expected.length) return 'the bytes module exports ' + bytes.length + ' bytes instead of ' + expected.length;
                for (let i = 0; i < expected.length; i++) {
                    if (bytes[i] !== expected[i]) return 'the byte ' + i + ' of the bytes module is ' + bytes[i] + ' instead of ' + expected[i];
                }

                if (!('parsed' in globalThis)) return 'ok';
                const pending = [[data, parsed, '$']];
                while (pending.length > 0) {
                    const [actual, value, path] = pending.pop();
                    if (typeof actual !== 'object' || actual === null || typeof value !== 'object' || value === null) {
                        if (!Object.is(actual, value)) return path + ' is ' + String(actual) + ' instead of ' + String(value);
                        continue;
                    }

                    if (Array.isArray(actual) !== Array.isArray(value)) return path + ' is not an array in both values';
                    if (Object.getPrototypeOf(actual) !== Object.getPrototypeOf(value)) return path + ' does not have the same prototype';
                    const actualKeys = Reflect.ownKeys(actual);
                    const keys = Reflect.ownKeys(value);
                    if (actualKeys.length !== keys.length) return path + ' has ' + actualKeys.length + ' keys instead of ' + keys.length;
                    for (let i = 0; i < keys.length; i++) {
                        if (actualKeys[i] !== keys[i]) return 'the key ' + i + ' of ' + path + ' is ' + String(actualKeys[i]) + ' instead of ' + String(keys[i]);
                        const actualDescriptor = Object.getOwnPropertyDescriptor(actual, keys[i]);
                        const descriptor = Object.getOwnPropertyDescriptor(value, keys[i]);
                        if (actualDescriptor.writable !== descriptor.writable || actualDescriptor.enumerable !== descriptor.enumerable || actualDescriptor.configurable !== descriptor.configurable) return path + '.' + String(keys[i]) + ' does not have the same attributes';
                        pending.push([actualDescriptor.value, descriptor.value, path + '.' + String(keys[i])]);
                    }
                }

                return 'ok';
            }

            globalThis.checkResult = check();
            """;

        try
        {
            var module = realm.ParseModule(main, "main.js");
            module.LoadRequestedModules();
            engine.RunJobs();
            FuzzAssert.Equal(JsModuleStatus.Unlinked, module.Status, "The synthetic modules were not loaded");
            module.Link();
            module.Evaluate();
            engine.RunJobs();
            if (module.HasEvaluationError)
            {
                using (engine.Enter())
                {
                    FuzzAssert.Fail("The module that imports the synthetic modules failed: " + new JsException(module.EvaluationError, location: null).Message);
                }
            }

            using (engine.Enter())
            {
                var result = realm.GlobalObject.GetProperty("checkResult");
                FuzzAssert.Equal("ok", result.IsString ? result.AsString() : result.ToString(), "The synthetic modules do not export their content");
            }
        }
        catch (JsTerminationException)
        {
            // A large value exceeds the statement limit of the comparison
        }
    }

    // The input is a time zone and a default locale, separated by NUL: an identifier that is not valid or not known falls
    // back to a default (UTC, en-US), so the engine works with any value
    private static void EngineOptions(ReadOnlySpan<byte> data)
    {
        var timeZone = FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));
        var locale = FuzzText.Decode(data);

        using var engine = new JsEngine(new JsEngineOptions
        {
            MaxStatements = 100_000,
            Timeout = TimeSpan.FromSeconds(10),
            RecursionLimit = 1_000,
            TimeZone = timeZone,
            DefaultLocale = locale,
            TimeProvider = FixedTimeProvider.Instance,
        });
        var realm = engine.CreateRealm();
        try
        {
            var result = realm.Execute(
                """
                (() => {
                    const options = new Intl.DateTimeFormat().resolvedOptions();
                    if (typeof options.locale !== 'string' || Intl.getCanonicalLocales(options.locale)[0] !== options.locale) return 'the default locale ' + options.locale + ' is not a canonical language tag';
                    if (new Intl.NumberFormat().resolvedOptions().locale !== options.locale) return 'Intl.NumberFormat does not use the default locale';
                    if (typeof options.timeZone !== 'string') return 'the time zone is not a string';
                    if (new Intl.DateTimeFormat('en', { timeZone: options.timeZone }).resolvedOptions().timeZone !== options.timeZone) return 'the time zone ' + options.timeZone + ' is not a canonical time zone';
                    if (Temporal.Now.timeZoneId() !== options.timeZone) return 'Temporal.Now.timeZoneId() is ' + Temporal.Now.timeZoneId() + ' instead of ' + options.timeZone;
                    const date = new Date(2026, 0, 15, 12, 30);
                    if (date.getHours() !== 12 || date.getMinutes() !== 30) return 'the local time does not round trip';
                    const offset = date.getTimezoneOffset();
                    if (!Number.isFinite(offset)) return 'the offset of the time zone is ' + offset;
                    const zoned = Temporal.Now.zonedDateTimeISO();
                    if (zoned.timeZoneId !== options.timeZone) return 'the zoned date time is in ' + zoned.timeZoneId;
                    [date.toString(), date.toLocaleString(), (1234.5).toLocaleString(), 'I'.toLocaleLowerCase(), ['b', 'a'].sort(new Intl.Collator().compare), new Intl.PluralRules().select(1), new Intl.RelativeTimeFormat().format(1, 'day'), new Intl.ListFormat().format(['a', 'b'])];
                    return 'ok';
                })()
                """,
                "options.js");
            FuzzAssert.Equal("ok", result.IsString ? result.AsString() : result.ToString(), $"The engine does not work with the time zone '{FuzzText.Escape(timeZone)}' and the locale '{FuzzText.Escape(locale)}'");
        }
        catch (JsException exception)
        {
            FuzzAssert.Fail($"The engine does not work with the time zone '{FuzzText.Escape(timeZone)}' and the locale '{FuzzText.Escape(locale)}': {exception.Message}");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(nameof(JsEngineOptions.TimeZone), StringComparison.Ordinal))
        {
            // A time zone that is not in the IANA database is a misconfiguration of the host, reported when the scripts first use the local time zone
            FuzzAssert.False(IsNamedTimeZone(timeZone), $"The engine rejects the time zone '{FuzzText.Escape(timeZone)}' that Intl.DateTimeFormat accepts: {exception.Message}");
        }
    }

    // Whether Intl.DateTimeFormat accepts the identifier as an IANA time zone (not an offset time zone such as +05:30)
    private static bool IsNamedTimeZone(string timeZone)
    {
        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("timeZone", timeZone);
        }

        var result = realm.Execute(
            """
            (() => {
                try { return !/^[+-]/.test(new Intl.DateTimeFormat('en', { timeZone }).resolvedOptions().timeZone); }
                catch (e) { if (e instanceof RangeError) return false; throw e; }
            })()
            """,
            "timezone.js");
        return result.IsBoolean && result.AsBoolean();
    }

    private static JsEngine CreateEngine(JsModuleLoader? loader, TimeProvider? timeProvider = null)
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
            TimeProvider = timeProvider ?? TimeProvider.System,
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

    // The test_parsing files of JSONTestSuite, and the JSON texts of the seeds of the other targets
    private static IEnumerable<FuzzSeed> GetJsonSeeds()
    {
        var assembly = typeof(JavaScriptEngineFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!name.StartsWith(JsonTestSuitePrefix, StringComparison.Ordinal))
                continue;

            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed("jsontestsuite-" + Path.GetFileNameWithoutExtension(name[JsonTestSuitePrefix.Length..]), memory.ToArray());
        }

        foreach (var (name, json) in JavaScriptSeeds.Json)
        {
            yield return FuzzSeed.FromText(name, json);
        }
    }

    private static IEnumerable<FuzzSeed> GetEngineOptionsSeeds()
    {
        foreach (var (name, timeZone, locale) in JavaScriptSeeds.EngineOptions)
        {
            yield return new FuzzSeed(name, (byte[])[.. FuzzText.Encode(timeZone), Separator, .. FuzzText.Encode(locale)]);
        }
    }

    private enum ScriptOutcomeKind
    {
        Value,
        Exception,
        Terminated,
    }

    // How a script ends: its completion value (kind and string), its uncaught error (message and location), or the limit that stopped it
    private readonly record struct ScriptOutcome(ScriptOutcomeKind Kind, string Text, JsSourceLocation? Location);

    // The clock of the scripts that must give the same result twice; the timestamps (timers, timeouts) still use the system clock
    private sealed class FixedTimeProvider : TimeProvider
    {
        public static FixedTimeProvider Instance { get; } = new();

        public override DateTimeOffset GetUtcNow() => new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class SyntheticModuleLoader : JsModuleLoader
    {
        public Dictionary<string, JsModule?> Modules { get; } = new(StringComparer.Ordinal);

        protected override void LoadImportedModule(JsModuleLoadRequest request)
        {
            if (Modules.GetValueOrDefault(request.Specifier) is { } module)
            {
                request.Complete(module);
            }
            else
            {
                request.Fail("Cannot find the module '" + request.Specifier + "'");
            }
        }
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
