using System.Collections;
using System.Text.Json;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Tests;

public class ParserTests : ParserTestHelper
{
    public static TheoryData<string, string, string, bool> ConformanceCases()
    {
        using var stream = typeof(ParserTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Yaml.Tests.files.yaml-test-suite.cases.json")!;
        using var document = JsonDocument.Parse(stream);
        var result = new TheoryData<string, string, string, bool>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            result.Add(item.GetProperty("id").GetString()!, item.GetProperty("yaml").GetString()!, item.GetProperty("events").GetString()!, item.GetProperty("error").GetBoolean());
        }

        return result;
    }

    [Theory]
    [MemberData(nameof(ConformanceCases))]
    public void YamlTestSuite_StringParser(string id, string yaml, string expectedEvents, bool invalid)
    {
        _ = id;
        using var reader = new StringReader(yaml);
        AssertConformance(Parser.CreateParser(reader), expectedEvents, invalid);
    }

    [Theory]
    [MemberData(nameof(ConformanceCases))]
    public void YamlTestSuite_BufferedParser(string id, string yaml, string expectedEvents, bool invalid)
    {
        _ = id;
        using var reader = new StringReader(yaml);
        AssertConformance(new Parser<LookAheadBuffer>(new LookAheadBuffer(reader, 12)), expectedEvents, invalid);
    }

    [Theory]
    [InlineData("\\x0")]
    [InlineData("\\u123")]
    [InlineData("\\U00110000")]
    [InlineData("\\UFFFFFFFF")]
    [InlineData("\\uD800")]
    [InlineData("\\uDC00")]
    [InlineData("\\uD800\\u0041")]
    [InlineData("\\uDC00\\uD800")]
    [InlineData("\\'")]
    public void InvalidUnicodeEscapes_ThrowYamlException(string escape)
    {
        using var reader = new StringReader("\"" + escape + "\"");
        Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(Parser.CreateParser(reader)));
    }

    [Theory]
    [InlineData("%C0%AF")]
    [InlineData("%ED%A0%80")]
    [InlineData("%F4%90%80%80")]
    [InlineData("%FF")]
    [InlineData("%E2%82")]
    [InlineData("%GG")]
    public void InvalidTagUtf8Escapes_ThrowYamlException(string escape)
    {
        using var reader = new StringReader("!<tag:example.com," + escape + "> value");
        Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(Parser.CreateParser(reader)));
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x0B)]
    [InlineData(0x1F)]
    [InlineData(0x7F)]
    [InlineData(0x9F)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void NonPrintableInput_ThrowsYamlException(int codePoint)
    {
        var character = ((char)codePoint).ToString();
        foreach (var yaml in new[] { "a" + character, "'a" + character + "'", "\"a" + character + "\"", "|\n  a" + character, "# a" + character })
        {
            using var reader = new StringReader(yaml);
            Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(Parser.CreateParser(reader)));
            using var bufferedReader = new StringReader(yaml);
            Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(new Parser<LookAheadBuffer>(new LookAheadBuffer(bufferedReader, 12))));
        }
    }

    [Theory]
    [InlineData("\uFEFF---\nvalue", "+STR\n+DOC ---\n=VAL :value\n-DOC\n-STR")]
    [InlineData("\uFEFFkey: value", "+STR\n+DOC\n+MAP\n=VAL :key\n=VAL :value\n-MAP\n-DOC\n-STR")]
    [InlineData("first\n...\n\uFEFF---\nsecond", "+STR\n+DOC\n=VAL :first\n-DOC ...\n+DOC ---\n=VAL :second\n-DOC\n-STR")]
    [InlineData("'a\uFEFFb'", "+STR\n+DOC\n=VAL 'a\uFEFFb\n-DOC\n-STR")]
    [InlineData("\"a\uFEFFb\"", "+STR\n+DOC\n=VAL \"a\uFEFFb\n-DOC\n-STR")]
    public void ByteOrderMarks_AreAllowedInDocumentPrefixesAndQuotedScalars(string yaml, string expected)
    {
        using var reader = new StringReader(yaml);
        Assert.Equal(expected, ReadConformanceEvents(Parser.CreateParser(reader)));
        using var bufferedReader = new StringReader(yaml);
        Assert.Equal(expected, ReadConformanceEvents(new Parser<LookAheadBuffer>(new LookAheadBuffer(bufferedReader, 12))));
    }

    [Theory]
    [InlineData("a\uFEFFb")]
    [InlineData("|\n  a\uFEFFb")]
    [InlineData(">\n  a\uFEFFb")]
    public void ByteOrderMarks_AreRejectedInsideUnquotedScalars(string yaml)
    {
        using var reader = new StringReader(yaml);
        Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(Parser.CreateParser(reader)));
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, false)]
    [InlineData(1025, false)]
    [InlineData(1023, true)]
    [InlineData(1024, true)]
    [InlineData(1025, true)]
    public void ImplicitKeyLengthLimit_CountsUnicodeCharacters(int length, bool supplementary)
    {
        var key = supplementary ? string.Concat(Enumerable.Repeat("\U0001F600", length)) : new string('a', length);
        var yaml = key + ": value\n";
        using var reader = new StringReader(yaml);
        var parser = Parser.CreateParser(reader);
        if (length > 1024)
        {
            Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(parser));
        }
        else
        {
            Assert.Equal("+STR\n+DOC\n+MAP\n=VAL :" + key + "\n=VAL :value\n-MAP\n-DOC\n-STR", ReadConformanceEvents(parser));
        }
    }

    private static void AssertConformance(IParser parser, string expectedEvents, bool invalid)
    {
        if (invalid)
        {
            Assert.ThrowsAny<YamlException>(() => ReadConformanceEvents(parser));
        }
        else
        {
            Assert.Equal(expectedEvents, ReadConformanceEvents(parser));
            Assert.False(parser.MoveNext());
        }
    }

    private static string ReadConformanceEvents(IParser parser)
    {
        var events = new List<string>();
        while (parser.MoveNext())
        {
            var current = parser.Current;
            var properties = current is NodeEvent node
                ? (node.Anchor is null ? "" : " &" + node.Anchor) + (node.Tag is null ? "" : " <" + node.Tag + ">")
                : "";
            events.Add(current switch
            {
                Events.StreamStart => "+STR",
                Events.StreamEnd => "-STR",
                Events.DocumentStart start => start.IsImplicit ? "+DOC" : "+DOC ---",
                Events.DocumentEnd end => end.IsImplicit ? "-DOC" : "-DOC ...",
                Events.MappingStart mapping => "+MAP" + (mapping.Style is YamlStyle.Flow ? " {}" : "") + properties,
                Events.MappingEnd => "-MAP",
                Events.SequenceStart sequence => "+SEQ" + (sequence.Style is YamlStyle.Flow ? " []" : "") + properties,
                Events.SequenceEnd => "-SEQ",
                Events.AnchorAlias alias => "=ALI *" + alias.Value,
                Events.Scalar scalar => "=VAL" + properties + " " + ScalarIndicator(scalar.Style) + EscapeEventValue(scalar.Value),
                _ => throw new InvalidOperationException($"Unexpected event: {current}"),
            });
        }

        return string.Join('\n', events);
    }

    private static char ScalarIndicator(ScalarStyle style) => style switch
    {
        ScalarStyle.Plain => ':',
        ScalarStyle.SingleQuoted => '\'',
        ScalarStyle.DoubleQuoted => '"',
        ScalarStyle.Literal => '|',
        ScalarStyle.Folded => '>',
        _ => throw new InvalidOperationException($"Unexpected scalar style: {style}"),
    };

    private static string EscapeEventValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\0", "\\0", StringComparison.Ordinal)
            .Replace("\b", "\\b", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDocument()
    {
        AssertSequenceOfEventsFrom(ParserFor("empty.yaml"),
            StreamStart,
            StreamEnd);
    }

    [Fact]
    public void VerifyEventsOnExample1()
    {
        AssertSequenceOfEventsFrom(ParserFor("test1.yaml"),
            StreamStart,
            DocumentStart(Explicit, Version(1, 1),
                TagDirective("!", "!foo"),
                TagDirective("!yaml!", TagYaml),
                TagDirective("!!", TagYaml)),
            PlainScalar(string.Empty),
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample2()
    {
        AssertSequenceOfEventsFrom(ParserFor("test2.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            SingleQuotedScalar("a scalar"),
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample3()
    {
        AssertSequenceOfEventsFrom(ParserFor("test3.yaml"),
            StreamStart,
            DocumentStart(Explicit),
            SingleQuotedScalar("a scalar"),
            DocumentEnd(Explicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample4()
    {
        AssertSequenceOfEventsFrom(ParserFor("test4.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            SingleQuotedScalar("a scalar"),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            SingleQuotedScalar("another scalar"),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            SingleQuotedScalar("yet another scalar"),
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample5()
    {
        AssertSequenceOfEventsFrom(ParserFor("test5.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            AnchoredFlowSequenceStart("A"),
            AnchorAlias("A"),
            SequenceEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample6()
    {
        var parser = ParserFor("test6.yaml");
        AssertSequenceOfEventsFrom(parser,
            StreamStart,
            DocumentStart(Implicit),
            ExplicitDoubleQuotedScalar(TagYaml + "float", "3.14"),
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample7()
    {
        AssertSequenceOfEventsFrom(ParserFor("test7.yaml"),
            StreamStart,
            DocumentStart(Explicit),
            PlainScalar(string.Empty),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            PlainScalar("a plain scalar"),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            SingleQuotedScalar("a single-quoted scalar"),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            DoubleQuotedScalar("a double-quoted scalar"),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            LiteralScalar("a literal scalar"),
            DocumentEnd(Implicit),
            DocumentStart(Explicit),
            FoldedScalar("a folded scalar"),
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample8()
    {
        AssertSequenceOfEventsFrom(ParserFor("test8.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            FlowSequenceStart,
            PlainScalar("item 1"),
            PlainScalar("item 2"),
            PlainScalar("item 3"),
            SequenceEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample9()
    {
        AssertSequenceOfEventsFrom(ParserFor("test9.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            FlowMappingStart,
            PlainScalar("a simple key"),
            PlainScalar("a value"),
            PlainScalar("a complex key"),
            PlainScalar("another value"),
            MappingEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample10()
    {
        AssertSequenceOfEventsFrom(ParserFor("test10.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            BlockSequenceStart,
            PlainScalar("item 1"),
            PlainScalar("item 2"),
            BlockSequenceStart,
            PlainScalar("item 3.1"),
            PlainScalar("item 3.2"),
            SequenceEnd,
            BlockMappingStart,
            PlainScalar("key 1"),
            PlainScalar("value 1"),
            PlainScalar("key 2"),
            PlainScalar("value 2"),
            MappingEnd,
            SequenceEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample11()
    {
        AssertSequenceOfEventsFrom(ParserFor("test11.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            BlockMappingStart,
            PlainScalar("a simple key"),
            PlainScalar("a value"),
            PlainScalar("a complex key"),
            PlainScalar("another value"),
            PlainScalar("a mapping"),
            BlockMappingStart,
            PlainScalar("key 1"),
            PlainScalar("value 1"),
            PlainScalar("key 2"),
            PlainScalar("value 2"),
            MappingEnd,
            PlainScalar("a sequence"),
            BlockSequenceStart,
            PlainScalar("item 1"),
            PlainScalar("item 2"),
            SequenceEnd,
            MappingEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample12()
    {
        AssertSequenceOfEventsFrom(ParserFor("test12.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            BlockSequenceStart,
            BlockSequenceStart,
            PlainScalar("item 1"),
            PlainScalar("item 2"),
            SequenceEnd,
            BlockMappingStart,
            PlainScalar("key 1"),
            PlainScalar("value 1"),
            PlainScalar("key 2"),
            PlainScalar("value 2"),
            MappingEnd,
            BlockMappingStart,
            PlainScalar("complex key"),
            PlainScalar("complex value"),
            MappingEnd,
            SequenceEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample13()
    {
        AssertSequenceOfEventsFrom(ParserFor("test13.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            BlockMappingStart,
            PlainScalar("a sequence"),
            BlockSequenceStart,
            PlainScalar("item 1"),
            PlainScalar("item 2"),
            SequenceEnd,
            PlainScalar("a mapping"),
            BlockMappingStart,
            PlainScalar("key 1"),
            PlainScalar("value 1"),
            PlainScalar("key 2"),
            PlainScalar("value 2"),
            MappingEnd,
            MappingEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokensOnExample14()
    {
        AssertSequenceOfEventsFrom(ParserFor("test14.yaml"),
            StreamStart,
            DocumentStart(Implicit),
            BlockMappingStart,
            PlainScalar("key"),
            BlockSequenceStart,
            PlainScalar("item 1"),
            PlainScalar("item 2"),
            SequenceEnd,
            MappingEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    [Fact]
    public void VerifyTokenWithLocalTags()
    {
        AssertSequenceOfEventsFrom(ParserFor("local-tags.yaml"),
            StreamStart,
            DocumentStart(Explicit),
            TaggedBlockMappingStart("!MyObject"),
            PlainScalar("a"),
            PlainScalar("1.0"),
            PlainScalar("b"),
            PlainScalar("42"),
            PlainScalar("c"),
            PlainScalar("-7"),
            MappingEnd,
            DocumentEnd(Implicit),
            StreamEnd);
    }

    private static IParser ParserFor(string name)
    {
        return Parser.CreateParser(YamlFile(name));
    }

    private static void AssertSequenceOfEventsFrom(IParser parser, params ParsingEvent[] events)
    {
        var eventNumber = 1;
        foreach (var expected in events)
        {
            Assert.True(parser.MoveNext(), $"Missing parse event number {eventNumber.ToString(CultureInfo.InvariantCulture)}");
            Assert.NotNull(parser.Current, $"Missing parse event number {eventNumber.ToString(CultureInfo.InvariantCulture)}");
            AssertEvent(expected, parser.Current, eventNumber);
            eventNumber++;
        }
        Assert.False(parser.MoveNext(), "Found extra parse events");
    }

    private static void AssertEvent(ParsingEvent expected, ParsingEvent actual, int eventNumber)
    {
        Assert.Equal(expected.GetType(), actual.GetType(), $"Parse event {eventNumber.ToString(CultureInfo.InvariantCulture)} is not of the expected type.");

        foreach (var property in expected.GetType().GetProperties())
        {
            if (property.PropertyType == typeof(Mark) || !property.CanRead)
            {
                continue;
            }

            var value = property.GetValue(actual, null);
            var expectedValue = property.GetValue(expected, null);
            if (expectedValue is IEnumerable enumerable && !(expectedValue is string))
            {
                Dump.Write("\t{0} = {{", property.Name);
                Dump.Write(string.Join(", ", (IEnumerable?)value));
                Dump.WriteLine("}");

                if (expectedValue is ICollection expectedCollection && value is ICollection collection)
                {
                    var expectedCount = expectedCollection.Count;
                    var valueCount = collection.Count;
                    Assert.Equal(expectedCount, valueCount, $"Compared size of collections in property {property.Name} in parse event {eventNumber.ToString(CultureInfo.InvariantCulture)}");
                }

                var values = ((IEnumerable)value!).GetEnumerator();
                var expectedValues = enumerable.GetEnumerator();
                while (expectedValues.MoveNext())
                {
                    Assert.True(values.MoveNext(), $"Property {property.Name} in parse event {eventNumber.ToString(CultureInfo.InvariantCulture)} had too few elements");
                    Assert.Equal(expectedValues.Current, values.Current, $"Compared element in property {property.Name} in parse event {eventNumber.ToString(CultureInfo.InvariantCulture)}");
                }
                Assert.False(values.MoveNext(), $"Property {property.Name} in parse event {eventNumber.ToString(CultureInfo.InvariantCulture)} had too many elements");
            }
            else
            {
                Dump.WriteLine("\t{0} = {1}", property.Name, value);
                Assert.Equal(expectedValue, value, $"Compared property {property.Name} in parse event {eventNumber.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }
}


