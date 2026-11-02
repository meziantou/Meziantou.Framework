using System.Globalization;
using System.Numerics;
using System.Text;

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

    // The sizes around the thresholds of the algorithms: the small values, the decimal formatting of BigInteger, the
    // decimal parsing by parts and the formatting by divide and conquer
    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1000)]
    [InlineData(16384)]
    [InlineData(16385)]
    [InlineData(100000)]
    public void LargeValuesToStringAndBack(int bitCount)
    {
        using var realm = new TestRealm();
        var random = new Random(bitCount);
        var magnitude = (BigInteger.One << (bitCount - 1)) | RandomBits(random, bitCount - 1);
        foreach (var value in new[] { magnitude, -magnitude })
        {
            // The reference conversion is quadratic: a few radices for the large values
            var radices = bitCount <= 20000 ? Enumerable.Range(2, 35) : [2, 3, 7, 8, 10, 16, 32, 36];
            foreach (var radix in radices)
            {
                var text = realm.CallWithThis(JsValue.FromBigInt(value), "BigInt.prototype.toString", radix).AsString();
                Assert.Equal(ReferenceToString(value, radix), text);
            }

            var digits = value.ToString(CultureInfo.InvariantCulture);
            Assert.Equal(value, realm.Call("BigInt", digits).AsBigInt());
            Assert.Equal(value, realm.Call("BigInt", " " + digits.Replace("-", "-0000", StringComparison.Ordinal) + "\n").AsBigInt());
            Assert.Equal(digits, realm.Call("String", JsValue.FromBigInt(value)).AsString());
        }

        Assert.Equal(magnitude, realm.Call("BigInt", "0x" + ReferenceToString(magnitude, 16).ToUpperInvariant()).AsBigInt());
        Assert.Equal(magnitude, realm.Call("BigInt", "0o" + ReferenceToString(magnitude, 8)).AsBigInt());
        Assert.Equal(magnitude, realm.Call("BigInt", "0B000" + ReferenceToString(magnitude, 2)).AsBigInt());
        Assert.Equal(magnitude, realm.Realm.Execute(ReferenceToString(magnitude, 10) + "n").AsBigInt());
        Assert.Equal(magnitude, realm.Realm.Execute("0x" + ReferenceToString(magnitude, 16) + "n").AsBigInt());
        Assert.Equal(magnitude, realm.Realm.Execute("0o" + ReferenceToString(magnitude, 8) + "n").AsBigInt());
        Assert.Equal(magnitude, realm.Realm.Execute("0b" + ReferenceToString(magnitude, 2) + "n").AsBigInt());
        Assert.Equal(magnitude, realm.Realm.Execute("0x" + string.Join('_', ReferenceToString(magnitude, 16).Chunk(3).Select(chunk => new string(chunk))) + "n").AsBigInt());
        Assert.Equal(magnitude, realm.Realm.Execute(string.Join('_', ReferenceToString(magnitude, 10).Chunk(5).Select(chunk => new string(chunk))) + "n").AsBigInt());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("000000000000000000000000000000")]
    [InlineData("999999999999999999")]
    [InlineData("1000000000000000000")]
    [InlineData("0x0000000000000000000000000001")]
    [InlineData("0xffffffffffffffff")]
    [InlineData("0x10000000000000000")]
    [InlineData("0o1777777777777777777777")]
    [InlineData("0b1111111111111111111111111111111111111111111111111111111111111111")]
    public void ParseBoundaries(string text)
    {
        using var realm = new TestRealm();
        var expected = text switch
        {
            ['0', 'x', .. var rest] => BigInteger.Parse("0" + rest, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            ['0', 'o', .. var rest] => rest.Aggregate(BigInteger.Zero, (value, c) => (value * 8) + (c - '0')),
            ['0', 'b', .. var rest] => rest.Aggregate(BigInteger.Zero, (value, c) => (value * 2) + (c - '0')),
            _ => BigInteger.Parse(text, CultureInfo.InvariantCulture),
        };
        Assert.Equal(expected, realm.Call("BigInt", text).AsBigInt());
        if (text is not ['0', >= '0' and <= '9', ..])
        {
            Assert.Equal(expected, realm.Realm.Execute(text + "n").AsBigInt());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test data")]
    public void LargeDecimalStrings(bool interruptible)
    {
        using var cancellation = new CancellationTokenSource();
        using var realm = CreateRealm(interruptible, cancellation);
        // Around the parts that BigInteger parses at once (2^18 digits, 2^20 on .NET 11)
        foreach (var length in new[] { 262144, 262145, 1048577 })
        {
            var random = new Random(length);
            var digits = string.Concat(Enumerable.Range(0, length).Select(i => (char)('0' + random.Next(i == 0 ? 1 : 0, 10))));
            var expected = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            Assert.Equal(expected, realm.Call("BigInt", digits).AsBigInt());
            Assert.Equal(expected, realm.Call("BigInt", "000" + digits).AsBigInt());
            Assert.Equal(-expected, realm.Call("BigInt", "-" + digits).AsBigInt());
            Assert.Equal(expected, realm.Realm.Execute(digits + "n").AsBigInt());
            Assert.Equal(digits, realm.Realm.Execute(digits + "n.toString()").AsString());
        }

        // The zeros between the parts formatted by BigInteger
        var zeros = "1" + new string('0', 1_500_000) + "7" + new string('0', 300_000);
        Assert.Equal(zeros, realm.Call("String", realm.Call("BigInt", zeros)).AsString());

        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", new string('1', 100000) + "a")));
        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", "0x" + new string('f', 100000) + "g")));
        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", "0b" + new string('1', 100000) + "2")));
        Assert.Equal("SyntaxError", realm.GetErrorName(() => realm.Call("BigInt", "-0x" + new string('f', 100000))));
    }

    // The products and the powers larger than the parts that BigInteger computes at once (4 million bits, 8 million on .NET 11)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test data")]
    public void LargeArithmetic(bool interruptible)
    {
        using var cancellation = new CancellationTokenSource();
        using var realm = CreateRealm(interruptible, cancellation);
        var random = new Random(42);
        var x = (BigInteger.One << 4_400_000) | RandomBits(random, 4_400_000);
        var y = (BigInteger.One << 4_000_000) | RandomBits(random, 4_000_000);
        var large = (BigInteger.One << 8_400_000) | RandomBits(random, 8_400_000);
        var small = (BigInteger.One << 100_000) | RandomBits(random, 100_000);
        Assert.Equal(x * y, BigIntOperations.Multiply(x, y));
        Assert.Equal(-x * y, BigIntOperations.Multiply(-x, y));
        Assert.Equal(x * x, BigIntOperations.Multiply(-x, -x));
        Assert.Equal(-x * x, BigIntOperations.Multiply(x, -x));
        Assert.Equal(large * small, BigIntOperations.Multiply(small, large));
        Assert.Equal(large * 12345, BigIntOperations.Multiply(large, 12345));

        // The powers are checked modulo primes: computing them with BigInteger.Pow takes seconds
        AssertPower(3, 5_400_000);
        AssertPower(-3, 5_400_001);
        AssertPower(-12, 1_500_001);
        Assert.Equal(BigInteger.One << 100_000_000, BigIntOperations.Exponentiate(2, 100_000_000));
        Assert.Equal(-(BigInteger.One << 99_999_999), BigIntOperations.Exponentiate(-2, 99_999_999));
        Assert.Equal(BigInteger.One << 99_999_999, BigIntOperations.Exponentiate(BigInteger.One << 33_333_333, 3));
        Assert.True(realm.Realm.Execute("2n ** 100000000n === 1n << 100000000n").AsBoolean());
        Assert.Equal("RangeError", realm.GetErrorName(() => realm.Realm.Execute("2n ** 1073741824n")));

        static void AssertPower(BigInteger @base, int exponent)
        {
            var result = BigIntOperations.Exponentiate(@base, exponent);
            Assert.Equal(@base.Sign < 0 && exponent % 2 == 1 ? -1 : 1, result.Sign);
            Assert.Equal((long)Math.Floor(exponent * Math.Log2(Math.Abs((double)@base))) + 1, (long)BigInteger.Abs(result).GetBitLength());
            foreach (var prime in new BigInteger[] { 1_000_000_007, (BigInteger.One << 61) - 1, (BigInteger.One << 89) - 1 })
            {
                var expected = BigInteger.ModPow(@base, exponent, prime);
                var actual = BigInteger.Remainder(result, prime);
                Assert.Equal(expected, actual);
            }
        }
    }

    // Without a limit, the large operations are computed at once by BigInteger; a cancellation token splits them into parts
    private static TestRealm CreateRealm(bool interruptible, CancellationTokenSource cancellation)
    {
        var realm = new TestRealm(interruptible ? new JsEngineOptions { CancellationToken = cancellation.Token } : null);
        Assert.Equal(interruptible, JsEngine.CurrentWorkCanBeInterrupted);
        return realm;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test data")]
    private static BigInteger RandomBits(Random random, int bitCount)
    {
        var bytes = new byte[(bitCount / 8) + 1];
        random.NextBytes(bytes);
        return new BigInteger(bytes, isUnsigned: true) & ((BigInteger.One << bitCount) - 1);
    }

    private static string ReferenceToString(BigInteger value, int radix)
    {
        if (radix == 10)
            return value.ToString(CultureInfo.InvariantCulture);

        if (value.IsZero)
            return "0";

        var builder = new StringBuilder();
        var magnitude = BigInteger.Abs(value);

        // Chunks of digits that fit in a long, from the last one
        var chunkDigits = (int)(62 / Math.Log2(radix));
        var divisor = BigInteger.Pow(radix, chunkDigits);
        while (!magnitude.IsZero)
        {
            magnitude = BigInteger.DivRem(magnitude, divisor, out var remainder);
            var chunk = (long)remainder;
            for (var i = 0; i < chunkDigits; i++)
            {
                builder.Append("0123456789abcdefghijklmnopqrstuvwxyz"[(int)(chunk % radix)]);
                chunk /= radix;
            }
        }

        var digits = builder.ToString().TrimEnd('0');
        if (value.Sign < 0)
        {
            digits += "-";
        }

        return string.Concat(digits.Reverse());
    }
}
