using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Schemas;

namespace Meziantou.Framework.Yaml.Tests;

/// <summary>
/// Comprehensive tests targeting YAML 1.2 specification edge cases across
/// schema resolution, parsing, scanning, and round-trip behavior.
/// </summary>
public sealed class Yaml12CoreTests
{
    // ───────────────────────────────────────────────────────────────────
    // YAML 1.2 §10.1 -  Failsafe Schema
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void FailsafeSchema_AllPlainScalarsResolveToStr()
    {
        var schema = new FailsafeSchema();
        // Even values that look like bool/int/null must resolve to !!str
        foreach (var text in new[] { "true", "false", "null", "42", "3.14", ".inf", ".nan", "~" })
        {
            var tag = schema.GetDefaultTag(new Scalar(text));
            Assert.Equal(SchemaBase.StrShortTag, tag, $"Failsafe should resolve '{text}' as !!str");
        }
    }

    [Fact]
    public void FailsafeSchema_QuotedScalarsResolveToStr()
    {
        var schema = new FailsafeSchema();
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar(null, null, "42", ScalarStyle.SingleQuoted, false, true)));
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar(null, null, "true", ScalarStyle.DoubleQuoted, false, true)));
    }

    // ───────────────────────────────────────────────────────────────────
    // YAML 1.2 §10.2 -  JSON Schema
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void JsonSchema_RejectsPlainStrings()
    {
        var schema = new JsonSchema();
        // JSON schema should NOT resolve arbitrary plain strings
        Assert.Null(schema.GetDefaultTag(new Scalar(null, null, "hello", ScalarStyle.Plain, true, false)));
    }

    [Fact]
    public void JsonSchema_NullOnlyLowercase()
    {
        var schema = new JsonSchema();
        Assert.Equal(JsonSchema.NullShortTag, schema.GetDefaultTag(new Scalar("null")));
        // JSON schema is strict: "Null" and "NULL" should NOT match
        Assert.Null(schema.GetDefaultTag(new Scalar(null, null, "Null", ScalarStyle.Plain, true, false)));
        Assert.Null(schema.GetDefaultTag(new Scalar(null, null, "NULL", ScalarStyle.Plain, true, false)));
    }

    [Fact]
    public void JsonSchema_BoolOnlyLowercase()
    {
        var schema = new JsonSchema();
        Assert.Equal(JsonSchema.BoolShortTag, schema.GetDefaultTag(new Scalar("true")));
        Assert.Equal(JsonSchema.BoolShortTag, schema.GetDefaultTag(new Scalar("false")));
        // "True", "TRUE" should NOT match in JSON schema
        Assert.Null(schema.GetDefaultTag(new Scalar(null, null, "True", ScalarStyle.Plain, true, false)));
        Assert.Null(schema.GetDefaultTag(new Scalar(null, null, "FALSE", ScalarStyle.Plain, true, false)));
    }

    [Fact]
    public void JsonSchema_IntegerEdgeCases()
    {
        var schema = new JsonSchema();
        Assert.Equal(JsonSchema.IntShortTag, schema.GetDefaultTag(new Scalar("0")));
        Assert.Equal(JsonSchema.IntShortTag, schema.GetDefaultTag(new Scalar("-1")));

        Assert.True(schema.TryParse(new Scalar("0"), true, out _, out var zeroVal));
        Assert.Equal(0, zeroVal);
    }

    [Fact]
    public void JsonSchema_FloatEdgeCases()
    {
        var schema = new JsonSchema();
        // .inf / -.inf / .nan
        Assert.True(schema.TryParse(new Scalar(".inf"), true, out _, out var posInf));
        Assert.Equal(double.PositiveInfinity, posInf);

        Assert.True(schema.TryParse(new Scalar("-.inf"), true, out _, out var negInf));
        Assert.Equal(double.NegativeInfinity, negInf);

        Assert.True(schema.TryParse(new Scalar(".nan"), true, out _, out var nan));
        Assert.True(double.IsNaN((double)nan!));

        // Scientific notation
        Assert.True(schema.TryParse(new Scalar("1e10"), true, out var tag, out var sci));
        Assert.Equal(JsonSchema.FloatShortTag, tag);
        Assert.Equal(1e10, sci);
    }

    [Fact]
    public void JsonSchema_QuotedValuesAlwaysStr()
    {
        var schema = new JsonSchema();
        // Even "null" or "true" when quoted should be !!str
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar(null, null, "null", ScalarStyle.DoubleQuoted, false, false)));
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar(null, null, "42", ScalarStyle.SingleQuoted, false, false)));
    }

    // ───────────────────────────────────────────────────────────────────
    // YAML 1.2 §10.3 -  Core Schema
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void CoreSchema_NullVariations()
    {
        var schema = new CoreSchema();

        Assert.True(schema.TryParse(new Scalar("null"), true, out var tag, out var val));
        Assert.Equal(JsonSchema.NullShortTag, tag);
        Assert.Null(val);

        Assert.True(schema.TryParse(new Scalar("Null"), true, out _, out _));
        Assert.True(schema.TryParse(new Scalar("NULL"), true, out _, out _));
        Assert.True(schema.TryParse(new Scalar("~"), true, out _, out _));
    }

    [Fact]
    public void CoreSchema_BoolCaseInsensitive()
    {
        var schema = new CoreSchema();
        foreach (var trueVal in new[] { "true", "True", "TRUE" })
        {
            Assert.True(schema.TryParse(new Scalar(trueVal), true, out _, out var val));
            Assert.Equal(true, val, $"'{trueVal}' should parse as true");
        }
        foreach (var falseVal in new[] { "false", "False", "FALSE" })
        {
            Assert.True(schema.TryParse(new Scalar(falseVal), true, out _, out var val));
            Assert.Equal(false, val, $"'{falseVal}' should parse as false");
        }
    }

    [Fact]
    public void CoreSchema_OctalIntegers()
    {
        var schema = new CoreSchema();
        Assert.True(schema.TryParse(new Scalar("0o10"), true, out var tag, out var val));
        Assert.Equal(JsonSchema.IntShortTag, tag);
        Assert.Equal(8, val);
    }

    [Fact]
    public void CoreSchema_HexIntegers()
    {
        var schema = new CoreSchema();

        Assert.True(schema.TryParse(new Scalar("0x10"), true, out _, out var val));
        Assert.Equal(16, val);

        Assert.True(schema.TryParse(new Scalar("0xFF"), true, out _, out var val2));
        Assert.Equal(255, val2);

        Assert.True(schema.TryParse(new Scalar("0xDEAD"), true, out _, out var val3));
        Assert.Equal(0xDEAD, val3);
    }

    [Fact]
    public void CoreSchema_IntegersWithUnderscores()
    {
        var schema = new CoreSchema();
        Assert.True(schema.TryParse(new Scalar("1_000"), true, out var tag, out var val));
        Assert.Equal(JsonSchema.IntShortTag, tag);
        Assert.Equal(1000, val);
    }

    [Fact]
    public void CoreSchema_FloatVariations()
    {
        var schema = new CoreSchema();

        // Positive infinity variations
        Assert.True(schema.TryParse(new Scalar(".inf"), true, out _, out var posInf));
        Assert.Equal(double.PositiveInfinity, posInf);

        Assert.True(schema.TryParse(new Scalar(".Inf"), true, out _, out var posInf2));
        Assert.Equal(double.PositiveInfinity, posInf2);

        Assert.True(schema.TryParse(new Scalar(".INF"), true, out _, out var posInf3));
        Assert.Equal(double.PositiveInfinity, posInf3);

        // Negative infinity
        Assert.True(schema.TryParse(new Scalar("-.inf"), true, out _, out var negInf));
        Assert.Equal(double.NegativeInfinity, negInf);

        Assert.True(schema.TryParse(new Scalar("-.Inf"), true, out _, out var negInf2));
        Assert.Equal(double.NegativeInfinity, negInf2);

        // NaN variations
        Assert.True(schema.TryParse(new Scalar(".nan"), true, out _, out var nan));
        Assert.True(double.IsNaN((double)nan!));

        Assert.True(schema.TryParse(new Scalar(".NaN"), true, out _, out var nan2));
        Assert.True(double.IsNaN((double)nan2!));

        Assert.True(schema.TryParse(new Scalar(".NAN"), true, out _, out var nan3));
        Assert.True(double.IsNaN((double)nan3!));
    }

    [Fact]
    public void CoreSchema_FloatWithExponent()
    {
        var schema = new CoreSchema();

        Assert.True(schema.TryParse(new Scalar("1.5e3"), true, out var tag, out var val));
        Assert.Equal(JsonSchema.FloatShortTag, tag);
        Assert.Equal(1500.0, val);

        Assert.True(schema.TryParse(new Scalar("2.5E-1"), true, out _, out var val2));
        Assert.Equal(0.25, val2);
    }

    [Fact]
    public void CoreSchema_PlainStringFallback()
    {
        var schema = new CoreSchema();
        // Unrecognized plain scalars should fall back to !!str
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar("hello world")));
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(new Scalar("not-a-bool")));
    }

    [Fact]
    public void CoreSchema_NegativeIntegers()
    {
        var schema = new CoreSchema();

        Assert.True(schema.TryParse(new Scalar("-42"), true, out var tag, out var val));
        Assert.Equal(JsonSchema.IntShortTag, tag);
        Assert.Equal(-42, val);

        Assert.True(schema.TryParse(new Scalar("+42"), true, out _, out var val2));
        Assert.Equal(42, val2);
    }

    // ───────────────────────────────────────────────────────────────────
    // Extended Schema -  Timestamps, bools, merge, binary integers
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtendedSchema_ExtendedBoolValues()
    {
        var schema = new ExtendedSchema();

        foreach (var trueVal in new[] { "y", "Y", "yes", "Yes", "YES", "on", "On", "ON", "true", "True", "TRUE" })
        {
            Assert.True(schema.TryParse(new Scalar(trueVal), true, out var tag, out var val), $"'{trueVal}' should be recognized");
            Assert.Equal(JsonSchema.BoolShortTag, tag, $"'{trueVal}' should be !!bool");
            Assert.Equal(true, val, $"'{trueVal}' should parse as true");
        }

        foreach (var falseVal in new[] { "n", "N", "no", "No", "NO", "off", "Off", "OFF", "false", "False", "FALSE" })
        {
            Assert.True(schema.TryParse(new Scalar(falseVal), true, out var tag, out var val), $"'{falseVal}' should be recognized");
            Assert.Equal(JsonSchema.BoolShortTag, tag, $"'{falseVal}' should be !!bool");
            Assert.Equal(false, val, $"'{falseVal}' should parse as false");
        }
    }

    [Fact]
    public void ExtendedSchema_BinaryIntegers()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("0b1010"), true, out var tag, out var val));
        Assert.Equal(JsonSchema.IntShortTag, tag);
        Assert.Equal(10, val);

        Assert.True(schema.TryParse(new Scalar("0b11111111"), true, out _, out var val2));
        Assert.Equal(255, val2);
    }

    [Fact]
    public void ExtendedSchema_NegativeBinaryIntegers()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("-0b1010"), true, out _, out var val));
        Assert.Equal(-10, val);
    }

    [Fact]
    public void ExtendedSchema_NegativeHexIntegers()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("-0x10"), true, out _, out var val));
        Assert.Equal(-16, val);
    }

    [Fact]
    public void ExtendedSchema_NegativeOctalIntegers()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("-0o10"), true, out _, out var val));
        Assert.Equal(-8, val);
    }

    [Fact]
    public void ExtendedSchema_MergeKey()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("<<"), true, out var tag, out var val));
        Assert.Equal(ExtendedSchema.MergeShortTag, tag);
        Assert.Equal("<<", val);
    }

    [Fact]
    public void ExtendedSchema_TimestampDateOnly()
    {
        var schema = new ExtendedSchema();
        Assert.True(schema.TryParse(new Scalar("2001-01-23"), true, out var tag, out var val));
        Assert.Equal(ExtendedSchema.TimestampShortTag, tag);
        Assert.Equal(new DateTime(2001, 1, 23), val);
    }

    [Fact]
    public void ExtendedSchema_TimestampWithTime()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("2001-12-15 02:59:43.1"), true, out var tag, out var val));
        Assert.Equal(ExtendedSchema.TimestampShortTag, tag);
        var dt = (DateTime)val!;
        Assert.Equal(2001, dt.Year);
        Assert.Equal(12, dt.Month);
        Assert.Equal(15, dt.Day);
        Assert.Equal(2, dt.Hour);
        Assert.Equal(59, dt.Minute);
        Assert.Equal(43, dt.Second);
    }

    [Fact]
    public void ExtendedSchema_TimestampWithMilliseconds()
    {
        var schema = new ExtendedSchema();
        Assert.True(schema.TryParse(new Scalar("2002-12-14 21:59:43.234"), true, out _, out var val));
        var dt = (DateTime)val!;
        Assert.Equal(234, dt.Millisecond);
    }

    [Fact]
    public void ExtendedSchema_NullVariationsIncludeEmptyAndTilde()
    {
        var schema = new ExtendedSchema();

        Assert.True(schema.TryParse(new Scalar("~"), true, out var tag, out _));
        Assert.Equal(JsonSchema.NullShortTag, tag);

        // Empty string matches the extended schema's null regex pattern (which includes empty)
        Assert.True(schema.TryParse(new Scalar(""), true, out var tag2, out _));
        Assert.Equal(JsonSchema.NullShortTag, tag2);
    }

    // ───────────────────────────────────────────────────────────────────
    // Tag Expansion / Shortening
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TagExpansion_AllSchemas()
    {
        var schemas = new IYamlSchema[] { new FailsafeSchema(), new JsonSchema(), new CoreSchema(), new ExtendedSchema() };

        foreach (var schema in schemas)
        {
            Assert.Equal("tag:yaml.org,2002:str", schema.ExpandTag("!!str"));
            Assert.Equal("tag:yaml.org,2002:map", schema.ExpandTag("!!map"));
            Assert.Equal("tag:yaml.org,2002:seq", schema.ExpandTag("!!seq"));
        }
    }

    [Fact]
    public void TagShortening_AllSchemas()
    {
        var schemas = new IYamlSchema[] { new FailsafeSchema(), new JsonSchema(), new CoreSchema(), new ExtendedSchema() };

        foreach (var schema in schemas)
        {
            Assert.Equal("!!str", schema.ShortenTag("tag:yaml.org,2002:str"));
            Assert.Equal("!!map", schema.ShortenTag("tag:yaml.org,2002:map"));
            Assert.Equal("!!seq", schema.ShortenTag("tag:yaml.org,2002:seq"));
        }
    }

    [Fact]
    public void TagExpansion_UnknownTagPassesThrough()
    {
        var schema = new CoreSchema();
        Assert.Equal("!custom", schema.ExpandTag("!custom"));
        Assert.Equal("tag:example.com,2024:foo", schema.ShortenTag("tag:example.com,2024:foo"));
    }

    [Fact]
    public void TagExpansion_NullReturnsNull()
    {
        var schema = new CoreSchema();
        Assert.Null(schema.ExpandTag(null));
        Assert.Null(schema.ShortenTag(null));
    }

    [Fact]
    public void ExtendedSchema_TimestampTagRegistered()
    {
        var schema = new ExtendedSchema();
        Assert.Equal(ExtendedSchema.TimestampLongTag, schema.ExpandTag(ExtendedSchema.TimestampShortTag));
        Assert.Equal(ExtendedSchema.TimestampShortTag, schema.ShortenTag(ExtendedSchema.TimestampLongTag));
    }

    [Fact]
    public void ExtendedSchema_MergeTagRegistered()
    {
        var schema = new ExtendedSchema();
        Assert.Equal(ExtendedSchema.MergeLongTag, schema.ExpandTag(ExtendedSchema.MergeShortTag));
        Assert.Equal(ExtendedSchema.MergeShortTag, schema.ShortenTag(ExtendedSchema.MergeLongTag));
    }

    // ───────────────────────────────────────────────────────────────────
    // Schema -  GetDefaultTag for Type
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void CoreSchema_TypeTagMappings()
    {
        var schema = new CoreSchema();
        Assert.Equal(JsonSchema.BoolShortTag, schema.GetDefaultTag(typeof(bool)));
        Assert.Equal(JsonSchema.IntShortTag, schema.GetDefaultTag(typeof(int)));
        Assert.Equal(JsonSchema.IntShortTag, schema.GetDefaultTag(typeof(long)));
        Assert.Equal(JsonSchema.FloatShortTag, schema.GetDefaultTag(typeof(float)));
        Assert.Equal(JsonSchema.FloatShortTag, schema.GetDefaultTag(typeof(double)));
        Assert.Equal(SchemaBase.StrShortTag, schema.GetDefaultTag(typeof(string)));
    }

    [Fact]
    public void Schema_GetTypeForDefaultTag()
    {
        var schema = new CoreSchema();
        Assert.Equal(typeof(bool), schema.GetTypeForDefaultTag(JsonSchema.BoolShortTag));
        Assert.Equal(typeof(int), schema.GetTypeForDefaultTag(JsonSchema.IntShortTag));
        Assert.Equal(typeof(string), schema.GetTypeForDefaultTag(SchemaBase.StrShortTag));
        Assert.Null(schema.GetTypeForDefaultTag("!!unknown"));
        Assert.Null(schema.GetTypeForDefaultTag(null));
    }

    [Fact]
    public void Schema_IsTagImplicit()
    {
        var schema = new CoreSchema();
        Assert.True(schema.IsTagImplicit("!!str"));
        Assert.True(schema.IsTagImplicit("!!int"));
        Assert.True(schema.IsTagImplicit(null));
        Assert.False(schema.IsTagImplicit("!custom"));
    }

    // ───────────────────────────────────────────────────────────────────
    // Parser -  Empty & Multi-document Streams
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_EmptyInput_ProducesStreamStartEnd()
    {
        var events = ParseAll("");
        Assert.HasCount(2, events);
        Assert.IsType<StreamStart>(events[0]);
        Assert.IsType<StreamEnd>(events[1]);
    }

    [Fact]
    public void Parser_SingleDocumentImplicit()
    {
        var events = ParseAll("hello\n");
        AssertDocumentContainsScalar(events, "hello");
    }

    [Fact]
    public void Parser_SingleDocumentExplicit()
    {
        var events = ParseAll("---\nhello\n...\n");
        AssertDocumentContainsScalar(events, "hello");
        var docEnd = events.OfType<DocumentEnd>().First();
        Assert.False(docEnd.IsImplicit);
    }

    [Fact]
    public void Parser_MultipleDocuments()
    {
        var events = ParseAll("---\nfoo\n---\nbar\n---\nbaz\n");
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(3, scalars);
        Assert.Equal("foo", scalars[0].Value);
        Assert.Equal("bar", scalars[1].Value);
        Assert.Equal("baz", scalars[2].Value);
    }

    [Fact]
    public void Parser_DocumentEndMarker()
    {
        // After "...", a new document requires "---"
        var events = ParseAll("foo\n...\n---\nbar\n");
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(2, scalars);
        Assert.Equal("foo", scalars[0].Value);
        Assert.Equal("bar", scalars[1].Value);
    }

    [Fact]
    public void Parser_DocumentEndMarkerWithoutNewDocStart_ThrowsException()
    {
        // Bare content after "..." without "---" is an error
        Assert.ThrowsAny<YamlException>(() => ParseAll("foo\n...\nbar\n"));
    }

    [Fact]
    public void Parser_ImplicitThenExplicitDocument()
    {
        var events = ParseAll("first\n---\nsecond\n");
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(2, scalars);
        Assert.Equal("first", scalars[0].Value);
        Assert.Equal("second", scalars[1].Value);
    }

    [Fact]
    public void Parser_EmptyExplicitDocument()
    {
        var events = ParseAll("---\n...\n");
        // Should have StreamStart, DocStart, empty Scalar, DocEnd, StreamEnd
        var docStarts = events.OfType<DocumentStart>().ToList();
        Assert.Single(docStarts);
    }

    // ───────────────────────────────────────────────────────────────────
    // Scanner/Parser -  Block Scalar Indicators
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BlockScalar_LiteralClip()
    {
        // Default chomping (clip): single trailing newline
        const string Yaml = "data: |\n  line1\n  line2\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("line1\nline2\n", value);
    }

    [Fact]
    public void BlockScalar_LiteralStrip()
    {
        // Strip chomping: no trailing newline
        const string Yaml = "data: |-\n  line1\n  line2\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("line1\nline2", value);
    }

    [Fact]
    public void BlockScalar_LiteralKeep()
    {
        // Keep chomping: preserve all trailing newlines
        const string Yaml = "data: |+\n  line1\n  line2\n\n\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("line1\nline2\n\n\n", value);
    }

    [Fact]
    public void BlockScalar_FoldedClip()
    {
        const string Yaml = "data: >\n  folded\n  text\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("folded text\n", value);
    }

    [Fact]
    public void BlockScalar_FoldedStrip()
    {
        const string Yaml = "data: >-\n  folded\n  text\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("folded text", value);
    }

    [Fact]
    public void BlockScalar_FoldedKeep()
    {
        const string Yaml = "data: >+\n  folded\n  text\n\n\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("folded text\n\n\n", value);
    }

    [Fact]
    public void BlockScalar_ExplicitIndentation()
    {
        // |2 means content is indented 2 spaces from block header's column
        const string Yaml = "data: |2\n  text\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("text\n", value);
    }

    [Fact]
    public void BlockScalar_LiteralPreservesBlankLines()
    {
        const string Yaml = "data: |\n  line1\n\n  line3\n";
        var value = ParseSingleMappingValue(Yaml);
        Assert.Equal("line1\n\nline3\n", value);
    }

    // ───────────────────────────────────────────────────────────────────
    // Scanner -  Escape Sequences
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DoubleQuoted_BasicEscapeSequences()
    {
        var cases = new (string Yaml, string Expected)[]
        {
            ("\"\\n\"", "\n"),
            ("\"\\t\"", "\t"),
            ("\"\\\\\"", "\\"),
            ("\"\\\"\"", "\""),
            ("\"\\0\"", "\0"),
            ("\"\\a\"", "\a"),
            ("\"\\b\"", "\b"),
            ("\"\\r\"", "\r"),
            ("\"\\e\"", "\x1B"),
            ("\"\\/\"", "/"),
            ("\"\\x41\"", "A"),
            ("\"\\u0041\"", "A"),
            ("\"\\U00000041\"", "A"),
        };

        foreach (var (yaml, expected) in cases)
        {
            var scalar = ParseSingleScalar(yaml + "\n");
            Assert.Equal(expected, scalar, $"Escape in {yaml} failed");
        }
    }

    [Fact]
    public void DoubleQuoted_UnicodeEscapes()
    {
        // 2-digit hex
        Assert.Equal("\x7F", ParseSingleScalar("\"\\x7F\"\n"));

        // 4-digit unicode
        Assert.Equal("\u00E9", ParseSingleScalar("\"\\u00E9\"\n")); // é

        // 8-digit unicode (emoji)
        Assert.Equal("\U0001F600", ParseSingleScalar("\"\\U0001F600\"\n"));
    }

    [Fact]
    public void DoubleQuoted_SpecialUnicodeEscapes()
    {
        // \N = next line (U+0085)
        Assert.Equal("\u0085", ParseSingleScalar("\"\\N\"\n"));

        // \_ = non-breaking space (U+00A0)
        Assert.Equal("\u00A0", ParseSingleScalar("\"\\_\"\n"));

        // \L = line separator (U+2028)
        Assert.Equal("\u2028", ParseSingleScalar("\"\\L\"\n"));

        // \P = paragraph separator (U+2029)
        Assert.Equal("\u2029", ParseSingleScalar("\"\\P\"\n"));
    }

    [Fact]
    public void DoubleQuoted_InvalidEscapeThrows()
    {
        Assert.Throws<SyntaxErrorException>(() => ParseSingleScalar("\"\\q\"\n"));
    }

    [Fact]
    public void DoubleQuoted_EscapedLineBreakIsSkipped()
    {
        // A backslash before a newline in double-quoted means line continuation
        var yaml = "\"line\\\n  continuation\"\n";
        var scalar = ParseSingleScalar(yaml);
        Assert.Equal("linecontinuation", scalar);
    }

    [Fact]
    public void SingleQuoted_DoubledQuoteEscape()
    {
        var yaml = "'it''s'\n";
        var scalar = ParseSingleScalar(yaml);
        Assert.Equal("it's", scalar);
    }

    [Fact]
    public void SingleQuoted_NoBackslashEscapes()
    {
        // In single-quoted, backslash is literal
        var yaml = "'hello\\nworld'\n";
        var scalar = ParseSingleScalar(yaml);
        Assert.Equal("hello\\nworld", scalar);
    }

    // ───────────────────────────────────────────────────────────────────
    // Flow Collections
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void FlowSequence_Empty()
    {
        var events = ParseAll("[]\n");
        var seqStarts = events.OfType<SequenceStart>().ToList();
        var seqEnds = events.OfType<SequenceEnd>().ToList();
        Assert.Single(seqStarts);
        Assert.Single(seqEnds);
    }

    [Fact]
    public void FlowMapping_Empty()
    {
        var events = ParseAll("{}\n");
        var mapStarts = events.OfType<MappingStart>().ToList();
        var mapEnds = events.OfType<MappingEnd>().ToList();
        Assert.Single(mapStarts);
        Assert.Single(mapEnds);
    }

    [Fact]
    public void FlowSequence_NestedInMapping()
    {
        const string Yaml = "key: [1, 2, 3]\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(4, scalars); // "key", "1", "2", "3"
    }

    [Fact]
    public void FlowMapping_NestedInSequence()
    {
        const string Yaml = "- {a: 1, b: 2}\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(4, scalars); // "a", "1", "b", "2"
    }

    [Fact]
    public void FlowSequence_Nested()
    {
        const string Yaml = "[[1, 2], [3, 4]]\n";
        var events = ParseAll(Yaml);
        var seqStarts = events.OfType<SequenceStart>().ToList();
        Assert.HasCount(3, seqStarts); // outer + 2 inner
    }

    [Fact]
    public void FlowMapping_NestedInFlowMapping()
    {
        const string Yaml = "{outer: {inner: value}}\n";
        var events = ParseAll(Yaml);
        var mapStarts = events.OfType<MappingStart>().ToList();
        Assert.HasCount(2, mapStarts);
    }

    // ───────────────────────────────────────────────────────────────────
    // Anchors & Aliases
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AnchorAlias_OnScalar()
    {
        const string Yaml = "a: &anchor value\nb: *anchor\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        var anchored = scalars.First(s => s.Anchor == "anchor");
        Assert.Equal("value", anchored.Value);
        var alias = events.OfType<AnchorAlias>().First();
        Assert.Equal("anchor", alias.Value);
    }

    [Fact]
    public void AnchorAlias_OnSequence()
    {
        const string Yaml = "a: &seq\n  - 1\n  - 2\nb: *seq\n";
        var events = ParseAll(Yaml);
        var seqStart = events.OfType<SequenceStart>().First();
        Assert.Equal("seq", seqStart.Anchor);
        var alias = events.OfType<AnchorAlias>().First();
        Assert.Equal("seq", alias.Value);
    }

    [Fact]
    public void AnchorAlias_OnMapping()
    {
        const string Yaml = "a: &map\n  x: 1\n  y: 2\nb: *map\n";
        var events = ParseAll(Yaml);
        var mapStart = events.OfType<MappingStart>().First(m => m.Anchor == "map");
        Assert.NotNull(mapStart);
        var alias = events.OfType<AnchorAlias>().First();
        Assert.Equal("map", alias.Value);
    }

    // ───────────────────────────────────────────────────────────────────
    // Comments
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Comments_AreIgnoredByParser()
    {
        const string Yaml = "# This is a comment\nkey: value # inline comment\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(2, scalars);
        Assert.Equal("key", scalars[0].Value);
        Assert.Equal("value", scalars[1].Value);
    }

    [Fact]
    public void CommentOnlyDocument()
    {
        var events = ParseAll("# just a comment\n");
        // Should produce stream start + end only (or empty document)
        Assert.IsType<StreamStart>(events[0]);
        Assert.IsType<StreamEnd>(events.Last());
    }

    // ───────────────────────────────────────────────────────────────────
    // Special Key Edge Cases
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyValueInMapping()
    {
        const string Yaml = "key:\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(2, scalars);
        Assert.Equal("key", scalars[0].Value);
        Assert.Equal("", scalars[1].Value); // empty value
    }

    [Fact]
    public void BoolLikeKeys()
    {
        // Keys that look like booleans should still be parsed as scalars
        const string Yaml = "true: 1\nfalse: 0\nnull: nothing\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(6, scalars);
        Assert.Equal("true", scalars[0].Value);
        Assert.Equal("false", scalars[2].Value);
        Assert.Equal("null", scalars[4].Value);
    }

    [Fact]
    public void NumericKeys()
    {
        const string Yaml = "42: value\n3.14: pi\n";
        var events = ParseAll(Yaml);
        var scalars = events.OfType<Scalar>().ToList();
        Assert.HasCount(4, scalars);
        Assert.Equal("42", scalars[0].Value);
        Assert.Equal("3.14", scalars[2].Value);
    }

    // ───────────────────────────────────────────────────────────────────
    // YamlStream Model -  Round-Trip Tests
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Model_SimpleMapping_RoundTrip()
    {
        const string Yaml = "name: John\nage: 30\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var mapping = (YamlMapping)stream[0].Contents!;

        Assert.Equal("John", mapping["name"]!.ToObject<string>());
        Assert.Equal(30, mapping["age"]!.ToObject<int>());

        using var output = new StringWriter();
        stream.WriteTo(output, true);
        var reparsed = YamlStream.Load(new StringReader(output.ToString()));
        var remapping = (YamlMapping)reparsed[0].Contents!;
        Assert.Equal("John", remapping["name"]!.ToObject<string>());
    }

    [Fact]
    public void Model_Sequence_RoundTrip()
    {
        const string Yaml = "- alpha\n- beta\n- gamma\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var seq = (YamlSequence)stream[0].Contents!;

        Assert.HasCount(3, seq);
        Assert.Equal("alpha", ((YamlValue)seq[0]).Value);
        Assert.Equal("gamma", ((YamlValue)seq[2]).Value);
    }

    [Fact]
    public void Model_NestedStructure_RoundTrip()
    {
        const string Yaml = "root:\n  child:\n    - item1\n    - item2\n  value: 42\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        using var output = new StringWriter();
        stream.WriteTo(output, true);
        var reparsed = YamlStream.Load(new StringReader(output.ToString()));

        var rootMap = (YamlMapping)reparsed[0].Contents!;
        var innerMap = (YamlMapping)rootMap["root"]!;
        var child = (YamlSequence)innerMap["child"]!;
        Assert.HasCount(2, child);
        Assert.Equal("item1", ((YamlValue)child[0]).Value);
    }

    [Fact]
    public void Model_MultiDocument_RoundTrip()
    {
        const string Yaml = "---\nfirst\n---\nsecond\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        Assert.HasCount(2, stream);
        Assert.Equal("first", ((YamlValue)stream[0].Contents!).Value);
        Assert.Equal("second", ((YamlValue)stream[1].Contents!).Value);
    }

    [Fact]
    public void Model_EmptyMapping()
    {
        const string Yaml = "{}\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var mapping = (YamlMapping)stream[0].Contents!;
        Assert.Empty(mapping);
    }

    [Fact]
    public void Model_EmptySequence()
    {
        const string Yaml = "[]\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var seq = (YamlSequence)stream[0].Contents!;
        Assert.Empty(seq);
    }

    // ───────────────────────────────────────────────────────────────────
    // Serializer -  Values that need quoting
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Serializer_BoolLikeStrings_AreQuoted()
    {
        var yaml = YamlSerializer.Serialize("true");
        // The value "true" serialized as a string should be quoted
        var result = YamlSerializer.Deserialize<string>(yaml);
        Assert.Equal("true", result);
    }

    [Fact]
    public void Serializer_NullLikeStrings_AreQuoted()
    {
        var yaml = YamlSerializer.Serialize("null");
        var result = YamlSerializer.Deserialize<string>(yaml);
        Assert.Equal("null", result);
    }

    [Fact]
    public void Serializer_NumericLikeStrings_AreQuoted()
    {
        var yaml = YamlSerializer.Serialize("42");
        var result = YamlSerializer.Deserialize<string>(yaml);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Serializer_SpecialFloatStrings_AreQuoted()
    {
        foreach (var val in new[] { ".inf", "-.inf", ".nan" })
        {
            var yaml = YamlSerializer.Serialize(val);
            var result = YamlSerializer.Deserialize<string>(yaml);
            Assert.Equal(val, result, $"Round-trip of string '{val}' failed");
        }
    }

    [Fact]
    public void Serializer_EmptyString_RoundTrips()
    {
        var yaml = YamlSerializer.Serialize("");
        var result = YamlSerializer.Deserialize<string>(yaml);
        Assert.Equal("", result);
    }

    [Fact]
    public void Serializer_NullValue_SerializesToNullLiteral()
    {
        var yaml = YamlSerializer.Serialize<string?>(null);
        Assert.Contains("null", yaml);

        // When deserializing "null" as object, it returns null
        var objResult = YamlSerializer.Deserialize<object>(yaml);
        Assert.Null(objResult);
    }

    // ───────────────────────────────────────────────────────────────────
    // Emitter -  Additional edge cases
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Emitter_AnchorAlias_RoundTrip()
    {
        var output = new StringWriter();
        var emitter = new Emitter(output);

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart(null, null, true));
        emitter.Emit(new MappingStart());
        emitter.Emit(new Scalar("a"));
        emitter.Emit(new Scalar("anchor1", null, "value", ScalarStyle.Plain, true, false));
        emitter.Emit(new Scalar("b"));
        emitter.Emit(new AnchorAlias("anchor1"));
        emitter.Emit(new MappingEnd());
        emitter.Emit(new DocumentEnd(true));
        emitter.Emit(new StreamEnd());

        var yaml = output.ToString();
        Assert.Contains("&anchor1", yaml);
        Assert.Contains("*anchor1", yaml);
    }

    [Fact]
    public void Emitter_MultipleDocuments()
    {
        var output = new StringWriter();
        var emitter = new Emitter(output);

        emitter.Emit(new StreamStart());

        emitter.Emit(new DocumentStart(null, null, false));
        emitter.Emit(new Scalar("first"));
        emitter.Emit(new DocumentEnd(false));

        emitter.Emit(new DocumentStart(null, null, false));
        emitter.Emit(new Scalar("second"));
        emitter.Emit(new DocumentEnd(false));

        emitter.Emit(new StreamEnd());

        var yaml = output.ToString();
        Assert.Contains("---", yaml);
        Assert.Contains("first", yaml);
        Assert.Contains("second", yaml);
        Assert.Contains("...", yaml);
    }

    [Fact]
    public void Emitter_NestedFlowCollections()
    {
        var output = new StringWriter();
        var emitter = new Emitter(output);

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart(null, null, true));
        emitter.Emit(new SequenceStart(null, null, true, YamlStyle.Flow));
        emitter.Emit(new SequenceStart(null, null, true, YamlStyle.Flow));
        emitter.Emit(new Scalar("a"));
        emitter.Emit(new Scalar("b"));
        emitter.Emit(new SequenceEnd());
        emitter.Emit(new SequenceStart(null, null, true, YamlStyle.Flow));
        emitter.Emit(new Scalar("c"));
        emitter.Emit(new SequenceEnd());
        emitter.Emit(new SequenceEnd());
        emitter.Emit(new DocumentEnd(true));
        emitter.Emit(new StreamEnd());

        var yaml = output.ToString();
        Assert.Contains("[[a, b], [c]]", yaml);
    }

    // ───────────────────────────────────────────────────────────────────
    // Complex YAML structures -  integration-level parsing tests
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ComplexNestedDocument()
    {
        const string Yaml = @"server:
  host: localhost
  port: 8080
  features:
    - auth
    - logging
  database:
    host: db.local
    port: 5432
";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var root = (YamlMapping)stream[0].Contents!;
        var server = (YamlMapping)root["server"]!;
        Assert.Equal("localhost", server["host"]!.ToObject<string>());
        Assert.Equal(8080, server["port"]!.ToObject<int>());

        var features = (YamlSequence)server["features"]!;
        Assert.HasCount(2, features);

        var db = (YamlMapping)server["database"]!;
        Assert.Equal("db.local", db["host"]!.ToObject<string>());
    }

    [Fact]
    public void Parse_MixedFlowAndBlockCollections()
    {
        const string Yaml = "block:\n  flow: {a: 1, b: [2, 3]}\n  plain: text\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var root = (YamlMapping)stream[0].Contents!;
        var block = (YamlMapping)root["block"]!;
        var flow = (YamlMapping)block["flow"]!;
        Assert.Equal("1", flow["a"]!.ToObject<string>());
    }

    [Fact]
    public void Parse_MultilineBlockScalar_Literal()
    {
        const string Yaml = "description: |\n  This is a\n  multi-line\n  description.\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var root = (YamlMapping)stream[0].Contents!;
        var value = ((YamlValue)root["description"]!).Value;
        Assert.Equal("This is a\nmulti-line\ndescription.\n", value);
    }

    [Fact]
    public void Parse_MultilineFoldedScalar()
    {
        const string Yaml = "summary: >\n  This is a\n  folded\n  paragraph.\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var root = (YamlMapping)stream[0].Contents!;
        var value = ((YamlValue)root["summary"]!).Value;
        Assert.Equal("This is a folded paragraph.\n", value);
    }

    [Fact]
    public void Parse_BlockScalar_FoldedWithBlankLines()
    {
        // Blank lines in folded scalars become literal newlines
        const string Yaml = "text: >\n  paragraph1\n\n  paragraph2\n";
        var stream = YamlStream.Load(new StringReader(Yaml));
        var root = (YamlMapping)stream[0].Contents!;
        var value = ((YamlValue)root["text"]!).Value;
        Assert.Equal("paragraph1\nparagraph2\n", value);
    }

    // ───────────────────────────────────────────────────────────────────
    // Error handling
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_UnclosedFlowSequence_ThrowsException()
    {
        Assert.ThrowsAny<YamlException>(() => ParseAll("[1, 2\n"));
    }

    [Fact]
    public void Parse_UnclosedFlowMapping_ThrowsException()
    {
        Assert.ThrowsAny<YamlException>(() => ParseAll("{a: 1\n"));
    }

    [Fact]
    public void Parse_DuplicateAnchor_Succeeds()
    {
        // YAML allows redefining anchors; the last definition wins
        const string Yaml = "a: &x 1\nb: &x 2\nc: *x\n";
        var events = ParseAll(Yaml);
        var alias = events.OfType<AnchorAlias>().First();
        Assert.Equal("x", alias.Value);
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static List<ParsingEvent> ParseAll(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        var events = new List<ParsingEvent>();
        while (parser.MoveNext())
        {
            Assert.NotNull(parser.Current);
            events.Add(parser.Current);
        }
        return events;
    }

    private static string ParseSingleScalar(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar scalar)
            {
                return scalar.Value;
            }
        }
        throw new InvalidOperationException("No scalar found in YAML");
    }

    private static string ParseSingleMappingValue(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        Scalar? lastScalar = null;
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar scalar)
            {
                if (lastScalar is not null)
                {
                    return scalar.Value;
                }
                lastScalar = scalar;
            }
        }
        throw new InvalidOperationException("No mapping value found in YAML");
    }

    private static void AssertDocumentContainsScalar(List<ParsingEvent> events, string expectedValue)
    {
        var scalar = events.OfType<Scalar>().FirstOrDefault(s => s.Value == expectedValue);
        Assert.NotNull(scalar, $"Expected scalar '{expectedValue}' not found");
    }
}
