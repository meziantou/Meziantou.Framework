using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Schemas;

namespace Meziantou.Framework.Yaml.Tests;

public class SchemaTests
{
    [Fact]
    public void TestFailsafeSchema()
    {
        var schema = new FailsafeSchema();
        TestFailsafeSchemaCommon(schema);
    }

    [Fact]
    public void TestJsonSchema()
    {
        var schema = new JsonSchema();
        TestJsonSchemaCommon(schema);

        // Json should not accept plain literal
        Assert.Equal(null, schema.GetDefaultTag(new Scalar(null, null, "boom", ScalarStyle.Plain, true, false)));
    }

    [Fact]
    public void TestCoreSchema()
    {
        var schema = new CoreSchema();

        TestCoreSchemaCommon(schema);
    }

    [Fact]
    public void TestExtendedSchema()
    {
        var schema = new ExtendedSchema();

        TestCoreSchemaCommon(schema);

        TryParse(schema, "2002-12-14", ExtendedSchema.TimestampShortTag, new DateTime(2002, 12, 14));
        TryParse(schema, "2002-12-14 21:59:43.234", ExtendedSchema.TimestampShortTag, new DateTime(2002, 12, 14, 21, 59, 43, 234));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("3", 3)]
    [InlineData("-19", -19)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("-2147483649", -2147483649L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("9223372036854775808", 9223372036854775808UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    public void JsonSchema_IntegerResolutionMatchesSpecification(string text, object expected)
    {
        TryParse(new JsonSchema(), text, JsonSchema.IntShortTag, expected);
    }

    [Theory]
    [InlineData("0.", 0d)]
    [InlineData("-0.0", -0d)]
    [InlineData("12e03", 12000d)]
    [InlineData("-2E+05", -200000d)]
    [InlineData("1.e2", 100d)]
    [InlineData("1E-2", 0.01d)]
    [InlineData("1e309", double.PositiveInfinity)]
    [InlineData("-1e309", double.NegativeInfinity)]
    [InlineData("1e-400", 0d)]
    public void JsonSchema_FloatResolutionMatchesSpecification(string text, double expected)
    {
        // YAML 1.2.2 explicitly permits an empty fraction after the dot, unlike JSON syntax.
        TryParse(new JsonSchema(), text, JsonSchema.FloatShortTag, expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("~")]
    [InlineData("True")]
    [InlineData("NULL")]
    [InlineData("+12")]
    [InlineData("+12.3")]
    [InlineData("01")]
    [InlineData("-01")]
    [InlineData("01.5")]
    [InlineData("0o7")]
    [InlineData("0x3A")]
    [InlineData("0b10")]
    [InlineData("1_000")]
    [InlineData("1_")]
    [InlineData("1__0")]
    [InlineData("1.0_0")]
    [InlineData(".5")]
    [InlineData(".inf")]
    [InlineData("-.inf")]
    [InlineData(".nan")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e-")]
    [InlineData("1e1.5")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("null\n")]
    [InlineData("true\n")]
    public void JsonSchema_UnmatchedPlainScalarsAreRejected(string text)
    {
        var schema = new JsonSchema();
        Assert.False(schema.TryParse(new Scalar(text), true, out var tag, out var value));
        Assert.Null(tag);
        Assert.Null(value);
    }

    [Theory]
    [InlineData("null\n")]
    [InlineData("true\n")]
    [InlineData("123\n")]
    [InlineData("0x10\n")]
    [InlineData("1.5\n")]
    [InlineData(".inf\n")]
    [InlineData("\n")]
    [InlineData("TrUe")]
    [InlineData("nUlL")]
    [InlineData(".iNF")]
    [InlineData("+.nan")]
    [InlineData("-.NaN")]
    [InlineData("0O7")]
    [InlineData("0Xf")]
    [InlineData("0o8")]
    [InlineData("0xG")]
    [InlineData("+0x1")]
    [InlineData("+0o1")]
    [InlineData("0x")]
    [InlineData("0o")]
    [InlineData(".e2")]
    [InlineData("1e+")]
    [InlineData("1.0_0")]
    [InlineData("1e1_0")]
    [InlineData("2001-12-15")]
    [InlineData("1:20")]
    [InlineData("yes")]
    [InlineData("off")]
    public void CoreSchema_UnmatchedPlainScalarsRemainStrings(string text)
    {
        TryParse(new CoreSchema(), text, SchemaBase.StrShortTag, text);
    }

    [Theory]
    [InlineData("", JsonSchema.NullShortTag, null)]
    [InlineData("~", JsonSchema.NullShortTag, null)]
    [InlineData("Null", JsonSchema.NullShortTag, null)]
    [InlineData("NULL", JsonSchema.NullShortTag, null)]
    [InlineData("+.5", JsonSchema.FloatShortTag, 0.5d)]
    [InlineData("-.5", JsonSchema.FloatShortTag, -0.5d)]
    [InlineData("00.5", JsonSchema.FloatShortTag, 0.5d)]
    [InlineData("+12e03", JsonSchema.FloatShortTag, 12000d)]
    [InlineData("+.INF", JsonSchema.FloatShortTag, double.PositiveInfinity)]
    [InlineData("-.INF", JsonSchema.FloatShortTag, double.NegativeInfinity)]
    [InlineData(".NAN", JsonSchema.FloatShortTag, double.NaN)]
    public void CoreSchema_ExtendedResolutionMatchesSpecification(string text, string expectedTag, object? expected)
    {
        TryParse(new CoreSchema(), text, expectedTag, expected);
    }

    [Theory]
    [InlineData(ScalarStyle.Plain)]
    [InlineData(ScalarStyle.SingleQuoted)]
    [InlineData(ScalarStyle.DoubleQuoted)]
    [InlineData(ScalarStyle.Literal)]
    [InlineData(ScalarStyle.Folded)]
    public void FailsafeSchema_PreservesScalarContentForEveryStyle(ScalarStyle style)
    {
        var schema = new FailsafeSchema();
        foreach (var text in new[] { "", "null", "True", "-0", "0xFF", ".NAN", "line\n", "α😀" })
        {
            var scalar = new Scalar(null, null, text, style, true, true);
            Assert.True(schema.TryParse(scalar, true, out var tag, out var value));
            Assert.Equal(SchemaBase.StrShortTag, tag);
            Assert.Equal(text, value);
        }
    }

    [Theory]
    [InlineData("true\n", typeof(bool))]
    [InlineData("42\n", typeof(int))]
    [InlineData("1.5\n", typeof(double))]
    public void CoreSchema_TypedResolutionRequiresTheEntireScalar(string text, Type type)
    {
        Assert.False(new CoreSchema().TryParse(new Scalar(text), type, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("0", JsonSchema.IntShortTag)]
    [InlineData("-0", JsonSchema.IntShortTag)]
    [InlineData("0.", JsonSchema.FloatShortTag)]
    [InlineData("null", JsonSchema.NullShortTag)]
    [InlineData("false", JsonSchema.BoolShortTag)]
    public void JsonSchema_TagResolutionDoesNotDecodeValues(string text, string expectedTag)
    {
        Assert.True(new JsonSchema().TryParse(new Scalar(text), false, out var tag, out var value));
        Assert.Equal(expectedTag, tag);
        Assert.Null(value);
    }

    [Theory]
    [InlineData(YamlSchemaKind.Json, "!!float 42", 42d)]
    [InlineData(YamlSchemaKind.Json, "!!float '42'", 42d)]
    [InlineData(YamlSchemaKind.Json, "!!float -0", -0d)]
    [InlineData(YamlSchemaKind.Json, "!!float .inf", double.PositiveInfinity)]
    [InlineData(YamlSchemaKind.Json, "!!float -.inf", double.NegativeInfinity)]
    [InlineData(YamlSchemaKind.Json, "!!float .nan", double.NaN)]
    [InlineData(YamlSchemaKind.Core, "!!float 42", 42d)]
    [InlineData(YamlSchemaKind.Core, "!!float \"42\"", 42d)]
    [InlineData(YamlSchemaKind.Extended, "!!float 42", 42d)]
    public void Serializer_ExplicitFloatTagTakesPrecedenceOverImplicitResolution(YamlSchemaKind schema, string yaml, double expected)
    {
        var options = new YamlSerializerOptions { UseSchema = true, Schema = schema };
        var value = YamlSerializer.Deserialize<object>(yaml, options);
        Assert.Equal(expected, Assert.IsType<double>(value));
        Assert.Equal(expected, YamlSerializer.Deserialize<double>(yaml, options));
    }

    [Fact]
    public void Serializer_JsonSchemaNegativeZeroIsAnInteger()
    {
        var options = new YamlSerializerOptions { UseSchema = true, Schema = YamlSchemaKind.Json };
        Assert.Equal(0, Assert.IsType<int>(YamlSerializer.Deserialize<object>("-0", options)));
        Assert.Equal(0, YamlSerializer.Deserialize<int>("-0", options));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, ".inf")]
    [InlineData(double.NegativeInfinity, "-.inf")]
    [InlineData(double.NaN, ".nan")]
    public void Serializer_NonFiniteNumbersUseYamlSpellingsAndExplicitJsonTags(double value, string spelling)
    {
        foreach (var schema in new[] { YamlSchemaKind.Core, YamlSchemaKind.Json })
        {
            var options = new YamlSerializerOptions { Schema = schema, UseSchema = true };
            var expected = schema is YamlSchemaKind.Json ? "!!float " + spelling : spelling;
            Assert.Equal(expected + "\n", YamlSerializer.Serialize(value, options));
            Assert.Equal(expected + "\n", YamlSerializer.Serialize((float)value, options));
            Assert.Equal(expected + "\n", YamlSerializer.Serialize((Half)value, options));
            Assert.Equal(value, YamlSerializer.Deserialize<double>(expected, options));
            Assert.Equal((float)value, YamlSerializer.Deserialize<float>(expected, options));
            Assert.Equal((Half)value, YamlSerializer.Deserialize<Half>(expected, options));
            Assert.Equal(value, Assert.IsType<double>(YamlSerializer.Deserialize<object>(expected, options)));
        }
    }

    private static void TestFailsafeSchemaCommon(FailsafeSchema schema)
    {
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar("true")));
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar("custom", "boom")));
        Assert.Equal(FailsafeSchema.MapShortTag, schema.GetDefaultTag(new MappingStart()));
        Assert.Equal(FailsafeSchema.SeqShortTag, schema.GetDefaultTag(new SequenceStart()));

        Assert.Equal(FailsafeSchema.MapLongTag, schema.ExpandTag("!!map"));
        Assert.Equal(FailsafeSchema.SeqLongTag, schema.ExpandTag("!!seq"));
        Assert.Equal(SchemaBase.StrLongTag, schema.ExpandTag("!!str"));

        Assert.Equal("!!map", schema.ShortenTag(FailsafeSchema.MapLongTag));
        Assert.Equal("!!seq", schema.ShortenTag(FailsafeSchema.SeqLongTag));
        Assert.Equal("!!str", schema.ShortenTag(SchemaBase.StrLongTag));

        TryParse(schema, "true", SchemaBase.StrShortTag, "true");
    }

    private static void TestJsonSchemaCommon(JsonSchema schema)
    {
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar(null, null, "true", ScalarStyle.DoubleQuoted, false, false)));
        Assert.Equal(JsonSchema.BoolShortTag, schema.GetDefaultTag(new Scalar("true")));
        Assert.Equal(JsonSchema.NullShortTag, schema.GetDefaultTag(new Scalar("null")));
        Assert.Equal(JsonSchema.IntShortTag, schema.GetDefaultTag(new Scalar("5")));
        Assert.Equal(JsonSchema.FloatShortTag, schema.GetDefaultTag(new Scalar("5.5")));

        Assert.Equal(JsonSchema.NullLongTag, schema.ExpandTag("!!null"));
        Assert.Equal(JsonSchema.BoolLongTag, schema.ExpandTag("!!bool"));
        Assert.Equal(JsonSchema.IntLongTag, schema.ExpandTag("!!int"));
        Assert.Equal(JsonSchema.FloatLongTag, schema.ExpandTag("!!float"));

        Assert.Equal("!!null", schema.ShortenTag(JsonSchema.NullLongTag));
        Assert.Equal("!!bool", schema.ShortenTag(JsonSchema.BoolLongTag));
        Assert.Equal("!!int", schema.ShortenTag(JsonSchema.IntLongTag));
        Assert.Equal("!!float", schema.ShortenTag(JsonSchema.FloatLongTag));

        TryParse(schema, "null", JsonSchema.NullShortTag, null);
        TryParse(schema, "true", JsonSchema.BoolShortTag, true);
        TryParse(schema, "false", JsonSchema.BoolShortTag, false);
        TryParse(schema, "5", JsonSchema.IntShortTag, 5);
        TryParse(schema, "5.5", JsonSchema.FloatShortTag, 5.5);
    }

    private static void TestCoreSchemaCommon(CoreSchema schema)
    {
        TestJsonSchemaCommon(schema);

        // Core schema is accepting plain string
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar("boom")));
        Assert.Equal(JsonSchema.BoolShortTag, schema.GetDefaultTag(new Scalar("True")));
        Assert.Equal(JsonSchema.BoolShortTag, schema.GetDefaultTag(new Scalar("TRUE")));
        Assert.Equal(JsonSchema.IntShortTag, schema.GetDefaultTag(new Scalar("0x10")));

        TryParse(schema, "TRUE", JsonSchema.BoolShortTag, true);
        TryParse(schema, "FALSE", JsonSchema.BoolShortTag, false);
        TryParse(schema, "0x10", JsonSchema.IntShortTag, 16);
        TryParse(schema, "16", JsonSchema.IntShortTag, 16);
        TryParse(schema, ".inf", JsonSchema.FloatShortTag, double.PositiveInfinity);
    }

    private static void TryParse(IYamlSchema schema, string scalar, string expectedShortTag, object? expectedValue)
    {
        Assert.True(schema.TryParse(new Scalar(scalar), true, out var tag, out var value));
        Assert.Equal(expectedShortTag, tag);
        Assert.Equal(expectedValue, value);
    }
}
