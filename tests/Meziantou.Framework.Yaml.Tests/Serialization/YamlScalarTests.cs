using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlScalarTests
{
    [Fact]
    public void IsNull_Works()
    {
        var cases = new (string Text, bool Expected)[]
        {
            ("", true),
            ("   ", true),
            ("~", true),
            ("null", true),
            ("NULL", true),
            ("Null", true),
            ("nil", false),
            ("0", false),
        };

        foreach (var @case in cases)
        {
            Assert.Equal(@case.Expected, YamlScalar.IsNull(@case.Text.AsSpan()));
        }
    }

    [Fact]
    public void TryParseBool_Works()
    {
        var cases = new (string Text, bool ExpectedValue, bool ExpectedSuccess)[]
        {
            ("true", true, true),
            ("TRUE", true, true),
            (" false ", false, true),
            ("False", false, true),
            ("yes", false, false),
            ("", false, false),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseBool(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success);
            if (success)
            {
                Assert.Equal(@case.ExpectedValue, value);
            }
        }
    }

    [Fact]
    public void TryParseInt64_Works()
    {
        var cases = new (string Text, long ExpectedValue, bool ExpectedSuccess)[]
        {
            ("0", 0L, true),
            ("123", 123L, true),
            ("+123", 123L, true),
            ("-123", -123L, true),
            ("1_000", 1000L, true),
            ("0x10", 16L, true),
            ("0o10", 8L, true),
            ("0b10", 2L, true),
            ("-0x10", -16L, true),
            ("not-a-number", 0L, false),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseInt64(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success);
            if (success)
            {
                Assert.Equal(@case.ExpectedValue, value);
            }
        }
    }

    [Fact]
    public void TryParseUInt64_Works()
    {
        var cases = new (string Text, ulong ExpectedValue, bool ExpectedSuccess)[]
        {
            ("0", 0UL, true),
            ("123", 123UL, true),
            ("+123", 123UL, true),
            ("1_000", 1000UL, true),
            ("0xFF", 255UL, true),
            ("0o10", 8UL, true),
            ("0b10", 2UL, true),
            ("-1", 0UL, false),
            ("", 0UL, false),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseUInt64(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success);
            if (success)
            {
                Assert.Equal(@case.ExpectedValue, value);
            }
        }
    }

    [Fact]
    public void TryParseDecimal_Works()
    {
        var cases = new (string Text, string ExpectedInvariant, bool ExpectedSuccess)[]
        {
            ("1.5", "1.5", true),
            ("  1_000.25  ", "1000.25", true),
            (".inf", "0", false),
            ("-.inf", "0", false),
            (".nan", "0", false),
            ("not-a-decimal", "0", false),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseDecimal(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success);
            if (success)
            {
                Assert.Equal(@case.ExpectedInvariant, value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public void TryParseDouble_Works()
    {
        var cases = new (string Text, double ExpectedValue, bool ExpectedSuccess)[]
        {
            (".inf", 0.0, true),
            ("+.inf", 0.0, true),
            ("-.inf", 0.0, true),
            (".nan", 0.0, true),
            ("1.5", 1.5, true),
            ("  1_000.25  ", 1000.25, true),
            ("not-a-double", 0.0, false),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseDouble(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success);
            if (!success)
            {
                continue;
            }

            if (@case.Text.Contains(".nan", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(double.IsNaN(value));
                continue;
            }

            if (@case.Text.Contains("-.inf", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(double.IsNegativeInfinity(value));
                continue;
            }

            if (@case.Text.Contains(".inf", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(double.IsPositiveInfinity(value));
                continue;
            }

            Assert.Equal(@case.ExpectedValue, value);
        }
    }

    [Fact]
    public void StringDeserializer_DistinguishesPlainAndQuotedScalars()
    {
        var yaml = """
            plainNull: null
            quotedNull: "null"
            plainEmpty:
            quotedBool: "true"
            quotedInt: "42"
            """;

        var values = YamlSerializer.Deserialize<Dictionary<string, string?>>(yaml)!;

        Assert.Null(values["plainNull"]);
        Assert.Null(values["plainEmpty"]);
        Assert.Equal("null", values["quotedNull"]);
        Assert.Equal("true", values["quotedBool"]);
        Assert.Equal("42", values["quotedInt"]);
    }

    [Fact]
    public void UntypedDeserializer_DistinguishesPlainAndQuotedScalars()
    {
        var yaml = """
            plainNull: null
            quotedNull: "null"
            plainBool: true
            quotedBool: "true"
            plainInt: 42
            quotedInt: "42"
            """;

        var values = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml)!;

        Assert.Null(values["plainNull"]);
        Assert.Equal("null", values["quotedNull"]);
        Assert.Equal(true, values["plainBool"]);
        Assert.Equal("true", values["quotedBool"]);
        Assert.Equal(42L, values["plainInt"]);
        Assert.Equal("42", values["quotedInt"]);
    }

    [Fact]
    public void StringSerializer_QuotesStandaloneQuestionMarkValue()
    {
        var yaml = YamlSerializer.Serialize(new IndicatorStringModel { Bar = "?" });

        Assert.Contains("Bar: \"?\"", yaml);
        var roundTrip = YamlSerializer.Deserialize<IndicatorStringModel>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal("?", roundTrip.Bar);
    }

    [Fact]
    public void SchemaAwareDeserialization_UsesExtendedSchemaForPlainScalars()
    {
        var options = new YamlSerializerOptions
        {
            Schema = YamlSchemaKind.Extended,
            UseSchema = true,
        };

        Assert.Equal(true, YamlSerializer.Deserialize<bool>("yes", options));
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<bool>("\"yes\"", options));

        var yaml = """
            plainBool: yes
            quotedBool: "yes"
            plainNull:
            quotedNull: "null"
            binary: 0b10
            hex: 0x10
            """;
        var values = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml, options)!;

        Assert.Equal(true, values["plainBool"]);
        Assert.Equal("yes", values["quotedBool"]);
        Assert.Null(values["plainNull"]);
        Assert.Equal("null", values["quotedNull"]);
        Assert.Equal(2, values["binary"]);
        Assert.Equal(16, values["hex"]);
    }

    private sealed class IndicatorStringModel
    {
        public string? Bar { get; set; }
    }

    [Fact]
    public void TryParseUInt64_ReturnsFalseWhenTheValueOverflows()
    {
        var cases = new (string Text, bool ExpectedSuccess, ulong ExpectedValue)[]
        {
            ("0xFFFFFFFFFFFFFFFF", true, ulong.MaxValue),
            ("0x10000000000000000", false, 0),
            ("0o1777777777777777777777", true, ulong.MaxValue),
            ("0o2000000000000000000000", false, 0),
            ("0b1111111111111111111111111111111111111111111111111111111111111111", true, ulong.MaxValue),
            ("0b10000000000000000000000000000000000000000000000000000000000000000", false, 0),
            ("18446744073709551615", true, ulong.MaxValue),
            ("18446744073709551616", false, 0),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseUInt64(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success, @case.Text);
            if (success)
            {
                Assert.Equal(@case.ExpectedValue, value, @case.Text);
            }
        }
    }

    [Fact]
    public void TryParseInt64_ReturnsFalseWhenTheValueOverflows()
    {
        var cases = new (string Text, bool ExpectedSuccess, long ExpectedValue)[]
        {
            ("0x7FFFFFFFFFFFFFFF", true, long.MaxValue),
            ("-0x8000000000000000", true, long.MinValue),
            ("0x8000000000000000", false, 0),
            ("0x10000000000000000", false, 0),
            ("0o777777777777777777777", true, long.MaxValue),
            ("0o2000000000000000000000", false, 0),
            ("0b111111111111111111111111111111111111111111111111111111111111111", true, long.MaxValue),
            ("0b10000000000000000000000000000000000000000000000000000000000000000", false, 0),
        };

        foreach (var @case in cases)
        {
            var success = YamlScalar.TryParseInt64(@case.Text.AsSpan(), out var value);
            Assert.Equal(@case.ExpectedSuccess, success, @case.Text);
            if (success)
            {
                Assert.Equal(@case.ExpectedValue, value, @case.Text);
            }
        }
    }

    [Fact]
    public void Serialize_StringThatLooksLikeAnOverflowingNumber_RoundTrips()
    {
        var cases = new[]
        {
            "0x10000000000000000",
            "0o2000000000000000000000",
            "0b10000000000000000000000000000000000000000000000000000000000000000",
            "18446744073709551616",
        };

        foreach (var text in cases)
        {
            var yaml = YamlSerializer.Serialize(text);
            Assert.Equal(text, YamlSerializer.Deserialize<string>(yaml), text);
        }
    }

    [Fact]
    public void Deserialize_SchemaKeepsDecimalPrecision()
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        Assert.Equal(0.1234567890123456789012345678m, YamlSerializer.Deserialize<decimal>("0.1234567890123456789012345678", options));
        Assert.Equal(1.0000000000000000000000000001m, YamlSerializer.Deserialize<decimal>("1.0000000000000000000000000001", options));
    }

    [Fact]
    public void Deserialize_SchemaSupportsDecimalsBeyondUInt64()
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        Assert.Equal(decimal.MaxValue, YamlSerializer.Deserialize<decimal>("79228162514264337593543950335", options));
        Assert.Equal(decimal.MinValue, YamlSerializer.Deserialize<decimal>("-79228162514264337593543950335", options));
        Assert.Equal(18446744073709551616m, YamlSerializer.Deserialize<decimal>("18446744073709551616", options));
    }

    [Fact]
    public void Deserialize_SchemaKeepsTheBaseOfIntegerScalarsForDecimals()
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        Assert.Equal(16m, YamlSerializer.Deserialize<decimal>("0x10", options));
        Assert.Equal(8m, YamlSerializer.Deserialize<decimal>("0o10", options));
    }

    [Fact]
    public void Deserialize_SchemaRejectsIntegersThatDoNotFitTheDestination()
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<int>("79228162514264337593543950335", options));
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<long>("79228162514264337593543950335", options));
    }

    [Theory]
    [InlineData("|-\n  true\n", "true")]
    [InlineData("|-\n  null\n", "null")]
    [InlineData("|-\n  123\n", "123")]
    [InlineData("|-\n  1.5\n", "1.5")]
    [InlineData(">-\n  true\n", "true")]
    [InlineData(">-\n  null\n", "null")]
    [InlineData(">-\n  123\n", "123")]
    public void Deserialize_SchemaKeepsBlockScalarsAsStrings(string yaml, string expected)
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        var value = YamlSerializer.Deserialize<object>(yaml, options);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("|-\n  true\n", "true")]
    [InlineData(">-\n  null\n", "null")]
    public void Deserialize_BlockScalarsAreStringsWithoutSchema(string yaml, string expected)
    {
        var value = YamlSerializer.Deserialize<object>(yaml);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Deserialize_SchemaResolvesIntegerSpellings()
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        Assert.Equal(12, YamlSerializer.Deserialize<int>("012", options));
        Assert.Equal(12, YamlSerializer.Deserialize<object>("012", options));
        Assert.Equal("0b101", YamlSerializer.Deserialize<object>("0b101", options));
        Assert.Equal("1_000", YamlSerializer.Deserialize<object>("1_000", options));

        var extended = new YamlSerializerOptions { UseSchema = true, Schema = YamlSchemaKind.Extended };
        Assert.Equal(5, YamlSerializer.Deserialize<object>("0b101", extended));
        Assert.Equal(1000, YamlSerializer.Deserialize<object>("1_000", extended));
    }
}
