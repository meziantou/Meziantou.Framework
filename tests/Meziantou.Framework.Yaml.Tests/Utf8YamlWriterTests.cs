using System.Buffers;

namespace Meziantou.Framework.Yaml.Tests;

public sealed class Utf8YamlWriterTests
{
    [Fact]
    public void ReadmeExample_Write()
    {
        #region Utf8YamlWriter_Write
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8YamlWriter(buffer))
        {
            writer.WriteStartMapping();
            writer.WritePropertyName("name");
            writer.WriteStringValue("web");
            writer.WritePropertyName("replicas");
            writer.WriteNumberValue(3);
            writer.WritePropertyName("version");
            writer.WriteStringValue("1.0"); // quoted: plain 1.0 would be read as a number
            writer.WritePropertyName("ports");
            writer.WriteStartSequence(YamlStyle.Flow);
            writer.WriteNumberValue(80);
            writer.WriteNumberValue(443);
            writer.WriteEndSequence();
            writer.WritePropertyName("command");
            writer.WriteStringValue("echo hello\necho world\n", ScalarStyle.Literal);
            writer.WriteEndMapping();
        }

        Assert.Equal("""
            name: web
            replicas: 3
            version: '1.0'
            ports: [80, 443]
            command: |
              echo hello
              echo world

            """, Encoding.UTF8.GetString(buffer.WrittenSpan));
        #endregion
    }

    [Fact]
    public void ReadmeExample_WriteTo()
    {
        #region Utf8YamlWriter_WriteTo
        using var document = YamlDocument.Parse("""
            name: web # a comment is not a node: it is not written
            settings: {replicas: 3, image: "nginx:1.27"}
            """);

        using var stream = new MemoryStream();
        using (var writer = new Utf8YamlWriter(stream))
        {
            document.Root["settings"].WriteTo(writer);
        }

        Assert.Equal("{replicas: 3, image: \"nginx:1.27\"}\n", Encoding.UTF8.GetString(stream.ToArray()));
        #endregion
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("C# 14", "C# 14")]
    [InlineData("12:30", "12:30")]
    [InlineData("it's", "it's")]
    [InlineData("-1x", "-1x")]
    [InlineData("é😀", "é😀")]
    [InlineData("", "''")]
    [InlineData("123", "'123'")]
    [InlineData("1.5", "'1.5'")]
    [InlineData("true", "'true'")]
    [InlineData("yes", "'yes'")]
    [InlineData("null", "'null'")]
    [InlineData("~", "'~'")]
    [InlineData(".inf", "'.inf'")]
    [InlineData("a: b", "'a: b'")]
    [InlineData("a #b", "'a #b'")]
    [InlineData("#a", "'#a'")]
    [InlineData("- a", "'- a'")]
    [InlineData("-", "'-'")]
    [InlineData("&a", "'&a'")]
    [InlineData("*a", "'*a'")]
    [InlineData("!a", "'!a'")]
    [InlineData("%a", "'%a'")]
    [InlineData("@a", "'@a'")]
    [InlineData("[a]", "'[a]'")]
    [InlineData(" a", "' a'")]
    [InlineData("a ", "'a '")]
    [InlineData("a\tb", "'a\tb'")]
    [InlineData("'a'", "'''a'''")]
    [InlineData("--- a", "'--- a'")]
    [InlineData("a\nb", "\"a\\nb\"")]
    [InlineData("a\u0001b", "\"a\\x01b\"")]
    [InlineData("a\"b\\c", "a\"b\\c")]
    [InlineData("\"a", "'\"a'")]
    [InlineData("\u0085", "\"\\N\"")]
    [InlineData("\u2028", "\"\\L\"")]
    [InlineData("\uFEFF", "\"\\uFEFF\"")]
    public void WriteStringValue_AnyStyle_WritesTheSimplestStyleThatReadsBackAsTheString(string value, string expected)
    {
        var yaml = Write(writer => writer.WriteStringValue(value));

        Assert.Equal(expected + "\n", yaml);
        Assert.Equal(value, ReadSingleScalar(yaml, out var style));
        if (style is ScalarStyle.Plain)
        {
            Assert.False(Meziantou.Framework.Yaml.Serialization.YamlScalar.ResolvesToNonString(value, YamlSchemaKind.Core));
        }
    }

    [Theory]
    [InlineData("a,b", "'a,b'")]
    [InlineData("a]", "'a]'")]
    [InlineData("a?b", "'a?b'")]
    [InlineData("a:b", "a:b")]
    [InlineData("a b", "a b")]
    public void WriteStringValue_InFlowSequence_QuotesTheFlowIndicators(string value, string expected)
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartSequence(YamlStyle.Flow);
            writer.WriteStringValue(value);
            writer.WriteStringValue("x");
            writer.WriteEndSequence();
        });

        Assert.Equal("[" + expected + ", x]\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(value, document.Root[0].GetString());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("~")]
    public void WriteStringValue_PlainStyle_KeepsThePlainStyle(string value)
    {
        var yaml = Write(writer => writer.WriteStringValue(value, ScalarStyle.Plain));

        Assert.Equal(value + "\n", yaml);
    }

    [Fact]
    public void WriteStringValue_PlainStyleThatIsNotPossible_QuotesAString()
    {
        var yaml = Write(writer => writer.WriteStringValue("a: b", ScalarStyle.Plain));

        Assert.Equal("'a: b'\n", yaml);
    }

    [Fact]
    public void WriteStringValue_PlainStyleOfADocumentMarker_StartsTheDocumentWithAMarker()
    {
        var yaml = Write(writer => writer.WriteStringValue("--- a", ScalarStyle.Plain));

        Assert.Equal("--- --- a\n", yaml);
        Assert.Equal("--- a", ReadSingleScalar(yaml, out var style));
        Assert.Equal(ScalarStyle.Plain, style);
    }

    [Theory]
    [InlineData("a\nb\n", "|\n  a\n  b\n")]
    [InlineData("a\nb", "|-\n  a\n  b\n")]
    [InlineData("a\n\n", "|+\n  a\n\n")]
    [InlineData("a\n\nb\n", "|\n  a\n\n  b\n")]
    [InlineData("a\n  b\n", "|\n  a\n    b\n")]
    [InlineData(" a\n", "\" a\\n\"")]
    [InlineData("a\r\nb", "\"a\\r\\nb\"")]
    [InlineData("a\n \n", "\"a\\n \\n\"")]
    [InlineData("\n", "\"\\n\"")]
    public void WriteStringValue_LiteralStyle_WritesABlockScalarWhenItIsPossible(string value, string expected)
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartMapping();
            writer.WritePropertyName("key");
            writer.WriteStringValue(value, ScalarStyle.Literal);
            writer.WriteEndMapping();
        });

        Assert.Equal("key: " + expected + (expected is [.., '\n'] ? "" : "\n"), yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(value, document.Root["key"].GetString());
    }

    [Fact]
    public void Write_NestedBlockCollections_UsesTheCompactNotationInSequences()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartSequence();
            writer.WriteStartMapping();
            writer.WritePropertyName("a");
            writer.WriteNumberValue(1);
            writer.WritePropertyName("b");
            writer.WriteStartSequence();
            writer.WriteStringValue("x");
            writer.WriteStartSequence();
            writer.WriteStringValue("p");
            writer.WriteStringValue("z");
            writer.WriteEndSequence();
            writer.WriteEndSequence();
            writer.WriteEndMapping();
            writer.WriteStartMapping();
            writer.WriteEndMapping();
            writer.WriteStartSequence();
            writer.WriteEndSequence();
            writer.WriteEndSequence();
        });

        Assert.Equal("""
            - a: 1
              b:
                - x
                - - p
                  - z
            - {}
            - []

            """, yaml);
    }

    [Fact]
    public void Write_IndentSize_IndentsTheBlockCollections()
    {
        var yaml = Write(new Utf8YamlWriterOptions { IndentSize = 4 }, writer =>
        {
            writer.WriteStartMapping();
            writer.WritePropertyName("a");
            writer.WriteStartMapping();
            writer.WritePropertyName("b");
            writer.WriteStartSequence();
            writer.WriteStringValue("c");
            writer.WriteEndSequence();
            writer.WriteEndMapping();
            writer.WriteEndMapping();
        });

        Assert.Equal("a:\n    b:\n        - c\n", yaml);
    }

    [Fact]
    public void Write_FlowCollections_AreWrittenOnOneLine()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartMapping(YamlStyle.Flow);
            writer.WritePropertyName("a");
            writer.WriteStartSequence(YamlStyle.Block);
            writer.WriteNumberValue(1);
            writer.WriteStartMapping();
            writer.WriteEndMapping();
            writer.WriteEndSequence();
            writer.WritePropertyName("b");
            writer.WriteStringValue("", ScalarStyle.Plain);
            writer.WriteEndMapping();
        });

        Assert.Equal("{a: [1, {}], b:}\n", yaml);
    }

    [Fact]
    public void Write_AnchorsTagsAndAliases()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartMapping();
            writer.WritePropertyName("a");
            writer.WriteAnchor("x");
            writer.WriteTag("tag:yaml.org,2002:str");
            writer.WriteStringValue("123", ScalarStyle.Plain);
            writer.WritePropertyName("b");
            writer.WriteAliasValue("x");
            writer.WriteAliasValue("x");
            writer.WriteStringValue("alias key");
            writer.WritePropertyName("c");
            writer.WriteAnchor("m");
            writer.WriteTag("!local");
            writer.WriteStartMapping();
            writer.WritePropertyName("d");
            writer.WriteTag("tag:example.com,2000:a b");
            writer.WriteStringValue("", ScalarStyle.Plain);
            writer.WriteEndMapping();
            writer.WritePropertyName("e");
            writer.WriteTag("!");
            writer.WriteStringValue("1", ScalarStyle.Plain);
            writer.WriteEndMapping();
        });

        Assert.Equal("""
            a: &x !!str 123
            b: *x
            *x : alias key
            c: &m !local
              d: !<tag:example.com,2000:a%20b>
            e: ! 1

            """, yaml);

        using var document = YamlDocument.Parse(yaml);
        var root = document.Root;
        Assert.Equal("tag:yaml.org,2002:str", root["a"].Tag);
        Assert.Equal("x", root["a"].Anchor);
        Assert.Equal("123", root["b"].ResolveAlias().GetString());
        Assert.Equal("!local", root["c"].Tag);
        Assert.Equal("tag:example.com,2000:a b", root["c"]["d"].Tag);
        Assert.Equal("!", root["e"].Tag);
    }

    [Fact]
    public void ReadmeExample_TagDirectivesAndComments()
    {
        #region Utf8YamlWriter_TagDirectivesAndComments
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8YamlWriter(buffer))
        {
            writer.WriteTagDirective("!e!", "tag:example.com,2000:");
            writer.WriteComment("Generated file: do not edit");
            writer.WriteStartMapping();
            writer.WritePropertyName("origin");
            writer.WriteTag("tag:example.com,2000:point");
            writer.WriteStartMapping(YamlStyle.Flow);
            writer.WritePropertyName("left");
            writer.WriteNumberValue(0);
            writer.WritePropertyName("top");
            writer.WriteNumberValue(0);
            writer.WriteEndMapping();
            writer.WriteComment("The servers,\nin the order of preference");
            writer.WritePropertyName("servers");
            writer.WriteStartSequence();
            writer.WriteComment("none yet");
            writer.WriteEndSequence();
            writer.WriteEndMapping();
        }

        Assert.Equal("""
            %TAG !e! tag:example.com,2000:
            # Generated file: do not edit
            ---
            origin: !e!point {left: 0, top: 0}
            # The servers,
            # in the order of preference
            servers:
              # none yet
              []

            """, Encoding.UTF8.GetString(buffer.WrittenSpan));
        #endregion

        using var document = YamlDocument.Parse(buffer.WrittenMemory);
        Assert.Equal("tag:example.com,2000:point", document.Root["origin"].Tag);
        Assert.Equal(0, document.Root["servers"].GetLength());
    }

    [Fact]
    public void WriteTagDirective_AppliesToTheNextDocument()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStringValue("a");

            // A tag that is neither a local tag nor a URI needs a directive
            writer.WriteTagDirective("!x!", "y-");
            writer.WriteTag("y-light");
            writer.WriteStringValue("green");
            writer.WriteStartDocument();
            Assert.Throws<ArgumentException>(() => writer.WriteTag("y-light"));
            writer.WriteNullValue();
        });

        Assert.Equal("a\n...\n%TAG !x! y-\n--- !x!light green\n--- null\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal("y-light", document.GetDocumentRoot(1).Tag);
    }

    [Fact]
    public void WriteTagDirective_RedeclaredPrimaryHandle_WritesLocalTagsVerbatim()
    {
        var yaml = Write(writer =>
        {
            writer.WriteTagDirective("!", "tag:example.com,2000:");
            writer.WriteTagDirective("!e!", "{a%");
            writer.WriteStartSequence();
            writer.WriteTag("!local");
            writer.WriteStringValue("a");
            writer.WriteTag("tag:example.com,2000:x");
            writer.WriteStringValue("b");
            writer.WriteTag("{a%b!");
            writer.WriteStringValue("c");
            writer.WriteTag("tag:yaml.org,2002:str");
            writer.WriteStringValue("d");
            writer.WriteEndSequence();
        });

        Assert.Equal("%TAG ! tag:example.com,2000:\n%TAG !e! %7Ba%25\n---\n- !<!local> a\n- !x b\n- !e!b%21 c\n- !!str d\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(["!local", "tag:example.com,2000:x", "{a%b!", "tag:yaml.org,2002:str"], document.Root.EnumerateSequence().Select(node => node.Tag));
    }

    [Fact]
    public void WriteTagDirective_InvalidCalls_Throw()
    {
        using var writer = new Utf8YamlWriter(new ArrayBufferWriter<byte>());
        Assert.Throws<ArgumentException>(() => writer.WriteTagDirective("e", "tag:e:"));
        Assert.Throws<ArgumentException>(() => writer.WriteTagDirective("!e e!", "tag:e:"));
        writer.WriteTagDirective("!e_1!", "tag:e1:");
        Assert.Throws<ArgumentException>(() => writer.WriteTagDirective("!e!", ""));
        writer.WriteTagDirective("!e!", "tag:e:");
        Assert.Throws<InvalidOperationException>(() => writer.WriteTagDirective("!e!", "tag:f:"));
        writer.WriteStartDocument();
        Assert.Throws<InvalidOperationException>(() => writer.WriteTagDirective("!f!", "tag:f:"));
        writer.WriteStartSequence();
        Assert.Throws<InvalidOperationException>(() => writer.WriteTagDirective("!f!", "tag:f:"));
    }

    [Fact]
    public void WriteComment_IsWrittenBetweenTheEntriesOfBlockCollections()
    {
        var yaml = Write(writer =>
        {
            writer.WriteComment("first\r\n\nthird");
            writer.WriteStartSequence();
            writer.WriteStartMapping();
            writer.WriteComment("before the first key");
            writer.WritePropertyName("a");
            writer.WriteNumberValue(1);
            writer.WriteEndMapping();
            writer.WriteComment("between items");
            writer.WriteStartSequence();
            writer.WriteComment("in an empty sequence");
            writer.WriteEndSequence();
            writer.WriteEndSequence();
            writer.WriteComment("after the root");
            writer.WriteStartDocument();
            writer.WriteComment("after the marker");
            writer.WriteStringValue("--- a", ScalarStyle.Plain);
        });

        Assert.Equal("""
            # first
            #
            # third
            -
              # before the first key
              a: 1
            # between items
            -
              # in an empty sequence
              []
            # after the root
            ---
            # after the marker
             --- a

            """, yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(1, document.Root[0]["a"].TryGetInt64(out var a) ? a : 0);
        Assert.Equal(0, document.Root[1].GetLength());
        Assert.Equal("--- a", document.GetDocumentRoot(1).GetString());
    }

    [Fact]
    public void WriteComment_InvalidCalls_Throw()
    {
        using var writer = new Utf8YamlWriter(new ArrayBufferWriter<byte>());
        Assert.Throws<ArgumentException>(() => writer.WriteComment("a\u0001b"));
        Assert.Throws<ArgumentException>(() => writer.WriteComment("a\u2028b"));
        writer.WriteStartMapping();
        writer.WritePropertyName("a");
        Assert.Throws<InvalidOperationException>(() => writer.WriteComment("between a key and its value"));
        writer.WriteStartSequence(YamlStyle.Flow);
        Assert.Throws<InvalidOperationException>(() => writer.WriteComment("in a flow collection"));
        writer.WriteEndSequence();
        writer.WriteAnchor("x");
        Assert.Throws<InvalidOperationException>(() => writer.WriteComment("after an anchor"));
    }

    [Fact]
    public void WriteTo_WritesTheTagDirectivesOfTheDocuments()
    {
        var yaml = "%TAG !e! tag:example.com,2000:\n--- !e!point {x: 1}\n...\n%TAG !m! y-\n--- !m!light green\n--- !local x\n";
        using var document = YamlDocument.Parse(yaml);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8YamlWriter(buffer))
        {
            document.WriteTo(writer);
        }

        Assert.Equal(yaml, Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Theory]
    [InlineData("key: value\nlist:\n  - a b\n  - c:d, e\n", "key: value\nlist:\n  - a b\n  - c:d, e\n")]
    [InlineData(" --- a :: b\n", "'--- a :': b\n")]
    public void WriteTo_CopiesThePlainScalarsThatKeepTheirMeaning(string yaml, string expected)
    {
        using var document = YamlDocument.Parse(yaml);
        var written = Write(document.WriteTo);

        Assert.Equal(expected, written);
    }

    [Fact]
    public void WriteTo_PlainScalarOfABlockMappingInAFlowSequence()
    {
        using var document = YamlDocument.Parse("k: c:d, e\n");
        var written = Write(writer =>
        {
            writer.WriteStartSequence(YamlStyle.Flow);
            document.Root["k"].WriteTo(writer);
            writer.WriteEndSequence();
        });

        Assert.Equal("['c:d, e']\n", written);
    }

    [Fact]
    public void WriteAnchor_AcceptsTheCharactersOfYaml12()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartSequence();
            writer.WriteAnchor("a:\u2028é");
            writer.WriteStringValue("value");
            writer.WriteAliasValue("a:\u2028é");
            writer.WriteEndSequence();
        });

        Assert.Equal("- &a:\u2028é value\n- *a:\u2028é\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal("value", document.Root[1].ResolveAlias().GetString());
    }

    [Fact]
    public void Write_CollectionAndLongKeys_AreExplicitKeys()
    {
        var longKey = new string('k', 1200);
        var yaml = Write(writer =>
        {
            writer.WriteStartMapping();
            writer.WriteStartSequence();
            writer.WriteStringValue("a");
            writer.WriteStringValue("b");
            writer.WriteEndSequence();
            writer.WriteStringValue("v1");
            writer.WritePropertyName(longKey);
            writer.WriteStringValue("v2");
            writer.WriteStringValue("", ScalarStyle.Plain);
            writer.WriteStartMapping();
            writer.WritePropertyName("x");
            writer.WriteStringValue("q");
            writer.WriteEndMapping();
            writer.WriteEndMapping();
        });

        Assert.Equal("? - a\n  - b\n: v1\n? " + longKey + "\n: v2\n?\n: x: q\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        var entries = document.Root.EnumerateMapping().ToList();
        Assert.Equal(YamlElementKind.Sequence, entries[0].Key.Kind);
        Assert.Equal(longKey, entries[1].Key.GetString());
        Assert.True(entries[2].Key.IsNull);
        Assert.Equal("q", entries[2].Value["x"].GetString());
    }

    [Fact]
    public void Write_Documents()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStringValue("a");
            writer.WriteStartDocument();
            writer.WriteStartMapping();
            writer.WritePropertyName("b");
            writer.WriteNumberValue(1);
            writer.WriteEndMapping();
            writer.WriteEndDocument();
            writer.WriteStringValue("", ScalarStyle.Plain);
        });

        // An empty document needs its marker
        Assert.Equal("a\n---\nb: 1\n...\n---\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(3, document.DocumentCount);
        Assert.True(document.GetDocumentRoot(2).IsNull);
    }

    [Fact]
    public void WriteNumberValue_WritesNumbersThatReadBackWithTheirType()
    {
        var yaml = Write(writer =>
        {
            writer.WriteStartSequence(YamlStyle.Flow);
            writer.WriteNumberValue(-42);
            writer.WriteNumberValue(ulong.MaxValue);
            writer.WriteNumberValue(5.0);
            writer.WriteNumberValue(0.1);
            writer.WriteNumberValue(1e300);
            writer.WriteNumberValue(-0.0);
            writer.WriteNumberValue(double.NaN);
            writer.WriteNumberValue(double.PositiveInfinity);
            writer.WriteNumberValue(float.NegativeInfinity);
            writer.WriteNumberValue(2.5f);
            writer.WriteNumberValue(12.50m);
            writer.WriteBooleanValue(true);
            writer.WriteNullValue();
            writer.WriteEndSequence();
        });

        Assert.Equal("[-42, 18446744073709551615, 5.0, 0.1, 1E+300, -0.0, .nan, .inf, -.inf, 2.5, 12.50, true, null]\n", yaml);
        using var document = YamlDocument.Parse(yaml);
        var values = document.Root.EnumerateSequence().Take(11).Select(node => node.TryGetDouble(out var value) ? value : throw new InvalidOperationException(node.GetString())).ToArray();
        Assert.Equal([-42.0, 1.8446744073709552E+19, 5.0, 0.1, 1e300, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2.5, 12.5], values);
        Assert.Equal(ScalarStyle.Plain, document.Root[11].ScalarStyle);
        Assert.True(document.Root[12].IsNull);
    }

    [Fact]
    public void Write_InvalidSequenceOfCalls_Throws()
    {
        using var writer = new Utf8YamlWriter(new ArrayBufferWriter<byte>());
        Assert.Throws<InvalidOperationException>(() => writer.WritePropertyName("a"));
        Assert.Throws<InvalidOperationException>(() => writer.WriteEndMapping());
        writer.WriteStartSequence();
        Assert.Throws<InvalidOperationException>(() => writer.WriteEndMapping());
        Assert.Throws<InvalidOperationException>(() => writer.WritePropertyName("a"));
        Assert.Throws<InvalidOperationException>(() => writer.WriteStartDocument());
        writer.WriteAnchor("a");
        Assert.Throws<InvalidOperationException>(() => writer.WriteAnchor("b"));
        Assert.Throws<InvalidOperationException>(() => writer.WriteAliasValue("b"));
        Assert.Throws<InvalidOperationException>(() => writer.WriteEndSequence());
        writer.WriteNullValue();
        writer.WriteEndSequence();
        Assert.Throws<InvalidOperationException>(() => writer.WriteStringValue("second root"));
        Assert.Throws<ArgumentException>(() => writer.WriteAnchor("a b"));
        Assert.Throws<ArgumentException>(() => writer.WriteAnchor("a,b"));
        Assert.Throws<ArgumentException>(() => writer.WriteTag(""));

        // A %TAG directive can resolve a tag to a text that is neither a local tag nor a URI
        Assert.Throws<ArgumentException>(() => writer.WriteTag("y-light"));
        Assert.Throws<ArgumentException>(() => writer.WriteStringValue("\uD800"));
        Assert.Throws<ArgumentException>(() => writer.WriteStringValue(new byte[] { 0xFF }));
    }

    [Fact]
    public void Write_KeyWithoutValueOrEmptyPlainInFlowSequence_Throws()
    {
        using var writer = new Utf8YamlWriter(new ArrayBufferWriter<byte>());
        writer.WriteStartMapping();
        writer.WritePropertyName("a");
        Assert.Throws<InvalidOperationException>(() => writer.WriteEndMapping());
        writer.WriteStartSequence(YamlStyle.Flow);
        Assert.Throws<ArgumentException>(() => writer.WriteStringValue("", ScalarStyle.Plain));
    }

    [Fact]
    public void Write_MaxDepth_Throws()
    {
        using var writer = new Utf8YamlWriter(new ArrayBufferWriter<byte>(), new Utf8YamlWriterOptions { MaxDepth = 2 });
        writer.WriteStartSequence();
        writer.WriteStartSequence();
        Assert.Throws<InvalidOperationException>(() => writer.WriteStartSequence());
    }

    [Fact]
    public void Write_Stream_KeepsTheBytesUntilFlush()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8YamlWriter(stream))
        {
            writer.WriteStartMapping();
            writer.WritePropertyName("a"u8);
            writer.WriteStringValue("b"u8);
            Assert.Equal(0, stream.Length);
            Assert.Equal(4, writer.BytesPending);
            writer.Flush();
            Assert.Equal(4, writer.BytesCommitted);
            Assert.Equal(0, writer.BytesPending);
            writer.WriteEndMapping();
        }

        Assert.Equal("a: b\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Theory]
    [MemberData(nameof(Utf8YamlReaderTests.ValidYamlTestSuiteCases), MemberType = typeof(Utf8YamlReaderTests))]
    public void YamlTestSuite_WriteTo_WritesTheSameNodes(string id)
    {
        using var source = YamlDocument.Parse(YamlTestSuite.Get(id).Yaml);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8YamlWriter(buffer))
        {
            source.WriteTo(writer);
        }

        var yaml = Encoding.UTF8.GetString(buffer.WrittenSpan);
        using var written = YamlDocument.Parse(buffer.WrittenMemory);
        Assert.Equal(ReadNodes(source), ReadNodes(written));

        // The output only uses the constructs that the scanner-based parser reads the same way
        var parserEvents = YamlTestSuite.ReadEvents(new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml)));
        Assert.Equal(parserEvents, Utf8YamlReaderTests.ReadEvents(buffer.WrittenSpan.ToArray()));
    }

    private static List<string> ReadNodes(YamlDocument document)
    {
        var events = new List<string>();
        for (var i = 0; i < document.DocumentCount; i++)
        {
            YamlDocumentTests.AddEvents(document.GetDocumentRoot(i), events, normalizeScalarStyles: true);
        }

        return events;
    }

    private static string ReadSingleScalar(string yaml, out ScalarStyle style)
    {
        using var document = YamlDocument.Parse(yaml);
        style = document.Root.ScalarStyle;
        return document.Root.GetString();
    }

    private static string Write(Action<Utf8YamlWriter> write) => Write(default, write);

    private static string Write(Utf8YamlWriterOptions options, Action<Utf8YamlWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8YamlWriter(buffer, options))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
