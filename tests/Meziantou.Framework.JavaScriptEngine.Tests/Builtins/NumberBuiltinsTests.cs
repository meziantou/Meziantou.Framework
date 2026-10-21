using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class NumberBuiltinsTests
{
    // The expected values are the outputs of Node.js (V8)
    [Theory]
    [InlineData(1e21, 2, "1e+21")]
    [InlineData(1000000000000000128, 0, "1000000000000000128")]
    [InlineData(0.5, 0, "1")]
    [InlineData(1.5, 0, "2")]
    [InlineData(2.5, 0, "3")]
    [InlineData(-1.5, 0, "-2")]
    [InlineData(1.005, 2, "1.00")]
    [InlineData(1.45, 1, "1.4")]
    [InlineData(-0.0, 2, "0.00")]
    [InlineData(-0.0000001, 2, "-0.00")]
    [InlineData(0.000001, 7, "0.0000010")]
    [InlineData(123.456, 100, "123.4560000000000030695446184836328029632568359375000000000000000000000000000000000000000000000000000000")]
    [InlineData(5e-324, 100, "0.0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(double.NaN, 2, "NaN")]
    [InlineData(double.NegativeInfinity, 2, "-Infinity")]
    public void ToFixed(double value, int fractionDigits, string expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(expected, realm.CallWithThis(value, "Number.prototype.toFixed", fractionDigits).AsString());
    }

    [Theory]
    [InlineData(0.0, null, "0e+0")]
    [InlineData(0.0, 2, "0.00e+0")]
    [InlineData(123456.0, 2, "1.23e+5")]
    [InlineData(1.25, 1, "1.3e+0")]
    [InlineData(1.35, 1, "1.4e+0")]
    [InlineData(5e-324, null, "5e-324")]
    [InlineData(1.7976931348623157e308, 20, "1.79769313486231570815e+308")]
    [InlineData(-6.9e-11, 4, "-6.9000e-11")]
    [InlineData(123.456, null, "1.23456e+2")]
    public void ToExponential(double value, int? fractionDigits, string expected)
    {
        using var realm = new TestRealm();
        var result = fractionDigits is { } f ? realm.CallWithThis(value, "Number.prototype.toExponential", f) : realm.CallWithThis(value, "Number.prototype.toExponential");
        Assert.Equal(expected, result.AsString());
    }

    [Theory]
    [InlineData(0.0, 1, "0")]
    [InlineData(0.0, 5, "0.0000")]
    [InlineData(123.456, 4, "123.5")]
    [InlineData(0.000001234, 2, "0.0000012")]
    [InlineData(0.0000001234, 2, "1.2e-7")]
    [InlineData(123456789.0, 3, "1.23e+8")]
    [InlineData(1e21, 3, "1.00e+21")]
    [InlineData(25.0, 1, "3e+1")]
    [InlineData(35.0, 1, "4e+1")]
    [InlineData(-1.5, 1, "-2")]
    [InlineData(5e-324, 100, "4.940656458412465441765687928682213723650598026143247644255856825006755072702087518652998363616359924e-324")]
    public void ToPrecision(double value, int precision, string expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(expected, realm.CallWithThis(value, "Number.prototype.toPrecision", precision).AsString());
    }

    [Theory]
    [InlineData(255.0, 16, "ff")]
    [InlineData(-255.0, 2, "-11111111")]
    [InlineData(0.5, 2, "0.1")]
    [InlineData(0.1, 3, "0.0022002200220022002200220022002201")]
    [InlineData(Math.PI, 36, "3.53i5ab8p5f")]
    [InlineData(1e21, 36, "5v1j4f4ds7c000")]
    [InlineData(1152921504608081664.0, 7, "2031000661631354426200")]
    [InlineData(-0.0, 2, "0")]
    [InlineData(0.000001, 16, "0.000010c6f7a0b5ed8d")]
    [InlineData(1.7976931348623157e308, 36, "1a1e4vngaiqo0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(double.NaN, 36, "NaN")]
    [InlineData(double.PositiveInfinity, 2, "Infinity")]
    [InlineData(123.456, 10, "123.456")]
    public void ToStringRadix(double value, int radix, string expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(expected, realm.CallWithThis(value, "Number.prototype.toString", radix).AsString());
    }

    [Fact]
    public void ToStringRadix2Subnormal()
    {
        using var realm = new TestRealm();
        Assert.Equal("0." + new string('0', 1073) + "1", realm.CallWithThis(5e-324, "Number.prototype.toString", 2).AsString());
    }

    [Fact]
    public void ArgumentRanges()
    {
        using var realm = new TestRealm();
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(1, "Number.prototype.toFixed", 101)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(1, "Number.prototype.toFixed", double.PositiveInfinity)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(double.NaN, "Number.prototype.toFixed", -1)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(1, "Number.prototype.toExponential", -1)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(1, "Number.prototype.toPrecision", 0)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(1, "Number.prototype.toString", 1)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(1, "Number.prototype.toString", 37)));

        // A non-finite number is formatted before the range of the argument is checked
        Assert.Equal("Infinity", realm.CallWithThis(double.PositiveInfinity, "Number.prototype.toExponential", 1000).AsString());
        Assert.Equal("NaN", realm.CallWithThis(double.NaN, "Number.prototype.toPrecision", 1000).AsString());
        Assert.Equal("1.000", realm.CallWithThis(1, "Number.prototype.toFixed", 3.9).AsString());
    }

    [Fact]
    public void ThisValue()
    {
        using var realm = new TestRealm();
        var wrapper = realm.Construct("Number", 42);
        Assert.Equal(42, realm.CallWithThis(wrapper, "Number.prototype.valueOf").AsNumber());
        Assert.Equal("101010", realm.CallWithThis(wrapper, "Number.prototype.toString", 2).AsString());
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.CallWithThis("1", "Number.prototype.valueOf")));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.CallWithThis(realm.CreateObject(), "Number.prototype.toFixed")));
        Assert.Equal(0, realm.CallWithThis(realm.Get("Number.prototype"), "Number.prototype.valueOf").AsNumber());
    }

    [Fact]
    public void Constructor()
    {
        using var realm = new TestRealm();
        Assert.Equal(0, realm.Call("Number").AsNumber());
        Assert.True(double.IsNaN(realm.Call("Number", JsValue.Undefined).AsNumber()));
        Assert.Equal(255, realm.Call("Number", " 0xff ").AsNumber());
        Assert.Equal(18446744073709551616.0, realm.Call("Number", JsValue.FromBigInt(BigInteger.Pow(2, 64) - 1)).AsNumber());
        Assert.Equal(9007199254740992.0, realm.Call("Number", JsValue.FromBigInt(BigInteger.Pow(2, 53) + 1)).AsNumber());
        Assert.Equal(9007199254740996.0, realm.Call("Number", JsValue.FromBigInt(BigInteger.Pow(2, 53) + 3)).AsNumber());
        Assert.Equal(double.MaxValue, realm.Call("Number", JsValue.FromBigInt(BigInteger.Pow(2, 1024) - BigInteger.Pow(2, 970) - 1)).AsNumber());
        Assert.Equal(double.PositiveInfinity, realm.Call("Number", JsValue.FromBigInt(BigInteger.Pow(2, 1024) - BigInteger.Pow(2, 970))).AsNumber());
        Assert.Equal(double.NegativeInfinity, realm.Call("Number", JsValue.FromBigInt(-BigInteger.Pow(2, 1100))).AsNumber());

        var wrapper = realm.Construct("Number", "12").AsObject();
        Assert.Equal(ObjectClass.Number, wrapper.Class);
        Assert.Same(realm.Get("Number.prototype").AsObject(), wrapper.GetPrototypeOf());
    }

    [Fact]
    public void Statics()
    {
        using var realm = new TestRealm();
        Assert.Equal(realm.Get("parseInt"), realm.Get("Number.parseInt"));
        Assert.Equal(realm.Get("parseFloat"), realm.Get("Number.parseFloat"));
        Assert.Equal(Math.Pow(2, -52), realm.Get("Number.EPSILON").AsNumber());
        Assert.Equal(5e-324, realm.Get("Number.MIN_VALUE").AsNumber());
        Assert.Equal(9007199254740991, realm.Get("Number.MAX_SAFE_INTEGER").AsNumber());

        Assert.True(realm.Call("Number.isInteger", 5.0).AsBoolean());
        Assert.False(realm.Call("Number.isInteger", 5.5).AsBoolean());
        Assert.False(realm.Call("Number.isInteger", "5").AsBoolean());
        Assert.False(realm.Call("Number.isInteger", double.PositiveInfinity).AsBoolean());
        Assert.True(realm.Call("Number.isSafeInteger", 9007199254740991.0).AsBoolean());
        Assert.False(realm.Call("Number.isSafeInteger", 9007199254740992.0).AsBoolean());
        Assert.False(realm.Call("Number.isNaN", "NaN").AsBoolean());
        Assert.True(realm.Call("Number.isNaN", double.NaN).AsBoolean());
        Assert.False(realm.Call("Number.isFinite", "1").AsBoolean());

        var constructor = realm.Get("Number").AsObject();
        Assert.True(constructor.GetOwnProperty(new PropertyKey("MAX_VALUE"), out var descriptor));
        Assert.Equal(PropertyFlags.None, descriptor.Flags);
        Assert.Equal(1, constructor.Get(new PropertyKey("length")).AsNumber());
    }
}
