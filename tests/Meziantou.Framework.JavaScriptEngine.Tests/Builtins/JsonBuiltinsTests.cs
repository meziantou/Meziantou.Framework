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
