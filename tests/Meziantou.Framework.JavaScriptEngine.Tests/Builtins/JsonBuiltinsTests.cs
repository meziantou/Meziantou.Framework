using System.Text;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class JsonBuiltinsTests
{
    private static string Stringify(TestRealm realm, params JsValue[] arguments) => realm.Call("JSON.stringify", arguments).AsString();

    private static JsValue Parse(TestRealm realm, string text) => realm.Call("JSON.parse", text);

    /// <summary>Defines String.prototype.toString, used to convert the String objects (the String built-ins are written separately).</summary>
    private static void DefineStringToString(TestRealm realm)
    {
        var toString = realm.CreateFunction((thisValue, _) => JsValue.FromJsString(((JsStringObject)thisValue.AsObject()).StringData));
        realm.Realm.Intrinsics.StringPrototype.DefineBuiltIn("toString", toString);
    }

    [Theory]
    [InlineData("{\"a\":[1,{\"b\":2}],\"c\":\"x\"}")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[1,-2.5,1e+21,5e-324,true,false,null,\"\"]")]
    [InlineData("{\"0\":1,\"a\":{\"b\":{\"c\":[[[]]]}},\"__proto__\":{}}")]
    [InlineData("\"\\ud800\\u0001\\n\\\\\\\"\"")]
    [InlineData("-0")]
    public void RoundTrip(string json)
    {
        using var realm = new TestRealm();
        var expected = json == "-0" ? "0" : json;
        Assert.Equal(expected, Stringify(realm, Parse(realm, json)));
    }

    [Fact]
    public void ParseValues()
    {
        using var realm = new TestRealm();
        Assert.Equal(-0.0, Parse(realm, " -0 ").AsNumber());
        Assert.True(double.IsNegative(Parse(realm, "-0").AsNumber()));
        Assert.Equal(1.5e300, Parse(realm, "1.5e300").AsNumber());
        Assert.Equal(12345678901234567890.0, Parse(realm, "12345678901234567890").AsNumber());
        Assert.Equal(double.PositiveInfinity, Parse(realm, "1e400").AsNumber());
        Assert.Equal("\u2028\u2029\ud800/", Parse(realm, "\"\u2028\u2029\\ud800\\/\"").AsString());
        Assert.Equal("\u00E9", Parse(realm, "\"\\u00E9\"").AsString());
        Assert.True(Parse(realm, "\t\r\n null \t\r\n").IsNull);

        // Duplicate keys: the last value wins at the position of the first key; __proto__ is an own property
        var obj = Parse(realm, "{\"a\":1,\"b\":2,\"a\":3,\"__proto__\":4}").AsObject();
        Assert.Equal("{\"a\":3,\"b\":2,\"__proto__\":4}", Stringify(realm, obj));
        Assert.Same(realm.Realm.Intrinsics.ObjectPrototype, obj.GetPrototypeOf());

        var array = Parse(realm, "[1,[2,[3]],4]").AsObject();
        Assert.IsType<JsArray>(array);
        Assert.Equal(3u, ((JsArray)array).Length);
        Assert.Same(realm.Realm.Intrinsics.ArrayPrototype, array.GetPrototypeOf());
    }

    [Theory]
    [InlineData("", "Unexpected end of JSON input")]
    [InlineData("[1,]", "Unexpected token ']' in JSON at position 3 (line 1 column 4)")]
    [InlineData("{\"a\" 1}", "Expected ':' after property name in JSON at position 5 (line 1 column 6)")]
    [InlineData("01", "Unexpected non-whitespace character after JSON at position 1 (line 1 column 2)")]
    [InlineData("\"\\x\"", "Bad escaped character in JSON at position 2 (line 1 column 3)")]
    [InlineData("[\n1,\n  x]", "Unexpected token 'x' in JSON at position 7 (line 3 column 3)")]
    [InlineData("{\"a\":1,}", "Expected property name or '}' in JSON at position 7 (line 1 column 8)")]
    [InlineData("[1 2]", "Expected ',' or ']' after array element in JSON at position 3 (line 1 column 4)")]
    [InlineData("\"a\u0001\"", "Bad control character in string literal in JSON at position 2 (line 1 column 3)")]
    [InlineData("\"abc", "Unterminated string in JSON at position 4 (line 1 column 5)")]
    [InlineData("-", "No number after minus sign in JSON at position 1 (line 1 column 2)")]
    [InlineData("1.", "Unterminated fractional number in JSON at position 2 (line 1 column 3)")]
    [InlineData("1e+", "Exponent part is missing a number in JSON at position 3 (line 1 column 4)")]
    [InlineData("tru", "Unexpected end of JSON input")]
    [InlineData("nul1", "Unexpected token '1' in JSON at position 3 (line 1 column 4)")]
    [InlineData("\"\\u12g4\"", "Bad Unicode escape in JSON at position 5 (line 1 column 6)")]
    public void ParseErrors(string json, string message)
    {
        using var realm = new TestRealm();
        var error = realm.GetError(() => Parse(realm, json));
        Assert.Equal("SyntaxError", JsOperations.ToString(error.Get(new PropertyKey("name"))));
        Assert.Equal(message, JsOperations.ToString(error.Get(new PropertyKey("message"))));
    }

    [Theory]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("0x10")]
    [InlineData("'a'")]
    [InlineData("\u00A01")]
    [InlineData("\u000B1")]
    [InlineData("[1,,2]")]
    [InlineData("{a:1}")]
    [InlineData("undefined")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("\"\t\"")]
    [InlineData("1 2")]
    public void ParseInvalid(string json)
    {
        using var realm = new TestRealm();
        Assert.Equal("SyntaxError", realm.GetErrorName(() => Parse(realm, json)));
    }

    [Fact]
    public void ParseDeepNesting()
    {
        using var realm = new TestRealm();
        const int Depth = 200_000;
        var json = new string('[', Depth) + new string(']', Depth);
        var value = Parse(realm, json);
        for (var i = 0; i < 10; i++)
        {
            value = value.AsObject().Get(new PropertyKey(0));
        }

        Assert.True(value.IsObject);

        // The serialization is recursive: it throws a RangeError instead of overflowing the stack
        Assert.Equal("RangeError", realm.GetErrorName(() => Stringify(realm, Parse(realm, json))));
    }

    [Fact]
    public void ParseLargeInput()
    {
        using var realm = new TestRealm();
        var builder = new StringBuilder("[");
        for (var i = 0; i < 20_000; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"id\":").Append(i).Append(",\"name\":\"item ").Append(i).Append("\",\"tags\":[\"a\",\"b\\n\"],\"value\":").Append(i * 1.5).Append('}');
        }

        builder.Append(']');
        var json = builder.ToString();
        var value = Parse(realm, json);
        Assert.Equal(20_000u, ((JsArray)value.AsObject()).Length);
        Assert.Equal(json, Stringify(realm, value));
    }

    [Fact]
    public void ReviverContextSource()
    {
        using var realm = new TestRealm();
        var log = new List<string>();
        var reviver = realm.CreateFunction((thisValue, arguments) =>
        {
            var source = arguments[2].AsObject().Get(new PropertyKey("source"));
            log.Add($"{JsOperations.ToString(arguments[0])}={Stringify(realm, arguments[1])}:{(source.IsUndefined ? "undefined" : source.AsString())}");
            return arguments[1];
        });

        realm.Call("JSON.parse", " {\"a\": 1.50e1, \"b\": [true, \"x\\ty\", null], \"a\": -0} ", reviver);
        Assert.Equal(["a=0:-0", "0=true:true", "1=\"x\\ty\":\"x\\ty\"", "2=null:null", "b=[true,\"x\\ty\",null]:undefined", "={\"a\":0,\"b\":[true,\"x\\ty\",null]}:undefined"], log);
    }

    [Fact]
    public void ReviverForwardModification()
    {
        // test262 built-ins/JSON/parse/reviver-call-args-after-forward-modification.js
        using var realm = new TestRealm();
        var log = new List<string>();
        var reviver = realm.CreateFunction((thisValue, arguments) =>
        {
            var key = arguments[0];
            var value = arguments[1];
            var source = arguments[2].AsObject().Get(new PropertyKey("source"));
            log.Add($"key: |{JsOperations.ToString(key)}| value: {Stringify(realm, value)} source: |{JsOperations.ToString(source)}|");
            if (value.IsNumber && value.AsNumber() == 1)
            {
                var array = thisValue.AsObject().Get(new PropertyKey(1)).AsObject();
                array.CreateDataPropertyOrThrow(new PropertyKey(0), "barf");
            }

            return thisValue.AsObject().Get(JsOperations.ToPropertyKey(key));
        });

        realm.Call("JSON.parse", "[1,[]]", reviver);
        Assert.Equal(
        [
            "key: |0| value: 1 source: |1|",
            "key: |0| value: \"barf\" source: |undefined|",
            "key: |1| value: [\"barf\"] source: |undefined|",
            "key: || value: [1,[\"barf\"]] source: |undefined|",
        ], log);
    }

    [Fact]
    public void ReviverDeletesAndReplaces()
    {
        using var realm = new TestRealm();
        var reviver = realm.CreateFunction((thisValue, arguments) =>
        {
            var key = JsOperations.ToString(arguments[0]);
            return key switch
            {
                "drop" or "1" => JsValue.Undefined,
                "double" => arguments[1].AsNumber() * 2,
                _ => arguments[1],
            };
        });

        var result = realm.Call("JSON.parse", "{\"drop\":1,\"double\":21,\"arr\":[1,2,3]}", reviver);
        Assert.Equal("{\"double\":42,\"arr\":[1,null,3]}", Stringify(realm, result));
        Assert.False(result.AsObject().Get(new PropertyKey("arr")).AsObject().HasOwnProperty(new PropertyKey(1)));
    }

    [Fact]
    public void StringifyIndentation()
    {
        using var realm = new TestRealm();
        DefineStringToString(realm);
        var value = Parse(realm, "{\"a\":[1,{\"b\":2}],\"c\":\"x\",\"e\":[],\"f\":{}}");
        Assert.Equal("{\n  \"a\": [\n    1,\n    {\n      \"b\": 2\n    }\n  ],\n  \"c\": \"x\",\n  \"e\": [],\n  \"f\": {}\n}", Stringify(realm, value, JsValue.Null, 2));
        Assert.Equal("{\n----------\"a\": [\n--------------------1,\n--------------------{\n------------------------------\"b\": 2\n--------------------}\n----------],\n----------\"c\": \"x\",\n----------\"e\": [],\n----------\"f\": {}\n}", Stringify(realm, value, JsValue.Null, "--------------xx"));
        Assert.Equal(Stringify(realm, value, JsValue.Null, 10), Stringify(realm, value, JsValue.Null, 100));
        Assert.Equal(Stringify(realm, value), Stringify(realm, value, JsValue.Null, 0.9));
        Assert.Equal(Stringify(realm, value, JsValue.Null, 3), Stringify(realm, value, JsValue.Null, JsOperations.ToObject(3)));
        Assert.Equal(Stringify(realm, value, JsValue.Null, "ab"), Stringify(realm, value, JsValue.Null, JsOperations.ToObject("ab")));
        Assert.Equal(Stringify(realm, value), Stringify(realm, value, JsValue.Null, true));
    }

    [Fact]
    public void StringifyStrings()
    {
        using var realm = new TestRealm();
        Assert.Equal("\"\ud800\udc00\\ud800 \\udc00\\u0001\\u001f\\\"\\\\/\\b\\f\\n\\r\\t\u2028\"", Stringify(realm, "\ud800\udc00\ud800 \udc00\u0001\u001f\"\\/\b\f\n\r\t\u2028"));
    }

    [Fact]
    public void StringifyValues()
    {
        using var realm = new TestRealm();
        DefineStringToString(realm);
        var function = realm.CreateFunction((_, _) => JsValue.Undefined);
        Assert.Equal("[null,null,null,0,null,1e+21,5e-324]", Stringify(realm, realm.CreateArray(JsValue.Undefined, function, double.NaN, -0.0, double.PositiveInfinity, 1e21, 5e-324)));
        Assert.True(realm.Call("JSON.stringify", JsValue.Undefined).IsUndefined);
        Assert.True(realm.Call("JSON.stringify", function).IsUndefined);
        Assert.True(realm.Call("JSON.stringify", new JsSymbol("s")).IsUndefined);
        Assert.Equal("{\"a\":1}", Stringify(realm, realm.CreateObject(("a", 1), ("b", JsValue.Undefined), ("c", function))));
        Assert.Equal("[1,\"s\",true,10]", Stringify(realm, realm.CreateArray(JsOperations.ToObject(1), JsOperations.ToObject("s"), JsOperations.ToObject(true), realm.CreateObject(("toJSON", realm.CreateFunction((_, _) => 10))))));
        Assert.Equal("{\"x\":1e1000,\"y\":[\"a\"]}", Stringify(realm, realm.CreateObject(("x", realm.Call("JSON.rawJSON", "1e1000")), ("y", realm.CreateArray(realm.Call("JSON.rawJSON", "\"a\""))))));

        // The key is passed to toJSON
        var toJson = realm.CreateFunction((_, arguments) => "key:" + JsOperations.ToString(arguments[0]));
        Assert.Equal("{\"k\":\"key:k\"}", Stringify(realm, realm.CreateObject(("k", realm.CreateObject(("toJSON", toJson))))));
    }

    [Fact]
    public void StringifyBigInt()
    {
        using var realm = new TestRealm();
        Assert.Equal("TypeError", realm.GetErrorName(() => Stringify(realm, JsValue.FromBigInt(1))));
        Assert.Equal("TypeError", realm.GetErrorName(() => Stringify(realm, JsOperations.ToObject(JsValue.FromBigInt(1)))));
        Assert.Equal("{\"a\":1}", Stringify(realm, realm.CreateObject(("a", realm.Call("JSON.rawJSON", JsValue.FromBigInt(1))))));

        realm.Get("BigInt.prototype").AsObject().CreateDataPropertyOrThrow(new PropertyKey("toJSON"), realm.CreateFunction((thisValue, _) => JsOperations.ToString(thisValue)));
        Assert.Equal("[\"12\"]", Stringify(realm, realm.CreateArray(JsValue.FromBigInt(12))));
    }

    [Fact]
    public void StringifyReplacer()
    {
        using var realm = new TestRealm();
        DefineStringToString(realm);
        var value = realm.CreateObject(("b", 1), ("a", 2), ("1", 3), ("c", realm.CreateObject(("a", 4), ("z", 5))));
        Assert.Equal("{\"a\":2,\"1\":3,\"b\":1}", Stringify(realm, value, realm.CreateArray("a", 1, "b", "a", realm.CreateObject())));
        Assert.Equal("{\"a\":2,\"c\":{\"a\":4}}", Stringify(realm, value, realm.CreateArray(JsOperations.ToObject("a"), JsOperations.ToObject("c"))));

        var calls = new List<string>();
        var replacer = realm.CreateFunction((thisValue, arguments) =>
        {
            calls.Add(JsOperations.ToString(arguments[0]));
            return arguments[1].IsNumber ? arguments[1].AsNumber() * 10 : arguments[1];
        });
        Assert.Equal("{\"1\":30,\"b\":10,\"a\":20,\"c\":{\"a\":40,\"z\":50}}", Stringify(realm, value, replacer));
        Assert.Equal(["", "1", "b", "a", "c", "a", "z"], calls);
    }

    [Fact]
    public void StringifyCycles()
    {
        using var realm = new TestRealm();
        var obj = realm.CreateObject();
        obj.CreateDataPropertyOrThrow(new PropertyKey("self"), obj);
        Assert.Equal("TypeError", realm.GetErrorName(() => Stringify(realm, obj)));

        var array = realm.CreateArray();
        array.CreateDataPropertyOrThrow(new PropertyKey(0), array);
        Assert.Equal("TypeError", realm.GetErrorName(() => Stringify(realm, array)));

        // The same object twice is not a cycle
        var shared = realm.CreateObject(("x", 1));
        Assert.Equal("[{\"x\":1},{\"x\":1}]", Stringify(realm, realm.CreateArray(shared, shared)));
    }

    [Fact]
    public void RawJson()
    {
        using var realm = new TestRealm();
        var raw = realm.Call("JSON.rawJSON", 1).AsObject();
        Assert.Null(raw.GetPrototypeOf());
        Assert.False(raw.IsExtensible());
        Assert.True(raw.GetOwnProperty(new PropertyKey("rawJSON"), out var descriptor));
        Assert.Equal("1", descriptor.Value.AsString());
        Assert.Equal(PropertyFlags.Enumerable, descriptor.Flags);
        Assert.Single(raw.OwnPropertyKeys());

        Assert.True(realm.Call("JSON.isRawJSON", raw).AsBoolean());
        Assert.False(realm.Call("JSON.isRawJSON", realm.CreateObject(("rawJSON", "1"))).AsBoolean());
        Assert.False(realm.Call("JSON.isRawJSON").AsBoolean());

        Assert.Equal("null", realm.Call("JSON.rawJSON", JsValue.Null).AsObject().Get(new PropertyKey("rawJSON")).AsString());
        foreach (var invalid in new[] { "", " 1", "1 ", "\t1", "1\n", "\r1", "{}", "[]", "[1]", "1 2", "'a'", "undefined" })
        {
            Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("JSON.rawJSON", invalid)));
        }

        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("JSON.rawJSON", JsValue.Undefined)));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("JSON.rawJSON", new JsSymbol("s"))));
    }

    // The files of JSONTestSuite that are not UTF-8 text without a BOM: invalid UTF-8 sequences, UTF-16, a BOM. The suite
    // tests how a parser handles these bytes, but a host decodes them before JSON.parse sees them (the invalid sequences
    // become U+FFFD and the BOM is removed), so these files only test the byte decoding.
    private static readonly HashSet<string> JsonTestSuiteDecodingOnlyFiles = new(StringComparer.Ordinal)
    {
        "i_string_UTF-16LE_with_BOM.json",
        "i_string_UTF-8_invalid_sequence.json",
        "i_string_UTF8_surrogate_U+D800.json",
        "i_string_invalid_utf-8.json",
        "i_string_iso_latin_1.json",
        "i_string_lone_utf8_continuation_byte.json",
        "i_string_not_in_unicode_range.json",
        "i_string_overlong_sequence_2_bytes.json",
        "i_string_overlong_sequence_6_bytes.json",
        "i_string_overlong_sequence_6_bytes_null.json",
        "i_string_truncated-utf-8.json",
        "i_string_utf16BE_no_BOM.json",
        "i_string_utf16LE_no_BOM.json",
        "i_structure_UTF-8_BOM_empty_object.json",
        "n_array_a_invalid_utf8.json",
        "n_array_invalid_utf8.json",
        "n_number_invalid-utf-8-in-bigger-int.json",
        "n_number_invalid-utf-8-in-exponent.json",
        "n_number_invalid-utf-8-in-int.json",
        "n_number_real_with_invalid_utf8_after_e.json",
        "n_object_lone_continuation_byte_in_key_and_trailing_comma.json",
        "n_string_invalid-utf-8-in-escape.json",
        "n_string_invalid_utf8_after_escape.json",
        "n_structure_UTF8_BOM_no_data.json",
        "n_structure_incomplete_UTF8_BOM.json",
        "n_structure_lone-invalid-utf-8.json",
        "n_structure_single_eacute.json",
    };

    /// <summary>
    /// Parses the files of JSONTestSuite (<c>JSONTestSuite/README.md</c>): the <c>y_</c> files must parse, the <c>n_</c>
    /// files must throw a SyntaxError, and every result must be the one V8 gives (<c>Corpus/JSONTestSuiteV8.md</c>), which
    /// sets the result of the implementation-defined <c>i_</c> files. The values of the <c>y_</c> files must also survive
    /// JSON.stringify and JSON.parse.
    /// </summary>
    [Fact]
    public void JsonTestSuite_MatchesV8()
    {
        const string ResourcePrefix = "JSONTestSuite.";
        var assembly = typeof(JsonBuiltinsTests).Assembly;
        var expectations = new Dictionary<string, string?>(StringComparer.Ordinal);
        using (var stream = assembly.GetManifestResourceStream("Meziantou.Framework.JavaScriptEngine.Tests.Corpus.JSONTestSuiteV8.jsonl")!)
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                    continue;

                using var document = System.Text.Json.JsonDocument.Parse(line);
                var root = document.RootElement;
                expectations.Add(root.GetProperty("f").GetString()!, root.TryGetProperty("r", out var result) ? result.GetString() : null);
            }
        }

        using var realm = new TestRealm();
        realm.Realm.Execute("""
            // JSON.stringify writes null for the infinite numbers and 0 for -0: write them as { number: '...' }
            const replacer = (key, value) => typeof value === 'number' && (!Number.isFinite(value) || Object.is(value, -0)) ? { number: Object.is(value, -0) ? '-0' : String(value) } : value;

            // -0 becomes 0 through JSON.stringify: the numbers are compared with ===. The loops are not callbacks of
            // built-ins (Array.prototype.every): each call of a callback uses much more of the .NET stack than a call
            // of a function, and i_structure_500_nested_arrays.json is 500 levels deep.
            function same(a, b) {
              if (typeof a !== 'object' || a === null || typeof b !== 'object' || b === null) {
                return a === b;
              }

              if (Array.isArray(a) !== Array.isArray(b) || Object.getPrototypeOf(a) !== Object.getPrototypeOf(b)) {
                return false;
              }

              const keysA = Object.keys(a);
              const keysB = Object.keys(b);
              if (keysA.length !== keysB.length) {
                return false;
              }

              for (let i = 0; i < keysA.length; i++) {
                if (keysA[i] !== keysB[i] || !same(a[keysA[i]], b[keysB[i]])) {
                  return false;
                }
              }

              return true;
            }

            function parse(text, reviver) {
              try {
                return [JSON.parse(text, reviver)];
              } catch (e) {
                if (e instanceof SyntaxError) {
                  return undefined;
                }

                throw e;
              }
            }

            // The value as V8 records it (undefined for a SyntaxError), and whether it survives JSON.stringify and JSON.parse.
            // With a reviver, the parser also records the source of the values (a code path of its own): the reviver must
            // see the same values, and the source of the primitive values.
            globalThis.parseJsonTestSuiteFile = function (text) {
              let sourcesMatch = true;
              const revived = parse(text, function (key, value, context) {
                if (typeof value !== 'object' || value === null) {
                  sourcesMatch &&= typeof context.source === 'string' && Object.is(JSON.parse(context.source), value);
                }

                return value;
              });
              const parsed = parse(text);
              if (parsed === undefined) {
                return revived === undefined ? undefined : 'parsed with a reviver only';
              }

              if (revived === undefined || !same(parsed[0], revived[0]) || !sourcesMatch) {
                return 'different with a reviver';
              }

              const value = parsed[0];
              return [JSON.stringify(value, replacer), same(value, JSON.parse(JSON.stringify(value)))];
            };
            """);

        var names = assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Select(name => name[ResourcePrefix.Length..]).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expectations.Keys.Order(StringComparer.Ordinal), names);

        var failures = new List<string>();
        var decodingOnlyFiles = new List<string>();
        foreach (var name in names)
        {
            byte[] bytes;
            using (var stream = assembly.GetManifestResourceStream(ResourcePrefix + name)!)
            {
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }

            // The UTF-8 decode of the Encoding Standard, as TextDecoder and fetch do: the BOM is removed and the invalid
            // sequences become U+FFFD
            var hasBom = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
            if (hasBom || !System.Text.Unicode.Utf8.IsValid(bytes))
            {
                decodingOnlyFiles.Add(name);
            }

            var text = Encoding.UTF8.GetString(bytes.AsSpan(hasBom ? 3 : 0));
            var expected = expectations[name];
            if ((name.StartsWith("y_", StringComparison.Ordinal) && expected is null) || (name.StartsWith("n_", StringComparison.Ordinal) && expected is not null))
            {
                failures.Add($"{name}: V8 does not give the result of the suite");
            }

            JsValue result;
            try
            {
                result = realm.Call("parseJsonTestSuiteFile", text);
            }
            catch (JsException ex)
            {
                // No call of a function here: the catch block runs on top of the frames of the exception, which may be a
                // RangeError of a full stack
                failures.Add($"{name}: {JsOperations.ToString(ex.Value.AsObject().Get(new PropertyKey("name")))}: {JsOperations.ToString(ex.Value.AsObject().Get(new PropertyKey("message")))}");
                continue;
            }

            if (result.IsUndefined)
            {
                if (expected is not null)
                {
                    failures.Add($"{name}: unexpected SyntaxError, expected {expected}");
                }

                continue;
            }

            if (result.IsString)
            {
                failures.Add($"{name}: {result.AsString()}");
                continue;
            }

            var actual = result.AsObject().Get(new PropertyKey(0)).AsString();
            if (expected is null)
            {
                failures.Add($"{name}: expected a SyntaxError, got {actual}");
            }
            else if (actual != expected)
            {
                failures.Add($"{name}: expected {expected}, got {actual}");
            }

            if (name.StartsWith("y_", StringComparison.Ordinal) && !result.AsObject().Get(new PropertyKey(1)).AsBoolean())
            {
                failures.Add($"{name}: the value changes through JSON.stringify and JSON.parse");
            }
        }

        Assert.HasCount(318, names);
        Assert.Equal(JsonTestSuiteDecodingOnlyFiles.Order(StringComparer.Ordinal), decodingOnlyFiles);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("const o = { a: 1, get b() { delete this.c; this.d = 4; return 2; }, c: 3 }; JSON.stringify(o)", "{\"a\":1,\"b\":2}")]
    [InlineData("const o = { get a() { this.b = 5; return 1; }, b: 2 }; JSON.stringify(o)", "{\"a\":1,\"b\":5}")]
    [InlineData("const o = { get a() { Object.defineProperty(this, 'b', { get() { return 'g'; } }); return 1; }, b: 2 }; JSON.stringify(o)", "{\"a\":1,\"b\":\"g\"}")]
    [InlineData("const o = { a: 1, b: 2 }; Object.defineProperty(o, 'h', { value: 3, enumerable: false }); o[Symbol('s')] = 4; JSON.stringify(o)", "{\"a\":1,\"b\":2}")]
    [InlineData("JSON.stringify({ b: 1, 2: 2, a: 3, 1: 4 })", "{\"1\":4,\"2\":2,\"b\":1,\"a\":3}")]
    [InlineData("JSON.stringify([1, , 3, undefined, () => 1, Symbol()])", "[1,null,3,null,null,null]")]
    [InlineData("const a = [1, 2, 3]; Object.defineProperty(a, 1, { get() { a.length = 1; return 'g'; } }); JSON.stringify(a)", "[1,\"g\",null]")]
    [InlineData("const a = [1, 2]; a.toJSON = () => 'own'; JSON.stringify([a, [3]])", "[\"own\",[3]]")]
    [InlineData("class P { constructor(x) { this.x = x; } toJSON(key) { return key + ':' + this.x; } } JSON.stringify({ a: new P(1), b: [new P(2)], c: { x: 3 } })", "{\"a\":\"a:1\",\"b\":[\"0:2\"],\"c\":{\"x\":3}}")]
    [InlineData("const o = Object.create(null); o.a = 1; JSON.stringify([o, Object.create(o)])", "[{\"a\":1},{}]")]
    [InlineData("JSON.stringify(Array.from({ length: 3 }, (_, i) => ({ a: i, b: 'x' + i })), (k, v) => k === 'b' ? undefined : v)", "[{\"a\":0},{\"a\":1},{\"a\":2}]")]
    [InlineData("JSON.stringify([{ a: 1, b: 2 }, { a: 3, b: 4 }], ['b'])", "[{\"b\":2},{\"b\":4}]")]
    [InlineData("JSON.stringify([{ a: [1, { b: 2 }] }], null, 2)", "[\n  {\n    \"a\": [\n      1,\n      {\n        \"b\": 2\n      }\n    ]\n  }\n]")]
    [InlineData("JSON.stringify({ a: { toJSON() { return JSON.stringify({ inner: [1, 'x'] }); } }, b: 2 })", "{\"a\":\"{\\\"inner\\\":[1,\\\"x\\\"]}\",\"b\":2}")]
    public void StringifyFastPathEdgeCases(string script, string expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(expected, realm.Realm.Execute(script).AsString());
    }

    [Fact]
    public void StringifyToJsonAddedDuringSerialization()
    {
        using var realm = new TestRealm();

        // A getter adds toJSON to the prototypes after the objects and arrays before it were serialized without one
        Assert.Equal("[[0],{\"y\":1},\"array\",\"object\",\"array\",\"object\"]", realm.Realm.Execute("""
            const data = [
              [0],
              { get y() { Object.prototype.toJSON = () => 'object'; Array.prototype.toJSON = () => 'array'; return 1; } },
              [2],
              { z: 3 },
              [4],
              { z: 5 },
            ];
            let result;
            try {
              result = JSON.stringify(data);
            } finally {
              delete Object.prototype.toJSON;
              delete Array.prototype.toJSON;
            }

            result;
            """).AsString());

        // The prototype of an object changes during the serialization
        Assert.Equal("[{},{\"a\":1},\"proto\"]", realm.Realm.Execute("""
            const items = [{}, { get a() { Object.setPrototypeOf(items[2], { toJSON: () => 'proto' }); return 1; } }, {}];
            JSON.stringify(items);
            """).AsString());

        // After the first serialization, the next one sees a toJSON added to a prototype between them
        Assert.Equal("[{\"a\":1}] \"t\" [{\"a\":1}]", realm.Realm.Execute("""
            const values = [{ a: 1 }];
            const first = JSON.stringify(values);
            Object.prototype.toJSON = () => 't';
            const second = JSON.stringify(values);
            delete Object.prototype.toJSON;
            first + ' ' + second + ' ' + JSON.stringify(values);
            """).AsString());
    }

    [Fact]
    public void StringifyProxies()
    {
        using var realm = new TestRealm();
        Assert.Equal("[{\"a\":\"A\",\"b\":\"B\"},[\"0!\",\"1!\"]] get:toJSON,ownKeys,get:a,get:b,get:toJSON,get:length,get:0,get:1", realm.Realm.Execute("""
            const log = [];
            const target = { a: 1, b: 2 };
            const proxy = new Proxy(target, {
              get(t, key) { log.push('get:' + String(key)); return key === 'toJSON' ? undefined : String(key).toUpperCase(); },
              ownKeys(t) { log.push('ownKeys'); return Reflect.ownKeys(t); },
            });
            const arrayProxy = new Proxy([1, 2], {
              get(t, key) { log.push('get:' + String(key)); return key === 'length' ? 2 : key === 'toJSON' ? undefined : key + '!'; },
            });
            JSON.stringify([proxy, arrayProxy]) + ' ' + log.join(',');
            """).AsString());
    }

    [Fact]
    public void StringifyEscapingAndLongText()
    {
        using var realm = new TestRealm();
        Assert.Equal("\"\\ud800a\ud83d\ude00\\udc00\\u0001\\b\\t\\n\\f\\r\\\"\\\\\u2028\"", realm.Realm.Execute("JSON.stringify('\\ud800a\\ud83d\\ude00\\udc00\\u0001\\b\\t\\n\\f\\r\"\\\\\\u2028')").AsString());

        // A text longer than the initial buffer, with escapes at the boundaries of the copied runs
        var text = realm.Realm.Execute("""
            const long = 'a'.repeat(1000) + '"' + 'b'.repeat(300) + '\n';
            JSON.stringify(Array.from({ length: 50 }, (_, i) => ({ i, long })));
            """).AsString();
        var expectedItem = "\"long\":\"" + new string('a', 1000) + "\\\"" + new string('b', 300) + "\\n\"}";
        Assert.Equal(50, CountOccurrences(text, expectedItem));
        Assert.StartsWith("[{\"i\":0,\"long\":\"aaa", text, StringComparison.Ordinal);
        Assert.EndsWith(expectedItem + "]", text, StringComparison.Ordinal);

        static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }

    [Theory]
    [InlineData("""[{"a":1,"b":2},{"b":3,"a":4},{"a":5,"a":6,"b":7},{"a":8,"b":9,"c":10},{"a":11},{"a":12,"b":13}]""", """[{"a":1,"b":2},{"b":3,"a":4},{"a":6,"b":7},{"a":8,"b":9,"c":10},{"a":11},{"a":12,"b":13}]""")]
    [InlineData("""[{"id":1,"child":{"id":2,"name":"x"}},{"id":3,"child":{"name":"y","id":4}},{"id":5,"child":{"id":6,"name":"z"}}]""", """[{"id":1,"child":{"id":2,"name":"x"}},{"id":3,"child":{"name":"y","id":4}},{"id":5,"child":{"id":6,"name":"z"}}]""")]
    [InlineData("""[{"a":1,"0":2},{"a":3,"0":4,"1":5}]""", """[{"0":2,"a":1},{"0":4,"1":5,"a":3}]""")]
    [InlineData("""[{"ab":0},{"a\u0062":1,"ab":2},{"ab":3,"ab":4}]""", """[{"ab":0},{"ab":2},{"ab":4}]""")]
    [InlineData("""[[1,2.5],[true,"s",null],[1,"a"],[],[{}],[-0]]""", """[[1,2.5],[true,"s",null],[1,"a"],[],[{}],[0]]""")]
    public void ParseRepeatedShapes(string json, string expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(expected, Stringify(realm, Parse(realm, json)));
    }

    [Fact]
    public void ParseRepeatedShapesAreIndependentObjects()
    {
        using var realm = new TestRealm();
        Assert.Equal("""[{"a":1,"b":2,"c":3},{"a":4},{"b":6,"a":5}] a,b,c|a|b,a [1,2,"x"] true""", realm.Realm.Execute("""
            const items = JSON.parse('[{"a":1,"b":2},{"a":4,"b":5},{"a":5,"b":6}]');
            items[0].c = 3;
            delete items[1].b;
            delete items[2].a;
            items[2].a = 5;
            const numbers = JSON.parse('[1,2]');
            numbers.push('x');
            JSON.stringify(items) + ' ' + items.map(o => Object.keys(o).join()).join('|') + ' ' + JSON.stringify(numbers) + ' ' + Array.isArray(numbers);
            """).AsString());
    }

    [Fact]
    public void ParseAfterError()
    {
        using var realm = new TestRealm();
        Assert.Equal("SyntaxError", realm.GetErrorName(() => Parse(realm, "[1,[2,[3,{\"a\":[4,")));
        Assert.Equal("[5,[6]]", Stringify(realm, Parse(realm, "[5,[6]]")));
    }

    [Fact]
    public void ParseLargeArraysOfEachKind()
    {
        using var realm = new TestRealm();

        // Above 5312 elements, the storage is rounded to a power of two (the size of the pooled storages)
        Assert.Equal("6000 5999 s 6001 true|6000 5999 5999 6001 x|6000 s0 s5999 6001 1", realm.Realm.Execute("""
            const n = 6000;
            const mixed = JSON.parse('[' + Array.from({ length: n }, (_, i) => i % 2 ? i : '"s"').join() + ']');
            const numbers = JSON.parse('[' + Array.from({ length: n }, (_, i) => i).join() + ']');
            const strings = JSON.parse('[' + Array.from({ length: n }, (_, i) => '"s' + i + '"').join() + ']');
            mixed.push(true);
            numbers.push('x');
            strings.push(1);
            [
              [mixed.length - 1, mixed[n - 1], mixed[n - 2], mixed.length, mixed[n]].join(' '),
              [numbers.length - 1, numbers[n - 1], numbers.lastIndexOf(n - 1), numbers.length, numbers[n]].join(' '),
              [strings.length - 1, strings[0], strings[n - 1], strings.length, strings[n]].join(' '),
            ].join('|');
            """).AsString());
    }

    [Fact]
    public void JsonObject()
    {
        using var realm = new TestRealm();
        var json = realm.Get("JSON").AsObject();
        Assert.Equal("JSON", json.Get(new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
        Assert.Equal(2, realm.Get("JSON.parse.length").AsNumber());
        Assert.Equal(3, realm.Get("JSON.stringify.length").AsNumber());
        Assert.Equal(1, realm.Get("JSON.rawJSON.length").AsNumber());
        Assert.Equal(1, realm.Get("JSON.isRawJSON.length").AsNumber());
    }
}
