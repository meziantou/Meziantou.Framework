namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class GlobalFunctionsTests
{
    [Fact]
    public void ValueProperties()
    {
        using var realm = new TestRealm();
        var global = realm.Realm.GlobalObject;
        Assert.True(global.GetOwnProperty(new PropertyKey("globalThis"), out var globalThis));
        Assert.Same(global, globalThis.Value.AsObject());
        Assert.Equal(PropertyFlags.Writable | PropertyFlags.Configurable, globalThis.Flags);

        foreach (var name in new[] { "NaN", "Infinity", "undefined" })
        {
            Assert.True(global.GetOwnProperty(new PropertyKey(name), out var descriptor));
            Assert.Equal(PropertyFlags.None, descriptor.Flags);
        }

        Assert.True(double.IsNaN(realm.Get("NaN").AsNumber()));
        Assert.Equal(double.PositiveInfinity, realm.Get("Infinity").AsNumber());
        Assert.True(realm.Get("undefined").IsUndefined);
    }

    [Theory]
    [InlineData("0x1f", null, 31.0)]
    [InlineData("  -12px", null, -12.0)]
    [InlineData("123", 37.0, double.NaN)]
    [InlineData("123", 1.0, double.NaN)]
    [InlineData("1e3", null, 1.0)]
    [InlineData("-0", null, -0.0)]
    [InlineData("zz", 36.0, 1295.0)]
    [InlineData("11111111111111111111111111111111111111111111111111111111111", 2.0, 576460752303423500.0)]
    [InlineData("12345678901234567890123", null, 1.2345678901234568e+22)]
    [InlineData("0x", null, double.NaN)]
    [InlineData("0x10", 16.0, 16.0)]
    [InlineData("0x10", 10.0, 0.0)]
    [InlineData("7fffffffffffffffffff", 16.0, 6.044629098073146e+23)]
    [InlineData("10", 4294967312.0, 16.0)]
    [InlineData("\u00A0\uFEFF\u2028 42", null, 42.0)]
    [InlineData("", null, double.NaN)]
    public void ParseInt(string value, double? radix, double expected)
    {
        using var realm = new TestRealm();
        var result = radix is { } r ? realm.Call("parseInt", value, r) : realm.Call("parseInt", value);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(result.AsNumber()));
    }

    [Theory]
    [InlineData("  .5e", 0.5)]
    [InlineData("-.e3", double.NaN)]
    [InlineData("1.e3x", 1000.0)]
    [InlineData("Infinityx", double.PositiveInfinity)]
    [InlineData("-Infinity", double.NegativeInfinity)]
    [InlineData("-0", -0.0)]
    [InlineData("0x10", 0.0)]
    [InlineData("1e1000", double.PositiveInfinity)]
    [InlineData("+infinity", double.NaN)]
    [InlineData("1_000", 1.0)]
    [InlineData("\n 3.14abc", 3.14)]
    [InlineData("-", double.NaN)]
    [InlineData(".", double.NaN)]
    public void ParseFloat(string value, double expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(realm.Call("parseFloat", value).AsNumber()));
    }

    [Fact]
    public void IsNaNAndIsFinite()
    {
        using var realm = new TestRealm();
        Assert.True(realm.Call("isNaN", "abc").AsBoolean());
        Assert.False(realm.Call("isNaN", "12").AsBoolean());
        Assert.True(realm.Call("isFinite", "12").AsBoolean());
        Assert.False(realm.Call("isFinite", double.PositiveInfinity).AsBoolean());
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("isNaN", JsValue.FromBigInt(1))));
    }

    [Fact]
    public void UriFunctions()
    {
        using var realm = new TestRealm();
        Assert.Equal("a%20b%26%C3%A9%F0%9F%98%80", realm.Call("encodeURIComponent", "a b&\u00E9\U0001F600").AsString());
        Assert.Equal("http://x/?a=b#c%20d", realm.Call("encodeURI", "http://x/?a=b#c d").AsString());
        Assert.Equal("%3B%2FA\u00E9", realm.Call("decodeURI", "%3B%2F%41%C3%A9").AsString());
        Assert.Equal(";/A\u00E9", realm.Call("decodeURIComponent", "%3B%2F%41%C3%A9").AsString());
        Assert.Equal("\U0001F600", realm.Call("decodeURIComponent", "%F0%9F%98%80").AsString());
        Assert.Equal("abc", realm.Call("decodeURIComponent", "abc").AsString());

        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("encodeURIComponent", "\ud800")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("encodeURIComponent", "\udc00a")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%G0")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%80")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%C0%80"))); // overlong
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%ED%A0%80"))); // surrogate
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%F4%90%80%80"))); // > U+10FFFF
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%C3%41")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%C3")));
        Assert.Equal("URIError", realm.GetErrorName(() => realm.Call("decodeURIComponent", "%F8%80%80%80")));
    }

    [Fact]
    public void EscapeAndUnescape()
    {
        using var realm = new TestRealm();
        Assert.Equal("a%20b%E9%u0100@*_+-./", realm.Call("escape", "a b\u00E9\u0100@*_+-./").AsString());
        Assert.Equal("AA%zz%u00", realm.Call("unescape", "%u0041%41%zz%u00").AsString());
        Assert.Equal("%u004", realm.Call("unescape", "%u004").AsString());
        Assert.Equal("\u0004", realm.Call("unescape", "%04").AsString());
    }
}
