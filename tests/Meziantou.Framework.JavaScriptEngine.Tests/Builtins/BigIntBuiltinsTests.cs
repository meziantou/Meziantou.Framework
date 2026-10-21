using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class BigIntBuiltinsTests
{
    private static JsValue Big(string value) => JsValue.FromBigInt(BigInteger.Parse(value, CultureInfo.InvariantCulture));

    [Fact]
    public void Constructor()
    {
        using var realm = new TestRealm();
        Assert.Equal(Big("31"), realm.Call("BigInt", " 0x1F "));
        Assert.Equal(Big("-12"), realm.Call("BigInt", " -12 "));
        Assert.Equal(Big("0"), realm.Call("BigInt", ""));
        Assert.Equal(Big("1000000000000000000000"), realm.Call("BigInt", 1e21));
        Assert.Equal(Big("1"), realm.Call("BigInt", true));
        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", "1.5")));
        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", "0x")));
        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", "-0x1")));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.Call("BigInt", 1.5)));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.Call("BigInt", double.NaN)));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("BigInt", JsValue.Undefined)));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("BigInt", JsValue.Null)));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Construct("BigInt", 1)));
        Assert.True(realm.Get("BigInt").AsObject().IsConstructor);
    }

    [Theory]
    [InlineData("asIntN", 8, "255", "-1")]
    [InlineData("asIntN", 8, "128", "-128")]
    [InlineData("asUintN", 8, "-1", "255")]
    [InlineData("asUintN", 64, "-1", "18446744073709551615")]
    [InlineData("asIntN", 64, "9223372036854775808", "-9223372036854775808")]
    [InlineData("asIntN", 9007199254740991, "-5", "-5")]
    [InlineData("asUintN", 9007199254740991, "5", "5")]
    [InlineData("asUintN", 0, "5", "0")]
    [InlineData("asIntN", 1, "1", "-1")]
    public void AsN(string name, double bits, string value, string expected)
    {
        using var realm = new TestRealm();
        Assert.Equal(Big(expected), realm.Call("BigInt." + name, bits, Big(value)));
    }

    [Fact]
    public void AsNErrors()
    {
        using var realm = new TestRealm();
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.Call("BigInt.asUintN", 9007199254740991.0, Big("-1"))));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.Call("BigInt.asIntN", -1, Big("1"))));
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.Call("BigInt.asIntN", 9007199254740992.0, Big("1"))));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("BigInt.asIntN", 8, 1)));
    }

    [Fact]
    public void PrototypeToString()
    {
        using var realm = new TestRealm();
        Assert.Equal("3ewfdnca0n6ld1ggvfgg", realm.CallWithThis(JsValue.FromBigInt(BigInteger.Pow(2, 100)), "BigInt.prototype.toString", 36).AsString());
        Assert.Equal("-6106454640561632563602145", realm.CallWithThis(JsValue.FromBigInt(-BigInteger.Pow(2, 70) + 12345), "BigInt.prototype.toString", 7).AsString());
        Assert.Equal("-10000000000000000", realm.CallWithThis(JsValue.FromBigInt(-BigInteger.Pow(2, 64)), "BigInt.prototype.toString", 16).AsString());
        Assert.Equal("0", realm.CallWithThis(Big("0"), "BigInt.prototype.toString", 2).AsString());
        Assert.Equal("-123", realm.CallWithThis(Big("-123"), "BigInt.prototype.toString").AsString());
        Assert.Equal("-123", realm.CallWithThis(Big("-123"), "BigInt.prototype.toLocaleString").AsString());
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.CallWithThis(Big("1"), "BigInt.prototype.toString", 37)));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.CallWithThis(1, "BigInt.prototype.toString")));

        var wrapper = JsOperations.ToObject(Big("5"));
        Assert.Equal(Big("5"), realm.CallWithThis(wrapper, "BigInt.prototype.valueOf"));
        Assert.Equal("BigInt", realm.Get("BigInt.prototype").AsObject().Get(new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
    }

    [Fact]
    public void Operators()
    {
        using var realm = new TestRealm();
        Assert.Equal(new BigInteger(-3), BigIntOperations.SignedRightShift(-5, 1));
        Assert.Equal(new BigInteger(-2), BigIntOperations.Divide(-5, 2));
        Assert.Equal(new BigInteger(-1), BigIntOperations.Remainder(-5, 2));
        Assert.Equal(new BigInteger(-8), BigIntOperations.LeftShift(-1, 3));
        Assert.Equal(new BigInteger(-1), BigIntOperations.LeftShift(-1, -3));
        Assert.Equal(new BigInteger(0), BigIntOperations.LeftShift(5, -10));
        Assert.Equal(new BigInteger(-1), BigIntOperations.LeftShift(-5, -1000000000000));
        Assert.Equal(new BigInteger(0), BigIntOperations.LeftShift(0, 1000000000000));
        Assert.Equal(new BigInteger(-6), BigIntOperations.BitwiseNot(5));
        Assert.Equal(new BigInteger(1), BigIntOperations.Exponentiate(0, 0));
        Assert.Equal(new BigInteger(-1), BigIntOperations.Exponentiate(-1, BigInteger.Pow(10, 30) + 1));
        Assert.Equal(BigInteger.Pow(3, 40), BigIntOperations.Exponentiate(3, 40));
        Assert.Equal("RangeError", realm.GetErrorName(() => BigIntOperations.Exponentiate(2, -1)));
        Assert.Equal("RangeError", realm.GetErrorName(() => BigIntOperations.Exponentiate(2, BigInteger.Pow(10, 30))));
        Assert.Equal("RangeError", realm.GetErrorName(() => BigIntOperations.Divide(1, 0)));
        Assert.Equal("RangeError", realm.GetErrorName(() => BigIntOperations.Remainder(1, 0)));
        Assert.Equal("RangeError", realm.GetErrorName(() => BigIntOperations.LeftShift(1, BigInteger.Pow(10, 30))));
        Assert.Equal("TypeError", realm.GetErrorName(() => BigIntOperations.UnsignedRightShift()));
    }

    [Fact]
    public void IsLessThan()
    {
        using var realm = new TestRealm();
        Assert.True(BigIntOperations.IsLessThan(Big("1"), 1.5, leftFirst: true));
        Assert.False(BigIntOperations.IsLessThan(Big("2"), 1.5, leftFirst: true));
        Assert.True(BigIntOperations.IsLessThan(1.5, Big("2"), leftFirst: true));
        Assert.True(BigIntOperations.IsLessThan(Big("1"), "2", leftFirst: true));
        Assert.Null(BigIntOperations.IsLessThan("x", Big("1"), leftFirst: true));
        Assert.Null(BigIntOperations.IsLessThan(Big("1"), double.NaN, leftFirst: true));
        Assert.True(BigIntOperations.IsLessThan(9007199254740992.0, Big("9007199254740993"), leftFirst: true));
        Assert.False(BigIntOperations.IsLessThan(Big("-1"), double.NegativeInfinity, leftFirst: true));
        Assert.True(BigIntOperations.IsLessThan(Big("-1"), double.PositiveInfinity, leftFirst: true));
        Assert.True(BigIntOperations.IsLessThan("a", "b", leftFirst: true));
        Assert.True(BigIntOperations.IsLessThan("10", 9, leftFirst: false) is false);
    }

    [Fact]
    public void ToBigInt64()
    {
        using var realm = new TestRealm();
        Assert.Equal(-1L, BigIntOperations.ToBigInt64(JsValue.FromBigInt(BigInteger.Pow(2, 64) - 1)));
        Assert.Equal(long.MinValue, BigIntOperations.ToBigInt64(JsValue.FromBigInt(BigInteger.Pow(2, 63))));
        Assert.Equal(ulong.MaxValue, BigIntOperations.ToBigUint64(Big("-1")));
        Assert.Equal(1UL, BigIntOperations.ToBigUint64(JsValue.FromBigInt(BigInteger.Pow(2, 64) + 1)));
    }
}
