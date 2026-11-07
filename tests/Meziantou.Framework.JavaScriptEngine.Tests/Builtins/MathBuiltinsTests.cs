namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class MathBuiltinsTests
{
    private static double Call(TestRealm realm, string name, params JsValue[] arguments) => realm.Call("Math." + name, arguments).AsNumber();

    private static void AssertSameValue(double expected, double actual)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual));
            return;
        }

        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [Theory]
    [InlineData(0.49999999999999994, 0.0)]
    [InlineData(-0.5, -0.0)]
    [InlineData(-0.2, -0.0)]
    [InlineData(-0.0, -0.0)]
    [InlineData(2.5, 3.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(4503599627370495.5, 4503599627370496.0)]
    [InlineData(-4503599627370495.5, -4503599627370495.0)]
    [InlineData(9007199254740991.0, 9007199254740991.0)]
    public void Round(double value, double expected)
    {
        using var realm = new TestRealm();
        AssertSameValue(expected, Call(realm, "round", value));
    }

    [Theory]
    [InlineData(1.00048828125, 1.0)]
    [InlineData(65520.0, double.PositiveInfinity)]
    [InlineData(65519.99, 65504.0)]
    [InlineData(2.9802322387695312e-8, 0.0)]
    [InlineData(2.980232238769532e-8, 5.960464477539063e-8)]
    [InlineData(-0.0, -0.0)]
    public void F16Round(double value, double expected)
    {
        using var realm = new TestRealm();
        AssertSameValue(expected, Call(realm, "f16round", value));
    }

    [Fact]
    public void F16RoundIsNotRoundedThroughFloat()
    {
        // 1 + 2^-11 + 2^-40 is above the midpoint of 1 and 1 + 2^-10; the float closest to it is the midpoint
        using var realm = new TestRealm();
        AssertSameValue(1.0009765625, Call(realm, "f16round", 1 + Math.Pow(2, -11) + Math.Pow(2, -40)));
    }

    [Fact]
    public void MaxMin()
    {
        using var realm = new TestRealm();
        AssertSameValue(double.NegativeInfinity, Call(realm, "max"));
        AssertSameValue(double.PositiveInfinity, Call(realm, "min"));
        AssertSameValue(0.0, Call(realm, "max", -0.0, 0.0));
        AssertSameValue(0.0, Call(realm, "max", 0.0, -0.0));
        AssertSameValue(-0.0, Call(realm, "min", 0.0, -0.0));
        AssertSameValue(-0.0, Call(realm, "min", -0.0, 0.0));
        AssertSameValue(double.NaN, Call(realm, "max", 1, double.NaN, 3));

        // All the arguments are converted, even after a NaN
        var conversions = 0;
        var valueOf = realm.CreateFunction((_, _) =>
        {
            conversions++;
            return 1;
        });
        var obj = realm.CreateObject(("valueOf", valueOf));
        AssertSameValue(double.NaN, Call(realm, "min", double.NaN, obj, obj));
        Assert.Equal(2, conversions);
    }

    [Fact]
    public void Hypot()
    {
        using var realm = new TestRealm();
        AssertSameValue(0.0, Call(realm, "hypot"));
        AssertSameValue(0.0, Call(realm, "hypot", -0.0));
        AssertSameValue(5, Call(realm, "hypot", 3, 4));
        AssertSameValue(double.PositiveInfinity, Call(realm, "hypot", double.NaN, double.NegativeInfinity));
        AssertSameValue(double.NaN, Call(realm, "hypot", double.NaN, 1));
        Assert.Equal(1.414213562373095e+200, Call(realm, "hypot", 1e200, 1e200), 1e186);
        Assert.Equal(1.414213562373095e-200, Call(realm, "hypot", 1e-200, 1e-200), 1e-214);
    }

    [Fact]
    public void Functions()
    {
        using var realm = new TestRealm();
        AssertSameValue(double.NaN, Call(realm, "pow", 1, double.PositiveInfinity));
        AssertSameValue(double.NaN, Call(realm, "pow", -1, double.NegativeInfinity));
        AssertSameValue(double.NaN, Call(realm, "pow", 1, double.NaN));
        AssertSameValue(1, Call(realm, "pow", double.NaN, 0));
        AssertSameValue(double.NaN, Call(realm, "pow", -8, 1.0 / 3));
        AssertSameValue(-2, Call(realm, "cbrt", -8));
        AssertSameValue(31, Call(realm, "clz32", 1));
        AssertSameValue(32, Call(realm, "clz32", 0));
        AssertSameValue(-5, Call(realm, "imul", 4294967295.0, 5));
        AssertSameValue(-0.0, Call(realm, "sign", -0.0));
        AssertSameValue(-1, Call(realm, "sign", -3));
        AssertSameValue(-0.0, Call(realm, "trunc", -0.5));
        AssertSameValue(-0.0, Call(realm, "ceil", -0.5));
        AssertSameValue(1.00000000005e-10, Call(realm, "expm1", 1e-10));
        AssertSameValue(9.999999999500001e-11, Call(realm, "log1p", 1e-10));
        AssertSameValue(-0.0, Call(realm, "expm1", -0.0));
        AssertSameValue(-0.0, Call(realm, "log1p", -0.0));
        AssertSameValue(-1, Call(realm, "expm1", double.NegativeInfinity));
        AssertSameValue(double.PositiveInfinity, Call(realm, "expm1", 1000));
        AssertSameValue(double.NegativeInfinity, Call(realm, "log1p", -1));
        AssertSameValue(double.NaN, Call(realm, "log1p", -2));
        AssertSameValue(1.718281828459045, Call(realm, "expm1", 1));
        AssertSameValue(3.4028234663852886e38, Call(realm, "fround", 3.4028235e38));

        var random = Call(realm, "random");
        Assert.True(random is >= 0 and < 1);

        var math = realm.Get("Math").AsObject();
        Assert.True(math.GetOwnProperty(new PropertyKey(WellKnownSymbols.ToStringTag), out var tag));
        Assert.Equal("Math", tag.Value.AsString());
        Assert.Equal(PropertyFlags.Configurable, tag.Flags);
        Assert.Equal(2, math.Get(new PropertyKey("max")).AsObject().Get(new PropertyKey("length")).AsNumber());
    }

    [Fact]
    public void SumPrecise()
    {
        using var realm = new TestRealm();
        double Sum(params double[] values) => Call(realm, "sumPrecise", realm.CreateIterable([.. values.Select(v => (JsValue)v)]));

        AssertSameValue(6, Sum(1, 2, 3));
        AssertSameValue(0, Sum(1e308, -1e308));
        AssertSameValue(0.2, Sum(0.1, 0.1));
        AssertSameValue(0, Sum(0.1, -0.1));
        AssertSameValue(0.30000000000000004, Sum(1e308, 1e308, 0.1, 0.1, 1e30, 0.1, -1e30, -1e308, -1e308));
        AssertSameValue(0.1, Sum(1e30, 0.1, -1e30));
        AssertSameValue(9.9792015476736e+291, Sum(8.98846567431158e+307, 8.988465674311579e+307, -1.7976931348623157e+308));
        AssertSameValue(9.565271205476347e+307, Sum(-5.630637621603525e+255, 9.565271205476345e+307, 2.9937604643020797e+292));
        AssertSameValue(1.61796594939028e+308, Sum(6.739986666787661e+66, 2, -1.2689709186578243e-116, 1.7046015739467354e+308, -9.979201547673601e+291, 6.160926733208294e+307, -3.179557053031852e+234, -7.027282978772846e+307, -0.7500000000000001));
        AssertSameValue(double.NegativeInfinity, Sum(0.31150493246968836, -8.988465674311582e+307, 1.8315037361673755e-270, -15.999999999999996, 2.9999999999999996, 7.345200721499384e+164, -2.033582473639399, -8.98846567431158e+307, -3.5737295155405993e+292, 4.13894772383715e-124, -3.6111186457260667e-35, 2.387234887098013e+180, 7.645295562778372e-298, 3.395189016861822e-103, -2.6331611115768973e-149));
        AssertSameValue(-1.5936821971565685e+308, Sum(-1.1442589134409902e+308, 9.593842098384855e+138, 4.494232837155791e+307, -1.3482698511467367e+308, 4.494232837155792e+307));
        AssertSameValue(-1.5936821971565687e+308, Sum(-1.1442589134409902e+308, 4.494232837155791e+307, -1.3482698511467367e+308, 4.494232837155792e+307));
        AssertSameValue(1.7976931348623157e+308, Sum(-2.534858246857893e+115, 8.988465674311579e+307, 8.98846567431158e+307));
        AssertSameValue(double.PositiveInfinity, Sum(1.3588124894186193e+308, 1.4803986201152006e+223, 6.741349255733684e+307));
        AssertSameValue(double.PositiveInfinity, Sum(6.741349255733684e+307, 1.7976931348623155e+308, -7.388327292663961e+41));
        AssertSameValue(1.7976931348623157e+308, Sum(-1.9807040628566093e+28, 1.7976931348623157e+308, 9.9792015476736e+291));
        AssertSameValue(double.PositiveInfinity, Sum(1.7976931348623157e+308, 7.999999999999999, -1.908963895403937e-230, 1.6445950082320264e+292, 2.0734856707605806e+205));
        AssertSameValue(double.NegativeInfinity, Sum(6.197409167220438e-223, -9.979201547673601e+291, -1.7976931348623157e+308));
        AssertSameValue(8.353845887521184e+307, Sum(4.49423283715579e+307, 8.944251746776101e+307, -0.0002441406250000001, 1.1752060710043817e+308, 4.940846717201632e+292, -1.6836699406454528e+308));
        AssertSameValue(-1.7976931348623157e+308, Sum(8.988465674311579e+307, 7.999999999999998, 7.029158107234023e-308, -2.2303483759420562e-172, -1.7976931348623157e+308, -8.98846567431158e+307));
        AssertSameValue(double.PositiveInfinity, Sum(8.98846567431158e+307, 8.98846567431158e+307));
        AssertSameValue(5e-324, Sum(5e-324, 1, -1));
        AssertSameValue(1e-323, Sum(5e-324, 5e-324));

        AssertSameValue(-0.0, Sum());
        AssertSameValue(-0.0, Sum(-0.0, -0.0));
        AssertSameValue(0.0, Sum(-0.0, 0.0));
        AssertSameValue(double.PositiveInfinity, Sum(double.PositiveInfinity, 1));
        AssertSameValue(double.NaN, Sum(double.PositiveInfinity, double.NegativeInfinity));
        AssertSameValue(double.NaN, Sum(double.NaN, 1));

        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("Math.sumPrecise")));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("Math.sumPrecise", 1, 2)));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("Math.sumPrecise", realm.CreateIterable(1, "2"))));
        Assert.Equal("TypeError", realm.GetErrorName(() => realm.Call("Math.sumPrecise", realm.CreateIterable(double.NaN, JsValue.FromBigInt(1)))));
        Assert.Equal(2, realm.ReturnCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test data")]
    public void SumPreciseRandom()
    {
        // Multiples of 2^-10: the exact sum is computed with integers
        using var realm = new TestRealm();
        var random = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            var count = random.Next(1, 50);
            var values = new JsValue[count];
            long expected = 0;
            for (var j = 0; j < count; j++)
            {
                var value = random.NextInt64(-1L << 52, 1L << 52);
                values[j] = value / 1024.0;
                expected += value;
            }

            Assert.Equal(expected / 1024.0, Call(realm, "sumPrecise", realm.CreateIterable(values)));
        }
    }
}
