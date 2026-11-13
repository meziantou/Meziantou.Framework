using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiWellKnownScalarTypeParityTests
{
    private sealed class HalfPayload
    {
        public Half Value { get; set; }
    }

    [Fact]
    public void Deserialize_Half_ShouldRoundTrip()
    {
        var toml = """
            Value = 1.5
            """;

        var payload = TomlSerializer.Deserialize<HalfPayload>(toml);

        Assert.NotNull(payload);
        Assert.Equal((Half)1.5, payload!.Value);

        var roundtripToml = TomlSerializer.Serialize(payload);
        var roundtrip = TomlSerializer.Deserialize<HalfPayload>(roundtripToml);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Value, roundtrip!.Value);
    }

    [Fact]
    public void Deserialize_Half_OutOfRange_ShouldThrowWithLocation()
    {
        var toml = """
            Value = 1e100
            """;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<HalfPayload>(toml));
        Assert.NotNull(ex);
        Assert.Equal(1, ex!.Line);
        Assert.True(ex.Column > 0);
    }

    private sealed class Int128Payload
    {
        public Int128 Value { get; set; }
    }

    private sealed class UInt128Payload
    {
        public UInt128 Value { get; set; }
    }

    [Fact]
    public void Deserialize_Int128_ShouldRoundTripWithinTomlRange()
    {
        var toml = """
            Value = 123
            """;

        var payload = TomlSerializer.Deserialize<Int128Payload>(toml);

        Assert.NotNull(payload);
        Assert.Equal((Int128)123, payload!.Value);

        var roundtripToml = TomlSerializer.Serialize(payload);
        var roundtrip = TomlSerializer.Deserialize<Int128Payload>(roundtripToml);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Value, roundtrip!.Value);
    }

    [Fact]
    public void Serialize_Int128_OutOfTomlRange_ShouldThrow()
    {
        var payload = new Int128Payload { Value = (Int128)long.MaxValue + 1 };
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(payload));
    }

    [Fact]
    public void Deserialize_UInt128_ShouldRoundTripWithinTomlRange()
    {
        var toml = """
            Value = 42
            """;

        var payload = TomlSerializer.Deserialize<UInt128Payload>(toml);

        Assert.NotNull(payload);
        Assert.Equal((UInt128)42, payload!.Value);

        var roundtripToml = TomlSerializer.Serialize(payload);
        var roundtrip = TomlSerializer.Deserialize<UInt128Payload>(roundtripToml);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Value, roundtrip!.Value);
    }

    [Fact]
    public void Serialize_UInt128_OutOfTomlRange_ShouldThrow()
    {
        var payload = new UInt128Payload { Value = (UInt128)long.MaxValue + 1 };
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(payload));
    }

    private sealed class ComplexPayload
    {
        public Complex Value { get; set; }
    }

    [Fact]
    public void Serialize_Complex_WritesInlineTable()
    {
        var toml = TomlSerializer.Serialize(new ComplexPayload { Value = new Complex(1.5, -2) });

        Assert.Equal("Value = {Real = 1.5, Imaginary = -2.0}" + Environment.NewLine, toml);
        Assert.Equal(new Complex(1.5, -2), TomlSerializer.Deserialize<ComplexPayload>(toml)!.Value);
    }

    [Fact]
    public void Serialize_Complex_UsesNamingPolicy()
    {
        var options = new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.CamelCase };

        var toml = TomlSerializer.Serialize(new ComplexPayload { Value = new Complex(1, 2) }, options);

        Assert.Equal("value = {real = 1.0, imaginary = 2.0}" + Environment.NewLine, toml);
        Assert.Equal(new Complex(1, 2), TomlSerializer.Deserialize<ComplexPayload>(toml, options)!.Value);
    }

    [Fact]
    public void Deserialize_Complex_AcceptsStandardTableAndIntegers()
    {
        var toml = """
            [Value]
            Imaginary = 4
            Real = 3
            """;

        Assert.Equal(new Complex(3, 4), TomlSerializer.Deserialize<ComplexPayload>(toml)!.Value);
    }

    [Fact]
    public void Deserialize_Complex_MissingPartIsZero()
    {
        Assert.Equal(new Complex(0, 2), TomlSerializer.Deserialize<ComplexPayload>("Value = {Imaginary = 2.0}")!.Value);
    }

    [Fact]
    public void Deserialize_Complex_NonFiniteParts()
    {
        var value = TomlSerializer.Deserialize<ComplexPayload>("Value = {Real = nan, Imaginary = -inf}")!.Value;

        Assert.True(double.IsNaN(value.Real));
        Assert.Equal(double.NegativeInfinity, value.Imaginary);
        Assert.Equal("Value = {Real = nan, Imaginary = -inf}" + Environment.NewLine, TomlSerializer.Serialize(new ComplexPayload { Value = value }));
    }

    [Fact]
    public void Deserialize_Complex_CaseInsensitive()
    {
        var options = new TomlSerializerOptions { PropertyNameCaseInsensitive = true };

        Assert.Equal(new Complex(1, 2), TomlSerializer.Deserialize<ComplexPayload>("Value = { REAL = 1.0, imaginary = 2.0 }", options)!.Value);
    }

    [Fact]
    public void Deserialize_Complex_UnknownKey()
    {
        // The computed Magnitude and Phase are ignored, unless unmapped members are disallowed
        var toml = "Value = {Real = 3.0, Imaginary = 4.0, Magnitude = 5.0, Phase = 0.9}";

        Assert.Equal(new Complex(3, 4), TomlSerializer.Deserialize<ComplexPayload>(toml)!.Value);
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ComplexPayload>(toml, new TomlSerializerOptions { UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow }));
        Assert.Contains("Magnitude", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Value = 1.0")]
    [InlineData("Value = {Real = \"a\"}")]
    public void Deserialize_Complex_Invalid_ShouldThrow(string toml)
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ComplexPayload>(toml));
    }

    [Fact]
    public void Serialize_Complex_Root()
    {
        var toml = TomlSerializer.Serialize(new Complex(1, 2));

        Assert.Equal("""
            Real = 1.0
            Imaginary = 2.0

            """.ReplaceLineEndings(), toml);
        Assert.Equal(new Complex(1, 2), TomlSerializer.Deserialize<Complex>(toml));
    }

    [Fact]
    public void Serialize_ComplexCollection()
    {
        var value = new List<Complex> { new(1, 2), new(3, 4) };

        var toml = TomlSerializer.Serialize(new Dictionary<string, List<Complex>> { ["Values"] = value });

        Assert.Equal("Values = [{Real = 1.0, Imaginary = 2.0}, {Real = 3.0, Imaginary = 4.0}]" + Environment.NewLine, toml);
        Assert.Equal(value, TomlSerializer.Deserialize<Dictionary<string, List<Complex>>>(toml)!["Values"]);
    }

#if NET11_0_OR_GREATER
    private sealed class Ieee754Payload
    {
        public BFloat16 BFloat16 { get; set; }

        public Decimal32 Decimal32 { get; set; }

        public Decimal64 Decimal64 { get; set; }

        public Decimal128 Decimal128 { get; set; }

        public Decimal64? NullableDecimal64 { get; set; }

        public Complex<double> ComplexDouble { get; set; }

        public Complex<Half> ComplexHalf { get; set; }

        public Complex<Decimal128> ComplexDecimal128 { get; set; }

        public Complex<float>? NullableComplexSingle { get; set; }
    }

    private sealed class Payload<T>
    {
        public T? Value { get; set; }
    }

    [Fact]
    public void Serialize_Ieee754Types_RoundTrip()
    {
        var payload = new Ieee754Payload
        {
            BFloat16 = (BFloat16)1.5f,
            Decimal32 = Decimal32.Parse("1.234567", CultureInfo.InvariantCulture),
            Decimal64 = Decimal64.Parse("-0.1", CultureInfo.InvariantCulture),
            Decimal128 = Decimal128.Parse("1234567890123456789012345678901.234", CultureInfo.InvariantCulture),
            NullableDecimal64 = Decimal64.Parse("1.50", CultureInfo.InvariantCulture),
            ComplexDouble = new Complex<double>(1.5, -2),
            ComplexHalf = new Complex<Half>(Half.NaN, Half.PositiveInfinity),
            ComplexDecimal128 = new Complex<Decimal128>(Decimal128.Parse("0.1", CultureInfo.InvariantCulture), Decimal128.Parse("1E-400", CultureInfo.InvariantCulture)),
            NullableComplexSingle = new Complex<float>(0.1f, 2),
        };

        var toml = TomlSerializer.Serialize(payload);

        Assert.Equal("""
            BFloat16 = 1.5
            Decimal32 = 1.234567
            Decimal64 = -0.1
            Decimal128 = 1234567890123456789012345678901.234
            NullableDecimal64 = 1.50
            ComplexDouble = {Real = 1.5, Imaginary = -2.0}
            ComplexHalf = {Real = nan, Imaginary = +inf}
            ComplexDecimal128 = {Real = 0.1, Imaginary = 1E-400}
            NullableComplexSingle = {Real = 0.1, Imaginary = 2.0}

            """.ReplaceLineEndings(), toml);

        var roundtrip = TomlSerializer.Deserialize<Ieee754Payload>(toml)!;
        Assert.Equal(payload.BFloat16, roundtrip.BFloat16);
        Assert.Equal(payload.Decimal32, roundtrip.Decimal32);
        Assert.Equal(payload.Decimal64, roundtrip.Decimal64);
        Assert.Equal(payload.Decimal128, roundtrip.Decimal128);
        Assert.Equal("1.50", roundtrip.NullableDecimal64!.Value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(payload.ComplexDouble, roundtrip.ComplexDouble);
        Assert.True(Half.IsNaN(roundtrip.ComplexHalf.Real));
        Assert.Equal(Half.PositiveInfinity, roundtrip.ComplexHalf.Imaginary);
        Assert.Equal(payload.ComplexDecimal128, roundtrip.ComplexDecimal128);
        Assert.Equal(payload.NullableComplexSingle, roundtrip.NullableComplexSingle);
    }

    [Theory]
    [InlineData("Value = 1", "1")]
    [InlineData("Value = -42", "-42")]
    [InlineData("Value = 0x10", "16")]
    [InlineData("Value = 1_000.000_1", "1000.0001")]
    [InlineData("Value = +1.5e-3", "0.0015")]
    [InlineData("Value = 1E-400", "1E-400")]
    [InlineData("Value = 1.000000000000000000000000000000001", "1.000000000000000000000000000000001")]
    [InlineData("Value = -0.0", "-0.0")]
    [InlineData("Value = inf", "Infinity")]
    [InlineData("Value = +inf", "Infinity")]
    [InlineData("Value = -inf", "-Infinity")]
    [InlineData("Value = nan", "NaN")]
    [InlineData("Value = -nan", "NaN")]
    public void Deserialize_Decimal128(string toml, string expected)
    {
        var value = TomlSerializer.Deserialize<Payload<Decimal128>>(toml)!.Value;

        Assert.Equal(expected, value.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1.50", "1.50")]
    [InlineData("1E+20", "1E+20")]
    [InlineData("-1E-30", "-1E-30")]
    [InlineData("NaN", "nan")]
    [InlineData("Infinity", "+inf")]
    [InlineData("-Infinity", "-inf")]
    public void Serialize_Decimal64(string value, string expected)
    {
        var toml = TomlSerializer.Serialize(new Payload<Decimal64> { Value = Decimal64.Parse(value, CultureInfo.InvariantCulture) });

        Assert.Equal("Value = " + expected + Environment.NewLine, toml);
        var roundtrip = TomlSerializer.Deserialize<Payload<Decimal64>>(toml)!.Value;
        Assert.Equal(value, roundtrip.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0", "0.0")]
    [InlineData("-0", "-0.0")]
    [InlineData("12", "12.0")]
    public void Serialize_Decimal64_Integral_WritesFloat(string value, string expected)
    {
        var decimal64 = Decimal64.Parse(value, CultureInfo.InvariantCulture);

        var toml = TomlSerializer.Serialize(new Payload<Decimal64> { Value = decimal64 });

        Assert.Equal("Value = " + expected + Environment.NewLine, toml);
        var roundtrip = TomlSerializer.Deserialize<Payload<Decimal64>>(toml)!.Value;
        Assert.Equal(decimal64, roundtrip);
        Assert.Equal(Decimal64.IsNegative(decimal64), Decimal64.IsNegative(roundtrip));
    }

    [Theory]
    [InlineData("1E+400")]
    [InlineData("-9.999999999999999999999999999999999E+6144")]
    public void Serialize_Decimal128_OutsideDoubleRange_ShouldThrow(string value)
    {
        var payload = new Payload<Decimal128> { Value = Decimal128.Parse(value, CultureInfo.InvariantCulture) };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(payload));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Payload<Decimal128>>("Value = " + value));
    }

    [Fact]
    public void Deserialize_Decimal64_RoundedOutsideDoubleRange_ShouldThrow()
    {
        // The literal is double.MaxValue, but it rounds to a larger Decimal64 that could not be written back
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Payload<Decimal64>>("Value = 1.7976931348623157e308"));
        Assert.Equal(Decimal128.Parse("1.7976931348623157E+308", CultureInfo.InvariantCulture), TomlSerializer.Deserialize<Payload<Decimal128>>("Value = 1.7976931348623157e308")!.Value);
    }

    [Theory]
    [InlineData("Value = 1e97")]
    [InlineData("Value = -1e100")]
    [InlineData("Value = \"1.0\"")]
    [InlineData("Value = true")]
    public void Deserialize_Decimal32_Invalid_ShouldThrowWithLocation(string toml)
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Payload<Decimal32>>(toml));
        Assert.Equal(1, ex.Line);
    }

    [Fact]
    public void Deserialize_BFloat16_OutOfRange_ShouldThrow()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Payload<BFloat16>>("Value = 1e300"));
        Assert.Equal(BFloat16.NegativeInfinity, TomlSerializer.Deserialize<Payload<BFloat16>>("Value = -inf")!.Value);
    }

    [Fact]
    public void Serialize_Untyped_Ieee754Types()
    {
        var value = new Dictionary<string, object>
        {
            ["bfloat16"] = (BFloat16)2.5f,
            ["decimal128"] = Decimal128.Parse("0.10", CultureInfo.InvariantCulture),
            ["complex"] = new Complex(1, 2),
        };

        var toml = TomlSerializer.Serialize(value);

        Assert.Equal("""
            bfloat16 = 2.5
            decimal128 = 0.10
            complex = {Real = 1.0, Imaginary = 2.0}

            """.ReplaceLineEndings(), toml);
    }
#endif
}
