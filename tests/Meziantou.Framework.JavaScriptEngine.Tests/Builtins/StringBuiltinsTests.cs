namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class StringBuiltinsTests
{
    [Fact]
    public void Constructor_CalledAndConstructed()
    {
        using var context = new BuiltinsTestContext();
        var stringConstructor = context.Global("String");
        Assert.Equal("", stringConstructor.Call(JsValue.Undefined, []).AsString());
        Assert.Equal("12", stringConstructor.Call(JsValue.Undefined, [12]).AsString());

        var symbol = new JsSymbol("desc");
        Assert.Equal("Symbol(desc)", stringConstructor.Call(JsValue.Undefined, [symbol]).AsString());
        context.AssertThrows("TypeError", () => JsOperations.Construct(stringConstructor, [symbol]));

        var wrapper = JsOperations.Construct(stringConstructor, ["abc"]).AsObject();
        Assert.Equal(ObjectClass.String, wrapper.Class);
        Assert.Equal(3, wrapper.Get(new PropertyKey("length")).AsNumber());
        Assert.Equal("b", wrapper.Get(new PropertyKey(1)).AsString());
        Assert.Same(context.Realm.Intrinsics.StringPrototype, wrapper.GetPrototypeOf());
    }

    [Fact]
    public void FromCharCodeAndFromCodePoint()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("AB\uFFFF\u0000", context.CallStatic("String", "fromCharCode", 65, 65602, -1, 65536).AsString());
        Assert.Equal("a\U0001F600", context.CallStatic("String", "fromCodePoint", 97, 0x1F600).AsString());
        context.AssertThrows("RangeError", () => context.CallStatic("String", "fromCodePoint", 0x110000));
        context.AssertThrows("RangeError", () => context.CallStatic("String", "fromCodePoint", 1.5));
        context.AssertThrows("RangeError", () => context.CallStatic("String", "fromCodePoint", -1));
        context.AssertThrows("RangeError", () => context.CallStatic("String", "fromCodePoint", double.NaN));
    }

    [Fact]
    public void Raw()
    {
        using var context = new BuiltinsTestContext();
        var template = context.Object();
        template.CreateDataPropertyOrThrow("raw", context.Array("a", "b", "c"));
        Assert.Equal("a1b2c", context.CallStatic("String", "raw", template, 1, 2, 3).AsString());
        Assert.Equal("abc", context.CallStatic("String", "raw", template).AsString());

        var empty = context.Object();
        empty.CreateDataPropertyOrThrow("raw", context.Object());
        Assert.Equal("", context.CallStatic("String", "raw", empty).AsString());
    }

    [Theory]
    [InlineData("abc", 0, "a")]
    [InlineData("abc", -1, "c")]
    [InlineData("abc", 3, "undefined")]
    [InlineData("abc", -4, "undefined")]
    public void At(string value, double index, string expected)
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal(expected, context.CallPrototype("String", "at", value, index).ToString());
    }

    [Fact]
    public void CodePoints()
    {
        using var context = new BuiltinsTestContext();
        const string Value = "a\U0001F600\uD800";
        Assert.Equal(0x1F600, context.CallPrototype("String", "codePointAt", Value, 1).AsNumber());
        Assert.Equal(0xDE00, context.CallPrototype("String", "codePointAt", Value, 2).AsNumber());
        Assert.Equal(0xD800, context.CallPrototype("String", "codePointAt", Value, 3).AsNumber());
        Assert.True(context.CallPrototype("String", "codePointAt", Value, 4).IsUndefined);
        Assert.Equal(0xD83D, context.CallPrototype("String", "charCodeAt", Value, 1).AsNumber());
        Assert.True(double.IsNaN(context.CallPrototype("String", "charCodeAt", Value, 9).AsNumber()));
        Assert.Equal("", context.CallPrototype("String", "charAt", Value, -1).AsString());

        Assert.False(context.CallPrototype("String", "isWellFormed", Value).AsBoolean());
        Assert.True(context.CallPrototype("String", "isWellFormed", "a\U0001F600").AsBoolean());
        Assert.Equal("a\U0001F600\uFFFD\uFFFDb", context.CallPrototype("String", "toWellFormed", "a\U0001F600\uDC00\uD800b").AsString());

        var symbolIterator = context.Realm.Intrinsics.StringPrototype.Get(new PropertyKey(WellKnownSymbols.Iterator)).AsObject();
        var values = BuiltinsTestContext.Iterate(symbolIterator.Call(Value, []));
        Assert.Equal(["a", "\U0001F600", "\uD800"], values.Select(v => v.AsString()).ToArray());
    }

    [Fact]
    public void SearchMethods()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal(3, context.CallPrototype("String", "indexOf", "abcabc", "a", 1).AsNumber());
        Assert.Equal(6, context.CallPrototype("String", "indexOf", "abcabc", "", 10).AsNumber());
        Assert.Equal(3, context.CallPrototype("String", "lastIndexOf", "abcabc", "a").AsNumber());
        Assert.Equal(0, context.CallPrototype("String", "lastIndexOf", "abcabc", "a", 2).AsNumber());
        Assert.Equal(6, context.CallPrototype("String", "lastIndexOf", "abcabc", "").AsNumber());
        Assert.Equal(-1, context.CallPrototype("String", "lastIndexOf", "ab", "abc").AsNumber());
        Assert.Equal(3, context.CallPrototype("String", "lastIndexOf", "abcabc", "a", double.NaN).AsNumber());
        Assert.True(context.CallPrototype("String", "includes", "abcabc", "ca").AsBoolean());
        Assert.True(context.CallPrototype("String", "startsWith", "abcabc", "ca", 2).AsBoolean());
        Assert.True(context.CallPrototype("String", "endsWith", "abcabc", "ab", 5).AsBoolean());
        Assert.False(context.CallPrototype("String", "endsWith", "abcabc", "ab").AsBoolean());

        // IsRegExp: an object whose Symbol.match is truthy
        var regExpLike = context.Object();
        regExpLike.CreateDataPropertyOrThrow(WellKnownSymbols.Match, true);
        context.AssertThrows("TypeError", () => context.CallPrototype("String", "includes", "abc", regExpLike));
        context.AssertThrows("TypeError", () => context.CallPrototype("String", "includes", JsValue.Null, "a"));
    }

    [Fact]
    public void SliceSubstringSubstr()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("cd", context.CallPrototype("String", "slice", "abcdef", 2, -2).AsString());
        Assert.Equal("", context.CallPrototype("String", "slice", "abcdef", 4, 2).AsString());
        Assert.Equal("cd", context.CallPrototype("String", "substring", "abcdef", 4, 2).AsString());
        Assert.Equal("ab", context.CallPrototype("String", "substring", "abcdef", double.NaN, 2).AsString());
        Assert.Equal("ef", context.CallPrototype("String", "substr", "abcdef", -2).AsString());
        Assert.Equal("bc", context.CallPrototype("String", "substr", "abcdef", 1, 2).AsString());
        Assert.Equal("", context.CallPrototype("String", "substr", "abcdef", 1, -2).AsString());
    }

    [Fact]
    public void PadAndRepeat()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("abcab1", context.CallPrototype("String", "padStart", "1", 6, "abc").AsString());
        Assert.Equal("1abcab", context.CallPrototype("String", "padEnd", "1", 6, "abc").AsString());
        Assert.Equal("    1", context.CallPrototype("String", "padStart", "1", 5).AsString());
        Assert.Equal("1", context.CallPrototype("String", "padStart", "1", 5, "").AsString());
        context.AssertThrows("RangeError", () => context.CallPrototype("String", "padStart", "1", double.PositiveInfinity, "a"));

        Assert.Equal("ababab", context.CallPrototype("String", "repeat", "ab", 3).AsString());
        Assert.Equal("", context.CallPrototype("String", "repeat", "", 1e9).AsString());
        context.AssertThrows("RangeError", () => context.CallPrototype("String", "repeat", "ab", -1));
        context.AssertThrows("RangeError", () => context.CallPrototype("String", "repeat", "ab", double.PositiveInfinity));
        context.AssertThrows("RangeError", () => context.CallPrototype("String", "repeat", "ab", 1 << 30));
    }

    [Fact]
    public void Trim()
    {
        using var context = new BuiltinsTestContext();
        const string Value = "\uFEFF\u00A0\u2028 \t a b \u3000\n\r";
        Assert.Equal("a b", context.CallPrototype("String", "trim", Value).AsString());
        Assert.Equal("a b \u3000\n\r", context.CallPrototype("String", "trimStart", Value).AsString());
        Assert.Equal("\uFEFF\u00A0\u2028 \t a b", context.CallPrototype("String", "trimEnd", Value).AsString());

        // U+180E is not a white space anymore
        Assert.Equal("\u180Ea", context.CallPrototype("String", "trim", "\u180Ea").AsString());

        var prototype = context.Realm.Intrinsics.StringPrototype;
        Assert.Same(prototype.Get(new PropertyKey("trimStart")).AsObject(), prototype.Get(new PropertyKey("trimLeft")).AsObject());
        Assert.Same(prototype.Get(new PropertyKey("trimEnd")).AsObject(), prototype.Get(new PropertyKey("trimRight")).AsObject());
        Assert.Equal("trimStart", BuiltinsTestContext.Get(prototype.Get(new PropertyKey("trimLeft")), "name").AsString());
    }

    [Theory]
    [InlineData("Hello World", "hello world", "HELLO WORLD")]
    [InlineData("stra\u00DFe", "stra\u00DFe", "STRASSE")]
    [InlineData("\u0130", "i\u0307", "\u0130")]
    [InlineData("\uFB03", "\uFB03", "FFI")]
    [InlineData("\u0149", "\u0149", "\u02BCN")]
    [InlineData("\u1F80", "\u1F80", "\u1F08\u0399")]
    [InlineData("\U00010400", "\U00010428", "\U00010400")]
    public void CaseMappings(string value, string lower, string upper)
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal(lower, context.CallPrototype("String", "toLowerCase", value).AsString());
        Assert.Equal(upper, context.CallPrototype("String", "toUpperCase", value).AsString());
        Assert.Equal(lower, context.CallPrototype("String", "toLocaleLowerCase", value).AsString());
        Assert.Equal(upper, context.CallPrototype("String", "toLocaleUpperCase", value).AsString());
    }

    [Theory]
    [InlineData("\u03A3", "\u03C3")]
    [InlineData("A\u03A3", "a\u03C2")]
    [InlineData("A\u03A3B", "a\u03C3b")]
    [InlineData("A.\u03A3", "a.\u03C2")]
    [InlineData("A\u00AD\u03A3", "a\u00AD\u03C2")]
    [InlineData("\u0345\u03A3", "\u0345\u03C3")]
    [InlineData("\u0391\u0345\u03A3", "\u03B1\u0345\u03C2")]
    [InlineData("A\u03A3\u0345", "a\u03C2\u0345")]
    [InlineData("A\u03A3\u0345\u0391", "a\u03C3\u0345\u03B1")]
    [InlineData("A\u03A3.b", "a\u03C3.b")]
    [InlineData("\u2160\u03A3", "\u2170\u03C2")]
    [InlineData("\u039F\u0394\u03A5\u03A3\u03A3\u0395\u03A5\u03A3", "\u03BF\u03B4\u03C5\u03C3\u03C3\u03B5\u03C5\u03C2")]
    public void FinalSigma(string value, string expected)
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal(expected, context.CallPrototype("String", "toLowerCase", value).AsString());
    }

    [Fact]
    public void FinalSigma_SupplementaryCodePoints()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("\U0001D4A2\u03C2", context.CallPrototype("String", "toLowerCase", "\U0001D4A2\u03A3").AsString());
        Assert.Equal("a\U0001D242\u03C2", context.CallPrototype("String", "toLowerCase", "A\U0001D242\u03A3").AsString());
        Assert.Equal("a\u03C3\U0001D4A2", context.CallPrototype("String", "toLowerCase", "A\u03A3\U0001D4A2").AsString());
        Assert.Equal("\uD800a", context.CallPrototype("String", "toLowerCase", "\uD800A").AsString());
    }

    [Theory]
    [InlineData("\u1E9B\u0323", null, "\u1E9B\u0323")]
    [InlineData("\u1E9B\u0323", "NFC", "\u1E9B\u0323")]
    [InlineData("\u1E9B\u0323", "NFD", "\u017F\u0323\u0307")]
    [InlineData("\u1E9B\u0323", "NFKC", "\u1E69")]
    [InlineData("\u1E9B\u0323", "NFKD", "s\u0323\u0307")]
    [InlineData("e\u0301", "NFC", "\u00E9")]
    public void Normalize(string value, string? form, string expected)
    {
        using var context = new BuiltinsTestContext();
        var result = form is null ? context.CallPrototype("String", "normalize", value) : context.CallPrototype("String", "normalize", value, form);
        Assert.Equal(expected, result.AsString());
    }

    [Fact]
    public void Normalize_InvalidForm()
    {
        using var context = new BuiltinsTestContext();
        context.AssertThrows("RangeError", () => context.CallPrototype("String", "normalize", "a", "nfc"));
        Assert.Equal("\uD800", context.CallPrototype("String", "normalize", "\uD800", "NFKD").AsString());
    }

    [Fact]
    public void LocaleCompare()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal(0, context.CallPrototype("String", "localeCompare", "\u00E9", "e\u0301").AsNumber());
        Assert.Equal(-1, context.CallPrototype("String", "localeCompare", "a", "b").AsNumber());
        Assert.Equal(1, context.CallPrototype("String", "localeCompare", "b", "a").AsNumber());
    }

    [Fact]
    public void Html()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("<a name=\"x&quot;y\">t</a>", context.CallPrototype("String", "anchor", "t", "x\"y").AsString());
        Assert.Equal("<b>t</b>", context.CallPrototype("String", "bold", "t").AsString());
        Assert.Equal("<font size=\"undefined\">t</font>", context.CallPrototype("String", "fontsize", "t").AsString());
        context.AssertThrows("TypeError", () => context.CallPrototype("String", "big", JsValue.Undefined));
    }

    [Fact]
    public void ThisStringValue()
    {
        using var context = new BuiltinsTestContext();
        var wrapper = JsOperations.ToObject("abc");
        Assert.Equal("abc", context.CallPrototype("String", "valueOf", wrapper).AsString());
        Assert.Equal("abc", context.CallPrototype("String", "toString", "abc").AsString());
        context.AssertThrows("TypeError", () => context.CallPrototype("String", "valueOf", context.Object()));
    }
}
