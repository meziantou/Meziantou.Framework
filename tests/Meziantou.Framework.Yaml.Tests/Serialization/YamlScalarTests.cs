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
}
