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
            ("nULL", false),
            ("NuLl", false),
            ("\u00A0", false),
            ("\u3000", false),
            ("null\u3000", false),
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
            ("tRUE", false, false),
            ("fALSE", false, false),
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
            (".iNf", 0.0, false),
            (".nAn", 0.0, false),
            ("NaN", 0.0, false),
            ("Infinity", 0.0, false),
            ("-Infinity", 0.0, false),
            ("1\u00A0", 0.0, false),
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

    [Theory]
    [InlineData("0X1F")]
    [InlineData("0O7")]
    [InlineData("0B1")]
    public void TryParseInt64_RejectsUppercaseBasePrefixes(string text)
    {
        Assert.False(YamlScalar.TryParseInt64(text.AsSpan(), out _));
        Assert.False(YamlScalar.TryParseUInt64(text.AsSpan(), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_StringMember_KeepsSpellingsThatAreNotCoreNull(bool useSourceGeneration)
    {
        foreach (var text in new[] { "nULL", "NuLl", "\u00A0", "\u3000", "\u2028" })
        {
            var result = Deserialize<ScalarHolder>("Text: " + text + "\n", useSourceGeneration);

            Assert.NotNull(result);
            Assert.Equal(text, result.Text);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_UntypedMember_ResolvesPlainScalarsWithTheCoreSchemaSpellings(bool useSourceGeneration)
    {
        foreach (var text in new[] { "nULL", "tRUE", "fALSE", ".iNf", ".nAn", "NaN", "Infinity", "-Infinity", "0X1F", "0O7", "1\u00A0" })
        {
            var result = Deserialize<ScalarHolder>("Value: " + text + "\n", useSourceGeneration);

            Assert.NotNull(result);
            Assert.Equal(text, result.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_UntypedMember_ReadsIntegersBeyondInt64(bool useSourceGeneration)
    {
        var result = Deserialize<ScalarHolder>("Value: 18446744073709551615\n", useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal(ulong.MaxValue, result.Value);
        Assert.Equal(ulong.MaxValue, YamlSerializer.Deserialize<object>("18446744073709551615"));
    }

    [Theory]
    [InlineData(false, "! 42", "42")]
    [InlineData(false, "! true", "true")]
    [InlineData(false, "! null", "null")]
    [InlineData(true, "! 42", "42")]
    [InlineData(true, "! true", "true")]
    [InlineData(true, "! null", "null")]
    public void Deserialize_NonSpecificTagMakesAPlainScalarAString(bool useSchema, string yaml, string expected)
    {
        var options = new YamlSerializerOptions { UseSchema = useSchema };

        Assert.Equal(expected, YamlSerializer.Deserialize<object>(yaml, options));
        Assert.Equal(expected, YamlSerializer.Deserialize<string>(yaml, options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_Untyped_HonorsExplicitCoreTags(bool useSchema)
    {
        var options = new YamlSerializerOptions { UseSchema = useSchema };

        Assert.Equal(42L, Convert.ToInt64(YamlSerializer.Deserialize<object>("!!int \"42\"", options), CultureInfo.InvariantCulture));
        Assert.Equal(1.0, YamlSerializer.Deserialize<object>("!!float 1", options));
        Assert.Equal(true, YamlSerializer.Deserialize<object>("!!bool 'true'", options));
        Assert.Null(YamlSerializer.Deserialize<object>("!!null ''", options));
        Assert.Equal("42", YamlSerializer.Deserialize<object>("!!str 42", options));
    }

    [Theory]
    [InlineData(false, "!!int abc")]
    [InlineData(false, "!!int 4.5")]
    [InlineData(false, "!!bool yes")]
    [InlineData(false, "!!null abc")]
    [InlineData(false, "!!float 0x10")]
    [InlineData(true, "!!int abc")]
    [InlineData(true, "!!int 4.5")]
    [InlineData(true, "!!bool yes")]
    [InlineData(true, "!!null abc")]
    [InlineData(true, "!!float 0x10")]
    public void Deserialize_Untyped_RejectsContentThatDoesNotMatchItsExplicitTag(bool useSchema, string yaml)
    {
        var options = new YamlSerializerOptions { UseSchema = useSchema };

        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>(yaml, options));
    }

    [Theory]
    [InlineData(false, "!!int")]
    [InlineData(false, "!!float")]
    [InlineData(false, "!!bool")]
    [InlineData(false, "!!int ~")]
    [InlineData(true, "!!int")]
    [InlineData(true, "!!float")]
    [InlineData(true, "!!bool")]
    [InlineData(true, "!!int ~")]
    public void Deserialize_UntypedMember_RejectsNullContentWithAnotherExplicitTag(bool useSourceGeneration, string value)
    {
        Assert.Throws<YamlException>(() => Deserialize<ScalarHolder>("Value: " + value + "\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_UntypedMember_AcceptsExplicitNullTag(bool useSourceGeneration)
    {
        var result = Deserialize<ScalarHolder>("Value: !!null\nText: !!null ~\n", useSourceGeneration);

        Assert.NotNull(result);
        Assert.Null(result.Value);
        Assert.Null(result.Text);
    }

    [Theory]
    [InlineData("0x1FFFFFFFFFFFFFFFF")]
    [InlineData("0o7777777777777777777777777")]
    [InlineData("99999999999999999999999999999999999999999999")]
    public void Serialize_StringThatLooksLikeAnIntegerOfAnyMagnitude_IsQuoted(string value)
    {
        var yaml = YamlSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = value });

        Assert.Equal("k: \"" + value + "\"\n", yaml);
        Assert.Equal(value, YamlSerializer.Deserialize<Dictionary<string, object>>(yaml, new YamlSerializerOptions { UseSchema = true })!["k"]);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("N")]
    [InlineData("Off")]
    [InlineData("2001-12-14")]
    [InlineData("0b101")]
    [InlineData("1_000")]
    public void Serialize_ExtendedSchema_QuotesStringsTheSchemaResolvesToAnotherType(string value)
    {
        var options = new YamlSerializerOptions { UseSchema = true, Schema = YamlSchemaKind.Extended };

        var yaml = YamlSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) { ["a"] = value }, options);
        var result = YamlSerializer.Deserialize<Dictionary<string, object>>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(value, result["a"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialize_NegativeZero_KeepsItsSign(bool useSchema)
    {
        var options = new YamlSerializerOptions { UseSchema = useSchema };
        var value = new NegativeZeroHolder { Double = -0.0, Single = -0.0f, Half = -Half.Zero, Value = -0.0 };

        var yaml = YamlSerializer.Serialize(value, options);
        var result = YamlSerializer.Deserialize<NegativeZeroHolder>(yaml, options);

        Assert.Equal("Double: -0.0\nSingle: -0.0\nHalf: -0.0\nValue: -0.0\n", yaml);
        Assert.NotNull(result);
        Assert.True(double.IsNegative(result.Double));
        Assert.True(float.IsNegative(result.Single));
        Assert.True(Half.IsNegative(result.Half));
        Assert.True(double.IsNegative(Assert.IsType<double>(result.Value)));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Serialize_DotNetFloatingPointNames_AreStillQuoted(string value)
    {
        Assert.Equal("\"" + value + "\"\n", YamlSerializer.Serialize(value));
    }

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, ScalarYamlContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);

    internal sealed class ScalarHolder
    {
        public string? Text { get; set; }

        public object? Value { get; set; }
    }

    private sealed class NegativeZeroHolder
    {
        public double Double { get; set; }

        public float Single { get; set; }

        public Half Half { get; set; }

        public object? Value { get; set; }
    }
}

#pragma warning disable MA0048 // File name must match type name
[YamlSerializable(typeof(YamlScalarTests.ScalarHolder))]
internal sealed partial class ScalarYamlContext : YamlSerializerContext
{
}
